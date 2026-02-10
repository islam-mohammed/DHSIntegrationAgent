using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DHSIntegrationAgent.Tests.Integration.TestHelpers;

public sealed class NamedClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;
    public NamedClientFactory(HttpClient client) => _client = client;
    public HttpClient CreateClient(string name) => _client;
}

public sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    public sealed record CapturedRequest(HttpMethod Method, string Uri, string? ContentEncoding, string DecodedBody);

    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<CapturedRequest> Captured { get; } = new();

    public CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bodyBytes = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var encoding = request.Content?.Headers.ContentEncoding is null
            ? null
            : string.Join(",", request.Content.Headers.ContentEncoding);

        var decoded = DecodeBody(bodyBytes, encoding);

        Captured.Add(new CapturedRequest(
            request.Method,
            request.RequestUri!.ToString(),
            encoding,
            decoded));

        return _responder(request);
    }

    private static string DecodeBody(byte[] bytes, string? contentEncoding)
    {
        if (!string.IsNullOrWhiteSpace(contentEncoding) &&
            contentEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream(bytes);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var sr = new StreamReader(gz, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
