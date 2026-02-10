namespace DHSIntegrationAgent.Contracts.Providers;

public sealed record ProviderPayerDto(
    string CompanyCode,
    string? PayerCode,
    string? PayerNameEn,
    string? ParentPayerNameEn
);
