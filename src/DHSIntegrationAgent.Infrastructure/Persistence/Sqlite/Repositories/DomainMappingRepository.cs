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
            (ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue, MappingStatus, DiscoveredUtc, LastUpdatedUtc)
            VALUES
            ($p, $c, $dn, $dt, $sv, $ms, $now, $now)
            ON CONFLICT(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                MappingStatus = excluded.MappingStatus,
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
        await using var cmd = CreateCommand(
            """
            INSERT INTO DomainMapping
            (ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue, TargetValue, MappingStatus, DiscoveredUtc, LastUpdatedUtc)
            VALUES
            ($p, $c, $dn, $dt, $sv, $tv, $ms, $now, $now)
            ON CONFLICT(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                TargetValue = excluded.TargetValue,
                MappingStatus = excluded.MappingStatus,
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
