using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Domain.WorkStates;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class StreamBWorker : IWorker
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IDispatchService _dispatchService;
    private readonly IBatchRegistry _batchRegistry;
    private readonly ILogger<StreamBWorker> _logger;

    public string Id => "StreamB";
    public string DisplayName => "Stream B: Sender";

    public StreamBWorker(
        ISqliteUnitOfWorkFactory uowFactory,
        IDispatchService dispatchService,
        IBatchRegistry batchRegistry,
        ILogger<StreamBWorker> logger)
    {
        _uowFactory = uowFactory;
        _dispatchService = dispatchService;
        _batchRegistry = batchRegistry;
        _logger = logger;
    }

    public async Task ExecuteAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        _logger.LogInformation("Stream B worker started.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessEligibleBatchesAsync(progress, ct);
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stream B worker loop encountered an error.");
                progress.Report(new WorkerProgressReport(Id, $"Error: {ex.Message}", IsError: true));
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }

        _logger.LogInformation("Stream B worker stopped.");
    }

    private async Task ProcessEligibleBatchesAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        IReadOnlyList<BatchRow> batchesToProcess;

        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            var readyBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Ready, ct);
            var sendingBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Sending, ct);

            var all = new List<BatchRow>(readyBatches.Count + sendingBatches.Count);
            all.AddRange(readyBatches);
            all.AddRange(sendingBatches);
            batchesToProcess = all;
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
                await _dispatchService.ProcessBatchSenderAsync(batch, progress, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process sender for batch {BatchId}.", batch.BatchId);
            }
        }
    }
}
