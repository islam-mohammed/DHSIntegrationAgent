using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Infrastructure;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Domain.WorkStates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

/// <summary>
/// WBS 1.4 verification:
/// - On startup, clear expired claim leases and restore Claim.EnqueueStatus based on LockedBy.
/// - On startup, mark Dispatch rows stuck InFlight as Failed with LastError='App restart during request'.
/// </summary>
public sealed class SqliteCrashRecovery_Wbs1_4_Tests
{
    [Fact]
    public async Task Startup_recovery_clears_expired_claim_leases_and_marks_inflight_dispatch_failed()
    {
        // ---------------------------
        // Arrange: create temp DB path
        // ---------------------------
        var dbPath = Path.Combine(Path.GetTempPath(), $"dhs_agent_wbs1_4_{Guid.NewGuid():N}.db");

        // ---------------------------
        // Arrange: build configuration
        // ---------------------------
        // This satisfies options binding + any "fail-fast" validation you added in WBS 0.3:
        // - Api:BaseUrl must be valid (even though this test doesn't call the API)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:EnvironmentName"] = "Test",
                ["App:DatabasePath"] = dbPath,

                ["Api:BaseUrl"] = "https://example.invalid/",
                ["Api:TimeoutSeconds"] = "30"
            })
            .Build();

        // ---------------------------
        // Arrange: build DI container
        // ---------------------------
        // We register Application + Infrastructure the same way the real app does.
        // This ensures we test real ISqliteConnectionFactory, real migrator, and the hosted service registration.
        var services = new ServiceCollection();

        // Logging is required because hosted services depend on ILogger<T>.
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

        services.AddDhsApplication(config);
        services.AddDhsInfrastructure(config);

        await using var provider = services.BuildServiceProvider();

        // ---------------------------
        // Arrange: migrate schema (so tables exist)
        // ---------------------------
        // Crash recovery assumes Claim/Dispatch tables exist, so we explicitly run migrations first.
        var migrator = provider.GetRequiredService<ISqliteMigrator>();
        await migrator.MigrateAsync(CancellationToken.None);

        // We'll use the same connection factory the app uses.
        var connFactory = provider.GetRequiredService<ISqliteConnectionFactory>();

        // ---------------------------
        // Arrange: seed DB with test rows
        // ---------------------------
        // We insert:
        // 1) A Batch row (Dispatch has FK to Batch).
        // 2) 3 expired leases (Sender/Retry/Requeue) and 1 non-expired lease.
        // 3) One Dispatch stuck InFlight.
        var now = DateTime.UtcNow;
        var nowIso = now.ToString("O");
        var pastIso = now.AddMinutes(-30).ToString("O");   // expired lease timestamp
        var futureIso = now.AddMinutes(+30).ToString("O"); // NOT expired lease timestamp

        await using (var conn = await connFactory.OpenConnectionAsync(CancellationToken.None))
        await using (var tx = await conn.BeginTransactionAsync(CancellationToken.None))
        {
            // Insert a Batch row so Dispatch can reference it.
            var batchId = await InsertBatchAsync(conn, tx,
                providerDhsCode: "P1",
                companyCode: "C1",
                monthKey: "202501",
                batchStatus: (int)BatchStatus.Ready,
                createdUtcIso: nowIso,
                updatedUtcIso: nowIso,
                CancellationToken.None);

            // Expired claim leased by Sender -> should revert to NotSent
            await InsertClaimAsync(conn, tx,
                providerDhsCode: "P1",
                proIdClaim: 101,
                companyCode: "C1",
                monthKey: "202501",
                enqueueStatus: (int)EnqueueStatus.InFlight,
                completionStatus: (int)CompletionStatus.Unknown,
                lockedBy: "Sender",
                inFlightUntilUtcIso: pastIso,
                attemptCount: 0,
                firstSeenUtcIso: nowIso,
                lastUpdatedUtcIso: "2000-01-01T00:00:00.0000000Z", // intentionally old so we can assert it changes
                CancellationToken.None);

            // Expired claim leased by Retry -> should revert to Failed
            await InsertClaimAsync(conn, tx,
                providerDhsCode: "P1",
                proIdClaim: 102,
                companyCode: "C1",
                monthKey: "202501",
                enqueueStatus: (int)EnqueueStatus.InFlight,
                completionStatus: (int)CompletionStatus.Unknown,
                lockedBy: "Retry",
                inFlightUntilUtcIso: pastIso,
                attemptCount: 2,
                firstSeenUtcIso: nowIso,
                lastUpdatedUtcIso: "2000-01-01T00:00:00.0000000Z",
                CancellationToken.None);

            // Expired claim leased by Requeue -> should revert to Enqueued
            await InsertClaimAsync(conn, tx,
                providerDhsCode: "P1",
                proIdClaim: 103,
                companyCode: "C1",
                monthKey: "202501",
                enqueueStatus: (int)EnqueueStatus.InFlight,
                completionStatus: (int)CompletionStatus.Unknown,
                lockedBy: "Requeue",
                inFlightUntilUtcIso: pastIso,
                attemptCount: 1,
                firstSeenUtcIso: nowIso,
                lastUpdatedUtcIso: "2000-01-01T00:00:00.0000000Z",
                CancellationToken.None);

            // NOT expired -> should remain untouched (still locked/in-flight)
            await InsertClaimAsync(conn, tx,
                providerDhsCode: "P1",
                proIdClaim: 104,
                companyCode: "C1",
                monthKey: "202501",
                enqueueStatus: (int)EnqueueStatus.InFlight,
                completionStatus: (int)CompletionStatus.Unknown,
                lockedBy: "Sender",
                inFlightUntilUtcIso: futureIso,
                attemptCount: 0,
                firstSeenUtcIso: nowIso,
                lastUpdatedUtcIso: "2000-01-01T00:00:00.0000000Z",
                CancellationToken.None);

            // Dispatch stuck InFlight -> should become Failed + LastError
            await InsertDispatchAsync(conn, tx,
                dispatchId: Guid.NewGuid().ToString("D"),
                providerDhsCode: "P1",
                batchId: batchId,
                sequenceNo: 1,
                dispatchType: (int)DispatchType.NormalSend,
                dispatchStatus: (int)DispatchStatus.InFlight,
                createdUtcIso: nowIso,
                updatedUtcIso: "2000-01-01T00:00:00.0000000Z",
                CancellationToken.None);

            await tx.CommitAsync(CancellationToken.None);
        }

        // ---------------------------
        // Act: run crash recovery hosted service
        // ---------------------------
        // The class is internal, so we resolve it via IHostedService and match by type name.
        // This keeps the test black-box and aligned with the real startup wiring.
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        var recoveryService = hostedServices.Single(s => s.GetType().Name == "SqliteCrashRecoveryHostedService");

        await recoveryService.StartAsync(CancellationToken.None);

        // ---------------------------
        // Assert: read DB and validate updates
        // ---------------------------
        await using (var conn = await connFactory.OpenConnectionAsync(CancellationToken.None))
        {
            // Expired Sender -> NotSent, unlocked
            var c101 = await ReadClaimAsync(conn, "P1", 101, CancellationToken.None);
            Assert.Equal((int)EnqueueStatus.NotSent, c101.EnqueueStatus);
            Assert.Null(c101.LockedBy);
            Assert.Null(c101.InFlightUntilUtc);

            // Expired Retry -> Failed, unlocked
            var c102 = await ReadClaimAsync(conn, "P1", 102, CancellationToken.None);
            Assert.Equal((int)EnqueueStatus.Failed, c102.EnqueueStatus);
            Assert.Null(c102.LockedBy);
            Assert.Null(c102.InFlightUntilUtc);

            // Expired Requeue -> Enqueued, unlocked
            var c103 = await ReadClaimAsync(conn, "P1", 103, CancellationToken.None);
            Assert.Equal((int)EnqueueStatus.Enqueued, c103.EnqueueStatus);
            Assert.Null(c103.LockedBy);
            Assert.Null(c103.InFlightUntilUtc);

            // Not expired -> unchanged (still in-flight, still locked)
            var c104 = await ReadClaimAsync(conn, "P1", 104, CancellationToken.None);
            Assert.Equal((int)EnqueueStatus.InFlight, c104.EnqueueStatus);
            Assert.Equal("Sender", c104.LockedBy);
            Assert.Equal(futureIso, c104.InFlightUntilUtc);

            // Dispatch: InFlight -> Failed + safe LastError
            var d = await ReadSingleDispatchAsync(conn, CancellationToken.None);
            Assert.Equal((int)DispatchStatus.Failed, d.DispatchStatus);
            Assert.Equal("App restart during request", d.LastError);

            // Sanity: UpdatedUtc should have been moved forward from our very old value.
            Assert.NotEqual("2000-01-01T00:00:00.0000000Z", d.UpdatedUtc);
        }
    }

    // -------------------------
    // Helpers: Inserts
    // -------------------------

    private static async Task<long> InsertBatchAsync(
        DbConnection conn,
        DbTransaction tx,
        string providerDhsCode,
        string companyCode,
        string monthKey,
        int batchStatus,
        string createdUtcIso,
        string updatedUtcIso,
        CancellationToken ct)
    {
        const string sql = """
        INSERT INTO Batch (ProviderDhsCode, CompanyCode, PayerCode, MonthKey, BcrId, BatchStatus, HasResume, CreatedUtc, UpdatedUtc, LastError)
        VALUES ($p, $c, NULL, $m, NULL, $s, 0, $created, $updated, NULL);
        SELECT last_insert_rowid();
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        Add(cmd, "$p", providerDhsCode);
        Add(cmd, "$c", companyCode);
        Add(cmd, "$m", monthKey);
        Add(cmd, "$s", batchStatus);
        Add(cmd, "$created", createdUtcIso);
        Add(cmd, "$updated", updatedUtcIso);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    private static async Task InsertClaimAsync(
        DbConnection conn,
        DbTransaction tx,
        string providerDhsCode,
        long proIdClaim,
        string companyCode,
        string monthKey,
        int enqueueStatus,
        int completionStatus,
        string lockedBy,
        string inFlightUntilUtcIso,
        int attemptCount,
        string firstSeenUtcIso,
        string lastUpdatedUtcIso,
        CancellationToken ct)
    {
        // Minimal NOT NULL columns + fields used by WBS 1.4 recovery.
        const string sql = """
        INSERT INTO Claim (
            ProviderDhsCode, ProIdClaim, CompanyCode, MonthKey,
            BatchId, BcrId,
            EnqueueStatus, CompletionStatus,
            LockedBy, InFlightUntilUtc,
            AttemptCount,
            NextRetryUtc, LastError, LastResumeCheckUtc,
            RequeueAttemptCount, NextRequeueUtc, LastRequeueError,
            LastEnqueuedUtc,
            FirstSeenUtc, LastUpdatedUtc
        )
        VALUES (
            $p, $id, $c, $m,
            NULL, NULL,
            $enqueue, $completion,
            $lockedBy, $inflightUntil,
            $attemptCount,
            NULL, NULL, NULL,
            0, NULL, NULL,
            NULL,
            $firstSeen, $lastUpdated
        );
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        Add(cmd, "$p", providerDhsCode);
        Add(cmd, "$id", proIdClaim);
        Add(cmd, "$c", companyCode);
        Add(cmd, "$m", monthKey);
        Add(cmd, "$enqueue", enqueueStatus);
        Add(cmd, "$completion", completionStatus);
        Add(cmd, "$lockedBy", lockedBy);
        Add(cmd, "$inflightUntil", inFlightUntilUtcIso);
        Add(cmd, "$attemptCount", attemptCount);
        Add(cmd, "$firstSeen", firstSeenUtcIso);
        Add(cmd, "$lastUpdated", lastUpdatedUtcIso);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertDispatchAsync(
        DbConnection conn,
        DbTransaction tx,
        string dispatchId,
        string providerDhsCode,
        long batchId,
        int sequenceNo,
        int dispatchType,
        int dispatchStatus,
        string createdUtcIso,
        string updatedUtcIso,
        CancellationToken ct)
    {
        // Minimal NOT NULL columns + fields used by WBS 1.4 recovery.
        const string sql = """
        INSERT INTO Dispatch (
            DispatchId, ProviderDhsCode, BatchId,
            BcrId, SequenceNo,
            DispatchType, DispatchStatus,
            AttemptCount, NextRetryUtc,
            RequestSizeBytes, RequestGzip, HttpStatusCode,
            LastError, CorrelationId,
            CreatedUtc, UpdatedUtc
        )
        VALUES (
            $id, $p, $batch,
            NULL, $seq,
            $type, $status,
            0, NULL,
            NULL, 1, NULL,
            NULL, NULL,
            $created, $updated
        );
        """;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        Add(cmd, "$id", dispatchId);
        Add(cmd, "$p", providerDhsCode);
        Add(cmd, "$batch", batchId);
        Add(cmd, "$seq", sequenceNo);
        Add(cmd, "$type", dispatchType);
        Add(cmd, "$status", dispatchStatus);
        Add(cmd, "$created", createdUtcIso);
        Add(cmd, "$updated", updatedUtcIso);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    // -------------------------
    // Helpers: Reads
    // -------------------------

    private sealed record ClaimRow(int EnqueueStatus, string? LockedBy, string? InFlightUntilUtc);

    private static async Task<ClaimRow> ReadClaimAsync(DbConnection conn, string providerDhsCode, long proIdClaim, CancellationToken ct)
    {
        const string sql = """
        SELECT EnqueueStatus, LockedBy, InFlightUntilUtc
        FROM Claim
        WHERE ProviderDhsCode = $p AND ProIdClaim = $id;
        """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        Add(cmd, "$p", providerDhsCode);
        Add(cmd, "$id", proIdClaim);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));

        var enqueue = reader.GetInt32(0);
        var lockedBy = reader.IsDBNull(1) ? null : reader.GetString(1);
        var inflight = reader.IsDBNull(2) ? null : reader.GetString(2);

        return new ClaimRow(enqueue, lockedBy, inflight);
    }

    private sealed record DispatchRow(int DispatchStatus, string? LastError, string UpdatedUtc);

    private static async Task<DispatchRow> ReadSingleDispatchAsync(DbConnection conn, CancellationToken ct)
    {
        // This test inserts exactly one dispatch.
        const string sql = """
        SELECT DispatchStatus, LastError, UpdatedUtc
        FROM Dispatch
        LIMIT 1;
        """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));

        var status = reader.GetInt32(0);
        var lastError = reader.IsDBNull(1) ? null : reader.GetString(1);
        var updatedUtc = reader.GetString(2);

        return new DispatchRow(status, lastError, updatedUtc);
    }

    // -------------------------
    // Helpers: parameter add
    // -------------------------

    private static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    [Fact]
    public async Task Startup_recovery_for_unknown_LockedBy_uses_AttemptCount_fallback_only_when_lease_expired()
    {
        // ---------------------------
        // Arrange: unique DB per test
        // ---------------------------
        // Why: isolation. Prevents tests from stepping on each other and avoids stale state.
        var dbPath = Path.Combine(Path.GetTempPath(), $"dhs_agent_wbs1_4_unknown_{Guid.NewGuid():N}.db");

        // ---------------------------
        // Arrange: minimal config
        // ---------------------------
        // Why: AddDhsApplication/AddDhsInfrastructure expect options to exist (WBS 0.3 validation).
        // We supply a safe Api:BaseUrl even though this test does not call HTTP.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:EnvironmentName"] = "Test",
                ["App:DatabasePath"] = dbPath,

                ["Api:BaseUrl"] = "https://example.invalid/",
                ["Api:TimeoutSeconds"] = "30"
            })
            .Build();

        // ---------------------------
        // Arrange: DI container
        // ---------------------------
        // Why: we want a black-box integration test that uses the real registrations.
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

        // These two calls mirror real app wiring.
        services.AddDhsApplication(config);
        services.AddDhsInfrastructure(config);

        await using var provider = services.BuildServiceProvider();

        // ---------------------------
        // Arrange: ensure schema exists
        // ---------------------------
        // Why: crash recovery updates Claim + Dispatch tables; they must exist.
        var migrator = provider.GetRequiredService<ISqliteMigrator>();
        await migrator.MigrateAsync(CancellationToken.None);

        // We'll use the same connection factory the app uses.
        var connFactory = provider.GetRequiredService<ISqliteConnectionFactory>();

        // ---------------------------
        // Arrange: seed Claims
        // ---------------------------
        // We create:
        // 1) expired lease with unknown LockedBy + AttemptCount=0  -> expect NotSent (0)
        // 2) expired lease with unknown LockedBy + AttemptCount=2  -> expect Failed  (3)
        // 3) NOT expired lease with unknown LockedBy              -> must remain InFlight + Locked
        //
        // Why: this directly validates the fallback CASE logic in SqliteCrashRecoveryHostedService.
        var now = DateTime.UtcNow;                 // recovery logic is based on UTC
        var nowIso = now.ToString("O");            // stored as ISO-8601 roundtrip
        var pastIso = now.AddMinutes(-30).ToString("O");   // expired
        var futureIso = now.AddMinutes(+30).ToString("O"); // not expired

        // EnqueueStatus enum values per schema/spec:
        // 0 NotSent, 1 InFlight, 2 Enqueued, 3 Failed
        const int NotSent = 0;
        const int InFlight = 1;
        const int Failed = 3;

        await using (var conn = await connFactory.OpenConnectionAsync(CancellationToken.None))
        await using (var tx = await conn.BeginTransactionAsync(CancellationToken.None))
        {
            // Expired + unknown LockedBy + AttemptCount=0 -> fallback should be NotSent.
            await InsertClaimAsync(conn, tx,
                providerDhsCode: "P1",
                proIdClaim: 201,
                companyCode: "C1",
                monthKey: "202501",
                enqueueStatus: InFlight,
                completionStatus: 0,                 // doesn't matter for this recovery scenario
                lockedBy: "WeirdWorker",             // unknown LockedBy
                inFlightUntilUtcIso: pastIso,        // expired lease
                attemptCount: 0,                     // drives fallback branch
                firstSeenUtcIso: nowIso,
                lastUpdatedUtcIso: "2000-01-01T00:00:00.0000000Z",
                CancellationToken.None);

            // Expired + unknown LockedBy + AttemptCount>0 -> fallback should be Failed.
            await InsertClaimAsync(conn, tx,
                providerDhsCode: "P1",
                proIdClaim: 202,
                companyCode: "C1",
                monthKey: "202501",
                enqueueStatus: InFlight,
                completionStatus: 0,
                lockedBy: "WeirdWorker",
                inFlightUntilUtcIso: pastIso,
                attemptCount: 2,
                firstSeenUtcIso: nowIso,
                lastUpdatedUtcIso: "2000-01-01T00:00:00.0000000Z",
                CancellationToken.None);

            // Not expired -> must remain locked and InFlight.
            await InsertClaimAsync(conn, tx,
                providerDhsCode: "P1",
                proIdClaim: 203,
                companyCode: "C1",
                monthKey: "202501",
                enqueueStatus: InFlight,
                completionStatus: 0,
                lockedBy: "WeirdWorker",
                inFlightUntilUtcIso: futureIso,      // NOT expired
                attemptCount: 0,
                firstSeenUtcIso: nowIso,
                lastUpdatedUtcIso: "2000-01-01T00:00:00.0000000Z",
                CancellationToken.None);

            // Commit inserts so recovery can read/update them.
            await tx.CommitAsync(CancellationToken.None);
        }

        // ---------------------------
        // Act: run crash recovery
        // ---------------------------
        // The hosted service should be registered in Infrastructure DI (WBS 1.4).
        // We resolve it through IHostedService to keep the test black-box.
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        var recoveryService = hostedServices.Single(s => s.GetType().Name == "SqliteCrashRecoveryHostedService");
        await recoveryService.StartAsync(CancellationToken.None);

        // ---------------------------
        // Assert: verify each claim outcome
        // ---------------------------
        await using (var conn = await connFactory.OpenConnectionAsync(CancellationToken.None))
        {
            // Claim 201: expired + attemptCount 0 -> NotSent, unlocked.
            var c201 = await ReadClaimAsync(conn, "P1", 201, CancellationToken.None);
            Assert.Equal(NotSent, c201.EnqueueStatus);
            Assert.Null(c201.LockedBy);
            Assert.Null(c201.InFlightUntilUtc);

            // Claim 202: expired + attemptCount >0 -> Failed, unlocked.
            var c202 = await ReadClaimAsync(conn, "P1", 202, CancellationToken.None);
            Assert.Equal(Failed, c202.EnqueueStatus);
            Assert.Null(c202.LockedBy);
            Assert.Null(c202.InFlightUntilUtc);

            // Claim 203: not expired -> should remain unchanged.
            var c203 = await ReadClaimAsync(conn, "P1", 203, CancellationToken.None);
            Assert.Equal(InFlight, c203.EnqueueStatus);
            Assert.Equal("WeirdWorker", c203.LockedBy);
            Assert.Equal(futureIso, c203.InFlightUntilUtc);
        }
    }

}
