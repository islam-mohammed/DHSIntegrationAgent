using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Providers;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Domain.WorkStates;
using DHSIntegrationAgent.App.UI.ViewModels;

namespace DHSIntegrationAgent.App.UI.Services;

/// <summary>
/// Implementation of IBatchTracker that manages BatchProgressViewModels and background tasks.
/// </summary>
public sealed class BatchTracker : IBatchTracker
{
    private readonly IFetchStageService _fetchStageService;
    private readonly IAttachmentDispatchService _attachmentDispatchService;
    private readonly IWorkerEngine _workerEngine;
    private readonly IDispatchService _dispatchService;
    private readonly IBatchRegistry _batchRegistry;
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;

    // Throttling for persistence to avoid "database is locked" during high-frequency updates
    private readonly ConcurrentDictionary<long, DateTimeOffset> _lastPersistTime = new();
    private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(2);

    public ObservableCollection<BatchProgressViewModel> ActiveBatches { get; } = new();
    object IBatchTracker.ActiveBatches => ActiveBatches;

    public BatchTracker(
        IFetchStageService fetchStageService,
        IAttachmentDispatchService attachmentDispatchService,
        IWorkerEngine workerEngine,
        IDispatchService dispatchService,
        IBatchRegistry batchRegistry,
        ISqliteUnitOfWorkFactory unitOfWorkFactory)
    {
        _fetchStageService = fetchStageService ?? throw new ArgumentNullException(nameof(fetchStageService));
        _attachmentDispatchService = attachmentDispatchService ?? throw new ArgumentNullException(nameof(attachmentDispatchService));
        _workerEngine = workerEngine ?? throw new ArgumentNullException(nameof(workerEngine));
        _dispatchService = dispatchService ?? throw new ArgumentNullException(nameof(dispatchService));
        _batchRegistry = batchRegistry ?? throw new ArgumentNullException(nameof(batchRegistry));
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));

        _workerEngine.ProgressChanged += OnWorkerProgressChanged;
    }

    private void OnWorkerProgressChanged(object? sender, WorkerProgressReport report)
    {
        if (report.BatchId.HasValue)
        {
            var batchId = report.BatchId.Value;
            var progressViewModel = ActiveBatches.FirstOrDefault(x => x.InternalBatchId == batchId);
            if (progressViewModel != null)
            {
                int processed = 0;
                int total = 0;
                int percentage = 0;
                string message = "";

                // UI Update
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (report.WorkerId == "StreamB") progressViewModel.IsSending = true;
                    if (report.Percentage.HasValue) progressViewModel.PercentageOverride = report.Percentage.Value;
                    if (report.ProcessedCount.HasValue) progressViewModel.ProcessedClaims = report.ProcessedCount.Value;
                    if (report.TotalCount.HasValue) progressViewModel.TotalClaims = report.TotalCount.Value;
                    if (report.Message != null) progressViewModel.StatusMessage = report.Message;
                    if (report.IsError) progressViewModel.IsError = true;

                    if (report.Percentage >= 100)
                    {
                        progressViewModel.IsCompleted = true;
                    }

                    // Capture state safely on UI thread
                    processed = progressViewModel.ProcessedClaims;
                    total = progressViewModel.TotalClaims;
                    percentage = (int)(progressViewModel.PercentageOverride ?? 0);
                    message = progressViewModel.StatusMessage ?? "";
                });

                // Persistence Update (Fire and forget to not block UI or Worker)
                // Throttle updates to avoid SQLite write storms
                bool shouldPersist = false;
                var now = DateTimeOffset.UtcNow;

                if (report.IsError || (report.Percentage >= 100) || (report.Message != null && report.Message.StartsWith("Error")))
                {
                    shouldPersist = true;
                }
                else
                {
                    var lastTime = _lastPersistTime.GetOrAdd(batchId, DateTimeOffset.MinValue);
                    if ((now - lastTime) > PersistInterval)
                    {
                        shouldPersist = true;
                    }
                }

                if (shouldPersist)
                {
                    _lastPersistTime[batchId] = now;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var uow = await _unitOfWorkFactory.CreateAsync(default);
                            await uow.Batches.UpdateProgressAsync(
                                batchId,
                                processed,
                                total,
                                percentage,
                                message,
                                DateTimeOffset.UtcNow,
                                default
                            );

                            await uow.CommitAsync(default);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to persist batch progress: {ex.Message}");
                        }
                    });
                }
            }
        }
    }

    public async Task RestoreActiveBatchesAsync()
    {
        try
        {
            await using var uow = await _unitOfWorkFactory.CreateAsync(default);

            // Fetch batches in active states
            var fetchingBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Fetching, default);
            var sendingBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Sending, default);

            // Combine active batches
            var activeBatches = fetchingBatches.Concat(sendingBatches);

            // Update UI on Dispatcher
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var batch in activeBatches)
                {
                    // Skip if already tracked
                    if (ActiveBatches.Any(x => x.InternalBatchId == batch.BatchId))
                        continue;

                    var progressViewModel = new BatchProgressViewModel
                    {
                        InternalBatchId = batch.BatchId,
                        BatchNumber = batch.BcrId ?? $"Batch {batch.BatchId}",
                        StatusMessage = batch.CurrentStageMessage ?? "Resumed...",
                        TotalClaims = batch.TotalClaims,
                        ProcessedClaims = batch.ProcessedClaims,
                        PercentageOverride = batch.Percentage,
                        IsSending = batch.BatchStatus == BatchStatus.Sending
                    };

                    ActiveBatches.Add(progressViewModel);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error restoring active batches: {ex.Message}");
        }
    }

    public void TrackAttachmentUpload(IEnumerable<BatchRow> batches)
    {
        foreach (var batch in batches)
        {
            var progressViewModel = new BatchProgressViewModel
            {
                InternalBatchId = batch.BatchId,
                BatchNumber = batch.BcrId ?? $"Batch {batch.BatchId}",
                StatusMessage = "Initializing Attachment Upload...",
                TotalClaims = 0, // Will be updated during process
            };

            // Ensure we add to collection on UI thread
            System.Windows.Application.Current?.Dispatcher.Invoke(() => ActiveBatches.Add(progressViewModel));

            // Run the background task for attachments
            _ = Task.Run(async () =>
            {
                try
                {
                    _batchRegistry.Register(batch.BatchId);

                    var progress = new Progress<WorkerProgressReport>(report =>
                    {
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            if (report.Percentage.HasValue) progressViewModel.PercentageOverride = report.Percentage.Value;
                            if (report.ProcessedCount.HasValue) progressViewModel.ProcessedClaims = report.ProcessedCount.Value;
                            if (report.TotalCount.HasValue) progressViewModel.TotalClaims = report.TotalCount.Value;
                            if (report.Message != null) progressViewModel.StatusMessage = report.Message;
                        });
                    });

                    await _attachmentDispatchService.ProcessAttachmentsForBatchesAsync(new[] { batch.BatchId }, progress, default);

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        progressViewModel.StatusMessage = "Attachments uploaded successfully.";
                        progressViewModel.IsCompleted = true;
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        progressViewModel.StatusMessage = $"Attachment Error: {ex.Message}";
                        progressViewModel.IsError = true;
                    });
                }
                finally
                {
                    _batchRegistry.Unregister(batch.BatchId);
                }
            });
        }
    }

    public void TrackBatchRetry(BatchRow batch, Action<RetryBatchResult> onCompletion)
    {
        var progressViewModel = new BatchProgressViewModel
        {
            InternalBatchId = batch.BatchId,
            BatchNumber = batch.BcrId ?? $"Batch {batch.BatchId}",
            StatusMessage = "Initializing Retry...",
            TotalClaims = 0,
            PercentageOverride = 60
        };

        // Ensure we add to collection on UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var existing = ActiveBatches.FirstOrDefault(x => x.InternalBatchId == batch.BatchId);
            if (existing != null) ActiveBatches.Remove(existing);
            ActiveBatches.Add(progressViewModel);
        });

        _ = Task.Run(async () =>
        {
            RetryBatchResult result = new RetryBatchResult(0, 0, 0);
            try
            {
                _batchRegistry.Register(batch.BatchId);

                var progress = new Progress<WorkerProgressReport>(report =>
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (report.Percentage.HasValue) progressViewModel.PercentageOverride = report.Percentage.Value;
                        if (report.ProcessedCount.HasValue) progressViewModel.ProcessedClaims = report.ProcessedCount.Value;
                        if (report.TotalCount.HasValue) progressViewModel.TotalClaims = report.TotalCount.Value;
                        if (report.Message != null) progressViewModel.StatusMessage = report.Message;
                    });
                });

                result = await _dispatchService.RetryBatchAsync(batch, progress, default);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    progressViewModel.StatusMessage = "Retry complete.";
                    progressViewModel.IsCompleted = true;
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    progressViewModel.StatusMessage = $"Retry Error: {ex.Message}";
                    progressViewModel.IsError = true;
                });
            }
            finally
            {
                _batchRegistry.Unregister(batch.BatchId);
                System.Windows.Application.Current?.Dispatcher.Invoke(() => onCompletion(result));
            }
        });
    }

    public void TrackBatchCreation(BatchRow batch, FinancialSummary? financialSummary)
    {
        var progressViewModel = new BatchProgressViewModel
        {
            InternalBatchId = batch.BatchId,
            BatchNumber = batch.BcrId ?? "Pending...",
            StatusMessage = "Validating Financial Information",
            TotalClaims = financialSummary?.TotalClaims ?? 0,
        };

        // Ensure we add to collection on UI thread
        if (System.Windows.Application.Current?.Dispatcher != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = ActiveBatches.FirstOrDefault(x => x.InternalBatchId == batch.BatchId);
                if (existing != null)
                {
                    ActiveBatches.Remove(existing);
                }
                ActiveBatches.Add(progressViewModel);
            });
        }
        else
        {
            var existing = ActiveBatches.FirstOrDefault(x => x.InternalBatchId == batch.BatchId);
            if (existing != null)
            {
                ActiveBatches.Remove(existing);
            }
            ActiveBatches.Add(progressViewModel);
        }

        // Run the background task (Stream A: Fetch & Stage)
        _ = Task.Run(async () =>
        {
            try
            {
                _batchRegistry.Register(batch.BatchId);

                var progress = new Progress<WorkerProgressReport>(report =>
                {
                    // Update progressViewModel on UI thread
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (report.BcrId != null) progressViewModel.BatchNumber = report.BcrId;
                        if (report.Percentage.HasValue) progressViewModel.PercentageOverride = report.Percentage.Value;
                        if (report.ProcessedCount.HasValue) progressViewModel.ProcessedClaims = report.ProcessedCount.Value;
                        if (report.TotalCount.HasValue) progressViewModel.TotalClaims = report.TotalCount.Value;
                        if (report.Message != null) progressViewModel.StatusMessage = report.Message;
                    });
                });

                // 1. Process Batch (Obtain BCR ID, Fetch Claims, Stage)
                // Note: FetchStageService updates status to Fetching internally
                await _fetchStageService.ProcessBatchAsync(batch, progress, default);

                // 2. Post any discovered missing mappings
                await _fetchStageService.PostMissingMappingsAsync(batch.ProviderDhsCode, progress, default);

                // 3. Ensure Worker Engine is running to process the batch (Stream B)
                if (!_workerEngine.IsRunning)
                {
                    await _workerEngine.StartAsync(default);
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    progressViewModel.StatusMessage = "Staged and ready. Sending...";
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    progressViewModel.StatusMessage = $"Error: {ex.Message}";
                    progressViewModel.IsError = true;
                });
            }
            finally
            {
                _batchRegistry.Unregister(batch.BatchId);
            }
        });
    }
}
