using System.ComponentModel.DataAnnotations;

namespace DHSIntegrationAgent.Application.Configuration;

public sealed class ApiOptions
{
    [Required]
    [Url]
    public string BaseUrl { get; init; } = "";

    public bool UseGzipPostRequests { get; init; } = true;

    public string[] DisableGzipForEndpoints { get; init; } = Array.Empty<string>();

    public bool IsGzipDisabledForEndpoint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (DisableGzipForEndpoints == null || DisableGzipForEndpoints.Length == 0) return false;

        var normalizedPath = path.Trim().TrimStart('/').ToLowerInvariant();

        return DisableGzipForEndpoints
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Any(e =>
            {
                var normalizedConfig = e.Trim().TrimStart('/').ToLowerInvariant();
                return normalizedPath.Contains(normalizedConfig) || normalizedConfig.Contains(normalizedPath);
            });
    }
}
