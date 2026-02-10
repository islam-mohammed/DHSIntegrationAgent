using System.Data.Common;
using System.Text;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class AttachmentRepository : SqliteRepositoryBase, IAttachmentRepository
{
    private readonly IColumnEncryptor _encryptor;

    public AttachmentRepository(DbConnection conn, DbTransaction tx, IColumnEncryptor encryptor)
        : base(conn, tx)
    {
        _encryptor = encryptor;
    }

    public async Task UpsertAsync(AttachmentRow row, CancellationToken cancellationToken)
    {
        // Encrypt PHI-adjacent fields before storing
        var locationBytesEncrypted = row.LocationBytesPlaintext is null
            ? null
            : await _encryptor.EncryptAsync(row.LocationBytesPlaintext, cancellationToken);

        var attachBitEncrypted = row.AttachBitBase64Plaintext is null
            ? null
            : await _encryptor.EncryptAsync(row.AttachBitBase64Plaintext, cancellationToken);

        var locationPathEncrypted = string.IsNullOrWhiteSpace(row.LocationPathPlaintext)
            ? null
            : await _encryptor.EncryptAsync(Encoding.UTF8.GetBytes(row.LocationPathPlaintext), cancellationToken);

        var onlineUrlEncrypted = string.IsNullOrWhiteSpace(row.OnlineUrlPlaintext)
            ? null
            : await _encryptor.EncryptAsync(Encoding.UTF8.GetBytes(row.OnlineUrlPlaintext), cancellationToken);

        await using var cmd = CreateCommand(
            """
            INSERT INTO Attachment
            (AttachmentId, ProviderDhsCode, ProIdClaim, AttachmentSourceType,
             LocationPath, LocationPathEncrypted,
             LocationBytesEncrypted, AttachBitBase64Encrypted,
             FileName, ContentType, SizeBytes, Sha256,
             OnlineURL, OnlineUrlEncrypted,
             UploadStatus, AttemptCount, NextRetryUtc, LastError, CreatedUtc, UpdatedUtc)
            VALUES
            ($id, $p, $cid, $st,
             NULL, $lpEnc,
             $lbEnc, $abEnc,
             $fn, $ct, $sz, $sha,
             NULL, $urlEnc,
             $us, $ac, $nru, $err, $c, $u)
            ON CONFLICT(AttachmentId)
            DO UPDATE SET
                AttachmentSourceType = excluded.AttachmentSourceType,
                LocationPath = NULL,
                LocationPathEncrypted = excluded.LocationPathEncrypted,
                LocationBytesEncrypted = excluded.LocationBytesEncrypted,
                AttachBitBase64Encrypted = excluded.AttachBitBase64Encrypted,
                FileName = excluded.FileName,
                ContentType = excluded.ContentType,
                SizeBytes = excluded.SizeBytes,
                Sha256 = excluded.Sha256,
                OnlineURL = NULL,
                OnlineUrlEncrypted = excluded.OnlineUrlEncrypted,
                UploadStatus = excluded.UploadStatus,
                AttemptCount = excluded.AttemptCount,
                NextRetryUtc = excluded.NextRetryUtc,
                LastError = excluded.LastError,
                UpdatedUtc = excluded.UpdatedUtc;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$id", row.AttachmentId);
        SqliteSqlBuilder.AddParam(cmd, "$p", row.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$cid", row.ProIdClaim);
        SqliteSqlBuilder.AddParam(cmd, "$st", (int)row.SourceType);

        SqliteSqlBuilder.AddParam(cmd, "$lpEnc", locationPathEncrypted);
        SqliteSqlBuilder.AddParam(cmd, "$lbEnc", locationBytesEncrypted);
        SqliteSqlBuilder.AddParam(cmd, "$abEnc", attachBitEncrypted);

        SqliteSqlBuilder.AddParam(cmd, "$fn", row.FileName);
        SqliteSqlBuilder.AddParam(cmd, "$ct", row.ContentType);
        SqliteSqlBuilder.AddParam(cmd, "$sz", row.SizeBytes);
        SqliteSqlBuilder.AddParam(cmd, "$sha", row.Sha256);

        SqliteSqlBuilder.AddParam(cmd, "$urlEnc", onlineUrlEncrypted);

        SqliteSqlBuilder.AddParam(cmd, "$us", (int)row.UploadStatus);
        SqliteSqlBuilder.AddParam(cmd, "$ac", row.AttemptCount);
        SqliteSqlBuilder.AddParam(cmd, "$nru", row.NextRetryUtc is null ? null : SqliteUtc.ToIso(row.NextRetryUtc.Value));
        SqliteSqlBuilder.AddParam(cmd, "$err", row.LastError);
        SqliteSqlBuilder.AddParam(cmd, "$c", SqliteUtc.ToIso(row.CreatedUtc));
        SqliteSqlBuilder.AddParam(cmd, "$u", SqliteUtc.ToIso(row.UpdatedUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(string attachmentId, UploadStatus status, string? onlineUrl, string? lastError, DateTimeOffset utcNow, TimeSpan? nextRetryDelay, CancellationToken cancellationToken)
    {
        var nextRetryIso = nextRetryDelay is null ? null : SqliteUtc.ToIso(utcNow.Add(nextRetryDelay.Value));

        byte[]? onlineUrlEncrypted = null;
        if (!string.IsNullOrWhiteSpace(onlineUrl))
            onlineUrlEncrypted = await _encryptor.EncryptAsync(Encoding.UTF8.GetBytes(onlineUrl), cancellationToken);

        await using var cmd = CreateCommand(
            """
            UPDATE Attachment
            SET UploadStatus = $s,
                OnlineURL = NULL,
                OnlineUrlEncrypted = COALESCE($urlEnc, OnlineUrlEncrypted),
                LastError = $err,
                AttemptCount = AttemptCount + 1,
                NextRetryUtc = $nru,
                UpdatedUtc = $now
            WHERE AttachmentId = $id;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$s", (int)status);
        SqliteSqlBuilder.AddParam(cmd, "$urlEnc", onlineUrlEncrypted);
        SqliteSqlBuilder.AddParam(cmd, "$err", lastError);
        SqliteSqlBuilder.AddParam(cmd, "$nru", nextRetryIso);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$id", attachmentId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
