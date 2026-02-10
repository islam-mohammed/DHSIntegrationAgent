using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record DomainMappingRow(
    long DomainMappingId,
    string ProviderDhsCode,
    string CompanyCode,
    string DomainName,
    int DomainTableId,
    string SourceValue,
    string TargetValue,
    DateTimeOffset DiscoveredUtc,
    DateTimeOffset? LastPostedUtc,
    DateTimeOffset LastUpdatedUtc,
    string? Notes);

public interface IDomainMappingRepository
{
    Task<IReadOnlyList<DomainMappingRow>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persist APPROVED mapping values (source -> target) into ApprovedDomainMapping table.
    /// Used by ProviderConfigurationService on config refresh and by refresh workflows later.
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

    Task UpdateAsync(
        long domainMappingId,
        string targetValue,
        DateTimeOffset utcNow,
        DateTimeOffset? lastPostedUtc,
        string? notes,
        CancellationToken cancellationToken);
}
