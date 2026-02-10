using DHSIntegrationAgent.Contracts.Providers;

namespace DHSIntegrationAgent.Application.Providers;

/// <summary>
/// WBS 2.3: Provider configuration loader with SQLite caching rules.
/// Reads ProviderDhsCode + TTL from SQLite AppSettings.
/// </summary> 
public interface IProviderConfigurationService
{
    /// <summary>
    /// Loads provider configuration applying cache rules:
    /// - If cached and not expired => return cached
    /// - If expired/missing => refresh via API
    /// - If refresh fails => return last cached (if any) with IsStale=true
    /// </summary>
    Task<ProviderConfigurationSnapshot> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Refreshes both approved and missing domain mappings from the specialized endpoint.
    /// </summary>
    Task RefreshDomainMappingsAsync(string providerDhsCode, CancellationToken ct);
}
