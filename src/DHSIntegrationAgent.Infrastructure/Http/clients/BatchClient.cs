using System.Net;
using System.Text;
using System.Text.Json;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Configuration;
using Microsoft.Extensions.Options;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

/// <summary>
/// WBS 2.4 Batch API client.
/// POST /api/Batch/CreateBatchRequest.
///
/// Test contract:
/// - gzip controlled by ApiOptions.UseGzipPostRequests (tests do not run DI pipeline handlers)
/// - response contract JSON: { succeeded, statusCode, message, batchId }
/// - MUST parse JSON even if HTTP status is 500
/// </summary>
public sealed class BatchClient : IBatchClient
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

    public BatchClient(IHttpClientFactory httpClientFactory, IOptions<ApiOptions>? apiOptions = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _apiOptions = apiOptions;
    }

    public async Task<CreateBatchResult> CreateBatchAsync(
        IReadOnlyCollection<CreateBatchRequestItem> batchRequests,
        CancellationToken ct)
    {
        if (batchRequests is null || batchRequests.Count == 0)
            return new CreateBatchResult(false, 0, 0, "At least one batch request is required.");

        foreach (var item in batchRequests)
        {
            if (string.IsNullOrWhiteSpace(item.CompanyCode))
                return new CreateBatchResult(false, 0, 0, "companyCode is required.");

            if (string.IsNullOrWhiteSpace(item.ProviderDhsCode))
                return new CreateBatchResult(false, 0, 0, "providerDhsCode is required.");

            if (item.TotalClaims < 0)
                return new CreateBatchResult(false, 0, 0, "totalClaims must be >= 0.");

            if (item.BatchEndDate < item.BatchStartDate)
                return new CreateBatchResult(false, 0, 0, "batchEndDate must be >= batchStartDate.");
        }

        var client = _httpClientFactory.CreateClient("BackendApi");
        const string path = "api/Batch/CreateBatchRequest";

        // Exact shape expected by tests
        var requestBody = new CreateBatchRequestEnvelope(batchRequests);

        var gzipEnabled = _apiOptions?.Value.UseGzipPostRequests ?? true;

        HttpContent content;
        if (gzipEnabled)
        {
            // Tests expect Content-Encoding: gzip
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

        // Always parse JSON body if possible (even on HTTP 500)
        var parsed = TryParseResponse(body);
        if (parsed is not null)
        {
            var ok = parsed.Succeeded && parsed.StatusCode == 200;
            return new CreateBatchResult(
                Succeeded: ok,
                StatusCode: parsed.StatusCode,
                BatchId: parsed.BatchId,
                ErrorMessage: ok ? null : parsed.Message);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new CreateBatchResult(false, (int)response.StatusCode, 0,
                $"CreateBatch failed: empty response (HTTP {(int)response.StatusCode} {response.StatusCode}).");
        }

        return new CreateBatchResult(false, (int)response.StatusCode, 0,
            $"CreateBatch failed: unexpected response body (HTTP {(int)response.StatusCode} {response.StatusCode}).");
    }

    public async Task<GetBatchRequestResult> GetBatchRequestAsync(
        string providerDhsCode,
        int? month = null,
        int? year = null,
        short? payerId = null,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerDhsCode))
            return FailGetBatchRequest("providerDhsCode is required.", 0);

        var client = _httpClientFactory.CreateClient("BackendApi");

        // Build query string
        var queryParams = new List<string>
        {
            $"pageNumber={pageNumber}",
            $"pageSize={pageSize}"
        };

        if (month.HasValue)
            queryParams.Add($"month={month.Value}");

        if (year.HasValue)
            queryParams.Add($"year={year.Value}");

        if (payerId.HasValue)
            queryParams.Add($"payerId={payerId.Value}");

        var queryString = string.Join("&", queryParams);
        var path = $"api/Batch/GetBatchRequest/{Uri.EscapeDataString(providerDhsCode)}?{queryString}";

        try
        {
            using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return FailGetBatchRequest(
                        $"API returned 404 Not Found for endpoint: {path}\n" +
                        $"ProviderDhsCode: '{providerDhsCode}'\n" +
                        "Please verify the API endpoint and provider code.",
                        (int)response.StatusCode);
                }

                return FailGetBatchRequest(
                    $"API request failed with status {(int)response.StatusCode} {response.StatusCode}",
                    (int)response.StatusCode);
            }

            var parsed = TryParseGetBatchResponse(body);
            if (parsed is not null)
            {
                var items = parsed.Data?.Data?.Select(d => new BatchRequestItem(
                    BcrId: d.BcrId,
                    BcrCreatedOn: d.BcrCreatedOn,
                    BcrMonth: d.BcrMonth,
                    BcrYear: d.BcrYear,
                    UserName: d.UserName,
                    PayerNameEn: d.PayerNameEn,
                    PayerNameAr: d.PayerNameAr,
                    CompanyCode: d.CompanyCode,
                    MidTableTotalClaim: d.MidTableTotalClaim,
                    BatchStatus: d.BatchStatus
                )).ToList() ?? new List<BatchRequestItem>();

                return new GetBatchRequestResult(
                    Succeeded: parsed.Succeeded,
                    StatusCode: parsed.StatusCode,
                    Message: parsed.Message,
                    Errors: parsed.Errors,
                    Data: items,
                    PageNumber: parsed.Data?.PageNumber ?? 1,
                    PageSize: parsed.Data?.PageSize ?? 10,
                    TotalCount: parsed.Data?.TotalCount ?? 0,
                    TotalPages: parsed.Data?.TotalPages ?? 0,
                    HasPreviousPage: parsed.Data?.HasPreviousPage ?? false,
                    HasNextPage: parsed.Data?.HasNextPage ?? false
                );
            }

            return FailGetBatchRequest($"Failed to parse response: {body}", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return FailGetBatchRequest($"Network error: {ex.Message}", 0);
        }
        catch (Exception ex)
        {
            return FailGetBatchRequest($"Unexpected error: {ex.Message}", 0);
        }
    }

    private static GetBatchRequestResult FailGetBatchRequest(string message, int statusCode)
    {
        return new GetBatchRequestResult(
            Succeeded: false,
            StatusCode: statusCode,
            Message: message,
            Errors: new[] { message },
            Data: null,
            PageNumber: 1,
            PageSize: 10,
            TotalCount: 0,
            TotalPages: 0,
            HasPreviousPage: false,
            HasNextPage: false
        );
    }

    private static GetBatchResponse? TryParseGetBatchResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GetBatchResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static BatchResponse? TryParseResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<BatchResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record CreateBatchRequestEnvelope(IReadOnlyCollection<CreateBatchRequestItem> BatchRequests);

    private sealed record BatchResponse(bool Succeeded, int StatusCode, string Message, int BatchId);

    private sealed record GetBatchResponse(
        bool Succeeded,
        int StatusCode,
        string? Message,
        IReadOnlyList<string>? Errors,
        GetBatchDataEnvelope? Data
    );

    private sealed record GetBatchDataEnvelope(
        IReadOnlyList<GetBatchDataItem>? Data,
        int PageNumber,
        int PageSize,
        int TotalCount,
        int TotalPages,
        bool HasPreviousPage,
        bool HasNextPage
    );

    private sealed record GetBatchDataItem(
        int BcrId,
        DateTime BcrCreatedOn,
        int BcrMonth,
        int BcrYear,
        string? UserName,
        string? PayerNameEn,
        string? PayerNameAr,
        string? CompanyCode,
        int MidTableTotalClaim,
        string? BatchStatus
    );
}
