using System.Collections.ObjectModel;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Providers;

namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// Service for tracking active batch creation and fetching tasks.
/// </summary>
public interface IBatchTracker
{
    /// <summary>
    /// Starts tracking a batch creation process.
    /// </summary>
    void TrackBatchCreation(BatchRow batch, FinancialSummary? financialSummary);

    /// <summary>
    /// Gets the collection of active batch creation progress items.
    /// Note: In this project, we're returning the ViewModel collection directly for simplicity.
    /// </summary>
    object ActiveBatches { get; }
}
