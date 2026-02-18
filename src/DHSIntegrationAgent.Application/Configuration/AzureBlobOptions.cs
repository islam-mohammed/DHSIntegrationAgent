namespace DHSIntegrationAgent.Application.Configuration;

public sealed class AzureBlobOptions
{
    public string SasUrl { get; init; } = "";

    /// <summary>
    /// Optional path to a file containing the encrypted SAS URL.
    /// If SasUrl is empty, the service will attempt to read and decrypt from this file.
    /// </summary>
    public string? SasUrlEncryptedFilePath { get; init; }

    public string ConnectionString { get; init; } = "";
    public string ContainerName { get; init; } = "claimattachment";
}
