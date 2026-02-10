namespace DHSIntegrationAgent.Application.Security;

public interface IKeyRing
{
    /// <summary>Returns the active key used for new encryption.</summary>
    Task<KeyMaterial> GetActiveKeyAsync(CancellationToken cancellationToken);

    /// <summary>Try load a key by KeyId (active or previous).</summary>
    Task<KeyMaterial?> TryGetKeyAsync(string keyId, CancellationToken cancellationToken);
}
