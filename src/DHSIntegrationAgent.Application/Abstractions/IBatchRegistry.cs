namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// Registry to track active batch operations to prevent race conditions between manual and background processes.
/// </summary>
public interface IBatchRegistry
{
    /// <summary>
    /// Attempts to register a batch as being actively processed.
    /// Returns true if successfully registered, false if it is already registered.
    /// </summary>
    bool TryRegister(long batchId);

    /// <summary>
    /// Unregisters a batch from active processing.
    /// </summary>
    void Unregister(long batchId);

    /// <summary>
    /// Checks if a batch is currently registered as active.
    /// </summary>
    bool IsRegistered(long batchId);

    /// <summary>
    /// Checks if there are any actively registered batches.
    /// </summary>
    bool HasRegisteredBatches { get; }
}
