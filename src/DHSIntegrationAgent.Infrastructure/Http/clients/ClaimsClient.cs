using DHSIntegrationAgent.Contracts.Claims;
﻿using System.Net;
using System.Text;
using System.Text.Json;
using DHSIntegrationAgent.Application.Abstractions;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

/// <summary>
/// WBS 2.5: Claims API client.
/// POST /api/Claims/SendClaim (gzip JSON array).
///
/// Notes:
/// - DO NOT log request bodies (PHI). ApiLoggingHandler logs safe metadata.
/// - Gzip compression is applied by GzipRequestHandler (pipeline).
/// - Timeouts are applied only if ApiTimeoutHandler is registered in the pipeline.
/// </summary>
public sealed class ClaimsClient : IClaimsClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    // Response parsing (tolerant)
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ClaimsClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<SendClaimResult> SendClaimAsync(string claimBundlesJsonArray, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(claimBundlesJsonArray))
            return Fail("SendClaim payload is empty.", httpStatusCode: null);

        // Minimal validation: must be a JSON array
        var trimmed = claimBundlesJsonArray.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
            return Fail("SendClaim payload must be a JSON array string.", httpStatusCode: null);

        var client = _httpClientFactory.CreateClient("BackendApi");
        const string path = "api/Claims/SendClaim";

        using var content = new StringContent(trimmed, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var httpCode = (int)response.StatusCode;

        var body = await response.Content.ReadAsStringAsync(ct);

        // Non-200: treat as failure
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return Fail($"SendClaim failed (HTTP {httpCode} {response.StatusCode}).", httpStatusCode: httpCode);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Fail("SendClaim failed: empty response body.", httpStatusCode: httpCode);
        }

        // Parse: expect data.successClaimsProidClaim[] and data.failClaimsProidClaim[]
        if (!TryParseSendClaimResponse(body, out var succeeded, out var message, out var successIds, out var failIds))
        {
            return Fail("SendClaim returned an unexpected response shape.", httpStatusCode: httpCode);
        }

        // If backend provides a succeeded flag and it's false, treat as failure even if HTTP 200
        if (succeeded.HasValue && !succeeded.Value)
        {
            return new SendClaimResult(
                Succeeded: false,
                ErrorMessage: string.IsNullOrWhiteSpace(message) ? "SendClaim returned succeeded=false." : message,
                SuccessClaimsProIdClaim: successIds,
                FailClaimsProIdClaim: failIds,
                HttpStatusCode: httpCode);
        }

        return new SendClaimResult(
            Succeeded: true,
            ErrorMessage: null,
            SuccessClaimsProIdClaim: successIds,
            FailClaimsProIdClaim: failIds,
            HttpStatusCode: httpCode);
    }

    private static SendClaimResult Fail(string error, int? httpStatusCode)
        => new(
            Succeeded: false,
            ErrorMessage: error,
            SuccessClaimsProIdClaim: Array.Empty<long>(),
            FailClaimsProIdClaim: Array.Empty<long>(),
            HttpStatusCode: httpStatusCode);

    private static bool TryParseSendClaimResponse(
        string json,
        out bool? succeeded,
        out string? message,
        out IReadOnlyList<long> successIds,
        out IReadOnlyList<long> failIds)
    {
        succeeded = null;
        message = null;
        successIds = Array.Empty<long>();
        failIds = Array.Empty<long>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Optional top-level common fields
            succeeded = TryGetBool(root, "succeeded", "Succeeded", "success", "Success");
            message = TryGetString(root, "message", "Message", "error", "Error");

            // Required: data.successClaimsProidClaim[] and data.failClaimsProidClaim[]
            if (!TryGetObject(root, out var dataObj, "data", "Data"))
                return false;

            var success = ReadLongArray(dataObj, "successClaimsProidClaim", "SuccessClaimsProidClaim", "successClaimsProIdClaim", "successClaimsProidclaim");
            var fail = ReadLongArray(dataObj, "failClaimsProidClaim", "FailClaimsProidClaim", "failClaimsProIdClaim", "failClaimsProidclaim");

            // Even if one list is missing, we still accept and return what we have (tolerant).
            successIds = success;
            failIds = fail;

            // At least one list should exist to consider this recognizable.
            return success.Count > 0 || fail.Count > 0 || dataObj.ValueKind == JsonValueKind.Object;
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
