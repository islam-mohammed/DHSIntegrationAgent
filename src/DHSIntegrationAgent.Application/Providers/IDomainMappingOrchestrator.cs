using System.Threading;
using System.Threading.Tasks;

namespace DHSIntegrationAgent.Application.Providers;

/// <summary>
/// Orchestrates domain mapping operations for the UI.
/// Refreshes from provider configuration cache and posts missing items to the API.
/// </summary>
public interface IDomainMappingOrchestrator
{
    Task RefreshFromProviderConfigAsync(CancellationToken ct);
    Task PostMissingNowAsync(CancellationToken ct);
}
