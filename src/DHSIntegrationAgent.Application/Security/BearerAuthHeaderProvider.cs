using DHSIntegrationAgent.Application.Security;

namespace DHSIntegrationAgent.Infrastructure.Security;

// Provides outbound headers for API calls.
// If a token exists, attach "Authorization: Bearer <token>".
// If token is missing, return no auth headers.
public sealed class BearerAuthHeaderProvider : IAuthHeaderProvider
{
    private const string AuthorizationHeaderName = "Authorization"; // Standard HTTP auth header.
    private const string Scheme = "Bearer";                         // Standard JWT bearer scheme.

    private readonly IAuthTokenStore _tokenStore;                   // Injected in-memory store.

    public BearerAuthHeaderProvider(IAuthTokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    public Task<IReadOnlyDictionary<string, string>> GetHeadersAsync(CancellationToken ct)
    {
        // Read the token from memory.
        var token = _tokenStore.GetToken();

        // If no token, return empty headers.
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        // Return Authorization header WITHOUT logging the token anywhere.
        IReadOnlyDictionary<string, string> headers = new Dictionary<string, string>
        {
            [AuthorizationHeaderName] = $"{Scheme} {token}"
        };

        return Task.FromResult(headers);
    }
}
