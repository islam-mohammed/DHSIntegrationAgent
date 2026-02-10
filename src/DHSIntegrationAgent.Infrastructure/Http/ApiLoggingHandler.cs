using System.Diagnostics;
using DHSIntegrationAgent.Application.Observability;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Http;

public sealed class ApiLoggingHandler : DelegatingHandler
{
    public const string CorrelationHeaderName = "X-Correlation-Id";

    private readonly ILogger<ApiLoggingHandler> _logger;
    private readonly IApiCallRecorder _recorder;

    private static readonly Dictionary<string, string> PathToAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        { "api/Authentication/login", "Authentication_Login" },
        { "api/Batch/CreateBatchRequest", "Batch_Create" },
        { "api/Batch/GetBatchRequest", "Batch_Get" },
        { "api/Provider/GetProviderConfigration", "Provider_GetConfig" },
        { "api/DomainMapping/GetProviderDomainMapping", "DomainMapping_GetProviderMapping" },
        { "api/DomainMapping/InsertMissMappingDomain", "DomainMapping_InsertMissing" },
        { "api/DomainMapping/GetMissingDomainMappings", "DomainMapping_GetMissing" },
        { "api/DomainMapping/GetProviderDomainMappingsWithMissing", "DomainMapping_GetWithMissing" },
        { "api/Claims/SendClaim", "Claims_Send" },
        { "api/Claims/GetHISProIdClaimsByBcrId", "Claims_GetCompletedIds" }
    };

    public ApiLoggingHandler(ILogger<ApiLoggingHandler> logger, IApiCallRecorder recorder)
    {
        _logger = logger;
        _recorder = recorder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        var correlationId = EnsureCorrelationId(request);
        long? reqBytes = request.Content?.Headers.ContentLength;
        bool wasGzip = request.Content?.Headers.ContentEncoding.Any(e => e.Equals("gzip", StringComparison.OrdinalIgnoreCase)) ?? false;

        HttpResponseMessage? response = null;
        Exception? error = null;

        try
        {
            response = await base.SendAsync(request, ct);
            return response;
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            var responseUtc = DateTimeOffset.UtcNow;

            int? statusCode = response is null ? null : (int)response.StatusCode;
            long? respBytes = response?.Content?.Headers.ContentLength;
            var url = request.RequestUri?.ToString() ?? "";

            var endpointName = GetEndpointName(url);
            var providerDhsCode = ExtractProviderDhsCode(url);
            var succeeded = error == null && response is { IsSuccessStatusCode: true };

            var record = new ApiCallRecord(
                CorrelationId: correlationId,
                HttpMethod: request.Method.Method,
                Url: url,
                EndpointName: endpointName,
                ProviderDhsCode: providerDhsCode,
                StartedUtc: startedUtc,
                ResponseUtc: responseUtc,
                ElapsedMs: sw.ElapsedMilliseconds,
                StatusCode: statusCode,
                Succeeded: succeeded,
                ErrorType: error?.GetType().Name,
                ErrorMessage: error is null ? null : SanitizeError(error.Message),
                RequestBytes: reqBytes,
                ResponseBytes: respBytes,
                WasGzipRequest: wasGzip
            );

            // Use CancellationToken.None to ensure we log even if the request was canceled/aborted.
            // Persistence of logs is critical for auditing.
            await _recorder.RecordAsync(record, CancellationToken.None);

            _logger.LogInformation(
                "API {EndpointName} ({Method} {Url}) -> {Status} in {Elapsed}ms (corr={CorrelationId}, succeeded={Succeeded})",
                record.EndpointName, record.HttpMethod, record.Url, record.StatusCode, record.ElapsedMs, record.CorrelationId, record.Succeeded
            );
        }
    }

    private static string GetEndpointName(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Unknown";

        foreach (var kvp in PathToAlias)
        {
            if (url.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        // Fallback: return the path part if possible, otherwise full URL
        try
        {
            if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
            {
                return uri.IsAbsoluteUri ? uri.AbsolutePath : url;
            }
        }
        catch { }

        return url;
    }

    private static string? ExtractProviderDhsCode(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (url.Contains("api/Batch/GetBatchRequest/", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(url);
        if (url.Contains("api/Provider/GetProviderConfigration/", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(url);
        if (url.Contains("api/DomainMapping/GetProviderDomainMapping/", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(url);
        if (url.Contains("api/DomainMapping/GetMissingDomainMappings/", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(url);
        if (url.Contains("api/DomainMapping/GetProviderDomainMappingsWithMissing/", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(url);

        return null;
    }

    private static string? ExtractLastSegment(string url)
    {
        try
        {
            string path;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                path = uri.AbsolutePath;
            }
            else
            {
                // Handle relative URL by stripping query string
                path = url.Split('?')[0];
            }

            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.LastOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string EnsureCorrelationId(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues(CorrelationHeaderName, out var values))
        {
            var existing = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(existing))
                return existing!;
        }

        var corr = Guid.NewGuid().ToString("N");
        request.Headers.TryAddWithoutValidation(CorrelationHeaderName, corr);
        return corr;
    }

    private static string SanitizeError(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return "error";

        msg = msg.Replace("\r", " ").Replace("\n", " ");
        return msg.Length <= 300 ? msg : msg[..300];
    }
}
