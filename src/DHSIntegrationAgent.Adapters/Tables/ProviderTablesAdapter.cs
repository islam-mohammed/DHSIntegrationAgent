using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Providers;
using System.Data;
using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Adapters.Tables;

/// <summary>
/// WBS 3.2: Table-to-table provider DB extraction adapter.
///
/// ProviderDhsCode is treated as STRING across the solution.
/// Provider DB schema (tableToTable) stores provider_dhsCode as INT; we compare using TRY_CONVERT(int, @ProviderDhsCode)
/// to keep queries index-friendly where possible.
/// </summary>
public sealed class ProviderTablesAdapter : IProviderTablesAdapter
{
    private readonly IProviderDbFactory _providerDbFactory;
    private readonly ISqliteUnitOfWorkFactory _uowFactory;

    private sealed record ProviderTablesConfig(
        string ClaimKeyColumnName,
        string DateColumnName,
        string HeaderSourceName,
        string DoctorSourceName,
        string ServiceSourceName,
        string DiagnosisSourceName,
        string LabSourceName,
        string RadiologySourceName,
        string AttachmentSourceName,
        string OpticalSourceName,
        string ItemDetailsSourceName,
        string AchiSourceName
    );

    // Cache extraction configuration to avoid redundant SQLite lookups (WBS 3.2 efficiency).
    private readonly ConcurrentDictionary<string, ProviderTablesConfig> _configCache = new();

    // Defaults derived from the provided tableToTable schema script.
    private const string DefaultHeaderTable = "DHSClaim_Header";
    private const string DefaultDoctorTable = "DHS_Doctor";
    private const string DefaultServiceTable = "DHSService_Details";
    private const string DefaultDiagnosisTable = "DHSDiagnosis_Details";
    private const string DefaultLabTable = "DHSLab_Details";
    private const string DefaultRadiologyTable = "DHSRadiology_Details";
    private const string DefaultAttachmentTable = "DHS_Attachment";
    private const string DefaultOpticalTable = "DHS_OpticalVitalSign";
    private const string DefaultItemDetailsTable = "DHS_ItemDetails";
    private const string DefaultAchiTable = "DHS_ACHI";

    // Date column choice (can be adjusted if you confirm different semantics):
    private const string DefaultHeaderDateColumn = "InvoiceDate";

    // Provider claim key column
    private const string DefaultClaimKeyColumn = "ProIdClaim";

    // Provider DB command timeout
    private const int ProviderDbCommandTimeoutSeconds = 60;

    public ProviderTablesAdapter(
        IProviderDbFactory providerDbFactory,
        ISqliteUnitOfWorkFactory uowFactory)
    {
        _providerDbFactory = providerDbFactory;
        _uowFactory = uowFactory;
    }

    public async Task<int> CountClaimsAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        CancellationToken ct)
    {
        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT COUNT(1)
FROM {config.HeaderSourceName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate;";

        cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
        cmd.AddParameter("@ProviderDhsCode", providerDhsCode, DbType.String, 50);
        cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
        cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<int> CountAttachmentsAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        CancellationToken ct)
    {
        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT COUNT(1)
FROM {config.AttachmentSourceName} a
INNER JOIN {config.HeaderSourceName} h ON a.{config.ClaimKeyColumnName} = h.{config.ClaimKeyColumnName}
WHERE h.CompanyCode = @CompanyCode
  AND h.{config.DateColumnName} >= @StartDate
  AND h.{config.DateColumnName} <= @EndDate;";

        cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
        cmd.AddParameter("@ProviderDhsCode", providerDhsCode, DbType.String, 50);
        cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
        cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task<FinancialSummary> GetFinancialSummaryAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        CancellationToken ct)
    {
        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT
    COUNT(1) AS TotalClaims,
    SUM(TotalNetAmount) AS TotalNetAmount,
    SUM(ClaimedAmount) AS TotalClaimedAmount,
    SUM(TotalDiscount) AS TotalDiscount,
    SUM(TotalDeductible) AS TotalDeductible
FROM {config.HeaderSourceName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate;";

        cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
        cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
        cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new FinancialSummary(
                TotalClaims: reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                TotalNetAmount: reader.IsDBNull(1) ? 0m : Convert.ToDecimal(reader.GetValue(1)),
                TotalClaimedAmount: reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2)),
                TotalDiscount: reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3)),
                TotalDeductible: reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4))
            );
        }

        return new FinancialSummary(0, 0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<int>> ListClaimKeysAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        int pageSize,
        int? lastSeenClaimKey,
        CancellationToken ct)
    {
        if (pageSize <= 0) pageSize = 500;
        if (pageSize > 5000) pageSize = 5000;

        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT TOP (@PageSize) {config.ClaimKeyColumnName}
FROM {config.HeaderSourceName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate
  AND (@LastSeen IS NULL OR {config.ClaimKeyColumnName} > @LastSeen)
ORDER BY {config.ClaimKeyColumnName} ASC;";

        cmd.AddParameter("@PageSize", pageSize, DbType.Int32);
        cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
        cmd.AddParameter("@ProviderDhsCode", providerDhsCode, DbType.String, 50);
        cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
        cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);
        cmd.AddParameter("@LastSeen", lastSeenClaimKey, DbType.Int32);

        var keys = new List<int>(capacity: Math.Min(pageSize, 1024));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            keys.Add(reader.GetInt32(0));
        }

        return keys;
    }

    public async Task<ProviderClaimBundleRaw?> GetClaimBundleRawAsync(
        string providerDhsCode,
        int proIdClaim,
        CancellationToken ct)
    {
        var result = await GetClaimBundlesRawBatchAsync(providerDhsCode, new[] { proIdClaim }, ct);
        return result.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ProviderClaimBundleRaw>> GetClaimBundlesRawBatchAsync(
        string providerDhsCode,
        IReadOnlyList<int> proIdClaims,
        CancellationToken ct)
    {
        if (proIdClaims.Count == 0) return Array.Empty<ProviderClaimBundleRaw>();

        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        // Fetch everything in batch queries.
        var headers = await FetchHeaderBatchAsync(handle.Connection, config.HeaderSourceName, config.ClaimKeyColumnName, providerDhsCode, proIdClaims, ct);
        var doctors = await FetchSingleRowBatchAsync(handle.Connection, config.DoctorSourceName, config.ClaimKeyColumnName, proIdClaims, ct);
        var services = await FetchManyRowsBatchAsync(handle.Connection, config.ServiceSourceName, config.ClaimKeyColumnName, proIdClaims, ct);
        var diagnoses = await FetchManyRowsBatchAsync(handle.Connection, config.DiagnosisSourceName, config.ClaimKeyColumnName, proIdClaims, ct);
        var labs = await FetchManyRowsBatchAsync(handle.Connection, config.LabSourceName, config.ClaimKeyColumnName, proIdClaims, ct);
        var radiology = await FetchManyRowsBatchAsync(handle.Connection, config.RadiologySourceName, config.ClaimKeyColumnName, proIdClaims, ct);
        var attachments = await FetchManyRowsBatchAsync(handle.Connection, config.AttachmentSourceName, config.ClaimKeyColumnName, proIdClaims, ct);
        var optical = await FetchManyRowsBatchAsync(handle.Connection, config.OpticalSourceName, config.ClaimKeyColumnName, proIdClaims, ct);
        var itemDetails = await FetchManyRowsBatchAsync(handle.Connection, config.ItemDetailsSourceName, config.ClaimKeyColumnName, proIdClaims, ct);
        var achi = await FetchManyRowsBatchAsync(handle.Connection, config.AchiSourceName, config.ClaimKeyColumnName, proIdClaims, ct);

        var result = new List<ProviderClaimBundleRaw>(proIdClaims.Count);
        foreach (var id in proIdClaims)
        {
            if (!headers.TryGetValue(id, out var header))
                continue;

            var doctorObj = doctors.GetValueOrDefault(id);
            var doctorArr = doctorObj != null ? new JsonArray { CloneObject(doctorObj) } : new JsonArray();

            result.Add(new ProviderClaimBundleRaw(
                ProIdClaim: id,
                Header: header,
                DhsDoctors: doctorArr,
                Services: services.GetValueOrDefault(id, new JsonArray()),
                Diagnoses: diagnoses.GetValueOrDefault(id, new JsonArray()),
                Labs: labs.GetValueOrDefault(id, new JsonArray()),
                Radiology: radiology.GetValueOrDefault(id, new JsonArray()),
                Attachments: attachments.GetValueOrDefault(id, new JsonArray()),
                OpticalVitalSigns: optical.GetValueOrDefault(id, new JsonArray()),
                ItemDetails: itemDetails.GetValueOrDefault(id, new JsonArray()),
                Achi: achi.GetValueOrDefault(id, new JsonArray())
            ));
        }

        return result;
    }

    private static JsonObject CloneObject(JsonObject obj)
    {
        var json = obj.ToJsonString();
        return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }

    public async Task<IReadOnlyList<ScannedDomainValue>> GetDistinctDomainValuesAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        IReadOnlyList<BaselineDomain> domains,
        CancellationToken ct)
    {
        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        var results = new List<ScannedDomainValue>();

        foreach (var domain in domains)
        {
            string tableName;
            string columnName;
            bool isHeader;

            if (domain.FieldPath.StartsWith("claimHeader."))
            {
                tableName = config.HeaderSourceName;
                columnName = domain.FieldPath.Substring("claimHeader.".Length);
                isHeader = true;
            }
            else if (domain.FieldPath.StartsWith("serviceDetails."))
            {
                tableName = config.ServiceSourceName;
                columnName = domain.FieldPath.Substring("serviceDetails.".Length);
                isHeader = false;
            }
            else if (domain.FieldPath.StartsWith("diagnosisDetails."))
            {
                tableName = config.DiagnosisSourceName;
                columnName = domain.FieldPath.Substring("diagnosisDetails.".Length);
                isHeader = false;
            }
            else if (domain.FieldPath.StartsWith("dhsDoctors."))
            {
                tableName = config.DoctorSourceName;
                columnName = domain.FieldPath.Substring("dhsDoctors.".Length);
                isHeader = false;
            }
            else
            {
                continue;
            }

            using var cmd = handle.Connection.CreateCommand();
            cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

            if (isHeader)
            {
                cmd.CommandText = $@"
SELECT DISTINCT {columnName}
FROM {tableName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate
  AND {columnName} IS NOT NULL;";
            }
            else
            {
                cmd.CommandText = $@"
SELECT DISTINCT t.{columnName}
FROM {tableName} t
INNER JOIN {config.HeaderSourceName} h ON t.{config.ClaimKeyColumnName} = h.{config.ClaimKeyColumnName}
WHERE h.CompanyCode = @CompanyCode
  AND h.{config.DateColumnName} >= @StartDate
  AND h.{config.DateColumnName} <= @EndDate
  AND t.{columnName} IS NOT NULL;";
            }

            cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
            cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
            cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                {
                    var val = reader.GetValue(0).ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        results.Add(new ScannedDomainValue(domain.DomainName, domain.DomainTableId, val.Trim()));
                    }
                }
            }
        }

        return results.Distinct().ToList();
    }

    public async Task<IReadOnlyList<JsonObject>> GetAttachmentsForBatchAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        CancellationToken ct)
    {
        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT a.*
FROM {config.AttachmentSourceName} a
INNER JOIN {config.HeaderSourceName} h ON a.{config.ClaimKeyColumnName} = h.{config.ClaimKeyColumnName}
WHERE h.CompanyCode = @CompanyCode
  AND h.{config.DateColumnName} >= @StartDate
  AND h.{config.DateColumnName} <= @EndDate;";

        cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
        cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
        cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

        var results = new List<JsonObject>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.ReadRowAsJsonObject());
        }
        return results;
    }

    private async Task<ProviderTablesConfig> ResolveConfigAsync(string providerDhsCode, CancellationToken ct)
    {
        if (_configCache.TryGetValue(providerDhsCode, out var cached))
            return cached;

        await using var uow = await _uowFactory.CreateAsync(ct);

        var profile = await uow.ProviderProfiles.GetActiveByProviderDhsCodeAsync(providerDhsCode, ct);

        var extraction = profile is not null
            ? await uow.ProviderExtractionConfigs.GetAsync(new ProviderKey(profile.ProviderCode), ct)
            : null;

        var config = new ProviderTablesConfig(
            ClaimKeyColumnName: string.IsNullOrWhiteSpace(extraction?.ClaimKeyColumnName) ? DefaultClaimKeyColumn : extraction.ClaimKeyColumnName,
            DateColumnName: string.IsNullOrWhiteSpace(extraction?.DateColumnName) ? DefaultHeaderDateColumn : extraction.DateColumnName,
            HeaderSourceName: string.IsNullOrWhiteSpace(extraction?.HeaderSourceName) ? DefaultHeaderTable : extraction.HeaderSourceName,
            DoctorSourceName: string.IsNullOrWhiteSpace(extraction?.DoctorSourceName) ? DefaultDoctorTable : extraction.DoctorSourceName,
            ServiceSourceName: string.IsNullOrWhiteSpace(extraction?.ServiceSourceName) ? DefaultServiceTable : extraction.ServiceSourceName,
            DiagnosisSourceName: string.IsNullOrWhiteSpace(extraction?.DiagnosisSourceName) ? DefaultDiagnosisTable : extraction.DiagnosisSourceName,
            LabSourceName: string.IsNullOrWhiteSpace(extraction?.LabSourceName) ? DefaultLabTable : extraction.LabSourceName,
            RadiologySourceName: string.IsNullOrWhiteSpace(extraction?.RadiologySourceName) ? DefaultRadiologyTable : extraction.RadiologySourceName,
            AttachmentSourceName: string.IsNullOrWhiteSpace(extraction?.AttachmentSourceName) ? DefaultAttachmentTable : extraction.AttachmentSourceName,
            OpticalSourceName: string.IsNullOrWhiteSpace(extraction?.OpticalSourceName) ? DefaultOpticalTable : extraction.OpticalSourceName,
            ItemDetailsSourceName: string.IsNullOrWhiteSpace(extraction?.ItemDetailsSourceName) ? DefaultItemDetailsTable : extraction.ItemDetailsSourceName,
            AchiSourceName: string.IsNullOrWhiteSpace(extraction?.AchiSourceName) ? DefaultAchiTable : extraction.AchiSourceName
        );

        _configCache.TryAdd(providerDhsCode, config);
        return config;
    }

    private static async Task<JsonObject?> ReadSingleRowAsync(
        DbConnection conn,
        string sql,
        string providerDhsCode,
        int claimKey,
        CancellationToken ct,
        bool includeProviderFilter)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;
        cmd.CommandText = sql;

        cmd.AddParameter("@ClaimKey", claimKey, DbType.Int32);

        if (includeProviderFilter)
        {
            cmd.AddParameter("@ProviderDhsCode", providerDhsCode, DbType.String, 50);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return reader.ReadRowAsJsonObject();
    }

    private static async Task<JsonArray> ReadManyRowsAsync(
        DbConnection conn,
        string sql,
        string providerDhsCode,
        int claimKey,
        CancellationToken ct,
        bool includeProviderFilter)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;
        cmd.CommandText = sql;

        cmd.AddParameter("@ClaimKey", claimKey, DbType.Int32);

        if (includeProviderFilter)
        {
            cmd.AddParameter("@ProviderDhsCode", providerDhsCode, DbType.String, 50);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAllRowsAsJsonArrayAsync(ct);
    }

    private static async Task<Dictionary<int, JsonObject>> FetchHeaderBatchAsync(
        DbConnection conn,
        string tableName,
        string keyCol,
        string providerDhsCode,
        IReadOnlyList<int> keys,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        var keyList = string.Join(",", keys);
        cmd.CommandText = $@"
SELECT *
FROM {tableName}
WHERE {keyCol} IN ({keyList})";

        // Preserve original behavior of adding ProviderDhsCode parameter even if not explicitly in SQL string
        cmd.AddParameter("@ProviderDhsCode", providerDhsCode, DbType.String, 50);

        var result = new Dictionary<int, JsonObject>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = reader.ReadRowAsJsonObject();
            if (row.TryGetPropertyValue(keyCol, out var val) && val != null)
            {
                if (int.TryParse(val.ToString(), out var id))
                    result[id] = row;
            }
        }
        return result;
    }

    private static async Task<Dictionary<int, JsonObject>> FetchSingleRowBatchAsync(
        DbConnection conn,
        string tableName,
        string keyCol,
        IReadOnlyList<int> keys,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        var keyList = string.Join(",", keys);
        cmd.CommandText = $@"
WITH CTE AS (
    SELECT *, ROW_NUMBER() OVER(PARTITION BY {keyCol} ORDER BY (SELECT NULL)) as rn
    FROM {tableName}
    WHERE {keyCol} IN ({keyList})
)
SELECT * FROM CTE WHERE rn = 1";

        var result = new Dictionary<int, JsonObject>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = reader.ReadRowAsJsonObject();
            row.Remove("rn");
            if (row.TryGetPropertyValue(keyCol, out var val) && val != null)
            {
                if (int.TryParse(val.ToString(), out var id))
                    result[id] = row;
            }
        }
        return result;
    }

    private static async Task<Dictionary<int, JsonArray>> FetchManyRowsBatchAsync(
        DbConnection conn,
        string tableName,
        string keyCol,
        IReadOnlyList<int> keys,
        CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        var keyList = string.Join(",", keys);
        cmd.CommandText = $@"
SELECT *
FROM {tableName}
WHERE {keyCol} IN ({keyList})";

        var result = new Dictionary<int, JsonArray>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = reader.ReadRowAsJsonObject();
            if (row.TryGetPropertyValue(keyCol, out var val) && val != null)
            {
                if (int.TryParse(val.ToString(), out var id))
                {
                    if (!result.TryGetValue(id, out var arr))
                    {
                        arr = new JsonArray();
                        result[id] = arr;
                    }
                    arr.Add(row);
                }
            }
        }
        return result;
    }
}
