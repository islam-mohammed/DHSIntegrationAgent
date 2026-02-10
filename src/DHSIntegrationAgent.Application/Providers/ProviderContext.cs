using DHSIntegrationAgent.Application.Persistence;

namespace DHSIntegrationAgent.Application.Providers;

public sealed class ProviderContext : IProviderContext
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;

    public ProviderContext(ISqliteUnitOfWorkFactory uowFactory)
    {
        _uowFactory = uowFactory;
    }

    public async Task<string> GetProviderDhsCodeAsync(CancellationToken ct)
    {
        await using var uow = await _uowFactory.CreateAsync(ct);
        var settings = await uow.AppSettings.GetAsync(ct);

        var providerDhsCode = settings.ProviderDhsCode ?? string.Empty;
        if (string.IsNullOrWhiteSpace(providerDhsCode))
            throw new InvalidOperationException("ProviderDhsCode is not configured in AppSettings.");

        // no writes -> no commit required
        return providerDhsCode;
    }
}
