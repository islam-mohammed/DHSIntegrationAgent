using System.Data.Common;
using System.Text;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Contracts.Adapters;
using Oracle.ManagedDataAccess.Client;

namespace DHSIntegrationAgent.Adapters;

/// <summary>
/// Provider DB factory for Oracle HIS databases.
/// Reads ProviderProfile from SQLite, decrypts the connection string in-memory,
/// and returns an OracleConnection.
///
/// Expected connection string format (Easy Connect):
///   Data Source=host:1521/SERVICE_NAME;User Id=user;Password=pass;
/// Or TNS alias (requires tnsnames.ora on the agent machine):
///   Data Source=MYALIAS;User Id=user;Password=pass;
/// </summary>
public sealed class OracleProviderDbFactory : IProviderDbFactory
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IColumnEncryptor _encryptor;

    public OracleProviderDbFactory(
        ISqliteUnitOfWorkFactory uowFactory,
        IColumnEncryptor encryptor)
    {
        _uowFactory = uowFactory;
        _encryptor = encryptor;
    }

    public async Task<ProviderDbHandle> OpenAsync(string providerDhsCode, CancellationToken cancellationToken)
    {
        var handle = await CreateAsync(providerDhsCode, cancellationToken);
        await handle.Connection.OpenAsync(cancellationToken);
        return handle;
    }

    public async Task<ProviderDbHandle> CreateAsync(string providerDhsCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerDhsCode))
            throw new ArgumentException("ProviderDhsCode is required.", nameof(providerDhsCode));

        await using var uow = await _uowFactory.CreateAsync(cancellationToken);

        var profile = await uow.ProviderProfiles.GetActiveByProviderDhsCodeAsync(providerDhsCode, cancellationToken);
        if (profile is null)
            throw new InvalidOperationException(
                $"No active ProviderProfile found for ProviderDhsCode='{providerDhsCode}'. Configure provider DB in Settings/Onboarding.");

        if (!IsOracleEngine(profile.DbEngine))
            throw new NotSupportedException(
                $"ProviderProfile.DbEngine='{profile.DbEngine}' is not supported by OracleProviderDbFactory.");

        var plaintextBytes = await _encryptor.DecryptAsync(profile.EncryptedConnectionString, cancellationToken);
        var plaintext = Encoding.UTF8.GetString(plaintextBytes);

        // OracleConnectionStringBuilder validates and normalises the connection string.
        // NOTE: Do NOT log builder.ConnectionString — it contains credentials.
        var builder = new OracleConnectionStringBuilder(plaintext);

        // Enforce a connection timeout if the stored string does not specify one.
        if (!plaintext.Contains("Connection Timeout", StringComparison.OrdinalIgnoreCase) &&
            !plaintext.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase))
        {
            builder.ConnectionTimeout = 30;
        }

        DbConnection connection = new OracleConnection(builder.ConnectionString);

        return new ProviderDbHandle(
            providerDhsCode,
            profile.ProviderCode,
            profile.DbEngine,
            profile.IntegrationType,
            connection);
    }

    private static bool IsOracleEngine(string dbEngine)
        => dbEngine.Equals("oracle", StringComparison.OrdinalIgnoreCase)
           || dbEngine.Equals("oracle_db", StringComparison.OrdinalIgnoreCase);
}
