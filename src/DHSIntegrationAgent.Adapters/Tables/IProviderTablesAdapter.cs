using System.Text.Json.Nodes;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Contracts.Providers;

namespace DHSIntegrationAgent.Adapters.Tables;

public interface IProviderTablesAdapter
{
    Task<int> CountClaimsAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        CancellationToken ct);

    Task<FinancialSummary> GetFinancialSummaryAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        CancellationToken ct);

    Task<IReadOnlyList<int>> ListClaimKeysAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        int pageSize,
        int? lastSeenClaimKey,
        CancellationToken ct);

    Task<ProviderClaimBundleRaw?> GetClaimBundleRawAsync(
        string providerDhsCode,
        int proIdClaim,
        CancellationToken ct);

    Task<IReadOnlyList<ScannedDomainValue>> GetDistinctDomainValuesAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset batchStartDateUtc,
        DateTimeOffset batchEndDateUtc,
        IReadOnlyList<BaselineDomain> domains,
        CancellationToken ct);
}
