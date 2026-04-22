using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DHSIntegrationAgent.Adapters.Tables;

namespace DHSIntegrationAgent.Adapters;

public static class AdaptersServiceCollectionExtensions
{
    public static IServiceCollection AddDhsAdapters(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // WBS 3.1: Provider DB connection factory.
        // Engine-specific factories are registered directly so RoutingProviderDbFactory
        // can inject them without going through the IProviderDbFactory abstraction.
        services.AddSingleton<SqlServerProviderDbFactory>();
        services.AddSingleton<OracleProviderDbFactory>();
        services.AddSingleton<IProviderDbFactory, RoutingProviderDbFactory>();
        services.AddSingleton<IProviderTablesAdapter, ProviderTablesAdapter>();

        return services;
    }
}
