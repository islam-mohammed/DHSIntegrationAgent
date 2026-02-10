namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record ClaimPayloadRow(
    ClaimKey Key,
    byte[] PayloadJsonPlaintext, // repo encrypts before storing
    string PayloadSha256,
    int PayloadVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
