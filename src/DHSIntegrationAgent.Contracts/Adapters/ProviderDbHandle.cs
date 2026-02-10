using System.Data.Common;

namespace DHSIntegrationAgent.Contracts.Adapters;

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
