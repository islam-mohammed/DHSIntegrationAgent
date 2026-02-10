using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Contracts.Persistence;

// Deprecated: kept for backward compatibility if still needed for MappingRow types
public sealed record DomainMappingRow(
    long DomainMappingId,
    string ProviderDhsCode,
    string CompanyCode,
    string DomainName,
    int DomainTableId,
    string SourceValue,
    string? TargetValue,
    MappingStatus MappingStatus,
    DateTimeOffset DiscoveredUtc,
    DateTimeOffset? LastPostedUtc,
    DateTimeOffset LastUpdatedUtc,
    string? Notes);
