using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DHSIntegrationAgent.Infrastructure.Http;

public sealed class GzipJsonHttpContent : HttpContent
{
    private readonly byte[] _gzippedBytes;

    private GzipJsonHttpContent(byte[] gzippedBytes)
    {
        _gzippedBytes = gzippedBytes;

        Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        Headers.ContentEncoding.Add("gzip");
    }

    public static GzipJsonHttpContent Create<T>(T value, JsonSerializerOptions? options = null)
    {
        options ??= DefaultJson.Options;

        var json = JsonSerializer.Serialize(value, options);
        var utf8 = Encoding.UTF8.GetBytes(json);

        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(utf8, 0, utf8.Length);
        }

        return new GzipJsonHttpContent(ms.ToArray());
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => stream.WriteAsync(_gzippedBytes, 0, _gzippedBytes.Length);

    protected override bool TryComputeLength(out long length)
    {
        length = _gzippedBytes.Length;
        return true;
    }

    private static class DefaultJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }
}
