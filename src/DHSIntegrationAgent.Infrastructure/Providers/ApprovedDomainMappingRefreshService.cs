using System.Text.Json.Nodes;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Domain.WorkStates;
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
    private readonly IDomainMappingClient _domainMappingClient;

    public ApprovedDomainMappingRefreshService(
        ILogger<ApprovedDomainMappingRefreshService> logger,
        ISqliteUnitOfWorkFactory uowFactory,
        ProviderDomainMappingClient client,
        IDomainMappingClient domainMappingClient)
    {
        _logger = logger;
        _uowFactory = uowFactory;
        _client = client;
        _domainMappingClient = domainMappingClient;
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

        // Determine company codes from AppSettings/config if your API doesn’t include it.
        // Here we default to DEFAULT if missing (consistent with config loader behavior).
        const string defaultCompanyCode = "DEFAULT";

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

            var companyCode =
                TryGetString(item, "companyCode") ??
                TryGetString(item, "CompanyCode") ??
                defaultCompanyCode;

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
                companyCode!,
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

    public async Task<ProviderDomainMappingsData?> RefreshAllMappingsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await using var uow = await _uowFactory.CreateAsync(ct);
        var settings = await uow.AppSettings.GetAsync(ct);

        var providerDhsCode = settings.ProviderDhsCode ?? "";
        if (string.IsNullOrWhiteSpace(providerDhsCode))
            throw new InvalidOperationException("AppSettings.ProviderDhsCode is required to refresh all domain mappings.");

        var result = await _domainMappingClient.GetProviderDomainMappingsWithMissingAsync(providerDhsCode, ct);
        if (!result.Succeeded || result.Data is null)
        {
            _logger.LogWarning("GetProviderDomainMappingsWithMissing failed. Error={Error}", result.Message);
            return null;
        }

        var data = result.Data;
        const string defaultCompanyCode = "DEFAULT";

        // 1) Upsert approved mappings
        if (data.DomainMappings is not null)
        {
            foreach (var item in data.DomainMappings)
            {
                var domainName = item.DomainName ?? item.DomainTableName ?? "";
                var domainTableId = item.DomTable_ID;
                var sourceValue = item.ProviderDomainValue ?? item.SourceValue ?? "";
                var targetValue = item.CodeValue ?? item.DisplayValue ?? item.TargetValue ?? "";
                var companyCode = item.CompanyCode ?? defaultCompanyCode;

                if (domainTableId is null || string.IsNullOrWhiteSpace(sourceValue) || string.IsNullOrWhiteSpace(targetValue))
                    continue;

                await uow.DomainMappings.UpsertApprovedAsync(
                    providerDhsCode,
                    companyCode,
                    domainName,
                    domainTableId.Value,
                    sourceValue,
                    targetValue,
                    now,
                    ct);
            }
        }

        // 2) Upsert missing mappings
        if (data.MissingDomainMappings is not null)
        {
            foreach (var item in data.MissingDomainMappings)
            {
                var domainName = item.DomainTableName ?? item.DomainName ?? "";
                var domainTableId = item.DomainTableId;
                var sourceValue = item.ProviderCodeValue ?? item.SourceValue ?? "";
                var companyCode = item.CompanyCode ?? defaultCompanyCode;

                if (string.IsNullOrWhiteSpace(sourceValue))
                    continue;

                await uow.DomainMappings.UpsertDiscoveredAsync(
                    providerDhsCode,
                    companyCode,
                    domainName,
                    domainTableId,
                    sourceValue,
                    MappingStatus.Missing,
                    now,
                    ct);
            }
        }

        await uow.CommitAsync(ct);

        _logger.LogInformation(
            "RefreshAllMappings complete. ProviderDhsCode={ProviderDhsCode}, ApprovedCount={ApprovedCount}, MissingCount={MissingCount}",
            providerDhsCode, data.DomainMappings?.Count ?? 0, data.MissingDomainMappings?.Count ?? 0);

        return data;
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
