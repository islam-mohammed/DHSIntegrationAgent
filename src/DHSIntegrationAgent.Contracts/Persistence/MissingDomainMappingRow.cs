using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record MissingDomainMappingRow(
    long MissingMappingId,
    string ProviderDhsCode,
    string DomainName,
    int DomainTableId,
    string SourceValue,
    DiscoverySource DiscoverySource,
    MappingStatus MappingStatus,
    DateTimeOffset DiscoveredUtc,
    DateTimeOffset? LastPostedUtc,
    DateTimeOffset LastUpdatedUtc,
    string? Notes,
    string? ProviderNameValue,
    string? DomainTableName);
