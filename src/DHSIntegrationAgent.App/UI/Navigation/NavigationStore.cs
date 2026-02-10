using DHSIntegrationAgent.App.UI.Mvvm;

namespace DHSIntegrationAgent.App.UI.Navigation;

/// <summary>
/// Holds the currently displayed ViewModel (the "screen").
/// The ShellViewModel listens to CurrentViewModelChanged and updates the UI.
/// </summary>
public sealed class NavigationStore
{
    private ViewModelBase? _currentViewModel;

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        set
        {
            if (ReferenceEquals(_currentViewModel, value))
                return;

            // If the outgoing VM holds resources, dispose it (optional pattern).
            if (_currentViewModel is IDisposable d)
                d.Dispose();

            _currentViewModel = value;
            CurrentViewModelChanged?.Invoke();
        }
    }

    public event Action? CurrentViewModelChanged;
}
