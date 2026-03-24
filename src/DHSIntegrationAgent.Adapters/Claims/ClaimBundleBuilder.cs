using DHSIntegrationAgent.Contracts.Claims;
﻿using System.Globalization;
using System.Text.Json.Nodes;
using DHSIntegrationAgent.Domain.Claims;

namespace DHSIntegrationAgent.Adapters.Claims;

/// <summary>
/// WBS 3.5: Canonical ClaimBundle builder + normalization.
/// Enforces:
/// - ProIdClaim is int (required)
/// - CompanyCode exists (inject if missing)
/// - MonthKey computed from preferred date fields (YYYYMM; required)
///
/// IMPORTANT:
/// This builder MUST NOT inject provider_dhsCode or bCR_Id.
/// Injection happens immediately before SendClaim (Streams B/C/D).
/// </summary>
public sealed class ClaimBundleBuilder
{
    public sealed record Options(
        IReadOnlyList<string> ProIdClaimFieldCandidates,
        IReadOnlyList<string> CompanyCodeFieldCandidates,
        IReadOnlyList<string> DateFieldCandidatesForMonthKey,
        IReadOnlyList<string> PatientNameFieldCandidates,
        IReadOnlyList<string> PatientIdFieldCandidates,
        IReadOnlyList<string> InvoiceNumberFieldCandidates,
        IReadOnlyList<string> TotalNetAmountFieldCandidates,
        IReadOnlyList<string> ClaimedAmountFieldCandidates
    )
    {
        public static readonly Options Default = new(
            ProIdClaimFieldCandidates: new[] { "proIdClaim", "ProIdClaim", "claimId", "ClaimId" },
            CompanyCodeFieldCandidates: new[] { "companyCode", "CompanyCode" },
            DateFieldCandidatesForMonthKey: new[] { "invoiceDate", "InvoiceDate", "claimDate", "ClaimDate", "createdDate", "CreatedDate" },
            PatientNameFieldCandidates: new[] { "patientName", "PatientName", "name", "Name" },
            PatientIdFieldCandidates: new[] { "patientID", "patientId", "PatientID", "PatientId", "id", "Id" },
            InvoiceNumberFieldCandidates: new[] { "invoiceNumber", "InvoiceNumber", "billNo", "BillNo" },
            TotalNetAmountFieldCandidates: new[] { "totalNetAmount", "TotalNetAmount", "netAmount", "NetAmount" },
            ClaimedAmountFieldCandidates: new[] { "claimedAmount", "ClaimedAmount" }
        );
    }

    private readonly Options _options;

    private enum SchemaDataType
    {
        String,
        Integer,
        Decimal,
        Boolean,
        DateTime
    }

    private static readonly Dictionary<string, SchemaDataType> _schemaDataTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Int / BigInt
        { "DestinationCode", SchemaDataType.Integer },
        { "provider_dhsCode", SchemaDataType.Integer },
        { "Age", SchemaDataType.Integer },
        { "DurationOFIllness", SchemaDataType.Integer },
        { "UserID", SchemaDataType.Integer },
        { "RelatedClaim", SchemaDataType.Integer },
        { "bCR_Id", SchemaDataType.Integer },
        { "ventilationhours", SchemaDataType.Integer },
        { "DiagnosisID", SchemaDataType.Integer },
        { "DHSI", SchemaDataType.Integer },
        { "diagnosis_Order", SchemaDataType.Integer },
        { "fK_DiagnosisType_ID", SchemaDataType.Integer },
        { "fK_ConditionOnset_ID", SchemaDataType.Integer },
        { "genderID", SchemaDataType.Integer },
        { "LabID", SchemaDataType.Integer },
        { "RadiologyID", SchemaDataType.Integer },
        { "ServiceID", SchemaDataType.Integer },
        { "SubmissionReasonCode", SchemaDataType.Integer },
        { "TreatmentTypeIndicator", SchemaDataType.Integer },
        { "PBMDuration", SchemaDataType.Integer },
        { "PBMTimes", SchemaDataType.Integer },
        { "PBMUnit", SchemaDataType.Integer },
        { "nphiesServiceTypeID", SchemaDataType.Integer },
        { "daysSupply", SchemaDataType.Integer },
        { "Doc_ID", SchemaDataType.Integer },
        { "SerialNo", SchemaDataType.Integer },

        // Decimal
        { "ClaimedAmount", SchemaDataType.Decimal },
        { "ClaimedAmountSAR", SchemaDataType.Decimal },
        { "TotalNetAmount", SchemaDataType.Decimal },
        { "TotalDiscount", SchemaDataType.Decimal },
        { "TotalDeductible", SchemaDataType.Decimal },
        { "NumberOfIncidents", SchemaDataType.Decimal },
        { "LineClaimedAmount", SchemaDataType.Decimal },
        { "CoInsurance", SchemaDataType.Decimal },
        { "CoPay", SchemaDataType.Decimal },
        { "LineItemDiscount", SchemaDataType.Decimal },
        { "NetAmount", SchemaDataType.Decimal },
        { "VatPercentage", SchemaDataType.Decimal },
        { "PatientVatAmount", SchemaDataType.Decimal },
        { "NetVatAmount", SchemaDataType.Decimal },
        { "CashAmount", SchemaDataType.Decimal },
        { "UnitPrice", SchemaDataType.Decimal },
        { "DiscPercentage", SchemaDataType.Decimal },
        { "priceListPrice", SchemaDataType.Decimal },
        { "priceListDiscount", SchemaDataType.Decimal },
        { "factor", SchemaDataType.Decimal },
        { "tax", SchemaDataType.Decimal },
        { "RightEyePower_Distance", SchemaDataType.Decimal },
        { "RightEyePower_Near", SchemaDataType.Decimal },
        { "LeftEyePower_Distance", SchemaDataType.Decimal },
        { "LeftEyePower_Near", SchemaDataType.Decimal },

        // Bit / Boolean
        { "ReferInd", SchemaDataType.Boolean },
        { "EmerInd", SchemaDataType.Boolean },
        { "DHSM", SchemaDataType.Boolean },
        { "NewBorn", SchemaDataType.Boolean },
        { "IsFetched", SchemaDataType.Boolean },
        { "isSync", SchemaDataType.Boolean },
        { "isUnacceptPrimaryDiagnosis", SchemaDataType.Boolean },
        { "isMorphologyRequired", SchemaDataType.Boolean },
        { "isRTAType", SchemaDataType.Boolean },
        { "isWPAType", SchemaDataType.Boolean },
        { "VatIndicator", SchemaDataType.Boolean },
        { "Maternity", SchemaDataType.Boolean },
        { "isUnlistedCode", SchemaDataType.Boolean },
        { "hasResult", SchemaDataType.Boolean },
        { "Active", SchemaDataType.Boolean },

        // DateTime / Date
        { "ProRequestDate", SchemaDataType.DateTime },
        { "InvoiceDate", SchemaDataType.DateTime },
        { "BatchDate", SchemaDataType.DateTime },
        { "BatchStartDate", SchemaDataType.DateTime },
        { "BatchEndDate", SchemaDataType.DateTime },
        { "DOB", SchemaDataType.DateTime },
        { "AdmissionDate", SchemaDataType.DateTime },
        { "DischargeDate", SchemaDataType.DateTime },
        { "EmergencyStartDate", SchemaDataType.DateTime },
        { "TriageDate", SchemaDataType.DateTime },
        { "FetchDate", SchemaDataType.DateTime },
        { "DiagnosisDate", SchemaDataType.DateTime },
        { "diagnosis_Date", SchemaDataType.DateTime },
        { "VisitDate", SchemaDataType.DateTime },
        { "ResultDate", SchemaDataType.DateTime },
        { "TreatmentFromDate", SchemaDataType.DateTime },
        { "TreatmentToDate", SchemaDataType.DateTime },
        { "WorkOrder", SchemaDataType.DateTime },
        { "Doc_DOB", SchemaDataType.DateTime }
    };

    public ClaimBundleBuilder(Options? options = null)
    {
        _options = options ?? Options.Default;
    }

    /// <summary>
    /// Builds and normalizes a ClaimBundle from canonical claim parts.
    /// </summary>
    /// <param name="parts">Canonical entity parts.</param>
    /// <param name="companyCode">CompanyCode chosen for the run. Injected if missing.</param>
    public ClaimBundleBuildResult Build(CanonicalClaimParts parts, string companyCode)
    {
        if (parts is null) throw new ArgumentNullException(nameof(parts));

        var issues = new List<ClaimBundleValidationIssue>();

        if (parts.ClaimHeader is null)
        {
            issues.Add(Block("MissingHeader", "claimHeader", null, "ClaimHeader is required."));
            return Fail(issues);
        }

        var header = CloneObject(parts.ClaimHeader);
        var headerKeys = BuildKeyMap(header);

        // 1) Ensure ProIdClaim exists and is int
        if (!TryReadInt(header, headerKeys, _options.ProIdClaimFieldCandidates, out var proIdClaim, out var rawProId))
        {
            issues.Add(Block("MissingProIdClaim", "claimHeader.proIdClaim", rawProId,
                "ProIdClaim is required and must be int-compatible."));
            return Fail(issues);
        }

        // Force canonical field name: proIdClaim (ensure no duplicates)
        RemovePropertyIgnoreCase(header, headerKeys, "proIdClaim");
        header["proIdClaim"] = proIdClaim;

        // 2) Ensure CompanyCode exists (inject if missing)
        var canonicalCompanyCode = NormalizeString(companyCode);
        if (string.IsNullOrWhiteSpace(canonicalCompanyCode))
        {
            issues.Add(Block("MissingCompanyCode", "claimHeader.companyCode", companyCode,
                "CompanyCode is required (run input)."));
            return Fail(issues);
        }

        if (!TryReadString(header, headerKeys, _options.CompanyCodeFieldCandidates, out var existingCompanyCode))
        {
            header["companyCode"] = canonicalCompanyCode;
        }
        else
        {
            // Normalize existing
            header["companyCode"] = NormalizeString(existingCompanyCode);
        }

        // 3) Compute MonthKey (YYYYMM) from a date field
        if (!TryReadDate(header, headerKeys, _options.DateFieldCandidatesForMonthKey, out var dt, out var rawDate))
        {
            issues.Add(Block("MissingMonthKeyDate", "claimHeader.invoiceDate|claimDate", rawDate,
                "A date field is required to compute MonthKey (YYYYMM)."));
            return Fail(issues);
        }

        var monthKey = dt.ToString("yyyyMM", CultureInfo.InvariantCulture);

        // We keep MonthKey out of payload unless backend expects it.
        // SQLite staging stores MonthKey separately; payload stays canonical.

        var serviceDetails = parts.ServiceDetails ?? new JsonArray();
        var diagnosisDetails = parts.DiagnosisDetails ?? new JsonArray();
        var labDetails = parts.LabDetails ?? new JsonArray();
        var radiologyDetails = parts.RadiologyDetails ?? new JsonArray();
        var opticalVitalSigns = parts.OpticalVitalSigns ?? new JsonArray();
        var dhsDoctors = parts.DhsDoctors ?? new JsonArray();

        // 4) Convert fields based on dhs_schema.json types
        ConvertNode(header);
        ConvertNode(serviceDetails);
        ConvertNode(diagnosisDetails);
        ConvertNode(labDetails);
        ConvertNode(radiologyDetails);
        ConvertNode(opticalVitalSigns);
        ConvertNode(dhsDoctors);

        // 5) Ensure arrays exist (empty arrays allowed)
        var bundle = new ClaimBundle(
            claimHeader: header,
            serviceDetails: serviceDetails,
            diagnosisDetails: diagnosisDetails,
            labDetails: labDetails,
            radiologyDetails: radiologyDetails,
            opticalVitalSigns: opticalVitalSigns,
            dhsDoctors: dhsDoctors
        );

        // 6) Normalize detail items
        // serviceDetails uses all-lowercase 'proidclaim' as STRING per latest requirement (only here).
        EnsureProIdClaimWithoutIssues(bundle.ServiceDetails, proIdClaim.ToString(CultureInfo.InvariantCulture), "proidclaim");

        // Other detail sets MUST have proIdClaim as integer per latest requirement.
        EnsureProIdClaimInt(bundle.DiagnosisDetails, proIdClaim);
        EnsureProIdClaimInt(bundle.LabDetails, proIdClaim);
        EnsureProIdClaimInt(bundle.RadiologyDetails, proIdClaim);
        EnsureProIdClaimInt(bundle.OpticalVitalSigns, proIdClaim);
        EnsureProIdClaimInt(bundle.DhsDoctors, proIdClaim);

        // 7) Normalize specific fields
        NormalizeDiagnosisDates(bundle.DiagnosisDetails);

        return new ClaimBundleBuildResult(
            Succeeded: true,
            ProIdClaim: proIdClaim,
            CompanyCode: canonicalCompanyCode,
            MonthKey: monthKey,
            Bundle: bundle,
            Issues: issues
        );
    }

    // -------------------------
    // Normalization helpers
    // -------------------------

    private static void ConvertNode(JsonNode? node)
    {
        if (node is null) return;

        if (node is JsonObject obj)
        {
            var keys = obj.Select(k => k.Key).ToList();
            foreach (var key in keys)
            {
                // Preserve existing proclaimid implementation
                if (key.Equals("proIdClaim", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (obj[key] is JsonValue jVal)
                {
                    if (_schemaDataTypes.TryGetValue(key, out var schemaType))
                    {
                        obj[key] = ConvertToType(jVal, schemaType);
                    }
                    else
                    {
                        // Default to string for any other unmapped fields (like varchar/nvarchar)
                        obj[key] = ConvertToType(jVal, SchemaDataType.String);
                    }
                }
                else if (obj[key] is JsonObject || obj[key] is JsonArray)
                {
                    ConvertNode(obj[key]);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                ConvertNode(item);
            }
        }
    }

    private static JsonNode? ConvertToType(JsonValue value, SchemaDataType targetType)
    {
        if (value == null) return null;

        var strVal = value.ToString().Trim('"');
        if (string.IsNullOrWhiteSpace(strVal)) return null;

        return targetType switch
        {
            SchemaDataType.Integer => (long.TryParse(strVal, out var l) || decimal.TryParse(strVal, out var dec) && (l = (long)dec) == dec) ? JsonValue.Create(l) : null,
            SchemaDataType.Decimal => decimal.TryParse(strVal, out var dec) ? JsonValue.Create(dec) : null,
            SchemaDataType.Boolean => (bool.TryParse(strVal, out var b) ? JsonValue.Create(b) : (strVal == "1" || strVal.Equals("true", StringComparison.OrdinalIgnoreCase) ? JsonValue.Create(true) : JsonValue.Create(false))),
            SchemaDataType.DateTime => DateTimeOffset.TryParse(strVal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? JsonValue.Create(dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")) : JsonValue.Create(strVal),
            _ => JsonValue.Create(strVal)
        };
    }

    private static void EnsureProIdClaimWithoutIssues(JsonArray details, string value, string targetFieldName = "proIdClaim")
    {
        for (var i = 0; i < details.Count; i++)
        {
            if (details[i] is not JsonObject obj)
                continue;

            var keyMap = BuildKeyMap(obj);
            // Remove existing variations to ensure no duplicates
            RemovePropertyIgnoreCase(obj, keyMap, "proIdClaim");
            obj[targetFieldName] = value;
        }
    }

    private static void EnsureProIdClaimInt(JsonArray details, long value, string targetFieldName = "proidclaim")
    {
        for (var i = 0; i < details.Count; i++)
        {
            if (details[i] is not JsonObject obj)
                continue;

            var keyMap = BuildKeyMap(obj);
            // Remove existing variations to ensure no duplicates
            RemovePropertyIgnoreCase(obj, keyMap, "proIdClaim");
            obj[targetFieldName] = value;
        }
    }

    private static void NormalizeDiagnosisDates(JsonArray diagnosisDetails)
    {
        for (var i = 0; i < diagnosisDetails.Count; i++)
        {
            if (diagnosisDetails[i] is not JsonObject obj)
                continue;

            var keyMap = BuildKeyMap(obj);

            // Try variations. 'DiagnosisDate' covers 'diagnosisDate' due to case-insensitive keyMap.
            if ((TryGetPropertyIgnoreCase(obj, keyMap, "DiagnosisDate", out var node) ||
                 TryGetPropertyIgnoreCase(obj, keyMap, "diagnosis_Date", out node)) && node != null)
            {
                var raw = node.ToString().Trim('"');
                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                {
                    // Remove variations to ensure no duplicates.
                    RemovePropertyIgnoreCase(obj, keyMap, "DiagnosisDate");
                    RemovePropertyIgnoreCase(obj, keyMap, "diagnosis_Date");
                    obj["DiagnosisDate"] = dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                else if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                {
                    // Remove variations to ensure no duplicates
                    RemovePropertyIgnoreCase(obj, keyMap, "DiagnosisDate");
                    RemovePropertyIgnoreCase(obj, keyMap, "diagnosis_Date");
                    obj["DiagnosisDate"] = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }
        }
    }

    private static bool TryReadInt(JsonObject obj, Dictionary<string, List<string>> keyMap, IReadOnlyList<string> candidates, out int value, out string? raw)
    {
        value = default;
        raw = null;

        foreach (var name in candidates)
        {
            if (!TryGetPropertyIgnoreCase(obj, keyMap, name, out var node))
                continue;

            raw = node?.ToString();

            if (node is null) continue;

            // numeric node
            if (node is JsonValue v)
            {
                if (v.TryGetValue<int>(out var i))
                {
                    value = i;
                    return true;
                }

                if (v.TryGetValue<long>(out var l) && l >= int.MinValue && l <= int.MaxValue)
                {
                    value = (int)l;
                    return true;
                }

                if (v.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si))
                {
                    value = si;
                    return true;
                }
            }

            // fallback parse
            if (int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private static void RemovePropertyIgnoreCase(JsonObject obj, Dictionary<string, List<string>> keyMap, string name)
    {
        if (keyMap.TryGetValue(name, out var keysToRemove))
        {
            foreach (var key in keysToRemove)
            {
                obj.Remove(key);
            }
            keyMap.Remove(name);
        }
    }

    private static bool TryReadString(JsonObject obj, Dictionary<string, List<string>> keyMap, IReadOnlyList<string> candidates, out string? value)
    {
        value = null;

        foreach (var name in candidates)
        {
            if (!TryGetPropertyIgnoreCase(obj, keyMap, name, out var node))
                continue;

            value = NormalizeString(node?.ToString());
            if (!string.IsNullOrWhiteSpace(value))
                return true;
        }

        return false;
    }

    private static bool TryReadDate(JsonObject obj, Dictionary<string, List<string>> keyMap, IReadOnlyList<string> candidates, out DateTimeOffset dt, out string? raw)
    {
        dt = default;
        raw = null;

        foreach (var name in candidates)
        {
            if (!TryGetPropertyIgnoreCase(obj, keyMap, name, out var node))
                continue;

            raw = node?.ToString()?.Trim('"');
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            // Support ISO timestamps and common date formats
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dt))
                return true;

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
            {
                dt = new DateTimeOffset(d, TimeSpan.Zero);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPropertyIgnoreCase(JsonObject obj, Dictionary<string, List<string>> keyMap, string name, out JsonNode? node)
    {
        node = null;
        if (keyMap.TryGetValue(name, out var originalKeys) && originalKeys.Count > 0)
        {
            return obj.TryGetPropertyValue(originalKeys[0], out node);
        }
        return false;
    }

    private static Dictionary<string, List<string>> BuildKeyMap(JsonObject obj)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in obj)
        {
            if (!map.TryGetValue(kv.Key, out var list))
            {
                list = new List<string>();
                map[kv.Key] = list;
            }
            list.Add(kv.Key);
        }
        return map;
    }

    private static JsonObject CloneObject(JsonObject obj)
    {
        // JsonObject.Clone() is not available; do a round-trip clone.
        var json = obj.ToJsonString();
        return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }

    private static string? NormalizeString(string? s)
    {
        if (s is null) return null;
        var t = s.Trim();
        return t.Length == 0 ? null : t;
    }

    private static ClaimBundleBuildResult Fail(List<ClaimBundleValidationIssue> issues)
        => new(false, 0, string.Empty, string.Empty, null, issues);

    private static ClaimBundleValidationIssue Block(string type, string path, string? raw, string msg)
        => new(type, path, raw, msg, IsBlocking: true);

}
