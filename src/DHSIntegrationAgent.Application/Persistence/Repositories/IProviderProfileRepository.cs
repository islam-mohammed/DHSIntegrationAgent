namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record ProviderProfileRow(
    string ProviderCode,
    string ProviderDhsCode,
    string DbEngine,
    string IntegrationType,
    byte[] EncryptedConnectionString,
    string? EncryptionKeyId,
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public interface IProviderProfileRepository
{
    Task UpsertAsync(
        ProviderProfileRow row,
        CancellationToken cancellationToken);

    Task<ProviderProfileRow?> GetAsync(ProviderKey key, CancellationToken cancellationToken);

    Task<ProviderProfileRow?> GetActiveByProviderDhsCodeAsync(string providerDhsCode, CancellationToken cancellationToken);

    Task SetActiveAsync(ProviderKey key, bool isActive, DateTimeOffset utcNow, CancellationToken cancellationToken);
}
