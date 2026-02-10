using DHSIntegrationAgent.Contracts.Adapters;

namespace DHSIntegrationAgent.Adapters;

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
