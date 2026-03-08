using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Workers;

namespace DHSIntegrationAgent.Application.Abstractions;

public interface IDispatchService
{
    Task ProcessBatchSenderAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct);
    Task<RetryBatchResult> RetryBatchAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct);
    Task<RetryBatchResult> AutomaticRetryBatchAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct);
    Task<ManualRetryResult> ManualRetryDispatchPacketAsync(string dispatchId, IProgress<WorkerProgressReport> progress, CancellationToken ct);
    Task<ManualRetryResult> ResumeBatchAsync(long batchId, IProgress<WorkerProgressReport> progress, CancellationToken ct);
}

public sealed record RetryBatchResult(int TotalRetried, int SuccessCount, int FailedCount);
public sealed record ManualRetryResult(bool Succeeded, string? ErrorMessage, int RetriedCount, int SuccessCount, int FailedCount);
