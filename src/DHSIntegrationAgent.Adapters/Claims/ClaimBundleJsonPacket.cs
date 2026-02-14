using System.Text.Json;
using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Adapters.Claims;

/// <summary>
/// Helper for building the SendClaim request body: JSON array of 1..40 Claim objects.
/// Stream B/C/D will call this AFTER injecting provider_dhsCode + bCR_Id into claimHeader.
/// </summary>
public static class ClaimBundleJsonPacket
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string ToJsonArray(IEnumerable<object> bundles)
    {
        if (bundles is null) throw new ArgumentNullException(nameof(bundles));
        return JsonSerializer.Serialize(bundles, Options);
    }
}
