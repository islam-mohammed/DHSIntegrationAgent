namespace DHSIntegrationAgent.Contracts.Observability;

public sealed record ApiCallRecord(
    string CorrelationId,
    string HttpMethod,
    string Url,
    string EndpointName,
    string? ProviderDhsCode,
    DateTimeOffset StartedUtc,
    DateTimeOffset? ResponseUtc,
    long ElapsedMs,
    int? StatusCode,
    bool Succeeded,
    string? ErrorType,
    string? ErrorMessage,
    long? RequestBytes,
    long? ResponseBytes,
    bool WasGzipRequest
);
