using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class BatchRepository : SqliteRepositoryBase, IBatchRepository
{
    public BatchRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task<long?> TryGetBatchIdAsync(BatchKey key, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT BatchId
            FROM Batch
            WHERE ProviderDhsCode = $p AND CompanyCode = $c AND MonthKey = $m
            LIMIT 1;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", key.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$c", key.CompanyCode);
        SqliteSqlBuilder.AddParam(cmd, "$m", key.MonthKey);

        var obj = await cmd.ExecuteScalarAsync(cancellationToken);
        return obj is null || obj == DBNull.Value ? null : Convert.ToInt64(obj);
    }

    public async Task<long> EnsureBatchAsync(BatchKey key, BatchStatus batchStatus, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using (var insert = CreateCommand(
            """
            INSERT OR IGNORE INTO Batch
            (ProviderDhsCode, CompanyCode, MonthKey, BatchStatus, HasResume, CreatedUtc, UpdatedUtc)
            VALUES ($p, $c, $m, $status, 0, $now, $now);
            """))
        {
            SqliteSqlBuilder.AddParam(insert, "$p", key.ProviderDhsCode);
            SqliteSqlBuilder.AddParam(insert, "$c", key.CompanyCode);
            SqliteSqlBuilder.AddParam(insert, "$m", key.MonthKey);
            SqliteSqlBuilder.AddParam(insert, "$status", (int)batchStatus);
            SqliteSqlBuilder.AddParam(insert, "$now", SqliteUtc.ToIso(utcNow));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var id = await TryGetBatchIdAsync(key, cancellationToken);
        return id ?? throw new InvalidOperationException("EnsureBatch failed: row not found after insert.");
    }

    public async Task SetBcrIdAsync(long batchId, string bcrId, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE Batch
            SET BcrId = $bcr, UpdatedUtc = $now
            WHERE BatchId = $id;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$bcr", bcrId);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$id", batchId);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows != 1) throw new InvalidOperationException("SetBcrId failed (expected 1 row).");
    }

    public async Task UpdateStatusAsync(long batchId, BatchStatus status, bool? hasResume, string? lastError, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE Batch
            SET BatchStatus = $s,
                HasResume = COALESCE($hr, HasResume),
                LastError = $err,
                UpdatedUtc = $now
            WHERE BatchId = $id;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$s", (int)status);
        SqliteSqlBuilder.AddParam(cmd, "$hr", hasResume is null ? null : (hasResume.Value ? 1 : 0));
        SqliteSqlBuilder.AddParam(cmd, "$err", lastError);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$id", batchId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
