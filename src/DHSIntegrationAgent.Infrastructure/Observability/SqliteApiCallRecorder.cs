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
            await using var uow = await _uowFactory.CreateAsync(ct);

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
                cancellationToken: ct
            );

            await uow.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist API call log to SQLite for {EndpointName} (CorrelationId={CorrelationId})",
                record.EndpointName, record.CorrelationId);
        }
    }
}
