using DHSIntegrationAgent.Contracts.DomainMapping;

﻿namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// WBS 2.7 Domain Mapping API client contract.
/// POST /api/DomainMapping/InsertMissMappingDomain (gzip JSON).
/// GET /api/DomainMapping/GetMissingDomainMappings/{providerDhsCode}
/// </summary>
public interface IDomainMappingClient
{
    Task<InsertMissMappingDomainResult> InsertMissMappingDomainAsync(
        InsertMissMappingDomainRequest request,
        CancellationToken ct);

    Task<GetMissingDomainMappingsResult> GetMissingDomainMappingsAsync(
        string providerDhsCode,
        CancellationToken ct);

    Task<GetProviderDomainMappingsWithMissingResult> GetProviderDomainMappingsWithMissingAsync(
        string providerDhsCode,
        CancellationToken ct);
}
