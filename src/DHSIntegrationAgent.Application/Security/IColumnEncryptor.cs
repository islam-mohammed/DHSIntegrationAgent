namespace DHSIntegrationAgent.Application.Security;

public interface IColumnEncryptor
{
    /// <summary>
    /// Encrypts plaintext into a self-describing blob (contains version + keyId + nonce + tag + ciphertext).
    /// </summary>
    Task<byte[]> EncryptAsync(byte[] plaintext, CancellationToken cancellationToken);

    /// <summary>Decrypts a self-describing blob back into plaintext.</summary>
    Task<byte[]> DecryptAsync(byte[] encryptedBlob, CancellationToken cancellationToken);
}
