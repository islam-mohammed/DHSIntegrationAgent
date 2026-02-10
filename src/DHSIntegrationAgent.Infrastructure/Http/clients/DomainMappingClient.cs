using System.Net;
using System.Text;
using System.Text.Json;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

public sealed class DomainMappingClient : IDomainMappingClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ApiOptions>? _apiOptions;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DomainMappingClient(IHttpClientFactory httpClientFactory, IOptions<ApiOptions>? apiOptions = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _apiOptions = apiOptions;
    }

    public async Task<InsertMissMappingDomainResult> InsertMissMappingDomainAsync(
        InsertMissMappingDomainRequest request,
        CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.ProviderDhsCode))
            return FailInsert("providerDhsCode is required.", 400);

        if (request.MismappedItems is null || request.MismappedItems.Count == 0)
            return FailInsert("mismappedItems must contain at least one item.", 400);

        foreach (var item in request.MismappedItems)
        {
            if (string.IsNullOrWhiteSpace(item.ProviderCodeValue))
                return FailInsert("mismappedItems.providerCodeValue is required.", 400);
            if (string.IsNullOrWhiteSpace(item.ProviderNameValue))
                return FailInsert("mismappedItems.providerNameValue is required.", 400);
            if (item.DomainTableId < 0)
                return FailInsert("mismappedItems.domainTableId must be >= 0.", 400);
        }

        var client = _httpClientFactory.CreateClient("BackendApi");

        var useGzip = _apiOptions?.Value.UseGzipPostRequests ?? true;

        using HttpContent content = useGzip
            ? (HttpContent)GzipJsonHttpContent.Create(request, RequestJsonOptions)
            : new StringContent(JsonSerializer.Serialize(request, RequestJsonOptions), Encoding.UTF8, "application/json");


        using var resp = await client.PostAsync("api/DomainMapping/InsertMissMappingDomain", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        // NOTE: Service might return structured JSON even on 500; parse payload if present.
        if (resp.StatusCode == HttpStatusCode.OK)
            return ParseInsert(body);

        // Try parse payload anyway (required by WBS 2.7 tests)
        var parsed = ParseInsert(body);
        if (parsed.Succeeded || parsed.Data is not null || (parsed.Errors is not null && parsed.Errors.Count > 0))
            return parsed with { StatusCode = (int)resp.StatusCode };

        return FailInsert($"InsertMissMappingDomain failed (HTTP {(int)resp.StatusCode}).", (int)resp.StatusCode);
    }

    public async Task<GetMissingDomainMappingsResult> GetMissingDomainMappingsAsync(string providerDhsCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerDhsCode))
            return FailMissing("providerDhsCode is required.", 400);

        var client = _httpClientFactory.CreateClient("BackendApi");

        var path = $"api/DomainMapping/GetMissingDomainMappings/{Uri.EscapeDataString(providerDhsCode)}";
        using var resp = await client.GetAsync(path, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == HttpStatusCode.OK)
            return ParseMissing(body);

        // Try parse payload anyway
        var parsed = ParseMissing(body);
        if (parsed.Succeeded || parsed.Data is not null || (parsed.Errors is not null && parsed.Errors.Count > 0))
            return parsed with { StatusCode = (int)resp.StatusCode };

        return FailMissing($"GetMissingDomainMappings failed (HTTP {(int)resp.StatusCode}).", (int)resp.StatusCode);
    }

    private static InsertMissMappingDomainResult ParseInsert(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<InsertMissMappingDomainResult>(json, JsonOptions);
            return result ?? FailInsert("Failed to deserialize API response.", 500);
        }
        catch (Exception ex)
        {
            return FailInsert($"Failed to deserialize API response: {ex.Message}", 500);
        }
    }

    private static GetMissingDomainMappingsResult ParseMissing(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<GetMissingDomainMappingsResult>(json, JsonOptions);
            return result ?? FailMissing("Failed to deserialize API response.", 500);
        }
        catch (Exception ex)
        {
            return FailMissing($"Failed to deserialize API response: {ex.Message}", 500);
        }
    }

    private static InsertMissMappingDomainResult FailInsert(string error, int statusCode)
        => new(
            Succeeded: false,
            StatusCode: statusCode,
            Message: error,
            Errors: new List<string> { error },
            Data: new InsertMissMappingDomainData(
                TotalItems: 0,
                InsertedCount: 0,
                SkippedCount: 0,
                SkippedItems: Array.Empty<string>()));

    private static GetMissingDomainMappingsResult FailMissing(string error, int statusCode)
        => new(
            Succeeded: false,
            StatusCode: statusCode,
            Message: error,
            Errors: new List<string> { error },
            Data: null);
}
