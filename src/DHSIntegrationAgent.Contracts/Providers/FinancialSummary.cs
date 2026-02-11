namespace DHSIntegrationAgent.Contracts.Providers;

/// <summary>
/// WBS 3.2: Financial summary for a batch of claims from the provider HIS.
/// </summary>
public record FinancialSummary(
    int TotalClaims,
    decimal TotalNetAmount,
    decimal TotalClaimedAmount,
    decimal TotalDiscount,
    decimal TotalDeductible)
{
    /// <summary>
    /// Validation formula: sum(TotalNetAmount) = sum(ClaimedAmount) - sum(TotalDiscount) - sum(TotalDeductible)
    /// </summary>
    public bool IsValid => TotalNetAmount == (TotalClaimedAmount - TotalDiscount - TotalDeductible);
}
