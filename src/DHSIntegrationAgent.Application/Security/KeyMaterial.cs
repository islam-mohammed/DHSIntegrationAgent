namespace DHSIntegrationAgent.Application.Security;

public sealed record KeyMaterial(
    string KeyId,
    DateTimeOffset CreatedUtc,
    byte[] KeyBytes // 32 bytes for AES-256
);
