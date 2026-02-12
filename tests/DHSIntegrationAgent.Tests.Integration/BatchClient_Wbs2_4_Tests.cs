using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Infrastructure.Http.Clients;
using DHSIntegrationAgent.Tests.Integration.TestHelpers;
using Microsoft.Extensions.Options;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

/// <summary>
/// WBS 2.4 contract tests for BatchClient:
/// - POST /api/Batch/CreateBatchRequest
/// - request JSON keys: batchRequests[] with companyCode, batchStartDate, batchEndDate, totalClaims, providerDhsCode
/// - gzip controlled by ApiOptions.UseGzipPostRequests
/// - response contract: { succeeded, statusCode, message, batchId }
/// - MUST parse JSON even if HTTP status is 500
/// </summary>
public sealed class BatchClient_Wbs2_4_Tests
{
    [Fact]
    public async Task CreateBatch_success_when_succeeded_true_and_statusCode_200_and_gzip_enabled()
    {
        var responseJson = """
            {
              "succeeded": true,
              "statusCode": 200,
              "message": "ok",
              "errors": null,
              "data": {
                "createdBatchIds": [ 123 ]
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

        var opts = Options.Create(new ApiOptions
        {
            BaseUrl = "https://example.invalid/",
            UseGzipPostRequests = true
        });

        var sut = new BatchClient(factory, opts);

        var items = new List<CreateBatchRequestItem>
        {
            new(
                CompanyCode: "45646546",
                BatchStartDate: DateTimeOffset.Parse("2026-02-02T06:28:26.189Z"),
                BatchEndDate: DateTimeOffset.Parse("2026-02-02T06:28:26.189Z"),
                TotalClaims: 77,
                ProviderDhsCode: "54455")
        };

        var result = await sut.CreateBatchAsync(items, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(123, result.BatchId);

        Assert.Single(handler.Captured);
        var captured = handler.Captured[0];

        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.EndsWith("/api/Batch/CreateBatchRequest", captured.Uri, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("gzip", captured.ContentEncoding, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(captured.DecodedBody);
        var root = doc.RootElement;

        var arr = root.GetProperty("batchRequests");
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(1, arr.GetArrayLength());

        var first = arr[0];
        Assert.Equal("45646546", first.GetProperty("companyCode").GetString());
        Assert.Equal("54455", first.GetProperty("providerDhsCode").GetString());
        Assert.Equal(77, first.GetProperty("totalClaims").GetInt32());
        Assert.True(first.TryGetProperty("batchStartDate", out _));
        Assert.True(first.TryGetProperty("batchEndDate", out _));
    }

    [Fact]
    public async Task CreateBatch_returns_backend_message_even_if_http_500()
    {
        var responseJson = """
            {
              "succeeded": false,
              "statusCode": 500,
              "message": "Server error",
              "errors": ["Some error"],
              "data": null
            }
            """;

        var handler = new CapturingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
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

        var sut = new BatchClient(factory, opts);

        var result = await sut.CreateBatchAsync(new[]
        {
            new CreateBatchRequestItem("c", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, "p")
        }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.StatusCode);
        Assert.Equal("Server error", result.ErrorMessage);
    }
}
