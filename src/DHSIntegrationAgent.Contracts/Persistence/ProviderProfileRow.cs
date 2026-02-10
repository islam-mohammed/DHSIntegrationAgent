namespace DHSIntegrationAgent.Contracts.Persistence;

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
