using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Contracts.Claims;

public sealed record ClaimSpecificLeaseRequest(
    string ProviderDhsCode,
    IReadOnlyList<int> ProIdClaims,
    string LockedBy,
    DateTimeOffset UtcNow,
    DateTimeOffset LeaseUntilUtc,
    IReadOnlyList<EnqueueStatus> EligibleEnqueueStatuses
);
