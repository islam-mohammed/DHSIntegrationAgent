using System.Security.Cryptography;
using DHSIntegrationAgent.Application.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Security;

internal sealed class CryptoKeyInitializationHostedService : IHostedService
{
    private readonly ISqliteConnectionFactory _connFactory;
    private readonly IKeyProtector _protector;
    private readonly ILogger<CryptoKeyInitializationHostedService> _logger;

    public CryptoKeyInitializationHostedService(
        ISqliteConnectionFactory connFactory,
        IKeyProtector protector,
        ILogger<CryptoKeyInitializationHostedService> logger)
    {
        _connFactory = connFactory;
        _protector = protector;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _connFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        // Check if active key exists
        await using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT ActiveKeyId, ActiveKeyDpapiBlob FROM AppMeta WHERE Id = 1;";
            await using var r = await check.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
                throw new InvalidOperationException("AppMeta row missing (Id=1).");

            var keyId = r.IsDBNull(0) ? null : r.GetString(0);
            var blobExists = !r.IsDBNull(1);

            if (!string.IsNullOrWhiteSpace(keyId) && blobExists)
            {
                await tx.CommitAsync(cancellationToken);
                return; // already initialized
            }
        }

        // Create new 256-bit key
        var newKey = new byte[32];
        RandomNumberGenerator.Fill(newKey);

        var newKeyId = Guid.NewGuid().ToString("D");
        var createdUtc = DateTimeOffset.UtcNow.ToString("O");
        var dpapiBlob = _protector.Protect(newKey);

        // Store protected blob only
        await using (var update = conn.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText =
                """
                UPDATE AppMeta
                SET ActiveKeyId = $id,
                    ActiveKeyCreatedUtc = $created,
                    ActiveKeyDpapiBlob = $blob
                WHERE Id = 1;
                """;
            var p1 = update.CreateParameter(); p1.ParameterName = "$id"; p1.Value = newKeyId; update.Parameters.Add(p1);
            var p2 = update.CreateParameter(); p2.ParameterName = "$created"; p2.Value = createdUtc; update.Parameters.Add(p2);
            var p3 = update.CreateParameter(); p3.ParameterName = "$blob"; p3.Value = dpapiBlob; update.Parameters.Add(p3);

            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        // Log only safe metadata (never log key bytes/blob)
        _logger.LogInformation("Encryption key initialized. ActiveKeyId={KeyId}", newKeyId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
