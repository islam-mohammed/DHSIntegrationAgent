using DHSIntegrationAgent.Bootstrapper;
using DHSIntegrationAgent.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DHSIntegrationAgent.Tests.Unit;

public class DependencyInjectionTests
{
    [Fact]
    public void WorkerEngine_CanBeResolved()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "http://localhost",
                ["App:DatabasePath"] = "test.db",
                ["App:EnvironmentName"] = "Development"
            })
            .Build();

        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new MockHostEnv());

        services.AddDhsIntegrationAgent(configuration);

        var serviceProvider = services.BuildServiceProvider();

        // This will throw if dependencies are missing
        var workerEngine = serviceProvider.GetService<WorkerEngine>();

        Assert.NotNull(workerEngine);
    }

    private sealed class MockHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
