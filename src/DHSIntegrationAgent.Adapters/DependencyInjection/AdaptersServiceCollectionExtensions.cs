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
        // This release supports SQL Server provider HIS databases.
        services.AddSingleton<IProviderDbFactory, SqlServerProviderDbFactory>();
        services.AddSingleton<IProviderTablesAdapter, ProviderTablesAdapter>();

        return services;
    }
}
