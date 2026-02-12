using System.Data.Common;
using System.IO;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly AppOptions _appOptions;

    public SqliteConnectionFactory(IOptions<AppOptions> appOptions)
    {
        _appOptions = appOptions.Value;
        DatabasePath = SqliteDatabasePathResolver.Resolve(_appOptions);
    }

    public string DatabasePath { get; }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        // ReadWriteCreate will create the DB if missing.
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // WBS 1.2: We explicitly avoid Shared Cache because it causes SQLITE_LOCKED (Error 6)
            // when multiple connections in the same process access the same table.
            // Private cache + WAL is the recommended pattern for high concurrency.
            Cache = SqliteCacheMode.Private,
            Pooling = true
        };

        var conn = new SqliteConnection(csb.ConnectionString);
        await conn.OpenAsync(cancellationToken);

        // Required pragmas:
        // - Foreign keys must be ON (SQLite default is OFF).
        // - WAL improves concurrency for desktop apps with multiple short transactions.
        await ApplyPragmasAsync(conn, cancellationToken);

        return conn;
    }

    private static async Task ApplyPragmasAsync(SqliteConnection conn, CancellationToken ct)
    {
        // 1. Set busy_timeout FIRST.
        // We use a 10s timeout to handle transient write locks.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA busy_timeout = 10000;";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 2. Session-level settings.
        // journal_mode = WAL is set once in SqliteMigrator to avoid race conditions.
        var otherPragmas = new[]
        {
            "PRAGMA foreign_keys = ON;",
            "PRAGMA synchronous = NORMAL;",
            "PRAGMA temp_store = MEMORY;"
        };

        foreach (var sql in otherPragmas)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
