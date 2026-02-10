using System.ComponentModel.DataAnnotations;

namespace DHSIntegrationAgent.Application.Configuration;

public sealed class AppOptions
{
    [Required]
    public string EnvironmentName { get; init; } = "Development";

    public string DatabasePath { get; init; } = "";

    public string LogFolder { get; init; } = "";
}
