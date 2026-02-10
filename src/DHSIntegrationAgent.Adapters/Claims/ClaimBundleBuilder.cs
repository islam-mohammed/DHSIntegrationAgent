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
        IReadOnlyList<string> DateFieldCandidatesForMonthKey
    )
    {
        public static readonly Options Default = new(
            ProIdClaimFieldCandidates: new[] { "proIdClaim", "ProIdClaim", "claimId", "ClaimId" },
            CompanyCodeFieldCandidates: new[] { "companyCode", "CompanyCode" },
            DateFieldCandidatesForMonthKey: new[] { "invoiceDate", "InvoiceDate", "claimDate", "ClaimDate", "createdDate", "CreatedDate" }
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

        // Force canonical field name: proIdClaim
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
            opticalVitalSigns: parts.OpticalVitalSigns ?? new JsonArray()
        );

        // 5) Normalize detail items: ensure each detail (if object) has proIdClaim for traceability (non-blocking)
        EnsureProIdClaimOnDetails(bundle.ServiceDetails, proIdClaim, issues, "serviceDetails");
        EnsureProIdClaimOnDetails(bundle.DiagnosisDetails, proIdClaim, issues, "diagnosisDetails");
        EnsureProIdClaimOnDetails(bundle.LabDetails, proIdClaim, issues, "labDetails");
        EnsureProIdClaimOnDetails(bundle.RadiologyDetails, proIdClaim, issues, "radiologyDetails");
        EnsureProIdClaimOnDetails(bundle.OpticalVitalSigns, proIdClaim, issues, "opticalVitalSigns");

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

    private static void EnsureProIdClaimOnDetails(JsonArray details, int proIdClaim, List<ClaimBundleValidationIssue> issues, string arrayName)
    {
        for (var i = 0; i < details.Count; i++)
        {
            if (details[i] is not JsonObject obj)
                continue;

            // If missing, inject for traceability (non-blocking).
            if (!obj.TryGetPropertyValue("proIdClaim", out _))
            {
                obj["proIdClaim"] = proIdClaim;
                issues.Add(Info("InjectedProIdClaim", $"{arrayName}[{i}].proIdClaim", null,
                    "Injected proIdClaim into detail item for traceability."));
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

            raw = node?.ToString();
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

    private static ClaimBundleValidationIssue Info(string type, string path, string? raw, string msg)
        => new(type, path, raw, msg, IsBlocking: false);
}
