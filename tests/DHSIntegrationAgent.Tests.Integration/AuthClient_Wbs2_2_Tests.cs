using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Infrastructure.Http.Clients;
using DHSIntegrationAgent.Tests.Integration.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

/// <summary>
/// WBS 2.2 contract tests for AuthClient:
/// - POST /api/Authentication/login
/// - request JSON keys: email, groupID, password
/// - gzip is controlled by ApiOptions.UseGzipPostRequests
/// - response contract (JSON): { succeeded, statusCode, message }
/// - MUST parse JSON even if HTTP status is 401/500
/// </summary>
public sealed class AuthClient_Wbs2_2_Tests
{
    [Fact]
    public async Task Login_success_when_succeeded_true_and_statusCode_200_and_gzip_enabled()
    {
        var responseJson = """{"succeeded": true, "statusCode": 200, "message": "Login successful"}""";

        var handler = new CapturingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var factory = new NamedClientFactory(httpClient);

        var opts = Options.Create(new ApiOptions
        {
            BaseUrl = "https://example.invalid/",
            UseGzipPostRequests = true
        });

        var sut = new AuthClient(factory, opts);

        var email = "eelgendy@dhsarabia.com";
        var password = "A_123456a";
        var groupId = "d9a90b95-906e-434c-8ff7-ad1c976c9b50";

        var result = await sut.LoginAsync(email, groupId, password, CancellationToken.None);

        Assert.True(result.Succeeded);

        Assert.Single(handler.Captured);
        var captured = handler.Captured[0];

        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.EndsWith("/api/Authentication/login", captured.Uri, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("gzip", captured.ContentEncoding, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(captured.DecodedBody);
        var root = doc.RootElement;

        Assert.Equal(email, root.GetProperty("email").GetString());
        Assert.Equal(password, root.GetProperty("password").GetString());
        Assert.Equal(groupId, root.GetProperty("groupID").GetString());
    }
}
