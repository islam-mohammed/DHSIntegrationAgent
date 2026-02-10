namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public readonly record struct ClaimKey(string ProviderDhsCode, int ProIdClaim);
public readonly record struct BatchKey(string ProviderDhsCode, string CompanyCode, string MonthKey); // YYYYMM
public readonly record struct ProviderKey(string ProviderCode);
