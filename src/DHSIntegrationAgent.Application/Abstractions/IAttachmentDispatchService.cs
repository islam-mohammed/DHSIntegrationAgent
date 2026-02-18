using DHSIntegrationAgent.Contracts.Workers;

namespace DHSIntegrationAgent.Application.Abstractions;

public interface IAttachmentDispatchService
{
    Task ProcessAttachmentsForBatchesAsync(IEnumerable<long> batchIds, IProgress<WorkerProgressReport> progress, CancellationToken ct);
}
