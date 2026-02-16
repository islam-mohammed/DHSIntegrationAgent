using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DHSIntegrationAgent.Domain.Claims;

/// <summary>
/// Canonical in-memory claim payload shape. All provider integration modes
/// (Tables / Views / Custom SQL) must produce this same shape.
/// </summary>
public sealed class ClaimBundle
{
    [JsonPropertyName("claimHeader")]
    public JsonObject ClaimHeader { get; }

    [JsonPropertyName("serviceDetails")]
    public JsonArray ServiceDetails { get; }

    [JsonPropertyName("diagnosisDetails")]
    public JsonArray DiagnosisDetails { get; }

    [JsonPropertyName("labDetails")]
    public JsonArray LabDetails { get; }

    [JsonPropertyName("radiologyDetails")]
    public JsonArray RadiologyDetails { get; }

    [JsonPropertyName("opticalVitalSigns")]
    public JsonArray OpticalVitalSigns { get; }

    [JsonPropertyName("doctorDetails")]
    public JsonArray? DoctorDetails { get; }

    public ClaimBundle(
        JsonObject claimHeader,
        JsonArray? serviceDetails = null,
        JsonArray? diagnosisDetails = null,
        JsonArray? labDetails = null,
        JsonArray? radiologyDetails = null,
        JsonArray? opticalVitalSigns = null,
        JsonArray? doctorDetails = null)
    {
        ClaimHeader = claimHeader ?? throw new ArgumentNullException(nameof(claimHeader));
        ServiceDetails = serviceDetails ?? new JsonArray();
        DiagnosisDetails = diagnosisDetails ?? new JsonArray();
        LabDetails = labDetails ?? new JsonArray();
        RadiologyDetails = radiologyDetails ?? new JsonArray();
        OpticalVitalSigns = opticalVitalSigns ?? new JsonArray();
        DoctorDetails = doctorDetails ?? new JsonArray();
    }

    /// <summary>
    /// Serialize as compact JSON (staging payload). Uses camelCase conventions by default.
    /// Staging must be deterministic (no injected ProviderDhsCode/BcrId at this stage).
    /// </summary>
    public string ToJsonString()
        => ToJsonNode().ToJsonString(JsonDefaults.Canonical);

    public JsonObject ToJsonNode()
        => new()
        {
            ["claimHeader"] = ClaimHeader,
            ["serviceDetails"] = ServiceDetails,
            ["diagnosisDetails"] = DiagnosisDetails,
            ["labDetails"] = LabDetails,
            ["radiologyDetails"] = RadiologyDetails,
            ["opticalVitalSigns"] = OpticalVitalSigns,
            ["doctorDetails"] = DoctorDetails
        };

    public static class JsonDefaults
    {
        public static readonly JsonSerializerOptions Canonical = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }
}
