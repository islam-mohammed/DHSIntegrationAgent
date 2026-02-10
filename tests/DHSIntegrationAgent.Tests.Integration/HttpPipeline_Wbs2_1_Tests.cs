using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application;
using DHSIntegrationAgent.Infrastructure;
using DHSIntegrationAgent.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DHSIntegrationAgent.Tests.Integration;

public sealed class HttpPipeline_Wbs2_1_Tests
{
    [Fact]
    public async Task PostJson_is_gzipped_sets_correlation_and_decompresses_response()
    {
        // -----------------------------
        // 1) Start a local echo server
        // -----------------------------
        // Why: We need a real HTTP endpoint to verify actual headers + gzipped body bytes.
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}/";

        var capture = new TaskCompletionSource<CapturedRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HttpPipeline_Wbs2_1_Tests).Assembly.FullName!,
            EnvironmentName = "Development"
        });

        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls(baseUrl);

        var app = builder.Build();

        app.MapPost("/echo", async context =>
        {
            // Read raw request bytes
            byte[] requestBytes;
            await using (var ms = new MemoryStream())
            {
                await context.Request.Body.CopyToAsync(ms);
                requestBytes = ms.ToArray();
            }

            // Capture headers we care about
            var contentEncoding = context.Request.Headers["Content-Encoding"].ToString();
            var correlationId = context.Request.Headers[ApiLoggingHandler.CorrelationHeaderName].ToString();

            // Decompress request if gzipped so we can validate the original JSON
            string decodedBody;
            if (contentEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                decodedBody = DecompressGzipToString(requestBytes);
            }
            else
            {
                decodedBody = Encoding.UTF8.GetString(requestBytes);
            }

            capture.TrySetResult(new CapturedRequest(
                ContentEncoding: contentEncoding,
                CorrelationId: correlationId,
                DecodedBody: decodedBody));

            // Return a gzipped JSON response so we can verify AutomaticDecompression
            var responseJson = """{"ok":true,"message":"gzipped-response"}""";
            var responsePlain = Encoding.UTF8.GetBytes(responseJson);
            var responseGz = CompressGzip(responsePlain);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Content-Encoding"] = "gzip";

            await context.Response.Body.WriteAsync(responseGz);
        });

        await app.StartAsync();

        try
        {
            // ----------------------------------------
            // 2) Build DI + resolve the named HttpClient
            // ----------------------------------------
            // Why: This exercises your real DI registration for WBS 2.1.
            var services = new ServiceCollection();

            // Logging is needed because ApiLoggingHandler depends on ILogger<>
            services.AddLogging(lb => lb.AddDebug().AddConsole().SetMinimumLevel(LogLevel.Information));

            // Minimal configuration to satisfy options + HttpClient registration
            var cfg = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    // WBS 2.1 required
                    new KeyValuePair<string, string?>("Api:BaseUrl", baseUrl),
                    new KeyValuePair<string, string?>("Api:TimeoutSeconds", "30"),

                    // WBS 0.3 validation / defaults
                    new KeyValuePair<string, string?>("App:EnvironmentName", "Development"),
                    new KeyValuePair<string, string?>("App:DatabasePath", Path.Combine(Path.GetTempPath(), $"wbs21_{Guid.NewGuid():N}.db")),
                    new KeyValuePair<string, string?>("AzureBlob:SasUrl", ""), // must be empty if you enforced 0.3 policy
                })
                .Build();

            services.AddDhsApplication(cfg);
            services.AddDhsInfrastructure(cfg);

            await using var sp = services.BuildServiceProvider();

            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var client = httpFactory.CreateClient("BackendApi");

            // ----------------------------------------
            // 3) Send a POST JSON with normal StringContent
            // ----------------------------------------
            // Why: If your WBS 2.1 gzip middleware (handler) is correct, it will auto-gzip this request.
            const string originalJson = """{"hello":"world"}""";
            using var content = new StringContent(originalJson, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("echo", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // ----------------------------------------
            // 4) Verify server captured gzip + correlation + correct decoded JSON
            // ----------------------------------------
            var captured = await capture.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // gzip verification:
            // - If this fails, it means WBS 2.1 is not complete (gzip not automatic),
            //   OR you didn’t register GzipRequestHandler in the pipeline.
            Assert.Contains("gzip", captured.ContentEncoding, StringComparison.OrdinalIgnoreCase);

            // correlation verification:
            Assert.False(string.IsNullOrWhiteSpace(captured.CorrelationId));

            // content verification (after decompress):
            Assert.Equal(originalJson, captured.DecodedBody);

            // ----------------------------------------
            // 5) Verify response decompression worked
            // ----------------------------------------
            // Server returned gzipped JSON with Content-Encoding:gzip.
            // If your SocketsHttpHandler.AutomaticDecompression is wired, ReadAsStringAsync() returns plaintext JSON.
            Assert.Contains(@"""ok"":true", responseBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("gzipped-response", responseBody, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    // -----------------------------
    // Helpers
    // -----------------------------

    private static int GetFreeTcpPort()
    {
        // Why: avoid hardcoding ports, prevents test collisions in parallel runs.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static byte[] CompressGzip(byte[] plaintext)
    {
        // Why: server sends a gzipped response so client decompression can be verified.
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(plaintext, 0, plaintext.Length);
        }
        return ms.ToArray();
    }

    private static string DecompressGzipToString(byte[] gzippedBytes)
    {
        // Why: server needs to decode request body to confirm the client truly gzipped it.
        using var input = new MemoryStream(gzippedBytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private sealed record CapturedRequest(string ContentEncoding, string CorrelationId, string DecodedBody);
}
