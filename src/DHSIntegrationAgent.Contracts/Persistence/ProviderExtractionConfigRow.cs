namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record ProviderExtractionConfigRow(
    string ProviderCode,
    string? ClaimKeyColumnName,
    string? HeaderSourceName,
    string? DetailsSourceName,
    string? CustomHeaderSql,
    string? CustomServiceSql,
    string? CustomDiagnosisSql,
    string? CustomLabSql,
    string? CustomRadiologySql,
    string? CustomOpticalSql,
    string? Notes,
    DateTimeOffset UpdatedUtc);
