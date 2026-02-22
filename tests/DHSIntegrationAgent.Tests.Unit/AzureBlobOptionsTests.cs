using DHSIntegrationAgent.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DHSIntegrationAgent.Tests.Unit;

public class AzureBlobOptionsTests
{
    [Fact]
    public void AzureBlobOptions_CanBeBound_WithNewNames()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureBlob:AttachmentBlobStorageCon"] = "AccountName=test;AccountKey=key",
                ["AzureBlob:AttachmentBlobStorageContainer"] = "test-container"
            })
            .Build();

        services.Configure<AzureBlobOptions>(configuration.GetSection("AzureBlob"));
        var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AzureBlobOptions>>().Value;

        Assert.Equal("AccountName=test;AccountKey=key", options.AttachmentBlobStorageCon);
        Assert.Equal("test-container", options.AttachmentBlobStorageContainer);
    }

    [Fact]
    public void AzureBlobOptions_CompatibilityProperties_Work()
    {
        var options = new AzureBlobOptions
        {
            AttachmentBlobStorageCon = "AccountName=test;AccountKey=key",
            AttachmentBlobStorageContainer = "test-container"
        };

        Assert.Equal(options.AttachmentBlobStorageCon, options.ConnectionString);
        Assert.Equal(options.AttachmentBlobStorageContainer, options.ContainerName);
    }
}
