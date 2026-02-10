using System.Diagnostics;
using DHSIntegrationAgent.Application.Observability;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Http;

public sealed class ApiLoggingHandler : DelegatingHandler
{
    public const string CorrelationHeaderName = "X-Correlation-Id";

    private readonly ILogger<ApiLoggingHandler> _logger;
    private readonly IApiCallRecorder _recorder;

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

            int? statusCode = response is null ? null : (int)response.StatusCode;
            long? respBytes = response?.Content?.Headers.ContentLength;
            var url = request.RequestUri?.ToString() ?? "";

            var record = new ApiCallRecord(
                CorrelationId: correlationId,
                HttpMethod: request.Method.Method,
                Url: url,
                StartedUtc: startedUtc,
                ElapsedMs: sw.ElapsedMilliseconds,
                StatusCode: statusCode,
                RequestBytes: reqBytes,
                ResponseBytes: respBytes,
                ErrorType: error?.GetType().Name,
                ErrorMessage: error is null ? null : SanitizeError(error.Message)
            );

            await _recorder.RecordAsync(record, ct);

            _logger.LogInformation(
                "API {Method} {Url} -> {Status} in {Elapsed}ms (corr={CorrelationId})",
                record.HttpMethod, record.Url, record.StatusCode, record.ElapsedMs, record.CorrelationId
            );
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
