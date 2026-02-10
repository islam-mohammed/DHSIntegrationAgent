using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class ProviderExtractionConfigRepository : SqliteRepositoryBase, IProviderExtractionConfigRepository
{
    public ProviderExtractionConfigRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task UpsertAsync(ProviderExtractionConfigRow row, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO ProviderExtractionConfig
            (ProviderCode, ClaimKeyColumnName, HeaderSourceName, DetailsSourceName,
             CustomHeaderSql, CustomServiceSql, CustomDiagnosisSql, CustomLabSql, CustomRadiologySql, CustomOpticalSql,
             Notes, UpdatedUtc)
            VALUES
            ($pc, $ck, $hs, $ds, $h, $s, $d, $l, $r, $o, $n, $u)
            ON CONFLICT(ProviderCode)
            DO UPDATE SET
                ClaimKeyColumnName = excluded.ClaimKeyColumnName,
                HeaderSourceName = excluded.HeaderSourceName,
                DetailsSourceName = excluded.DetailsSourceName,
                CustomHeaderSql = excluded.CustomHeaderSql,
                CustomServiceSql = excluded.CustomServiceSql,
                CustomDiagnosisSql = excluded.CustomDiagnosisSql,
                CustomLabSql = excluded.CustomLabSql,
                CustomRadiologySql = excluded.CustomRadiologySql,
                CustomOpticalSql = excluded.CustomOpticalSql,
                Notes = excluded.Notes,
                UpdatedUtc = excluded.UpdatedUtc;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$pc", row.ProviderCode);
        SqliteSqlBuilder.AddParam(cmd, "$ck", row.ClaimKeyColumnName);
        SqliteSqlBuilder.AddParam(cmd, "$hs", row.HeaderSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$ds", row.DetailsSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$h", row.CustomHeaderSql);
        SqliteSqlBuilder.AddParam(cmd, "$s", row.CustomServiceSql);
        SqliteSqlBuilder.AddParam(cmd, "$d", row.CustomDiagnosisSql);
        SqliteSqlBuilder.AddParam(cmd, "$l", row.CustomLabSql);
        SqliteSqlBuilder.AddParam(cmd, "$r", row.CustomRadiologySql);
        SqliteSqlBuilder.AddParam(cmd, "$o", row.CustomOpticalSql);
        SqliteSqlBuilder.AddParam(cmd, "$n", row.Notes);
        SqliteSqlBuilder.AddParam(cmd, "$u", SqliteUtc.ToIso(row.UpdatedUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProviderExtractionConfigRow?> GetAsync(ProviderKey key, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT ProviderCode, ClaimKeyColumnName, HeaderSourceName, DetailsSourceName,
                   CustomHeaderSql, CustomServiceSql, CustomDiagnosisSql, CustomLabSql, CustomRadiologySql, CustomOpticalSql,
                   Notes, UpdatedUtc
            FROM ProviderExtractionConfig
            WHERE ProviderCode = $pc
            LIMIT 1;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$pc", key.ProviderCode);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        return new ProviderExtractionConfigRow(
            ProviderCode: r.GetString(0),
            ClaimKeyColumnName: r.IsDBNull(1) ? null : r.GetString(1),
            HeaderSourceName: r.IsDBNull(2) ? null : r.GetString(2),
            DetailsSourceName: r.IsDBNull(3) ? null : r.GetString(3),
            CustomHeaderSql: r.IsDBNull(4) ? null : r.GetString(4),
            CustomServiceSql: r.IsDBNull(5) ? null : r.GetString(5),
            CustomDiagnosisSql: r.IsDBNull(6) ? null : r.GetString(6),
            CustomLabSql: r.IsDBNull(7) ? null : r.GetString(7),
            CustomRadiologySql: r.IsDBNull(8) ? null : r.GetString(8),
            CustomOpticalSql: r.IsDBNull(9) ? null : r.GetString(9),
            Notes: r.IsDBNull(10) ? null : r.GetString(10),
            UpdatedUtc: SqliteUtc.FromIso(r.GetString(11)));
    }
}
