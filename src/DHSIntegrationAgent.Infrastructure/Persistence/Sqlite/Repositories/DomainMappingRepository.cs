using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class DomainMappingRepository : SqliteRepositoryBase, IDomainMappingRepository
{
    public DomainMappingRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task<IReadOnlyList<DomainMappingRow>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT
                DomainMappingId,
                ProviderDhsCode,
                CompanyCode,
                DomainName,
                DomainTableId,
                SourceValue,
                TargetValue,
                MappingStatus,
                DiscoveredUtc,
                LastPostedUtc,
                LastUpdatedUtc,
                Notes
            FROM DomainMapping
            ORDER BY DomainMappingId DESC;
            """);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<DomainMappingRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DomainMappingRow(
                DomainMappingId: reader.GetInt64(0),
                ProviderDhsCode: reader.GetString(1),
                CompanyCode: reader.GetString(2),
                DomainName: reader.GetString(3),
                DomainTableId: reader.GetInt32(4),
                SourceValue: reader.GetString(5),
                TargetValue: reader.IsDBNull(6) ? null : reader.GetString(6),
                MappingStatus: (MappingStatus)reader.GetInt32(7),
                DiscoveredUtc: SqliteUtc.FromIso(reader.GetString(8)),
                LastPostedUtc: reader.IsDBNull(9) ? null : SqliteUtc.FromIso(reader.GetString(9)),
                LastUpdatedUtc: SqliteUtc.FromIso(reader.GetString(10)),
                Notes: reader.IsDBNull(11) ? null : reader.GetString(11)
            ));
        }

        return results;
    }

    public async Task UpsertDiscoveredAsync(
        string providerDhsCode,
        string companyCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        MappingStatus mappingStatus,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO DomainMapping
            (ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue,
             MappingStatus, DiscoveredUtc, LastUpdatedUtc,
             ProviderCodeValue, ProviderNameValue, DomainTableName)
            VALUES
            ($p, $c, $dn, $dt, $sv,
             $ms, $now, $now,
             $pcv, $pnv, $dtn)
            ON CONFLICT(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                -- do not downgrade Approved rows
                MappingStatus = CASE
                    WHEN DomainMapping.MappingStatus = $approved THEN $approved
                    ELSE excluded.MappingStatus
                END,
                ProviderCodeValue = COALESCE(DomainMapping.ProviderCodeValue, excluded.ProviderCodeValue),
                ProviderNameValue = COALESCE(DomainMapping.ProviderNameValue, excluded.ProviderNameValue),
                DomainTableName = COALESCE(DomainMapping.DomainTableName, excluded.DomainTableName),
                LastUpdatedUtc = excluded.LastUpdatedUtc;
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$c", companyCode);
        SqliteSqlBuilder.AddParam(cmd, "$dn", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$ms", (int)mappingStatus);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);

        // discovered missing uses the scanned value as both code/name until user maps it
        SqliteSqlBuilder.AddParam(cmd, "$pcv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$pnv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$dtn", domainName);

        SqliteSqlBuilder.AddParam(cmd, "$approved", (int)MappingStatus.Approved);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertApprovedAsync(
        string providerDhsCode,
        string companyCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        string targetValue,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        int? parsedDhs = int.TryParse(targetValue, out var tmp) ? tmp : null;

        await using var cmd = CreateCommand(
            """
            INSERT INTO DomainMapping
            (ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue,
             TargetValue, MappingStatus, DiscoveredUtc, LastUpdatedUtc,
             ProviderDomainCode, ProviderDomainValue, DhsDomainValue)
            VALUES
            ($p, $c, $dn, $dt, $sv,
             $tv, $ms, $now, $now,
             $pdc, $pdv, $ddv)
            ON CONFLICT(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                TargetValue = excluded.TargetValue,
                MappingStatus = excluded.MappingStatus,
                ProviderDomainCode = COALESCE(excluded.ProviderDomainCode, DomainMapping.ProviderDomainCode),
                ProviderDomainValue = COALESCE(excluded.ProviderDomainValue, DomainMapping.ProviderDomainValue),
                DhsDomainValue = COALESCE(excluded.DhsDomainValue, DomainMapping.DhsDomainValue),
                LastUpdatedUtc = excluded.LastUpdatedUtc;
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$c", companyCode);
        SqliteSqlBuilder.AddParam(cmd, "$dn", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$tv", targetValue);
        SqliteSqlBuilder.AddParam(cmd, "$ms", (int)MappingStatus.Approved);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);

        SqliteSqlBuilder.AddParam(cmd, "$pdc", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$pdv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$ddv", parsedDhs);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertApprovedFromProviderConfigAsync(
        string providerDhsCode,
        string companyCode,
        int domTableId,
        string domainName,
        string providerDomainCode,
        string providerDomainValue,
        int dhsDomainValue,
        bool? isDefault,
        string? codeValue,
        string? displayValue,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO DomainMapping
            (ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue,
             TargetValue, MappingStatus, DiscoveredUtc, LastUpdatedUtc,
             ProviderDomainCode, ProviderDomainValue, DhsDomainValue, IsDefault, CodeValue, DisplayValue)
            VALUES
            ($p, $c, $dn, $dt, $sv,
             $tv, $ms, $now, $now,
             $pdc, $pdv, $ddv, $def, $cv, $dv)
            ON CONFLICT(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                TargetValue = excluded.TargetValue,
                MappingStatus = excluded.MappingStatus,
                ProviderDomainCode = excluded.ProviderDomainCode,
                ProviderDomainValue = excluded.ProviderDomainValue,
                DhsDomainValue = excluded.DhsDomainValue,
                IsDefault = excluded.IsDefault,
                CodeValue = excluded.CodeValue,
                DisplayValue = excluded.DisplayValue,
                LastUpdatedUtc = excluded.LastUpdatedUtc;
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$c", companyCode);
        SqliteSqlBuilder.AddParam(cmd, "$dn", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domTableId);

        // DomainMapping table uses SourceValue as the provider-domain value for approved items
        SqliteSqlBuilder.AddParam(cmd, "$sv", providerDomainValue);

        SqliteSqlBuilder.AddParam(cmd, "$tv", dhsDomainValue.ToString());
        SqliteSqlBuilder.AddParam(cmd, "$ms", (int)MappingStatus.Approved);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);

        SqliteSqlBuilder.AddParam(cmd, "$pdc", providerDomainCode);
        SqliteSqlBuilder.AddParam(cmd, "$pdv", providerDomainValue);
        SqliteSqlBuilder.AddParam(cmd, "$ddv", dhsDomainValue);
        SqliteSqlBuilder.AddParam(cmd, "$def", isDefault is null ? null : (isDefault.Value ? 1 : 0));
        SqliteSqlBuilder.AddParam(cmd, "$cv", codeValue);
        SqliteSqlBuilder.AddParam(cmd, "$dv", displayValue);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertMissingFromProviderConfigAsync(
        string providerDhsCode,
        string companyCode,
        int domainTableId,
        string domainName,
        string providerCodeValue,
        string providerNameValue,
        string? domainTableName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        // Server "missing" are old/known-by-server => treat as Posted to avoid repost loops.
        // Merge rule: never downgrade Approved.
        await using var cmd = CreateCommand(
            """
            INSERT INTO DomainMapping
            (ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue,
             TargetValue, MappingStatus, DiscoveredUtc, LastUpdatedUtc, LastPostedUtc,
             ProviderCodeValue, ProviderNameValue, DomainTableName)
            VALUES
            ($p, $c, $dn, $dt, $sv,
             NULL, $ms, $now, $now, $now,
             $pcv, $pnv, $dtn)
            ON CONFLICT(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                MappingStatus = CASE
                    WHEN DomainMapping.MappingStatus = $approved THEN $approved
                    ELSE DomainMapping.MappingStatus
                END,
                ProviderCodeValue = COALESCE(DomainMapping.ProviderCodeValue, excluded.ProviderCodeValue),
                ProviderNameValue = COALESCE(DomainMapping.ProviderNameValue, excluded.ProviderNameValue),
                DomainTableName = COALESCE(DomainMapping.DomainTableName, excluded.DomainTableName),
                LastPostedUtc = COALESCE(DomainMapping.LastPostedUtc, excluded.LastPostedUtc),
                LastUpdatedUtc = excluded.LastUpdatedUtc;
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);

        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$c", companyCode);
        SqliteSqlBuilder.AddParam(cmd, "$dn", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);

        // DomainMapping table uses SourceValue as the providerCodeValue for missing items
        SqliteSqlBuilder.AddParam(cmd, "$sv", providerCodeValue);

        SqliteSqlBuilder.AddParam(cmd, "$ms", (int)MappingStatus.Posted);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);

        SqliteSqlBuilder.AddParam(cmd, "$pcv", providerCodeValue);
        SqliteSqlBuilder.AddParam(cmd, "$pnv", providerNameValue);
        SqliteSqlBuilder.AddParam(cmd, "$dtn", domainTableName);

        SqliteSqlBuilder.AddParam(cmd, "$approved", (int)MappingStatus.Approved);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(
        long domainMappingId,
        MappingStatus status,
        string? targetValue,
        DateTimeOffset utcNow,
        DateTimeOffset? lastPostedUtc,
        string? notes,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE DomainMapping
            SET MappingStatus = $ms,
                TargetValue = $tv,
                LastPostedUtc = COALESCE($lp, LastPostedUtc),
                LastUpdatedUtc = $now,
                Notes = $notes
            WHERE DomainMappingId = $id;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$ms", (int)status);
        SqliteSqlBuilder.AddParam(cmd, "$tv", targetValue);
        SqliteSqlBuilder.AddParam(cmd, "$lp", lastPostedUtc is null ? null : SqliteUtc.ToIso(lastPostedUtc.Value));
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$notes", notes);
        SqliteSqlBuilder.AddParam(cmd, "$id", domainMappingId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
