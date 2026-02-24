using DHSIntegrationAgent.Contracts.Claims;
using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class ClaimRepository : SqliteRepositoryBase, IClaimRepository
{
    public ClaimRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task UpsertStagedAsync(
        string providerDhsCode,
        int proIdClaim,
        string companyCode,
        string monthKey,
        long? batchId,
        string? bcrId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        // Staging creates/refreshes the claim and sets it to NotSent+Unknown (non-blocking).
        await using var cmd = CreateCommand(
            """
            INSERT INTO Claim
            (ProviderDhsCode, ProIdClaim, CompanyCode, MonthKey, BatchId, BcrId,
             EnqueueStatus, CompletionStatus, LockedBy, InFlightUntilUtc,
             AttemptCount, NextRetryUtc, LastError, LastResumeCheckUtc,
             RequeueAttemptCount, NextRequeueUtc, LastRequeueError,
             LastEnqueuedUtc, FirstSeenUtc, LastUpdatedUtc)
            VALUES
            ($p, $id, $cc, $mk, $bid, $bcr,
             $es, $cs, NULL, NULL,
             0, NULL, NULL, NULL,
             0, NULL, NULL,
             NULL, $now, $now)
            ON CONFLICT(ProviderDhsCode, ProIdClaim)
            DO UPDATE SET
                CompanyCode = excluded.CompanyCode,
                MonthKey = COALESCE(Claim.MonthKey, excluded.MonthKey),
                BatchId = COALESCE(Claim.BatchId, excluded.BatchId),
                BcrId = COALESCE(Claim.BcrId, excluded.BcrId),
                EnqueueStatus = CASE
                    WHEN Claim.EnqueueStatus = $enqueued THEN Claim.EnqueueStatus
                    ELSE $notsent
                END,
                CompletionStatus = CASE
                    WHEN Claim.CompletionStatus = $completed THEN Claim.CompletionStatus
                    ELSE $unknown
                END,
                LastUpdatedUtc = excluded.LastUpdatedUtc;
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$id", proIdClaim);
        SqliteSqlBuilder.AddParam(cmd, "$cc", companyCode);
        SqliteSqlBuilder.AddParam(cmd, "$mk", monthKey);
        SqliteSqlBuilder.AddParam(cmd, "$bid", batchId);
        SqliteSqlBuilder.AddParam(cmd, "$bcr", bcrId);
        SqliteSqlBuilder.AddParam(cmd, "$es", (int)EnqueueStatus.NotSent);
        SqliteSqlBuilder.AddParam(cmd, "$cs", (int)CompletionStatus.Unknown);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);

        // constants for conflict update CASE
        SqliteSqlBuilder.AddParam(cmd, "$enqueued", (int)EnqueueStatus.Enqueued);
        SqliteSqlBuilder.AddParam(cmd, "$notsent", (int)EnqueueStatus.NotSent);
        SqliteSqlBuilder.AddParam(cmd, "$completed", (int)CompletionStatus.Completed);
        SqliteSqlBuilder.AddParam(cmd, "$unknown", (int)CompletionStatus.Unknown);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClaimKey>> LeaseAsync(ClaimLeaseRequest request, CancellationToken cancellationToken)
    {
        if (request.Take <= 0) return Array.Empty<ClaimKey>();
        if (request.EligibleEnqueueStatuses.Count == 0) return Array.Empty<ClaimKey>();

        await using var cmd = CreateCommand("");
        var nowIso = SqliteUtc.ToIso(request.UtcNow);
        var leaseIso = SqliteUtc.ToIso(request.LeaseUntilUtc);

        var statusInts = request.EligibleEnqueueStatuses.Select(s => (int)s).ToList();
        var inClause = SqliteSqlBuilder.AddIntInClause(cmd, "s", statusInts);

        // Atomic lease acquisition + status transition to InFlight
        cmd.CommandText =
            $"""
            WITH cte AS (
              SELECT rowid
              FROM Claim
              WHERE ProviderDhsCode = $p
                AND EnqueueStatus IN {inClause}
                AND (InFlightUntilUtc IS NULL OR InFlightUntilUtc <= $now)
                {(request.BatchId.HasValue ? "AND BatchId = $bid" : "")}
                {(request.RequireRetryDue ? "AND (NextRetryUtc IS NULL OR NextRetryUtc <= $now)" : "")}
              ORDER BY ProIdClaim
              LIMIT $take
            )
            UPDATE Claim
            SET LockedBy = $lockedBy,
                InFlightUntilUtc = $leaseUntil,
                EnqueueStatus = $inflight,
                LastUpdatedUtc = $now
            WHERE rowid IN (SELECT rowid FROM cte)
            RETURNING ProviderDhsCode, ProIdClaim;
            """;

        SqliteSqlBuilder.AddParam(cmd, "$p", request.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$lockedBy", request.LockedBy);
        SqliteSqlBuilder.AddParam(cmd, "$leaseUntil", leaseIso);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);
        SqliteSqlBuilder.AddParam(cmd, "$take", request.Take);
        SqliteSqlBuilder.AddParam(cmd, "$inflight", (int)EnqueueStatus.InFlight);
        if (request.BatchId.HasValue)
        {
            SqliteSqlBuilder.AddParam(cmd, "$bid", request.BatchId.Value);
        }

        var leased = new List<ClaimKey>(request.Take);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            leased.Add(new ClaimKey(r.GetString(0), r.GetInt32(1)));
        }
        return leased;
    }

    public async Task ReleaseLeaseAsync(IReadOnlyList<ClaimKey> claims, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        if (claims.Count == 0) return;

        await using var cmd = CreateCommand("");
        var with = SqliteSqlBuilder.AddClaimKeysCte(cmd, "keys", claims);

        cmd.CommandText =
            $"""
            {with}
            UPDATE Claim
            SET LockedBy = NULL,
                InFlightUntilUtc = NULL,
                LastUpdatedUtc = $now
            WHERE EXISTS (
                SELECT 1 FROM keys k
                WHERE k.ProviderDhsCode = Claim.ProviderDhsCode AND k.ProIdClaim = Claim.ProIdClaim
            );
            """;
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkEnqueuedAsync(IReadOnlyList<ClaimKey> claims, string? bcrId, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        if (claims.Count == 0) return;

        await using var cmd = CreateCommand("");
        var with = SqliteSqlBuilder.AddClaimKeysCte(cmd, "keys", claims);

        cmd.CommandText =
            $"""
            {with}
            UPDATE Claim
            SET EnqueueStatus = $enq,
                BcrId = COALESCE($bcr, BcrId),
                NextRetryUtc = NULL,
                LastError = NULL,
                LastEnqueuedUtc = $now,
                LockedBy = NULL,
                InFlightUntilUtc = NULL,
                LastUpdatedUtc = $now
            WHERE EXISTS (
                SELECT 1 FROM keys k
                WHERE k.ProviderDhsCode = Claim.ProviderDhsCode AND k.ProIdClaim = Claim.ProIdClaim
            );
            """;
        SqliteSqlBuilder.AddParam(cmd, "$enq", (int)EnqueueStatus.Enqueued);
        SqliteSqlBuilder.AddParam(cmd, "$bcr", bcrId);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(IReadOnlyList<ClaimKey> claims, string errorMessage, DateTimeOffset utcNow, TimeSpan? nextRetryDelay, CancellationToken cancellationToken)
    {
        if (claims.Count == 0) return;

        var nextRetryIso = nextRetryDelay is null ? null : SqliteUtc.ToIso(utcNow.Add(nextRetryDelay.Value));

        await using var cmd = CreateCommand("");
        var with = SqliteSqlBuilder.AddClaimKeysCte(cmd, "keys", claims);

        cmd.CommandText =
            $"""
            {with}
            UPDATE Claim
            SET EnqueueStatus = $failed,
                LastError = $err,
                NextRetryUtc = $nextRetry,
                LockedBy = NULL,
                InFlightUntilUtc = NULL,
                LastUpdatedUtc = $now
            WHERE EXISTS (
                SELECT 1 FROM keys k
                WHERE k.ProviderDhsCode = Claim.ProviderDhsCode AND k.ProIdClaim = Claim.ProIdClaim
            );
            """;
        SqliteSqlBuilder.AddParam(cmd, "$failed", (int)EnqueueStatus.Failed);
        SqliteSqlBuilder.AddParam(cmd, "$err", errorMessage);
        SqliteSqlBuilder.AddParam(cmd, "$nextRetry", nextRetryIso);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetCompletionStatusAsync(IReadOnlyList<ClaimKey> claims, CompletionStatus completionStatus, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        if (claims.Count == 0) return;

        await using var cmd = CreateCommand("");
        var with = SqliteSqlBuilder.AddClaimKeysCte(cmd, "keys", claims);

        cmd.CommandText =
            $"""
            {with}
            UPDATE Claim
            SET CompletionStatus = $cs,
                LastUpdatedUtc = $now
            WHERE EXISTS (
                SELECT 1 FROM keys k
                WHERE k.ProviderDhsCode = Claim.ProviderDhsCode AND k.ProIdClaim = Claim.ProIdClaim
            );
            """;
        SqliteSqlBuilder.AddParam(cmd, "$cs", (int)completionStatus);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task IncrementAttemptAsync(IReadOnlyList<ClaimKey> claims, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        if (claims.Count == 0) return;

        await using var cmd = CreateCommand("");
        var with = SqliteSqlBuilder.AddClaimKeysCte(cmd, "keys", claims);

        cmd.CommandText =
            $"""
            {with}
            UPDATE Claim
            SET AttemptCount = AttemptCount + 1,
                LastUpdatedUtc = $now
            WHERE EXISTS (
                SELECT 1 FROM keys k
                WHERE k.ProviderDhsCode = Claim.ProviderDhsCode AND k.ProIdClaim = Claim.ProIdClaim
            );
            """;
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecoverInFlightAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        // Mark any Claim rows with InFlightUntilUtc < now as eligible again by clearing LockedBy/InFlightUntilUtc
        // and restoring EnqueueStatus (NotSent/Failed/Enqueued as appropriate).
        await using var cmd = CreateCommand(
            """
            UPDATE Claim
            SET EnqueueStatus = CASE
                    WHEN LockedBy = 'Sender' THEN $notSent
                    WHEN LockedBy = 'Retry' THEN $failed
                    WHEN LockedBy = 'Requeue' THEN $enqueued
                    ELSE EnqueueStatus
                END,
                LockedBy = NULL,
                InFlightUntilUtc = NULL,
                LastUpdatedUtc = $now
            WHERE EnqueueStatus = $inFlight
               OR (InFlightUntilUtc IS NOT NULL AND InFlightUntilUtc <= $now);
            """);

        SqliteSqlBuilder.AddParam(cmd, "$notSent", (int)EnqueueStatus.NotSent);
        SqliteSqlBuilder.AddParam(cmd, "$failed", (int)EnqueueStatus.Failed);
        SqliteSqlBuilder.AddParam(cmd, "$enqueued", (int)EnqueueStatus.Enqueued);
        SqliteSqlBuilder.AddParam(cmd, "$inFlight", (int)EnqueueStatus.InFlight);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(int Total, int Enqueued, int Failed)> GetBatchCountsAsync(long batchId, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT
                COUNT(*),
                SUM(CASE WHEN EnqueueStatus = $enqueued THEN 1 ELSE 0 END),
                SUM(CASE WHEN EnqueueStatus = $failed THEN 1 ELSE 0 END)
            FROM Claim
            WHERE BatchId = $bid;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$bid", batchId);
        SqliteSqlBuilder.AddParam(cmd, "$enqueued", (int)EnqueueStatus.Enqueued);
        SqliteSqlBuilder.AddParam(cmd, "$failed", (int)EnqueueStatus.Failed);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await r.ReadAsync(cancellationToken))
        {
            var total = r.GetInt32(0);
            var enqueued = r.IsDBNull(1) ? 0 : r.GetInt32(1);
            var failed = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            return (total, enqueued, failed);
        }
        return (0, 0, 0);
    }

    public async Task<IReadOnlyList<ClaimKey>> ListByBatchAsync(long batchId, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT ProviderDhsCode, ProIdClaim
            FROM Claim
            WHERE BatchId = $bid;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$bid", batchId);

        var result = new List<ClaimKey>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            result.Add(new ClaimKey(r.GetString(0), r.GetInt32(1)));
        }
        return result;
    }
}
