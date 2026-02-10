using DHSIntegrationAgent.Application.Security;

namespace DHSIntegrationAgent.Infrastructure.Security;

public sealed class NoAuthHeaderProvider : IAuthHeaderProvider
{
    public Task<IReadOnlyDictionary<string, string>> GetHeadersAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}