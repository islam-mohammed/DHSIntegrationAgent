namespace DHSIntegrationAgent.Contracts.Security;

/// <summary>
/// Normalized login result consumed by UI/business logic.
/// Token is optional because some backends may not return a token.
/// </summary>
public sealed record AuthLoginResult(
    bool Succeeded,
    string? ErrorMessage,
    string? Token);
