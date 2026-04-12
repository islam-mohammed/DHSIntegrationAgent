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

        var normalizedPath = path.ToLowerInvariant();
        return DisableGzipForEndpoints.Any(e => normalizedPath.Contains(e.ToLowerInvariant()));
    }
}
