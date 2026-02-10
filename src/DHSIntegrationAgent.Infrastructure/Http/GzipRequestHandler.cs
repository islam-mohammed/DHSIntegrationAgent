using System.IO.Compression;
using System.Net.Http.Headers;
using DHSIntegrationAgent.Application.Configuration;
using DHSIntegrationAgent.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DHSIntegrationAgent.Infrastructure.Http;

/// <summary>
/// Auto-compresses outbound JSON POST requests using gzip.
/// KB requires gzip for POST JSON.
/// We allow disabling ONLY for Development/troubleshooting.
/// </summary>
public sealed class GzipRequestHandler : DelegatingHandler
{
    private readonly IOptions<ApiOptions> _apiOptions;
    private readonly IHostEnvironment _env;
    private readonly ILogger<GzipRequestHandler> _logger;

    public GzipRequestHandler(
        IOptions<ApiOptions> apiOptions,
        IHostEnvironment env,
        ILogger<GzipRequestHandler> logger)
    {
        _apiOptions = apiOptions;
        _env = env;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // Decide if gzip is enabled (with compliance guard).
        var gzipEnabled = _apiOptions.Value.UseGzipPostRequests;

        // KB compliance: do not allow disabling gzip outside Development.
        if (!gzipEnabled && !_env.IsDevelopment())
        {
            gzipEnabled = true;
            _logger.LogWarning(
                "Api:UseGzipPostRequests=false ignored because environment is '{Env}'. Gzip is required by spec for POST JSON.",
                _env.EnvironmentName);
        }

        if (gzipEnabled)
        {
            // Only compress POST requests with JSON content.
            if (request.Method == HttpMethod.Post && request.Content is not null)
            {
                if (!IsAlreadyGzipped(request.Content) && IsJson(request.Content.Headers.ContentType))
                {
                    var original = request.Content;

                    // Read original payload bytes (buffers content).
                    var uncompressedBytes = await original.ReadAsByteArrayAsync(ct);

                    // Gzip compress
                    byte[] compressedBytes;
                    using (var ms = new MemoryStream())
                    {
                        using (var gzip = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
                        {
                            await gzip.WriteAsync(uncompressedBytes, 0, uncompressedBytes.Length, ct);
                        }
                        compressedBytes = ms.ToArray();
                    }

                    // Replace content with gzipped bytes
                    var gzContent = new ByteArrayContent(compressedBytes);

                    // Preserve Content-Type (application/json; charset=utf-8)
                    gzContent.Headers.ContentType = original.Headers.ContentType ?? new MediaTypeHeaderValue("application/json")
                    {
                        CharSet = "utf-8"
                    };

                    // Mark it as gzipped
                    gzContent.Headers.ContentEncoding.Add("gzip");

                    // Preserve other content headers (excluding ones we explicitly set)
                    foreach (var header in original.Headers)
                    {
                        if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(header.Key, "Content-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;

                        gzContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    request.Content = gzContent;
                }
            }
        }

        return await base.SendAsync(request, ct);
    }

    private static bool IsJson(MediaTypeHeaderValue? contentType)
        => contentType?.MediaType is not null &&
           contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);

    private static bool IsAlreadyGzipped(HttpContent content)
        => content.Headers.ContentEncoding.Any(e => e.Equals("gzip", StringComparison.OrdinalIgnoreCase));
}
