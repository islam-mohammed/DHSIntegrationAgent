using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class PayerProfileRepository : SqliteRepositoryBase, IPayerProfileRepository
{
    public PayerProfileRepository(DbConnection conn, DbTransaction tx) : base(conn, tx) { }

    public async Task UpsertManyAsync(IReadOnlyList<PayerProfileRow> rows, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;

        foreach (var row in rows)
        {
            await using var cmd = CreateCommand(
                """
                INSERT INTO PayerProfile
                (ProviderDhsCode, CompanyCode, PayerCode, PayerName, IsActive, CreatedUtc, UpdatedUtc)
                VALUES
                ($p, $cc, $pc, $pn, $a, $now, $now)
                ON CONFLICT(ProviderDhsCode, CompanyCode)
                DO UPDATE SET
                    PayerCode = excluded.PayerCode,
                    PayerName = excluded.PayerName,
                    IsActive = excluded.IsActive,
                    UpdatedUtc = excluded.UpdatedUtc;
                """);
            SqliteSqlBuilder.AddParam(cmd, "$p", row.ProviderDhsCode);
            SqliteSqlBuilder.AddParam(cmd, "$cc", row.CompanyCode);
            SqliteSqlBuilder.AddParam(cmd, "$pc", row.PayerCode);
            SqliteSqlBuilder.AddParam(cmd, "$pn", row.PayerName);
            SqliteSqlBuilder.AddParam(cmd, "$a", row.IsActive ? 1 : 0);
            SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<PayerProfileRow>> ListActiveAsync(string providerDhsCode, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT ProviderDhsCode, CompanyCode, PayerCode, PayerName, IsActive
            FROM PayerProfile
            WHERE ProviderDhsCode = $p AND IsActive = 1
            ORDER BY CompanyCode;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);

        var list = new List<PayerProfileRow>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new PayerProfileRow(
                ProviderDhsCode: r.GetString(0),
                CompanyCode: r.GetString(1),
                PayerCode: r.IsDBNull(2) ? null : r.GetString(2),
                PayerName: r.IsDBNull(3) ? null : r.GetString(3),
                IsActive: r.GetInt32(4) == 1));
        }
        return list;
    }

    public async Task SetActiveAsync(string providerDhsCode, string companyCode, bool isActive, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            UPDATE PayerProfile
            SET IsActive = $a, UpdatedUtc = $now
            WHERE ProviderDhsCode = $p AND CompanyCode = $cc;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$a", isActive ? 1 : 0);
        SqliteSqlBuilder.AddParam(cmd, "$now", SqliteUtc.ToIso(utcNow));
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$cc", companyCode);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PayerItem>> GetAllPayersAsync(string providerDhsCode, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT PayerId, PayerCode, PayerName
            FROM PayerProfile
            WHERE ProviderDhsCode = $p AND IsActive = 1
            ORDER BY PayerName;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", providerDhsCode);

        var list = new List<PayerItem>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var localPayerId = r.GetInt32(0);
            var payerCode = r.IsDBNull(1) ? null : r.GetString(1);
            var payerName = r.IsDBNull(2) ? null : r.GetString(2);


            var effectivePayerId = localPayerId;
            if (!string.IsNullOrWhiteSpace(payerCode) && int.TryParse(payerCode, out var parsed))
                effectivePayerId = parsed;

            list.Add(new PayerItem(
                PayerId: effectivePayerId,
                PayerName: payerName));
        }
        return list;
    }
}
