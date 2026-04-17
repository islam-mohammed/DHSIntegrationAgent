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
    private readonly ISchemaMapperService _schemaMapperService;

    private sealed record ProviderTablesConfig(
        int ClaimSplitFollowupDays,
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
        string AchiSourceName,
        JsonObject? ParsedFieldMapping
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

        if (config.ClaimSplitFollowupDays <= 0)
        {
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
        else
        {
            // We must group by 14 days and return the FIRST raw ID of the group as the canonical ID.
            cmd.CommandText = $@"
SELECT *
FROM {config.HeaderSourceName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate
ORDER BY {config.DateColumnName} ASC;";

            cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
            cmd.AddParameter("@ProviderDhsCode", providerDhsCode, DbType.String, 50);
            cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
            cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

            var rawRows = new List<JsonObject>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct)) rawRows.Add(reader.ReadRowAsJsonObject());
            }

            var mappedRows = rawRows.Select(r => _schemaMapperService.MapRow("Header", r, config.ParsedFieldMapping))
                .OrderBy(r => r.TryGetPropertyValue("PatientNo", out var pNode) && pNode != null ? pNode.ToString() : "")
                .ThenBy(r => r.TryGetPropertyValue("DoctorCode", out var dNode) && dNode != null ? dNode.ToString() : "")
                .ThenBy(r =>
                {
                    JsonNode? dateNode = null;
                    if (!r.TryGetPropertyValue("TreatmentFromDate", out dateNode) || dateNode == null)
                    {
                        r.TryGetPropertyValue(config.DateColumnName, out dateNode);
                    }
                    return dateNode != null && DateTime.TryParse(dateNode.ToString(), out var dt) ? dt : DateTime.MinValue;
                })
                .ToList();

            var keys = new List<int>(capacity: Math.Min(pageSize, 1024));
            var seenKeys = new HashSet<int>();

            string prevPatientNo = "";
            string prevDoctorCode = "";
            DateTime prevDate = DateTime.MinValue;
            int currentSplitId = 0;

            foreach (var row in mappedRows)
            {
                JsonNode? idNode = null;
                if (!row.TryGetPropertyValue("ProIdClaim", out idNode) || idNode == null)
                {
                    row.TryGetPropertyValue(config.ClaimKeyColumnName, out idNode);
                }
                if (idNode == null || !int.TryParse(idNode.ToString(), out var rawId)) continue;

                string patientNo = "";
                if (row.TryGetPropertyValue("PatientNo", out var pNode) && pNode != null) patientNo = pNode.ToString();

                string doctorCode = "";
                if (row.TryGetPropertyValue("DoctorCode", out var dNode) && dNode != null) doctorCode = dNode.ToString();

                DateTime currDate = DateTime.MinValue;
                JsonNode? dateNode = null;
                if (!row.TryGetPropertyValue("TreatmentFromDate", out dateNode) || dateNode == null)
                {
                    row.TryGetPropertyValue(config.DateColumnName, out dateNode);
                }
                if (dateNode != null)
                {
                    DateTime.TryParse(dateNode.ToString(), out currDate);
                }

                bool isNewGroup = false;
                if (prevPatientNo == "")
                {
                    isNewGroup = true;
                }
                else if (patientNo != prevPatientNo || doctorCode != prevDoctorCode ||
                         (currDate != DateTime.MinValue && prevDate != DateTime.MinValue && (currDate - prevDate).TotalDays >= config.ClaimSplitFollowupDays))
                {
                    isNewGroup = true;
                }

                if (isNewGroup)
                {
                    currentSplitId = rawId; // The first real raw ID of this 14-day group acts as the canonical bundle ID.
                    prevPatientNo = patientNo;
                    prevDoctorCode = doctorCode;
                    prevDate = currDate;
                }

                seenKeys.Add(currentSplitId);
            }

            var allKeys = seenKeys.ToList();
            allKeys.Sort(); // Guarantee deterministic ordering for cursor pagination

            foreach (var k in allKeys)
            {
                if (lastSeenClaimKey == null || k > lastSeenClaimKey)
                {
                    keys.Add(k);
                    if (keys.Count >= pageSize) break;
                }
            }

            return keys;
        }
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

        if (config.ClaimSplitFollowupDays <= 0)
        {
            // Fetch everything in batch queries using standard IDs
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
                if (!headers.TryGetValue(id, out var rawHeader))
                    continue;

                var header = _schemaMapperService.MapRow("Header", rawHeader, config.ParsedFieldMapping);

                var rawDoctorObj = doctors.GetValueOrDefault(id);
                var doctorObj = rawDoctorObj != null ? _schemaMapperService.MapRow("Doctor", rawDoctorObj, config.ParsedFieldMapping) : null;

                var svcs = new JsonArray();
                if (services.TryGetValue(id, out var srcSvcs))
                    foreach (var s in srcSvcs) svcs.Add(_schemaMapperService.MapRow("Services", CloneObject(s.AsObject()), config.ParsedFieldMapping));

                var diags = new JsonArray();
                if (diagnoses.TryGetValue(id, out var srcDiags))
                    foreach (var d in srcDiags) diags.Add(_schemaMapperService.MapRow("Diagnosis", CloneObject(d.AsObject()), config.ParsedFieldMapping));

                var l = new JsonArray();
                if (labs.TryGetValue(id, out var srcLabs))
                    foreach (var lab in srcLabs) l.Add(_schemaMapperService.MapRow("Labs", CloneObject(lab.AsObject()), config.ParsedFieldMapping));

                var r = new JsonArray();
                if (radiology.TryGetValue(id, out var srcRads))
                    foreach (var rad in srcRads) r.Add(_schemaMapperService.MapRow("Radiology", CloneObject(rad.AsObject()), config.ParsedFieldMapping));

                var atts = new JsonArray();
                if (attachments.TryGetValue(id, out var srcAtts))
                    foreach (var a in srcAtts) atts.Add(_schemaMapperService.MapRow("Attachments", CloneObject(a.AsObject()), config.ParsedFieldMapping));

                var opts = new JsonArray();
                if (optical.TryGetValue(id, out var srcOpts))
                    foreach (var o in srcOpts) opts.Add(_schemaMapperService.MapRow("Optical", CloneObject(o.AsObject()), config.ParsedFieldMapping));

                var itms = new JsonArray();
                if (itemDetails.TryGetValue(id, out var srcItms))
                    foreach (var itm in srcItms) itms.Add(_schemaMapperService.MapRow("ItemDetails", CloneObject(itm.AsObject()), config.ParsedFieldMapping));

                var achis = new JsonArray();
                if (achi.TryGetValue(id, out var srcAchis))
                    foreach (var ac in srcAchis) achis.Add(_schemaMapperService.MapRow("Achi", CloneObject(ac.AsObject()), config.ParsedFieldMapping));

                var doctorArr = doctorObj != null ? new JsonArray { CloneObject(doctorObj) } : new JsonArray();

                result.Add(new ProviderClaimBundleRaw(
                    ProIdClaim: id,
                    Header: header,
                    DhsDoctors: doctorArr,
                    Services: svcs,
                    Diagnoses: diags,
                    Labs: l,
                    Radiology: r,
                    Attachments: atts,
                    OpticalVitalSigns: opts,
                    ItemDetails: itms,
                    Achi: achis
                ));
            }

            return result;
        }
        else
        {
            // Split mode: The `proIdClaims` requested are the real database IDs (which act as the canonical group IDs).
            // We need to fetch the raw headers for these exact IDs to determine their PatientNo/DoctorCode/Date,
            // and then discover any OTHER claims in the database that should be grouped into them based on the 14-day gap.
            // Since Tables mode requires querying by the specific vendor claim keys (to avoid full table scans),
            // and we don't know the related sibling keys without a broad scan, we fetch an expanded window of headers around the target IDs.

            using var cmd = handle.Connection.CreateCommand();
            cmd.CommandTimeout = ProviderDbCommandTimeoutSeconds;

            var keyList = string.Join(",", proIdClaims);

            // Fetch the anchors
            cmd.CommandText = $@"
SELECT * FROM {config.HeaderSourceName} WHERE {config.ClaimKeyColumnName} IN ({keyList});";
            var anchorRows = new List<JsonObject>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct)) anchorRows.Add(reader.ReadRowAsJsonObject());
            }

            if (anchorRows.Count == 0) return Array.Empty<ProviderClaimBundleRaw>();

            // Determine the surrounding window for the 14-day gap search based on the anchors
            var mappedAnchors = anchorRows.Select(r => _schemaMapperService.MapRow("Header", r, config.ParsedFieldMapping)).ToList();
            var minDate = DateTime.MaxValue;
            var maxDate = DateTime.MinValue;
            var patients = new HashSet<string>();
            var companies = new HashSet<string>();

            foreach (var a in mappedAnchors)
            {
                JsonNode? dateNode = null;
                if (!a.TryGetPropertyValue("TreatmentFromDate", out dateNode) || dateNode == null)
                {
                    a.TryGetPropertyValue(config.DateColumnName, out dateNode);
                }
                if (dateNode != null && DateTime.TryParse(dateNode.ToString(), out var dt))
                {
                    if (dt < minDate) minDate = dt;
                    if (dt > maxDate) maxDate = dt;
                }
                if (a.TryGetPropertyValue("PatientNo", out var pNode) && pNode != null) patients.Add(pNode.ToString());
                if (a.TryGetPropertyValue("CompanyCode", out var cNode) && cNode != null) companies.Add(cNode.ToString());
            }

            // Expand window to capture potential siblings
            if (minDate == DateTime.MaxValue) minDate = DateTime.UtcNow.AddDays(-14);
            if (maxDate == DateTime.MinValue) maxDate = DateTime.UtcNow;

            // Enforce CompanyCode filter to prevent cross-tenant data leakage if the staging table is shared
            var companyList = string.Join(",", companies.Select(c => $"'{c.Replace("'", "''")}'"));
            var patientList = string.Join(",", patients.Select(p => $"'{p.Replace("'", "''")}'"));

            cmd.CommandText = $@"
SELECT * FROM {config.HeaderSourceName}
WHERE {config.DateColumnName} >= @MinDate
  AND {config.DateColumnName} <= @MaxDate";

            if (companies.Count > 0)
            {
                var companyCol = "CompanyCode";
                if (config.ParsedFieldMapping != null && config.ParsedFieldMapping.TryGetPropertyValue("Header", out var hn) && hn is JsonObject ho)
                {
                    if (ho.TryGetPropertyValue("Fields", out var fn) && fn is JsonObject fo)
                    {
                        var match = fo.FirstOrDefault(k => string.Equals(k.Key, "CompanyCode", StringComparison.OrdinalIgnoreCase));
                        if (match.Value != null) companyCol = match.Value.ToString();
                    }
                }
                cmd.CommandText += $" AND {companyCol} IN ({companyList})";
            }

            // Filter to our anchors' exact PatientNos to ensure we don't load the entire provider's month of claims
            if (patients.Count > 0)
            {
                var patientCol = "PatientNo";
                if (config.ParsedFieldMapping != null && config.ParsedFieldMapping.TryGetPropertyValue("Header", out var hn) && hn is JsonObject ho)
                {
                    if (ho.TryGetPropertyValue("Fields", out var fn) && fn is JsonObject fo)
                    {
                        var match = fo.FirstOrDefault(k => string.Equals(k.Key, "PatientNo", StringComparison.OrdinalIgnoreCase));
                        if (match.Value != null) patientCol = match.Value.ToString();
                    }
                }
                cmd.CommandText += $" AND {patientCol} IN ({patientList})";
            }

            cmd.CommandText += " ORDER BY " + config.DateColumnName + " ASC;";
            cmd.AddParameter("@MinDate", minDate.AddDays(-config.ClaimSplitFollowupDays), DbType.DateTime2);
            cmd.AddParameter("@MaxDate", maxDate.AddDays(config.ClaimSplitFollowupDays), DbType.DateTime2);

            var siblingRows = new List<JsonObject>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct)) siblingRows.Add(reader.ReadRowAsJsonObject());
            }

            var allMapped = siblingRows.Select(r => _schemaMapperService.MapRow("Header", r, config.ParsedFieldMapping))
                .OrderBy(r => r.TryGetPropertyValue("PatientNo", out var pNode) && pNode != null ? pNode.ToString() : "")
                .ThenBy(r => r.TryGetPropertyValue("DoctorCode", out var dNode) && dNode != null ? dNode.ToString() : "")
                .ThenBy(r =>
                {
                    JsonNode? dateNode = null;
                    if (!r.TryGetPropertyValue("TreatmentFromDate", out dateNode) || dateNode == null)
                    {
                        r.TryGetPropertyValue(config.DateColumnName, out dateNode);
                    }
                    return dateNode != null && DateTime.TryParse(dateNode.ToString(), out var dt) ? dt : DateTime.MinValue;
                })
                .ToList();

            string prevPatientNo = "";
            string prevDoctorCode = "";
            DateTime prevDate = DateTime.MinValue;
            int currentSplitId = 0;

            var groups = new Dictionary<int, List<int>>();
            var headerMap = new Dictionary<int, JsonObject>();

            foreach (var row in allMapped)
            {
                JsonNode? idNode = null;
                if (!row.TryGetPropertyValue("ProIdClaim", out idNode) || idNode == null)
                {
                    row.TryGetPropertyValue(config.ClaimKeyColumnName, out idNode);
                }
                if (idNode == null || !int.TryParse(idNode.ToString(), out var rawId)) continue;
                headerMap[rawId] = row;

                string patientNo = "";
                if (row.TryGetPropertyValue("PatientNo", out var pNode) && pNode != null) patientNo = pNode.ToString();
                string doctorCode = "";
                if (row.TryGetPropertyValue("DoctorCode", out var dNode) && dNode != null) doctorCode = dNode.ToString();

                DateTime currDate = DateTime.MinValue;
                JsonNode? dateNode = null;
                if (!row.TryGetPropertyValue("TreatmentFromDate", out dateNode) || dateNode == null)
                {
                    row.TryGetPropertyValue(config.DateColumnName, out dateNode);
                }
                if (dateNode != null)
                    DateTime.TryParse(dateNode.ToString(), out currDate);

                bool isNewGroup = false;
                if (prevPatientNo == "") isNewGroup = true;
                else if (patientNo != prevPatientNo || doctorCode != prevDoctorCode ||
                         (currDate != DateTime.MinValue && prevDate != DateTime.MinValue && (currDate - prevDate).TotalDays >= config.ClaimSplitFollowupDays))
                    isNewGroup = true;

                if (isNewGroup)
                {
                    currentSplitId = rawId; // The first real ID of the group becomes the canonical group ID
                    prevPatientNo = patientNo;
                    prevDoctorCode = doctorCode;
                    prevDate = currDate;
                }

                if (!groups.TryGetValue(currentSplitId, out var groupKeys))
                {
                    groupKeys = new List<int>();
                    groups[currentSplitId] = groupKeys;
                }
                groupKeys.Add(rawId);
            }

            var targetGroupIds = new HashSet<int>(proIdClaims);
            var processedGroups = new HashSet<List<int>>();
            var allRequiredKeys = new HashSet<int>();

            // First pass: collect all underlying vendor keys across all requested groups
            var requiredGroups = new List<(int CanonicalId, List<int> VendorKeys)>();

            foreach (var targetId in targetGroupIds)
            {
                List<int>? originalKeys = null;
                int canonicalGroupId = 0;

                foreach (var groupKvp in groups)
                {
                    if (groupKvp.Value.Contains(targetId))
                    {
                        canonicalGroupId = groupKvp.Key;
                        originalKeys = groupKvp.Value;
                        break;
                    }
                }

                if (originalKeys == null || !processedGroups.Add(originalKeys)) continue;

                requiredGroups.Add((canonicalGroupId, originalKeys));
                foreach (var k in originalKeys) allRequiredKeys.Add(k);
            }

            if (allRequiredKeys.Count == 0) return Array.Empty<ProviderClaimBundleRaw>();

            var keysList = allRequiredKeys.ToList();

            // Execute batch fetches ONCE for all required keys to prevent N+1 queries.
            var doctors = await FetchSingleRowBatchAsync(handle.Connection, config.DoctorSourceName, config.ClaimKeyColumnName, keysList, ct);
            var services = await FetchManyRowsBatchAsync(handle.Connection, config.ServiceSourceName, config.ClaimKeyColumnName, keysList, ct);
            var diagnoses = await FetchManyRowsBatchAsync(handle.Connection, config.DiagnosisSourceName, config.ClaimKeyColumnName, keysList, ct);
            var labs = await FetchManyRowsBatchAsync(handle.Connection, config.LabSourceName, config.ClaimKeyColumnName, keysList, ct);
            var radiology = await FetchManyRowsBatchAsync(handle.Connection, config.RadiologySourceName, config.ClaimKeyColumnName, keysList, ct);
            var attachments = await FetchManyRowsBatchAsync(handle.Connection, config.AttachmentSourceName, config.ClaimKeyColumnName, keysList, ct);
            var optical = await FetchManyRowsBatchAsync(handle.Connection, config.OpticalSourceName, config.ClaimKeyColumnName, keysList, ct);
            var itemDetails = await FetchManyRowsBatchAsync(handle.Connection, config.ItemDetailsSourceName, config.ClaimKeyColumnName, keysList, ct);
            var achi = await FetchManyRowsBatchAsync(handle.Connection, config.AchiSourceName, config.ClaimKeyColumnName, keysList, ct);

            var result = new List<ProviderClaimBundleRaw>(requiredGroups.Count);

            foreach (var group in requiredGroups)
            {
                var canonicalGroupId = group.CanonicalId;
                var originalKeys = group.VendorKeys;

                // Use the first header of the group as the canonical header
                var header = headerMap[originalKeys.First()];
                var doctorObj = doctors.GetValueOrDefault(originalKeys.First());
                if (doctorObj != null) doctorObj = _schemaMapperService.MapRow("Doctor", doctorObj, config.ParsedFieldMapping);
                var doctorArr = doctorObj != null ? new JsonArray { CloneObject(doctorObj) } : new JsonArray();

                var svcs = new JsonArray();
                var diags = new JsonArray();
                var ls = new JsonArray();
                var rads = new JsonArray();
                var atts = new JsonArray();
                var opts = new JsonArray();
                var itms = new JsonArray();
                var achis = new JsonArray();

                foreach (var k in originalKeys)
                {
                    if (services.TryGetValue(k, out var s)) foreach (var x in s) { var mapped = _schemaMapperService.MapRow("Services", CloneObject(x.AsObject()), config.ParsedFieldMapping); svcs.Add(mapped); }
                    if (diagnoses.TryGetValue(k, out var d)) foreach (var x in d) { var mapped = _schemaMapperService.MapRow("Diagnosis", CloneObject(x.AsObject()), config.ParsedFieldMapping); diags.Add(mapped); }
                    if (labs.TryGetValue(k, out var l)) foreach (var x in l) { var mapped = _schemaMapperService.MapRow("Labs", CloneObject(x.AsObject()), config.ParsedFieldMapping); ls.Add(mapped); }
                    if (radiology.TryGetValue(k, out var r)) foreach (var x in r) { var mapped = _schemaMapperService.MapRow("Radiology", CloneObject(x.AsObject()), config.ParsedFieldMapping); rads.Add(mapped); }
                    if (attachments.TryGetValue(k, out var a)) foreach (var x in a) { var mapped = _schemaMapperService.MapRow("Attachments", CloneObject(x.AsObject()), config.ParsedFieldMapping); atts.Add(mapped); }
                    if (optical.TryGetValue(k, out var o)) foreach (var x in o) { var mapped = _schemaMapperService.MapRow("Optical", CloneObject(x.AsObject()), config.ParsedFieldMapping); opts.Add(mapped); }
                    if (itemDetails.TryGetValue(k, out var itm)) foreach (var x in itm) { var mapped = _schemaMapperService.MapRow("ItemDetails", CloneObject(x.AsObject()), config.ParsedFieldMapping); itms.Add(mapped); }
                    if (achi.TryGetValue(k, out var ac)) foreach (var x in ac) { var mapped = _schemaMapperService.MapRow("Achi", CloneObject(x.AsObject()), config.ParsedFieldMapping); achis.Add(mapped); }
                }

                result.Add(new ProviderClaimBundleRaw(
                    ProIdClaim: canonicalGroupId,
                    Header: header,
                    DhsDoctors: doctorArr,
                    Services: svcs,
                    Diagnoses: diags,
                    Labs: ls,
                    Radiology: rads,
                    Attachments: atts,
                    OpticalVitalSigns: opts,
                    ItemDetails: itms,
                    Achi: achis
                ));
            }

            return result;
        }
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

        JsonObject? parsedMapping = null;
        if (!string.IsNullOrWhiteSpace(extraction?.FieldMappingJson))
        {
            try { parsedMapping = System.Text.Json.Nodes.JsonNode.Parse(extraction.FieldMappingJson) as JsonObject; } catch { }
        }

        string ExtractSourceName(string category, string defaultSource, string? legacySource)
        {
            if (parsedMapping != null && parsedMapping.TryGetPropertyValue(category, out var catNode) && catNode is JsonObject catObj)
            {
                if (catObj.TryGetPropertyValue("SourceName", out var srcNode) && srcNode != null)
                {
                    var val = srcNode.ToString();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
            return string.IsNullOrWhiteSpace(legacySource) ? defaultSource : legacySource;
        }

        var config = new ProviderTablesConfig(
            ClaimSplitFollowupDays: profile?.ClaimSplitFollowupDays ?? 0,
            ClaimKeyColumnName: string.IsNullOrWhiteSpace(extraction?.ClaimKeyColumnName) ? DefaultClaimKeyColumn : extraction.ClaimKeyColumnName,
            DateColumnName: string.IsNullOrWhiteSpace(extraction?.DateColumnName) ? DefaultHeaderDateColumn : extraction.DateColumnName,
            HeaderSourceName: ExtractSourceName("Header", DefaultHeaderTable, extraction?.HeaderSourceName),
            DoctorSourceName: ExtractSourceName("Doctor", DefaultDoctorTable, extraction?.DoctorSourceName),
            ServiceSourceName: ExtractSourceName("Services", DefaultServiceTable, extraction?.ServiceSourceName),
            DiagnosisSourceName: ExtractSourceName("Diagnosis", DefaultDiagnosisTable, extraction?.DiagnosisSourceName),
            LabSourceName: ExtractSourceName("Labs", DefaultLabTable, extraction?.LabSourceName),
            RadiologySourceName: ExtractSourceName("Radiology", DefaultRadiologyTable, extraction?.RadiologySourceName),
            AttachmentSourceName: ExtractSourceName("Attachment", DefaultAttachmentTable, extraction?.AttachmentSourceName),
            OpticalSourceName: ExtractSourceName("Optical", DefaultOpticalTable, extraction?.OpticalSourceName),
            ItemDetailsSourceName: ExtractSourceName("ItemDetails", DefaultItemDetailsTable, extraction?.ItemDetailsSourceName),
            AchiSourceName: ExtractSourceName("Achi", DefaultAchiTable, extraction?.AchiSourceName),
            ParsedFieldMapping: parsedMapping
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
