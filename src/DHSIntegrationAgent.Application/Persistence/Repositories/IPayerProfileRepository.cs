using DHSIntegrationAgent.Contracts.Persistence;

﻿namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IPayerProfileRepository
{
    Task UpsertManyAsync(
        IReadOnlyList<PayerProfileRow> rows,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PayerProfileRow>> ListActiveAsync(string providerDhsCode, CancellationToken cancellationToken);

    Task SetActiveAsync(string providerDhsCode, string companyCode, bool isActive, DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayerItem>> GetAllPayersAsync(string providerDhsCode, CancellationToken cancellationToken);

    Task<PayerProfileRow?> GetByCompanyCodeAsync(string providerDhsCode, string companyCode, CancellationToken cancellationToken);
}
