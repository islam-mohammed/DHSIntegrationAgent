using System.Threading;
using DHSIntegrationAgent.Application.Security;

namespace DHSIntegrationAgent.Infrastructure.Security;

// Simple, thread-safe, in-memory token store.
public sealed class InMemoryAuthTokenStore : IAuthTokenStore
{
    private string? _token; // Holds the current token in memory only.

    public string? GetToken()
        => Volatile.Read(ref _token); // Thread-safe read.

    public void SetToken(string? token)
        => Volatile.Write(ref _token, token); // Thread-safe write.

    public void Clear()
        => Volatile.Write(ref _token, null); // Thread-safe clear.
}
