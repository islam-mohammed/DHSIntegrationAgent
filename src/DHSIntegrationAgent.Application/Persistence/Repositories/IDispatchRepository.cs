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
}
