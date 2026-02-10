using DHSIntegrationAgent.App.UI.Mvvm;
using DHSIntegrationAgent.App.UI.Navigation;

namespace DHSIntegrationAgent.App.UI.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly NavigationStore _navigationStore;
    private readonly INavigationService _navigation;

    public ShellViewModel(NavigationStore navigationStore, INavigationService navigation)
    {
        _navigationStore = navigationStore;
        _navigation = navigation;

        _navigationStore.CurrentViewModelChanged += OnCurrentViewModelChanged;

        GoDashboardCommand = new RelayCommand(() => _navigation.NavigateTo<DashboardViewModel>(), () => IsMenuVisible);
        GoBatchesCommand = new RelayCommand(() => _navigation.NavigateTo<BatchesViewModel>(), () => IsMenuVisible);
        GoDomainMappingsCommand = new RelayCommand(() => _navigation.NavigateTo<DomainMappingsViewModel>(), () => IsMenuVisible);
        GoAttachmentsCommand = new RelayCommand(() => _navigation.NavigateTo<AttachmentsViewModel>(), () => IsMenuVisible);
        GoDiagnosticsCommand = new RelayCommand(() => _navigation.NavigateTo<DiagnosticsViewModel>(), () => IsMenuVisible);
        GoSettingsCommand = new RelayCommand(() => _navigation.NavigateTo<SettingsViewModel>(), () => IsMenuVisible);
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
