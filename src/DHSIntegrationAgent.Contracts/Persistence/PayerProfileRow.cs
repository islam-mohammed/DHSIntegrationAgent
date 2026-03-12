namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record PayerProfileRow(
    int PayerId,
    string ProviderDhsCode,
    string CompanyCode,
    string? PayerCode,
    string? PayerName,
    bool IsActive);
