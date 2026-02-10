using DHSIntegrationAgent.Application.Observability;
using DHSIntegrationAgent.Application.Persistence;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Observability;

public sealed class SqliteApiCallRecorder : IApiCallRecorder
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly ILogger<SqliteApiCallRecorder> _logger;

    public SqliteApiCallRecorder(ISqliteUnitOfWorkFactory uowFactory, ILogger<SqliteApiCallRecorder> logger)
    {
        _uowFactory = uowFactory;
        _logger = logger;
    }

    public async Task RecordAsync(ApiCallRecord record, CancellationToken ct)
    {
        try
        {
            // We use CancellationToken.None here because we want to ensure the API call is logged
            // even if the original request's cancellation token has been triggered.
            // Logging is a side-effect that should be completed for audit purposes.
            await using var uow = await _uowFactory.CreateAsync(CancellationToken.None);

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
                cancellationToken: CancellationToken.None
            );

            await uow.CommitAsync(CancellationToken.None);

            _logger.LogDebug("Successfully persisted API call log for {EndpointName} (CorrelationId={CorrelationId})",
                record.EndpointName, record.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist API call log to SQLite for {EndpointName} (CorrelationId={CorrelationId})",
                record.EndpointName, record.CorrelationId);
        }
    }
}
