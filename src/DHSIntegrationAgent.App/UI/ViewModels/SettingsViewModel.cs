using System.Text;
﻿using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Infrastructure.Security;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly ISystemClock _clock;
    private readonly IKeyProtector _protector;

    // Screen-only fields (real load/save will be wired to SQLite + provider config rules in later WBS)
    private string _groupId = "";
    private string _providerDhsCode = "";
    private string _networkUsername = "";
    private string _networkPassword = "";
    private byte[]? _fallbackNetworkPasswordEncrypted;

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
    public string NetworkUsername { get => _networkUsername; set => SetProperty(ref _networkUsername, value); }

    public string NetworkPassword
    {
        get => _networkPassword;
        set
        {
            if (SetProperty(ref _networkPassword, value))
            {
                // If the user manually changes the password, clear any fallback we held.
                _fallbackNetworkPasswordEncrypted = null;
            }
        }
    }

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

    public SettingsViewModel(
        ISqliteUnitOfWorkFactory uowFactory,
        ISystemClock clock,
        IKeyProtector protector)
    {
        _uowFactory = uowFactory;
        _clock = clock;
        _protector = protector;

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

            NetworkUsername = settings.NetworkUsername ?? "";
            NetworkPassword = "";

            // If decryption fails later, we want to preserve the original blob so a subsequent Save() doesn't wipe it.
            // Note: Setting NetworkPassword="" above triggers the property setter which clears _fallbackNetworkPasswordEncrypted,
            // so we must set it *after* that.
            _fallbackNetworkPasswordEncrypted = settings.NetworkPasswordEncrypted;

            if (settings.NetworkPasswordEncrypted != null && settings.NetworkPasswordEncrypted.Length > 0)
            {
                try
                {
                    var decrypted = _protector.Unprotect(settings.NetworkPasswordEncrypted);
                    NetworkPassword = Encoding.UTF8.GetString(decrypted);
                    // If successful, NetworkPassword setter runs and clears _fallbackNetworkPasswordEncrypted, which is correct.
                }
                catch
                {
                    // Ignore decryption errors (e.g. key change/corruption).
                    // NetworkPassword remains empty, but _fallbackNetworkPasswordEncrypted is preserved.
                }
            }

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
                string? netUser = null;
                byte[]? netPassEnc = null;

                if (!string.IsNullOrWhiteSpace(NetworkUsername))
                {
                    netUser = NetworkUsername.Trim();
                    if (!string.IsNullOrEmpty(NetworkPassword))
                    {
                        var bytes = Encoding.UTF8.GetBytes(NetworkPassword);
                        netPassEnc = _protector.Protect(bytes);
                    }
                    else if (_fallbackNetworkPasswordEncrypted != null)
                    {
                        // User hasn't changed the password (it's empty), but we have a fallback from load failure.
                        netPassEnc = _fallbackNetworkPasswordEncrypted;
                    }
                }

                await uow.AppSettings.UpdateSetupAsync(
                    GroupId,
                    ProviderDhsCode,
                    netUser,
                    netPassEnc,
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
