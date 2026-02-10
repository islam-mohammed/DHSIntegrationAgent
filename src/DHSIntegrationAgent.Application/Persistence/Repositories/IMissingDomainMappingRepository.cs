using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record MissingDomainMappingRow(
    long MissingMappingId,
    string ProviderDhsCode,
    string CompanyCode,
    string DomainName,
    int DomainTableId,
    string SourceValue,
    DiscoverySource DiscoverySource,
    DateTimeOffset DiscoveredUtc,
    DateTimeOffset LastUpdatedUtc,
    string? Notes);

public interface IMissingDomainMappingRepository
{
    Task UpsertAsync(
        string providerDhsCode,
        string companyCode,
        string domainName,
        int domainTableId,
        string sourceValue,
        DiscoverySource discoverySource,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MissingDomainMappingRow>> GetByProviderAsync(string providerDhsCode, CancellationToken ct);
}