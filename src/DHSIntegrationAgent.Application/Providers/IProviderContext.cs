namespace DHSIntegrationAgent.Application.Providers;

public interface IProviderContext
{
    Task<string> GetProviderDhsCodeAsync(CancellationToken ct);
}
