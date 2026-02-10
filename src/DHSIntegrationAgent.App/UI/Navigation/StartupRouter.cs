using System;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.App.UI.ViewModels;

namespace DHSIntegrationAgent.App.UI.Navigation;

/// <summary>
/// Reads SQLite.AppSettings and navigates to Setup if GroupID/ProviderDhsCode are missing.
/// Otherwise navigates forward (for now: Landing; later: Login in WBS 5.3).
/// </summary>
public sealed class StartupRouter : IStartupRouter
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly INavigationService _navigation;

    public StartupRouter(ISqliteUnitOfWorkFactory uowFactory, INavigationService navigation)
    {
        _uowFactory = uowFactory;
        _navigation = navigation;
    }

    public async Task RouteAsync(CancellationToken ct)
    {
        // 1) Open a UnitOfWork (SQLite connection + transaction scope)
        await using var uow = await _uowFactory.CreateAsync(ct);

        // 2) Read the singleton AppSettings row
        var settings = await uow.AppSettings.GetAsync(ct);

        // 3) Decide if first-run setup is needed
        var needsSetup =
            string.IsNullOrWhiteSpace(settings.GroupId) ||
            string.IsNullOrWhiteSpace(settings.ProviderDhsCode);

        // 4) Navigate based on the result
        if (needsSetup)
        {
            _navigation.NavigateTo<SetupViewModel>();
        }
        else
        {
            // NavigateTo<LoginViewModel>()
            _navigation.NavigateTo<LoginViewModel>();
        }

        // 5) No writes were done here; Commit is optional.
        // Keeping it explicit helps if your UoW pattern expects completion.
        await uow.CommitAsync(ct);
    }
}
