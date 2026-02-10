using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Observability;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Infrastructure.Http;
using DHSIntegrationAgent.Infrastructure.Http.Clients;
using DHSIntegrationAgent.Infrastructure.Observability;
using DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;
using DHSIntegrationAgent.Infrastructure.Providers;
using DHSIntegrationAgent.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;

namespace DHSIntegrationAgent.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDhsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ------------------------------------------------------------
        // Observability
        // ------------------------------------------------------------
        services.AddSingleton<IApiCallRecorder, LoggerApiCallRecorder>();

        // ------------------------------------------------------------
        // Auth header provider (currently no-op header provider)
        // ------------------------------------------------------------
        services.AddSingleton<IAuthHeaderProvider, NoAuthHeaderProvider>();

        // ------------------------------------------------------------
        // SQLite (WBS 1.1)
        // ------------------------------------------------------------
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<ISqliteMigrator, SqliteMigrator>();
        services.AddHostedService<SqliteMigrationHostedService>();

        // ------------------------------------------------------------
        // Crypto / PHI encryption at rest (WBS 1.3)
        // ------------------------------------------------------------
        services.AddSingleton<IKeyProtector, DpapiKeyProtector>();
        services.AddSingleton<IKeyRing, SqliteKeyRing>();
        services.AddSingleton<IColumnEncryptor, AesGcmColumnEncryptor>();
        services.AddHostedService<CryptoKeyInitializationHostedService>();

        // ------------------------------------------------------------
        // Crash recovery routines (WBS 1.4)
        // ------------------------------------------------------------
        services.AddHostedService<SqliteCrashRecoveryHostedService>();

        // ------------------------------------------------------------
        // Unit of Work (WBS 1.2 + WBS 1.3 integration)
        // ------------------------------------------------------------
        services.AddSingleton<ISqliteUnitOfWorkFactory, SqliteUnitOfWorkFactory>();

        // -----------------------------------------------------------
        // Provider config API client + cache service (WBS 2.3)
        // -----------------------------------------------------------
        services.AddSingleton<ProviderConfigurationClient>();
        services.AddSingleton<IProviderConfigurationService, ProviderConfigurationService>();

        // -----------------------------------------------------------
        // Auth/Login API client (WBS 2.2)  ✅ REQUIRED for LoginService
        // -----------------------------------------------------------
        services.AddSingleton<IAuthClient, AuthClient>();

        // -----------------------------------------------------------
        // Batch API client (WBS 2.4)  ✅ REQUIRED for BatchesViewModel
        // -----------------------------------------------------------
        services.AddSingleton<IBatchClient, BatchClient>();

        // -----------------------------------------------------------
        // Domain Mapping API client  ✅ REQUIRED for DomainMappingsViewModel
        // -----------------------------------------------------------
        services.AddSingleton<IDomainMappingClient, DomainMappingClient>();

        // -----------------------------------------------------------
        // Approved Domain Mapping refresh (Change Request)
        // -----------------------------------------------------------
        services.AddSingleton<ProviderDomainMappingClient>();
        services.AddSingleton<IApprovedDomainMappingRefreshService, ApprovedDomainMappingRefreshService>();

        // ------------------------------------------------------------
        // HTTP client pipeline
        // ------------------------------------------------------------
        services.AddTransient<GzipRequestHandler>();
        services.AddTransient<AuthHeaderHandler>();
        services.AddTransient<ApiLoggingHandler>();

        services.AddHttpClient("BackendApi", (sp, client) =>
        {
            var api = sp.GetRequiredService<IOptions<ApiOptions>>().Value;

            if (string.IsNullOrWhiteSpace(api.BaseUrl))
                throw new InvalidOperationException("Api:BaseUrl must be configured in appsettings.json");

            client.BaseAddress = new Uri(api.BaseUrl, UriKind.Absolute);

            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            return new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    RemoteCertificateValidationCallback = null
                }
            };
        })
        .AddHttpMessageHandler<GzipRequestHandler>()
        .AddHttpMessageHandler<AuthHeaderHandler>()
        .AddHttpMessageHandler<ApiLoggingHandler>();

        return services;
    }
}
