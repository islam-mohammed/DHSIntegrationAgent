using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Observability;

/// <summary>
/// Single background consumer that persists queued API call logs to SQLite.
/// Why:
/// - SQLite allows only one writer at a time; this prevents bursts of concurrent writers.
/// - Minimizes contention with Stream A and other state machine persistence.
/// </summary>
public sealed class SqliteApiCallLogWriterHostedService : BackgroundService
{
    private readonly SqliteApiCallRecorder _queue;
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly ILogger<SqliteApiCallLogWriterHostedService> _logger;

    // Small batches reduce lock hold time while still amortizing transaction overhead.
    private const int BatchSize = 50;

    public SqliteApiCallLogWriterHostedService(
        SqliteApiCallRecorder queue,
        ISqliteUnitOfWorkFactory uowFactory,
        ILogger<SqliteApiCallLogWriterHostedService> logger)
    {
        _queue = queue;
        _uowFactory = uowFactory;
        _logger = logger;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new messages; best-effort drain will occur until ExecuteAsync exits.
        _queue.Complete();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<ApiCallRecord>(BatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait until at least one record is available.
                if (!await _queue.Reader.WaitToReadAsync(stoppingToken))
                    break;

                buffer.Clear();

                while (buffer.Count < BatchSize && _queue.Reader.TryRead(out var record))
                    buffer.Add(record);

                if (buffer.Count == 0)
                    continue;

                await PersistBatchAsync(buffer, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApiCallLog writer crashed. API call logging to SQLite is now disabled until restart.");
        }
    }

    private async Task PersistBatchAsync(IReadOnlyList<ApiCallRecord> records, CancellationToken ct)
    {
        try
        {
            await using var uow = await _uowFactory.CreateAsync(ct);

            foreach (var record in records)
            {
                await uow.ApiCallLogs.InsertAsync(
                    endpointName: record.EndpointName,
                    correlationId: record.CorrelationId,
                    requestUtc: record.StartedUtc,
                    responseUtc: record.ResponseUtc,
                    durationMs: (int)record.ElapsedMs,
                    httpStatusCode: record.StatusCode,
                    succeeded: record.Succeeded,
                    errorMessage: record.ErrorMessage,
                    requestBytes: record.RequestBytes,
                    responseBytes: record.ResponseBytes,
                    wasGzipRequest: record.WasGzipRequest,
                    providerDhsCode: record.ProviderDhsCode,
                    cancellationToken: ct);
            }

            await uow.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // normal shutdown, best effort
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist {Count} API call logs to SQLite.", records.Count);
        }
    }
}
