using DHSIntegrationAgent.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.Workers;

public static class WorkersServiceCollectionExtensions
{
    public static IServiceCollection AddDhsWorkers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Real engine that orchestrates background workers.
        services.AddSingleton<WorkerEngine>();
        services.AddSingleton<IWorkerEngine>(sp => sp.GetRequiredService<WorkerEngine>());
        services.AddHostedService(sp => sp.GetRequiredService<WorkerEngine>());

        return services;
    }
}
