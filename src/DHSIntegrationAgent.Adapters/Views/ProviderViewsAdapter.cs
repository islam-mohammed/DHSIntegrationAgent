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
    private readonly ISchemaMapperService _schemaMapperService;

    private sealed record ProviderViewsConfig(
        string ViewSourceName,
        string ClaimKeyColumnName,
        string DateColumnName,
        int ClaimSplitFollowupDays,
        string DefaultTreatmentCountryCode,
        string DefaultSubmissionReasonCode,
        string DefaultPriority,
        int DefaultErPbmDuration,
        string SfdaServiceTypeIdentifier,
        List<string> PayersToDropZeroAmountServices,
        JsonObject? ParsedFieldMapping
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

        // Since we explicitly group views by patientno, doctorcode, claimtype, and a 14-day gap in memory,
        // trying to accurately `COUNT` these distinct sub-groups purely in a flat SQL query
        // across arbitrary vendor databases is highly error prone.
        // For accurate count matching, we use the base query but group by the core tuple
        // to approximate the final claim volume closely (ignoring the complex 14-day date-gap).
        // If exact matching is required at creation time, the backend will still accept the staged claim count.
        cmd.CommandText = $@"
SELECT COUNT(*) FROM (
    SELECT DISTINCT patientno, doctorcode, claimtype
    FROM {config.ViewSourceName}
    WHERE CompanyCode = @CompanyCode
      AND {config.DateColumnName} >= @StartDate
      AND {config.DateColumnName} <= @EndDate
) sub;";

        cmd.AddParameter("@CompanyCode", companyCode, DbType.AnsiString, 50);
        cmd.AddParameter("@StartDate", batchStartDateUtc.UtcDateTime, DbType.DateTime2);
        cmd.AddParameter("@EndDate", batchEndDateUtc.UtcDateTime, DbType.DateTime2);

        try
        {
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result);
        }
        catch
        {
            // Fallback if the view lacks these specific columns
            cmd.CommandText = $@"
SELECT COUNT(DISTINCT {config.ClaimKeyColumnName})
FROM {config.ViewSourceName}
WHERE CompanyCode = @CompanyCode
  AND {config.DateColumnName} >= @StartDate
  AND {config.DateColumnName} <= @EndDate;";
            var fallback = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(fallback);
        }
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
    COUNT(DISTINCT patientno + doctorcode + claimtype) AS TotalClaims,
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

        try
        {
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
        }
        catch
        {
            // Fallback if specific columns are missing from the raw view
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

            await using var fallbackReader = await cmd.ExecuteReaderAsync(ct);
            if (await fallbackReader.ReadAsync(ct))
            {
                return new FinancialSummary(
                    TotalClaims: fallbackReader.IsDBNull(0) ? 0 : Convert.ToInt32(fallbackReader.GetValue(0)),
                    TotalNetAmount: fallbackReader.IsDBNull(1) ? 0m : Convert.ToDecimal(fallbackReader.GetValue(1)),
                    TotalClaimedAmount: fallbackReader.IsDBNull(2) ? 0m : Convert.ToDecimal(fallbackReader.GetValue(2)),
                    TotalDiscount: fallbackReader.IsDBNull(3) ? 0m : Convert.ToDecimal(fallbackReader.GetValue(3)),
                    TotalDeductible: fallbackReader.IsDBNull(4) ? 0m : Convert.ToDecimal(fallbackReader.GetValue(4))
                );
            }
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
            // Note: If ClaimKeyColumnName outputs alphanumeric strings (e.g. "INV-123") instead of numeric IDs,
            // this integer-parsing requirement will drop them entirely (returning 0 keys).
            // However, because ProIdClaim is expected by NPHIES (and this system) to behave as a numerical grouping ID,
            // and we strictly require passing `IReadOnlyList<int>` downstream, we only yield keys that cast to INT.
            // If the provider has a string view key, they must alias a numeric hash or unique INT column to it in their View.
            if (!reader.IsDBNull(0))
            {
                var val = reader.GetValue(0);
                if (val is int i)
                {
                    keys.Add(i);
                }
                else if (val is string s && int.TryParse(s, out var parsed))
                {
                    keys.Add(parsed);
                }
                else if (val is long l && l <= int.MaxValue && l >= int.MinValue)
                {
                    keys.Add((int)l);
                }
                else if (val is short sh)
                {
                    keys.Add(sh);
                }
                else if (val is byte b)
                {
                    keys.Add(b);
                }
            }
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
            var rawRow = reader.ReadRowAsJsonObject();
            var row = _schemaMapperService.MapRow("Header", rawRow, config.ParsedFieldMapping);
            row = _schemaMapperService.MapRow("Services", row, config.ParsedFieldMapping);
            row = _schemaMapperService.MapRow("Diagnosis", row, config.ParsedFieldMapping);
            row = _schemaMapperService.MapRow("Labs", row, config.ParsedFieldMapping);
            row = _schemaMapperService.MapRow("Radiology", row, config.ParsedFieldMapping);
            row = _schemaMapperService.MapRow("Attachments", row, config.ParsedFieldMapping);
            row = _schemaMapperService.MapRow("Optical", row, config.ParsedFieldMapping);
            row = _schemaMapperService.MapRow("ItemDetails", row, config.ParsedFieldMapping);
            row = _schemaMapperService.MapRow("Achi", row, config.ParsedFieldMapping);

            JsonNode? val = null;
            if (!row.TryGetPropertyValue("ProIdClaim", out val) || val == null)
            {
                row.TryGetPropertyValue(config.ClaimKeyColumnName, out val);
            }

            if (val != null)
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

            // Order rows by patientno, doctorcode, treatmentfromdate, claimtype for gap analysis
            var orderedRows = rows.OrderBy(r => GetStringCaseInsensitive(r, "patientno") ?? "")
                .ThenBy(r => GetStringCaseInsensitive(r, "doctorcode") ?? "")
                .ThenBy(r =>
                {
                    if (TryGetPropertyIgnoreCase(r, "treatmentfromdate", out var dNode) && dNode != null)
                    {
                        if (DateTime.TryParse(dNode.ToString(), out var d)) return d;
                    }
                    return DateTime.MinValue;
                })
                .ThenBy(r => GetStringCaseInsensitive(r, "claimtype") ?? "")
                .ToList();

            // In-memory 14-day splitting algorithm based on the SP
            var splitGroups = new List<List<JsonObject>>();
            var currentGroup = new List<JsonObject>();

            string prevPatientNo = "";
            string prevDoctorCode = "";
            string prevClaimType = "";
            DateTime prevDate = DateTime.MinValue;

            foreach (var row in orderedRows)
            {
                string patientNo = GetStringCaseInsensitive(row, "patientno") ?? "";
                string doctorCode = GetStringCaseInsensitive(row, "doctorcode") ?? "";
                string claimType = GetStringCaseInsensitive(row, "claimtype") ?? "";

                DateTime currDate = DateTime.MinValue;
                if (TryGetPropertyIgnoreCase(row, "treatmentfromdate", out var cdNode) && cdNode != null)
                    DateTime.TryParse(cdNode.ToString(), out currDate);

                bool isNewGroup = false;

                if (currentGroup.Count == 0)
                {
                    isNewGroup = true;
                }
                else
                {
                    if (patientNo != prevPatientNo ||
                        doctorCode != prevDoctorCode ||
                        claimType != prevClaimType ||
                        (currDate != DateTime.MinValue && prevDate != DateTime.MinValue && (currDate - prevDate).TotalDays >= config.ClaimSplitFollowupDays))
                    {
                        isNewGroup = true;
                    }
                }

                if (isNewGroup)
                {
                    if (currentGroup.Count > 0)
                    {
                        splitGroups.Add(currentGroup);
                    }
                    currentGroup = new List<JsonObject>();
                    prevPatientNo = patientNo;
                    prevDoctorCode = doctorCode;
                    prevClaimType = claimType;
                    prevDate = currDate;
                }
                else
                {
                    // To accurately mimic the SQL LAG() gap grouping, we MUST NOT update
                    // prevDate if we are NOT a new group. It should retain the date of the FIRST visit
                    // of the group to calculate the 14-day window properly (or alternatively update it if we want rolling gap).
                    // The SP does DATEDIFF(day, LAG(treatmentfromdate, 1, treatmentfromdate) OVER (ORDER BY TempID), treatmentfromdate) >= @Followup
                    // LAG(..., 1) means we actually *do* want a rolling gap, so we MUST update it.
                    prevDate = currDate;
                }

                currentGroup.Add(row);
            }

            if (currentGroup.Count > 0)
            {
                splitGroups.Add(currentGroup);
            }

            // Process each split group as a separate Claim
            int subId = 1;
            foreach (var groupRows in splitGroups)
            {
                // Extract Header (take first row as header)
                var header = CloneObject(groupRows[0]);

                // Fetch ProIdClaim directly from the view column.
                // If the view provides an explicit distinct ProIdClaim per row, use it.
                int mappedId = id;
                if (TryGetPropertyIgnoreCase(header, "ProIdClaim", out var proIdNode) && proIdNode != null)
                {
                    if (int.TryParse(proIdNode.ToString(), out var parsedId))
                        mappedId = parsedId;
                }

                // If this single overarching group was split into multiple claims AND the view didn't
                // give us a unique ProIdClaim for this split piece (i.e. it still equals the parent ID),
                // we must generate a deterministic pseudo-ID to prevent duplicate key violations downstream.
                // This mimics the SP's `Scope_Identity()` generation.
                if (splitGroups.Count > 1 && mappedId == id)
                {
                    int hash = HashCode.Combine(id, subId);
                    mappedId = hash == int.MinValue ? -int.MaxValue : -Math.Abs(hash);
                }
                subId++;

                // Apply Default Configs to Header
                SetPropertyIgnoreCase(header, "TreatmentCountryCode", config.DefaultTreatmentCountryCode);
                SetPropertyIgnoreCase(header, "Priority", config.DefaultPriority);

                // Variables to track for InvestigationResult update
                bool hasLabOrRad = false;

                var services = new JsonArray();
                foreach (var row in groupRows)
                {
                    // SP logic: drop services with 0 amount for specific payers
                    decimal lineClaimedAmount = 0m;
                    if (TryGetPropertyIgnoreCase(row, "lineclaimedamount", out var lcaNode) && lcaNode != null)
                        decimal.TryParse(lcaNode.ToString(), out lineClaimedAmount);

                    string rowCompanyCode = GetStringCaseInsensitive(row, "companycode") ?? "";
                    if (config.PayersToDropZeroAmountServices.Contains(rowCompanyCode) && lineClaimedAmount == 0m)
                    {
                        continue; // Skip this service
                    }

                    var srvObj = CloneObject(row);

                    // SP logic: PBMDuration = Default for ER/OTHERS
                    string serviceCategory = GetStringCaseInsensitive(srvObj, "servicegategory")?.ToUpperInvariant() ?? "";
                    if (serviceCategory == "ER" || serviceCategory == "OTHERS")
                    {
                        SetPropertyIgnoreCase(srvObj, "PBMDuration", config.DefaultErPbmDuration);
                    }

                    // SP logic: NumberOfIncidents = 1 if < 1
                    int incidents = 0;
                    if (TryGetPropertyIgnoreCase(srvObj, "numberofincidents", out var incNode) && incNode != null)
                        int.TryParse(incNode.ToString(), out incidents);

                    if (incidents < 1)
                    {
                        SetPropertyIgnoreCase(srvObj, "numberofincidents", 1);
                    }

                    // SP Logic: DiagnosisCode override for SFDA
                    string serviceType = GetStringCaseInsensitive(srvObj, "servicetype") ?? "";
                    if (!string.IsNullOrEmpty(config.SfdaServiceTypeIdentifier) && serviceType == config.SfdaServiceTypeIdentifier)
                    {
                        // In a flat view, the diagnosis code on the service line is whatever was joined.
                        // To match the SP, we ensure it's explicitly pulled from the view's diagnosis column.
                        SetPropertyIgnoreCase(srvObj, "DiagnosisCode", GetStringCaseInsensitive(srvObj, "diagnosis_code"));
                    }

                    // SP Logic: Default Submission Reason Code
                    SetPropertyIgnoreCase(srvObj, "submissionreasoncode", config.DefaultSubmissionReasonCode);

                    services.Add(srvObj);

                    // Check if we have Lab or Rad for InvestigationResult update
                    if (serviceCategory == "LAB" || serviceCategory == "X-RAY" || serviceCategory == "RADIOLOGY")
                    {
                        hasLabOrRad = true;
                    }
                }

                // Apply InvestigationResult to Header
                SetPropertyIgnoreCase(header, "InvestigationResult", hasLabOrRad ? "IRA" : "NA");

                // Doctors (distinct by doctorcode)
                var doctors = new JsonArray();
                var seenDoctors = new HashSet<string>();
                foreach (var row in groupRows)
                {
                    if (TryGetPropertyIgnoreCase(row, "DoctorCode", out var doctorCodeNode) && doctorCodeNode != null)
                    {
                        var doctorCode = doctorCodeNode.ToString();
                        if (!seenDoctors.Contains(doctorCode))
                        {
                            seenDoctors.Add(doctorCode);

                            // Map explicitly based on SP DHS_Doctor insertion logic
                            var docObj = new JsonObject();
                            docObj["scfhsnumber"] = GetStringCaseInsensitive(row, "SCFHSNumber");
                            docObj["doctorname"] = GetStringCaseInsensitive(row, "doctorname");
                            docObj["bentype"] = GetStringCaseInsensitive(row, "bentype");
                            docObj["benhead"] = GetStringCaseInsensitive(row, "benhead");
                            docObj["specialty"] = GetStringCaseInsensitive(row, "specialty");
                            docObj["doctorcode"] = doctorCode;

                            doctors.Add(docObj);
                        }
                    }
                }

                // Diagnoses (distinct by diagnosiscode)
                var diagnoses = new JsonArray();
                var seenDiagnoses = new HashSet<string>();
                foreach (var row in groupRows)
                {
                    if (TryGetPropertyIgnoreCase(row, "DiagnosisCode", out var diagCodeNode) && diagCodeNode != null ||
                        TryGetPropertyIgnoreCase(row, "diagnosis_code", out diagCodeNode) && diagCodeNode != null)
                    {
                        var diagCode = diagCodeNode.ToString();
                        if (!seenDiagnoses.Contains(diagCode))
                        {
                            seenDiagnoses.Add(diagCode);

                            // Map explicitly based on SP dhsdiagnosis_details insertion logic
                            var diagObj = new JsonObject();
                            diagObj["diagnosiscode"] = diagCode;
                            diagObj["diagnosisdesc"] = GetStringCaseInsensitive(row, "diagnosisdesc");
                            diagObj["DiagnosisDate"] = GetStringCaseInsensitive(row, "DiagnosisDate");
                            diagObj["OnsetConditionTypeID"] = GetStringCaseInsensitive(row, "OnsetConditionTypeID");
                            diagObj["DiagnosisType"] = GetStringCaseInsensitive(row, "DiagnosisType");

                            diagnoses.Add(diagObj);
                        }
                    }
                }

                // Labs (distinct by labprofile + labtestname)
                var labs = new JsonArray();
                var seenLabs = new HashSet<string>();
                foreach (var row in groupRows)
                {
                    if (TryGetPropertyIgnoreCase(row, "labtestname", out var labNameNode) && labNameNode != null &&
                        TryGetPropertyIgnoreCase(row, "labprofile", out var labProfileNode) && labProfileNode != null)
                    {
                        var labName = labNameNode.ToString();
                        var labProfile = labProfileNode.ToString();
                        var key = $"{labProfile}|{labName}";

                        if (!seenLabs.Contains(key))
                        {
                            seenLabs.Add(key);

                            var labObj = new JsonObject();
                            labObj["labprofile"] = labProfile;
                            labObj["labtestname"] = labName;
                            labObj["labresult"] = GetStringCaseInsensitive(row, "labresult");
                            labObj["labunits"] = GetStringCaseInsensitive(row, "labunits");
                            labObj["LabLow"] = GetStringCaseInsensitive(row, "LabLow") ?? GetStringCaseInsensitive(row, "Ref_Range");
                            labObj["LabHigh"] = GetStringCaseInsensitive(row, "LabHigh") ?? GetStringCaseInsensitive(row, "Ref_Range");
                            labObj["ServiceCode"] = GetStringCaseInsensitive(row, "ServiceCode");
                            labObj["labsection"] = GetStringCaseInsensitive(row, "labsection");
                            labObj["visitdate"] = GetStringCaseInsensitive(row, "visitdate") ?? GetStringCaseInsensitive(row, "labdate");
                            labObj["ResultDate"] = GetStringCaseInsensitive(row, "ResultDate");
                            labObj["LOINCCode"] = GetStringCaseInsensitive(row, "LOINCCode") ?? GetStringCaseInsensitive(row, "loinccode");

                            labs.Add(labObj);
                        }
                    }
                }

                // Radiology (distinct by ServiceCode)
                var radiology = new JsonArray();
                var seenRad = new HashSet<string>();
                foreach (var row in groupRows)
                {
                    // In SP, Service_no maps to ServiceCode for Rad
                    if (TryGetPropertyIgnoreCase(row, "Service_no", out var radSvcNode) && radSvcNode != null ||
                        TryGetPropertyIgnoreCase(row, "ServiceCode", out radSvcNode) && radSvcNode != null)
                    {
                        var radSvc = radSvcNode.ToString();
                        if (!seenRad.Contains(radSvc) && !string.IsNullOrEmpty(radSvc))
                        {
                            seenRad.Add(radSvc);

                            var radObj = new JsonObject();
                            radObj["ServiceCode"] = radSvc;
                            radObj["ServiceDescription"] = GetStringCaseInsensitive(row, "ServiceDescription");
                            radObj["RadiologyResult"] = GetStringCaseInsensitive(row, "RadiologyResult") ?? GetStringCaseInsensitive(row, "xray_report");
                            radObj["VisitDate"] = GetStringCaseInsensitive(row, "VisitDate");
                            radObj["ResultDate"] = GetStringCaseInsensitive(row, "ResultDate") ?? GetStringCaseInsensitive(row, "VisitDate");

                            radiology.Add(radObj);
                        }
                    }
                }

                result.Add(new ProviderClaimBundleRaw(
                    ProIdClaim: mappedId,
                    Header: header,
                    DhsDoctors: doctors.Count > 0 ? doctors : null,
                    Services: services,
                    Diagnoses: diagnoses,
                    Labs: labs,
                    Radiology: radiology,
                    Attachments: new JsonArray(),
                    OpticalVitalSigns: new JsonArray(),
                    ItemDetails: new JsonArray(),
                    Achi: new JsonArray()
                ));
            }
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

        var dropZerosList = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile?.PayersToDropZeroAmountServicesCsv))
        {
            dropZerosList.AddRange(profile.PayersToDropZeroAmountServicesCsv.Split(',').Select(s => s.Trim()));
        }

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

        var config = new ProviderViewsConfig(
            ViewSourceName: ExtractSourceName("Header", DefaultViewName, extraction?.HeaderSourceName),
            ClaimKeyColumnName: string.IsNullOrWhiteSpace(extraction?.ClaimKeyColumnName) ? DefaultClaimKeyColumn : extraction.ClaimKeyColumnName,
            DateColumnName: string.IsNullOrWhiteSpace(extraction?.DateColumnName) ? DefaultDateColumn : extraction.DateColumnName,
            ClaimSplitFollowupDays: profile?.ClaimSplitFollowupDays ?? 14,
            DefaultTreatmentCountryCode: string.IsNullOrWhiteSpace(profile?.DefaultTreatmentCountryCode) ? "SAR" : profile.DefaultTreatmentCountryCode,
            DefaultSubmissionReasonCode: string.IsNullOrWhiteSpace(profile?.DefaultSubmissionReasonCode) ? "01" : profile.DefaultSubmissionReasonCode,
            DefaultPriority: string.IsNullOrWhiteSpace(profile?.DefaultPriority) ? "Normal" : profile.DefaultPriority,
            DefaultErPbmDuration: profile?.DefaultErPbmDuration ?? 1,
            SfdaServiceTypeIdentifier: string.IsNullOrWhiteSpace(profile?.SfdaServiceTypeIdentifier) ? "SFDA" : profile.SfdaServiceTypeIdentifier,
            PayersToDropZeroAmountServices: dropZerosList,
            ParsedFieldMapping: parsedMapping
        );

        _configCache.TryAdd(providerDhsCode, config);
        return config;
    }

    private static JsonObject CloneObject(JsonObject obj)
    {
        var json = obj.ToJsonString();
        return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }

    private static bool TryGetPropertyIgnoreCase(JsonObject obj, string propertyName, out JsonNode? value)
    {
        value = null;
        if (obj == null) return false;

        if (obj.TryGetPropertyValue(propertyName, out value))
            return true;

        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = kvp.Value;
                return true;
            }
        }
        return false;
    }

    private static string? GetStringCaseInsensitive(JsonObject obj, string propertyName)
    {
        if (TryGetPropertyIgnoreCase(obj, propertyName, out var node) && node != null)
        {
            return node.ToString().Trim('"');
        }
        return null;
    }

    private static void SetPropertyIgnoreCase(JsonObject obj, string propertyName, JsonNode? value)
    {
        if (obj == null) return;

        var existingKey = obj.Select(k => k.Key).FirstOrDefault(k => string.Equals(k, propertyName, StringComparison.OrdinalIgnoreCase));
        if (existingKey != null)
        {
            obj[existingKey] = value;
        }
        else
        {
            obj[propertyName] = value;
        }
    }
}
