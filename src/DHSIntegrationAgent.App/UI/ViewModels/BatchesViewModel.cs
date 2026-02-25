using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class BatchesViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IBatchClient _batchClient;
    private readonly IBatchTracker _batchTracker;

    public ObservableCollection<BatchRow> Batches { get; } = new();
    public object ActiveBatches => _batchTracker.ActiveBatches;
    public ObservableCollection<DispatchRow> DispatchHistory { get; } = new();

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
                _ = CheckFailedClaimsAsync();
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
        set
        {
            if (SetProperty(ref _isRetrying, value))
            {
                RetryFailedClaimsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand ApplyFilterCommand { get; }
    public RelayCommand ManualRetrySelectedPacketCommand { get; }
    public RelayCommand<BatchRow> RetryFailedClaimsCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand UploadAttachmentsCommand { get; }

    public BatchesViewModel(ISqliteUnitOfWorkFactory unitOfWorkFactory, IBatchClient batchClient, IBatchTracker batchTracker)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _batchClient = batchClient ?? throw new ArgumentNullException(nameof(batchClient));
        _batchTracker = batchTracker ?? throw new ArgumentNullException(nameof(batchTracker));

        ApplyFilterCommand = new RelayCommand(() => { /* screen-only */ });
        ManualRetrySelectedPacketCommand = new RelayCommand(() => { /* screen-only */ });
        RetryFailedClaimsCommand = new RelayCommand<BatchRow>(OnRetryFailedClaims, CanRetryFailedClaims);
        RefreshCommand = new AsyncRelayCommand(LoadBatchesAsync);
        SearchCommand = new AsyncRelayCommand(LoadBatchesAsync);
        UploadAttachmentsCommand = new RelayCommand(OnUploadAttachments);

        // Initialize with default "All" option synchronously to prevent type name display in ComboBox
        var allPayer = new PayerItem { PayerId = null, PayerName = "All" };
        Payers.Add(allPayer);
        SelectedPayer = allPayer;

        // Load additional payers from SQLite asynchronously
        _ = LoadPayersAsync();

        // Restore any active batches (progress bars) from persistence
        _ = _batchTracker.RestoreActiveBatchesAsync();
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

                // Use Dispatcher to ensure UI updates happen on the main thread
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        HasFailedClaims = counts.Failed > 0;
                    });
                }
                else
                {
                    HasFailedClaims = counts.Failed > 0;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking failed claims: {ex.Message}");
        }
    }

    private void OnRetryFailedClaims(BatchRow targetBatch)
    {
        if (targetBatch == null) return;

        IsRetrying = true;

        // Fetch persistence batch again to pass to tracker
        _ = Task.Run(async () =>
        {
            try
            {
                await using var uow = await _unitOfWorkFactory.CreateAsync(default);
                var batch = await uow.Batches.GetByBcrIdAsync(targetBatch.BcrId.ToString(), default);

                if (batch != null)
                {
                    _batchTracker.TrackBatchRetry(batch, result =>
                    {
                        // Back on UI thread
                        IsRetrying = false;
                        _ = LoadBatchesAsync(); // Refresh list

                        // Re-check failure status after refresh
                        _ = CheckFailedClaimsAsync();

                        MessageBox.Show(
                            $"Retry Complete.\n\n" +
                            $"Total Retried: {result.TotalRetried}\n" +
                            $"Success: {result.SuccessCount}\n" +
                            $"Failed: {result.FailedCount}",
                            "Retry Summary",
                            MessageBoxButton.OK,
                            result.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                    });
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsRetrying = false;
                        MessageBox.Show("Batch not found locally.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsRetrying = false;
                    MessageBox.Show($"Error starting retry: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        });
    }

    private void OnUploadAttachments()
    {
        // For multiple batches, we'd normally have a selection mechanism.
        // For now, let's assume we use the SelectedBatch or a mock list if we want to demonstrate "multiple at the same time".
        if (SelectedBatch != null)
        {
            // We need to map UI BatchRow to Persistence BatchRow (or just use minimal info)
            // Actually, TrackAttachmentUpload takes BatchRow from Persistence.
            // I'll need to fetch the full batch info.

            _ = Task.Run(async () =>
            {
                await using var uow = await _unitOfWorkFactory.CreateAsync(default);
                var batch = await uow.Batches.GetByBcrIdAsync(SelectedBatch.BcrId.ToString(), default);
                if (batch != null)
                {
                    _batchTracker.TrackAttachmentUpload(new[] { batch });
                }
                else
                {
                    // If not found in local DB, we might need to "ensure" it,
                    // but usually it should be there if we're seeing it in the list
                    // (unless it's only in the backend and not yet synced).
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show("Batch details not found in local database. Please process the batch first.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning));
                }
            });
        }
        else
        {
            MessageBox.Show("Please select a batch to upload attachments for.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task LoadPayersAsync()
    {
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
                // No provider configured, keep only "All" option
                MessageBox.Show(
                    "ProviderDhsCode is not configured in AppSettings.\n\n" +
                    "Please configure it in Settings page to load payers.",
                    "Configuration Info",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Load payers from SQLite
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var payersFromDb = await uow.Payers.GetAllPayersAsync(providerDhsCode, default);

                // Keep the "All" option at index 0, add payers from database
                // Remove any previously loaded payers (but keep "All")
                while (Payers.Count > 1)
                {
                    Payers.RemoveAt(1);
                }

                // Add payers from database
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
            // Add 2000ms delay to show loading animation
            await Task.Delay(2000);

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
                foreach (var item in result.Data)
                {
                    bool hasFailed = false;
                    try
                    {
                        await using var uow = await _unitOfWorkFactory.CreateAsync(default);
                        var batch = await uow.Batches.GetByBcrIdAsync(item.BcrId.ToString(), default);
                        if (batch != null)
                        {
                            var counts = await uow.Claims.GetBatchCountsAsync(batch.BatchId, default);
                            hasFailed = counts.Failed > 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking local batch status: {ex.Message}");
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
                        HasFailedClaims = hasFailed
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
        public bool HasFailedClaims { get; set; }
    }

    public sealed class DispatchRow
    {
        public int PacketId { get; set; }
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
