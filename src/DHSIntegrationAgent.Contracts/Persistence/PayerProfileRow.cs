namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record PayerProfileRow(
    string ProviderDhsCode,
    string CompanyCode,
    string? PayerCode,
    string? PayerName,
    bool IsActive);
