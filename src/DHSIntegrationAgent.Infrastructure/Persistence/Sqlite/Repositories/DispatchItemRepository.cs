using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class DispatchItemRepository : SqliteRepositoryBase, IDispatchItemRepository
{
    public DispatchItemRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task InsertManyAsync(IReadOnlyList<DispatchItemRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;

        // Multi-row insert (<= 40 typical)
        var values = new List<string>(rows.Count);
        await using var cmd = CreateCommand("");

        for (var i = 0; i < rows.Count; i++)
        {
            values.Add($"($d{i},$p{i},$id{i},$ord{i},$res{i},$err{i})");
            SqliteSqlBuilder.AddParam(cmd, $"$d{i}", rows[i].DispatchId);
            SqliteSqlBuilder.AddParam(cmd, $"$p{i}", rows[i].ProviderDhsCode);
            SqliteSqlBuilder.AddParam(cmd, $"$id{i}", rows[i].ProIdClaim);
            SqliteSqlBuilder.AddParam(cmd, $"$ord{i}", rows[i].ItemOrder);
            SqliteSqlBuilder.AddParam(cmd, $"$res{i}", (int)rows[i].ItemResult);
            SqliteSqlBuilder.AddParam(cmd, $"$err{i}", rows[i].ErrorMessage);
        }

        cmd.CommandText =
            $"""
            INSERT INTO DispatchItem
            (DispatchId, ProviderDhsCode, ProIdClaim, ItemOrder, ItemResult, ErrorMessage)
            VALUES {string.Join(",", values)};
            """;

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateItemResultAsync(
        string dispatchId,
        IReadOnlyList<(ClaimKey Key, DispatchItemResult Result, string? ErrorMessage)> results,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0) return;

        // Update per row (<= 40). Correctness-first.
        foreach (var item in results)
        {
            await using var cmd = CreateCommand(
                """
                UPDATE DispatchItem
                SET ItemResult = $r,
                    ErrorMessage = $e
                WHERE DispatchId = $d
                  AND ProviderDhsCode = $p
                  AND ProIdClaim = $id;
                """);
            SqliteSqlBuilder.AddParam(cmd, "$r", (int)item.Result);
            SqliteSqlBuilder.AddParam(cmd, "$e", item.ErrorMessage);
            SqliteSqlBuilder.AddParam(cmd, "$d", dispatchId);
            SqliteSqlBuilder.AddParam(cmd, "$p", item.Key.ProviderDhsCode);
            SqliteSqlBuilder.AddParam(cmd, "$id", item.Key.ProIdClaim);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<DispatchItemRow>> GetByDispatchIdAsync(string dispatchId, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT DispatchId, ProviderDhsCode, ProIdClaim, ItemOrder, ItemResult, ErrorMessage
            FROM DispatchItem
            WHERE DispatchId = $id
            ORDER BY ItemOrder;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$id", dispatchId);

        var results = new List<DispatchItemRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DispatchItemRow(
                DispatchId: reader.GetString(0),
                ProviderDhsCode: reader.GetString(1),
                ProIdClaim: reader.GetInt32(2),
                ItemOrder: reader.GetInt32(3),
                ItemResult: (DispatchItemResult)reader.GetInt32(4),
                ErrorMessage: reader.IsDBNull(5) ? null : reader.GetString(5)
            ));
        }

        return results;
    }
}
