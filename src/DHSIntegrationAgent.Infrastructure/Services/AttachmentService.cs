using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Persistence;
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
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private string? _cachedSasUrl;

    public AttachmentService(
        IOptions<AzureBlobOptions> options,
        IKeyProtector protector,
        ILogger<AttachmentService> logger,
        ISqliteUnitOfWorkFactory uowFactory)
    {
        _options = options.Value;
        _protector = protector;
        _logger = logger;
        _uowFactory = uowFactory;
    }

    public async Task<string> UploadAsync(AttachmentRow attachment, CancellationToken ct)
    {
        BlobContainerClient containerClient;

        if (!string.IsNullOrWhiteSpace(_options.AttachmentBlobStorageCon))
        {
            var serviceClient = new BlobServiceClient(_options.AttachmentBlobStorageCon);
            containerClient = serviceClient.GetBlobContainerClient(_options.AttachmentBlobStorageContainer);
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
        IDisposable? networkConnection = null;

        try
        {
            if (attachment.LocationBytesPlaintext != null)
            {
                uploadStream = new MemoryStream(attachment.LocationBytesPlaintext);
            }
            else if (attachment.AttachBitBase64Plaintext != null)
            {
                uploadStream = new MemoryStream(attachment.AttachBitBase64Plaintext);
            }
            else if (!string.IsNullOrWhiteSpace(attachment.LocationPathPlaintext))
            {
                // Try to authenticate if it's a network path
                try
                {
                    await using var uow = await _uowFactory.CreateAsync(ct);
                    var settings = await uow.AppSettings.GetAsync(ct);

                    if (!string.IsNullOrWhiteSpace(settings.NetworkUsername))
                    {
                        var root = Path.GetPathRoot(attachment.LocationPathPlaintext);
                        if (!string.IsNullOrEmpty(root) && root.StartsWith(@"\\"))
                        {
                            string password = "";
                            if (settings.NetworkPasswordEncrypted != null && settings.NetworkPasswordEncrypted.Length > 0)
                            {
                                var decrypted = _protector.Unprotect(settings.NetworkPasswordEncrypted);
                                password = Encoding.UTF8.GetString(decrypted);
                            }

                            // Remove trailing slash for share root if present, though GetPathRoot typically returns \\server\share
                            var resource = root.TrimEnd('\\');
                            networkConnection = NetworkShareAccessor.Connect(resource, settings.NetworkUsername, password);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to establish network connection for {Path}", attachment.LocationPathPlaintext);
                }

                if (File.Exists(attachment.LocationPathPlaintext))
                {
                    uploadStream = File.OpenRead(attachment.LocationPathPlaintext);
                }
            }

            if (uploadStream == null)
            {
                throw new InvalidOperationException($"No source content found for attachment {attachment.AttachmentId}");
            }

            using (uploadStream)
            {
                await blobClient.UploadAsync(uploadStream, overwrite: true, ct);
            }
        }
        finally
        {
            networkConnection?.Dispose();
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
