namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// WBS 2.8 API Health Client contract.
/// Calls GET /api/Health/CheckAPIHealth to verify backend connectivity and health.
/// </summary>
public interface IHealthClient
{
    /// <summary>
    /// Performs a health check against the backend API.
    /// </summary>
    Task<ApiHealthResult> CheckApiHealthAsync(CancellationToken ct);
}

/// <summary>
/// Result of the health check operation matching ObjectBaseResponse.
/// </summary>
public sealed record ApiHealthResult(
    bool Succeeded,
    int StatusCode,
    string? Message,
    IReadOnlyList<string>? Errors
);
