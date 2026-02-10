using System.Data.Common;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal abstract class SqliteRepositoryBase
{
    protected SqliteRepositoryBase(DbConnection conn, DbTransaction tx)
    {
        Conn = conn;
        Tx = tx;
    }

    protected DbConnection Conn { get; }
    protected DbTransaction Tx { get; }

    protected DbCommand CreateCommand(string sql)
    {
        var cmd = Conn.CreateCommand();
        cmd.Transaction = Tx;
        cmd.CommandText = sql;
        return cmd;
    }
}
