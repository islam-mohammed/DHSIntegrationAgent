using System.Net;
using System.Text;
using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Infrastructure;
using DHSIntegrationAgent.Application.Observability;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

public sealed class ApiCallLogPersistenceTests
{
    [Fact]
    public async Task ApiLoggingHandler_SavesToDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apicalllog_{Guid.NewGuid():N}.db");
        var baseUrl = "http://127.0.0.1:54321/";

        var services = new ServiceCollection();
        services.AddLogging();

        var mockEnv = new MockHostEnvironment { EnvironmentName = "Development" };
        services.AddSingleton<IHostEnvironment>(mockEnv);

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Api:BaseUrl", baseUrl),
                new KeyValuePair<string, string?>("App:DatabasePath", dbPath),
                new KeyValuePair<string, string?>("App:EnvironmentName", "Development"),
            })
            .Build();

        services.AddDhsApplication(cfg);
        services.AddDhsInfrastructure(cfg);

        await using var sp = services.BuildServiceProvider();

        var migrator = sp.GetRequiredService<ISqliteMigrator>();
        await migrator.MigrateAsync(default);

        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var client = httpFactory.CreateClient("BackendApi");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls(baseUrl);
        var app = builder.Build();
        app.MapGet("/api/Batch/GetBatchRequest/{provider}", () => Results.Ok(new { success = true }));
        await app.StartAsync();

        try
        {
            var response = await client.GetAsync("api/Batch/GetBatchRequest/PROV_LOG_TEST");

            await using var uow = await sp.GetRequiredService<ISqliteUnitOfWorkFactory>().CreateAsync(default);
            var logs = await uow.ApiCallLogs.GetRecentApiCallsAsync(10, default);

            Assert.NotEmpty(logs);
            var log = logs.FirstOrDefault(l => l.ProviderDhsCode == "PROV_LOG_TEST");
            Assert.NotNull(log);
            Assert.Equal("Batch_Get", log.EndpointName);
            Assert.True(log.Succeeded);
            Assert.Equal(200, log.HttpStatusCode);
            Assert.NotNull(log.ResponseUtc);
            Assert.True(log.DurationMs >= 0);
        }
        finally
        {
            await app.StopAsync();
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    private sealed class MockHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = default!;
        public string ApplicationName { get; set; } = default!;
        public string ContentRootPath { get; set; } = default!;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = default!;
    }
}
