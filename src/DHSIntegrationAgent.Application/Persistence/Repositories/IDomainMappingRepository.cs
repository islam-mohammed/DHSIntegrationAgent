using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IDomainMappingRepository
{
    Task<IReadOnlyList<ApprovedDomainMappingRow>> GetAllApprovedAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MissingDomainMappingRow>> GetAllMissingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MissingDomainMappingRow>> ListEligibleForPostingAsync(string providerDhsCode, CancellationToken ct);

    Task<IReadOnlyList<DomainMappingRow>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListProvidersWithPendingMappingsAsync(CancellationToken ct);

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

    Task UpdateMissingStatusAsync(
        long missingMappingId,
        MappingStatus status,
        DateTimeOffset utcNow,
        DateTimeOffset? lastPostedUtc,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string providerDhsCode, int domainTableId, string sourceValue, CancellationToken ct);
}
