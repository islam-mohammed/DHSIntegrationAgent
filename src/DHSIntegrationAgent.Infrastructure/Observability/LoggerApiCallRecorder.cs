using DHSIntegrationAgent.Application.Observability;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Observability;

public sealed class LoggerApiCallRecorder : IApiCallRecorder
{
    private readonly ILogger<LoggerApiCallRecorder> _logger;

    public LoggerApiCallRecorder(ILogger<LoggerApiCallRecorder> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(ApiCallRecord record, CancellationToken ct)
    {
        _logger.LogDebug(
            "ApiCallRecord corr={CorrelationId} status={Status} elapsed={Elapsed}ms req={ReqBytes} resp={RespBytes}",
            record.CorrelationId, record.StatusCode, record.ElapsedMs, record.RequestBytes, record.ResponseBytes
        );

        return Task.CompletedTask;
    }
}
