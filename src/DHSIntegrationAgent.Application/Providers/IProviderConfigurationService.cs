using System.Text.Json.Nodes;

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

/// <summary>
/// Returned to callers (UI / worker engine) so they know whether they are using cached/stale data.
/// </summary>
public sealed record ProviderConfigurationSnapshot(
  string ProviderDhsCode,
  string ConfigJson,
  DateTime FetchedUtc,
  DateTime ExpiresUtc,
  bool FromCache,
  bool IsStale,
  string? LastError,
  ProviderConfigurationParsed Parsed);

/// <summary>
/// We only parse the subset that is stable/needed now (WBS 2.3):
/// - providerPayers list (CompanyCode + optional PayerCode + names)
/// - domainMappings (approved) kept as JsonArray for later steps
/// - missingDomainMappings kept as JsonArray for later steps
/// </summary>
public sealed record ProviderConfigurationParsed(
    IReadOnlyList<ProviderPayerDto> ProviderPayers,
    JsonArray? DomainMappings,
    JsonArray? MissingDomainMappings
);

public sealed record ProviderPayerDto(
    string CompanyCode,
    string? PayerCode,
    string? PayerNameEn,
    string? ParentPayerNameEn
);
