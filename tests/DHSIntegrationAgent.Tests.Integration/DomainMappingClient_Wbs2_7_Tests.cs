using DHSIntegrationAgent.Contracts.DomainMapping;
using DHSIntegrationAgent.Contracts.Providers;
﻿using System;
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
/// WBS 2.7 contract tests for DomainMappingClient:
/// - POST /api/DomainMapping/InsertMissMappingDomain
/// - request JSON keys: providerDhsCode, mismappedItems[] with providerCodeValue, providerNameValue, domainTableId
/// - gzip controlled by ApiOptions.UseGzipPostRequests
/// - response contract:
///   { succeeded, statusCode, message, errors[], data{ totalItems, insertedCount, skippedCount, skippedItems[] } }
/// - MUST parse JSON even if HTTP status is 500
/// </summary>
public sealed class DomainMappingClient_Wbs2_7_Tests
{
    [Fact]
    public async Task InsertMissMappingDomain_success_when_succeeded_true_and_gzip_enabled()
    {
        var responseJson =
            """
            {
              "succeeded": true,
              "statusCode": 0,
              "message": "ok",
              "errors": [],
              "data": {
                "totalItems": 1,
                "insertedCount": 1,
                "skippedCount": 0,
                "skippedItems": []
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

        var sut = new DomainMappingClient(factory, opts);

        var req = new InsertMissMappingDomainRequest(
            ProviderDhsCode: "DHS123",
            MismappedItems: new List<MismappedItem>
            {
                new("CODE_A", "NAME_A", 7)
            });

        var result = await sut.InsertMissMappingDomainAsync(req, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.StatusCode);
        Assert.Equal("ok", result.Message);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Data.TotalItems);
        Assert.Equal(1, result.Data.InsertedCount);
        Assert.Equal(0, result.Data.SkippedCount);
        Assert.Empty(result.Data.SkippedItems);

        Assert.Single(handler.Captured);
        var captured = handler.Captured[0];

        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.EndsWith("/api/DomainMapping/InsertMissMappingDomain", captured.Uri, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("gzip", captured.ContentEncoding, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(captured.DecodedBody);
        var root = doc.RootElement;

        Assert.Equal("DHS123", root.GetProperty("providerDhsCode").GetString());

        var arr = root.GetProperty("mismappedItems");
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(1, arr.GetArrayLength());

        var first = arr[0];
        Assert.Equal("CODE_A", first.GetProperty("providerCodeValue").GetString());
        Assert.Equal("NAME_A", first.GetProperty("providerNameValue").GetString());
        Assert.Equal(7, first.GetProperty("domainTableId").GetInt32());
    }

    [Fact]
    public async Task InsertMissMappingDomain_returns_backend_errors_even_if_http_500()
    {
        var responseJson =
            """
            {
              "succeeded": false,
              "statusCode": 500,
              "message": "Server error",
              "errors": [ "E1", "E2" ],
              "data": {
                "totalItems": 2,
                "insertedCount": 0,
                "skippedCount": 2,
                "skippedItems": [ "CODE_A", "CODE_B" ]
              }
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

        var sut = new DomainMappingClient(factory, opts);

        var req = new InsertMissMappingDomainRequest(
            ProviderDhsCode: "DHS123",
            MismappedItems: new List<MismappedItem>
            {
                new("CODE_A", "NAME_A", 7),
                new("CODE_B", "NAME_B", 7)
            });

        var result = await sut.InsertMissMappingDomainAsync(req, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.StatusCode);
        Assert.Equal("Server error", result.Message);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("E1", result.Errors);
        Assert.Contains("E2", result.Errors);

        Assert.Equal(2, result.Data.TotalItems);
        Assert.Equal(0, result.Data.InsertedCount);
        Assert.Equal(2, result.Data.SkippedCount);
        Assert.Equal(2, result.Data.SkippedItems.Count);
    }
}
