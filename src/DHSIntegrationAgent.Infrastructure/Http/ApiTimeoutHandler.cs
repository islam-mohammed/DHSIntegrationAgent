using DHSIntegrationAgent.Application.Persistence;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Http;

/// <summary>
/// Applies per-request timeout using SQLite AppSettings.ApiTimeoutSeconds
/// (which is populated from ProviderConfig.generalConfiguration.apiTimeout).
/// This prevents HttpClient.Timeout from being the source of truth.
/// </summary>
public sealed class ApiTimeoutHandler : DelegatingHandler
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly ILogger<ApiTimeoutHandler> _logger;

    public ApiTimeoutHandler(ISqliteUnitOfWorkFactory uowFactory, ILogger<ApiTimeoutHandler> logger)
    {
        _uowFactory = uowFactory;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var timeoutSeconds = 60; // safe fallback

        try
        {
            await using var uow = await _uowFactory.CreateAsync(ct);
            var s = await uow.AppSettings.GetAsync(ct);
            timeoutSeconds = Math.Max(1, s.ApiTimeoutSeconds);
            await uow.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read ApiTimeoutSeconds from SQLite; using fallback timeout.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        return await base.SendAsync(request, cts.Token);
    }
}
