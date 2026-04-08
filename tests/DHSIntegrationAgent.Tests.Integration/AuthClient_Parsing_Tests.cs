using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Infrastructure.Http.Clients;
using DHSIntegrationAgent.Tests.Integration.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

public sealed class AuthClient_Parsing_Tests
{
    [Fact]
    public async Task Login_success_extracts_fullname()
    {
        var responseJson = """
{
  "succeeded": true,
  "statusCode": 200,
  "message": "Login successful",
  "errors": null,
  "data": {
    "succeeded": true,
    "statusCode": 200,
    "message": null,
    "errors": null,
    "data": {
      "email": "eelgendy@dhsarabia.com",
      "userName": "eman",
      "fullName": "eman magdy",
      "groupIds": null
    }
  }
}
""";

        var handler = new CapturingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") };
        var factory = new NamedClientFactory(httpClient);
        var sut = new AuthClient(factory);

        var result = await sut.LoginAsync("e", "p", "g", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("eman magdy", result.FullName);
    }
}
