namespace DHSIntegrationAgent.Application.Persistence;

public interface IAppSettingsStore
{
    Task<AppSettings?> GetAsync(CancellationToken ct);

    Task UpsertSetupAsync(
        string groupId,
        string providerDhsCode,
        DateTimeOffset utcNow,
        CancellationToken ct);
}
