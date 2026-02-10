using System.Net;
using System.Text;
using System.Text.Json;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Security;
using Microsoft.Extensions.Options;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

public sealed class AuthClient : IAuthClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ApiOptions>? _apiOptions;

    // Response parsing
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    // Request serialization (camelCase)
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // NOTE: optional apiOptions keeps old call sites working (tests/constructors).
    public AuthClient(IHttpClientFactory httpClientFactory, IOptions<ApiOptions>? apiOptions = null)
    {
        _httpClientFactory = httpClientFactory;
        _apiOptions = apiOptions;
    }

    public async Task<AuthLoginResult> LoginAsync(string email, string groupID, string password, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("BackendApi");
        const string path = "api/Authentication/login";

        var requestBody = new LoginRequest(email, password, groupID);

        HttpContent content;

        // Default to true (spec requires gzip). If config disables gzip (dev only), send plain JSON.
        var gzipEnabled = _apiOptions?.Value.UseGzipPostRequests ?? true;

        if (gzipEnabled)
        {
            content = GzipJsonHttpContent.Create(requestBody, RequestJsonOptions);
        }
        else
        {
            var json = JsonSerializer.Serialize(requestBody, RequestJsonOptions);
            content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        // Always parse the JSON body to match your contract
        var parsed = TryParseLoginResponse(body);
        if (parsed is not null)
        {
            if (parsed.Succeeded && parsed.StatusCode == 200)
                return new AuthLoginResult(true, null, null);

            return new AuthLoginResult(false,
                string.IsNullOrWhiteSpace(parsed.Message)
                    ? $"Login failed (statusCode={parsed.StatusCode})."
                    : parsed.Message, null);
        }

        if (string.IsNullOrWhiteSpace(body))
            return new AuthLoginResult(false, $"Login failed: empty response (HTTP {(int)response.StatusCode} {response.StatusCode}).", null);

        return new AuthLoginResult(false, $"Login failed: unexpected response body (HTTP {(int)response.StatusCode} {response.StatusCode}).", null);
    }

    private static LoginResponse? TryParseLoginResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<LoginResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LoginRequest(string Email, string GroupID, string Password);
    private sealed record LoginResponse(bool Succeeded, int StatusCode, string Message);
}
