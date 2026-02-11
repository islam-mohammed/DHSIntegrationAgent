using DHSIntegrationAgent.Contracts.Workers;

namespace DHSIntegrationAgent.Application.Abstractions;

public interface IWorker
{
    string Id { get; }
    string DisplayName { get; }
    Task ExecuteAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct);
}
