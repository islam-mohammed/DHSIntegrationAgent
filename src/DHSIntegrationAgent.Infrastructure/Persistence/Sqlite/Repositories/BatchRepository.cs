using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
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
            WHERE ProviderDhsCode = $p
              AND CompanyCode = $c
              AND MonthKey = $m
              AND (StartDateUtc = $s OR (StartDateUtc IS NULL AND $s IS NULL))
              AND (EndDateUtc = $e OR (EndDateUtc IS NULL AND $e IS NULL))
            LIMIT 1;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", key.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$c", key.CompanyCode);
        SqliteSqlBuilder.AddParam(cmd, "$m", key.MonthKey);
        SqliteSqlBuilder.AddParam(cmd, "$s", SqliteUtc.ToIso(key.StartDateUtc));
        SqliteSqlBuilder.AddParam(cmd, "$e", SqliteUtc.ToIso(key.EndDateUtc));

        var obj = await cmd.ExecuteScalarAsync(cancellationToken);
        return obj is null || obj == DBNull.Value ? null : Convert.ToInt64(obj);
    }

    public async Task<BatchRow?> GetByIdAsync(long batchId, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT BatchId, ProviderDhsCode, CompanyCode, PayerCode, MonthKey, StartDateUtc, EndDateUtc, BcrId, BatchStatus, HasResume, CreatedUtc, UpdatedUtc, LastError
            FROM Batch
            WHERE BatchId = $id;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$id", batchId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadBatch(reader);
        }

        return null;
    }

    public async Task<BatchRow?> GetByBcrIdAsync(string bcrId, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT BatchId, ProviderDhsCode, CompanyCode, PayerCode, MonthKey, StartDateUtc, EndDateUtc, BcrId, BatchStatus, HasResume, CreatedUtc, UpdatedUtc, LastError
            FROM Batch
            WHERE BcrId = $bcr;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$bcr", bcrId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadBatch(reader);
        }

        return null;
    }

    private static BatchRow ReadBatch(DbDataReader reader)
    {
        return new BatchRow(
            BatchId: reader.GetInt64(0),
            ProviderDhsCode: reader.GetString(1),
            CompanyCode: reader.GetString(2),
            PayerCode: reader.IsDBNull(3) ? null : reader.GetString(3),
            MonthKey: reader.GetString(4),
            StartDateUtc: reader.IsDBNull(5) ? null : SqliteUtc.FromIso(reader.GetString(5)),
            EndDateUtc: reader.IsDBNull(6) ? null : SqliteUtc.FromIso(reader.GetString(6)),
            BcrId: reader.IsDBNull(7) ? null : reader.GetString(7),
            BatchStatus: (BatchStatus)reader.GetInt32(8),
            HasResume: reader.GetInt32(9) != 0,
            CreatedUtc: SqliteUtc.FromIso(reader.GetString(10)),
            UpdatedUtc: SqliteUtc.FromIso(reader.GetString(11)),
            LastError: reader.IsDBNull(12) ? null : reader.GetString(12)
        );
    }

    public async Task<long> EnsureBatchAsync(BatchKey key, BatchStatus batchStatus, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using (var insert = CreateCommand(
            """
            INSERT OR IGNORE INTO Batch
            (ProviderDhsCode, CompanyCode, MonthKey, StartDateUtc, EndDateUtc, BatchStatus, HasResume, CreatedUtc, UpdatedUtc)
            VALUES ($p, $c, $m, $s, $e, $status, 0, $now, $now);
            """))
        {
            SqliteSqlBuilder.AddParam(insert, "$p", key.ProviderDhsCode);
            SqliteSqlBuilder.AddParam(insert, "$c", key.CompanyCode);
            SqliteSqlBuilder.AddParam(insert, "$m", key.MonthKey);
            SqliteSqlBuilder.AddParam(insert, "$s", SqliteUtc.ToIso(key.StartDateUtc));
            SqliteSqlBuilder.AddParam(insert, "$e", SqliteUtc.ToIso(key.EndDateUtc));
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

    public async Task UpdateStatusAsync(
        long batchId,
        BatchStatus status,
        bool? hasResume,
        string? lastError,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken,
        string? payerCode = null)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE Batch
            SET BatchStatus = $s,
                HasResume = COALESCE($hr, HasResume),
                LastError = $err,
                PayerCode = COALESCE($pc, PayerCode),
                UpdatedUtc = $now
            WHERE BatchId = $id;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$s", (int)status);
        SqliteSqlBuilder.AddParam(cmd, "$hr", hasResume is null ? null : (hasResume.Value ? 1 : 0));
        SqliteSqlBuilder.AddParam(cmd, "$err", lastError);
        SqliteSqlBuilder.AddParam(cmd, "$pc", payerCode);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$id", batchId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BatchRow>> ListByStatusAsync(BatchStatus status, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT BatchId, ProviderDhsCode, CompanyCode, PayerCode, MonthKey, StartDateUtc, EndDateUtc, BcrId, BatchStatus, HasResume, CreatedUtc, UpdatedUtc, LastError
            FROM Batch
            WHERE BatchStatus = $status;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$status", (int)status);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<BatchRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new BatchRow(
                BatchId: reader.GetInt64(0),
                ProviderDhsCode: reader.GetString(1),
                CompanyCode: reader.GetString(2),
                PayerCode: reader.IsDBNull(3) ? null : reader.GetString(3),
                MonthKey: reader.GetString(4),
                StartDateUtc: reader.IsDBNull(5) ? null : SqliteUtc.FromIso(reader.GetString(5)),
                EndDateUtc: reader.IsDBNull(6) ? null : SqliteUtc.FromIso(reader.GetString(6)),
                BcrId: reader.IsDBNull(7) ? null : reader.GetString(7),
                BatchStatus: (BatchStatus)reader.GetInt32(8),
                HasResume: reader.GetInt32(9) != 0,
                CreatedUtc: SqliteUtc.FromIso(reader.GetString(10)),
                UpdatedUtc: SqliteUtc.FromIso(reader.GetString(11)),
                LastError: reader.IsDBNull(12) ? null : reader.GetString(12)
            ));
        }

        return results;
    }
}
