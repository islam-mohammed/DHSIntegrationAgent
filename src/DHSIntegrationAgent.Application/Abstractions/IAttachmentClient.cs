namespace DHSIntegrationAgent.Application.Abstractions;

public interface IAttachmentClient
{
    Task<UpdateAttachmentsResult> UpdateAttachmentsAsync(string providerDhsCode, int proIdClaim, string attachmentsJson, CancellationToken ct);
}

public sealed record UpdateAttachmentsResult(bool Succeeded, string? ErrorMessage, int? HttpStatusCode);
