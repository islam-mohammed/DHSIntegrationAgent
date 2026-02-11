using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Contracts.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class WorkerEngine : IWorkerEngine, IHostedService, IDisposable
{
    private readonly IEnumerable<IWorker> _workers;
    private readonly ILogger<WorkerEngine> _logger;
    private CancellationTokenSource? _cts;
    private Task? _executingTask;

    public bool IsRunning { get; private set; }

    public event EventHandler<WorkerProgressReport>? ProgressChanged;

    public WorkerEngine(IEnumerable<IWorker> workers, ILogger<WorkerEngine> logger)
    {
        _workers = workers;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Worker Engine is already running.");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting Worker Engine...");

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _executingTask = RunWorkersAsync(_cts.Token);

        IsRunning = true;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!IsRunning)
        {
            _logger.LogWarning("Worker Engine is not running.");
            return;
        }

        _logger.LogInformation("Stopping Worker Engine...");

        _cts?.Cancel();

        if (_executingTask != null)
        {
            // Note: Task.WhenAny does not throw if the tasks are cancelled.
            await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, ct));
        }

        IsRunning = false;
        _logger.LogInformation("Worker Engine stopped.");
    }

    private async Task RunWorkersAsync(CancellationToken ct)
    {
        var progress = new Progress<WorkerProgressReport>(report => ProgressChanged?.Invoke(this, report));

        var workerTasks = _workers.Select(worker => Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Worker {WorkerId} ({DisplayName}) starting...", worker.Id, worker.DisplayName);
                await worker.ExecuteAsync(progress, ct);
                _logger.LogInformation("Worker {WorkerId} ({DisplayName}) finished.", worker.Id, worker.DisplayName);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker {WorkerId} ({DisplayName}) was cancelled.", worker.Id, worker.DisplayName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {WorkerId} ({DisplayName}) failed.", worker.Id, worker.DisplayName);
                ProgressChanged?.Invoke(this, new WorkerProgressReport(worker.Id, $"Error: {ex.Message}", IsError: true));
            }
        }, ct)).ToList();

        await Task.WhenAll(workerTasks);
    }

    public void Dispose()
    {
        _cts?.Dispose();
    }
}
