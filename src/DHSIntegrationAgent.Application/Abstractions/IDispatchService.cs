using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Workers;

namespace DHSIntegrationAgent.Application.Abstractions;

public interface IDispatchService
{
    Task ProcessBatchSenderAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct);
    Task<RetryBatchResult> RetryBatchAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct);
}

public sealed record RetryBatchResult(int TotalRetried, int SuccessCount, int FailedCount);
