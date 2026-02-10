using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record MissingDomainMappingRow(
    long MissingMappingId,
    string ProviderDhsCode,
    string DomainName,
    int DomainTableId,
    string SourceValue,
    DiscoverySource DiscoverySource,
    DateTimeOffset DiscoveredUtc,
    DateTimeOffset LastUpdatedUtc,
    string? Notes,
    string? ProviderNameValue,
    string? DomainTableName);
