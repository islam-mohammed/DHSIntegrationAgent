using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.App.UI.ViewModels;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Security;

namespace DHSIntegrationAgent.App.UI.Services;

/// <summary>
/// WBS 5.3 LoginService:
/// - Reads GroupID from SQLite.AppSettings (source of truth)
/// - Calls WBS 2.2 AuthClient (gzip + succeeded/statusCode/message contract)
/// - No remembered session (token is memory-only; no persistence)
/// </summary>
public sealed class LoginService : ILoginService
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IAuthClient _authClient;

    public LoginService(ISqliteUnitOfWorkFactory uowFactory, IAuthClient authClient)
    {
        _uowFactory = uowFactory;
        _authClient = authClient;
    }

    public async Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken ct)
    {
        await using var uow = await _uowFactory.CreateAsync(ct);

        // SQLite is source of truth for GroupID (saved by Setup).
        var settings = await uow.AppSettings.GetAsync(ct);

        if (string.IsNullOrWhiteSpace(settings.GroupId))
            return new LoginOutcome(false, "Setup is missing GroupID. Please complete Setup first.");

        // Delegate HTTP/gzip/response parsing to WBS 2.2 AuthClient.
        var result = await _authClient.LoginAsync(
            email: email,
            password: password,
            groupId: settings.GroupId,
            ct: ct);

        // No DB writes here; commit keeps UoW lifecycle consistent.
        await uow.CommitAsync(ct);

        return result.Succeeded
            ? new LoginOutcome(true, null)
            : new LoginOutcome(false, result.ErrorMessage ?? "Login failed.");
    }
}
