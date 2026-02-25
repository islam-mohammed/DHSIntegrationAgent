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
    /// Starts tracking attachment uploads for multiple batches.
    /// </summary>
    void TrackAttachmentUpload(IEnumerable<BatchRow> batches);

    /// <summary>
    /// Starts tracking a batch retry process.
    /// </summary>
    void TrackBatchRetry(BatchRow batch, Action<RetryBatchResult> onCompletion);

    /// <summary>
    /// Gets the collection of active batch creation progress items.
    /// Note: In this project, we're returning the ViewModel collection directly for simplicity.
    /// </summary>
    object ActiveBatches { get; }

    /// <summary>
    /// Restores any active batches from the persistent store into the tracking collection.
    /// </summary>
    Task RestoreActiveBatchesAsync();
}
