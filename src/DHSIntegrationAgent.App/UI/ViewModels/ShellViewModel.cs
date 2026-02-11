
using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.App.UI.Navigation;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Contracts.Workers;
using System.Threading;
using System.Threading.Tasks;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly NavigationStore _navigationStore;
    private readonly INavigationService _navigation;
    private readonly IWorkerEngine _workerEngine;

    private bool _showMissingDomainsNotification;
    private string _missingDomainsMessage = "";

    public ShellViewModel(NavigationStore navigationStore, INavigationService navigation, IWorkerEngine workerEngine)
    {
        _navigationStore = navigationStore;
        _navigation = navigation;
        _workerEngine = workerEngine;

        _navigationStore.CurrentViewModelChanged += OnCurrentViewModelChanged;
        _workerEngine.ProgressChanged += OnWorkerProgressChanged;

        GoDashboardCommand = new RelayCommand(() => _navigation.NavigateTo<DashboardViewModel>(), () => IsMenuVisible);
        GoBatchesCommand = new RelayCommand(() => _navigation.NavigateTo<BatchesViewModel>(), () => IsMenuVisible);
        GoDomainMappingsCommand = new RelayCommand(() => _navigation.NavigateTo<DomainMappingsViewModel>(), () => IsMenuVisible);
        GoAttachmentsCommand = new RelayCommand(() => _navigation.NavigateTo<AttachmentsViewModel>(), () => IsMenuVisible);
        GoDiagnosticsCommand = new RelayCommand(() => _navigation.NavigateTo<DiagnosticsViewModel>(), () => IsMenuVisible);
        GoSettingsCommand = new RelayCommand(() => _navigation.NavigateTo<SettingsViewModel>(), () => IsMenuVisible);
        GoContactCommand = new RelayCommand(() => _navigation.NavigateTo<ContactViewModel>());
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);
    }

    public ViewModelBase? CurrentViewModel => _navigationStore.CurrentViewModel;

    // Menu is shown after login/setup flows.
    public bool IsMenuVisible =>
        CurrentViewModel is not LoginViewModel &&
        CurrentViewModel is not SetupViewModel &&
        CurrentViewModel is not null;

    public RelayCommand GoDashboardCommand { get; }
    public RelayCommand GoBatchesCommand { get; }
    public RelayCommand GoDomainMappingsCommand { get; }
    public RelayCommand GoAttachmentsCommand { get; }
    public RelayCommand GoDiagnosticsCommand { get; }
    public RelayCommand GoSettingsCommand { get; }
    public RelayCommand GoContactCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }

    public bool ShowMissingDomainsNotification
    {
        get => _showMissingDomainsNotification;
        set => SetProperty(ref _showMissingDomainsNotification, value);
    }

    public string MissingDomainsMessage
    {
        get => _missingDomainsMessage;
        set => SetProperty(ref _missingDomainsMessage, value);
    }

    public RelayCommand DismissNotificationCommand => new(() => ShowMissingDomainsNotification = false);

    private void OnWorkerProgressChanged(object? sender, WorkerProgressReport e)
    {
        if (e.Message.StartsWith("DETECTED_MISSING_DOMAINS:"))
        {
            var countPart = e.Message.Substring("DETECTED_MISSING_DOMAINS:".Length);
            if (int.TryParse(countPart, out var count) && count > 0)
            {
                MissingDomainsMessage = $"{count} missing domain mappings detected.";
                ShowMissingDomainsNotification = true;
            }
        }
    }

    private async Task LogoutAsync()
    {
        // Stop the worker engine if it's running
        if (_workerEngine.IsRunning)
        {
            await _workerEngine.StopAsync(CancellationToken.None);
        }

        // Navigate back to login screen
        _navigation.NavigateTo<LoginViewModel>();
    }

    private void OnCurrentViewModelChanged()
    {
        OnPropertyChanged(nameof(CurrentViewModel));
        OnPropertyChanged(nameof(IsMenuVisible));

        // Refresh CanExecute for menu buttons
        GoDashboardCommand.RaiseCanExecuteChanged();
        GoBatchesCommand.RaiseCanExecuteChanged();
        GoDomainMappingsCommand.RaiseCanExecuteChanged();
        GoAttachmentsCommand.RaiseCanExecuteChanged();
        GoDiagnosticsCommand.RaiseCanExecuteChanged();
        GoSettingsCommand.RaiseCanExecuteChanged();
    }
}
