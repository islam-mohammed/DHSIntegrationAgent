using DHSIntegrationAgent.Contracts.Providers;
﻿using System.Net;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

/// <summary>
/// Calls backend Provider API to load configuration.
/// WBS 2.3 endpoint:
/// GET /api/Provider/GetProviderConfigration/{providerDhsCode}
/// </summary>
public sealed class ProviderConfigurationClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ProviderConfigurationClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    // Public because this client is injected into services and may be used from tests/projects outside Infrastructure.
    // Return type must be equally accessible => ProviderConfigHttpResult is public below.
    public async Task<ProviderConfigHttpResult> GetAsync(string providerDhsCode, string? etag, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("BackendApi");

        var path = $"api/Provider/GetProviderConfigration/{Uri.EscapeDataString(providerDhsCode)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (!string.IsNullOrWhiteSpace(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        // FIX: 304 enum member is NotModified (not IsNotModified)
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return ProviderConfigHttpResult.NotModified(response.Headers.ETag?.Tag ?? etag);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return ProviderConfigHttpResult.Failed($"Provider config failed (HTTP {(int)response.StatusCode}).");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var newEtag = response.Headers.ETag?.Tag;

        return ProviderConfigHttpResult.Ok(json, newEtag);
    }

    // Public because GetAsync is public and returns it.
    public sealed record ProviderConfigHttpResult(
        bool Success,
        bool IsNotModified,
        string? Json,
        string? ETag,
        string? Error)
    {
        public static ProviderConfigHttpResult Ok(string json, string? etag)
            => new(true, false, json, etag, null);

        public static ProviderConfigHttpResult NotModified(string? etag)
            => new(true, true, null, etag, null);

        public static ProviderConfigHttpResult Failed(string error)
            => new(false, false, null, null, error);
    }
}
