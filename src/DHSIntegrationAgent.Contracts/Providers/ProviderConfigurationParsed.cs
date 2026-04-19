using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Contracts.Providers;

/// <summary>
/// We only parse the subset that is stable/needed now (WBS 2.3):
/// - providerPayers list (CompanyCode + optional PayerCode + names)
/// - domainMappings (approved) kept as JsonArray for later steps
/// - missingDomainMappings kept as JsonArray for later steps
/// </summary>
public sealed record ProviderConfigurationParsed(
    IReadOnlyList<ProviderPayerDto> ProviderPayers,
    JsonArray? DomainMappings,
    JsonArray? MissingDomainMappings
);
