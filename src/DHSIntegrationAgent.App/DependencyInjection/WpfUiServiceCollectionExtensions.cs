using DHSIntegrationAgent.App.UI.Navigation;
using DHSIntegrationAgent.App.UI.Services;
using DHSIntegrationAgent.App.UI.ViewModels;
using DHSIntegrationAgent.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.App.DependencyInjection;

public static class WpfUiServiceCollectionExtensions
{
    public static IServiceCollection AddDhsWpfUi(this IServiceCollection services)
    {
        services.AddSingleton<NavigationStore>();
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddSingleton<ShellViewModel>();

        // ViewModels that navigation will create
        services.AddTransient<LoginViewModel>();
        services.AddTransient<SetupViewModel>();
        services.AddTransient<EngineControlViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<BatchesViewModel>();
        services.AddTransient<DomainMappingsViewModel>();
        services.AddTransient<AttachmentsViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ContactViewModel>();

        // Modal ViewModels
        services.AddTransient<CreateBatchViewModel>();

        // Startup routing (Setup vs Login)
        services.AddSingleton<IStartupRouter, StartupRouter>();

        // WBS 5.3 login orchestration
        services.AddSingleton<ILoginService, LoginService>();

        // Batch creation tracking
        services.AddSingleton<IBatchTracker, BatchTracker>();

        return services;
    }
}
