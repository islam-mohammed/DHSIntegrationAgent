using System.Security.Cryptography;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

namespace DHSIntegrationAgent.Infrastructure.Security;

internal sealed class SqliteKeyRing : IKeyRing
{
    private readonly ISqliteConnectionFactory _connFactory;
    private readonly IKeyProtector _protector;

    // cache avoids repeated DPAPI calls
    private KeyMaterial? _activeCache;
    private KeyMaterial? _previousCache;

    public SqliteKeyRing(ISqliteConnectionFactory connFactory, IKeyProtector protector)
    {
        _connFactory = connFactory;
        _protector = protector;
    }

    public async Task<KeyMaterial> GetActiveKeyAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);

        return _activeCache
               ?? throw new InvalidOperationException("Active encryption key missing. Key initialization should have created it.");
    }

    public async Task<KeyMaterial?> TryGetKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);

        if (_activeCache is not null && _activeCache.KeyId == keyId) return _activeCache;
        if (_previousCache is not null && _previousCache.KeyId == keyId) return _previousCache;

        return null;
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_activeCache is not null) return;

        await using var conn = await _connFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT ActiveKeyId, ActiveKeyCreatedUtc, ActiveKeyDpapiBlob,
                   PreviousKeyId, PreviousKeyCreatedUtc, PreviousKeyDpapiBlob
            FROM AppMeta WHERE Id = 1;
            """;

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            throw new InvalidOperationException("AppMeta singleton missing. Run migrations.");

        _activeCache = ReadKey(r, 0);
        _previousCache = ReadKey(r, 3);
    }

    private KeyMaterial? ReadKey(System.Data.Common.DbDataReader r, int baseIndex)
    {
        var keyId = r.IsDBNull(baseIndex) ? null : r.GetString(baseIndex);
        if (string.IsNullOrWhiteSpace(keyId)) return null;

        var createdIso = r.GetString(baseIndex + 1);
        var dpapiBlob = (byte[])r[baseIndex + 2];
        var keyBytes = _protector.Unprotect(dpapiBlob);

        if (keyBytes.Length != 32)
            throw new InvalidOperationException($"Invalid key length ({keyBytes.Length}). Expected 32 bytes.");

        return new KeyMaterial(keyId, DateTimeOffset.Parse(createdIso), keyBytes);
    }
}
