using System.Threading;
using System.Threading.Tasks;

namespace DHSIntegrationAgent.App.UI.Navigation;

/// <summary>
/// Decides the first screen to show at application startup.
/// This keeps startup routing out of ShellViewModel constructors (no async ctor problems).
/// </summary>
public interface IStartupRouter
{
    Task RouteAsync(CancellationToken ct);
}
