namespace DHSIntegrationAgent.Application.Configuration;

public sealed class CryptoOptions
{
    /// <summary>
    /// WBS 1.3: AES-GCM column encryption for PHI columns.
    /// Keep true in all non-dev environments.
    /// </summary>
    public bool EnableAtRestColumnEncryption { get; init; } = true;

    public bool EncryptClaimPayload { get; init; } = true;
    public bool EncryptAttachmentContent { get; init; } = true;
    public bool EncryptAttachmentPathsAndUrls { get; init; } = true;
    public bool EncryptValidationRawValue { get; init; } = true;
}
