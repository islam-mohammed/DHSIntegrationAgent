namespace DHSIntegrationAgent.Application.Claims;

/// <summary>
/// WBS 2.6 Resume API client contract.
/// Calls GET /api/Claims/GetHISProIdClaimsByBcrId/{bcrId}.
/// </summary>
public interface IResumeClient
{
    /// <summary>
    /// Returns the list of HIS ProIdClaim values confirmed inserted downstream for a given BcrId.
    /// Contract: response.data.hisProIdClaim[]
    /// </summary>
    Task<GetHisProIdClaimByBcrIdResult> GetHisProIdClaimByBcrIdAsync(string bcrId, CancellationToken ct);
}

/// <summary>
/// Normalized result for Stream C completion polling.
/// </summary>
public sealed record GetHisProIdClaimByBcrIdResult(
    bool Succeeded,
    string? ErrorMessage,
    IReadOnlyList<long> HisProIdClaim,
    int? HttpStatusCode
);
