using System.Diagnostics;
using DHSIntegrationAgent.Contracts.Observability;
using DHSIntegrationAgent.Application.Observability;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Http;

public sealed class ApiLoggingHandler : DelegatingHandler
{
    public const string CorrelationHeaderName = "X-Correlation-Id";

    private readonly ILogger<ApiLoggingHandler> _logger;
    private readonly IApiCallRecorder _recorder;

    private static readonly IReadOnlyList<KeyValuePair<string, string>> Aliases = new[]
    {
        new KeyValuePair<string, string>("api/Authentication/login", "Authentication_Login"),
        new KeyValuePair<string, string>("api/Batch/CreateBatchRequest", "Batch_Create"),
        new KeyValuePair<string, string>("api/Batch/GetBatchRequest", "Batch_Get"),
        new KeyValuePair<string, string>("api/Provider/GetProviderConfigration", "Provider_GetConfig"),
        new KeyValuePair<string, string>("api/DomainMapping/GetProviderDomainMappingsWithMissing", "DomainMapping_GetWithMissing"),
        new KeyValuePair<string, string>("api/DomainMapping/GetProviderDomainMapping", "DomainMapping_GetProviderMapping"),
        new KeyValuePair<string, string>("api/DomainMapping/InsertMissMappingDomain", "DomainMapping_InsertMissing"),
        new KeyValuePair<string, string>("api/DomainMapping/GetMissingDomainMappings", "DomainMapping_GetMissing"),
        new KeyValuePair<string, string>("api/Claims/SendClaim", "Claims_Send"),
        new KeyValuePair<string, string>("api/Claims/GetHISProIdClaimsByBcrId", "Claims_GetCompletedIds")
    };

    private readonly Application.Providers.IProviderContext _providerContext;

    public ApiLoggingHandler(ILogger<ApiLoggingHandler> logger, IApiCallRecorder recorder, Application.Providers.IProviderContext providerContext)
    {
        _logger = logger;
        _recorder = recorder;
        _providerContext = providerContext;
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
        string? activeProviderDhsCode = null;

        try
        {
            // Attempt to get active provider context
            try
            {
                activeProviderDhsCode = await _providerContext.GetProviderDhsCodeAsync(ct);
            }
            catch
            {
                // Ignore context retrieval errors (e.g. no active DB/profile)
            }

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
            var providerDhsCode = ExtractProviderDhsCode(url) ?? activeProviderDhsCode;
            var succeeded = error == null && response is { IsSuccessStatusCode: true };

            string? errorMessage = error is null ? null : SanitizeError(error.Message);

            if (error == null && response?.Content != null)
            {
                try
                {
                    var content = await response.Content.ReadAsStringAsync(ct);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var parsedErrors = string.Empty;
                        bool hasErrorsArray = false;

                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(content);
                            if (doc.RootElement.TryGetProperty("errors", out var errorsProp) && errorsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                hasErrorsArray = true;
                                var errorList = new List<string>();
                                foreach (var err in errorsProp.EnumerateArray())
                                {
                                    if (err.ValueKind == System.Text.Json.JsonValueKind.String)
                                    {
                                        var errStr = err.GetString();
                                        if (!string.IsNullOrWhiteSpace(errStr))
                                            errorList.Add(errStr);
                                    }
                                }
                                if (errorList.Count > 0)
                                {
                                    parsedErrors = string.Join(", ", errorList);
                                }
                            }
                        }
                        catch
                        {
                            // JSON parse error, keep plain content if status code is not 200
                        }

                        if (!succeeded)
                        {
                            errorMessage = SanitizeError(string.IsNullOrWhiteSpace(parsedErrors) ? content : parsedErrors);
                        }
                        else if (hasErrorsArray && !string.IsNullOrWhiteSpace(parsedErrors))
                        {
                            // If the HTTP status code was success, but there are explicit errors in the JSON body,
                            // treat it as a failure for the API call log.
                            succeeded = false;
                            errorMessage = SanitizeError(parsedErrors);
                        }
                    }
                    }
                catch
                {
                    // Ignore content read errors during logging
                }
            }

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
                ErrorMessage: errorMessage,
                RequestBytes: reqBytes,
                ResponseBytes: respBytes,
                WasGzipRequest: wasGzip
            );

            await _recorder.RecordAsync(record, ct);

            if (!succeeded)
            {
                _logger.LogError(
                    "API {EndpointName} ({Method} {Url}) -> {Status} failed in {Elapsed}ms (corr={CorrelationId}). Error: {ErrorMessage}",
                    record.EndpointName, record.HttpMethod, record.Url, record.StatusCode, record.ElapsedMs, record.CorrelationId, record.ErrorMessage
                );
            }
            else
            {
                _logger.LogInformation(
                    "API {EndpointName} ({Method} {Url}) -> {Status} in {Elapsed}ms (corr={CorrelationId}, succeeded={Succeeded})",
                    record.EndpointName, record.HttpMethod, record.Url, record.StatusCode, record.ElapsedMs, record.CorrelationId, record.Succeeded
                );
            }
        }
    }

    private static string GetEndpointName(string url)
    {
        var path = GetCleanPath(url);

        foreach (var kvp in Aliases)
        {
            if (path.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return url;
    }

    private static string? ExtractProviderDhsCode(string url)
    {
        var path = GetCleanPath(url);

        if (path.Contains("api/Batch/GetBatchRequest", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(path);
        if (path.Contains("api/Provider/GetProviderConfigration", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(path);
        if (path.Contains("api/DomainMapping/GetProviderDomainMapping", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(path);
        if (path.Contains("api/DomainMapping/GetMissingDomainMappings", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(path);
        if (path.Contains("api/DomainMapping/GetProviderDomainMappingsWithMissing", StringComparison.OrdinalIgnoreCase)) return ExtractLastSegment(path);

        return null;
    }

    private static string GetCleanPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        try
        {
            // Try to parse as absolute URI, then as relative
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                Uri.TryCreate("http://localhost/" + url.TrimStart('/'), UriKind.Absolute, out uri);
            }

            if (uri != null)
            {
                var path = uri.AbsolutePath;
                // Strip trailing slash
                if (path.Length > 1 && path.EndsWith('/')) path = path[..^1];
                return path.TrimStart('/');
            }
        }
        catch
        {
            // fallback
        }

        // Manual fallback: strip query string
        int qIdx = url.IndexOf('?');
        var result = qIdx >= 0 ? url[..qIdx] : url;
        return result.Trim('/');
    }

    private static string? ExtractLastSegment(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.LastOrDefault();
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

        return msg.Replace("\r", " ").Replace("\n", " ");
    }
}
