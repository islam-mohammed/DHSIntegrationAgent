using System.Text.Json.Serialization;

namespace DHSIntegrationAgent.Application.Abstractions;

public interface IAttachmentClient
{
    Task<UpdateAttachmentsResult> UploadAttachmentAsync(UploadAttachmentRequest request, CancellationToken ct);
}

public sealed record UpdateAttachmentsResult(bool Succeeded, string? ErrorMessage, int? HttpStatusCode);

public sealed record UploadAttachmentRequest(
    [property: JsonPropertyName("proIdClaim")] int ProIdClaim,
    [property: JsonPropertyName("attachments")] IEnumerable<AttachmentDto> Attachments);

public sealed record AttachmentDto(
    [property: JsonPropertyName("attachmentType")] string AttachmentType,
    [property: JsonPropertyName("fileSizeInByte")] long FileSizeInByte,
    [property: JsonPropertyName("onlineURL")] string OnlineURL,
    [property: JsonPropertyName("remarks")] string? Remarks,
    [property: JsonPropertyName("location")] string? Location);
