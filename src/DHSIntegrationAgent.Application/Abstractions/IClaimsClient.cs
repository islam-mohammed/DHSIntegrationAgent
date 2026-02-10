using DHSIntegrationAgent.Contracts.Claims;

﻿namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// WBS 2.5 Claims API client contract.
/// Calls POST /api/Claims/SendClaim (gzip JSON array payload).
/// </summary>
public interface IClaimsClient
{
    /// <summary>
    /// Sends a JSON array of ClaimBundle objects to the backend queue.
    /// Payload MUST be a JSON array string (e.g. "[{...},{...}]").
    /// Gzip compression is applied by the HTTP pipeline (GzipRequestHandler).
    /// </summary>
    Task<SendClaimResult> SendClaimAsync(string claimBundlesJsonArray, CancellationToken ct);
}
