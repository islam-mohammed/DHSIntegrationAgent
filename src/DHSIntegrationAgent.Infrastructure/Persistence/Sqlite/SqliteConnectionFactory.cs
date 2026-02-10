using System.Data.Common;
using System.IO;
using DHSIntegrationAgent.Application.Configuration;
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
            Cache = SqliteCacheMode.Shared,
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
        // busy_timeout in ms
        var commands = new[]
        {
            "PRAGMA foreign_keys = ON;",
            "PRAGMA journal_mode = WAL;",
            "PRAGMA synchronous = NORMAL;",
            "PRAGMA temp_store = MEMORY;",
            "PRAGMA busy_timeout = 5000;"
        };

        foreach (var sql in commands)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
