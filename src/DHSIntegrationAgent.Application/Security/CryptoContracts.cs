namespace DHSIntegrationAgent.Application.Security;

public sealed record KeyMaterial(
    string KeyId,
    DateTimeOffset CreatedUtc,
    byte[] KeyBytes // 32 bytes for AES-256
);

public interface IKeyRing
{
    /// <summary>Returns the active key used for new encryption.</summary>
    Task<KeyMaterial> GetActiveKeyAsync(CancellationToken cancellationToken);

    /// <summary>Try load a key by KeyId (active or previous).</summary>
    Task<KeyMaterial?> TryGetKeyAsync(string keyId, CancellationToken cancellationToken);
}

public interface IColumnEncryptor
{
    /// <summary>
    /// Encrypts plaintext into a self-describing blob (contains version + keyId + nonce + tag + ciphertext).
    /// </summary>
    Task<byte[]> EncryptAsync(byte[] plaintext, CancellationToken cancellationToken);

    /// <summary>Decrypts a self-describing blob back into plaintext.</summary>
    Task<byte[]> DecryptAsync(byte[] encryptedBlob, CancellationToken cancellationToken);
}
