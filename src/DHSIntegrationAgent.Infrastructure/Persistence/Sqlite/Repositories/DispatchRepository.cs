using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class DispatchRepository : SqliteRepositoryBase, IDispatchRepository
{
    public DispatchRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task InsertDispatchAsync(
        string dispatchId,
        string providerDhsCode,
        long batchId,
        string? bcrId,
        int sequenceNo,
        DispatchType dispatchType,
        DispatchStatus dispatchStatus,
        string? correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO Dispatch
            (DispatchId, ProviderDhsCode, BatchId, BcrId, SequenceNo, DispatchType, DispatchStatus, RequestGzip, CorrelationId, CreatedUtc, UpdatedUtc)
            VALUES
            ($id, $p, $bid, $bcr, $seq, $type, $status, 1, $corr, $now, $now);
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$id", dispatchId);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$bid", batchId);
        SqliteSqlBuilder.AddParam(cmd, "$bcr", bcrId);
        SqliteSqlBuilder.AddParam(cmd, "$seq", sequenceNo);
        SqliteSqlBuilder.AddParam(cmd, "$type", (int)dispatchType);
        SqliteSqlBuilder.AddParam(cmd, "$status", (int)dispatchStatus);
        SqliteSqlBuilder.AddParam(cmd, "$corr", correlationId);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateDispatchResultAsync(
        string dispatchId,
        DispatchStatus dispatchStatus,
        int? httpStatusCode,
        string? lastError,
        string? correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE Dispatch
            SET DispatchStatus = $status,
                HttpStatusCode = $http,
                LastError = $err,
                CorrelationId = $corr,
                UpdatedUtc = $now
            WHERE DispatchId = $id;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$status", (int)dispatchStatus);
        SqliteSqlBuilder.AddParam(cmd, "$http", httpStatusCode);
        SqliteSqlBuilder.AddParam(cmd, "$err", lastError);
        SqliteSqlBuilder.AddParam(cmd, "$corr", correlationId);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$id", dispatchId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task IncrementAttemptAndScheduleRetryAsync(string dispatchId, DateTimeOffset utcNow, TimeSpan? nextRetryDelay, CancellationToken cancellationToken)
    {
        var nextRetryIso = nextRetryDelay is null ? null : SqliteUtc.ToIso(utcNow.Add(nextRetryDelay.Value));

        await using var cmd = CreateCommand(
            """
            UPDATE Dispatch
            SET AttemptCount = AttemptCount + 1,
                NextRetryUtc = $next,
                UpdatedUtc = $now
            WHERE DispatchId = $id;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$next", nextRetryIso);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$id", dispatchId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecoverInFlightAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        // Mark any Dispatch rows left in InFlight as Failed with LastError='App restart during request' (diagnostics).
        await using var cmd = CreateCommand(
            """
            UPDATE Dispatch
            SET DispatchStatus = $failed,
                LastError = 'App restart during request',
                UpdatedUtc = $now
            WHERE DispatchStatus = $inFlight;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$failed", (int)DispatchStatus.Failed);
        SqliteSqlBuilder.AddParam(cmd, "$inFlight", (int)DispatchStatus.InFlight);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetNextSequenceNoAsync(long batchId, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT COALESCE(MAX(SequenceNo), 0) + 1
            FROM Dispatch
            WHERE BatchId = $bid;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$bid", batchId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<DispatchRow?> GetAsync(string dispatchId, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT DispatchId, ProviderDhsCode, BatchId, BcrId, SequenceNo, DispatchType, DispatchStatus,
                   AttemptCount, NextRetryUtc, RequestSizeBytes, RequestGzip, HttpStatusCode, LastError,
                   CorrelationId, CreatedUtc, UpdatedUtc
            FROM Dispatch
            WHERE DispatchId = $id;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$id", dispatchId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new DispatchRow(
                DispatchId: reader.GetString(0),
                ProviderDhsCode: reader.GetString(1),
                BatchId: reader.GetInt64(2),
                BcrId: reader.IsDBNull(3) ? null : reader.GetString(3),
                SequenceNo: reader.GetInt32(4),
                DispatchType: (DispatchType)reader.GetInt32(5),
                DispatchStatus: (DispatchStatus)reader.GetInt32(6),
                AttemptCount: reader.GetInt32(7),
                NextRetryUtc: reader.IsDBNull(8) ? null : SqliteUtc.FromIso(reader.GetString(8)),
                RequestSizeBytes: reader.IsDBNull(9) ? null : reader.GetInt32(9),
                RequestGzip: reader.GetBoolean(10),
                HttpStatusCode: reader.IsDBNull(11) ? null : reader.GetInt32(11),
                LastError: reader.IsDBNull(12) ? null : reader.GetString(12),
                CorrelationId: reader.IsDBNull(13) ? null : reader.GetString(13),
                CreatedUtc: SqliteUtc.FromIso(reader.GetString(14)),
                UpdatedUtc: SqliteUtc.FromIso(reader.GetString(15))
            );
        }

        return null;
    }


    public async Task<IReadOnlyList<DispatchRow>> GetRecentForBatchAsync(long batchId, int limit, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT DispatchId, ProviderDhsCode, BatchId, BcrId, SequenceNo, DispatchType, DispatchStatus,
                   AttemptCount, NextRetryUtc, RequestSizeBytes, RequestGzip, HttpStatusCode, LastError,
                   CorrelationId, CreatedUtc, UpdatedUtc
            FROM Dispatch
            WHERE BatchId = $bid
            ORDER BY CreatedUtc DESC
            LIMIT $lim;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$bid", batchId);
        SqliteSqlBuilder.AddParam(cmd, "$lim", limit);

        var results = new List<DispatchRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DispatchRow(
                reader.GetString(0), reader.GetString(1), reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt32(4), (DispatchType)reader.GetInt32(5),
                (DispatchStatus)reader.GetInt32(6), reader.GetInt32(7), reader.IsDBNull(8) ? null : SqliteUtc.FromIso(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.GetBoolean(10), reader.IsDBNull(11) ? null : reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13),
                SqliteUtc.FromIso(reader.GetString(14)), SqliteUtc.FromIso(reader.GetString(15))));
        }
        return results;
    }

    public async Task<bool> HasRecentRetryAsync(long batchId, IReadOnlyList<int> proIdClaims, DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        if (proIdClaims.Count == 0) return false;

        // Check if there is a Dispatch of type RetrySend created after sinceUtc for the given batch
        // AND that dispatch has exactly the same set of claims.
        // Doing this in pure SQL is possible but might be complex with array comparisons.
        // We'll fetch the recent retry dispatches and check their items in memory.

        await using var cmd = CreateCommand(
            """
            SELECT d.DispatchId
            FROM Dispatch d
            WHERE d.BatchId = $bid
              AND d.DispatchType = $type
              AND d.CreatedUtc >= $since;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$bid", batchId);
        SqliteSqlBuilder.AddParam(cmd, "$type", (int)DispatchType.RetrySend);
        SqliteSqlBuilder.AddParam(cmd, "$since", SqliteUtc.ToIso(sinceUtc));

        var recentDispatchIds = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                recentDispatchIds.Add(reader.GetString(0));
            }
        }

        if (recentDispatchIds.Count == 0) return false;

        var targetClaimsSet = proIdClaims.ToHashSet();

        // Check items for each recent dispatch
        foreach (var id in recentDispatchIds)
        {
            await using var itemCmd = CreateCommand(
                """
                SELECT ProIdClaim
                FROM DispatchItem
                WHERE DispatchId = $id;
                """);
            SqliteSqlBuilder.AddParam(itemCmd, "$id", id);

            var claimsInDispatch = new List<int>();
            await using (var itemReader = await itemCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await itemReader.ReadAsync(cancellationToken))
                {
                    claimsInDispatch.Add(itemReader.GetInt32(0));
                }
            }

            if (claimsInDispatch.Count == targetClaimsSet.Count && claimsInDispatch.All(c => targetClaimsSet.Contains(c)))
            {
                return true;
            }
        }

        return false;
    }
}
