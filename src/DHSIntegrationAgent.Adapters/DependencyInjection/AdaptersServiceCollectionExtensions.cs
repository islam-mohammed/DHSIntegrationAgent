using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Adapters.Views;

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
        services.AddSingleton<IProviderViewsAdapter, ProviderViewsAdapter>();
        services.AddSingleton<ISchemaMapperService, SchemaMapperService>();

        return services;
    }
}
