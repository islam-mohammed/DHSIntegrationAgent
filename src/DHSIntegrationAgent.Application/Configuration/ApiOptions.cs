using System.ComponentModel.DataAnnotations;

namespace DHSIntegrationAgent.Application.Configuration;

public sealed class ApiOptions
{
    [Required]
    [Url]
    public string BaseUrl { get; init; } = "";

    public bool UseGzipPostRequests { get; init; } = true;
}
