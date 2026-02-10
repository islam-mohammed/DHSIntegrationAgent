using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class ProviderConfigCacheRepository : SqliteRepositoryBase, IProviderConfigCacheRepository
{
    public ProviderConfigCacheRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task<ProviderConfigCacheRow?> GetValidAsync(string providerDhsCode, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT ProviderDhsCode, ConfigJson, FetchedUtc, ExpiresUtc, ETag, LastError
            FROM ProviderConfigCache
            WHERE ProviderDhsCode = $p
            LIMIT 1;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        var expires = SqliteUtc.FromIso(r.GetString(3));
        if (expires <= utcNow) return null;

        return new ProviderConfigCacheRow(
            ProviderDhsCode: r.GetString(0),
            ConfigJson: r.GetString(1),
            FetchedUtc: SqliteUtc.FromIso(r.GetString(2)),
            ExpiresUtc: expires,
            ETag: r.IsDBNull(4) ? null : r.GetString(4),
            LastError: r.IsDBNull(5) ? null : r.GetString(5));
    }

    public async Task<ProviderConfigCacheRow?> GetAnyAsync(string providerDhsCode, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
        SELECT ProviderDhsCode, ConfigJson, FetchedUtc, ExpiresUtc, ETag, LastError
        FROM ProviderConfigCache
        WHERE ProviderDhsCode = $p
        LIMIT 1;
        """);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        return new ProviderConfigCacheRow(
            ProviderDhsCode: r.GetString(0),
            ConfigJson: r.GetString(1),
            FetchedUtc: SqliteUtc.FromIso(r.GetString(2)),
            ExpiresUtc: SqliteUtc.FromIso(r.GetString(3)),
            ETag: r.IsDBNull(4) ? null : r.GetString(4),
            LastError: r.IsDBNull(5) ? null : r.GetString(5));
    }

    public async Task UpsertAsync(ProviderConfigCacheRow row, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO ProviderConfigCache
            (ProviderDhsCode, ConfigJson, FetchedUtc, ExpiresUtc, ETag, LastError)
            VALUES
            ($p, $json, $f, $x, $e, $err)
            ON CONFLICT(ProviderDhsCode)
            DO UPDATE SET
                ConfigJson = excluded.ConfigJson,
                FetchedUtc = excluded.FetchedUtc,
                ExpiresUtc = excluded.ExpiresUtc,
                ETag = excluded.ETag,
                LastError = excluded.LastError;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", row.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$json", row.ConfigJson);
        SqliteSqlBuilder.AddParam(cmd, "$f", SqliteUtc.ToIso(row.FetchedUtc));
        SqliteSqlBuilder.AddParam(cmd, "$x", SqliteUtc.ToIso(row.ExpiresUtc));
        SqliteSqlBuilder.AddParam(cmd, "$e", row.ETag);
        SqliteSqlBuilder.AddParam(cmd, "$err", row.LastError);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetErrorAsync(string providerDhsCode, string lastError, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE ProviderConfigCache
            SET LastError = $e
            WHERE ProviderDhsCode = $p;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$e", lastError);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
