using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly ISystemClock _clock;

    // Screen-only fields (real load/save will be wired to SQLite + provider config rules in later WBS)
    private string _groupId = "";
    private string _providerDhsCode = "";

    private int _fetchIntervalMinutes = 5;
    private int _configCacheTtlMinutes = 1440;

    private int _leaseDurationSeconds = 300;
    private int _streamAIntervalSeconds = 60;
    private int _resumePollIntervalSeconds = 60;
    private int _apiTimeoutSeconds = 60;
    private int _manualRetryCooldownMinutes = 10;

    private bool _isLoading;

    public string GroupId { get => _groupId; set => SetProperty(ref _groupId, value); }
    public string ProviderDhsCode { get => _providerDhsCode; set => SetProperty(ref _providerDhsCode, value); }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public int FetchIntervalMinutes { get => _fetchIntervalMinutes; set => SetProperty(ref _fetchIntervalMinutes, value); }
    public int ConfigCacheTtlMinutes { get => _configCacheTtlMinutes; set => SetProperty(ref _configCacheTtlMinutes, value); }

    // These are the ones you noted come from Provider Config API (and are persisted to SQLite AppSettings)
    public int LeaseDurationSeconds { get => _leaseDurationSeconds; set => SetProperty(ref _leaseDurationSeconds, value); }
    public int StreamAIntervalSeconds { get => _streamAIntervalSeconds; set => SetProperty(ref _streamAIntervalSeconds, value); }
    public int ResumePollIntervalSeconds { get => _resumePollIntervalSeconds; set => SetProperty(ref _resumePollIntervalSeconds, value); }
    public int ApiTimeoutSeconds { get => _apiTimeoutSeconds; set => SetProperty(ref _apiTimeoutSeconds, value); }
    public int ManualRetryCooldownMinutes { get => _manualRetryCooldownMinutes; set => SetProperty(ref _manualRetryCooldownMinutes, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ReloadProviderConfigCommand { get; }
    public AsyncRelayCommand LoadCommand { get; }

    public SettingsViewModel(ISqliteUnitOfWorkFactory uowFactory, ISystemClock clock)
    {
        _uowFactory = uowFactory;
        _clock = clock;

        LoadCommand = new AsyncRelayCommand(LoadAsync);

        SaveCommand = new AsyncRelayCommand(async () =>
        {
            await SaveAsync();
        });

        ReloadProviderConfigCommand = new AsyncRelayCommand(async () =>
        {
            // screen-only: later will call ProviderConfigurationService.LoadAsync()
            await Task.CompletedTask;
        });
    }

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;

            await using var uow = await _uowFactory.CreateAsync(CancellationToken.None);
            var settings = await uow.AppSettings.GetAsync(CancellationToken.None);

            GroupId = settings.GroupId ?? "";
            ProviderDhsCode = settings.ProviderDhsCode ?? "";
            FetchIntervalMinutes = settings.FetchIntervalMinutes;
            ConfigCacheTtlMinutes = settings.ConfigCacheTtlMinutes;
            LeaseDurationSeconds = settings.LeaseDurationSeconds;
            StreamAIntervalSeconds = settings.StreamAIntervalSeconds;
            ResumePollIntervalSeconds = settings.ResumePollIntervalSeconds;
            ApiTimeoutSeconds = settings.ApiTimeoutSeconds;
            ManualRetryCooldownMinutes = settings.ManualRetryCooldownMinutes;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            IsLoading = true;

            await using var uow = await _uowFactory.CreateAsync(CancellationToken.None);

            // Update setup (GroupId and ProviderDhsCode)
            if (!string.IsNullOrWhiteSpace(GroupId) && !string.IsNullOrWhiteSpace(ProviderDhsCode))
            {
                await uow.AppSettings.UpdateSetupAsync(
                    GroupId,
                    ProviderDhsCode,
                    _clock.UtcNow,
                    CancellationToken.None);
            }

            // Update general configuration
            await uow.AppSettings.UpdateGeneralConfigurationAsync(
                LeaseDurationSeconds,
                StreamAIntervalSeconds,
                ResumePollIntervalSeconds,
                ApiTimeoutSeconds,
                ConfigCacheTtlMinutes,
                FetchIntervalMinutes,
                ManualRetryCooldownMinutes,
                _clock.UtcNow,
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
