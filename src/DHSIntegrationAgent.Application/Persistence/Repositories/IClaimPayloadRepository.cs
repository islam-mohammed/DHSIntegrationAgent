using DHSIntegrationAgent.Contracts.Persistence;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IClaimPayloadRepository
{
    Task UpsertAsync(
        ClaimPayloadRow row,
        CancellationToken cancellationToken);

    Task<ClaimPayloadRow?> GetAsync(ClaimKey key, CancellationToken cancellationToken);

    IAsyncEnumerable<byte[]> GetPayloadsByBatchAsync(long batchId, CancellationToken cancellationToken);
}
