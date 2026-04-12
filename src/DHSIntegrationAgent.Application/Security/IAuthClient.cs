using DHSIntegrationAgent.Contracts.Security;

﻿namespace DHSIntegrationAgent.Application.Security;

/// <summary>
/// WBS 2.2 Auth/Login client contract used by WBS 5.3 Login screen.
/// </summary>
public interface IAuthClient
{
    /// <summary>
    /// Calls POST /api/UserManagementAPI/LoginUser (gzip JSON).
    /// GroupId comes from SQLite AppSettings (source of truth).
    /// </summary>
    Task<AuthLoginResult> LoginAsync(string email, string password, string groupId, CancellationToken ct);
}
