using DHSIntegrationAgent.App.UI.Mvvm;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.App.UI.Navigation;

/// <summary>
/// Uses DI container to create ViewModels.
/// This keeps constructors clean and makes future screens (Setup/Login/etc.) easy to wire.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly NavigationStore _store;
    private readonly IServiceProvider _services;

    public NavigationService(NavigationStore store, IServiceProvider services)
    {
        _store = store;
        _services = services;
    }

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        // ViewModels must be registered in DI.
        var vm = _services.GetRequiredService<TViewModel>();
        _store.CurrentViewModel = vm;
    }

    public void NavigateTo<TViewModel>(TViewModel viewModel) where TViewModel : ViewModelBase
    {
        _store.CurrentViewModel = viewModel;
    }
}
