using System.Collections.ObjectModel;
using System.Windows;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Domain.WorkStates;

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
        ISystemClock clock)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _tablesAdapter = tablesAdapter ?? throw new ArgumentNullException(nameof(tablesAdapter));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

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

            var startDate = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
            var endDate = startDate.AddMonths(1).AddTicks(-1);

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

            if (integrationType.Equals("Tables", StringComparison.OrdinalIgnoreCase))
            {
                var summary = await _tablesAdapter.GetFinancialSummaryAsync(
                    providerDhsCode,
                    SelectedPayer.CompanyCode,
                    startDate,
                    endDate,
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
                var count = await _tablesAdapter.CountClaimsAsync(providerDhsCode, SelectedPayer.CompanyCode, startDate, endDate, default);
                var result = MessageBox.Show($"Create batch for {SelectedPayer.PayerName} with {count} claims?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            // 4. Create Batch locally
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var monthKey = $"{year}{month:D2}";
                var key = new BatchKey(providerDhsCode, SelectedPayer.CompanyCode, monthKey);

                await uow.Batches.EnsureBatchAsync(key, BatchStatus.Ready, _clock.UtcNow, default);
                await uow.CommitAsync(default);
            }

            MessageBox.Show("Batch created successfully and is now ready for processing.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Close the window (this is a bit tricky from ViewModel, but we can use a property or event)
            // For now, we'll just let the user close it or assume the View handles it if we had a proper navigation service.
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
