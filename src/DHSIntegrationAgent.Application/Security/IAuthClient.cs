namespace DHSIntegrationAgent.Application.Security;

/// <summary>
/// WBS 2.2 Auth/Login client contract used by WBS 5.3 Login screen.
/// </summary>
public interface IAuthClient
{
    /// <summary>
    /// Calls POST /api/Authentication/login (gzip JSON).
    /// GroupId comes from SQLite AppSettings (source of truth).
    /// </summary>
    Task<AuthLoginResult> LoginAsync(string email, string password, string groupId, CancellationToken ct);
}

/// <summary>
/// Normalized login result consumed by UI/business logic.
/// Token is optional because some backends may not return a token.
/// </summary>
public sealed record AuthLoginResult(
    bool Succeeded,
    string? ErrorMessage,
    string? Token);
