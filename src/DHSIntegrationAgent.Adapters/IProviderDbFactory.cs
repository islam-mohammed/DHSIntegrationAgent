namespace DHSIntegrationAgent.Adapters;

using System.Data.Common;

/// <summary>
/// WBS 3.1: Provider DB connection factory.
/// Reads ProviderProfile (SQLite), decrypts the provider DB connection string in-memory,
/// and returns an ADO.NET connection for the configured engine (SQL Server in this release).
///
/// Note: ProviderDhsCode is treated as STRING throughout the solution.
/// </summary>
public interface IProviderDbFactory
{
    /// <summary>
    /// Creates a provider DB connection and opens it.
    /// </summary>
    Task<ProviderDbHandle> OpenAsync(string providerDhsCode, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a provider DB connection but does not open it.
    /// </summary>
    Task<ProviderDbHandle> CreateAsync(string providerDhsCode, CancellationToken cancellationToken);
}

/// <summary>
/// A lightweight handle that owns the provider DB connection.
/// </summary>
public sealed class ProviderDbHandle : IAsyncDisposable
{
    public ProviderDbHandle(
        string providerDhsCode,
        string providerCode,
        string dbEngine,
        string integrationType,
        DbConnection connection)
    {
        ProviderDhsCode = providerDhsCode;
        ProviderCode = providerCode;
        DbEngine = dbEngine;
        IntegrationType = integrationType;
        Connection = connection;
    }

    public string ProviderDhsCode { get; }
    public string ProviderCode { get; }
    public string DbEngine { get; }
    public string IntegrationType { get; }

    /// <summary>
    /// The underlying ADO.NET connection. May be open or closed depending on factory method.
    /// </summary>
    public DbConnection Connection { get; }

    public async ValueTask DisposeAsync()
    {
        try { await Connection.DisposeAsync(); }
        catch { /* ignore dispose errors */ }
    }
}
