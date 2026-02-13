using System.Collections.ObjectModel;
using System.Windows;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Providers;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.App.UI.ViewModels;

namespace DHSIntegrationAgent.App.UI.Services;

/// <summary>
/// Implementation of IBatchTracker that manages BatchProgressViewModels and background tasks.
/// </summary>
public sealed class BatchTracker : IBatchTracker
{
    private readonly IFetchStageService _fetchStageService;
    private readonly IWorkerEngine _workerEngine;

    public ObservableCollection<BatchProgressViewModel> ActiveBatches { get; } = new();
    object IBatchTracker.ActiveBatches => ActiveBatches;

    public BatchTracker(IFetchStageService fetchStageService, IWorkerEngine workerEngine)
    {
        _fetchStageService = fetchStageService ?? throw new ArgumentNullException(nameof(fetchStageService));
        _workerEngine = workerEngine ?? throw new ArgumentNullException(nameof(workerEngine));
    }

    public void TrackBatchCreation(BatchRow batch, FinancialSummary? financialSummary)
    {
        var progressViewModel = new BatchProgressViewModel
        {
            InternalBatchId = batch.BatchId,
            BatchNumber = batch.BcrId ?? "Pending...",
            StatusMessage = "Validating Financial Information",
            TotalClaims = financialSummary?.TotalClaims ?? 0,
            FinancialMessage = financialSummary != null ?
                $"Claims: {financialSummary.TotalClaims}, Net: {financialSummary.TotalNetAmount:N2} ({(financialSummary.IsValid ? "Valid ✅" : "Invalid ❌")})"
                : "Financial validation complete."
        };

        // Ensure we add to collection on UI thread
        if (System.Windows.Application.Current?.Dispatcher != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => ActiveBatches.Add(progressViewModel));
        }
        else
        {
            ActiveBatches.Add(progressViewModel);
        }

        // Run the background task (Stream A: Fetch & Stage)
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<WorkerProgressReport>(report =>
                {
                    // Update progressViewModel on UI thread
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (report.BcrId != null) progressViewModel.BatchNumber = report.BcrId;
                        if (report.ProcessedCount.HasValue) progressViewModel.ProcessedClaims = report.ProcessedCount.Value;
                        if (report.TotalCount.HasValue) progressViewModel.TotalClaims = report.TotalCount.Value;
                        if (report.Message != null) progressViewModel.StatusMessage = report.Message;
                    });
                });

                // 1. Process Batch (Obtain BCR ID, Fetch Claims, Stage)
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
                    progressViewModel.StatusMessage = "Staged and ready.";
                    progressViewModel.IsCompleted = true;
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
        });
    }
}
