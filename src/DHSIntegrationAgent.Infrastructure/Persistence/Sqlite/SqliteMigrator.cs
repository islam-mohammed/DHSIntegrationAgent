using System.Data.Common;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Persistence;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal sealed class SqliteMigrator : ISqliteMigrator
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ISystemClock _clock;
    private readonly ILogger<SqliteMigrator> _logger;

    public SqliteMigrator(
        ISqliteConnectionFactory connectionFactory,
        ISystemClock clock,
        ILogger<SqliteMigrator> logger)
    {
        _connectionFactory = connectionFactory;
        _clock = clock;
        _logger = logger;
    }

    public int CurrentSchemaVersion => SqliteMigrations.CurrentSchemaVersion;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var hasAppMeta = await TableExistsAsync(conn, "AppMeta", cancellationToken);

        if (!hasAppMeta)
        {
            // Safety: if the DB contains *some* tables but not AppMeta, it's not a valid agent DB.
            var anyUserTables = await AnyUserTablesExistAsync(conn, cancellationToken);
            if (anyUserTables)
                throw new InvalidOperationException($"SQLite DB at '{_connectionFactory.DatabasePath}' is missing AppMeta but contains tables. Refusing to migrate. Backup & reset the DB.");

            await CreateFreshDatabaseAsync(conn, cancellationToken);
            _logger.LogInformation("SQLite schema created at {DatabasePath} (SchemaVersion={SchemaVersion})",
                _connectionFactory.DatabasePath, CurrentSchemaVersion);
            return;
        }

        var current = await GetSchemaVersionAsync(conn, cancellationToken);

        if (current > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"SQLite schema version {current} is newer than this client supports ({CurrentSchemaVersion}). Update the app.");
        }

        if (current < CurrentSchemaVersion)
        {
            await UpgradeAsync(conn, fromVersion: current, cancellationToken);
            _logger.LogInformation("SQLite schema upgraded {From} -> {To}", current, CurrentSchemaVersion);
        }

        // Update LastOpenedUtc every startup (safe bookkeeping).
        await TouchLastOpenedUtcAsync(conn, cancellationToken);
    }

    private async Task CreateFreshDatabaseAsync(DbConnection conn, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var migration in SqliteMigrations.All)
        {
            foreach (var sql in migration.Statements)
                await ExecAsync(conn, tx, sql, ct);
        }

        // Seed singleton rows
        var now = _clock.UtcNow.ToString("O");
        var clientInstanceId = Guid.NewGuid().ToString("D");

        await ExecAsync(conn, tx,
            """
            INSERT INTO AppMeta (Id, SchemaVersion, CreatedUtc, LastOpenedUtc, ClientInstanceId)
            VALUES (1, $v, $created, $opened, $clientId);
            """,
            ct,
            ("$v", CurrentSchemaVersion),
            ("$created", now),
            ("$opened", now),
            ("$clientId", clientInstanceId));

        await ExecAsync(conn, tx,
            """
            INSERT INTO AppSettings (Id, GroupID, ProviderDhsCode, ConfigCacheTtlMinutes, FetchIntervalMinutes, ManualRetryCooldownMinutes, LeaseDurationSeconds, CreatedUtc, UpdatedUtc)
            VALUES (1, NULL, NULL, 1440, 5, 10, 120, $now, $now);
            """,
            ct,
            ("$now", now));

        await tx.CommitAsync(ct);
    }

    private async Task UpgradeAsync(DbConnection conn, int fromVersion, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var migration in SqliteMigrations.All.Where(m => m.Version > fromVersion).OrderBy(m => m.Version))
        {
            foreach (var sql in migration.Statements)
                await ExecAsync(conn, tx, sql, ct);

            await ExecAsync(conn, tx,
                "UPDATE AppMeta SET SchemaVersion = $v WHERE Id = 1;",
                ct,
                ("$v", migration.Version));
        }

        await tx.CommitAsync(ct);
    }

    private async Task<int> GetSchemaVersionAsync(DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SchemaVersion FROM AppMeta WHERE Id = 1;";
        var obj = await cmd.ExecuteScalarAsync(ct);
        if (obj is null || obj == DBNull.Value)
            throw new InvalidOperationException("AppMeta row missing or SchemaVersion NULL.");

        return Convert.ToInt32(obj);
    }

    private async Task TouchLastOpenedUtcAsync(DbConnection conn, CancellationToken ct)
    {
        var now = _clock.UtcNow.ToString("O");
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE AppMeta SET LastOpenedUtc = $now WHERE Id = 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$now";
        p.Value = now;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> TableExistsAsync(DbConnection conn, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = tableName;
        cmd.Parameters.Add(p);

        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj is not null;
    }

    private static async Task<bool> AnyUserTablesExistAsync(DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT 1
            FROM sqlite_master
            WHERE type='table'
              AND name NOT LIKE 'sqlite_%'
            LIMIT 1;
            """;
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj is not null;
    }

    private static async Task ExecAsync(
        DbConnection conn,
        DbTransaction tx,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
