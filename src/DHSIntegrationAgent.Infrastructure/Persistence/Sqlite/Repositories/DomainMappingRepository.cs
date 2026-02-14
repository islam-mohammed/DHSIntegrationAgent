using DHSIntegrationAgent.Contracts.Persistence;
using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class DomainMappingRepository : SqliteRepositoryBase, IDomainMappingRepository
{
    public DomainMappingRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task<IReadOnlyList<ApprovedDomainMappingRow>> GetAllApprovedAsync(CancellationToken cancellationToken)
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
                Notes,
                ProviderDomainCode,
                IsDefault,
                CodeValue,
                DisplayValue
            FROM ApprovedDomainMapping
            ORDER BY DomainMappingId DESC;
            """);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<ApprovedDomainMappingRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ApprovedDomainMappingRow(
                DomainMappingId: reader.GetInt64(0),
                ProviderDhsCode: reader.GetString(1),
                DomainName: reader.GetString(2),
                DomainTableId: reader.GetInt32(3),
                SourceValue: reader.GetString(4),
                TargetValue: reader.GetString(5),
                DiscoveredUtc: SqliteUtc.FromIso(reader.GetString(6)),
                LastPostedUtc: reader.IsDBNull(7) ? null : SqliteUtc.FromIso(reader.GetString(7)),
                LastUpdatedUtc: SqliteUtc.FromIso(reader.GetString(8)),
                Notes: reader.IsDBNull(9) ? null : reader.GetString(9),
                ProviderDomainCode: reader.IsDBNull(10) ? null : reader.GetString(10),
                IsDefault: reader.IsDBNull(11) ? null : reader.GetInt32(11) != 0,
                CodeValue: reader.IsDBNull(12) ? null : reader.GetString(12),
                DisplayValue: reader.IsDBNull(13) ? null : reader.GetString(13)
            ));
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> ListProvidersWithPendingMappingsAsync(CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            """
            SELECT DISTINCT ProviderDhsCode
            FROM MissingDomainMapping
            WHERE domainStatus IN ($m, $f);
            """);

        SqliteSqlBuilder.AddParam(cmd, "$m", (int)MappingStatus.Missing);
        SqliteSqlBuilder.AddParam(cmd, "$f", (int)MappingStatus.PostFailed);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var results = new List<string>();

        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async Task<IReadOnlyList<MissingDomainMappingRow>> GetAllMissingAsync(CancellationToken cancellationToken)
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
                domainStatus,
                DiscoveredUtc,
                LastPostedUtc,
                LastUpdatedUtc,
                Notes,
                ProviderNameValue,
                DomainTableName
            FROM MissingDomainMapping
            ORDER BY MissingMappingId DESC;
            """);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<MissingDomainMappingRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MissingDomainMappingRow(
                MissingMappingId: reader.GetInt64(0),
                ProviderDhsCode: reader.GetString(1),
                DomainName: reader.GetString(2),
                DomainTableId: reader.GetInt32(3),
                SourceValue: reader.GetString(4),
                DiscoverySource: (DiscoverySource)reader.GetInt32(5),
                MappingStatus: (MappingStatus)reader.GetInt32(6),
                DiscoveredUtc: SqliteUtc.FromIso(reader.GetString(7)),
                LastPostedUtc: reader.IsDBNull(8) ? null : SqliteUtc.FromIso(reader.GetString(8)),
                LastUpdatedUtc: SqliteUtc.FromIso(reader.GetString(9)),
                Notes: reader.IsDBNull(10) ? null : reader.GetString(10),
                ProviderNameValue: reader.IsDBNull(11) ? null : reader.GetString(11),
                DomainTableName: reader.IsDBNull(12) ? null : reader.GetString(12)
            ));
        }

        return results;
    }

    public async Task<IReadOnlyList<MissingDomainMappingRow>> ListEligibleForPostingAsync(string providerDhsCode, CancellationToken ct)
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
                domainStatus,
                DiscoveredUtc,
                LastPostedUtc,
                LastUpdatedUtc,
                Notes,
                ProviderNameValue,
                DomainTableName
            FROM MissingDomainMapping
            WHERE ProviderDhsCode = $p
              AND domainStatus IN ($m, $f)
            ORDER BY MissingMappingId ASC;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$m", (int)MappingStatus.Missing);
        SqliteSqlBuilder.AddParam(cmd, "$f", (int)MappingStatus.PostFailed);

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
                MappingStatus: (MappingStatus)reader.GetInt32(6),
                DiscoveredUtc: SqliteUtc.FromIso(reader.GetString(7)),
                LastPostedUtc: reader.IsDBNull(8) ? null : SqliteUtc.FromIso(reader.GetString(8)),
                LastUpdatedUtc: SqliteUtc.FromIso(reader.GetString(9)),
                Notes: reader.IsDBNull(10) ? null : reader.GetString(10),
                ProviderNameValue: reader.IsDBNull(11) ? null : reader.GetString(11),
                DomainTableName: reader.IsDBNull(12) ? null : reader.GetString(12)
            ));
        }

        return results;
    }

    public async Task<IReadOnlyList<DomainMappingRow>> GetAllAsync(CancellationToken cancellationToken)
    {
        var approved = await GetAllApprovedAsync(cancellationToken);
        var missing = await GetAllMissingAsync(cancellationToken);

        var results = new List<DomainMappingRow>();

        foreach (var a in approved)
        {
            results.Add(new DomainMappingRow(
                DomainMappingId: a.DomainMappingId,
                ProviderDhsCode: a.ProviderDhsCode,
                CompanyCode: "DEFAULT",
                DomainName: a.DomainName,
                DomainTableId: a.DomainTableId,
                SourceValue: a.SourceValue,
                TargetValue: a.TargetValue,
                MappingStatus: MappingStatus.Approved,
                DiscoveredUtc: a.DiscoveredUtc,
                LastPostedUtc: a.LastPostedUtc,
                LastUpdatedUtc: a.LastUpdatedUtc,
                Notes: a.Notes
            ));
        }

        foreach (var m in missing)
        {
            results.Add(new DomainMappingRow(
                DomainMappingId: m.MissingMappingId,
                ProviderDhsCode: m.ProviderDhsCode,
                CompanyCode: "DEFAULT",
                DomainName: m.DomainName,
                DomainTableId: m.DomainTableId,
                SourceValue: m.SourceValue,
                TargetValue: null,
                MappingStatus: m.MappingStatus,
                DiscoveredUtc: m.DiscoveredUtc,
                LastPostedUtc: m.LastPostedUtc,
                LastUpdatedUtc: m.LastUpdatedUtc,
                Notes: m.Notes
            ));
        }

        return results.OrderByDescending(x => x.LastUpdatedUtc).ToList();
    }

    public async Task UpsertDiscoveredAsync(
        string providerDhsCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        DiscoverySource discoverySource,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken,
        string? providerNameValue = null,
        string? domainTableName = null)
    {
        if (await IsAlreadyApprovedAsync(providerDhsCode, domainTableId, sourceValue, cancellationToken))
            return;

        await using var cmd = CreateCommand(
            """
            INSERT INTO MissingDomainMapping
            (ProviderDhsCode, DomainName, DomainTableId, SourceValue, DiscoverySource, domainStatus, DiscoveredUtc, LastUpdatedUtc,
             ProviderNameValue, DomainTableName)
            VALUES
            ($p, $dn, $dt, $sv, $ds, $ms, $now, $now, $pnv, $dtn)
            ON CONFLICT(ProviderDhsCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                DiscoverySource = excluded.DiscoverySource,
                LastUpdatedUtc = excluded.LastUpdatedUtc,
                ProviderNameValue = COALESCE(excluded.ProviderNameValue, MissingDomainMapping.ProviderNameValue),
                DomainTableName = COALESCE(excluded.DomainTableName, MissingDomainMapping.DomainTableName);
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$dn", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$ds", (int)discoverySource);
        SqliteSqlBuilder.AddParam(cmd, "$ms", (int)MappingStatus.Missing);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);
        SqliteSqlBuilder.AddParam(cmd, "$pnv", providerNameValue);
        SqliteSqlBuilder.AddParam(cmd, "$dtn", domainTableName);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertApprovedAsync(
        string providerDhsCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        string targetValue,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken,
        string? providerDomainCode = null,
        bool? isDefault = null,
        string? codeValue = null,
        string? displayValue = null)
    {
        await using var cmd = CreateCommand(
            """
            INSERT INTO ApprovedDomainMapping
            (ProviderDhsCode, DomainName, DomainTableId, SourceValue, TargetValue, DiscoveredUtc, LastUpdatedUtc,
             ProviderDomainCode, IsDefault, CodeValue, DisplayValue)
            VALUES
            ($p, $dn, $dt, $sv, $tv, $now, $now, $pdc, $def, $cv, $dv)
            ON CONFLICT(ProviderDhsCode, DomainTableId, SourceValue)
            DO UPDATE SET
                DomainName = excluded.DomainName,
                TargetValue = excluded.TargetValue,
                LastUpdatedUtc = excluded.LastUpdatedUtc,
                ProviderDomainCode = COALESCE(excluded.ProviderDomainCode, ApprovedDomainMapping.ProviderDomainCode),
                IsDefault = COALESCE(excluded.IsDefault, ApprovedDomainMapping.IsDefault),
                CodeValue = COALESCE(excluded.CodeValue, ApprovedDomainMapping.CodeValue),
                DisplayValue = COALESCE(excluded.DisplayValue, ApprovedDomainMapping.DisplayValue);
            """);

        var nowIso = SqliteUtc.ToIso(utcNow);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$dn", domainName);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);
        SqliteSqlBuilder.AddParam(cmd, "$tv", targetValue);
        SqliteSqlBuilder.AddParam(cmd, "$now", nowIso);
        SqliteSqlBuilder.AddParam(cmd, "$pdc", providerDomainCode);
        SqliteSqlBuilder.AddParam(cmd, "$def", isDefault is null ? null : (isDefault.Value ? 1 : 0));
        SqliteSqlBuilder.AddParam(cmd, "$cv", codeValue);
        SqliteSqlBuilder.AddParam(cmd, "$dv", displayValue);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await RemoveFromMissingAsync(providerDhsCode, domainTableId, sourceValue, cancellationToken);
    }

    public async Task UpsertApprovedFromProviderConfigAsync(
        string providerDhsCode,
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
        await UpsertApprovedAsync(
            providerDhsCode,
            domainName,
            domTableId,
            providerDomainValue,
            dhsDomainValue.ToString(),
            utcNow,
            cancellationToken,
            providerDomainCode,
            isDefault,
            codeValue,
            displayValue);
    }

    public async Task UpsertMissingFromProviderConfigAsync(
        string providerDhsCode,
        int domainTableId,
        string domainName,
        string providerCodeValue,
        string providerNameValue,
        string? domainTableName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        await UpsertDiscoveredAsync(
            providerDhsCode,
            domainName,
            domainTableId,
            providerCodeValue,
            DiscoverySource.Api,
            utcNow,
            cancellationToken,
            providerNameValue,
            domainTableName);
    }

    public async Task UpdateMissingStatusAsync(
        long missingMappingId,
        MappingStatus status,
        DateTimeOffset utcNow,
        DateTimeOffset? lastPostedUtc,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE MissingDomainMapping
            SET domainStatus = $status,
                LastPostedUtc = COALESCE($lp, LastPostedUtc),
                LastUpdatedUtc = $now
            WHERE MissingMappingId = $id;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$status", (int)status);
        SqliteSqlBuilder.AddParam(cmd, "$lp", lastPostedUtc is null ? null : SqliteUtc.ToIso(lastPostedUtc.Value));
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$id", missingMappingId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateApprovedStatusAsync(
        long domainMappingId,
        string? targetValue,
        DateTimeOffset utcNow,
        DateTimeOffset? lastPostedUtc,
        string? notes,
        CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE ApprovedDomainMapping
            SET TargetValue = COALESCE($tv, TargetValue),
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

    public async Task<bool> ExistsAsync(string providerDhsCode, int domainTableId, string sourceValue, CancellationToken ct)
    {
        if (await IsAlreadyApprovedAsync(providerDhsCode, domainTableId, sourceValue, ct))
            return true;

        await using var cmd = CreateCommand(
            "SELECT 1 FROM MissingDomainMapping WHERE ProviderDhsCode = $p AND DomainTableId = $dt AND SourceValue = $sv LIMIT 1;");
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }

    private async Task<bool> IsAlreadyApprovedAsync(string providerDhsCode, int domainTableId, string sourceValue, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            "SELECT 1 FROM ApprovedDomainMapping WHERE ProviderDhsCode = $p AND DomainTableId = $dt AND SourceValue = $sv LIMIT 1;");
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }

    private async Task RemoveFromMissingAsync(string providerDhsCode, int domainTableId, string sourceValue, CancellationToken ct)
    {
        await using var cmd = CreateCommand(
            "DELETE FROM MissingDomainMapping WHERE ProviderDhsCode = $p AND DomainTableId = $dt AND SourceValue = $sv;");
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$dt", domainTableId);
        SqliteSqlBuilder.AddParam(cmd, "$sv", sourceValue);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
