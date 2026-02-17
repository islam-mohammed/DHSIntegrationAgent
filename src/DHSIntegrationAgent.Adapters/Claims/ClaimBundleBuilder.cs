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

        // 1) Ensure ProIdClaim exists and is int
        if (!TryReadInt(header, _options.ProIdClaimFieldCandidates, out var proIdClaim, out var rawProId))
        {
            issues.Add(Block("MissingProIdClaim", "claimHeader.proIdClaim", rawProId,
                "ProIdClaim is required and must be int-compatible."));
            return Fail(issues);
        }

        // Force canonical field name: proIdClaim (ensure no duplicates)
        RemovePropertyIgnoreCase(header, "proIdClaim");
        header["proIdClaim"] = proIdClaim;

        // 2) Ensure CompanyCode exists (inject if missing)
        var canonicalCompanyCode = NormalizeString(companyCode);
        if (string.IsNullOrWhiteSpace(canonicalCompanyCode))
        {
            issues.Add(Block("MissingCompanyCode", "claimHeader.companyCode", companyCode,
                "CompanyCode is required (run input)."));
            return Fail(issues);
        }

        if (!TryReadString(header, _options.CompanyCodeFieldCandidates, out var existingCompanyCode))
        {
            header["companyCode"] = canonicalCompanyCode;
        }
        else
        {
            // Normalize existing
            header["companyCode"] = NormalizeString(existingCompanyCode);
        }

        // 3) Compute MonthKey (YYYYMM) from a date field
        if (!TryReadDate(header, _options.DateFieldCandidatesForMonthKey, out var dt, out var rawDate))
        {
            issues.Add(Block("MissingMonthKeyDate", "claimHeader.invoiceDate|claimDate", rawDate,
                "A date field is required to compute MonthKey (YYYYMM)."));
            return Fail(issues);
        }

        var monthKey = dt.ToString("yyyyMM", CultureInfo.InvariantCulture);

        // We keep MonthKey out of payload unless backend expects it.
        // SQLite staging stores MonthKey separately; payload stays canonical.

        // 4) Ensure arrays exist (empty arrays allowed)
        var bundle = new ClaimBundle(
            claimHeader: header,
            serviceDetails: parts.ServiceDetails ?? new JsonArray(),
            diagnosisDetails: parts.DiagnosisDetails ?? new JsonArray(),
            labDetails: parts.LabDetails ?? new JsonArray(),
            radiologyDetails: parts.RadiologyDetails ?? new JsonArray(),
            opticalVitalSigns: parts.OpticalVitalSigns ?? new JsonArray(),
            dhsDoctors: parts.DhsDoctors ?? new JsonArray()
        );

        // 5) Normalize detail items
        // serviceDetails uses all-lowercase 'proidclaim' as STRING per latest requirement (only here).
        EnsureProIdClaimWithoutIssues(bundle.ServiceDetails, proIdClaim.ToString(CultureInfo.InvariantCulture), "proidclaim");

        // Other detail sets MUST NOT have proIdClaim per latest requirement (only in serviceDetails).
        RemoveProIdClaim(bundle.DiagnosisDetails);
        RemoveProIdClaim(bundle.LabDetails);
        RemoveProIdClaim(bundle.RadiologyDetails);
        RemoveProIdClaim(bundle.OpticalVitalSigns);
        RemoveProIdClaim(bundle.DhsDoctors);

        // 6) Normalize specific fields
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

    private static void EnsureProIdClaimWithoutIssues(JsonArray details, string value, string targetFieldName = "proIdClaim")
    {
        for (var i = 0; i < details.Count; i++)
        {
            if (details[i] is not JsonObject obj)
                continue;

            // Remove existing variations to ensure no duplicates
            RemovePropertyIgnoreCase(obj, "proIdClaim");
            obj[targetFieldName] = value;
        }
    }

    private static void RemoveProIdClaim(JsonArray details)
    {
        for (var i = 0; i < details.Count; i++)
        {
            if (details[i] is not JsonObject obj)
                continue;

            RemovePropertyIgnoreCase(obj, "proIdClaim");
        }
    }

    private static void NormalizeDiagnosisDates(JsonArray diagnosisDetails)
    {
        for (var i = 0; i < diagnosisDetails.Count; i++)
        {
            if (diagnosisDetails[i] is not JsonObject obj)
                continue;

            // Try 'DiagnosisDate', 'diagnosisDate' and 'diagnosis_Date' variations
            if ((TryGetPropertyIgnoreCase(obj, "DiagnosisDate", out var node) ||
                 TryGetPropertyIgnoreCase(obj, "diagnosisDate", out node) ||
                 TryGetPropertyIgnoreCase(obj, "diagnosis_Date", out node)) && node != null)
            {
                var raw = node.ToString().Trim('"');
                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                {
                    // Remove ALL variations to ensure no duplicates
                    RemovePropertyIgnoreCase(obj, "DiagnosisDate");
                    RemovePropertyIgnoreCase(obj, "diagnosisDate");
                    RemovePropertyIgnoreCase(obj, "diagnosis_Date");
                    obj["DiagnosisDate"] = dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                else if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                {
                    // Remove ALL variations to ensure no duplicates
                    RemovePropertyIgnoreCase(obj, "DiagnosisDate");
                    RemovePropertyIgnoreCase(obj, "diagnosisDate");
                    RemovePropertyIgnoreCase(obj, "diagnosis_Date");
                    obj["DiagnosisDate"] = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }
        }
    }

    private static bool TryReadInt(JsonObject obj, IReadOnlyList<string> candidates, out int value, out string? raw)
    {
        value = default;
        raw = null;

        foreach (var name in candidates)
        {
            if (!TryGetPropertyIgnoreCase(obj, name, out var node))
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

    private static void RemovePropertyIgnoreCase(JsonObject obj, string name)
    {
        var keysToRemove = obj.Select(kv => kv.Key)
                              .Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                              .ToList();
        foreach (var key in keysToRemove)
        {
            obj.Remove(key);
        }
    }

    private static bool TryReadString(JsonObject obj, IReadOnlyList<string> candidates, out string? value)
    {
        value = null;

        foreach (var name in candidates)
        {
            if (!TryGetPropertyIgnoreCase(obj, name, out var node))
                continue;

            value = NormalizeString(node?.ToString());
            if (!string.IsNullOrWhiteSpace(value))
                return true;
        }

        return false;
    }

    private static bool TryReadDate(JsonObject obj, IReadOnlyList<string> candidates, out DateTimeOffset dt, out string? raw)
    {
        dt = default;
        raw = null;

        foreach (var name in candidates)
        {
            if (!TryGetPropertyIgnoreCase(obj, name, out var node))
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

    private static bool TryGetPropertyIgnoreCase(JsonObject obj, string name, out JsonNode? node)
    {
        node = null;

        // Exact
        if (obj.TryGetPropertyValue(name, out node))
            return true;

        // Case-insensitive scan
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                node = kv.Value;
                return true;
            }
        }

        return false;
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
