using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Tests.Integration
{
    internal class NoopApiCallLogRepository : IApiCallLogRepository
    {
        public Task InsertAsync(string endpointName, string? correlationId, DateTimeOffset requestUtc, DateTimeOffset? responseUtc, int? durationMs, int? httpStatusCode, bool succeeded, string? errorMessage, long? requestBytes, long? responseBytes, bool wasGzipRequest, string? providerDhsCode, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ApiCallLogItem>> GetRecentApiCallsAsync(int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ApiCallLogItem>>(Array.Empty<ApiCallLogItem>());
        }
    }
}