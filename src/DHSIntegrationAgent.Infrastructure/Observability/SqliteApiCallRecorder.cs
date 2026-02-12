using System.Threading.Channels;
using DHSIntegrationAgent.Application.Observability;
using DHSIntegrationAgent.Contracts.Observability;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Observability;

/// <summary>
/// Non-blocking API call recorder that enqueues records for a single background writer.
/// Why:
/// - Avoids "Task.Run per request" which creates many concurrent SQLite writers.
/// - Reduces SQLITE_BUSY / "database is locked" issues under Stream A and other write-heavy workloads.
/// </summary>
public sealed class SqliteApiCallRecorder : IApiCallRecorder
{
    // Bounded channel protects memory under bursts. When full we drop (best-effort telemetry).
    private readonly Channel<ApiCallRecord> _channel;
    private readonly ILogger<SqliteApiCallRecorder> _logger;

    public SqliteApiCallRecorder(ILogger<SqliteApiCallRecorder> logger)
    {
        _logger = logger;

        _channel = Channel.CreateBounded<ApiCallRecord>(new BoundedChannelOptions(capacity: 2048)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    /// <summary>
    /// Exposed for the hosted writer to drain records.
    /// Internal so only this assembly can access it.
    /// </summary>
    internal ChannelReader<ApiCallRecord> Reader => _channel.Reader;

    public Task RecordAsync(ApiCallRecord record, CancellationToken ct)
    {
        // Do NOT use the caller token: we want best-effort persistence even if request is cancelled.
        // Non-blocking: TryWrite. If channel is temporarily full, we drop oldest (bounded channel mode).
        try
        {
            if (!_channel.Writer.TryWrite(record))
            {
                _logger.LogWarning(
                    "API call log queue is full; dropping record for {EndpointName} (corr={CorrelationId}).",
                    record.EndpointName, record.CorrelationId);
            }
        }
        catch (Exception ex)
        {
            // Never let observability crash the app / pipeline.
            _logger.LogWarning(ex, "Failed to enqueue API call log record for {EndpointName}.", record.EndpointName);
        }

        return Task.CompletedTask;
    }

    internal void Complete() => _channel.Writer.TryComplete();
}
