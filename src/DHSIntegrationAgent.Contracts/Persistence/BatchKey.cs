namespace DHSIntegrationAgent.Contracts.Persistence;

public readonly record struct BatchKey(
    string ProviderDhsCode,
    string CompanyCode,
    string MonthKey,
    DateTimeOffset StartDateUtc,
    DateTimeOffset EndDateUtc);
