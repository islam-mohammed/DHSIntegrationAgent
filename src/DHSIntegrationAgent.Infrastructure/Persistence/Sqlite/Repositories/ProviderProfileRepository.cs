using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class ProviderProfileRepository : SqliteRepositoryBase, IProviderProfileRepository
{
    public ProviderProfileRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task UpsertAsync(ProviderProfileRow row, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO ProviderProfile
            (ProviderCode, ProviderDhsCode, DbEngine, IntegrationType, EncryptedConnectionString, EncryptionKeyId, IsActive, CreatedUtc, UpdatedUtc, EncryptedBlobStorageConnectionString, BlobStorageContainerName, FetchClaimCountPerThread, ClaimSplitFollowupDays, DefaultTreatmentCountryCode, DefaultSubmissionReasonCode, DefaultPriority, DefaultErPbmDuration, SfdaServiceTypeIdentifier, PayersToDropZeroAmountServicesCsv)
            VALUES
            ($pc, $pd, $db, $it, $cs, $kid, $a, $c, $u, $ebs, $bsc, $fcc, $cfd, $dtcc, $dsrc, $dp, $depd, $ssti, $ptdz)
            ON CONFLICT(ProviderCode)
            DO UPDATE SET
                ProviderDhsCode = excluded.ProviderDhsCode,
                DbEngine = excluded.DbEngine,
                IntegrationType = excluded.IntegrationType,
                EncryptedConnectionString = excluded.EncryptedConnectionString,
                EncryptionKeyId = excluded.EncryptionKeyId,
                IsActive = excluded.IsActive,
                UpdatedUtc = excluded.UpdatedUtc,
                EncryptedBlobStorageConnectionString = excluded.EncryptedBlobStorageConnectionString,
                BlobStorageContainerName = excluded.BlobStorageContainerName,
                FetchClaimCountPerThread = excluded.FetchClaimCountPerThread,
                ClaimSplitFollowupDays = excluded.ClaimSplitFollowupDays,
                DefaultTreatmentCountryCode = excluded.DefaultTreatmentCountryCode,
                DefaultSubmissionReasonCode = excluded.DefaultSubmissionReasonCode,
                DefaultPriority = excluded.DefaultPriority,
                DefaultErPbmDuration = excluded.DefaultErPbmDuration,
                SfdaServiceTypeIdentifier = excluded.SfdaServiceTypeIdentifier,
                PayersToDropZeroAmountServicesCsv = excluded.PayersToDropZeroAmountServicesCsv;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$pc", row.ProviderCode);
        SqliteSqlBuilder.AddParam(cmd, "$pd", row.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$db", row.DbEngine);
        SqliteSqlBuilder.AddParam(cmd, "$it", row.IntegrationType);
        SqliteSqlBuilder.AddParam(cmd, "$cs", row.EncryptedConnectionString);
        SqliteSqlBuilder.AddParam(cmd, "$kid", row.EncryptionKeyId);
        SqliteSqlBuilder.AddParam(cmd, "$a", row.IsActive ? 1 : 0);
        SqliteSqlBuilder.AddParam(cmd, "$c", SqliteUtc.ToIso(row.CreatedUtc));
        SqliteSqlBuilder.AddParam(cmd, "$u", SqliteUtc.ToIso(row.UpdatedUtc));
        SqliteSqlBuilder.AddParam(cmd, "$ebs", row.EncryptedBlobStorageConnectionString);
        SqliteSqlBuilder.AddParam(cmd, "$bsc", row.BlobStorageContainerName);
        SqliteSqlBuilder.AddParam(cmd, "$fcc", row.FetchClaimCountPerThread);
        SqliteSqlBuilder.AddParam(cmd, "$cfd", row.ClaimSplitFollowupDays);
        SqliteSqlBuilder.AddParam(cmd, "$dtcc", row.DefaultTreatmentCountryCode);
        SqliteSqlBuilder.AddParam(cmd, "$dsrc", row.DefaultSubmissionReasonCode);
        SqliteSqlBuilder.AddParam(cmd, "$dp", row.DefaultPriority);
        SqliteSqlBuilder.AddParam(cmd, "$depd", row.DefaultErPbmDuration);
        SqliteSqlBuilder.AddParam(cmd, "$ssti", row.SfdaServiceTypeIdentifier);
        SqliteSqlBuilder.AddParam(cmd, "$ptdz", row.PayersToDropZeroAmountServicesCsv);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProviderProfileRow?> GetAsync(ProviderKey key, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT ProviderCode, ProviderDhsCode, DbEngine, IntegrationType, EncryptedConnectionString, EncryptionKeyId, IsActive, CreatedUtc, UpdatedUtc, EncryptedBlobStorageConnectionString, BlobStorageContainerName, FetchClaimCountPerThread, ClaimSplitFollowupDays, DefaultTreatmentCountryCode, DefaultSubmissionReasonCode, DefaultPriority, DefaultErPbmDuration, SfdaServiceTypeIdentifier, PayersToDropZeroAmountServicesCsv
            FROM ProviderProfile
            WHERE ProviderCode = $pc
            LIMIT 1;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$pc", key.ProviderCode);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        return ReadRow(r);
    }

    public async Task<ProviderProfileRow?> GetActiveByProviderDhsCodeAsync(string providerDhsCode, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT ProviderCode, ProviderDhsCode, DbEngine, IntegrationType, EncryptedConnectionString, EncryptionKeyId, IsActive, CreatedUtc, UpdatedUtc, EncryptedBlobStorageConnectionString, BlobStorageContainerName, FetchClaimCountPerThread, ClaimSplitFollowupDays, DefaultTreatmentCountryCode, DefaultSubmissionReasonCode, DefaultPriority, DefaultErPbmDuration, SfdaServiceTypeIdentifier, PayersToDropZeroAmountServicesCsv
            FROM ProviderProfile
            WHERE ProviderDhsCode = $pd AND IsActive = 1
            LIMIT 1;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$pd", providerDhsCode);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        return ReadRow(r);
    }

    public async Task SetActiveAsync(ProviderKey key, bool isActive, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE ProviderProfile
            SET IsActive = $a, UpdatedUtc = $u
            WHERE ProviderCode = $pc;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$a", isActive ? 1 : 0);
        SqliteSqlBuilder.AddParam(cmd, "$u", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$pc", key.ProviderCode);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ProviderProfileRow ReadRow(DbDataReader r)
        => new(
            ProviderCode: r.GetString(0),
            ProviderDhsCode: r.GetString(1),
            DbEngine: r.GetString(2),
            IntegrationType: r.GetString(3),
            EncryptedConnectionString: (byte[])r["EncryptedConnectionString"],
            EncryptionKeyId: r.IsDBNull(5) ? null : r.GetString(5),
            IsActive: r.GetInt32(6) == 1,
            CreatedUtc: SqliteUtc.FromIso(r.GetString(7)),
            UpdatedUtc: SqliteUtc.FromIso(r.GetString(8)),
            EncryptedBlobStorageConnectionString: r.IsDBNull(9) ? null : (byte[])r["EncryptedBlobStorageConnectionString"],
            BlobStorageContainerName: r.IsDBNull(10) ? null : r.GetString(10),
            FetchClaimCountPerThread: r.IsDBNull(11) ? 300 : r.GetInt32(11),
            ClaimSplitFollowupDays: r.IsDBNull(12) ? null : r.GetInt32(12),
            DefaultTreatmentCountryCode: r.IsDBNull(13) ? null : r.GetString(13),
            DefaultSubmissionReasonCode: r.IsDBNull(14) ? null : r.GetString(14),
            DefaultPriority: r.IsDBNull(15) ? null : r.GetString(15),
            DefaultErPbmDuration: r.IsDBNull(16) ? null : r.GetInt32(16),
            SfdaServiceTypeIdentifier: r.IsDBNull(17) ? null : r.GetString(17),
            PayersToDropZeroAmountServicesCsv: r.IsDBNull(18) ? null : r.GetString(18));
}
