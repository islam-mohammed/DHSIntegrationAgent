using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class MissingDomainMappingRepository : SqliteRepositoryBase, IMissingDomainMappingRepository
{
    public MissingDomainMappingRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task UpsertAsync(
        string providerDhsCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        DiscoverySource discoverySource,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO MissingDomainMapping
            (ProviderDhsCode, DomainName, DomainTableId, SourceValue, DiscoverySource, DiscoveredUtc, LastUpdatedUtc)
            VALUES
            ($p, $dn, $dt, $sv, $ds, $now, $now)
            ON CONFLICT(ProviderDhsCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                DiscoverySource = excluded.DiscoverySource,
                LastUpdatedUtc = excluded.LastUpdatedUtc;
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$dn", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$ds", (int)discoverySource);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MissingDomainMappingRow>> GetByProviderAsync(string providerDhsCode, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            """
            SELECT
                MissingMappingId,
                ProviderDhsCode,
                DomainName,
                DomainTableId,
                SourceValue,
                DiscoverySource,
                DiscoveredUtc,
                LastUpdatedUtc,
                Notes
            FROM MissingDomainMapping
            WHERE ProviderDhsCode = $p
            ORDER BY MissingMappingId DESC;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<MissingDomainMappingRow>();

        while (await reader.ReadAsync(ct))
        {
            results.Add(new MissingDomainMappingRow(
                MissingMappingId: reader.GetInt64(0),
                ProviderDhsCode: reader.GetString(1),
                DomainName: reader.GetString(2),
                DomainTableId: reader.GetInt32(3),
                SourceValue: reader.GetString(4),
                DiscoverySource: (DiscoverySource)reader.GetInt32(5),
                DiscoveredUtc: SqliteUtc.FromIso(reader.GetString(6)),
                LastUpdatedUtc: SqliteUtc.FromIso(reader.GetString(7)),
                Notes: reader.IsDBNull(8) ? null : reader.GetString(8)
            ));
        }

        return results;
    }
}
