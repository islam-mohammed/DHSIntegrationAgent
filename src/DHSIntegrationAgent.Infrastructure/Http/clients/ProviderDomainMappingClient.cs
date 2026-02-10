using System.Net;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

/// <summary>
/// Calls backend DomainMapping API.
/// Change Request endpoint:
/// GET /api/DomainMapping/GetProviderDomainMapping/{providerDhsCode}
/// </summary>
public sealed class ProviderDomainMappingClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ProviderDomainMappingClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DomainMappingHttpResult> GetProviderDomainMappingAsync(string providerDhsCode, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("BackendApi");

        var path = $"api/DomainMapping/GetProviderDomainMapping/{Uri.EscapeDataString(providerDhsCode)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return DomainMappingHttpResult.Failed($"GetProviderDomainMapping failed (HTTP {(int)response.StatusCode}).");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return DomainMappingHttpResult.Ok(json);
    }

    public sealed record DomainMappingHttpResult(
        bool Success,
        string? Json,
        string? Error)
    {
        public static DomainMappingHttpResult Ok(string json) => new(true, json, null);
        public static DomainMappingHttpResult Failed(string error) => new(false, null, error);
    }
}
