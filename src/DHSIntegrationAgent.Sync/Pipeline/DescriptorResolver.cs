using System.Collections.Concurrent;
using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.Sync.Pipeline;

// Singleton: caches parsed VendorDescriptor objects for the session lifetime.
public sealed class DescriptorResolver
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly ConcurrentDictionary<string, VendorDescriptor> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DescriptorResolver(ISqliteUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<VendorDescriptor> ResolveAsync(string providerDhsCode, CancellationToken ct)
    {
        if (_cache.TryGetValue(providerDhsCode, out var cached))
            return cached;

        await using var uow = await _uowFactory.CreateAsync(ct);
        var descriptorJson = await uow.ProviderProfiles.GetVendorDescriptorAsync(providerDhsCode, ct);

        if (string.IsNullOrWhiteSpace(descriptorJson))
            throw new DescriptorNotFoundException(providerDhsCode);

        var descriptor = VendorDescriptor.Deserialize(descriptorJson);
        _cache[providerDhsCode] = descriptor;
        return descriptor;
    }

    // Evicts cached entry — call after a descriptor update.
    public void Invalidate(string providerDhsCode) => _cache.TryRemove(providerDhsCode, out _);
}
