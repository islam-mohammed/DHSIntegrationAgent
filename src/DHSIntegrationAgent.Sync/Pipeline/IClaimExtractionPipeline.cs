using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Application.Providers;

namespace DHSIntegrationAgent.Sync.Pipeline;

public interface IClaimExtractionPipeline
{
    // Count claims in the given date range for a company code.
    Task<int> CountClaimsAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct);

    // Fetch the next page of claim keys using keyset pagination.
    Task<ClaimPage> GetNextPageAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset start,
        DateTimeOffset end,
        int pageSize,
        ResumeCursor? cursor,
        CancellationToken ct);

    // Fetch fully assembled raw bundles for the given claim keys.
    Task<IReadOnlyList<ProviderClaimBundleRaw>> FetchBundlesAsync(
        string providerDhsCode,
        IReadOnlyList<int> claimKeys,
        CancellationToken ct);

    // Pre-batch domain value scan.
    Task<IReadOnlyList<ScannedDomainValue>> GetDistinctDomainValuesAsync(
        string providerDhsCode,
        string companyCode,
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<BaselineDomain> domains,
        CancellationToken ct);

    // Runs schema validation gates; throws on first error-severity issue.
    Task ValidateSchemaAsync(string providerDhsCode, CancellationToken ct);
}
