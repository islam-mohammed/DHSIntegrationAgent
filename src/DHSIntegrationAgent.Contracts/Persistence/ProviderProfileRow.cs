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
    DateTimeOffset UpdatedUtc,
    byte[]? EncryptedBlobStorageConnectionString = null,
    string? BlobStorageContainerName = null,
    int FetchClaimCountPerThread = 300,
    string? PayersToDropZeroAmountServicesCsv = null,
    int ClaimSplitFollowupDays = 14,
    string? DefaultTreatmentCountryCode = null,
    string? DefaultSubmissionReasonCode = null,
    string? DefaultPriority = null,
    int DefaultErPbmDuration = 1,
    string? SfdaServiceTypeIdentifier = null,
    string? DescriptorJson = null);
