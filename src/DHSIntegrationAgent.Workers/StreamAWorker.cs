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

        // Perform one-time migration of legacy Ready batches to ensure they are verified/completed.
        await MigrateLegacyBatchesAsync(ct);

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

    private async Task MigrateLegacyBatchesAsync(CancellationToken ct)
    {
        try
        {
            IReadOnlyList<BatchRow> legacyReadyBatches;
            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                legacyReadyBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Ready, ct);
            }

            if (legacyReadyBatches.Count > 0)
            {
                _logger.LogInformation("Found {Count} legacy Ready batches. Migrating to ensure staging completion...", legacyReadyBatches.Count);
                int migrated = 0;

                foreach (var batch in legacyReadyBatches)
                {
                    if (ct.IsCancellationRequested) break;

                    // If BcrId is missing, it's a Draft (created but not synced).
                    // If BcrId is present, it might be interrupted. Move to Fetching to re-verify.
                    var newStatus = string.IsNullOrEmpty(batch.BcrId) ? BatchStatus.Draft : BatchStatus.Fetching;

                    try
                    {
                        await using var uow = await _uowFactory.CreateAsync(ct);
                        await uow.Batches.UpdateStatusAsync(batch.BatchId, newStatus, null, null, _clock.UtcNow, ct);
                        await uow.CommitAsync(ct);
                        migrated++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to migrate legacy batch {BatchId}", batch.BatchId);
                    }
                }
                _logger.LogInformation("Migrated {Count} batches.", migrated);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during legacy batch migration.");
        }
    }

    private async Task ProcessReadyBatchesAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        IReadOnlyList<BatchRow> batchesToProcess;
        var providerCodes = new HashSet<string>();

        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            // Stream A processes Draft (new) and Fetching (resumed/migrated) batches.
            // It EXCLUDES Ready batches to avoid race conditions with Stream B.
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
