using DHSIntegrationAgent.Contracts.Claims;
using DHSIntegrationAgent.Contracts.Persistence;
﻿using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IClaimRepository
{
    // Stream A staging support (no business logic; just persistence)
    Task UpsertStagedAsync(
        string providerDhsCode,
        int proIdClaim,
        string companyCode,
        string monthKey,
        long? batchId,
        string? bcrId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    // Atomic lease acquisition (single UPDATE predicate)
    Task<IReadOnlyList<ClaimKey>> LeaseAsync(ClaimLeaseRequest request, CancellationToken cancellationToken);

    Task ReleaseLeaseAsync(IReadOnlyList<ClaimKey> claims, DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task MarkEnqueuedAsync(
        IReadOnlyList<ClaimKey> claims,
        string? bcrId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        IReadOnlyList<ClaimKey> claims,
        string errorMessage,
        DateTimeOffset utcNow,
        TimeSpan? nextRetryDelay,
        CancellationToken cancellationToken);

    Task SetCompletionStatusAsync(
        IReadOnlyList<ClaimKey> claims,
        CompletionStatus completionStatus,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task IncrementAttemptAsync(IReadOnlyList<ClaimKey> claims, DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task RecoverInFlightAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
}
