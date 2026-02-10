namespace DHSIntegrationAgent.Application.Persistence.Repositories;

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

public interface IProviderExtractionConfigRepository
{
    Task UpsertAsync(ProviderExtractionConfigRow row, CancellationToken cancellationToken);
    Task<ProviderExtractionConfigRow?> GetAsync(ProviderKey key, CancellationToken cancellationToken);
}
