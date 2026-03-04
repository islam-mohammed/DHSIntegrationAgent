using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IDispatchRepository
{
    Task InsertDispatchAsync(
        string dispatchId,
        string providerDhsCode,
        long batchId,
        string? bcrId,
        int sequenceNo,
        DispatchType dispatchType,
        DispatchStatus dispatchStatus,
        string? correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task UpdateDispatchResultAsync(
        string dispatchId,
        DispatchStatus dispatchStatus,
        int? httpStatusCode,
        string? lastError,
        string? correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task IncrementAttemptAndScheduleRetryAsync(
        string dispatchId,
        DateTimeOffset utcNow,
        TimeSpan? nextRetryDelay,
        CancellationToken cancellationToken);

    Task RecoverInFlightAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task<int> GetNextSequenceNoAsync(long batchId, CancellationToken cancellationToken);

    Task<Contracts.Persistence.DispatchRow?> GetAsync(string dispatchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Contracts.Persistence.DispatchRow>> GetRecentForBatchAsync(long batchId, int limit, CancellationToken cancellationToken);

    Task<bool> HasRecentRetryAsync(long batchId, IReadOnlyList<int> proIdClaims, DateTimeOffset sinceUtc, CancellationToken cancellationToken);
}
