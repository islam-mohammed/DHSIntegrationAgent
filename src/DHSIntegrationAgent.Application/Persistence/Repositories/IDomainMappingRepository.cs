using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

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
    Task<IReadOnlyList<DomainMappingRow>> GetAllAsync(CancellationToken cancellationToken);

    Task UpsertDiscoveredAsync(
        string providerDhsCode,
        string companyCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        MappingStatus mappingStatus,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Minimal upsert used by some flows: approved mapping (source -> target).
    /// </summary>
    Task UpsertApprovedAsync(
        string providerDhsCode,
        string companyCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        string targetValue,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Upsert APPROVED mappings coming from Provider Configuration:
    /// DomainMappingDto keys:
    /// domTable_ID, providerDomainCode, providerDomainValue, dhsDomainValue, isDefault, codeValue, displayValue
    /// </summary>
    Task UpsertApprovedFromProviderConfigAsync(
        string providerDhsCode,
        string companyCode,
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

    /// <summary>
    /// Upsert MISSING mappings coming from Provider Configuration:
    /// MissingDomainMappingDto keys:
    /// providerCodeValue, providerNameValue, domainTableId, domainTableName
    /// Merge rule: never downgrade Approved and never delete local scan-discovered missing.
    /// </summary>
    Task UpsertMissingFromProviderConfigAsync(
        string providerDhsCode,
        string companyCode,
        int domainTableId,
        string domainName,
        string providerCodeValue,
        string providerNameValue,
        string? domainTableName,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task UpdateStatusAsync(
        long domainMappingId,
        MappingStatus status,
        string? targetValue,
        DateTimeOffset utcNow,
        DateTimeOffset? lastPostedUtc,
        string? notes,
        CancellationToken cancellationToken);
}
