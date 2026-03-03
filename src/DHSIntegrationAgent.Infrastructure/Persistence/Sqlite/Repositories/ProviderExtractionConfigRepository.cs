using DHSIntegrationAgent.Contracts.Persistence;
﻿using System.Data.Common;
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
            (ProviderCode, ClaimKeyColumnName, DateColumnName, HeaderSourceName, DetailsSourceName, DoctorSourceName, ServiceSourceName, DiagnosisSourceName, LabSourceName, RadiologySourceName, AttachmentSourceName, OpticalSourceName, ItemDetailsSourceName, AchiSourceName,
             CustomHeaderSql, CustomServiceSql, CustomDiagnosisSql, CustomLabSql, CustomRadiologySql, CustomOpticalSql, CustomItemSql, CustomAchiSql,
             Notes, UpdatedUtc)
            VALUES
            ($pc, $ck, $dc, $hs, $ds, $doc, $srv, $diag, $lab, $rad, $att, $opt, $itm, $achi, $h, $s, $d, $l, $r, $o, $ci, $ca, $n, $u)
            ON CONFLICT(ProviderCode)
            DO UPDATE SET
                ClaimKeyColumnName = excluded.ClaimKeyColumnName,
                DateColumnName = excluded.DateColumnName,
                HeaderSourceName = excluded.HeaderSourceName,
                DetailsSourceName = excluded.DetailsSourceName,
                DoctorSourceName = excluded.DoctorSourceName,
                ServiceSourceName = excluded.ServiceSourceName,
                DiagnosisSourceName = excluded.DiagnosisSourceName,
                LabSourceName = excluded.LabSourceName,
                RadiologySourceName = excluded.RadiologySourceName,
                AttachmentSourceName = excluded.AttachmentSourceName,
                OpticalSourceName = excluded.OpticalSourceName,
                ItemDetailsSourceName = excluded.ItemDetailsSourceName,
                AchiSourceName = excluded.AchiSourceName,
                CustomHeaderSql = excluded.CustomHeaderSql,
                CustomServiceSql = excluded.CustomServiceSql,
                CustomDiagnosisSql = excluded.CustomDiagnosisSql,
                CustomLabSql = excluded.CustomLabSql,
                CustomRadiologySql = excluded.CustomRadiologySql,
                CustomOpticalSql = excluded.CustomOpticalSql,
                CustomItemSql = excluded.CustomItemSql,
                CustomAchiSql = excluded.CustomAchiSql,
                Notes = excluded.Notes,
                UpdatedUtc = excluded.UpdatedUtc;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$pc", row.ProviderCode);
        SqliteSqlBuilder.AddParam(cmd, "$ck", row.ClaimKeyColumnName);
        SqliteSqlBuilder.AddParam(cmd, "$dc", row.DateColumnName);
        SqliteSqlBuilder.AddParam(cmd, "$hs", row.HeaderSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$ds", row.DetailsSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$doc", row.DoctorSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$srv", row.ServiceSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$diag", row.DiagnosisSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$lab", row.LabSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$rad", row.RadiologySourceName);
        SqliteSqlBuilder.AddParam(cmd, "$att", row.AttachmentSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$opt", row.OpticalSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$itm", row.ItemDetailsSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$achi", row.AchiSourceName);
        SqliteSqlBuilder.AddParam(cmd, "$h", row.CustomHeaderSql);
        SqliteSqlBuilder.AddParam(cmd, "$s", row.CustomServiceSql);
        SqliteSqlBuilder.AddParam(cmd, "$d", row.CustomDiagnosisSql);
        SqliteSqlBuilder.AddParam(cmd, "$l", row.CustomLabSql);
        SqliteSqlBuilder.AddParam(cmd, "$r", row.CustomRadiologySql);
        SqliteSqlBuilder.AddParam(cmd, "$o", row.CustomOpticalSql);
        SqliteSqlBuilder.AddParam(cmd, "$ci", row.CustomItemSql);
        SqliteSqlBuilder.AddParam(cmd, "$ca", row.CustomAchiSql);
        SqliteSqlBuilder.AddParam(cmd, "$n", row.Notes);
        SqliteSqlBuilder.AddParam(cmd, "$u", SqliteUtc.ToIso(row.UpdatedUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProviderExtractionConfigRow?> GetAsync(ProviderKey key, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT ProviderCode, ClaimKeyColumnName, DateColumnName, HeaderSourceName, DetailsSourceName, DoctorSourceName, ServiceSourceName, DiagnosisSourceName, LabSourceName, RadiologySourceName, AttachmentSourceName, OpticalSourceName, ItemDetailsSourceName, AchiSourceName,
                   CustomHeaderSql, CustomServiceSql, CustomDiagnosisSql, CustomLabSql, CustomRadiologySql, CustomOpticalSql, CustomItemSql, CustomAchiSql,
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
            DateColumnName: r.IsDBNull(2) ? null : r.GetString(2),
            HeaderSourceName: r.IsDBNull(3) ? null : r.GetString(3),
            DetailsSourceName: r.IsDBNull(4) ? null : r.GetString(4),
            DoctorSourceName: r.IsDBNull(5) ? null : r.GetString(5),
            ServiceSourceName: r.IsDBNull(6) ? null : r.GetString(6),
            DiagnosisSourceName: r.IsDBNull(7) ? null : r.GetString(7),
            LabSourceName: r.IsDBNull(8) ? null : r.GetString(8),
            RadiologySourceName: r.IsDBNull(9) ? null : r.GetString(9),
            AttachmentSourceName: r.IsDBNull(10) ? null : r.GetString(10),
            OpticalSourceName: r.IsDBNull(11) ? null : r.GetString(11),
            ItemDetailsSourceName: r.IsDBNull(12) ? null : r.GetString(12),
            AchiSourceName: r.IsDBNull(13) ? null : r.GetString(13),
            CustomHeaderSql: r.IsDBNull(14) ? null : r.GetString(14),
            CustomServiceSql: r.IsDBNull(15) ? null : r.GetString(15),
            CustomDiagnosisSql: r.IsDBNull(16) ? null : r.GetString(16),
            CustomLabSql: r.IsDBNull(17) ? null : r.GetString(17),
            CustomRadiologySql: r.IsDBNull(18) ? null : r.GetString(18),
            CustomOpticalSql: r.IsDBNull(19) ? null : r.GetString(19),
            CustomItemSql: r.IsDBNull(20) ? null : r.GetString(20),
            CustomAchiSql: r.IsDBNull(21) ? null : r.GetString(21),
            Notes: r.IsDBNull(22) ? null : r.GetString(22),
            UpdatedUtc: SqliteUtc.FromIso(r.GetString(23)));
    }
}
