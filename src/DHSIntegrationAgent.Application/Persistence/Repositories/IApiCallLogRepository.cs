using DHSIntegrationAgent.Contracts.Persistence;

﻿namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public interface IApiCallLogRepository
{
    Task InsertAsync(
        string endpointName,
        string? correlationId,
        DateTimeOffset requestUtc,
        DateTimeOffset? responseUtc,
        int? durationMs,
        int? httpStatusCode,
        bool succeeded,
        string? errorMessage,
        long? requestBytes,
        long? responseBytes,
        bool wasGzipRequest,
        string? providerDhsCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiCallLogItem>> GetRecentApiCallsAsync(int limit, CancellationToken cancellationToken);
}
