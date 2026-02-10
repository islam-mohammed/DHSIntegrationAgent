using DHSIntegrationAgent.Contracts.Persistence;

﻿namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IProviderProfileRepository
{
    Task UpsertAsync(
        ProviderProfileRow row,
        CancellationToken cancellationToken);

    Task<ProviderProfileRow?> GetAsync(ProviderKey key, CancellationToken cancellationToken);

    Task<ProviderProfileRow?> GetActiveByProviderDhsCodeAsync(string providerDhsCode, CancellationToken cancellationToken);

    Task SetActiveAsync(ProviderKey key, bool isActive, DateTimeOffset utcNow, CancellationToken cancellationToken);
}
