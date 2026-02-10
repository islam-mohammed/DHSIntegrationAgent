using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record AttachmentRow(
     string AttachmentId,
    string ProviderDhsCode,
    int ProIdClaim,
    AttachmentSourceType SourceType,
    string? LocationPathPlaintext,            // repo encrypts into LocationPathEncrypted
    byte[]? LocationBytesPlaintext,           // repo encrypts into LocationBytesEncrypted
    byte[]? AttachBitBase64Plaintext,         // repo encrypts into AttachBitBase64Encrypted
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    string? Sha256,
    string? OnlineUrlPlaintext,               // repo encrypts into OnlineUrlEncrypted
    UploadStatus UploadStatus,
    int AttemptCount,
    DateTimeOffset? NextRetryUtc,
    string? LastError,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public interface IAttachmentRepository
{
    Task UpsertAsync(AttachmentRow row, CancellationToken cancellationToken);

    Task UpdateStatusAsync(
        string attachmentId,
        UploadStatus status,
        string? onlineUrl,
        string? lastError,
        DateTimeOffset utcNow,
        TimeSpan? nextRetryDelay,
        CancellationToken cancellationToken);
}
