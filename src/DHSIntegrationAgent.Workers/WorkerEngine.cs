using System.Threading;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Contracts.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class WorkerEngine : IWorkerEngine, IHostedService, IDisposable
{
    private readonly IEnumerable<IWorker> _workers;
    private readonly ILogger<WorkerEngine> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _executingTask;

    public bool IsRunning { get; private set; }

    public event EventHandler<WorkerProgressReport>? ProgressChanged;

    public WorkerEngine(IEnumerable<IWorker> workers, ILogger<WorkerEngine> logger)
    {
        _workers = workers;
        _logger = logger;
    }

    /// <summary>
    /// Explicitly implement IHostedService.StartAsync to prevent auto-start on app boot.
    /// The engine must be started manually after login.
    /// </summary>
    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker Engine hosted service initialized (waiting for manual start).");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Explicitly implement IHostedService.StopAsync to ensure host shutdown stops the workers.
    /// </summary>
    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Host is stopping; requesting Worker Engine shutdown.");
        await StopAsync(cancellationToken);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsRunning)
            {
                _logger.LogWarning("Worker Engine is already running.");
                return;
            }

            _logger.LogInformation("Starting Worker Engine...");

            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _executingTask = RunWorkersAsync(_cts.Token);

            IsRunning = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
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
                // Bounded stop: Wait for workers to finish, but no longer than 10 seconds
                // or until the provided cancellation token triggers.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

                try
                {
                    var timeoutTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
                    var completedTask = await Task.WhenAny(_executingTask, timeoutTask);

                    if (completedTask == timeoutTask && !_executingTask.IsCompleted)
                    {
                        _logger.LogWarning("Worker Engine stop timed out or was cancelled before workers finished.");
                    }
                }
                catch (OperationCanceledException)
                {
                    // This can happen if 'ct' was already cancelled.
                }
            }

            IsRunning = false;
            _logger.LogInformation("Worker Engine stopped.");
        }
        finally
        {
            _lock.Release();
        }
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
        _lock.Dispose();
    }
}
