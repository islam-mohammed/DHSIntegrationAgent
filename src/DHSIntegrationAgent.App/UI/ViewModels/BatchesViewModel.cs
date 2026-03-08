using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.App.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class BatchesViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IBatchClient _batchClient;
    private readonly IBatchTracker _batchTracker;
    private readonly IAttachmentDispatchService _attachmentDispatchService;
    private readonly INavigationService _navigation;

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
    public RelayCommand<BatchRow> ShowAttachmentsCommand { get; }
    public RelayCommand<BatchRow> ShowDispatchHistoryCommand { get; }

    public BatchesViewModel(
        ISqliteUnitOfWorkFactory unitOfWorkFactory,
        IBatchClient batchClient,
        IBatchTracker batchTracker,
        IAttachmentDispatchService attachmentDispatchService,
        INavigationService navigation)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _batchClient = batchClient ?? throw new ArgumentNullException(nameof(batchClient));
        _batchTracker = batchTracker ?? throw new ArgumentNullException(nameof(batchTracker));
        _attachmentDispatchService = attachmentDispatchService ?? throw new ArgumentNullException(nameof(attachmentDispatchService));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        ShowAttachmentsCommand = new RelayCommand<BatchRow>(OnShowAttachments);
        ShowDispatchHistoryCommand = new RelayCommand<BatchRow>(OnShowDispatchHistory);

        ApplyFilterCommand = new RelayCommand(() => { /* screen-only */ });
        ManualRetrySelectedPacketCommand = new RelayCommand<DispatchRow>(OnManualRetrySelectedPacket, CanManualRetrySelectedPacket);
        RefreshCommand = new AsyncRelayCommand(LoadBatchesAsync);
        SearchCommand = new AsyncRelayCommand(LoadBatchesAsync);
        UploadAttachmentsCommand = new AsyncRelayCommand(OnUploadAttachmentsAsync);
        ResumeSelectedBatchCommand = new AsyncRelayCommand<BatchRow>(OnResumeSelectedBatchAsync);

        // Initialize with default "All" option synchronously to prevent type name display in ComboBox
        var allPayer = new PayerItem { PayerId = null, PayerName = "All" };
        Payers.Add(allPayer);
        SelectedPayer = allPayer;
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

                try
                {
                    // Optimization: Bulk load local data in two queries instead of N+1 loop
                    var bcrIds = result.Data.Select(x => x.BcrId.ToString()).Distinct().ToList();

                    if (bcrIds.Count > 0)
                    {
                        await using var uow = await _unitOfWorkFactory.CreateAsync(default);

                        var localBatches = await uow.Batches.GetByBcrIdsAsync(bcrIds, default);
                        if (localBatches.Count > 0)
                        {
                            var batchIds = localBatches.Select(b => b.BatchId).ToList();
                            var countsMap = await uow.Claims.GetBatchCountsForBatchesAsync(batchIds, default);
                            var dispatchCountsMap = await uow.Dispatches.GetFailedDispatchCountsForBatchesAsync(batchIds, default);

                            foreach (var lb in localBatches)
                            {
                                if (lb.BcrId != null)
                                {
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

                    Batches.Add(new BatchRow
                    {
                        BcrId = item.BcrId,
                        BcrCreatedOn = item.BcrCreatedOn,
                        BcrMonth = item.BcrMonth,
                        BcrYear = item.BcrYear,
                        UserName = item.UserName,
                        PayerNameEn = item.PayerNameEn,
                        PayerNameAr = item.PayerNameAr,
                        CompanyCode = item.CompanyCode ?? "",
                        MidTableTotalClaim = item.MidTableTotalClaim,
                        BatchStatus = item.BatchStatus ?? "",
                        HasFailedClaims = hasFailed,
                        HasAttachments = hasAtt,
                        HasFailedDispatches = hasFailedDispatches,
                        ResumeBatch = item.ResumeBatch ?? false
                    });
                }
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

public sealed class BatchRow
    {
        public int BcrId { get; set; }
        public DateTime BcrCreatedOn { get; set; }
        public int BcrMonth { get; set; }
        public int BcrYear { get; set; }
        public string? UserName { get; set; }
        public string? PayerNameEn { get; set; }
        public string? PayerNameAr { get; set; }
        public string CompanyCode { get; set; } = "";
        public int MidTableTotalClaim { get; set; }
        public string BatchStatus { get; set; } = "";

        // Legacy properties for existing UI bindings
        public string Status => BatchStatus;
        public bool HasResume { get; set; }
        public bool ResumeBatch { get; set; }
        public bool CanResume => ResumeBatch && BcrId > 0;
        public bool HasFailedClaims { get; set; }
        public bool HasAttachments { get; set; }
        public bool HasFailedDispatches { get; set; }
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
