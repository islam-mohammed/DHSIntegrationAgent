namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record FailedClaimPageItem(
    string ProviderDhsCode,
    int ProIdClaim,
    string CompanyCode,
    string MonthKey,
    long? BatchId,
    int AttemptCount,
    string? LastError,
    DateTimeOffset LastUpdatedUtc);
