namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record ProviderConfigCacheRow(
    string ProviderDhsCode,
    string ConfigJson,
    DateTimeOffset FetchedUtc,
    DateTimeOffset ExpiresUtc,
    string? ETag,
    string? LastError);
