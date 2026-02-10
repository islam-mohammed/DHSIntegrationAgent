namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// WBS 2.4 Batch API client contract.
/// POST /api/Batch/CreateBatchRequest (gzip JSON).
/// GET /api/Batch/GetBatchRequest/{providerDhsCode} to retrieve batch requests.
///
/// IMPORTANT:
/// totalClaims MUST be supplied by upstream logic:
/// the count of claims fetched from provider DB filtered by
/// (companyCode, batchStartDate, batchEndDate).
/// </summary>
public interface IBatchClient
{
    /// <summary>
    /// Calls POST /api/Batch/CreateBatchRequest with body:
    /// { "batchRequests": [ { companyCode, batchStartDate, batchEndDate, totalClaims, providerDhsCode } ] }
    /// </summary>
    Task<CreateBatchResult> CreateBatchAsync(
        IReadOnlyCollection<CreateBatchRequestItem> batchRequests,
        CancellationToken ct);

    /// <summary>
    /// Gets batch requests for a provider (GET /api/Batch/GetBatchRequest/{providerDhsCode}).
    /// Supports optional filtering by month, year, and payer with pagination.
    /// </summary>
    Task<GetBatchRequestResult> GetBatchRequestAsync(
        string providerDhsCode,
        int? month = null,
        int? year = null,
        short? payerId = null,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken ct = default);
}

/// <summary>
/// Single batch request item (goes inside request.batchRequests[]).
/// </summary>
public sealed record CreateBatchRequestItem(
    string CompanyCode,
    DateTimeOffset BatchStartDate,
    DateTimeOffset BatchEndDate,
    int TotalClaims,
    string ProviderDhsCode
);

/// <summary>
/// Normalized response for CreateBatchRequest.
///
/// Tests (and current backend expectation in this solution) require:
/// { succeeded, statusCode, message, batchId }
/// </summary>
public sealed record CreateBatchResult(
    bool Succeeded,
    int StatusCode,
    int BatchId,
    string? ErrorMessage
);

/// <summary>
/// Response for GetBatchRequest with paginated data.
/// </summary>
public sealed record GetBatchRequestResult(
    bool Succeeded,
    int StatusCode,
    string? Message,
    IReadOnlyList<string>? Errors,
    IReadOnlyList<BatchRequestItem>? Data,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage
);

/// <summary>
/// Single batch request item from API response.
/// </summary>
public sealed record BatchRequestItem(
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
