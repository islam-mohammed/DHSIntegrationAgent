using DHSIntegrationAgent.Contracts.Observability;

namespace DHSIntegrationAgent.Application.Observability;

public interface IApiCallRecorder
{
    Task RecordAsync(ApiCallRecord record, CancellationToken ct);
}
