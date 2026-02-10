namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IApiCallLogRepository
{
    Task InsertAsync(
        string endpointName,
        string? correlationId,
        DateTimeOffset requestUtc,
        DateTimeOffset? responseUtc,
        int? durationMs,
        int? httpStatusCode,
        bool succeeded,
        string? errorMessage,
        long? requestBytes,
        long? responseBytes,
        bool wasGzipRequest,
        string? providerDhsCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiCallLogItem>> GetRecentApiCallsAsync(int limit, CancellationToken cancellationToken);
}

public record ApiCallLogItem(
    int ApiCallLogId,
    string? ProviderDhsCode,
    string EndpointName,
    string? CorrelationId,
    DateTimeOffset RequestUtc,
    DateTimeOffset? ResponseUtc,
    int? DurationMs,
    int? HttpStatusCode,
    bool Succeeded,
    string? ErrorMessage,
    long? RequestBytes,
    long? ResponseBytes,
    bool WasGzipRequest
);
