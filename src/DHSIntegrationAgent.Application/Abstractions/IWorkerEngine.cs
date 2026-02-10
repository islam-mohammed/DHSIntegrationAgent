namespace DHSIntegrationAgent.Application.Abstractions;

public interface IWorkerEngine
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
