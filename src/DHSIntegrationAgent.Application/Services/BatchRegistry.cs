using System.Collections.Concurrent;
using DHSIntegrationAgent.Application.Abstractions;

namespace DHSIntegrationAgent.Application.Services;

/// <summary>
/// In-memory registry to track active batches.
/// </summary>
public sealed class BatchRegistry : IBatchRegistry
{
    private readonly ConcurrentDictionary<long, byte> _activeBatches = new();

    public void Register(long batchId)
    {
        _activeBatches.TryAdd(batchId, 0);
    }

    public void Unregister(long batchId)
    {
        _activeBatches.TryRemove(batchId, out _);
    }

    public bool IsRegistered(long batchId)
    {
        return _activeBatches.ContainsKey(batchId);
    }
}
