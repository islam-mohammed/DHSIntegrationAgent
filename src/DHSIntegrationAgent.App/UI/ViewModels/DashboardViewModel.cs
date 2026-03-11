using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Providers;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IProviderContext _providerContext;

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

    public AsyncRelayCommand RefreshCommand { get; }

    public DashboardViewModel(IDashboardService dashboardService, IProviderContext providerContext)
    {
        _dashboardService = dashboardService;
        _providerContext = providerContext;

        RefreshCommand = new AsyncRelayCommand(() => LoadDataAsync(CancellationToken.None));
    }

    private async Task LoadDataAsync(CancellationToken ct)
    {
        var providerDhsCode = await _providerContext.GetProviderDhsCodeAsync(ct);
        ProviderDhsCode = providerDhsCode;

        string? payerCode = string.IsNullOrWhiteSpace(SelectedPayer) || SelectedPayer == "—" ? null : SelectedPayer;

        var metrics = await _dashboardService.GetMetricsAsync(providerDhsCode, payerCode, ct);

        StagedCount = metrics.StagedCount;
        EnqueuedCount = metrics.EnqueuedCount;
        CompletedCount = metrics.CompletedCount;
        FailedCount = metrics.FailedCount;

        LastFetchUtc = metrics.LastFetchUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
        LastSendUtc = metrics.LastSendUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
    }

    public Task OnNavigatedToAsync(CancellationToken ct)
    {
        return LoadDataAsync(ct);
    }
}
