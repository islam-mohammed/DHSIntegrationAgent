using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IMissingDomainMappingRepository
{
    Task UpsertAsync(MissingDomainMappingRow row, CancellationToken ct);
    Task<IReadOnlyList<MissingDomainMappingRow>> GetByProviderAsync(string providerDhsCode, CancellationToken ct);
}

public sealed record MissingDomainMappingRow(
    string ProviderDhsCode,
    int DomainTableId,
    string DomainName,
    string ProviderValue,
    string? ProviderNameValue,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string RawJson);