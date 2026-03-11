namespace DHSIntegrationAgent.Application.Abstractions;

public sealed record DashboardMetrics(
    int StagedCount,
    int EnqueuedCount,
    int CompletedCount,
    int FailedCount,
    DateTimeOffset? LastFetchUtc,
    DateTimeOffset? LastSendUtc
);

public interface IDashboardService
{
    Task<DashboardMetrics> GetMetricsAsync(string providerDhsCode, string? payerCode, CancellationToken ct);
}
