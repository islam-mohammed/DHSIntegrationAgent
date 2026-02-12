using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

public sealed class SqliteConcurrencyTests
{
    [Fact]
    public async Task Concurrent_writes_do_not_cause_table_is_locked_error()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dhs_concurrency_test_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:DatabasePath"] = dbPath,
                ["Api:BaseUrl"] = "https://example.invalid",
                ["Api:TimeoutSeconds"] = "30"
            })
            .Build();

        services.AddDhsApplication(config);
        services.AddDhsInfrastructure(config);

        using var sp = services.BuildServiceProvider();

        // 1. Setup schema
        var migrator = sp.GetRequiredService<ISqliteMigrator>();
        await migrator.MigrateAsync(CancellationToken.None);

        var uowFactory = sp.GetRequiredService<ISqliteUnitOfWorkFactory>();

        // 2. Perform concurrent writes from multiple tasks
        int taskCount = 10;
        int iterationsPerTask = 20;

        var tasks = Enumerable.Range(0, taskCount).Select(async taskId =>
        {
            for (int i = 0; i < iterationsPerTask; i++)
            {
                await using var uow = await uowFactory.CreateAsync(CancellationToken.None);

                // Write something (e.g. into ApiCallLog)
                await uow.ApiCallLogs.InsertAsync(
                    endpointName: $"TestEndpoint_{taskId}",
                    correlationId: Guid.NewGuid().ToString(),
                    requestUtc: DateTimeOffset.UtcNow,
                    responseUtc: DateTimeOffset.UtcNow,
                    durationMs: 10,
                    httpStatusCode: 200,
                    succeeded: true,
                    errorMessage: null,
                    requestBytes: 100,
                    responseBytes: 100,
                    wasGzipRequest: false,
                    providerDhsCode: "P1",
                    cancellationToken: CancellationToken.None
                );

                await uow.CommitAsync(CancellationToken.None);

                // Small random delay to increase interleaving
                await Task.Delay(new Random().Next(5, 20));
            }
        });

        // This should not throw 'database table is locked' or 'SQLITE_LOCKED'
        await Task.WhenAll(tasks);

        // 3. Verify count
        var connFactory = sp.GetRequiredService<ISqliteConnectionFactory>();
        await using (var conn = await connFactory.OpenConnectionAsync(CancellationToken.None))
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(1) FROM ApiCallLog;";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(taskCount * iterationsPerTask, count);
        }

        // Cleanup
        SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }
}
