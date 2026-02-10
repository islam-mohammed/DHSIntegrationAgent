using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Contracts.Providers;

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
