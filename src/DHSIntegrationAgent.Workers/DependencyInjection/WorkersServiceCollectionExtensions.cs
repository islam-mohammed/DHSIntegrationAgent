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
        // Stub for now; will be replaced by the real engine that runs Streams A–D.
        services.AddSingleton<IWorkerEngine, WorkerEngineStub>();

        return services;
    }
}
