using System.Data.Common;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Application.Security;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite.Repositories;

internal sealed class ClaimPayloadRepository : SqliteRepositoryBase, IClaimPayloadRepository
{
    private readonly IColumnEncryptor _encryptor;

    public ClaimPayloadRepository(DbConnection conn, DbTransaction tx, IColumnEncryptor encryptor)
        : base(conn, tx)
    {
        _encryptor = encryptor;
    }

    public async Task UpsertAsync(ClaimPayloadRow row, CancellationToken cancellationToken)
    {
        // Encrypt PHI before storing
        var encrypted = await _encryptor.EncryptAsync(row.PayloadJsonPlaintext, cancellationToken);

        await using var cmd = CreateCommand(
            """
            INSERT INTO ClaimPayload
            (ProviderDhsCode, ProIdClaim, PayloadJson, PayloadSha256, PayloadVersion, CreatedUtc, UpdatedUtc)
            VALUES
            ($p, $id, $payload, $sha, $ver, $created, $updated)
            ON CONFLICT(ProviderDhsCode, ProIdClaim)
            DO UPDATE SET
                PayloadJson = excluded.PayloadJson,
                PayloadSha256 = excluded.PayloadSha256,
                PayloadVersion = excluded.PayloadVersion,
                UpdatedUtc = excluded.UpdatedUtc;
            """);

        SqliteSqlBuilder.AddParam(cmd, "$p", row.Key.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$id", row.Key.ProIdClaim);
        SqliteSqlBuilder.AddParam(cmd, "$payload", encrypted);
        SqliteSqlBuilder.AddParam(cmd, "$sha", row.PayloadSha256);
        SqliteSqlBuilder.AddParam(cmd, "$ver", row.PayloadVersion);
        SqliteSqlBuilder.AddParam(cmd, "$created", SqliteUtc.ToIso(row.CreatedUtc));
        SqliteSqlBuilder.AddParam(cmd, "$updated", SqliteUtc.ToIso(row.UpdatedUtc));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ClaimPayloadRow?> GetAsync(ClaimKey key, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(
            """
            SELECT PayloadJson, PayloadSha256, PayloadVersion, CreatedUtc, UpdatedUtc
            FROM ClaimPayload
            WHERE ProviderDhsCode = $p AND ProIdClaim = $id
            LIMIT 1;
            """);
        SqliteSqlBuilder.AddParam(cmd, "$p", key.ProviderDhsCode);
        SqliteSqlBuilder.AddParam(cmd, "$id", key.ProIdClaim);

        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;

        var encrypted = (byte[])r["PayloadJson"];
        var plaintext = await _encryptor.DecryptAsync(encrypted, cancellationToken);

        return new ClaimPayloadRow(
            Key: key,
            PayloadJsonPlaintext: plaintext,
            PayloadSha256: r.GetString(1),
            PayloadVersion: r.GetInt32(2),
            CreatedUtc: SqliteUtc.FromIso(r.GetString(3)),
            UpdatedUtc: SqliteUtc.FromIso(r.GetString(4)));
    }
}
