namespace DHSIntegrationAgent.Application.Observability;

public sealed record ApiCallRecord(
    string CorrelationId,
    string HttpMethod,
    string Url,
    DateTimeOffset StartedUtc,
    long ElapsedMs,
    int? StatusCode,
    long? RequestBytes,
    long? ResponseBytes,
    string? ErrorType,
    string? ErrorMessage
);
