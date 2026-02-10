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
                DomainName,
                DomainTableId,
                SourceValue,
                TargetValue,
                DiscoveredUtc,
                LastPostedUtc,
                LastUpdatedUtc,
                Notes
            FROM ApprovedDomainMapping
            ORDER BY DomainMappingId DESC;
            """);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<DomainMappingRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DomainMappingRow(
                DomainMappingId: reader.GetInt64(0),
                ProviderDhsCode: reader.GetString(1),
                DomainName: reader.GetString(2),
                DomainTableId: reader.GetInt32(3),
                SourceValue: reader.GetString(4),
                TargetValue: reader.GetString(5),
                DiscoveredUtc: SqliteUtc.FromIso(reader.GetString(6)),
                LastPostedUtc: reader.IsDBNull(7) ? null : SqliteUtc.FromIso(reader.GetString(7)),
                LastUpdatedUtc: SqliteUtc.FromIso(reader.GetString(8)),
                Notes: reader.IsDBNull(9) ? null : reader.GetString(9)
            ));
        }

        return results;
    }

    public async Task UpsertApprovedAsync(
        string providerDhsCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        string targetValue,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO ApprovedDomainMapping
            (ProviderDhsCode, DomainName, DomainTableId, SourceValue, TargetValue, DiscoveredUtc, LastUpdatedUtc)
            VALUES
            ($p, $dn, $dt, $sv, $tv, $now, $now)
            ON CONFLICT(ProviderDhsCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                TargetValue = excluded.TargetValue,
                LastUpdatedUtc = excluded.LastUpdatedUtc;
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$dn", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$tv", targetValue);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        long domainMappingId,
        string targetValue,
        DateTimeOffset utcNow,
        DateTimeOffset? lastPostedUtc,
        string? notes,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE ApprovedDomainMapping
            SET TargetValue = $tv,
                LastPostedUtc = COALESCE($lp, LastPostedUtc),
                LastUpdatedUtc = $now,
                Notes = $notes
            WHERE DomainMappingId = $id;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$tv", targetValue);
        SqliteSqlBuilder.AddParam(cmd, "$lp", lastPostedUtc is null ? null : SqliteUtc.ToIso(lastPostedUtc.Value));
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$notes", notes);
        SqliteSqlBuilder.AddParam(cmd, "$id", domainMappingId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
