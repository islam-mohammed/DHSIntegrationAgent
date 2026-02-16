using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Adapters.Tables;

public sealed record ProviderClaimBundleRaw(
    int ProIdClaim,
    JsonObject Header,
    JsonArray? Doctor,
    JsonArray Services,
    JsonArray Diagnoses,
    JsonArray Labs,
    JsonArray Radiology,
    JsonArray Attachments,
    JsonArray OpticalVitalSigns,
    JsonArray ItemDetails,
    JsonArray Achi);
