namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record ApprovedDomainMappingRow(
    long DomainMappingId,
    string ProviderDhsCode,
    string DomainName,
    int DomainTableId,
    string SourceValue,
    string TargetValue,
    DateTimeOffset DiscoveredUtc,
    DateTimeOffset? LastPostedUtc,
    DateTimeOffset LastUpdatedUtc,
    string? Notes,
    string? ProviderDomainCode,
    bool? IsDefault,
    string? CodeValue,
    string? DisplayValue);
