using System.Collections.ObjectModel;
using System.Windows;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Domain.WorkStates;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Contracts.Providers;


namespace DHSIntegrationAgent.App.UI.ViewModels;

/// <summary>
/// ViewModel for the Create Batch view.
/// Manages batch creation functionality.
/// </summary>
public sealed class CreateBatchViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IProviderTablesAdapter _tablesAdapter;
    private readonly IProviderConfigurationService _configService;
    private readonly ISystemClock _clock;
    private readonly IWorkerEngine _workerEngine;
    private readonly IFetchStageService _fetchStageService;
    private readonly IBatchTracker _batchTracker;
    private readonly IBatchClient _batchClient;

    public event Action? RequestClose;

    private PayerItem? _selectedPayer;
    private string? _selectedMonth;
    private string? _selectedYear;
    private bool _isBusy;

    public ObservableCollection<PayerItem> Payers { get; } = new();

    public PayerItem? SelectedPayer
    {
        get => _selectedPayer;
        set => SetProperty(ref _selectedPayer, value);
    }

    public string? SelectedMonth
    {
        get => _selectedMonth;
        set => SetProperty(ref _selectedMonth, value);
    }

    public string? SelectedYear
    {
        get => _selectedYear;
        set => SetProperty(ref _selectedYear, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public AsyncRelayCommand CreateBatchCommand { get; }

    public CreateBatchViewModel(
        ISqliteUnitOfWorkFactory unitOfWorkFactory,
        IProviderTablesAdapter tablesAdapter,
        IProviderConfigurationService configService,
        ISystemClock clock,
        IWorkerEngine workerEngine,
        IFetchStageService fetchStageService,
        IBatchTracker batchTracker,
        IBatchClient batchClient)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _tablesAdapter = tablesAdapter ?? throw new ArgumentNullException(nameof(tablesAdapter));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _workerEngine = workerEngine ?? throw new ArgumentNullException(nameof(workerEngine));
        _fetchStageService = fetchStageService ?? throw new ArgumentNullException(nameof(fetchStageService));
        _batchTracker = batchTracker ?? throw new ArgumentNullException(nameof(batchTracker));
        _batchClient = batchClient ?? throw new ArgumentNullException(nameof(batchClient));

        CreateBatchCommand = new AsyncRelayCommand(ExecuteCreateBatchAsync);

        // Load payers from database
        _ = LoadPayersAsync();
    }

    private async Task ExecuteCreateBatchAsync()
    {
        if (IsBusy) return;

        if (SelectedPayer == null || string.IsNullOrWhiteSpace(SelectedMonth) || string.IsNullOrWhiteSpace(SelectedYear))
        {
            MessageBox.Show("Please select a Payer, Month, and Year.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            // 1. Resolve ProviderDhsCode
            string? providerDhsCode;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var settings = await uow.AppSettings.GetAsync(default);
                providerDhsCode = settings?.ProviderDhsCode;
            }

            if (string.IsNullOrWhiteSpace(providerDhsCode))
            {
                MessageBox.Show("ProviderDhsCode is not configured.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 2. Determine Date Range
            if (!int.TryParse(SelectedMonth, out int month) || !int.TryParse(SelectedYear, out int year))
            {
                MessageBox.Show("Invalid Month or Year selected.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startDateOffset = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
            var endDateOffset = startDateOffset.AddMonths(1).AddTicks(-1);

            BatchRow? batchToDelete = null;
            bool shouldReplace = false;

            // Check for existing batch
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var monthKey = $"{year}{month:D2}";
                var key = new BatchKey(providerDhsCode, SelectedPayer.CompanyCode, monthKey, startDateOffset, endDateOffset);
                var existingId = await uow.Batches.TryGetBatchIdAsync(key, default);

                if (existingId.HasValue)
                {
                    var existingBatch = await uow.Batches.GetByIdAsync(existingId.Value, default);
                    if (existingBatch != null)
                    {
                        var status = existingBatch.BatchStatus;
                        if (status == BatchStatus.Completed || status == BatchStatus.Failed)
                        {
                            var replace = MessageBox.Show(
                                "A completed or failed batch already exists for this Payer and Period. Do you want to delete and replace it?",
                                "Batch Exists",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (replace == MessageBoxResult.Yes)
                            {
                                batchToDelete = existingBatch;
                                shouldReplace = true;
                            }
                            else
                            {
                                return;
                            }
                        }
                        else
                        {
                            MessageBox.Show("An in-progress batch already exists for this Payer and Period.", "Cannot Create Batch", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
            }

            // 3. Check Integration Type and Validate Financials
            var integrationType = "Tables"; // Default

            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var profile = await uow.ProviderProfiles.GetActiveByProviderDhsCodeAsync(providerDhsCode, default);
                if (profile != null)
                {
                    integrationType = profile.IntegrationType;
                }
            }

            FinancialSummary? summary = null;
            if (integrationType.Equals("Tables", StringComparison.OrdinalIgnoreCase))
            {
                summary = await _tablesAdapter.GetFinancialSummaryAsync(
                    providerDhsCode,
                    SelectedPayer.CompanyCode,
                    startDateOffset,
                    endDateOffset,
                    default);

                var message = $"Batch Validation Summary for {SelectedPayer.PayerName} ({SelectedMonth}/{SelectedYear}):\n\n" +
                              $"Total Claims: {summary.TotalClaims}\n" +
                              $"Claimed Amount: {summary.TotalClaimedAmount:N2}\n" +
                              $"Total Discount: {summary.TotalDiscount:N2}\n" +
                              $"Total Deductible: {summary.TotalDeductible:N2}\n" +
                              $"Total Net Amount: {summary.TotalNetAmount:N2}\n\n" +
                              $"Financial Validation: {(summary.IsValid ? "VALID ✅" : "INVALID ❌")}\n\n" +
                              $"Do you want to proceed with creating this batch and starting the stream?";

                var result = MessageBox.Show(message, "Confirm Batch Creation", MessageBoxButton.YesNo, summary.IsValid ? MessageBoxImage.Question : MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            else
            {
                // For other integration types, just show a simple confirmation or count
                var count = await _tablesAdapter.CountClaimsAsync(providerDhsCode, SelectedPayer.CompanyCode, startDateOffset, endDateOffset, default);
                var result = MessageBox.Show($"Create batch for {SelectedPayer.PayerName} with {count} claims?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            RequestClose?.Invoke();

            // 4. Delete existing batch if replacement confirmed
            if (shouldReplace && batchToDelete != null)
            {
                // Delete from API
                if (!string.IsNullOrEmpty(batchToDelete.BcrId) && int.TryParse(batchToDelete.BcrId, out int bcrIdInt))
                {
                    var delResult = await _batchClient.DeleteBatchAsync(bcrIdInt, default);
                    if (!delResult.Succeeded)
                    {
                        MessageBox.Show($"Failed to delete batch from server: {delResult.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Delete locally
                await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
                {
                    await uow.Batches.DeleteAsync(batchToDelete.BatchId, default);
                    await uow.CommitAsync(default);
                }
            }

            // 5. Create Batch locally
            long batchId;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var monthKey = $"{year}{month:D2}";
                var key = new BatchKey(providerDhsCode, SelectedPayer.CompanyCode, monthKey, startDateOffset, endDateOffset);

                // Start as Draft to allow Fetch & Stage to pick it up properly (isolated)
                batchId = await uow.Batches.EnsureBatchAsync(key, BatchStatus.Draft, _clock.UtcNow, default);
                await uow.CommitAsync(default);
            }

            // 5. Run Stream A Fetch & Stage immediately (via Tracker)
            BatchRow? batchRow;
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                batchRow = await uow.Batches.GetByIdAsync(batchId, default);
            }

            if (batchRow != null)
            {
                _batchTracker.TrackBatchCreation(batchRow, summary);
            }

         
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error creating batch: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
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
                return;
            }

            // Load payers from SQLite
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var payersFromDb = await uow.Payers.ListActiveAsync(providerDhsCode, default);

                Payers.Clear();

                // Add payers from database
                foreach (var payer in payersFromDb)
                {
                    Payers.Add(new PayerItem 
                    { 
                        PayerId = 0, // PayerProfileRow doesn't have PayerId in the contract (it's AUTOINCREMENT in DB but not in Row record)
                        PayerName = payer.PayerName ?? $"Payer {payer.CompanyCode}",
                        CompanyCode = payer.CompanyCode
                    });
                }

                // Set first payer as default if available
                if (Payers.Count > 0)
                {
                    SelectedPayer = Payers[0];
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading payers: {ex.Message}");
        }
    }

    public sealed class PayerItem : IEquatable<PayerItem>
    {
        public short PayerId { get; set; }
        public string PayerName { get; set; } = "";
        public string CompanyCode { get; set; } = "";

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
