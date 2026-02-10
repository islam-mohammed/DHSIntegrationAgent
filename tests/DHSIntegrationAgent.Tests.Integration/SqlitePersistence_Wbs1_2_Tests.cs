using DHSIntegrationAgent.Contracts.Claims;
using DHSIntegrationAgent.Contracts.Persistence;
﻿using System;
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

public sealed class SqlitePersistence_Wbs1_2_Tests
{
    [Fact]
    public async Task Lease_is_atomic_and_sets_InFlight()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dhs_agent_test_{Guid.NewGuid():N}.db");

        ServiceProvider? sp = null;

        try
        {
            // Arrange: DI container
            var services = new ServiceCollection();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["App:DatabasePath"] = dbPath,

                    // Required by your existing Infrastructure registrations (safe dummy)
                    ["Api:BaseUrl"] = "https://example.invalid",
                    ["Api:TimeoutSeconds"] = "30"
                })
                .Build();

            services.AddDhsApplication(config);
            services.AddDhsInfrastructure(config);

            sp = services.BuildServiceProvider();

            // Ensure schema exists
            var migrator = sp.GetRequiredService<ISqliteMigrator>();
            await migrator.MigrateAsync(CancellationToken.None);

            // Seed claims using raw SQL (test setup only).
            // IMPORTANT: Write valid ISO timestamps for required fields.
            var connFactory = sp.GetRequiredService<ISqliteConnectionFactory>();
            await using (var conn = await connFactory.OpenConnectionAsync(CancellationToken.None))
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    """
                    INSERT INTO Claim
                    (ProviderDhsCode, ProIdClaim, CompanyCode, MonthKey, BatchId, BcrId,
                     EnqueueStatus, CompletionStatus, LockedBy, InFlightUntilUtc,
                     AttemptCount, NextRetryUtc, LastError, LastResumeCheckUtc,
                     RequeueAttemptCount, NextRequeueUtc, LastRequeueError,
                     LastEnqueuedUtc, FirstSeenUtc, LastUpdatedUtc)
                    VALUES
                    ('P1', 1, 'C1', '202601', NULL, NULL,
                     0, 0, NULL, NULL,
                     0, NULL, NULL, NULL,
                     0, NULL, NULL,
                     NULL, '2026-01-26T00:00:00.0000000Z', '2026-01-26T00:00:00.0000000Z'),

                    ('P1', 2, 'C1', '202601', NULL, NULL,
                     0, 0, NULL, NULL,
                     0, NULL, NULL, NULL,
                     0, NULL, NULL,
                     NULL, '2026-01-26T00:00:00.0000000Z', '2026-01-26T00:00:00.0000000Z'),

                    ('P1', 3, 'C1', '202601', NULL, NULL,
                     0, 0, NULL, NULL,
                     0, NULL, NULL, NULL,
                     0, NULL, NULL,
                     NULL, '2026-01-26T00:00:00.0000000Z', '2026-01-26T00:00:00.0000000Z');
                    """;

                await cmd.ExecuteNonQueryAsync();
            }

            var uowFactory = sp.GetRequiredService<ISqliteUnitOfWorkFactory>();

            var now = DateTimeOffset.UtcNow;
            var leaseUntil = now.AddSeconds(120);

            // Act: lease 2 claims with worker-A
            IReadOnlyList<ClaimKey> leased1;
            await using (var uow1 = await uowFactory.CreateAsync(CancellationToken.None))
            {
                leased1 = await uow1.Claims.LeaseAsync(
                    new ClaimLeaseRequest(
                        ProviderDhsCode: "P1",
                        LockedBy: "worker-A",
                        UtcNow: now,
                        LeaseUntilUtc: leaseUntil,
                        Take: 2,
                        EligibleEnqueueStatuses: new[] { EnqueueStatus.NotSent },
                        RequireRetryDue: false),
                    CancellationToken.None);

                await uow1.CommitAsync(CancellationToken.None);
            }

            Assert.Equal(2, leased1.Count);

            // Act: lease remaining claims with worker-B (should only get 1)
            IReadOnlyList<ClaimKey> leased2;
            await using (var uow2 = await uowFactory.CreateAsync(CancellationToken.None))
            {
                leased2 = await uow2.Claims.LeaseAsync(
                    new ClaimLeaseRequest(
                        ProviderDhsCode: "P1",
                        LockedBy: "worker-B",
                        UtcNow: now,
                        LeaseUntilUtc: leaseUntil,
                        Take: 2,
                        EligibleEnqueueStatuses: new[] { EnqueueStatus.NotSent },
                        RequireRetryDue: false),
                    CancellationToken.None);

                await uow2.CommitAsync(CancellationToken.None);
            }

            Assert.Single(leased2);

            // Assert: no overlap
            var leased1Ids = leased1.Select(x => x.ProIdClaim).ToHashSet();
            foreach (var id in leased2.Select(x => x.ProIdClaim))
                Assert.DoesNotContain(id, leased1Ids);

            // Assert: all leased rows are set to InFlight, and locked/lease fields are populated
            await using (var conn = await connFactory.OpenConnectionAsync(CancellationToken.None))
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    """
                    SELECT ProIdClaim, EnqueueStatus, LockedBy, InFlightUntilUtc
                    FROM Claim
                    WHERE ProviderDhsCode = 'P1'
                    ORDER BY ProIdClaim;
                    """;

                await using var r = await cmd.ExecuteReaderAsync();
                var rows = new List<(int Id, int Status, string? LockedBy, string? LeaseUntil)>();
                while (await r.ReadAsync())
                {
                    rows.Add((
                        Id: r.GetInt32(0),
                        Status: r.GetInt32(1),
                        LockedBy: r.IsDBNull(2) ? null : r.GetString(2),
                        LeaseUntil: r.IsDBNull(3) ? null : r.GetString(3)
                    ));
                }

                Assert.Equal(3, rows.Count);

                // Two rows leased by worker-A, one by worker-B
                var lockedByA = rows.Count(x => x.LockedBy == "worker-A");
                var lockedByB = rows.Count(x => x.LockedBy == "worker-B");
                Assert.Equal(2, lockedByA);
                Assert.Equal(1, lockedByB);

                // All three are InFlight after leasing
                foreach (var row in rows)
                {
                    Assert.Equal((int)EnqueueStatus.InFlight, row.Status);
                    Assert.False(string.IsNullOrWhiteSpace(row.LockedBy));
                    Assert.False(string.IsNullOrWhiteSpace(row.LeaseUntil));
                }
            }
        }
        finally
        {
            // IMPORTANT: dispose DI container so hosted services / disposables release resources
            sp?.Dispose();

            // IMPORTANT: clear pooled connections so file handles release on Windows
            SqliteConnection.ClearAllPools();

            // Delete with retry to tolerate Windows file handle lag
            if (File.Exists(dbPath))
            {
                for (var i = 0; i < 8; i++)
                {
                    try
                    {
                        File.Delete(dbPath);
                        break;
                    }
                    catch (IOException) when (i < 7)
                    {
                        Thread.Sleep(75);
                    }
                }
            }
        }
    }
}
