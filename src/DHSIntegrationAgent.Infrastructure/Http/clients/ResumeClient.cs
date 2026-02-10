using System.Net;
using System.Text.Json;
using DHSIntegrationAgent.Application.Claims;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

/// <summary>
/// WBS 2.6: Resume API client.
/// GET /api/Claims/GetHISProIdClaimsByBcrId/{bcrId}.
///
/// Expected contract:
///   response.data.hisProIdClaim[]
///
/// Notes:
/// - Do NOT log response bodies here (PHI safety). ApiLoggingHandler logs safe metadata.
/// - Auth headers are applied by AuthHeaderHandler.
/// - Response decompression is handled by the primary handler.
/// </summary>
public sealed class ResumeClient : IResumeClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ResumeClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<GetHisProIdClaimByBcrIdResult> GetHisProIdClaimByBcrIdAsync(string bcrId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bcrId))
            return Fail("bcrId is required.", httpStatusCode: null);

        var client = _httpClientFactory.CreateClient("BackendApi");

        // Spec: GET /api/Claims/GetHISProIdClaimsByBcrId/{bcrId}
        var path = $"api/Claims/GetHISProIdClaimsByBcrId/{Uri.EscapeDataString(bcrId)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var httpCode = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode != HttpStatusCode.OK)
            return Fail($"GetHISProIdClaimsByBcrId failed (HTTP {httpCode} {response.StatusCode}).", httpStatusCode: httpCode);

        if (string.IsNullOrWhiteSpace(body))
            return Fail("GetHISProIdClaimsByBcrId returned empty response body.", httpStatusCode: httpCode);

        if (!TryParse(body, out var succeeded, out var message, out var hisProIdClaim))
            return Fail("GetHISProIdClaimsByBcrId returned an unexpected response shape.", httpStatusCode: httpCode);

        // If backend includes succeeded=false, treat as failure even if HTTP 200.
        if (succeeded.HasValue && !succeeded.Value)
        {
            return new GetHisProIdClaimByBcrIdResult(
                Succeeded: false,
                ErrorMessage: string.IsNullOrWhiteSpace(message) ? "GetHISProIdClaimsByBcrId returned succeeded=false." : message,
                HisProIdClaim: hisProIdClaim,
                HttpStatusCode: httpCode);
        }

        return new GetHisProIdClaimByBcrIdResult(
            Succeeded: true,
            ErrorMessage: null,
            HisProIdClaim: hisProIdClaim,
            HttpStatusCode: httpCode);
    }

    private static GetHisProIdClaimByBcrIdResult Fail(string error, int? httpStatusCode)
        => new(
            Succeeded: false,
            ErrorMessage: error,
            HisProIdClaim: Array.Empty<long>(),
            HttpStatusCode: httpStatusCode);

    private static bool TryParse(
        string json,
        out bool? succeeded,
        out string? message,
        out IReadOnlyList<long> hisProIdClaim)
    {
        succeeded = null;
        message = null;
        hisProIdClaim = Array.Empty<long>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Optional top-level common fields (tolerant)
            succeeded = TryGetBool(root, "succeeded", "Succeeded", "success", "Success");
            message = TryGetString(root, "message", "Message", "error", "Error");

            // Contract: data.hisProIdClaim[]
            if (!TryGetObject(root, out var dataObj, "data", "Data"))
            {
                // Strictly speaking, contract says data.*.
                // We do NOT accept root-level fallback unless it matches exact property name.
                var rootFallback = ReadLongArray(root, "hisProIdClaim", "HisProIdClaim");
                if (rootFallback.Count == 0) return false;

                hisProIdClaim = rootFallback;
                return true;
            }

            var ids = ReadLongArray(dataObj, "hisProIdClaim", "HisProIdClaim", "hisProIDClaim", "hisProIdClaims");
            hisProIdClaim = ids;

            // Accept empty array as valid (meaning none completed yet).
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetObject(JsonElement root, out JsonElement obj, params string[] names)
    {
        obj = default;

        if (root.ValueKind != JsonValueKind.Object) return false;

        foreach (var n in names)
        {
            if (root.TryGetProperty(n, out var prop) && prop.ValueKind == JsonValueKind.Object)
            {
                obj = prop;
                return true;
            }
        }

        return false;
    }

    private static List<long> ReadLongArray(JsonElement parent, params string[] names)
    {
        foreach (var n in names)
        {
            if (parent.ValueKind == JsonValueKind.Object &&
                parent.TryGetProperty(n, out var prop) &&
                prop.ValueKind == JsonValueKind.Array)
            {
                var list = new List<long>();
                foreach (var el in prop.EnumerateArray())
                {
                    if (TryReadLong(el, out var v))
                        list.Add(v);
                }
                return list;
            }
        }

        return new List<long>();
    }

    private static bool TryReadLong(JsonElement el, out long value)
    {
        value = default;

        if (el.ValueKind == JsonValueKind.Number)
            return el.TryGetInt64(out value);

        if (el.ValueKind == JsonValueKind.String)
            return long.TryParse(el.GetString(), out value);

        return false;
    }

    private static string? TryGetString(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;

        foreach (var n in names)
        {
            if (obj.TryGetProperty(n, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
                if (prop.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) return prop.ToString();
            }
        }

        return null;
    }

    private static bool? TryGetBool(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;

        foreach (var n in names)
        {
            if (obj.TryGetProperty(n, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True) return true;
                if (prop.ValueKind == JsonValueKind.False) return false;

                if (prop.ValueKind == JsonValueKind.String &&
                    bool.TryParse(prop.GetString(), out var b))
                    return b;
            }
        }

        return null;
    }
}
