namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record ClaimPayloadRow(
    ClaimKey Key,
    byte[] PayloadJsonPlaintext, // repo encrypts before storing
    string PayloadSha256,
    int PayloadVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public interface IClaimPayloadRepository
{
    Task UpsertAsync(
        ClaimPayloadRow row,
        CancellationToken cancellationToken);

    Task<ClaimPayloadRow?> GetAsync(ClaimKey key, CancellationToken cancellationToken);
}
