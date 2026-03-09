using System.Net;
using System.Text.Json;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Infrastructure.Http.Clients;
using DHSIntegrationAgent.Tests.Integration.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DHSIntegrationAgent.Tests.Integration;

public class HealthClientTests
{
    [Fact]
    public async Task CheckApiHealthAsync_ReturnsSuccess_WhenApiResponseIsSuccess()
    {
        // Arrange
        var mockResponse = new { succeeded = true, statusCode = 200, message = "Success", errors = (List<string>?)null };
        var jsonResponse = JsonSerializer.Serialize(mockResponse);

        var services = new ServiceCollection();

        var options = Options.Create(new ApiOptions { BaseUrl = "http://localhost", UseGzipPostRequests = false });
        services.AddSingleton(options);

        var mockHandler = new CapturingHttpMessageHandler(req =>
        {
            if (req.RequestUri?.PathAndQuery == "/api/Health/CheckAPIHealth")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jsonResponse) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        services.AddHttpClient("BackendApi", client =>
        {
            client.BaseAddress = new Uri("http://localhost");
        }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);

        services.AddSingleton<IHealthClient, HealthClient>();
        var sp = services.BuildServiceProvider();

        var sut = sp.GetRequiredService<IHealthClient>();

        // Act
        var result = await sut.CheckApiHealthAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Success", result.Message);
    }
}
