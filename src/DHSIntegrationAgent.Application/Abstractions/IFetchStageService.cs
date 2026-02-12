using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Workers;

namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// Service for processing Stream A (Fetch & Stage) tasks.
/// </summary>
public interface IFetchStageService
{
    /// <summary>
    /// Processes a specific batch: ensures BcrId, fetches claims from HIS, stages them locally,
    /// and scans for missing domain mappings.
    /// </summary>
    Task ProcessBatchAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct);

    /// <summary>
    /// Posts missing domain mappings to the backend for a specific provider.
    /// </summary>
    Task PostMissingMappingsAsync(string providerDhsCode, IProgress<WorkerProgressReport> progress, CancellationToken ct);
}
