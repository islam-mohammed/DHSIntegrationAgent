using System.Net;
using System.Text;
using System.Text.Json;
using DHSIntegrationAgent.Application.Abstractions;

namespace DHSIntegrationAgent.Infrastructure.Http.Clients;

public sealed class AttachmentClient : IAttachmentClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AttachmentClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<UpdateAttachmentsResult> UploadAttachmentAsync(UploadAttachmentRequest request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("BackendApi");
        const string path = "api/Batch/UploadAttachment";

        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        var httpCode = (int)response.StatusCode;

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return new UpdateAttachmentsResult(true, null, httpCode);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return new UpdateAttachmentsResult(false, body, httpCode);
    }
}
