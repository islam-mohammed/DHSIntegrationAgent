using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record DispatchRow(
    string DispatchId,
    string ProviderDhsCode,
    long BatchId,
    string? BcrId,
    int SequenceNo,
    DispatchType DispatchType,
    DispatchStatus DispatchStatus,
    int AttemptCount,
    DateTimeOffset? NextRetryUtc,
    int? RequestSizeBytes,
    bool RequestGzip,
    int? HttpStatusCode,
    string? LastError,
    string? CorrelationId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc
);
