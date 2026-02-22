namespace DHSIntegrationAgent.Application.Configuration;

public sealed class AzureBlobOptions
{
    /// <summary>
    /// Optional path to a file containing the encrypted SAS URL.
    /// The service will attempt to read and decrypt from this file.
    /// </summary>
    public string? SasUrlEncryptedFilePath { get; init; }

    public string ConnectionString { get; init; } = "";
    public string ContainerName { get; init; } = "claimattachment";
}
