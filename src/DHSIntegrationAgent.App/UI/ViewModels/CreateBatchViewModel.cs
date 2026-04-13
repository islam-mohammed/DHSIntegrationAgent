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
using DHSIntegrationAgent.App.UI.Services;

namespace DHSIntegrationAgent.App.UI.ViewModels;

/// <summary>
/// ViewModel for the Create Batch view.
/// Manages batch creation functionality.
/// </summary>
public sealed class CreateBatchViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IBatchCreationOrchestrator _batchCreationOrchestrator;

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
        IBatchCreationOrchestrator batchCreationOrchestrator)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));
        _batchCreationOrchestrator = batchCreationOrchestrator ?? throw new ArgumentNullException(nameof(batchCreationOrchestrator));

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

            var batchesToDelete = new List<BatchRow>();

            // Check for existing batches
            await using (var uow = await _unitOfWorkFactory.CreateAsync(default))
            {
                var monthKey = $"{year}{month:D2}";
                var key = new BatchKey(providerDhsCode, SelectedPayer.CompanyCode, monthKey, startDateOffset, endDateOffset);
                var existingBatches = await uow.Batches.GetBatchesByKeyAsync(key, default);

                if (existingBatches.Any())
                {
                    bool hasCompletedOrFailed = false;
                    bool hasInProgress = false;

                    foreach (var existingBatch in existingBatches)
                    {
                        var status = existingBatch.BatchStatus;
                        if (status != BatchStatus.Completed && status != BatchStatus.Failed && status != BatchStatus.Deleted)
                        {
                            hasInProgress = true;
                            break;
                        }
                        if (status == BatchStatus.Completed || status == BatchStatus.Failed)
                        {
                            hasCompletedOrFailed = true;
                        }
                    }

                    if (hasInProgress)
                    {
                        MessageBox.Show(
                            "A batch is already in progress for this Payer and Period. Cannot create a new batch.",
                            "Batch In Progress",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    if (hasCompletedOrFailed)
                    {
                        var replace = MessageBox.Show(
                            "A completed or failed batch already exists for this Payer and Period. Do you want to delete and replace it?",
                            "Batch Exists",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (replace == MessageBoxResult.Yes)
                        {
                            batchesToDelete.AddRange(existingBatches);
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        // All existing batches are Deleted or In Progress, replace them silently
                        batchesToDelete.AddRange(existingBatches);
                    }
                }
            }

            var success = await _batchCreationOrchestrator.ConfirmAndCreateBatchAsync(
                SelectedPayer.CompanyCode,
                SelectedPayer.PayerName,
                month,
                year,
                false,
                batchesToDelete);

            if (success)
            {
                RequestClose?.Invoke();
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

                var sortedPayers = payersFromDb.OrderBy(p => p.PayerName ?? $"Payer {p.CompanyCode}").ToList();

                // Add payers from database
                foreach (var payer in sortedPayers)
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
