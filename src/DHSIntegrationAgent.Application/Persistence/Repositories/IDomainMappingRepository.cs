using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

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

public interface IDomainMappingRepository
{
    Task<IReadOnlyList<ApprovedDomainMappingRow>> GetAllApprovedAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MissingDomainMappingRow>> GetAllMissingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DomainMappingRow>> GetAllAsync(CancellationToken cancellationToken);

    Task UpsertDiscoveredAsync(
        string providerDhsCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        DiscoverySource discoverySource,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken,
        string? providerNameValue = null,
        string? domainTableName = null);

    Task UpsertApprovedAsync(
        string providerDhsCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        string targetValue,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken,
        string? providerDomainCode = null,
        bool? isDefault = null,
        string? codeValue = null,
        string? displayValue = null);

    Task UpsertApprovedFromProviderConfigAsync(
        string providerDhsCode,
        int domTableId,
        string domainName,
        string providerDomainCode,
        string providerDomainValue,
        int dhsDomainValue,
        bool? isDefault,
        string? codeValue,
        string? displayValue,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task UpsertMissingFromProviderConfigAsync(
        string providerDhsCode,
        int domainTableId,
        string domainName,
        string providerCodeValue,
        string providerNameValue,
        string? domainTableName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task UpdateApprovedStatusAsync(
        long domainMappingId,
        string? targetValue,
        DateTimeOffset utcNow,
        DateTimeOffset? lastPostedUtc,
        string? notes,
        CancellationToken cancellationToken);
}
