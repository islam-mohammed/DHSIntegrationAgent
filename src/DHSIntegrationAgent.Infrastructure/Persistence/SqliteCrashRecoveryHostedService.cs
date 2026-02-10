using System.Data.Common;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Runs once at application startup to recover SQLite work-state after an unclean shutdown.
/// WBS 1.4: "Startup recovery: expired leases; mark stale dispatches".
/// </summary>
internal sealed class SqliteCrashRecoveryHostedService : IHostedService
{
    private readonly ILogger<SqliteCrashRecoveryHostedService> _logger;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IOptions<AppOptions> _appOptions;

    public SqliteCrashRecoveryHostedService(
        ILogger<SqliteCrashRecoveryHostedService> logger,
        ISqliteConnectionFactory connectionFactory,
        IOptions<AppOptions> appOptions)
    {
        _logger = logger;
        _connectionFactory = connectionFactory;
        _appOptions = appOptions;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Use UTC ISO-8601.
        var nowUtc = DateTime.UtcNow;
        var nowIso = nowUtc.ToString("O");

        _logger.LogInformation("SQLite crash recovery starting. NowUtc={NowUtc}, DbPath={DbPath}",
            nowIso, _appOptions.Value.DatabasePath);

        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1) Release expired claim leases and restore EnqueueStatus based on LockedBy.
            var recoveredClaims = await RecoverExpiredClaimLeasesAsync(conn, tx, nowIso, cancellationToken);

            // 2) Mark Dispatch rows stuck in InFlight as Failed.
            var recoveredDispatches = await MarkInFlightDispatchesFailedAsync(conn, tx, nowIso, cancellationToken);

            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "SQLite crash recovery completed. RecoveredClaims={RecoveredClaims}, RecoveredDispatches={RecoveredDispatches}",
                recoveredClaims, recoveredDispatches);
        }
        catch (Exception ex)
        {
            // Fail-fast: if recovery fails, DB work-state could remain inconsistent.
            _logger.LogError(ex, "SQLite crash recovery failed. Startup will be aborted to avoid inconsistent processing.");
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // One-shot startup job; nothing to stop.
        return Task.CompletedTask;
    }

    private static async Task<int> RecoverExpiredClaimLeasesAsync(
        DbConnection conn,
        DbTransaction tx,
        string nowIsoUtc,
        CancellationToken ct)
    {
        // EnqueueStatus enum (from SQLite spec):
        // 0 NotSent, 1 InFlight, 2 Enqueued, 3 Failed
        //
        // Restore EnqueueStatus based on which worker leased it:
        // - LockedBy='Sender'  => restore to NotSent
        // - LockedBy='Retry'   => restore to Failed
        // - LockedBy='Requeue' => restore to Enqueued
        //
        // Only rows where InFlightUntilUtc < now are considered abandoned/expired.
        const string sql = @"
UPDATE Claim
SET
    EnqueueStatus =
        CASE
            WHEN LockedBy = 'Sender'  THEN 0
            WHEN LockedBy = 'Retry'   THEN 3
            WHEN LockedBy = 'Requeue' THEN 2
            -- Fallback: if it was InFlight but LockedBy is unexpected, decide from AttemptCount.
            WHEN EnqueueStatus = 1 AND IFNULL(AttemptCount, 0) > 0 THEN 3
            WHEN EnqueueStatus = 1 THEN 0
            ELSE EnqueueStatus
        END,
    LockedBy = NULL,
    InFlightUntilUtc = NULL,
    LastUpdatedUtc = @nowUtc
WHERE
    InFlightUntilUtc IS NOT NULL
    AND InFlightUntilUtc < @nowUtc;
";

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "@nowUtc";
        pNow.Value = nowIsoUtc;
        cmd.Parameters.Add(pNow);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected;
    }

    private static async Task<int> MarkInFlightDispatchesFailedAsync(
        DbConnection conn,
        DbTransaction tx,
        string nowIsoUtc,
        CancellationToken ct)
    {
        // DispatchStatus enum (from SQLite spec):
        // 0 Ready, 1 InFlight, 2 Succeeded, 3 Failed, 4 PartiallySucceeded
        //
        // Stream specs say: On startup mark Dispatch rows left InFlight as Failed with LastError='App restart during request'.
        const string sql = @"
UPDATE Dispatch
SET
    DispatchStatus = 3,
    LastError = 'App restart during request',
    UpdatedUtc = @nowUtc
WHERE
    DispatchStatus = 1;
";

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        var pNow = cmd.CreateParameter();
        pNow.ParameterName = "@nowUtc";
        pNow.Value = nowIsoUtc;
        cmd.Parameters.Add(pNow);

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected;
    }
}
