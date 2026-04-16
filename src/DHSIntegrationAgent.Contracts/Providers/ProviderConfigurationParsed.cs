using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Contracts.Providers;

/// <summary>
/// We only parse the subset that is stable/needed now (WBS 2.3):
/// - providerPayers list (CompanyCode + optional PayerCode + names)
/// - domainMappings (approved) kept as JsonArray for later steps
/// - missingDomainMappings kept as JsonArray for later steps
/// - single-view specific configurations like FollowupDays
/// </summary>
public sealed record ProviderConfigurationParsed(
    IReadOnlyList<ProviderPayerDto> ProviderPayers,
    JsonArray? DomainMappings,
    JsonArray? MissingDomainMappings,
    int? ClaimSplitFollowupDays = null,
    string? DefaultTreatmentCountryCode = null,
    string? DefaultSubmissionReasonCode = null,
    string? DefaultPriority = null,
    int? DefaultErPbmDuration = null,
    string? SfdaServiceTypeIdentifier = null,
    IReadOnlyList<string>? PayersToDropZeroAmountServices = null
);
