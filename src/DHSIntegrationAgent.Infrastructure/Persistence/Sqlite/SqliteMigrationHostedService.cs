using DHSIntegrationAgent.Application.Persistence;
using Microsoft.Extensions.Hosting;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal sealed class SqliteMigrationHostedService : IHostedService
{
    private readonly ISqliteMigrator _migrator;

    public SqliteMigrationHostedService(ISqliteMigrator migrator)
    {
        _migrator = migrator;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _migrator.MigrateAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
