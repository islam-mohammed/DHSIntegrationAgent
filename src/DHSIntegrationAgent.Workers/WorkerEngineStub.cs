using DHSIntegrationAgent.Application.Abstractions;

namespace DHSIntegrationAgent.Workers;

public sealed class WorkerEngineStub : IWorkerEngine
{
    public bool IsRunning { get; private set; }

    public Task StartAsync(CancellationToken ct)
    {
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        IsRunning = false;
        return Task.CompletedTask;
    }
}
