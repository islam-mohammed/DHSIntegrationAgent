namespace DHSIntegrationAgent.Application.Security;

public interface IAuthHeaderProvider
{
    /// <summary>
    /// Return headers to attach to outbound API requests.
    /// Doesn't include PHI or Token values.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetHeadersAsync(CancellationToken ct);
}
