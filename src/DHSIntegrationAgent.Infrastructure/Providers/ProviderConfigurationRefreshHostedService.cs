using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Application.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Providers;

/// <summary>
/// WBS 2.3 hardening:
/// - Periodically refresh Provider Configuration in the background.
/// - Respects cache TTL in SQLite (ProviderConfigCache.ExpiresUtc).
/// - Waits until user is logged in (auth token exists) and ProviderDhsCode is present.
/// - Uses ProviderConfigurationService.LoadAsync which already implements:
///     cache-hit short circuit, ETag conditional refresh, stale fallback, and secret redaction.
/// </summary>
internal sealed class ProviderConfigurationRefreshHostedService : BackgroundService
{
    private static readonly TimeSpan TokenPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MissingProviderCodePollInterval = TimeSpan.FromSeconds(5);

    // Refresh a bit before cache expiry to avoid hitting "expired cache" during work.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(30);

    // Guardrails (avoid extreme sleeps or tight loops).
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRefreshInterval = TimeSpan.FromMinutes(30);

    private readonly ILogger<ProviderConfigurationRefreshHostedService> _logger;
    private readonly IProviderConfigurationService _providerConfigurationService;
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IAuthTokenStore _tokenStore;

    public ProviderConfigurationRefreshHostedService(
        ILogger<ProviderConfigurationRefreshHostedService> logger,
        IProviderConfigurationService providerConfigurationService,
        ISqliteUnitOfWorkFactory uowFactory,
        IAuthTokenStore tokenStore)
    {
        _logger = logger;
        _providerConfigurationService = providerConfigurationService;
        _uowFactory = uowFactory;
        _tokenStore = tokenStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1) Wait until login. Avoid unauthenticated spam.
        while (!stoppingToken.IsCancellationRequested && string.IsNullOrWhiteSpace(_tokenStore.GetToken()))
            await Task.Delay(TokenPollInterval, stoppingToken);

        if (stoppingToken.IsCancellationRequested)
            return;

        _logger.LogInformation("Provider config refresh worker activated (auth token detected).");

        // 2) Prime cache immediately (best-effort).
        await TryRefreshAsync(stoppingToken);

        // 3) Periodic refresh loop with backoff.
        var backoff = new ExponentialBackoff(min: TimeSpan.FromSeconds(5), max: TimeSpan.FromMinutes(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nextDelay = await ComputeNextDelayAsync(stoppingToken);

                // Clamp to avoid extremes.
                if (nextDelay < MinRefreshInterval) nextDelay = MinRefreshInterval;
                if (nextDelay > MaxRefreshInterval) nextDelay = MaxRefreshInterval;

                await Task.Delay(nextDelay, stoppingToken);

                await TryRefreshAsync(stoppingToken);

                backoff.Reset();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // normal shutdown
            }
            catch (Exception ex)
            {
                var delay = backoff.NextDelayWithJitter();
                _logger.LogWarning(ex, "Provider config refresh loop error; backing off for {Delay}.", delay);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task TryRefreshAsync(CancellationToken ct)
    {
        try
        {
            var snapshot = await _providerConfigurationService.LoadAsync(ct);

            _logger.LogInformation(
                "Provider config refreshed. ProviderDhsCode={ProviderDhsCode}, FromCache={FromCache}, IsStale={IsStale}, ExpiresUtc={ExpiresUtc}",
                snapshot.ProviderDhsCode, snapshot.FromCache, snapshot.IsStale, snapshot.ExpiresUtc);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // PHI-safe: never log JSON payloads/tokens.
            _logger.LogWarning(ex, "Provider config refresh attempt failed.");
            throw;
        }
    }

    private async Task<TimeSpan> ComputeNextDelayAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await using var uow = await _uowFactory.CreateAsync(ct);
        var settings = await uow.AppSettings.GetAsync(ct);

        var providerDhsCode = settings.ProviderDhsCode ?? "";
        if (string.IsNullOrWhiteSpace(providerDhsCode))
            return MissingProviderCodePollInterval; // setup not completed

        ProviderConfigCacheRow? cache = await uow.ProviderConfigCache.GetAnyAsync(providerDhsCode, ct);
        await uow.CommitAsync(ct);

        if (cache is null)
            return TimeSpan.FromSeconds(5); // no cache exists yet

        // Schedule refresh slightly before expiry.
        var scheduledAt = cache.ExpiresUtc - RefreshSkew;
        var delay = scheduledAt - now;

        if (delay <= TimeSpan.Zero)
            return TimeSpan.FromSeconds(5);

        return delay;
    }

    private sealed class ExponentialBackoff
    {
        private readonly TimeSpan _min;
        private readonly TimeSpan _max;
        private int _attempt;

        public ExponentialBackoff(TimeSpan min, TimeSpan max)
        {
            _min = min;
            _max = max;
        }

        public void Reset() => _attempt = 0;

        public TimeSpan NextDelayWithJitter()
        {
            // 2^attempt * min (capped), ±20% jitter.
            var pow = Math.Pow(2, Math.Min(_attempt, 10));
            var candidate = TimeSpan.FromMilliseconds(_min.TotalMilliseconds * pow);
            if (candidate > _max) candidate = _max;

            _attempt++;

            var jitter = 0.8 + Random.Shared.NextDouble() * 0.4; // 0.8–1.2
            return TimeSpan.FromMilliseconds(candidate.TotalMilliseconds * jitter);
        }
    }
}
