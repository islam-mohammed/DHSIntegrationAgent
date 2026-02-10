namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record PayerProfileRow(
    string ProviderDhsCode,
    string CompanyCode,
    string? PayerCode,
    string? PayerName,
    bool IsActive);

public sealed record PayerItem(
    int PayerId,
    string? PayerName);

public interface IPayerProfileRepository
{
    Task UpsertManyAsync(
        IReadOnlyList<PayerProfileRow> rows,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PayerProfileRow>> ListActiveAsync(string providerDhsCode, CancellationToken cancellationToken);

    Task SetActiveAsync(string providerDhsCode, string companyCode, bool isActive, DateTimeOffset utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<PayerItem>> GetAllPayersAsync(string providerDhsCode, CancellationToken cancellationToken);
}
