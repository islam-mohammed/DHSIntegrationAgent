using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class AppSettingsRepository : IAppSettingsRepository
{
    private readonly DbConnection _conn;
    private readonly DbTransaction _tx;

    public AppSettingsRepository(DbConnection conn, DbTransaction tx)
    {
        _conn = conn;
        _tx = tx;
    }

    public async Task<AppSettingsRow> GetAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            """
            SELECT
                GroupID,
                ProviderDhsCode,
                NetworkUsername,
                NetworkPasswordEncrypted,
                ConfigCacheTtlMinutes,
                FetchIntervalMinutes,
                ManualRetryCooldownMinutes,
                LeaseDurationSeconds,
                StreamAIntervalSeconds,
                ResumePollIntervalSeconds,
                ApiTimeoutSeconds
            FROM AppSettings
            WHERE Id = 1;
            """;

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            throw new InvalidOperationException("AppSettings singleton row missing (Id=1). Run migrations.");

        return new AppSettingsRow(
            GroupId: r.IsDBNull(0) ? null : r.GetString(0),
            ProviderDhsCode: r.IsDBNull(1) ? null : r.GetString(1),
            NetworkUsername: r.IsDBNull(2) ? null : r.GetString(2),
            NetworkPasswordEncrypted: r.IsDBNull(3) ? null : (byte[])r[3],
            ConfigCacheTtlMinutes: r.GetInt32(4),
            FetchIntervalMinutes: r.GetInt32(5),
            ManualRetryCooldownMinutes: r.GetInt32(6),
            LeaseDurationSeconds: r.GetInt32(7),
            StreamAIntervalSeconds: r.GetInt32(8),
            ResumePollIntervalSeconds: r.GetInt32(9),
            ApiTimeoutSeconds: r.GetInt32(10));
    }

    public async Task UpdateSetupAsync(
        string groupId,
        string providerDhsCode,
        string? networkUsername,
        byte[]? networkPasswordEncrypted,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            """
            UPDATE AppSettings
            SET GroupID = $groupId,
                ProviderDhsCode = $providerDhsCode,
                NetworkUsername = $netUser,
                NetworkPasswordEncrypted = $netPass,
                UpdatedUtc = $now
            WHERE Id = 1;
            """;

        SqliteSqlBuilder.AddParam(cmd, "$groupId", groupId);
        SqliteSqlBuilder.AddParam(cmd, "$providerDhsCode", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$netUser", networkUsername);
        SqliteSqlBuilder.AddParam(cmd, "$netPass", networkPasswordEncrypted);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1) throw new InvalidOperationException("Failed to update AppSettings (expected 1 row).");
    }

    public async Task UpdateGeneralConfigurationAsync(
        int leaseDurationSeconds,
        int streamAIntervalSeconds,
        int resumePollIntervalSeconds,
        int apiTimeoutSeconds,
        int configCacheTtlMinutes,
        int fetchIntervalMinutes,
        int manualRetryCooldownMinutes,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            """
            UPDATE AppSettings
            SET
                LeaseDurationSeconds = $lease,
                StreamAIntervalSeconds = $streamA,
                ResumePollIntervalSeconds = $resume,
                ApiTimeoutSeconds = $apiTimeout,
                ConfigCacheTtlMinutes = $ttl,
                FetchIntervalMinutes = $fetch,
                ManualRetryCooldownMinutes = $cooldown,
                UpdatedUtc = $now
            WHERE Id = 1;
            """;

        SqliteSqlBuilder.AddParam(cmd, "$lease", leaseDurationSeconds);
        SqliteSqlBuilder.AddParam(cmd, "$streamA", streamAIntervalSeconds);
        SqliteSqlBuilder.AddParam(cmd, "$resume", resumePollIntervalSeconds);
        SqliteSqlBuilder.AddParam(cmd, "$apiTimeout", apiTimeoutSeconds);
        SqliteSqlBuilder.AddParam(cmd, "$ttl", configCacheTtlMinutes);
        SqliteSqlBuilder.AddParam(cmd, "$fetch", fetchIntervalMinutes);
        SqliteSqlBuilder.AddParam(cmd, "$cooldown", manualRetryCooldownMinutes);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1) throw new InvalidOperationException("Failed to update AppSettings (expected 1 row).");
    }
}
