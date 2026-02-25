using DHSIntegrationAgent.Contracts.Claims;
using DHSIntegrationAgent.Contracts.Persistence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Domain.WorkStates;
using DHSIntegrationAgent.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

public sealed class SqliteRepositoryBulkTests
{
    [Fact]
    public async Task Bulk_operations_return_correct_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dhs_agent_test_bulk_{Guid.NewGuid():N}.db");
        ServiceProvider? sp = null;

        try
        {
            // Arrange: DI container
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
            sp = services.BuildServiceProvider();

            // Migrate schema
            var migrator = sp.GetRequiredService<ISqliteMigrator>();
            await migrator.MigrateAsync(CancellationToken.None);

            var uowFactory = sp.GetRequiredService<ISqliteUnitOfWorkFactory>();
            var now = DateTimeOffset.UtcNow;
            var nowIso = now.ToString("O");

            // Seed Batches using Repository (easier)
            // Using default for dates as they are not nullable in BatchKey struct
            await using (var uow = await uowFactory.CreateAsync(CancellationToken.None))
            {
                await uow.Batches.EnsureBatchAsync(new BatchKey("P1", "C1", "202601", default, default), BatchStatus.Draft, now, CancellationToken.None);
                await uow.Batches.EnsureBatchAsync(new BatchKey("P1", "C1", "202602", default, default), BatchStatus.Ready, now, CancellationToken.None);

                var b1 = await uow.Batches.TryGetBatchIdAsync(new BatchKey("P1", "C1", "202601", default, default), CancellationToken.None);
                var b2 = await uow.Batches.TryGetBatchIdAsync(new BatchKey("P1", "C1", "202602", default, default), CancellationToken.None);

                await uow.Batches.SetBcrIdAsync(b1!.Value, "BCR-001", now, CancellationToken.None);
                await uow.Batches.SetBcrIdAsync(b2!.Value, "BCR-002", now, CancellationToken.None);

                await uow.CommitAsync(CancellationToken.None);
            }

            // Seed Claims manually (to control EnqueueStatus easily)
            var connFactory = sp.GetRequiredService<ISqliteConnectionFactory>();
            await using (var conn = await connFactory.OpenConnectionAsync(CancellationToken.None))
            await using (var cmd = conn.CreateCommand())
            {
                // Retrieve BatchIds
                long b1Id, b2Id;
                using (var getCmd = conn.CreateCommand())
                {
                    getCmd.CommandText = "SELECT BatchId, BcrId FROM Batch WHERE BcrId IN ('BCR-001', 'BCR-002')";
                    using var reader = await getCmd.ExecuteReaderAsync();
                    var map = new Dictionary<string, long>();
                    while (await reader.ReadAsync())
                    {
                        map[reader.GetString(1)] = reader.GetInt64(0);
                    }
                    b1Id = map["BCR-001"];
                    b2Id = map["BCR-002"];
                }

                cmd.CommandText =
                    $"""
                    INSERT INTO Claim
                    (ProviderDhsCode, ProIdClaim, CompanyCode, MonthKey, BatchId, BcrId,
                     EnqueueStatus, CompletionStatus, LockedBy, InFlightUntilUtc,
                     AttemptCount, NextRetryUtc, LastError, LastResumeCheckUtc,
                     RequeueAttemptCount, NextRequeueUtc, LastRequeueError,
                     LastEnqueuedUtc, FirstSeenUtc, LastUpdatedUtc)
                    VALUES
                    -- Batch 1: 1 Enqueued, 1 Failed, 1 NotSent (Total 3)
                    ('P1', 1, 'C1', '202601', {b1Id}, 'BCR-001', {(int)EnqueueStatus.Enqueued}, 0, NULL, NULL, 0, NULL, NULL, NULL, 0, NULL, NULL, NULL, '{nowIso}', '{nowIso}'),
                    ('P1', 2, 'C1', '202601', {b1Id}, 'BCR-001', {(int)EnqueueStatus.Failed}, 0, NULL, NULL, 0, NULL, NULL, NULL, 0, NULL, NULL, NULL, '{nowIso}', '{nowIso}'),
                    ('P1', 3, 'C1', '202601', {b1Id}, 'BCR-001', {(int)EnqueueStatus.NotSent}, 0, NULL, NULL, 0, NULL, NULL, NULL, 0, NULL, NULL, NULL, '{nowIso}', '{nowIso}'),

                    -- Batch 2: 2 Enqueued (Total 2)
                    ('P1', 4, 'C1', '202602', {b2Id}, 'BCR-002', {(int)EnqueueStatus.Enqueued}, 0, NULL, NULL, 0, NULL, NULL, NULL, 0, NULL, NULL, NULL, '{nowIso}', '{nowIso}'),
                    ('P1', 5, 'C1', '202602', {b2Id}, 'BCR-002', {(int)EnqueueStatus.Enqueued}, 0, NULL, NULL, 0, NULL, NULL, NULL, 0, NULL, NULL, NULL, '{nowIso}', '{nowIso}');
                    """;

                await cmd.ExecuteNonQueryAsync();
            }

            // Test GetByBcrIdsAsync
            await using (var uow = await uowFactory.CreateAsync(CancellationToken.None))
            {
                var batches = await uow.Batches.GetByBcrIdsAsync(new[] { "BCR-001", "BCR-002", "BCR-MISSING" }, CancellationToken.None);

                Assert.Equal(2, batches.Count);
                Assert.Contains(batches, b => b.BcrId == "BCR-001");
                Assert.Contains(batches, b => b.BcrId == "BCR-002");

                // Test GetBatchCountsForBatchesAsync
                var batchIds = batches.Select(b => b.BatchId).ToList();
                // Add a dummy ID that doesn't exist to verify it returns (0,0,0)
                batchIds.Add(99999);

                var counts = await uow.Claims.GetBatchCountsForBatchesAsync(batchIds, CancellationToken.None);

                Assert.Equal(3, counts.Count); // 2 existing + 1 dummy

                var b1 = batches.First(b => b.BcrId == "BCR-001");
                var b2 = batches.First(b => b.BcrId == "BCR-002");

                Assert.True(counts.ContainsKey(b1.BatchId));
                Assert.True(counts.ContainsKey(b2.BatchId));
                Assert.True(counts.ContainsKey(99999));

                var c1 = counts[b1.BatchId];
                Assert.Equal(3, c1.Total);
                Assert.Equal(1, c1.Enqueued);
                Assert.Equal(1, c1.Failed);

                var c2 = counts[b2.BatchId];
                Assert.Equal(2, c2.Total);
                Assert.Equal(2, c2.Enqueued);
                Assert.Equal(0, c2.Failed);

                var c3 = counts[99999];
                Assert.Equal(0, c3.Total);
            }
        }
        finally
        {
            sp?.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                for (var i = 0; i < 8; i++)
                {
                    try { File.Delete(dbPath); break; }
                    catch (IOException) { Thread.Sleep(75); }
                }
            }
        }
    }
}
