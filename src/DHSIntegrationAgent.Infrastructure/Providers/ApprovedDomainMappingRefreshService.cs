using System.Text.Json.Nodes;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Infrastructure.Http.Clients;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Providers;

public sealed class ApprovedDomainMappingRefreshService : IApprovedDomainMappingRefreshService
{
    private readonly ILogger<ApprovedDomainMappingRefreshService> _logger;
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly ProviderDomainMappingClient _client;

    public ApprovedDomainMappingRefreshService(
        ILogger<ApprovedDomainMappingRefreshService> logger,
        ISqliteUnitOfWorkFactory uowFactory,
        ProviderDomainMappingClient client)
    {
        _logger = logger;
        _uowFactory = uowFactory;
        _client = client;
    }

    public async Task RefreshApprovedMappingsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await using var uow = await _uowFactory.CreateAsync(ct);
        var settings = await uow.AppSettings.GetAsync(ct);

        var providerDhsCode = settings.ProviderDhsCode ?? "";
        if (string.IsNullOrWhiteSpace(providerDhsCode))
            throw new InvalidOperationException("AppSettings.ProviderDhsCode is required to refresh provider domain mappings.");

        var http = await _client.GetProviderDomainMappingAsync(providerDhsCode, ct);
        if (!http.Success || string.IsNullOrWhiteSpace(http.Json))
            throw new InvalidOperationException(http.Error ?? "GetProviderDomainMapping failed with no error message.");

        // Response might be:
        // - an array directly
        // - or envelope { data: [...] }
        JsonNode? node;
        try { node = JsonNode.Parse(http.Json); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetProviderDomainMapping returned invalid JSON.");
            throw;
        }

        if (node is JsonObject obj && obj.TryGetPropertyValue("data", out var dataNode))
            node = dataNode;

        if (node is not JsonArray arr || arr.Count == 0)
        {
            _logger.LogInformation("GetProviderDomainMapping returned no items. ProviderDhsCode={ProviderDhsCode}", providerDhsCode);
            await uow.CommitAsync(ct);
            return;
        }

        var upserted = 0;
        var skipped = 0;

        foreach (var item in arr.OfType<JsonObject>())
        {
            var domainName =
                TryGetString(item, "domainName") ??
                TryGetString(item, "domainTableName") ??
                TryGetString(item, "domain") ??
                "";

            var domainTableId =
                TryGetInt(item, "domainTableId") ??
                TryGetInt(item, "domainTableID") ??
                TryGetInt(item, "domainId");

            var sourceValue =
                TryGetString(item, "providerCodeValue") ??
                TryGetString(item, "providerNameValue") ??
                TryGetString(item, "sourceValue") ??
                TryGetString(item, "providerValue") ??
                "";

            var targetValue =
                TryGetString(item, "dhsCodeValue") ??
                TryGetString(item, "dhsNameValue") ??
                TryGetString(item, "targetValue") ??
                TryGetString(item, "mappedValue") ??
                "";

            if (string.IsNullOrWhiteSpace(domainName) ||
                domainTableId is null ||
                string.IsNullOrWhiteSpace(sourceValue) ||
                string.IsNullOrWhiteSpace(targetValue))
            {
                skipped++;
                _logger.LogWarning(
                    "Skipping provider domain mapping item due to missing fields. domainName='{DomainName}', domainTableId='{DomainTableId}', source='{Source}', target='{Target}'. RawItem={RawItem}",
                    domainName,
                    domainTableId?.ToString() ?? "<null>",
                    sourceValue,
                    targetValue,
                    item.ToJsonString());
                continue;
            }

            await uow.DomainMappings.UpsertApprovedAsync(
                providerDhsCode,
                domainName!,
                domainTableId.Value,
                sourceValue!,
                targetValue!,
                now,
                ct);

            upserted++;
        }

        await uow.CommitAsync(ct);

        _logger.LogInformation(
            "RefreshApprovedMappings complete. ProviderDhsCode={ProviderDhsCode}, Items={Items}, Upserted={Upserted}, Skipped={Skipped}",
            providerDhsCode, arr.Count, upserted, skipped);
    }

    private static string? TryGetString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var n) && n is not null
            ? (n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToString())
            : null;
    }

    private static int? TryGetInt(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var n) || n is null)
            return null;

        if (n is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return checked((int)l);
            if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        }

        return null;
    }
}
