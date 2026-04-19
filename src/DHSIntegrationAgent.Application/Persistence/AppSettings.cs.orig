namespace DHSIntegrationAgent.Application.Persistence;

/// <summary>
/// SQLite source-of-truth for one-time setup + runtime toggles (KB: AppSettings singleton).
/// </summary>
public sealed record AppSettings(
    string GroupID,
    string ProviderDhsCode,
    int ConfigCacheTtlMinutes,
    int FetchIntervalMinutes,
    int ManualRetryCooldownMinutes,
    int LeaseDurationSeconds,
    int FetchClaimCountPerThread,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
