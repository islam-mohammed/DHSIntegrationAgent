using System.Data.Common;
using System.Text;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Security;
using Microsoft.Data.SqlClient;

namespace DHSIntegrationAgent.Adapters;

/// <summary>
/// WBS 3.1: SQL Server provider DB factory.
/// Reads the active ProviderProfile row for ProviderDhsCode and creates a SqlConnection.
/// </summary>
public sealed class SqlServerProviderDbFactory : IProviderDbFactory
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IColumnEncryptor _encryptor;

    public SqlServerProviderDbFactory(
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

        // Decrypt connection string only in-memory
        var plaintextBytes = await _encryptor.DecryptAsync(profile.EncryptedConnectionString, cancellationToken);
        var plaintext = Encoding.UTF8.GetString(plaintextBytes);

        // Enforce secure defaults without breaking provider-specific settings.
        var builder = new SqlConnectionStringBuilder(plaintext);

        // Security hardening defaults (can be overridden explicitly in stored connection string).
        if (!builder.ContainsKey("Encrypt"))
            builder.Encrypt = true;
        if (!builder.ContainsKey("TrustServerCertificate"))
            builder.TrustServerCertificate = false;

        // Add an app name for SQL auditing/diagnostics (no PHI).
        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
            builder.ApplicationName = "DHSIntegrationAgent";

        // Defensive: make sure pooling stays enabled by default.
        if (!builder.ContainsKey("Pooling"))
            builder.Pooling = true;

        // NOTE: Do NOT log builder.ConnectionString (contains secrets).
        DbConnection connection = new SqlConnection(builder.ConnectionString);

        // Validate engine selection (this factory only supports SQL Server).
        if (!IsSqlServerEngine(profile.DbEngine))
            throw new NotSupportedException(
                $"ProviderProfile.DbEngine='{profile.DbEngine}' is not supported by SqlServerProviderDbFactory.");

        return new ProviderDbHandle(
            providerDhsCode,
            profile.ProviderCode,
            profile.DbEngine,
            profile.IntegrationType,
            connection);
    }

    private static bool IsSqlServerEngine(string dbEngine)
        => dbEngine.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)
           || dbEngine.Equals("mssql", StringComparison.OrdinalIgnoreCase)
           || dbEngine.Equals("sql_server", StringComparison.OrdinalIgnoreCase)
           || dbEngine.Equals("microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);
}
