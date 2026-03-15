namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// Service for deleting a batch safely, including server and local deletion.
/// </summary>
public interface IDeleteBatchService
{
    /// <summary>
    /// Deletes a batch from the backend API (if it has a BcrId) and from the local database.
    /// </summary>
    Task<DeleteBatchProcessResult> DeleteBatchAsync(long? localBatchId, string? bcrId, CancellationToken ct);
}

public sealed record DeleteBatchProcessResult(
    bool Succeeded,
    string? ErrorMessage
);
