using DHSIntegrationAgent.Contracts.Persistence;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IProviderExtractionConfigRepository
{
    Task UpsertAsync(ProviderExtractionConfigRow row, CancellationToken cancellationToken);
    Task<ProviderExtractionConfigRow?> GetAsync(ProviderKey key, CancellationToken cancellationToken);
}
