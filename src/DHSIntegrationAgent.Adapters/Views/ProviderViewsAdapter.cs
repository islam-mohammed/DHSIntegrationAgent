using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Providers;

namespace DHSIntegrationAgent.Adapters.Views;

/// <summary>
/// WBS 3.3: Single View Provider DB extraction adapter.
///
/// Fetches data from a single denormalized view and maps the flat rows into the nested
/// ProviderClaimBundleRaw structure in-memory.
/// </summary>
public sealed class ProviderViewsAdapter : IProviderViewsAdapter
{
    private readonly IProviderDbFactory _providerDbFactory;
    private readonly ISqliteUnitOfWorkFactory _uowFactory;

    private sealed record ProviderViewsConfig(
        string ViewSourceName,
        string ClaimKeyColumnName,
        string DateColumnName
    );

    private readonly ConcurrentDictionary<string, ProviderViewsConfig> _configCache = new();

    private const string DefaultViewName = "DHSClaimsDataView";
    private const string DefaultClaimKeyColumn = "ProIdClaim";
    private const string DefaultDateColumn = "TreatmentFromDate";

    private const int ProviderDbCommandTimeoutSeconds = 60;

    public ProviderViewsAdapter(
        IProviderDbFactory providerDbFactory,
        ISqliteUnitOfWorkFactory uowFactory)
    {
        _providerDbFactory = providerDbFactory;
        _uowFactory = uowFactory;
    }

    public async Task<int> CountClaimsAsync(string providerDhsCode, string companyCode, DateTimeOffset batchStartDateUtc, DateTimeOffset batchEndDateUtc, CancellationToken ct)
    {
        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT COUNT(DISTINCT {config.ClaimKeyColumnName})
FROM {config.ViewSourceName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate;";

        cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
        cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
        cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public Task<int> CountAttachmentsAsync(string providerDhsCode, string companyCode, DateTimeOffset batchStartDateUtc, DateTimeOffset batchEndDateUtc, CancellationToken ct)
    {
        // Views typically don't carry attachments natively via flat views, return 0 or implement custom logic if needed
        return Task.FromResult(0);
    }

    public async Task<FinancialSummary> GetFinancialSummaryAsync(string providerDhsCode, string companyCode, DateTimeOffset batchStartDateUtc, DateTimeOffset batchEndDateUtc, CancellationToken ct)
    {
        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT
    COUNT(DISTINCT {config.ClaimKeyColumnName}) AS TotalClaims,
    SUM(NetAmount) AS TotalNetAmount,
    SUM(LineClaimedAmount) AS TotalClaimedAmount,
    SUM(LineItemDiscount) AS TotalDiscount,
    SUM(CoInsurance) AS TotalDeductible
FROM {config.ViewSourceName}
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

    public async Task<IReadOnlyList<int>> ListClaimKeysAsync(string providerDhsCode, string companyCode, DateTimeOffset batchStartDateUtc, DateTimeOffset batchEndDateUtc, int pageSize, int? lastSeenClaimKey, CancellationToken ct)
    {
        if (pageSize <= 0) pageSize = 500;
        if (pageSize > 5000) pageSize = 5000;

        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        cmd.CommandText = $@"
SELECT DISTINCT TOP (@PageSize) {config.ClaimKeyColumnName}
FROM {config.ViewSourceName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate
  AND (@LastSeen IS NULL OR {config.ClaimKeyColumnName} > @LastSeen)
ORDER BY {config.ClaimKeyColumnName} ASC;";

        cmd.AddParameter("@PageSize", pageSize, DbType.Int32);
        cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
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

    public async Task<ProviderClaimBundleRaw?> GetClaimBundleRawAsync(string providerDhsCode, int proIdClaim, CancellationToken ct)
    {
        var result = await GetClaimBundlesRawBatchAsync(providerDhsCode, new[] { proIdClaim }, ct);
        return result.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ProviderClaimBundleRaw>> GetClaimBundlesRawBatchAsync(string providerDhsCode, IReadOnlyList<int> proIdClaims, CancellationToken ct)
    {
        if (proIdClaims.Count == 0) return Array.Empty<ProviderClaimBundleRaw>();

        var config = await ResolveConfigAsync(providerDhsCode, ct);

        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        using var cmd = handle.Connection.CreateCommand();
        cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

        var keyList = string.Join(",", proIdClaims);
        cmd.CommandText = $@"
SELECT *
FROM {config.ViewSourceName}
WHERE {config.ClaimKeyColumnName} IN ({keyList})
ORDER BY {config.ClaimKeyColumnName};";

        var claimsData = new Dictionary<int, List<JsonObject>>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = reader.ReadRowAsJsonObject();
            if (row.TryGetPropertyValue(config.ClaimKeyColumnName, out var val) && val != null)
            {
                if (int.TryParse(val.ToString(), out var id))
                {
                    if (!claimsData.TryGetValue(id, out var rows))
                    {
                        rows = new List<JsonObject>();
                        claimsData[id] = rows;
                    }
                    rows.Add(row);
                }
            }
        }

        var result = new List<ProviderClaimBundleRaw>(proIdClaims.Count);

        foreach (var id in proIdClaims)
        {
            if (!claimsData.TryGetValue(id, out var rows) || rows.Count == 0)
                continue;

            // Extract Header (take first row as header)
            var header = CloneObject(rows[0]);

            // Services
            var services = new JsonArray();
            foreach (var row in rows)
            {
                services.Add(CloneObject(row));
            }

            // Doctors (distinct by doctorcode)
            var doctors = new JsonArray();
            var seenDoctors = new HashSet<string>();
            foreach (var row in rows)
            {
                if (row.TryGetPropertyValue("DoctorCode", out var doctorCodeNode) && doctorCodeNode != null)
                {
                    var doctorCode = doctorCodeNode.ToString();
                    if (!seenDoctors.Contains(doctorCode))
                    {
                        seenDoctors.Add(doctorCode);
                        doctors.Add(CloneObject(row));
                    }
                }
            }

            // Diagnoses (distinct by diagnosiscode)
            var diagnoses = new JsonArray();
            var seenDiagnoses = new HashSet<string>();
            foreach (var row in rows)
            {
                if (row.TryGetPropertyValue("DiagnosisCode", out var diagCodeNode) && diagCodeNode != null)
                {
                    var diagCode = diagCodeNode.ToString();
                    if (!seenDiagnoses.Contains(diagCode))
                    {
                        seenDiagnoses.Add(diagCode);
                        diagnoses.Add(CloneObject(row));
                    }
                }
            }

            // Note: Since views usually do not separate Lab, Radiology, Optical, ItemDetails, Achi
            // we will let them be empty JSON arrays or map them if specific columns are present.
            // For now, aligning with the flat View extraction approach.

            result.Add(new ProviderClaimBundleRaw(
                ProIdClaim: id,
                Header: header,
                DhsDoctors: doctors.Count > 0 ? doctors : null,
                Services: services,
                Diagnoses: diagnoses,
                Labs: new JsonArray(),
                Radiology: new JsonArray(),
                Attachments: new JsonArray(),
                OpticalVitalSigns: new JsonArray(),
                ItemDetails: new JsonArray(),
                Achi: new JsonArray()
            ));
        }

        return result;
    }

    public Task<IReadOnlyList<JsonObject>> GetAttachmentsForBatchAsync(string providerDhsCode, string companyCode, DateTimeOffset batchStartDateUtc, DateTimeOffset batchEndDateUtc, CancellationToken ct)
    {
        // Views typically do not supply attachments natively
        return Task.FromResult<IReadOnlyList<JsonObject>>(Array.Empty<JsonObject>());
    }

    public async Task<IReadOnlyList<ScannedDomainValue>> GetDistinctDomainValuesAsync(string providerDhsCode, string companyCode, DateTimeOffset batchStartDateUtc, DateTimeOffset batchEndDateUtc, IReadOnlyList<BaselineDomain> domains, CancellationToken ct)
    {
        var config = await ResolveConfigAsync(providerDhsCode, ct);
        await using var handle = await _providerDbFactory.OpenAsync(providerDhsCode, ct);

        var results = new List<ScannedDomainValue>();

        foreach (var domain in domains)
        {
            var parts = domain.FieldPath.Split('.');
            if (parts.Length != 2) continue;

            var columnName = parts[1];

            using var cmd = handle.Connection.CreateCommand();
            cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

            cmd.CommandText = $@"
SELECT DISTINCT {columnName}
FROM {config.ViewSourceName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate
  AND {columnName} IS NOT NULL;";

            cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
            cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
            cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

            try
            {
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
            catch
            {
                // Ignore columns that might not exist in the view
            }
        }

        return results.Distinct().ToList();
    }

    private async Task<ProviderViewsConfig> ResolveConfigAsync(string providerDhsCode, CancellationToken ct)
    {
        if (_configCache.TryGetValue(providerDhsCode, out var cached))
            return cached;

        await using var uow = await _uowFactory.CreateAsync(ct);

        var profile = await uow.ProviderProfiles.GetActiveByProviderDhsCodeAsync(providerDhsCode, ct);

        var extraction = profile is not null
            ? await uow.ProviderExtractionConfigs.GetAsync(new ProviderKey(profile.ProviderCode), ct)
            : null;

        var config = new ProviderViewsConfig(
            ViewSourceName: string.IsNullOrWhiteSpace(extraction?.HeaderSourceName) ? DefaultViewName : extraction.HeaderSourceName,
            ClaimKeyColumnName: string.IsNullOrWhiteSpace(extraction?.ClaimKeyColumnName) ? DefaultClaimKeyColumn : extraction.ClaimKeyColumnName,
            DateColumnName: string.IsNullOrWhiteSpace(extraction?.DateColumnName) ? DefaultDateColumn : extraction.DateColumnName
        );

        _configCache.TryAdd(providerDhsCode, config);
        return config;
    }

    private static JsonObject CloneObject(JsonObject obj)
    {
        var json = obj.ToJsonString();
        return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }
}
