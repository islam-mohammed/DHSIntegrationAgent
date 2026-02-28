using DHSIntegrationAgent.App.UI.Mvvm;

namespace DHSIntegrationAgent.App.UI.Navigation;

/// <summary>
/// Navigation boundary used by ViewModels: "Go to another screen".
/// We navigate by ViewModel type, and WPF DataTemplates pick the View.
/// </summary>
public interface INavigationService
{
    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
    void NavigateTo<TViewModel>(TViewModel viewModel) where TViewModel : ViewModelBase;
}
