using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;

namespace DHSIntegrationAgent.Application.Dashboard;

public sealed class DashboardService : IDashboardService
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;

    public DashboardService(ISqliteUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<DashboardMetrics> GetMetricsAsync(string providerDhsCode, string? payerCode, CancellationToken ct)
    {
        await using var uow = await _uowFactory.CreateAsync(ct);

        string? companyCode = string.IsNullOrWhiteSpace(payerCode) ? null : payerCode;

        var (staged, enqueued, completed, failed) = await uow.Claims.GetDashboardCountsAsync(providerDhsCode, companyCode, ct);

        DateTimeOffset? lastFetchUtc = await uow.ApiCallLogs.GetLastSuccessfulCallUtcAsync("Batch_Create", ct);
        DateTimeOffset? lastSendUtc = await uow.ApiCallLogs.GetLastSuccessfulCallUtcAsync("Claims_Send", ct);

        return new DashboardMetrics(
            StagedCount: staged,
            EnqueuedCount: enqueued,
            CompletedCount: completed,
            FailedCount: failed,
            LastFetchUtc: lastFetchUtc,
            LastSendUtc: lastSendUtc
        );
    }
}
