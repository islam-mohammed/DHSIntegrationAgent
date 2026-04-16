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
    int? ClaimSplitFollowupDays = null,
    string? DefaultTreatmentCountryCode = null,
    string? DefaultSubmissionReasonCode = null,
    string? DefaultPriority = null,
    int? DefaultErPbmDuration = null,
    string? SfdaServiceTypeIdentifier = null,
    string? PayersToDropZeroAmountServicesCsv = null);
