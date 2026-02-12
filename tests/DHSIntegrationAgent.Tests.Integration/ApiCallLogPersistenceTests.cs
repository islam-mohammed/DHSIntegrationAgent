using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

public sealed class ApiCallLogPersistenceTests
{
    [Fact]
    public async Task ApiLoggingHandler_SavesToDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apicalllog_{Guid.NewGuid():N}.db");
        var baseUrl = "http://127.0.0.1:54321/";

        var cfg = BuildConfig(dbPath, baseUrl);

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDhsApplication(cfg);
                services.AddDhsInfrastructure(cfg);
            })
            .Build();

        await host.StartAsync();

        // Ensure DB exists
        var migrator = host.Services.GetRequiredService<ISqliteMigrator>();
        await migrator.MigrateAsync(default);

        var httpFactory = host.Services.GetRequiredService<IHttpClientFactory>();
        var client = httpFactory.CreateClient("BackendApi");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls(baseUrl);
        var app = builder.Build();
        app.MapGet("/api/Batch/GetBatchRequest/{provider}", () => Results.Ok(new { success = true }));
        await app.StartAsync();

        try
        {
            await client.GetAsync("api/Batch/GetBatchRequest/PROV_LOG_TEST");

            // Allow the background writer to flush.
            await WaitForLogAsync(host.Services, providerDhsCode: "PROV_LOG_TEST", timeoutMs: 2000);

            await using var uow = await host.Services.GetRequiredService<ISqliteUnitOfWorkFactory>().CreateAsync(default);
            var logs = await uow.ApiCallLogs.GetRecentApiCallsAsync(50, default);

            var log = logs.FirstOrDefault(l => l.ProviderDhsCode == "PROV_LOG_TEST");
            Assert.NotNull(log);
            Assert.Equal("Batch_Get", log!.EndpointName);
            Assert.True(log.Succeeded);
            Assert.Equal(200, log.HttpStatusCode);
            Assert.NotNull(log.ResponseUtc);
            Assert.True(log.DurationMs >= 0);
        }
        finally
        {
            await app.StopAsync();
            await host.StopAsync();

            SafeDelete(dbPath);
            SafeDelete(dbPath + "-wal");
            SafeDelete(dbPath + "-shm");
        }
    }

    [Fact]
    public async Task ApiLoggingHandler_SavesToDatabase_EvenIfCancelled()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apicalllog_cancel_{Guid.NewGuid():N}.db");
        var baseUrl = "http://127.0.0.1:54322/";

        var cfg = BuildConfig(dbPath, baseUrl);

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDhsApplication(cfg);
                services.AddDhsInfrastructure(cfg);
            })
            .Build();

        await host.StartAsync();

        var migrator = host.Services.GetRequiredService<ISqliteMigrator>();
        await migrator.MigrateAsync(default);

        var httpFactory = host.Services.GetRequiredService<IHttpClientFactory>();
        var client = httpFactory.CreateClient("BackendApi");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls(baseUrl);
        var app = builder.Build();
        app.MapGet("/api/Batch/GetBatchRequest/{provider}", async (HttpContext context) =>
        {
            await Task.Delay(1000, context.RequestAborted);
            return Results.Ok(new { success = true });
        });
        await app.StartAsync();

        try
        {
            using var cts = new CancellationTokenSource();
            var task = client.GetAsync("api/Batch/GetBatchRequest/PROV_CANCEL_TEST", cts.Token);

            await Task.Delay(100);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            await WaitForLogAsync(host.Services, providerDhsCode: "PROV_CANCEL_TEST", timeoutMs: 3000);

            await using var uow = await host.Services.GetRequiredService<ISqliteUnitOfWorkFactory>().CreateAsync(default);
            var logs = await uow.ApiCallLogs.GetRecentApiCallsAsync(50, default);

            var log = logs.FirstOrDefault(l => l.ProviderDhsCode == "PROV_CANCEL_TEST");
            Assert.NotNull(log);
            Assert.Equal("Batch_Get", log!.EndpointName);
            Assert.False(log.Succeeded);
        }
        finally
        {
            await app.StopAsync();
            await host.StopAsync();

            SafeDelete(dbPath);
            SafeDelete(dbPath + "-wal");
            SafeDelete(dbPath + "-shm");
        }
    }

    [Fact]
    public async Task ApiLoggingHandler_ExtractsProviderDhsCode_WithQueryString()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apicalllog_query_{Guid.NewGuid():N}.db");
        var baseUrl = "http://127.0.0.1:54323/";

        var cfg = BuildConfig(dbPath, baseUrl);

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDhsApplication(cfg);
                services.AddDhsInfrastructure(cfg);
            })
            .Build();

        await host.StartAsync();

        var migrator = host.Services.GetRequiredService<ISqliteMigrator>();
        await migrator.MigrateAsync(default);

        var httpFactory = host.Services.GetRequiredService<IHttpClientFactory>();
        var client = httpFactory.CreateClient("BackendApi");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls(baseUrl);
        var app = builder.Build();
        app.MapGet("/api/Batch/GetBatchRequest/{provider}", () => Results.Ok(new { success = true }));
        await app.StartAsync();

        try
        {
            await client.GetAsync("api/Batch/GetBatchRequest/PROV_QUERY_TEST?foo=bar");

            await WaitForLogAsync(host.Services, providerDhsCode: "PROV_QUERY_TEST", timeoutMs: 2000);

            await using var uow = await host.Services.GetRequiredService<ISqliteUnitOfWorkFactory>().CreateAsync(default);
            var logs = await uow.ApiCallLogs.GetRecentApiCallsAsync(50, default);

            var log = logs.FirstOrDefault(l => l.ProviderDhsCode == "PROV_QUERY_TEST");
            Assert.NotNull(log);
            Assert.Equal("Batch_Get", log!.EndpointName);
        }
        finally
        {
            await app.StopAsync();
            await host.StopAsync();

            SafeDelete(dbPath);
            SafeDelete(dbPath + "-wal");
            SafeDelete(dbPath + "-shm");
        }
    }

    private static IConfiguration BuildConfig(string dbPath, string baseUrl)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Api:BaseUrl", baseUrl),
                new KeyValuePair<string, string?>("App:DatabasePath", dbPath),
                new KeyValuePair<string, string?>("App:EnvironmentName", "Development"),
            })
            .Build();
    }

    private static async Task WaitForLogAsync(IServiceProvider sp, string providerDhsCode, int timeoutMs)
    {
        var start = Environment.TickCount;

        while (Environment.TickCount - start < timeoutMs)
        {
            await using var uow = await sp.GetRequiredService<ISqliteUnitOfWorkFactory>().CreateAsync(default);
            var logs = await uow.ApiCallLogs.GetRecentApiCallsAsync(50, default);

            if (logs.Any(l => l.ProviderDhsCode == providerDhsCode))
                return;

            await Task.Delay(50);
        }

        // Let the assertion fail with a helpful message.
        await using (var uow = await sp.GetRequiredService<ISqliteUnitOfWorkFactory>().CreateAsync(default))
        {
            var logs = await uow.ApiCallLogs.GetRecentApiCallsAsync(50, default);
            Assert.True(logs.Any(l => l.ProviderDhsCode == providerDhsCode),
                $"Timed out waiting for API call log with ProviderDhsCode={providerDhsCode}.");
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
