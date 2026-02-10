using DHSIntegrationAgent.Application.Abstractions;

﻿namespace DHSIntegrationAgent.Application.Providers;

/// <summary>
/// Lower-tier service used by UI/engine to refresh approved domain mappings from backend,
/// and persist them to SQLite (DomainMapping table).
/// No WPF dependency.
/// </summary>
public interface IApprovedDomainMappingRefreshService
{
    /// <summary>
    /// Calls backend endpoint /api/DomainMapping/GetProviderDomainMapping/{providerDhsCode}
    /// and upserts approved mappings into SQLite.
    /// </summary>
    Task RefreshApprovedMappingsAsync(CancellationToken ct);

    /// <summary>
    /// Calls backend endpoint /api/DomainMapping/GetProviderDomainMappingsWithMissing/{providerDhsCode}
    /// and upserts both approved and missing mappings into SQLite.
    /// </summary>
    Task<ProviderDomainMappingsData?> RefreshAllMappingsAsync(CancellationToken ct);
}
