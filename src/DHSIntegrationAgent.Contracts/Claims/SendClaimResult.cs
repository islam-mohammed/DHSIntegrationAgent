namespace DHSIntegrationAgent.Contracts.Claims;

/// <summary>
/// Normalized result for SendClaim.
/// Backend contract (per Stream Specs):
/// response.data.successClaimsProidClaim[] and response.data.failClaimsProidClaim[].
/// </summary>
public sealed record SendClaimResult(
    bool Succeeded,
    string? ErrorMessage,
    IReadOnlyList<long> SuccessClaimsProIdClaim,
    IReadOnlyList<long> FailClaimsProIdClaim,
    int? HttpStatusCode
);
