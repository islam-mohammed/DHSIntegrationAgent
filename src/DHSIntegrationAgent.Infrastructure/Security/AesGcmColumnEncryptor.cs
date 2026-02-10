using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DHSIntegrationAgent.Application.Security;

namespace DHSIntegrationAgent.Infrastructure.Security;

internal sealed class AesGcmColumnEncryptor : IColumnEncryptor
{
    private const uint Magic = 0x44485301; // "DHS" + version marker (opaque)
    private const byte FormatVersion = 1;

    private readonly IKeyRing _keyRing;

    public AesGcmColumnEncryptor(IKeyRing keyRing)
    {
        _keyRing = keyRing;
    }

    public async Task<byte[]> EncryptAsync(byte[] plaintext, CancellationToken cancellationToken)
    {
        var key = await _keyRing.GetActiveKeyAsync(cancellationToken);

        var nonce = new byte[12]; // recommended nonce size for GCM
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16]; // 128-bit tag

        using (var aes = new AesGcm(key.KeyBytes, tagSizeInBytes: 16))
        {
            // We bind the keyId as AAD so metadata tampering is detected
            var aad = Encoding.UTF8.GetBytes(key.KeyId);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        }

        // Blob layout:
        // [Magic(4)][Ver(1)][KeyIdLen(2)][KeyId(UTF8)][Nonce(12)][Tag(16)][Ciphertext(N)]
        var keyIdBytes = Encoding.UTF8.GetBytes(key.KeyId);
        if (keyIdBytes.Length > ushort.MaxValue) throw new InvalidOperationException("KeyId too long.");

        var totalLen = 4 + 1 + 2 + keyIdBytes.Length + 12 + 16 + ciphertext.Length;
        var blob = new byte[totalLen];

        var offset = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset, 4), Magic); offset += 4;
        blob[offset++] = FormatVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(offset, 2), (ushort)keyIdBytes.Length); offset += 2;
        keyIdBytes.CopyTo(blob.AsSpan(offset)); offset += keyIdBytes.Length;
        nonce.CopyTo(blob.AsSpan(offset)); offset += 12;
        tag.CopyTo(blob.AsSpan(offset)); offset += 16;
        ciphertext.CopyTo(blob.AsSpan(offset));

        return blob;
    }

    public async Task<byte[]> DecryptAsync(byte[] encryptedBlob, CancellationToken cancellationToken)
    {
        if (encryptedBlob.Length < 4 + 1 + 2 + 12 + 16)
            throw new CryptographicException("Encrypted blob too short.");

        var offset = 0;
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(encryptedBlob.AsSpan(offset, 4)); offset += 4;
        if (magic != Magic) throw new CryptographicException("Invalid encrypted blob magic.");

        var ver = encryptedBlob[offset++];
        if (ver != FormatVersion) throw new CryptographicException($"Unsupported encrypted blob version: {ver}");

        var keyIdLen = BinaryPrimitives.ReadUInt16LittleEndian(encryptedBlob.AsSpan(offset, 2)); offset += 2;
        var keyId = Encoding.UTF8.GetString(encryptedBlob, offset, keyIdLen); offset += keyIdLen;

        var nonce = encryptedBlob.AsSpan(offset, 12).ToArray(); offset += 12;
        var tag = encryptedBlob.AsSpan(offset, 16).ToArray(); offset += 16;
        var ciphertext = encryptedBlob.AsSpan(offset).ToArray();

        var key = await _keyRing.TryGetKeyAsync(keyId, cancellationToken);
        if (key is null) throw new CryptographicException($"No key found for KeyId={keyId}");

        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key.KeyBytes, tagSizeInBytes: 16))
        {
            var aad = Encoding.UTF8.GetBytes(key.KeyId);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
        }

        return plaintext;
    }
}
