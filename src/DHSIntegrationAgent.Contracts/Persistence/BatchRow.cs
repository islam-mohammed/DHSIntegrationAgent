using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record BatchRow(
    long BatchId,
    string ProviderDhsCode,
    string CompanyCode,
    string? PayerCode,
    string MonthKey,
    DateTimeOffset? StartDateUtc,
    DateTimeOffset? EndDateUtc,
    string? BcrId,
    BatchStatus BatchStatus,
    bool HasResume,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string? LastError
);
