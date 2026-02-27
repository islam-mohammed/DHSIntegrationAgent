using DHSIntegrationAgent.Contracts.Persistence;

namespace DHSIntegrationAgent.Application.Abstractions;

public interface IAttachmentService
{
    Task<(string Url, long SizeBytes)> UploadAsync(AttachmentRow attachment, CancellationToken ct);
}
