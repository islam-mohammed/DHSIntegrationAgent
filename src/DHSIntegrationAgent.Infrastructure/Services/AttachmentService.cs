using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Infrastructure.Security;
using DHSIntegrationAgent.Contracts.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DHSIntegrationAgent.Infrastructure.Services;

public sealed class AttachmentService : IAttachmentService
{
    private readonly AzureBlobOptions _options;
    private readonly IKeyProtector _protector;
    private readonly ILogger<AttachmentService> _logger;
    private string? _cachedSasUrl;

    public AttachmentService(
        IOptions<AzureBlobOptions> options,
        IKeyProtector protector,
        ILogger<AttachmentService> logger)
    {
        _options = options.Value;
        _protector = protector;
        _logger = logger;
    }

    public async Task<string> UploadAsync(AttachmentRow attachment, CancellationToken ct)
    {
        BlobContainerClient containerClient;

        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            var serviceClient = new BlobServiceClient(_options.ConnectionString);
            containerClient = serviceClient.GetBlobContainerClient(_options.ContainerName);
        }
        else
        {
            var sasUrl = await GetSasUrlAsync(ct);
            containerClient = new BlobContainerClient(new Uri(sasUrl));
        }

        // Use FileName or generate one if missing
        var fileName = attachment.FileName ?? $"{attachment.AttachmentId}.dat";

        // Path structure: ProviderDhsCode/ProIdClaim/FileName
        var blobPath = $"{attachment.ProviderDhsCode}/{attachment.ProIdClaim}/{fileName}";
        var blobClient = containerClient.GetBlobClient(blobPath);

        Stream? uploadStream = null;

        if (attachment.LocationBytesPlaintext != null)
        {
            uploadStream = new MemoryStream(attachment.LocationBytesPlaintext);
        }
        else if (attachment.AttachBitBase64Plaintext != null)
        {
            uploadStream = new MemoryStream(attachment.AttachBitBase64Plaintext);
        }
        else if (!string.IsNullOrWhiteSpace(attachment.LocationPathPlaintext) && File.Exists(attachment.LocationPathPlaintext))
        {
            uploadStream = File.OpenRead(attachment.LocationPathPlaintext);
        }

        if (uploadStream == null)
        {
            throw new InvalidOperationException($"No source content found for attachment {attachment.AttachmentId}");
        }

        using (uploadStream)
        {
            await blobClient.UploadAsync(uploadStream, overwrite: true, ct);
        }

        return blobClient.Uri.ToString();
    }

    private async Task<string> GetSasUrlAsync(CancellationToken ct)
    {
        if (_cachedSasUrl != null) return _cachedSasUrl;

        if (!string.IsNullOrWhiteSpace(_options.SasUrlEncryptedFilePath) && File.Exists(_options.SasUrlEncryptedFilePath))
        {
            try
            {
                var encryptedBytes = await File.ReadAllBytesAsync(_options.SasUrlEncryptedFilePath, ct);
                var decryptedBytes = _protector.Unprotect(encryptedBytes);
                _cachedSasUrl = Encoding.UTF8.GetString(decryptedBytes);
                return _cachedSasUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt SAS URL from file {FilePath}", _options.SasUrlEncryptedFilePath);
                throw;
            }
        }

        throw new InvalidOperationException("Azure Blob SAS URL or Connection String is not configured.");
    }
}
