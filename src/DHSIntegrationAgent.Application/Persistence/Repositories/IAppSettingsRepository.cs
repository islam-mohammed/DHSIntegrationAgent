namespace DHSIntegrationAgent.Application.Persistence.Repositories;

public sealed record AppSettingsRow(
    string? GroupId,
    string? ProviderDhsCode,
    int ConfigCacheTtlMinutes,
    int FetchIntervalMinutes,
    int ManualRetryCooldownMinutes,
    int LeaseDurationSeconds,
    int StreamAIntervalSeconds,
    int ResumePollIntervalSeconds,
    int ApiTimeoutSeconds);

public interface IAppSettingsRepository
{
    Task<AppSettingsRow> GetAsync(CancellationToken cancellationToken);

    Task UpdateSetupAsync(
        string groupId,
        string providerDhsCode,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    /// <summary>
    /// WBS 2.3 update: backend provider configuration now supplies these runtime knobs
    /// (generalConfiguration). We persist them into SQLite AppSettings (source of truth).
    /// </summary>
    Task UpdateGeneralConfigurationAsync(
        int leaseDurationSeconds,
        int streamAIntervalSeconds,
        int resumePollIntervalSeconds,
        int apiTimeoutSeconds,
        int configCacheTtlMinutes,
        int fetchIntervalMinutes,
        int manualRetryCooldownMinutes,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
