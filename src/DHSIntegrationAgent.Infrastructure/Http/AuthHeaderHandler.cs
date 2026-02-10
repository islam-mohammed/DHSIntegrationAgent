using DHSIntegrationAgent.Application.Security;

namespace DHSIntegrationAgent.Infrastructure.Http;

public sealed class AuthHeaderHandler : DelegatingHandler
{
    private readonly IAuthHeaderProvider _auth;

    public AuthHeaderHandler(IAuthHeaderProvider auth)
    {
        _auth = auth;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = await _auth.GetHeadersAsync(ct);

        foreach (var kv in headers)
        {
            // Avoid duplicates if request already sets it
            if (!request.Headers.Contains(kv.Key))
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        return await base.SendAsync(request, ct);
    }
}
