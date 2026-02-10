using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record ClaimLeaseRequest(
    string ProviderDhsCode,
    string LockedBy,
    DateTimeOffset UtcNow,
    DateTimeOffset LeaseUntilUtc,
    int Take,
    IReadOnlyList<EnqueueStatus> EligibleEnqueueStatuses,
    bool RequireRetryDue
);
