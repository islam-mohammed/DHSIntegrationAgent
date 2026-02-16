using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Adapters.Claims;

/// <summary>
/// Canonical entity parts produced by any provider integration mode.
/// - Tables (3.2): built from rows.
/// - Views (3.3): built from rows.
/// - Custom SQL (3.4): built from templates (aliased columns or via column map).
///
/// This keeps Stream logic stable: adapters only need to output these canonical parts.
/// </summary>
public sealed record CanonicalClaimParts(
    JsonObject ClaimHeader,
    JsonArray? ServiceDetails = null,
    JsonArray? DiagnosisDetails = null,
    JsonArray? LabDetails = null,
    JsonArray? RadiologyDetails = null,
    JsonArray? OpticalVitalSigns = null,
    JsonArray? DoctorDetails = null
);
