using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Domain.WorkStates;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class StreamAWorker : IWorker
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IFetchStageService _fetchStageService;
    private readonly ISystemClock _clock;
    private readonly ILogger<StreamAWorker> _logger;

    public string Id => "StreamA";
    public string DisplayName => "Stream A: Fetch & Stage";

    public StreamAWorker(
        ISqliteUnitOfWorkFactory uowFactory,
        IFetchStageService fetchStageService,
        ISystemClock clock,
        ILogger<StreamAWorker> logger)
    {
        _uowFactory = uowFactory;
        _fetchStageService = fetchStageService;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        _logger.LogInformation("Stream A worker started.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessReadyBatchesAsync(progress, ct);
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stream A worker loop encountered an error.");
                progress.Report(new WorkerProgressReport(Id, $"Error: {ex.Message}", IsError: true));
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }

        _logger.LogInformation("Stream A worker stopped.");
    }

    private async Task ProcessReadyBatchesAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        IReadOnlyList<BatchRow> batchesToProcess;
        var providerCodes = new HashSet<string>();

        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            // Stream A processes Draft batches (newly created) and Ready batches (manually triggered or BCR ensure needed).
            var draftBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Draft, ct);
            var readyBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Ready, ct);

            var all = new List<BatchRow>(draftBatches.Count + readyBatches.Count);
            all.AddRange(draftBatches);
            all.AddRange(readyBatches);
            batchesToProcess = all;
        }

        foreach (var batch in batchesToProcess)
        {
            if (ct.IsCancellationRequested) break;

            providerCodes.Add(batch.ProviderDhsCode);

            try
            {
                await _fetchStageService.ProcessBatchAsync(batch, progress, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process batch {BatchId}.", batch.BatchId);
                await using var uow = await _uowFactory.CreateAsync(ct);
                await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Failed, null, ex.Message, _clock.UtcNow, ct);
                await uow.CommitAsync(ct);
            }
        }

        // IMPORTANT: Do not spawn Task.Run writers here.
        // Posting missing mappings used to run in parallel via Task.Run, which created concurrent SQLite writers
        // and was a frequent source of SQLITE_BUSY ("database is locked") during Stream A staging.
        //
        // For stability, we post sequentially per provider after staging completes.
        // (A dedicated poster worker can be introduced later under WBS 4.6.)
        foreach (var providerCode in providerCodes)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await _fetchStageService.PostMissingMappingsAsync(providerCode, progress, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Missing mapping poster failed for provider {ProviderDhsCode}", providerCode);
            }
        }
    }
}
