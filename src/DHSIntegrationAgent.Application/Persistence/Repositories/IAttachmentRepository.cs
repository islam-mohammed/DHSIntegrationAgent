using DHSIntegrationAgent.Contracts.Persistence;
﻿using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IAttachmentRepository
{
    Task UpsertAsync(AttachmentRow row, CancellationToken cancellationToken);

    Task UpdateStatusAsync(
        string attachmentId,
        UploadStatus status,
        string? onlineUrl,
        string? lastError,
        DateTimeOffset utcNow,
        TimeSpan? nextRetryDelay,
        CancellationToken cancellationToken,
        long? sizeBytes = null);

    Task<IReadOnlyList<AttachmentRow>> GetByClaimAsync(string providerDhsCode, int proIdClaim, CancellationToken ct);

    Task<IReadOnlyList<AttachmentRow>> GetByBatchAsync(long batchId, CancellationToken ct);
    Task<int> CountByBatchAsync(long batchId, CancellationToken ct);
}
