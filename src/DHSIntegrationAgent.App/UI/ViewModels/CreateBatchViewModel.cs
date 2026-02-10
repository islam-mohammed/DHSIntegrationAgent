using System.Collections.ObjectModel;
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.App.UI.ViewModels;

/// <summary>
/// ViewModel for the Create Batch view.
/// Manages batch creation functionality.
/// </summary>
public sealed class CreateBatchViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _unitOfWorkFactory;

    private PayerItem? _selectedPayer;
    private string _selectedMonth = string.Empty;
    private string _selectedYear = string.Empty;

    public ObservableCollection<PayerItem> Payers { get; } = new();

    public PayerItem? SelectedPayer
    {
        get => _selectedPayer;
        set => SetProperty(ref _selectedPayer, value);
    }

    public string SelectedMonth
    {
        get => _selectedMonth;
        set => SetProperty(ref _selectedMonth, value);
    }

    public string SelectedYear
    {
        get => _selectedYear;
        set => SetProperty(ref _selectedYear, value);
    }

    public RelayCommand CreateBatchCommand { get; }

    public CreateBatchViewModel(ISqliteUnitOfWorkFactory unitOfWorkFactory)
    {
        _unitOfWorkFactory = unitOfWorkFactory ?? throw new ArgumentNullException(nameof(unitOfWorkFactory));

        CreateBatchCommand = new RelayCommand(() =>
        {
            // TODO: Implement batch creation logic
            System.Diagnostics.Debug.WriteLine($"Creating batch for Payer: {SelectedPayer?.PayerName}, Month: {SelectedMonth}, Year: {SelectedYear}");
        });

        // Load payers from database
        _ = LoadPayersAsync();
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
                var payersFromDb = await uow.Payers.GetAllPayersAsync(providerDhsCode, default);

                Payers.Clear();

                // Add payers from database
                foreach (var payer in payersFromDb)
                {
                    Payers.Add(new PayerItem 
                    { 
                        PayerId = (short)payer.PayerId, 
                        PayerName = payer.PayerName ?? $"Payer {payer.PayerId}" 
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
