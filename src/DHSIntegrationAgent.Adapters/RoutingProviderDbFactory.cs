using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Adapters;

namespace DHSIntegrationAgent.Adapters;

/// <summary>
/// Reads DbEngine from the active ProviderProfile and delegates to the correct
/// engine-specific factory (SqlServerProviderDbFactory or OracleProviderDbFactory).
/// This is the single IProviderDbFactory registered in DI.
/// </summary>
public sealed class RoutingProviderDbFactory : IProviderDbFactory
{
    private readonly SqlServerProviderDbFactory _sqlServer;
    private readonly OracleProviderDbFactory    _oracle;
    private readonly ISqliteUnitOfWorkFactory   _uowFactory;

    public RoutingProviderDbFactory(
        SqlServerProviderDbFactory sqlServer,
        OracleProviderDbFactory    oracle,
        ISqliteUnitOfWorkFactory   uowFactory)
    {
        _sqlServer  = sqlServer;
        _oracle     = oracle;
        _uowFactory = uowFactory;
    }

    public async Task<ProviderDbHandle> OpenAsync(string providerDhsCode, CancellationToken cancellationToken)
    {
        var factory = await ResolveAsync(providerDhsCode, cancellationToken);
        return await factory.OpenAsync(providerDhsCode, cancellationToken);
    }

    public async Task<ProviderDbHandle> CreateAsync(string providerDhsCode, CancellationToken cancellationToken)
    {
        var factory = await ResolveAsync(providerDhsCode, cancellationToken);
        return await factory.CreateAsync(providerDhsCode, cancellationToken);
    }

    private async Task<IProviderDbFactory> ResolveAsync(string providerDhsCode, CancellationToken ct)
    {
        await using var uow = await _uowFactory.CreateAsync(ct);
        var profile = await uow.ProviderProfiles.GetActiveByProviderDhsCodeAsync(providerDhsCode, ct);
        if (profile is null)
            throw new InvalidOperationException(
                $"No active ProviderProfile found for ProviderDhsCode='{providerDhsCode}'.");

        return profile.DbEngine.ToLowerInvariant() switch
        {
            "oracle" or "oracle_db" => _oracle,
            "sqlserver" or "mssql" or "sql_server" or "microsoft.sqlserver" => _sqlServer,
            _ => throw new NotSupportedException(
                $"DbEngine '{profile.DbEngine}' is not supported. Expected 'sqlserver' or 'oracle'.")
        };
    }
}
