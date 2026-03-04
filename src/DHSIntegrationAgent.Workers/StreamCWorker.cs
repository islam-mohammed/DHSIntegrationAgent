using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Workers;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class StreamCWorker : IWorker
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IDispatchService _dispatchService;
    private readonly IBatchRegistry _batchRegistry;
    private readonly ISystemClock _clock;
    private readonly ILogger<StreamCWorker> _logger;

    public string Id => "StreamC";
    public string DisplayName => "Stream C: Automatic Retry";

    public StreamCWorker(
        ISqliteUnitOfWorkFactory uowFactory,
        IDispatchService dispatchService,
        IBatchRegistry batchRegistry,
        ISystemClock clock,
        ILogger<StreamCWorker> logger)
    {
        _uowFactory = uowFactory;
        _dispatchService = dispatchService;
        _batchRegistry = batchRegistry;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        _logger.LogInformation("Stream C worker started.");

        while (!ct.IsCancellationRequested)
        {
            var loopDelay = TimeSpan.FromSeconds(60);
            try
            {
                await ProcessEligibleBatchesAsync(progress, ct);

                await Task.Delay(loopDelay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stream C worker loop encountered an error.");
                progress.Report(new WorkerProgressReport(Id, $"Error: {ex.Message}", IsError: true));
                await Task.Delay(loopDelay, ct);
            }
        }

        _logger.LogInformation("Stream C worker stopped.");
    }

    private async Task ProcessEligibleBatchesAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        IReadOnlyList<BatchRow> batchesToProcess;

        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            batchesToProcess = await uow.Batches.GetBatchesWithDueRetriesAsync(_clock.UtcNow, 4, ct);
        }

        foreach (var batch in batchesToProcess)
        {
            if (ct.IsCancellationRequested) break;

            if (_batchRegistry.IsRegistered(batch.BatchId))
            {
                _logger.LogInformation("Skipping batch {BatchId} as it is currently being processed by another task.", batch.BatchId);
                continue;
            }

            try
            {
                await _dispatchService.AutomaticRetryBatchAsync(batch, progress, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process automatic retry for batch {BatchId}.", batch.BatchId);
            }
        }
    }
}
