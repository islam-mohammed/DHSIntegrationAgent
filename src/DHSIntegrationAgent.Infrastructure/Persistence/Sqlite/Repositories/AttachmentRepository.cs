using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
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

    public async Task<IReadOnlyList<AttachmentRow>> GetByClaimAsync(string providerDhsCode, int proIdClaim, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            """
            SELECT AttachmentId, ProviderDhsCode, ProIdClaim, AttachmentSourceType,
                   LocationPathEncrypted, LocationBytesEncrypted, AttachBitBase64Encrypted,
                   FileName, ContentType, SizeBytes, Sha256,
                   OnlineUrlEncrypted, UploadStatus, AttemptCount, NextRetryUtc, LastError, CreatedUtc, UpdatedUtc
            FROM Attachment
            WHERE ProviderDhsCode = $p AND ProIdClaim = $cid;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$cid", proIdClaim);

        var result = new List<AttachmentRow>();

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var lpEnc = r["LocationPathEncrypted"] as byte[];
            var lbEnc = r["LocationBytesEncrypted"] as byte[];
            var abEnc = r["AttachBitBase64Encrypted"] as byte[];
            var urlEnc = r["OnlineUrlEncrypted"] as byte[];

            string? lp = null;
            if (lpEnc != null) lp = Encoding.UTF8.GetString(await _encryptor.DecryptAsync(lpEnc, ct));

            byte[]? lb = null;
            if (lbEnc != null) lb = await _encryptor.DecryptAsync(lbEnc, ct);

            byte[]? ab = null;
            if (abEnc != null) ab = await _encryptor.DecryptAsync(abEnc, ct);

            string? url = null;
            if (urlEnc != null) url = Encoding.UTF8.GetString(await _encryptor.DecryptAsync(urlEnc, ct));

            result.Add(new AttachmentRow(
                AttachmentId: r.GetString(0),
                ProviderDhsCode: r.GetString(1),
                ProIdClaim: r.GetInt32(2),
                SourceType: (AttachmentSourceType)r.GetInt32(3),
                LocationPathPlaintext: lp,
                LocationBytesPlaintext: lb,
                AttachBitBase64Plaintext: ab,
                FileName: r.IsDBNull(7) ? null : r.GetString(7),
                ContentType: r.IsDBNull(8) ? null : r.GetString(8),
                SizeBytes: r.IsDBNull(9) ? null : r.GetInt64(9),
                Sha256: r.IsDBNull(10) ? null : r.GetString(10),
                OnlineUrlPlaintext: url,
                UploadStatus: (UploadStatus)r.GetInt32(12),
                AttemptCount: r.GetInt32(13),
                NextRetryUtc: r.IsDBNull(14) ? null : SqliteUtc.FromIso(r.GetString(14)),
                LastError: r.IsDBNull(15) ? null : r.GetString(15),
                CreatedUtc: SqliteUtc.FromIso(r.GetString(16)),
                UpdatedUtc: SqliteUtc.FromIso(r.GetString(17))
            ));
        }

        return result;
    }
}
