using DHSIntegrationAgent.App.UI.Mvvm;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private int _stagedCount;
    private int _enqueuedCount;
    private int _completedCount;
    private int _failedCount;

    private string _lastFetchUtc = "—";
    private string _lastSendUtc = "—";
    private string _providerDhsCode = "—";
    private string _selectedPayer = "—";

    public int StagedCount { get => _stagedCount; set => SetProperty(ref _stagedCount, value); }
    public int EnqueuedCount { get => _enqueuedCount; set => SetProperty(ref _enqueuedCount, value); }
    public int CompletedCount { get => _completedCount; set => SetProperty(ref _completedCount, value); }
    public int FailedCount { get => _failedCount; set => SetProperty(ref _failedCount, value); }

    public string LastFetchUtc { get => _lastFetchUtc; set => SetProperty(ref _lastFetchUtc, value); }
    public string LastSendUtc { get => _lastSendUtc; set => SetProperty(ref _lastSendUtc, value); }

    public string ProviderDhsCode { get => _providerDhsCode; set => SetProperty(ref _providerDhsCode, value); }

    public string SelectedPayer
    {
        get => _selectedPayer;
        set => SetProperty(ref _selectedPayer, value);
    }

    // Screen-only scaffolding: a Refresh command placeholder
    public RelayCommand RefreshCommand { get; }

    public DashboardViewModel()
    {
        RefreshCommand = new RelayCommand(() =>
        {
            // Screen-only: real values will be wired in WBS 4.* streams + repositories.
            // Keep it harmless and PHI-safe.
        });
    }
}
