using System.Net.Http.Json;
using System.Text.Json;
using DHSIntegrationAgent.Application.Abstractions;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

public sealed class HealthClient : IHealthClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public HealthClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<ApiHealthResult> CheckApiHealthAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BackendApi");
            const string path = "api/Health/CheckAPIHealth";

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<HealthResponse>(body, JsonOptions);
                    if (parsed is not null)
                    {
                        return new ApiHealthResult(
                            parsed.Succeeded,
                            parsed.StatusCode,
                            parsed.Message,
                            parsed.Errors);
                    }
                }
                catch (JsonException)
                {
                    // If response is not valid JSON (e.g. an HTML proxy error), fall back
                    // to the default behavior below, preserving the actual HTTP status code.
                }
            }

            return new ApiHealthResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                $"HTTP {(int)response.StatusCode} {response.StatusCode}",
                null);
        }
        catch (Exception ex)
        {
            return new ApiHealthResult(false, 0, $"Network error: {ex.Message}", new[] { ex.ToString() });
        }
    }

    private sealed record HealthResponse(bool Succeeded, int StatusCode, string? Message, List<string>? Errors);
}
