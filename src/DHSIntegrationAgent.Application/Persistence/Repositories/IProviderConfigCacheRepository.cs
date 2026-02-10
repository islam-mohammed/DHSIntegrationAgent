using DHSIntegrationAgent.Contracts.Persistence;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IProviderConfigCacheRepository
{
    Task<ProviderConfigCacheRow?> GetValidAsync(string providerDhsCode, DateTimeOffset utcNow, CancellationToken cancellationToken);
    Task UpsertAsync(ProviderConfigCacheRow row, CancellationToken cancellationToken);
    Task SetErrorAsync(string providerDhsCode, string lastError, DateTimeOffset utcNow, CancellationToken cancellationToken);
    Task<ProviderConfigCacheRow?> GetAnyAsync(string providerDhsCode, CancellationToken cancellationToken);

}
