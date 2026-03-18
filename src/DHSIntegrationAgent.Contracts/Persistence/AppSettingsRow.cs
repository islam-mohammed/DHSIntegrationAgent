namespace DHSIntegrationAgent.Contracts.Persistence;

public sealed record AppSettingsRow(
    string? GroupId,
    string? ProviderDhsCode,
    string? NetworkUsername,
    byte[]? NetworkPasswordEncrypted,
    int ConfigCacheTtlMinutes,
    int FetchIntervalMinutes,
    int ManualRetryCooldownMinutes,
    int LeaseDurationSeconds,
    int StreamAIntervalSeconds,
    int ResumePollIntervalSeconds,
    int ApiTimeoutSeconds);
