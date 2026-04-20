using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Sync.Mapper;
using DHSIntegrationAgent.Sync.Rules;
using DHSIntegrationAgent.Sync.Sql;
using DHSIntegrationAgent.Sync.Validation;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Sync.Pipeline;

public sealed class ClaimExtractionPipeline : IClaimExtractionPipeline
{
    private const int CommandTimeoutSeconds = 60;

    // Session-scoped validation cache (static so it survives scoped pipeline re-creation).
    private static readonly ConcurrentDictionary<string, bool> _validatedProviders = new(StringComparer.OrdinalIgnoreCase);

    private readonly DescriptorResolver _resolver;
    private readonly ISqlDialectFactory _dialectFactory;
    private readonly DynamicSqlBuilder _sqlBuilder;
    private readonly TypeCoercionMap _typeMap;
    private readonly SchemaValidator _schemaValidator;
    private readonly PostLoadRuleEngine _ruleEngine;
    private readonly IProviderDbFactory _providerDbFactory;
    private readonly ILogger<ClaimExtractionPipeline> _logger;

    public ClaimExtractionPipeline(
        DescriptorResolver resolver,
        ISqlDialectFactory dialectFactory,
        DynamicSqlBuilder sqlBuilder,
        TypeCoercionMap typeMap,
        SchemaValidator schemaValidator,
        PostLoadRuleEngine ruleEngine,
        IProviderDbFactory providerDbFactory,
        ILogger<ClaimExtractionPipeline> logger)
    {
        _resolver = resolver;
        _dialectFactory = dialectFactory;
        _sqlBuilder = sqlBuilder;
        _typeMap = typeMap;
        _schemaValidator = schemaValidator;
        _ruleEngine = ruleEngine;
        _providerDbFactory = providerDbFactory;
        _logger = logger;
    }

    public async Task ValidateSchemaAsync(string providerDhsCode, CancellationToken ct)
    {
        if (_validatedProviders.ContainsKey(providerDhsCode)) return;

        var descriptor = await _resolver.ResolveAsync(providerDhsCode, ct);
        var dialect    = _dialectFactory.ForEngine(descriptor.DbEngine);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        var result = await _schemaValidator.ValidateAsync(handle.Connection, descriptor, dialect, ct);

        foreach (var issue in result.Issues)
            _logger.Log(
                issue.Severity == "Error" ? LogLevel.Error : LogLevel.Warning,
                "[{Gate}] entity={Entity} col={Column}: {Message}",
                issue.Gate, issue.Entity, issue.Column, issue.Message);

        if (!result.IsValid)
        {
            var errors = string.Join("; ", result.Issues.Where(i => i.Severity == "Error").Select(i => i.Message));
            throw new InvalidOperationException($"Schema validation failed for '{providerDhsCode}': {errors}");
        }

        _validatedProviders[providerDhsCode] = true;
    }

    public async Task<int> CountClaimsAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct)
    {
        var descriptor   = await _resolver.ResolveAsync(providerDhsCode, ct);
        var dialect      = _dialectFactory.ForEngine(descriptor.DbEngine);
        var headerSource = RequireSource(descriptor, CanonicalSchema.Header);
        var dateCol      = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.DateColumn);
        var compCol      = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.CompanyCodeColumn);

        var sql = _sqlBuilder.BuildCountQuery(headerSource, dateCol, compCol, dialect);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);
        using var cmd = BuildCommand(handle.Connection, sql, CommandTimeoutSeconds);
        AddParam(cmd, dialect.Param("CompanyCode"), companyCode, DbType.String);
        AddParam(cmd, dialect.Param("StartDate"), start.UtcDateTime, DbType.DateTime2);
        AddParam(cmd, dialect.Param("EndDate"), end.UtcDateTime, DbType.DateTime2);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<ClaimPage> GetNextPageAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset start,
        DateTimeOffset end,
        int pageSize,
        ResumeCursor? cursor,
        CancellationToken ct)
    {
        if (pageSize <= 0) pageSize = 100;

        var descriptor   = await _resolver.ResolveAsync(providerDhsCode, ct);
        var dialect      = _dialectFactory.ForEngine(descriptor.DbEngine);
        var headerSource = RequireSource(descriptor, CanonicalSchema.Header);
        var keyCol       = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.ClaimKeyColumn);
        var dateCol      = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.DateColumn);
        var compCol      = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.CompanyCodeColumn);

        var sql = _sqlBuilder.BuildPagedHeaderQuery(headerSource, keyCol, dateCol, compCol, dialect);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);
        using var cmd = BuildCommand(handle.Connection, sql, CommandTimeoutSeconds);
        AddParam(cmd, dialect.Param("PageSize"), pageSize, DbType.Int32);
        AddParam(cmd, dialect.Param("CompanyCode"), companyCode, DbType.String);
        AddParam(cmd, dialect.Param("StartDate"), start.UtcDateTime, DbType.DateTime2);
        AddParam(cmd, dialect.Param("EndDate"), end.UtcDateTime, DbType.DateTime2);
        // Nullable cursor: when null ADO.NET sends DBNull → @LastSeen IS NULL evaluates true.
        AddParam(cmd, dialect.Param("LastSeen"),
            cursor is not null ? (object)cursor.AsInt() : null,
            DbType.Int32);

        var keys = new List<int>(pageSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            keys.Add(reader.GetInt32(0));

        bool isLast      = keys.Count < pageSize;
        ResumeCursor? next = (!isLast && keys.Count > 0) ? ResumeCursor.FromInt(keys[^1]) : null;
        return new ClaimPage(keys, next, isLast);
    }

    public async Task<IReadOnlyList<ProviderClaimBundleRaw>> FetchBundlesAsync(
        string providerDhsCode,
        IReadOnlyList<int> claimKeys,
        CancellationToken ct)
    {
        if (claimKeys.Count == 0) return Array.Empty<ProviderClaimBundleRaw>();

        var descriptor = await _resolver.ResolveAsync(providerDhsCode, ct);
        var dialect    = _dialectFactory.ForEngine(descriptor.DbEngine);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);
        var conn = handle.Connection;

        var isDistinctDoctor = descriptor.DistinctEntities?.Contains(CanonicalSchema.Doctor, StringComparer.OrdinalIgnoreCase) ?? false;

        var headers     = await FetchHeadersAsync(conn, descriptor, claimKeys, dialect, ct);
        var doctors     = await FetchManyAsync(conn, descriptor, CanonicalSchema.Doctor, claimKeys, dialect, isDistinctDoctor, false, ct);
        var services    = await FetchManyAsync(conn, descriptor, CanonicalSchema.Service, claimKeys, dialect, false, true, ct);
        var diagnoses   = await FetchManyAsync(conn, descriptor, CanonicalSchema.Diagnosis, claimKeys, dialect, false, false, ct);
        var labs        = await FetchManyAsync(conn, descriptor, CanonicalSchema.Lab, claimKeys, dialect, false, false, ct);
        var radiology   = await FetchManyAsync(conn, descriptor, CanonicalSchema.Radiology, claimKeys, dialect, false, false, ct);
        var attachments = await FetchManyAsync(conn, descriptor, CanonicalSchema.Attachment, claimKeys, dialect, false, false, ct);

        var bundles = new List<ProviderClaimBundleRaw>(claimKeys.Count);
        foreach (var id in claimKeys)
        {
            if (!headers.TryGetValue(id, out var header)) continue;

            var raw = new ProviderClaimBundleRaw(
                ProIdClaim: id,
                Header: header,
                DhsDoctors: doctors.GetValueOrDefault(id, new JsonArray()),
                Services: services.GetValueOrDefault(id, new JsonArray()),
                Diagnoses: diagnoses.GetValueOrDefault(id, new JsonArray()),
                Labs: labs.GetValueOrDefault(id, new JsonArray()),
                Radiology: radiology.GetValueOrDefault(id, new JsonArray()),
                Attachments: attachments.GetValueOrDefault(id, new JsonArray()),
                OpticalVitalSigns: new JsonArray(),
                ItemDetails: new JsonArray(),
                Achi: new JsonArray());

            if (descriptor.PostLoadRules.Count > 0)
                _ruleEngine.Apply(raw, descriptor.PostLoadRules);

            bundles.Add(raw);
        }

        return bundles;
    }

    public async Task<IReadOnlyList<ScannedDomainValue>> GetDistinctDomainValuesAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<BaselineDomain> domains,
        CancellationToken ct)
    {
        var descriptor = await _resolver.ResolveAsync(providerDhsCode, ct);
        var dialect    = _dialectFactory.ForEngine(descriptor.DbEngine);
        var headerSource = RequireSource(descriptor, CanonicalSchema.Header);
        var keyCol     = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.ClaimKeyColumn);
        var dateCol    = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.DateColumn);
        var compCol    = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.CompanyCodeColumn);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);
        var results = new List<ScannedDomainValue>();

        foreach (var domain in domains)
        {
            var (entity, fieldName) = SplitFieldPath(domain.FieldPath);
            if (entity is null) continue;

            if (!descriptor.Sources.TryGetValue(entity, out var sourceName)) continue;
            if (!descriptor.ColumnManifests.TryGetValue(entity, out var manifestSection)) continue;

            var manifest = ColumnManifest.FromDescriptor(manifestSection);
            var sourceColumnName = manifest.GetSourceColumn(fieldName) ?? fieldName;
            bool isHeader = string.Equals(entity, CanonicalSchema.Header, StringComparison.OrdinalIgnoreCase);

            try
            {
                using var cmd = BuildCommand(handle.Connection, "", CommandTimeoutSeconds);

                if (isHeader)
                {
                    cmd.CommandText = $"""
                        SELECT DISTINCT {dialect.Quote(sourceColumnName)}
                        FROM {dialect.Quote(sourceName)}
                        WHERE {dialect.Quote(compCol)} = {dialect.Param("CompanyCode")}
                          AND {dialect.Quote(dateCol)} >= {dialect.Param("StartDate")}
                          AND {dialect.Quote(dateCol)} <= {dialect.Param("EndDate")}
                          AND {dialect.Quote(sourceColumnName)} IS NOT NULL
                        """;
                }
                else
                {
                    cmd.CommandText = $"""
                        SELECT DISTINCT t.{dialect.Quote(sourceColumnName)}
                        FROM {dialect.Quote(sourceName)} t
                        INNER JOIN {dialect.Quote(headerSource)} h ON t.{dialect.Quote(keyCol)} = h.{dialect.Quote(keyCol)}
                        WHERE h.{dialect.Quote(compCol)} = {dialect.Param("CompanyCode")}
                          AND h.{dialect.Quote(dateCol)} >= {dialect.Param("StartDate")}
                          AND h.{dialect.Quote(dateCol)} <= {dialect.Param("EndDate")}
                          AND t.{dialect.Quote(sourceColumnName)} IS NOT NULL
                        """;
                }

                AddParam(cmd, dialect.Param("CompanyCode"), companyCode, DbType.String);
                AddParam(cmd, dialect.Param("StartDate"), start.UtcDateTime, DbType.DateTime2);
                AddParam(cmd, dialect.Param("EndDate"), end.UtcDateTime, DbType.DateTime2);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (!reader.IsDBNull(0))
                    {
                        var val = reader.GetValue(0).ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(val))
                            results.Add(new ScannedDomainValue(domain.DomainName, domain.DomainTableId, val));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetDistinctDomainValuesAsync: failed for domain {Domain}", domain.DomainName);
            }
        }

        return results.Distinct().ToList();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<Dictionary<int, JsonObject>> FetchHeadersAsync(
        DbConnection conn,
        VendorDescriptor descriptor,
        IReadOnlyList<int> claimKeys,
        ISqlDialect dialect,
        CancellationToken ct)
    {
        if (!TryGetEntitySource(descriptor, CanonicalSchema.Header, out var source, out var manifest))
            return new Dictionary<int, JsonObject>();

        var keySource = ResolveSourceColumn(descriptor, CanonicalSchema.Header, descriptor.Filter.ClaimKeyColumn);
        var sql = _sqlBuilder.BuildEntityQuery(source, manifest, keySource, claimKeys, dialect);

        using var cmd = BuildCommand(conn, sql, CommandTimeoutSeconds);
        AddInClauseParams(cmd, claimKeys, dialect);

        var cm     = ColumnManifest.FromDescriptor(manifest);
        var result = new Dictionary<int, JsonObject>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = DescriptorDrivenReader.ReadRow(reader, cm, _typeMap, logger: _logger);
            if (TryGetClaimId(row, descriptor.Filter.ClaimKeyColumn, out var id))
                result[id] = row;
        }
        return result;
    }

    private async Task<Dictionary<int, JsonArray>> FetchManyAsync(
        DbConnection conn,
        VendorDescriptor descriptor,
        string entityName,
        IReadOnlyList<int> claimKeys,
        ISqlDialect dialect,
        bool distinct,
        bool isServiceEntity,
        CancellationToken ct)
    {
        if (!TryGetEntitySource(descriptor, entityName, out var source, out var manifest))
            return new Dictionary<int, JsonArray>();

        var keySource = ResolveSourceColumn(descriptor, entityName, descriptor.Filter.ClaimKeyColumn);
        var sql = _sqlBuilder.BuildEntityQuery(source, manifest, keySource, claimKeys, dialect, distinct);

        using var cmd = BuildCommand(conn, sql, CommandTimeoutSeconds);
        AddInClauseParams(cmd, claimKeys, dialect);

        var cm     = ColumnManifest.FromDescriptor(manifest);
        var result = new Dictionary<int, JsonArray>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = DescriptorDrivenReader.ReadRow(reader, cm, _typeMap, isServiceEntity, _logger);
            if (TryGetClaimId(row, descriptor.Filter.ClaimKeyColumn, out var id))
            {
                if (!result.TryGetValue(id, out var arr))
                {
                    arr = new JsonArray();
                    result[id] = arr;
                }
                arr.Add(row);
            }
        }
        return result;
    }

    private static bool TryGetEntitySource(
        VendorDescriptor descriptor,
        string entityName,
        out string source,
        out Dictionary<string, ColumnFieldDescriptor?> manifest)
    {
        source   = "";
        manifest = new Dictionary<string, ColumnFieldDescriptor?>();

        if (!descriptor.Sources.TryGetValue(entityName, out var s) || string.IsNullOrEmpty(s)) return false;
        if (!descriptor.ColumnManifests.TryGetValue(entityName, out var m) || m is null) return false;

        source   = s;
        manifest = m;
        return true;
    }

    private static bool TryGetClaimId(JsonObject row, string canonicalKeyName, out int id)
    {
        id = 0;
        if (!row.TryGetPropertyValue(canonicalKeyName, out var node) || node is null) return false;
        return int.TryParse(node.ToString(), out id);
    }

    private static string ResolveSourceColumn(VendorDescriptor descriptor, string entityName, string canonicalName)
    {
        if (descriptor.ColumnManifests.TryGetValue(entityName, out var m) &&
            m.TryGetValue(canonicalName, out var field) && field?.Source is not null)
            return field.Source;
        // Fallback: assume canonical == source (AFI convention).
        return canonicalName;
    }

    private static string RequireSource(VendorDescriptor descriptor, string entityName)
    {
        if (descriptor.Sources.TryGetValue(entityName, out var src) && !string.IsNullOrEmpty(src))
            return src;
        throw new InvalidOperationException($"Descriptor for '{descriptor.VendorId}' has no source defined for entity '{entityName}'.");
    }

    private static (string? entity, string field) SplitFieldPath(string fieldPath)
    {
        var idx = fieldPath.IndexOf('.');
        if (idx < 0) return (null, fieldPath);
        var prefix = fieldPath[..idx];
        var field  = fieldPath[(idx + 1)..];
        var entity = prefix switch
        {
            "claimHeader"      => CanonicalSchema.Header,
            "serviceDetails"   => CanonicalSchema.Service,
            "diagnosisDetails" => CanonicalSchema.Diagnosis,
            "dhsDoctors"       => CanonicalSchema.Doctor,
            "labDetails"       => CanonicalSchema.Lab,
            "radiologyDetails" => CanonicalSchema.Radiology,
            _ => null
        };
        return (entity, field);
    }

    private static DbCommand BuildCommand(DbConnection conn, string sql, int timeoutSeconds)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText    = sql;
        cmd.CommandTimeout = timeoutSeconds;
        return cmd;
    }

    private static void AddParam(DbCommand cmd, string name, object? value, DbType? dbType = null)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        if (dbType.HasValue) p.DbType = dbType.Value;
        cmd.Parameters.Add(p);
    }

    private static void AddInClauseParams(DbCommand cmd, IReadOnlyList<int> keys, ISqlDialect dialect)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = dialect.Param($"p{i}");
            p.Value = keys[i];
            p.DbType = DbType.Int32;
            cmd.Parameters.Add(p);
        }
    }
}
