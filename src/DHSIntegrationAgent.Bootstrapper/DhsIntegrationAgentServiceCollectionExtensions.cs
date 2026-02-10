using DHSIntegrationAgent.Adapters;
using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Infrastructure;
using DHSIntegrationAgent.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.Bootstrapper;

public static class DhsIntegrationAgentServiceCollectionExtensions
{
    /// <summary>
    /// Composition root for the agent (non-UI registrations).
    /// WPF calls only this to wire everything.
    /// </summary>
    public static IServiceCollection AddDhsIntegrationAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDhsApplication(configuration);
        services.AddDhsInfrastructure(configuration);
        services.AddDhsWorkers(configuration);
        services.AddDhsAdapters(configuration);

        return services;
    }
}
