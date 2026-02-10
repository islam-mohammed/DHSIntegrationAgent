using System.Data.Common;
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
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO Dispatch
            (DispatchId, ProviderDhsCode, BatchId, BcrId, SequenceNo, DispatchType, DispatchStatus, CreatedUtc, UpdatedUtc)
            VALUES
            ($id, $p, $bid, $bcr, $seq, $type, $status, $now, $now);
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$id", dispatchId);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$bid", batchId);
        SqliteSqlBuilder.AddParam(cmd, "$bcr", bcrId);
        SqliteSqlBuilder.AddParam(cmd, "$seq", sequenceNo);
        SqliteSqlBuilder.AddParam(cmd, "$type", (int)dispatchType);
        SqliteSqlBuilder.AddParam(cmd, "$status", (int)dispatchStatus);
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
}
