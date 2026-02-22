namespace DHSIntegrationAgent.Application.Configuration;

public sealed class AzureBlobOptions
{
    /// <summary>
    /// Optional path to a file containing the encrypted SAS URL.
    /// The service will attempt to read and decrypt from this file.
    /// </summary>
    public string? SasUrlEncryptedFilePath { get; init; }

    public string AttachmentBlobStorageCon { get; init; } = "";
    public string AttachmentBlobStorageContainer { get; init; } = "claimattachment";

    // Compatibility properties
    public string ConnectionString => AttachmentBlobStorageCon;
    public string ContainerName => AttachmentBlobStorageContainer;
}
