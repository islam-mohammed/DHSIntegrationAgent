using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Security;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal sealed class SqliteUnitOfWorkFactory : ISqliteUnitOfWorkFactory
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IColumnEncryptor _encryptor;

    public SqliteUnitOfWorkFactory(ISqliteConnectionFactory connectionFactory, IColumnEncryptor encryptor)
    {
        _connectionFactory = connectionFactory;
        _encryptor = encryptor;
    }

    public async Task<ISqliteUnitOfWork> CreateAsync(CancellationToken cancellationToken)
    {
        var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var tx = await conn.BeginTransactionAsync(cancellationToken);

        return new SqliteUnitOfWork(conn, tx, _encryptor);
    }
}
