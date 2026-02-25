using DHSIntegrationAgent.Contracts.Persistence;
﻿using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IBatchRepository
{
    Task<long?> TryGetBatchIdAsync(BatchKey key, CancellationToken cancellationToken);

    Task<BatchRow?> GetByIdAsync(long batchId, CancellationToken cancellationToken);

    Task<BatchRow?> GetByBcrIdAsync(string bcrId, CancellationToken cancellationToken);

    Task<long> EnsureBatchAsync(
        BatchKey key,
        BatchStatus batchStatus,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task SetBcrIdAsync(long batchId, string bcrId, DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task UpdateStatusAsync(
        long batchId,
        BatchStatus status,
        bool? hasResume,
        string? lastError,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken,
        string? payerCode = null);

    Task<IReadOnlyList<BatchRow>> ListByStatusAsync(BatchStatus status, CancellationToken cancellationToken);

    Task UpdateProgressAsync(
        long batchId,
        int processed,
        int total,
        int percentage,
        string? message,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
