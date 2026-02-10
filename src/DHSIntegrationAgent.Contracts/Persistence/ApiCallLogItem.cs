namespace DHSIntegrationAgent.Contracts.Persistence;

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
