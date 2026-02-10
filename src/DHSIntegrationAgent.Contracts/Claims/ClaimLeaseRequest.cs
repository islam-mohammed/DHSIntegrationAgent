using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Contracts.Claims;

public sealed record ClaimLeaseRequest(
    string ProviderDhsCode,
    string LockedBy,
    DateTimeOffset UtcNow,
    DateTimeOffset LeaseUntilUtc,
    int Take,
    IReadOnlyList<EnqueueStatus> EligibleEnqueueStatuses,
    bool RequireRetryDue
);
