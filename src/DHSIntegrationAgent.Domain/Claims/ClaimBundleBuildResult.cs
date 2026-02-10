namespace DHSIntegrationAgent.Domain.Claims;

/// <summary>
/// Result of building/normalizing a ClaimBundle.
/// Contains extracted deterministic identity signals used by SQLite staging.
/// </summary>
public sealed record ClaimBundleBuildResult(
    bool Succeeded,
    int ProIdClaim,
    string CompanyCode,
    string MonthKey,
    ClaimBundle? Bundle,
    IReadOnlyList<ClaimBundleValidationIssue> Issues
);

public sealed record ClaimBundleValidationIssue(
    string IssueType,
    string? FieldPath,
    string? RawValue,
    string Message,
    bool IsBlocking
);
