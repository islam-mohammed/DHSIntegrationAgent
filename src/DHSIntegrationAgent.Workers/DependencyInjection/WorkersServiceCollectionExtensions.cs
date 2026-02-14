using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.Workers;

public static class WorkersServiceCollectionExtensions
{
    public static IServiceCollection AddDhsWorkers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Infrastructure services for workers
        services.AddSingleton<IFetchStageService, FetchStageService>();
        services.AddSingleton<IDispatchService, DispatchService>();

        // Worker implementations
        services.AddSingleton<IWorker, StreamAWorker>();
        services.AddSingleton<IWorker, StreamBWorker>();

        // Real engine that orchestrates background workers.
        services.AddSingleton<WorkerEngine>();
        services.AddSingleton<IWorkerEngine>(sp => sp.GetRequiredService<WorkerEngine>());
        services.AddHostedService(sp => sp.GetRequiredService<WorkerEngine>());

        return services;
    }
}
