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
    private readonly IBatchRegistry _batchRegistry;

    public string Id => "StreamA";
    public string DisplayName => "Stream A: Fetch & Stage";

    public StreamAWorker(
        ISqliteUnitOfWorkFactory uowFactory,
        IFetchStageService fetchStageService,
        ISystemClock clock,
        ILogger<StreamAWorker> logger,
        IBatchRegistry batchRegistry)
    {
        _uowFactory = uowFactory;
        _fetchStageService = fetchStageService;
        _clock = clock;
        _logger = logger;
        _batchRegistry = batchRegistry;
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
            // Stream A processes Draft (new) and Fetching (resumed) batches.
            // It MUST NOT process Ready batches (those are for Stream B).
            var draftBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Draft, ct);
            var fetchingBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Fetching, ct);

            var all = new List<BatchRow>(draftBatches.Count + fetchingBatches.Count);
            all.AddRange(draftBatches);
            all.AddRange(fetchingBatches);
            batchesToProcess = all;
        }

        foreach (var batch in batchesToProcess)
        {
            if (ct.IsCancellationRequested) break;

            // Check if this batch is currently being processed manually by BatchTracker (or another mechanism)
            if (_batchRegistry.IsRegistered(batch.BatchId))
            {
                _logger.LogInformation("Skipping batch {BatchId} as it is currently being processed by another task.", batch.BatchId);
                continue;
            }

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

   
        // For stability, we post sequentially per provider after staging completes.
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
