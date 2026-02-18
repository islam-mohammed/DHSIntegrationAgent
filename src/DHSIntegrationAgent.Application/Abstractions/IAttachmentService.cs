using DHSIntegrationAgent.Contracts.Persistence;

namespace DHSIntegrationAgent.Application.Abstractions;

public interface IAttachmentService
{
    Task<string> UploadAsync(AttachmentRow attachment, CancellationToken ct);
}
