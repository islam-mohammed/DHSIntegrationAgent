using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.Application.Services;

public sealed class DeleteBatchService : IDeleteBatchService
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IBatchClient _batchClient;
    private readonly IBatchRegistry _batchRegistry;

    public DeleteBatchService(
        ISqliteUnitOfWorkFactory unitOfWorkFactory,
        IBatchClient batchClient,
        IBatchRegistry batchRegistry)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _batchClient = batchClient ?? throw new ArgumentNullException(nameof(batchClient));
        _batchRegistry = batchRegistry ?? throw new ArgumentNullException(nameof(batchRegistry));
    }

    public async Task<DeleteBatchProcessResult> DeleteBatchAsync(long? localBatchId, string? bcrId, CancellationToken ct)
    {
        if (localBatchId.HasValue && _batchRegistry.IsRegistered(localBatchId.Value))
        {
            return new DeleteBatchProcessResult(false, "Cannot delete a batch that is currently being processed.");
        }

        try
        {
            // Register batch to prevent concurrent operations
            if (localBatchId.HasValue)
            {
                _batchRegistry.Register(localBatchId.Value);
            }

            // 1. Delete from server if BcrId exists
            if (!string.IsNullOrEmpty(bcrId) && int.TryParse(bcrId, out int bcrIdInt) && bcrIdInt > 0)
            {
                var delResult = await _batchClient.DeleteBatchAsync(bcrIdInt, ct);
                if (!delResult.Succeeded)
                {
                    return new DeleteBatchProcessResult(false, $"Failed to delete batch from server: {delResult.Message}");
                }
            }

            // 2. Delete locally
            if (localBatchId.HasValue)
            {
                await using var uow = await _unitOfWorkFactory.CreateAsync(ct);
                await uow.Batches.UpdateStatusAsync(
                    localBatchId.Value,
                    DHSIntegrationAgent.Domain.WorkStates.BatchStatus.Deleted,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    ct);
                await uow.CommitAsync(ct);
            }

            return new DeleteBatchProcessResult(true, null);
        }
        catch (Exception ex)
        {
            return new DeleteBatchProcessResult(false, $"An unexpected error occurred while deleting the batch: {ex.Message}");
        }
        finally
        {
            if (localBatchId.HasValue)
            {
                _batchRegistry.Unregister(localBatchId.Value);
            }
        }
    }
}
