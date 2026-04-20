namespace DHSIntegrationAgent.Sync.Pipeline;

public sealed class DescriptorNotFoundException : Exception
{
    public DescriptorNotFoundException(string providerDhsCode)
        : base($"No active VendorDescriptor found for provider '{providerDhsCode}'. Re-login to fetch and cache the descriptor.") { }
}
