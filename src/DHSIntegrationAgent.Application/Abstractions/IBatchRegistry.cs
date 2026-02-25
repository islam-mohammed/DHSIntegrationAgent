namespace DHSIntegrationAgent.Application.Abstractions;

/// <summary>
/// Registry to track active batch operations to prevent race conditions between manual and background processes.
/// </summary>
public interface IBatchRegistry
{
    /// <summary>
    /// Registers a batch as being actively processed.
    /// </summary>
    void Register(long batchId);

    /// <summary>
    /// Unregisters a batch from active processing.
    /// </summary>
    void Unregister(long batchId);

    /// <summary>
    /// Checks if a batch is currently registered as active.
    /// </summary>
    bool IsRegistered(long batchId);
}
