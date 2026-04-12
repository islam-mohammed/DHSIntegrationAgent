using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.App.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.App.UI.Services;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class BatchesViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IBatchClient _batchClient;
    private readonly IBatchTracker _batchTracker;
    private readonly IAttachmentDispatchService _attachmentDispatchService;
    private readonly INavigationService _navigation;
    private readonly IWorkerEngine _workerEngine;
    private readonly IDeleteBatchService _deleteBatchService;
    private readonly IBatchCreationOrchestrator _batchCreationOrchestrator;

    public ObservableCollection<BatchRow> Batches { get; } = new();
    public object ActiveBatches => _batchTracker.ActiveBatches;
    public ObservableCollection<DispatchRow> DispatchHistory { get; } = new();

    private bool _hasDispatchHistory;
    public bool HasDispatchHistory
    {
        get => _hasDispatchHistory;
        set => SetProperty(ref _hasDispatchHistory, value);
    }

    private BatchRow? _selectedBatch;
    public BatchRow? SelectedBatch
    {
        get => _selectedBatch;
        set
        {
            if (SetProperty(ref _selectedBatch, value))
            {
                // Screen-only: populate placeholder detail
                DispatchHistory.Clear();
                HasDispatchHistory = false;
            }
        }
    }

    private string _filterCompanyCode = "";
    public string FilterCompanyCode { get => _filterCompanyCode; set => SetProperty(ref _filterCompanyCode, value); }

    private DateTime? _filterStartDate;
    public DateTime? FilterStartDate
    {
        get => _filterStartDate;
        set => SetProperty(ref _filterStartDate, value);
    }

    private DateTime? _filterEndDate;
    public DateTime? FilterEndDate
    {
        get => _filterEndDate;
        set => SetProperty(ref _filterEndDate, value);
    }

    private PayerItem? _selectedPayer;
    public PayerItem? SelectedPayer
    {
        get => _selectedPayer;
        set
        {
            if (SetProperty(ref _selectedPayer, value))
            {
                // Debug: Log selection changes
                System.Diagnostics.Debug.WriteLine($"SelectedPayer changed to: {value?.PayerName ?? "null"} (ID: {value?.PayerId})");
            }
        }
    }

    public ObservableCollection<PayerItem> Payers { get; } = new();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _hasFailedClaims;
    public bool HasFailedClaims
    {
        get => _hasFailedClaims;
        set => SetProperty(ref _hasFailedClaims, value);
    }

    private bool _isRetrying;
    public bool IsRetrying
    {
        get => _isRetrying;
        set => SetProperty(ref _isRetrying, value);
    }

    private bool _isResuming;
    public bool IsResuming
    {
        get => _isResuming;
        set => SetProperty(ref _isResuming, value);
    }

    public RelayCommand ApplyFilterCommand { get; }
    public RelayCommand<DispatchRow> ManualRetrySelectedPacketCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand UploadAttachmentsCommand { get; }
    public AsyncRelayCommand<BatchRow> ResumeSelectedBatchCommand { get; }
    public AsyncRelayCommand<BatchRow> DeleteSelectedBatchCommand { get; }
    public AsyncRelayCommand<BatchRow> RecreateSelectedBatchCommand { get; }
    public RelayCommand<BatchRow> ShowAttachmentsCommand { get; }
    public RelayCommand<BatchRow> ShowDispatchHistoryCommand { get; }
    public RelayCommand<BatchProgressViewModel> RemoveActiveBatchCommand { get; }

    public BatchesViewModel(
        ISqliteUnitOfWorkFactory unitOfWorkFactory,
        IBatchClient batchClient,
        IBatchTracker batchTracker,
        IAttachmentDispatchService attachmentDispatchService,
        INavigationService navigation,
        IWorkerEngine workerEngine,
        IDeleteBatchService deleteBatchService,
        IBatchCreationOrchestrator batchCreationOrchestrator)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _batchClient = batchClient ?? throw new ArgumentNullException(nameof(batchClient));
        _batchTracker = batchTracker ?? throw new ArgumentNullException(nameof(batchTracker));
        _attachmentDispatchService = attachmentDispatchService ?? throw new ArgumentNullException(nameof(attachmentDispatchService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _workerEngine = workerEngine ?? throw new ArgumentNullException(nameof(workerEngine));
        _deleteBatchService = deleteBatchService ?? throw new ArgumentNullException(nameof(deleteBatchService));
        _batchCreationOrchestrator = batchCreationOrchestrator ?? throw new ArgumentNullException(nameof(batchCreationOrchestrator));
        ShowAttachmentsCommand = new RelayCommand<BatchRow>(OnShowAttachments);
        ShowDispatchHistoryCommand = new RelayCommand<BatchRow>(OnShowDispatchHistory);
        RemoveActiveBatchCommand = new RelayCommand<BatchProgressViewModel>(OnRemoveActiveBatch);

        ApplyFilterCommand = new RelayCommand(() => { /* screen-only */ });
        ManualRetrySelectedPacketCommand = new RelayCommand<DispatchRow>(OnManualRetrySelectedPacket, CanManualRetrySelectedPacket);
        RefreshCommand = new AsyncRelayCommand(LoadBatchesAsync);
        SearchCommand = new AsyncRelayCommand(LoadBatchesAsync);
        UploadAttachmentsCommand = new AsyncRelayCommand(OnUploadAttachmentsAsync);
        ResumeSelectedBatchCommand = new AsyncRelayCommand<BatchRow>(OnResumeSelectedBatchAsync);
        DeleteSelectedBatchCommand = new AsyncRelayCommand<BatchRow>(OnDeleteSelectedBatchAsync);
        RecreateSelectedBatchCommand = new AsyncRelayCommand<BatchRow>(OnRecreateSelectedBatchAsync);

        // Initialize with default "All" option synchronously to prevent type name display in ComboBox
        var allPayer = new PayerItem { PayerId = null, PayerName = "All" };
        Payers.Add(allPayer);
        SelectedPayer = allPayer;

        _workerEngine.ProgressChanged += OnWorkerProgressChanged;
    }

    private void OnWorkerProgressChanged(object? sender, DHSIntegrationAgent.Contracts.Workers.WorkerProgressReport report)
    {
        if (report.BatchId.HasValue && report.WorkerId == "StreamB")
        {
            var batchId = report.BatchId.Value;

            _ = Task.Run(async () =>
            {
                try
                {
                    string? bcrId = null;
                    await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
                    {
                        var localBatch = await uow.Batches.GetByIdAsync(batchId, default);
                        if (localBatch != null)
                        {
                            bcrId = localBatch.BcrId;
                        }
                    }

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        var batchRow = Batches.FirstOrDefault(b => b.LocalBatchId == batchId);

                        if (batchRow == null && !string.IsNullOrEmpty(bcrId))
                        {
                            batchRow = Batches.FirstOrDefault(b => b.BcrId.ToString() == bcrId);
                        }

                        if (batchRow != null)
                        {
                            if (!string.IsNullOrEmpty(bcrId) && batchRow.BcrId == 0)
                            {
                                if (int.TryParse(bcrId, out var parsedBcrId))
                                {
                                    batchRow.BcrId = parsedBcrId;
                                }
                            }

                            if (report.Percentage < 100 && batchRow.BatchStatus != "Sending" && batchRow.BatchStatus != "Completed")
                            {
                                batchRow.BatchStatus = "Sending";
                            }
                        }

                        if (report.Percentage >= 100 || report.IsError)
                        {
                            _ = LoadBatchesAsync();
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error handling StreamB progress for batch UI update: {ex.Message}");
                }
            });
        }
    }

    public async Task InitializeAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            // Execute sequentially to avoid SQLite locking issues during startup
            // when background workers might also be accessing the database.
            await LoadPayersAsync();
            await _batchTracker.RestoreActiveBatchesAsync();
            await LoadBatchesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRetryFailedClaims(BatchRow batch) => !IsRetrying;

    private async Task CheckFailedClaimsAsync()
    {
        HasFailedClaims = false;
        if (SelectedBatch == null) return;

        try
        {
            await using var uow = await _unitOfWorkFactory.CreateAsync(default);
            var batch = await uow.Batches.GetByBcrIdAsync(SelectedBatch.BcrId.ToString(), default);
            if (batch != null)
            {
                var counts = await uow.Claims.GetBatchCountsAsync(batch.BatchId, default);
                var rawDispatches = await uow.Dispatches.GetRecentForBatchAsync(batch.BatchId, 50, default);

                // Use Dispatcher to ensure UI updates happen on the main thread
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        HasFailedClaims = counts.Failed > 0;
                        DispatchHistory.Clear();
                HasDispatchHistory = false;
                        foreach(var d in rawDispatches) {
                            if (d.DispatchStatus == DHSIntegrationAgent.Domain.WorkStates.DispatchStatus.Failed) {
                                DispatchHistory.Add(new DispatchRow { PacketId = d.DispatchId, AttemptUtc = d.CreatedUtc.ToString("g"), Result = d.DispatchStatus.ToString(), CorrelationId = d.CorrelationId ?? "" });
                            }
                        }
                        HasDispatchHistory = DispatchHistory.Count > 0;
                    });
                }
                else
                {
                    HasFailedClaims = counts.Failed > 0;
                    DispatchHistory.Clear();
                HasDispatchHistory = false;
                    foreach(var d in rawDispatches) {
                        if (d.DispatchStatus == DHSIntegrationAgent.Domain.WorkStates.DispatchStatus.Failed) {
                            DispatchHistory.Add(new DispatchRow { PacketId = d.DispatchId, AttemptUtc = d.CreatedUtc.ToString("g"), Result = d.DispatchStatus.ToString(), CorrelationId = d.CorrelationId ?? "" });
                        }
                    }
                    HasDispatchHistory = DispatchHistory.Count > 0;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking failed claims: {ex.Message}");
        }
    }


    private bool CanManualRetrySelectedPacket(DispatchRow dispatchRow) => !IsRetrying && dispatchRow != null;

    private void OnRemoveActiveBatch(BatchProgressViewModel progressItem)
    {
        if (progressItem != null)
        {
            _batchTracker.RemoveTrackedBatch(progressItem.InternalBatchId);
        }
    }

    private void OnShowDispatchHistory(BatchRow batch)
    {
        if (batch == null) return;

        SelectedBatch = batch;
        _ = CheckFailedClaimsAsync();
    }

    private void OnManualRetrySelectedPacket(DispatchRow dispatchRow)
    {
        if (dispatchRow == null) return;

        IsRetrying = true;
        ManualRetrySelectedPacketCommand.RaiseCanExecuteChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<DHSIntegrationAgent.Contracts.Workers.WorkerProgressReport>(report =>
                {
                });

                var result = await ((App)System.Windows.Application.Current).ServiceHost!.Services
                    .GetRequiredService<IDispatchService>()
                    .ManualRetryDispatchPacketAsync(dispatchRow.PacketId, progress, default);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsRetrying = false;
                    ManualRetrySelectedPacketCommand.RaiseCanExecuteChanged();

                    if (result.Succeeded)
                    {
                        MessageBox.Show(
                            $"Manual Retry Complete.\n\n" +
                            $"Retried: {result.RetriedCount}\n" +
                            $"Success: {result.SuccessCount}\n" +
                            $"Failed: {result.FailedCount}",
                            "Retry Summary",
                            MessageBoxButton.OK,
                            result.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Retry failed: {result.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsRetrying = false;
                    ManualRetrySelectedPacketCommand.RaiseCanExecuteChanged();
                    MessageBox.Show($"Error starting manual retry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        });
    }

        private void OnShowAttachments(BatchRow batch)
    {
        if (batch == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                long? localBatchId = null;
                await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
                {
                    var localBatch = await uow.Batches.GetByBcrIdAsync(batch.BcrId.ToString(), default);
                    if (localBatch != null)
                    {
                        localBatchId = localBatch.BatchId;
                    }
                }

                if (localBatchId.HasValue)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var attachmentsVm = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AttachmentsViewModel>(
                            ((App)System.Windows.Application.Current).ServiceHost!.Services);

                        _ = attachmentsVm.InitializeAsync(localBatchId.Value, $"Batch BCR ID: {batch.BcrId}");
                        _navigation.NavigateTo(attachmentsVm);
                    });
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Batch not found locally. Please process the batch first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading attachments: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        });
    }

    private async Task OnRecreateSelectedBatchAsync(BatchRow? batch)
    {
        if (batch == null || !batch.CanRecreate || !batch.LocalBatchId.HasValue) return;

        IsLoading = true;
        try
        {
            DHSIntegrationAgent.Contracts.Persistence.BatchRow? localBatchRow;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                localBatchRow = await uow.Batches.GetByIdAsync(batch.LocalBatchId.Value, default);
            }

            if (localBatchRow == null)
            {
                MessageBox.Show("Batch not found locally. Please process the batch first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var success = await _batchCreationOrchestrator.ConfirmAndCreateBatchAsync(
                batch.CompanyCode,
                batch.PayerNameEn ?? $"Payer {batch.CompanyCode}",
                batch.BcrMonth,
                batch.BcrYear,
                true,
                new[] { localBatchRow });

            if (success)
            {
                await LoadBatchesAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error recreating batch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OnDeleteSelectedBatchAsync(BatchRow? batch)
    {
        if (batch == null) return;

        var message = $"Are you sure you want to delete batch {batch.BcrId} (Company: {batch.CompanyCode})?\nThis will remove it from the server and local storage permanently.";
        if (batch.BcrId <= 0 && batch.LocalBatchId.HasValue)
        {
            message = $"Are you sure you want to delete this pending local batch (Company: {batch.CompanyCode})?\nThis will remove it from local storage permanently.";
        }

        var result = MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var bcrIdStr = batch.BcrId > 0 ? batch.BcrId.ToString() : null;
            var delResult = await _deleteBatchService.DeleteBatchAsync(batch.LocalBatchId, bcrIdStr, default);
            if (!delResult.Succeeded)
            {
                MessageBox.Show(delResult.ErrorMessage ?? "Failed to delete batch.", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Update UI status to Deleted instead of removing
            batch.BatchStatus = "Deleted";

            MessageBox.Show("Batch deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OnResumeSelectedBatchAsync(BatchRow? batch)
    {
        if (batch == null || !batch.CanResume) return;

        IsResuming = true;
        try
        {
            long batchId;
            int pendingCount;

            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var localBatch = await uow.Batches.GetByBcrIdAsync(batch.BcrId.ToString(), default);
                if (localBatch == null)
                {
                    MessageBox.Show("Batch details not found in local database. Please process the batch first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                batchId = localBatch.BatchId;
                pendingCount = await uow.Claims.CountPendingCompletionAsync(batchId, default);
            }

            var result = MessageBox.Show(
                $"This batch has {pendingCount} claims pending completion confirmation.\nResume will check completed claims using the resume API and retry/requeue incomplete claims if needed.\nDo you want to continue?",
                "Confirm Resume",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _batchTracker.TrackManualResume(batchId, batch.BcrId.ToString(), resumeResult =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsResuming = false;

                        if (resumeResult.Succeeded)
                        {
                            MessageBox.Show(
                                $"Resume Complete.\n\n" +
                                $"Processed: {resumeResult.RetriedCount}\n" +
                                $"Success: {resumeResult.SuccessCount}\n" +
                                $"Failed: {resumeResult.FailedCount}",
                                "Resume Summary",
                                MessageBoxButton.OK,
                                resumeResult.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show($"Resume failed: {resumeResult.ErrorMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                });
                return; // Tracker handles completion, so skip finally block setting IsResuming=false prematurely
            }
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Error during resume: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        IsResuming = false;
    }

    private async Task OnUploadAttachmentsAsync()
    {
        if (SelectedBatch == null)
        {
            MessageBox.Show("Please select a batch to upload attachments for.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsLoading = true;

            DHSIntegrationAgent.Contracts.Persistence.BatchRow? batch = null;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                batch = await uow.Batches.GetByBcrIdAsync(SelectedBatch.BcrId.ToString(), default);
            }

            if (batch == null)
            {
                MessageBox.Show("Batch details not found in local database. Please process the batch first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Count attachments (CPU/IO bound, keep async)
            int count = await _attachmentDispatchService.CountAttachmentsAsync(batch, default);

            var result = MessageBox.Show(
                $"Found {count} attachments related to this batch.\n\nDo you want to proceed with upload?",
                "Confirm Upload",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _batchTracker.TrackAttachmentUpload(new[] { batch });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error preparing upload: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPayersAsync()
    {
        try
        {
            // 1. Fetch data from DB (Single UOW, short-lived transaction)
            string? providerDhsCode;
            IReadOnlyList<DHSIntegrationAgent.Contracts.Persistence.PayerItem> payersFromDb = Array.Empty<DHSIntegrationAgent.Contracts.Persistence.PayerItem>();

            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var settings = await uow.AppSettings.GetAsync(default);
                providerDhsCode = settings?.ProviderDhsCode;

                if (!string.IsNullOrWhiteSpace(providerDhsCode))
                {
                    payersFromDb = await uow.Payers.GetAllPayersAsync(providerDhsCode, default);
                }
            } // UOW Disposed immediately, releasing SHARED lock before UI updates

            // 2. Validate configuration
            if (string.IsNullOrWhiteSpace(providerDhsCode))
            {
                // No provider configured, keep only "All" option
                MessageBox.Show(
                    "ProviderDhsCode is not configured in AppSettings.\n\n" +
                    "Please configure it in Settings page to load payers.",
                    "Configuration Info",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // 3. Update UI (outside transaction)
            // Keep the "All" option at index 0, add payers from database
            while (Payers.Count > 1)
            {
                Payers.RemoveAt(1);
            }

            foreach (var payer in payersFromDb)
            {
                Payers.Add(new PayerItem
                {
                    PayerId = (short)payer.PayerId,
                    PayerName = payer.PayerName ?? $"Payer {payer.PayerId}"
                });
            }

            // Debug info
            if (payersFromDb.Count == 0)
            {
                MessageBox.Show(
                    $"No active payers found for ProviderDhsCode: '{providerDhsCode}'.\n\n" +
                    $"Please verify:\n" +
                    $"1. PayerProfile table has records with ProviderDhsCode = '{providerDhsCode}'\n" +
                    $"2. IsActive = 1 for those records\n\n" +
                    $"Current database records show ProviderDhsCode: 'D111' and 'D112'.",
                    "No Payers Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            // Show error to help debug
            MessageBox.Show(
                $"Error loading payers from database:\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Remove any partially loaded payers
            while (Payers.Count > 1)
            {
                Payers.RemoveAt(1);
            }
        }
    }

        public async Task LoadBatchesAsync()
    {
        IsLoading = true;
        try
        {
            // Get ProviderDhsCode from AppSettings
            string? providerDhsCode;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var settings = await uow.AppSettings.GetAsync(default);
                providerDhsCode = settings?.ProviderDhsCode;
            }

            if (string.IsNullOrWhiteSpace(providerDhsCode))
            {
                MessageBox.Show(
                    "ProviderDhsCode is not configured. Please configure it in Settings page.",
                    "Configuration Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Extract month and year from filter dates
            int? filterMonth = FilterStartDate?.Month;
            int? filterYear = FilterStartDate?.Year;
            short? filterPayerId = SelectedPayer?.PayerId;

            // Call API to get batch requests with filters
            var result = await _batchClient.GetBatchRequestAsync(
                providerDhsCode, 
                filterMonth, 
                filterYear,
                filterPayerId,
                pageNumber: 1, 
                pageSize: 100,
                ct: default);

            if (!result.Succeeded)
            {
                if (result.StatusCode == 404)
                {
                    MessageBox.Show(
                        $"API endpoint not found.\n\n" +
                        $"Details:\n{result.Message}\n\n" +
                        $"Please verify the API configuration.",
                        "API Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to load batch requests.\n\n" +
                        $"Error: {result.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                return;
            }

            // Update the Batches collection
            Batches.Clear();
            if (result.Data != null)
            {
                var failedClaimsMap = new Dictionary<string, bool>();
                var hasAttachmentsMap = new Dictionary<string, bool>();
                var failedDispatchesMap = new Dictionary<string, bool>();
                var localBatchIdMap = new Dictionary<string, long>();
                var finalLocalBatches = new List<DHSIntegrationAgent.Contracts.Persistence.BatchRow>();

                try
                {
                    // Optimization: Bulk load local data in two queries instead of N+1 loop
                    var bcrIds = result.Data.Select(x => x.BcrId.ToString()).Distinct().ToList();

                    if (bcrIds.Count > 0)
                    {
                        await using var uow = await _unitOfWorkFactory.CreateAsync(default);

                        var localBatches = (await uow.Batches.GetByBcrIdsAsync(bcrIds, default)).ToList();
                        var updatedLocalBatches = new List<DHSIntegrationAgent.Contracts.Persistence.BatchRow>();
                        bool hasAnyUpdates = false;

                        foreach (var apiItem in result.Data)
                        {
                            var bcrIdStr = apiItem.BcrId.ToString();
                            var lb = localBatches.FirstOrDefault(x => x.BcrId == bcrIdStr);

                            if (lb != null)
                            {
                                bool needsUpdate = false;
                                var targetStatus = lb.BatchStatus;

                                if (!string.IsNullOrEmpty(apiItem.BatchStatus) && Enum.TryParse<DHSIntegrationAgent.Domain.WorkStates.BatchStatus>(apiItem.BatchStatus, true, out var apiStatus))
                                {
                                    // Do not revert a locally Deleted batch to a non-deleted status returned by the API
                                    if (lb.BatchStatus != apiStatus && lb.BatchStatus != DHSIntegrationAgent.Domain.WorkStates.BatchStatus.Deleted)
                                    {
                                        targetStatus = apiStatus;
                                        needsUpdate = true;
                                    }
                                }

                                bool apiHasResume = apiItem.ResumeBatch ?? false;
                                if (lb.HasResume != apiHasResume)
                                {
                                    needsUpdate = true;
                                }

                                if (needsUpdate)
                                {
                                    await uow.Batches.UpdateStatusAsync(
                                        lb.BatchId,
                                        targetStatus,
                                        apiHasResume,
                                        lb.LastError,
                                        DateTimeOffset.UtcNow,
                                        default
                                    );
                                    hasAnyUpdates = true;

                                    updatedLocalBatches.Add(lb with { BatchStatus = targetStatus, HasResume = apiHasResume });
                                }
                                else
                                {
                                    updatedLocalBatches.Add(lb);
                                }
                            }
                            else
                            {
                                var monthKey = $"{apiItem.BcrYear}{apiItem.BcrMonth:D2}";
                                var startDateUtc = new DateTimeOffset(apiItem.BcrYear, apiItem.BcrMonth, 1, 0, 0, 0, TimeSpan.Zero);
                                var endDateUtc = startDateUtc.AddMonths(1).AddTicks(-1);

                                var key = new DHSIntegrationAgent.Contracts.Persistence.BatchKey(
                                    providerDhsCode,
                                    apiItem.CompanyCode ?? "",
                                    monthKey,
                                    startDateUtc,
                                    endDateUtc);

                                var targetStatus = DHSIntegrationAgent.Domain.WorkStates.BatchStatus.Completed;
                                if (!string.IsNullOrEmpty(apiItem.BatchStatus) && Enum.TryParse<DHSIntegrationAgent.Domain.WorkStates.BatchStatus>(apiItem.BatchStatus, true, out var parsedStatus))
                                {
                                    targetStatus = parsedStatus;
                                }

                                bool apiHasResume = apiItem.ResumeBatch ?? false;

                                long newBatchId = await uow.Batches.EnsureBatchAsync(
                                    key,
                                    targetStatus,
                                    apiItem.MidTableTotalClaim,
                                    DateTimeOffset.UtcNow,
                                    default);

                                await uow.Batches.SetBcrIdAsync(newBatchId, bcrIdStr, DateTimeOffset.UtcNow, default);

                                await uow.Batches.UpdateStatusAsync(
                                    newBatchId,
                                    targetStatus,
                                    apiHasResume,
                                    null,
                                    DateTimeOffset.UtcNow,
                                    default);

                                hasAnyUpdates = true;

                                var newRow = await uow.Batches.GetByIdAsync(newBatchId, default);
                                if (newRow != null)
                                {
                                    updatedLocalBatches.Add(newRow);
                                }
                            }
                        }

                        localBatches = updatedLocalBatches;
                        finalLocalBatches = localBatches;

                        if (hasAnyUpdates)
                        {
                            await uow.CommitAsync(default);
                        }

                        if (localBatches.Count > 0)
                        {
                            var batchIds = localBatches.Select(b => b.BatchId).ToList();
                            var countsMap = await uow.Claims.GetBatchCountsForBatchesAsync(batchIds, default);
                            var dispatchCountsMap = await uow.Dispatches.GetFailedDispatchCountsForBatchesAsync(batchIds, default);

                            foreach (var lb in localBatches)
                            {
                                if (lb.BcrId != null)
                                {
                                    localBatchIdMap[lb.BcrId] = lb.BatchId;

                                    if (countsMap.TryGetValue(lb.BatchId, out var counts))
                                    {
                                        if (counts.Failed > 0)
                                        {
                                            failedClaimsMap[lb.BcrId] = true;
                                        }
                                    }

                                    if (dispatchCountsMap.TryGetValue(lb.BatchId, out var dispatchCount))
                                    {
                                        if (dispatchCount > 0)
                                        {
                                            failedDispatchesMap[lb.BcrId] = true;
                                        }
                                    }

                                    var attachmentCount = await uow.Attachments.CountByBatchAsync(lb.BatchId, default);
                                    if (attachmentCount > 0)
                                    {
                                        hasAttachmentsMap[lb.BcrId] = true;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error bulk loading batch status: {ex.Message}");
                }

                foreach (var item in result.Data)
                {
                    bool hasFailed = false;
                    if (failedClaimsMap.TryGetValue(item.BcrId.ToString(), out var failed))
                    {
                        hasFailed = failed;
                    }

                    bool hasAtt = false;
                    if (hasAttachmentsMap.TryGetValue(item.BcrId.ToString(), out var att))
                    {
                        hasAtt = att;
                    }

                    bool hasFailedDispatches = false;
                    if (failedDispatchesMap.TryGetValue(item.BcrId.ToString(), out var fd))
                    {
                        hasFailedDispatches = fd;
                    }

                    long? localBatchId = null;
                    if (localBatchIdMap.TryGetValue(item.BcrId.ToString(), out var bid))
                    {
                        localBatchId = bid;
                    }

                    string effectivePayerName = string.IsNullOrWhiteSpace(item.PayerNameEn) ? (item.CompanyCode ?? "") : item.PayerNameEn;
                    string displayPayerName = !string.IsNullOrWhiteSpace(item.ParentPayerNameEn) ? $"{item.ParentPayerNameEn} - {effectivePayerName}" : effectivePayerName;

                    string displayStatus = item.BatchStatus ?? "";
                    if (localBatchId.HasValue && finalLocalBatches != null)
                    {
                        var lb = finalLocalBatches.FirstOrDefault(b => b.BatchId == localBatchId.Value);
                        if (lb != null)
                        {
                            displayStatus = lb.BatchStatus.ToString();
                        }
                    }

                    Batches.Add(new BatchRow
                    {
                        LocalBatchId = localBatchId,
                        BcrId = item.BcrId,
                        BcrCreatedOn = DHSIntegrationAgent.Application.Helpers.DateTimeHelper.ConvertToKsaTime(item.BcrCreatedOn),
                        BcrMonth = item.BcrMonth,
                        BcrYear = item.BcrYear,
                        UserName = item.UserName,
                        PayerNameEn = displayPayerName,
                        PayerNameAr = item.PayerNameAr,
                        CompanyCode = item.CompanyCode ?? "",
                        MidTableTotalClaim = item.MidTableTotalClaim,
                        BatchStatus = displayStatus,
                        HasFailedClaims = hasFailed,
                        HasAttachments = hasAtt,
                        HasFailedDispatches = hasFailedDispatches,
                        ResumeBatch = item.ResumeBatch ?? false
                    });
                }
            }

            // Also load any local Draft or Fetching batches that don't have a backend BcrId yet,
            // or aren't returned by the API yet.
            try
            {
                await using var uow = await _unitOfWorkFactory.CreateAsync(default);
                var draftBatches = await uow.Batches.ListByStatusAsync(DHSIntegrationAgent.Domain.WorkStates.BatchStatus.Draft, default);
                var fetchingBatches = await uow.Batches.ListByStatusAsync(DHSIntegrationAgent.Domain.WorkStates.BatchStatus.Fetching, default);

                // Deduplicate by BatchId in case a batch moved from Draft to Fetching between the two queries
                var localPendingBatches = draftBatches.Concat(fetchingBatches)
                    .GroupBy(b => b.BatchId)
                    .Select(g => g.First())
                    .ToList();

                foreach (var lb in localPendingBatches)
                {
                    // If it already has a BcrId and is already in the list, skip it.
                    if (!string.IsNullOrEmpty(lb.BcrId) && Batches.Any(b => b.BcrId.ToString() == lb.BcrId))
                    {
                        continue;
                    }

                    int bcrMonth = 0;
                    int bcrYear = 0;

                    if (!string.IsNullOrEmpty(lb.MonthKey) && lb.MonthKey.Length == 6)
                    {
                        int.TryParse(lb.MonthKey.Substring(0, 4), out bcrYear);
                        int.TryParse(lb.MonthKey.Substring(4, 2), out bcrMonth);
                    }

                    // Apply filters to local batches
                    if (filterMonth.HasValue && filterMonth.Value != bcrMonth)
                        continue;

                    if (filterYear.HasValue && filterYear.Value != bcrYear)
                        continue;

                    // Try to fetch payer profile for the names
                    string payerNameEn = $"Payer {lb.CompanyCode}";
                    string payerNameAr = "";
                    var payerProfile = await uow.Payers.GetByCompanyCodeAsync(providerDhsCode, lb.CompanyCode, default);
                    if (payerProfile != null)
                    {
                        payerNameEn = payerProfile.PayerName ?? payerNameEn;

                        // Apply payer filter logic (if it differs from "All" which usually sets filterPayerId = null)
                        if (filterPayerId.HasValue)
                        {
                            var effectivePayerId = payerProfile.PayerId;
                            if (!string.IsNullOrWhiteSpace(payerProfile.PayerCode) && int.TryParse(payerProfile.PayerCode, out var parsed))
                            {
                                effectivePayerId = parsed;
                            }

                            if (effectivePayerId != filterPayerId.Value)
                            {
                                continue;
                            }
                        }
                    }
                    else if (filterPayerId.HasValue)
                    {
                        // If we are filtering by a specific PayerId, but couldn't find a profile for this batch, skip it.
                        continue;
                    }

                    int batchBcrId = 0;
                    if (!string.IsNullOrEmpty(lb.BcrId))
                    {
                        int.TryParse(lb.BcrId, out batchBcrId);
                    }

                    Batches.Insert(0, new BatchRow
                    {
                        LocalBatchId = lb.BatchId,
                        BcrId = batchBcrId,
                        BcrCreatedOn = DHSIntegrationAgent.Application.Helpers.DateTimeHelper.ConvertToKsaTime(lb.CreatedUtc.DateTime),
                        BcrMonth = bcrMonth,
                        BcrYear = bcrYear,
                        UserName = "Local User",
                        PayerNameEn = payerNameEn,
                        PayerNameAr = payerNameAr,
                        CompanyCode = lb.CompanyCode,
                        MidTableTotalClaim = lb.TotalClaims,
                        BatchStatus = lb.BatchStatus.ToString(),
                        HasFailedClaims = false,
                        HasAttachments = false,
                        HasFailedDispatches = false,
                        ResumeBatch = false
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading local pending batches: {ex.Message}");
            }
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(
                $"Network error occurred while loading batch requests.\n\n" +
                $"Details: {ex.Message}\n\n" +
                $"Please check your internet connection and try again.",
                "Network Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"An unexpected error occurred.\n\n" +
                $"Details: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

public sealed class BatchRow : ViewModelBase
    {
        private long? _localBatchId;
        private int _bcrId;
        private DateTime _bcrCreatedOn;
        private int _bcrMonth;
        private int _bcrYear;
        private string? _userName;
        private string? _payerNameEn;
        private string? _payerNameAr;
        private string _companyCode = "";
        private int _midTableTotalClaim;
        private string _batchStatus = "";
        private bool _hasResume;
        private bool _resumeBatch;
        private bool _hasFailedClaims;
        private bool _hasAttachments;
        private bool _hasFailedDispatches;

        public long? LocalBatchId { get => _localBatchId; set => SetProperty(ref _localBatchId, value); }
        public int BcrId { get => _bcrId; set => SetProperty(ref _bcrId, value); }
        public DateTime BcrCreatedOn { get => _bcrCreatedOn; set => SetProperty(ref _bcrCreatedOn, value); }
        public int BcrMonth { get => _bcrMonth; set => SetProperty(ref _bcrMonth, value); }
        public int BcrYear { get => _bcrYear; set => SetProperty(ref _bcrYear, value); }
        public string? UserName { get => _userName; set => SetProperty(ref _userName, value); }
        public string? PayerNameEn { get => _payerNameEn; set => SetProperty(ref _payerNameEn, value); }
        public string? PayerNameAr { get => _payerNameAr; set => SetProperty(ref _payerNameAr, value); }
        public string CompanyCode { get => _companyCode; set => SetProperty(ref _companyCode, value); }
        public int MidTableTotalClaim { get => _midTableTotalClaim; set => SetProperty(ref _midTableTotalClaim, value); }
        public string BatchStatus
        {
            get => _batchStatus;
            set
            {
                if (SetProperty(ref _batchStatus, value))
                {
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(CanUploadAttachments));
                    OnPropertyChanged(nameof(CanDelete));
                    OnPropertyChanged(nameof(CanRecreate));
                    OnPropertyChanged(nameof(HasActions));
                }
            }
        }

        // Legacy properties for existing UI bindings
        public string Status => BatchStatus;
        public bool HasResume { get => _hasResume; set => SetProperty(ref _hasResume, value); }
        public bool ResumeBatch
        {
            get => _resumeBatch;
            set
            {
                if (SetProperty(ref _resumeBatch, value))
                {
                    OnPropertyChanged(nameof(CanResume));
                    OnPropertyChanged(nameof(HasActions));
                }
            }
        }
        public bool CanResume => ResumeBatch;
        public bool CanUploadAttachments => BatchStatus == "Completed";
        public bool CanDelete => BatchStatus == "Completed";
        public bool CanRecreate => BatchStatus == "Completed";
        public bool HasFailedClaims { get => _hasFailedClaims; set => SetProperty(ref _hasFailedClaims, value); }
        public bool HasAttachments
        {
            get => _hasAttachments;
            set
            {
                if (SetProperty(ref _hasAttachments, value))
                {
                    OnPropertyChanged(nameof(HasActions));
                }
            }
        }
        public bool HasFailedDispatches
        {
            get => _hasFailedDispatches;
            set
            {
                if (SetProperty(ref _hasFailedDispatches, value))
                {
                    OnPropertyChanged(nameof(HasActions));
                }
            }
        }

        public bool HasActions => HasAttachments || CanResume || CanUploadAttachments || HasFailedDispatches || CanDelete || CanRecreate;
    }

    public sealed class DispatchRow
    {
        public string PacketId { get; set; } = "";
        public string AttemptUtc { get; set; } = "";
        public string Result { get; set; } = "";
        public string CorrelationId { get; set; } = "";
    }

    public sealed class PayerItem : IEquatable<PayerItem>
    {
        public short? PayerId { get; set; }
        public string PayerName { get; set; } = "";

        public override string ToString() => PayerName;

        public bool Equals(PayerItem? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return PayerId == other.PayerId && PayerName == other.PayerName;
        }

        public override bool Equals(object? obj) => Equals(obj as PayerItem);

        public override int GetHashCode() => HashCode.Combine(PayerId, PayerName);
    }
}
