using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddDhsApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // App (non-secret)
        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection("App"))
            .ValidateDataAnnotations()
            .ValidateOnStart();


        // Api (non-secret but required)
        services.AddOptions<ApiOptions>()
            .Bind(configuration.GetSection("Api"))
            .ValidateDataAnnotations()
            .Validate(o =>
            {
                if (!Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri)) return false;
                return uri.Scheme is "http" or "https";
            }, "Api:BaseUrl must be an absolute http/https URL.")
            .ValidateOnStart();

        // AzureBlob (secrets policy enforcement)
        services.AddOptions<AzureBlobOptions>()
            .Bind(configuration.GetSection("AzureBlob"))
            .ValidateDataAnnotations()
            .Validate(o => string.IsNullOrWhiteSpace(o.SasUrl),
                "AzureBlob:SasUrl must NOT be set in appsettings/env vars. Store SAS securely (DPAPI-protected in SQLite settings) and load it at runtime.")
            .ValidateOnStart();

        return services;
    }
}
