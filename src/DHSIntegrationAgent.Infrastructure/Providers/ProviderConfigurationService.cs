using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using DHSIntegrationAgent.Domain.WorkStates;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Infrastructure.Http.Clients;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Infrastructure.Providers;

public sealed class ProviderConfigurationService : IProviderConfigurationService
{
    private const int DefaultTtlMinutes = 1440;

    private const int PlaceholderRetryTtlMinutes = 2;

    private const string SingleProviderCode = "DEFAULT";

    private readonly ILogger<ProviderConfigurationService> _logger;
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly ProviderConfigurationClient _client;
    private readonly IColumnEncryptor _encryptor;

    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public ProviderConfigurationService(
        ILogger<ProviderConfigurationService> logger,
        ISqliteUnitOfWorkFactory uowFactory,
        ProviderConfigurationClient client,
        IColumnEncryptor encryptor)
    {
        _logger = logger;
        _uowFactory = uowFactory;
        _client = client;
        _encryptor = encryptor;
    }

    public async Task<ProviderConfigurationSnapshot> LoadAsync(CancellationToken ct)
    {
        await _loadGate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;

            await using var uow = await _uowFactory.CreateAsync(ct);

            var settings = await uow.AppSettings.GetAsync(ct);

            var providerDhsCode = settings.ProviderDhsCode ?? "";
            if (string.IsNullOrWhiteSpace(providerDhsCode))
                throw new InvalidOperationException("AppSettings.ProviderDhsCode is required to load provider configuration.");

            var lastKnownTtlMinutes = settings.ConfigCacheTtlMinutes <= 0 ? DefaultTtlMinutes : settings.ConfigCacheTtlMinutes;

            var valid = await uow.ProviderConfigCache.GetValidAsync(providerDhsCode, now, ct);
            if (valid is not null)
            {
                await uow.CommitAsync(ct);
                return BuildSnapshot(valid, fromCache: true, isStale: false);
            }

            var anyCache = await uow.ProviderConfigCache.GetAnyAsync(providerDhsCode, ct);

            var http = await _client.GetAsync(providerDhsCode, anyCache?.ETag, ct);

            if (http.Success && http.IsNotModified && anyCache is not null)
            {
                var refreshed = anyCache with
                {
                    FetchedUtc = now,
                    ExpiresUtc = now.AddMinutes(lastKnownTtlMinutes),
                    ETag = http.ETag ?? anyCache.ETag,
                    LastError = null
                };

                await uow.ProviderConfigCache.UpsertAsync(refreshed, ct);

                // Refresh PayerProfile using the cached provider configuration (ETag 304 path).
                await TryUpsertPayerProfilesFromCachedJsonAsync(uow, providerDhsCode, refreshed.ConfigJson, now, ct);

                await uow.CommitAsync(ct);

                return BuildSnapshot(refreshed, fromCache: true, isStale: false);
            }

            if (http.Success && !string.IsNullOrWhiteSpace(http.Json))
            {
                var payload = TryExtractPayloadObject(http.Json);
                if (payload is null)
                {
                    return await FallbackAsync(uow, providerDhsCode, anyCache, now,
                        "Provider config JSON missing expected 'data' payload.", ct);
                }

                await TryUpsertProviderProfileFromConfigAsync(uow, providerDhsCode, payload, now, ct);

                RedactProviderSecrets(payload);

                // ✅ Change Request: persist APPROVED domainMappings[] into SQLite DomainMapping
                await TryUpsertApprovedDomainMappingsFromConfigAsync(uow, providerDhsCode, payload, now, ct);

                // ✅ Change Request: persist MISSING domainMappings[] into SQLite DomainMapping
                await TryUpsertMissingDomainMappingsFromConfigAsync(uow, providerDhsCode, payload, now, ct);

                var ttlMinutes = lastKnownTtlMinutes;

                if (TryReadGeneralConfiguration(payload, out var gc))
                {
                    ttlMinutes = gc.ConfigCacheTtlMinutes > 0 ? gc.ConfigCacheTtlMinutes : ttlMinutes;

                    await uow.AppSettings.UpdateGeneralConfigurationAsync(
                        leaseDurationSeconds: gc.LeaseDurationSeconds,
                        streamAIntervalSeconds: gc.StreamAIntervalSeconds,
                        resumePollIntervalSeconds: gc.ResumePollIntervalSeconds,
                        apiTimeoutSeconds: gc.ApiTimeoutSeconds,
                        configCacheTtlMinutes: gc.ConfigCacheTtlMinutes,
                        fetchIntervalMinutes: gc.FetchIntervalMinutes,
                        manualRetryCooldownMinutes: gc.ManualRetryCooldownMinutes,
                        utcNow: now,
                        cancellationToken: ct);
                }
                else
                {
                    _logger.LogWarning("Provider config payload missing generalConfiguration; using last-known runtime values from SQLite AppSettings.");
                }

                var sanitizedJson = payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));

                var fresh = new ProviderConfigCacheRow(
                    ProviderDhsCode: providerDhsCode,
                    ConfigJson: sanitizedJson,
                    FetchedUtc: now,
                    ExpiresUtc: now.AddMinutes(ttlMinutes),
                    ETag: http.ETag,
                    LastError: null);

                await uow.ProviderConfigCache.UpsertAsync(fresh, ct);

                // Persist Provider Payers into SQLite PayerProfile using the same refresh/TTL policy as ProviderConfigCache.
                await TryUpsertPayerProfilesFromConfigAsync(uow, providerDhsCode, payload, now, ct);

                await uow.CommitAsync(ct);

                return BuildSnapshot(fresh, fromCache: false, isStale: false);
            }

            var error = http.Error ?? $"Provider configuration refresh failed (HTTP/transport).";
            return await FallbackAsync(uow, providerDhsCode, anyCache, now, error, ct);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task TryUpsertProviderProfileFromConfigAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        JsonObject payload,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var providerInfo = GetCaseInsensitive(payload, "providerInfo") as JsonObject;
            if (providerInfo is null)
                return;

            var connectionString = GetString(providerInfo, "connectionString");
            if (string.IsNullOrWhiteSpace(connectionString))
                return;

            var dbEngine = GetString(providerInfo, "dbEngine");
            if (string.IsNullOrWhiteSpace(dbEngine))
                dbEngine = "sqlserver";

            var integrationType = GetString(providerInfo, "integrationType");
            if (string.IsNullOrWhiteSpace(integrationType))
                integrationType = "Tables";

            var encrypted = await _encryptor.EncryptAsync(Encoding.UTF8.GetBytes(connectionString), ct);

            var row = new ProviderProfileRow(
                ProviderCode: SingleProviderCode,
                ProviderDhsCode: providerDhsCode,
                DbEngine: dbEngine!,
                IntegrationType: integrationType!,
                EncryptedConnectionString: encrypted,
                EncryptionKeyId: null,
                IsActive: true,
                CreatedUtc: now,
                UpdatedUtc: now);

            await uow.ProviderProfiles.UpsertAsync(row, ct);
            await uow.ProviderProfiles.SetActiveAsync(new ProviderKey(SingleProviderCode), true, now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert ProviderProfile from provider configuration payload.");
        }
    }

    private async Task TryUpsertApprovedDomainMappingsFromConfigAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        JsonObject payload,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var mappings = GetCaseInsensitive(payload, "domainMappings") as JsonArray;
            if (mappings is null || mappings.Count == 0)
            {
                _logger.LogInformation("Provider configuration contains no domainMappings to upsert. ProviderDhsCode={ProviderDhsCode}", providerDhsCode);
                return;
            }

            // determine company codes (if not present on item)
            var companyCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var providerPayers = GetCaseInsensitive(payload, "providerPayers") as JsonArray;

            if (providerPayers is not null)
            {
                foreach (var p in providerPayers.OfType<JsonObject>())
                {
                    var cc = GetString(p, "companyCode");
                    if (!string.IsNullOrWhiteSpace(cc))
                        companyCodes.Add(cc!);
                }
            }

            if (companyCodes.Count == 0)
                companyCodes.Add("DEFAULT");

            var upserted = 0;
            var skipped = 0;

            foreach (var item in mappings.OfType<JsonObject>())
            {
                // exact keys are unknown, so we accept safe variants + log skipped items
                var domainName =
                    GetString(item, "domainName") ??
                    GetString(item, "domainTableName") ??
                    GetString(item, "domain") ??
                    "";

                var domainTableId =
                    GetInt(item, "domainTableId") ??
                    GetInt(item, "domainTableID") ??
                    GetInt(item, "domainId");

                var sourceValue =
                    GetString(item, "providerCodeValue") ??
                    GetString(item, "providerNameValue") ??
                    GetString(item, "sourceValue") ??
                    GetString(item, "providerValue") ??
                    "";

                var targetValue =
                    GetString(item, "dhsCodeValue") ??
                    GetString(item, "dhsNameValue") ??
                    GetString(item, "targetValue") ??
                    GetString(item, "mappedValue") ??
                    "";

                var itemCompanyCode =
                    GetString(item, "companyCode");

                if (string.IsNullOrWhiteSpace(domainName) ||
                    domainTableId is null ||
                    string.IsNullOrWhiteSpace(sourceValue) ||
                    string.IsNullOrWhiteSpace(targetValue))
                {
                    skipped++;
                    _logger.LogWarning(
                        "Skipping approved domain mapping due to missing fields. domainName='{DomainName}', domainTableId='{DomainTableId}', source='{Source}', target='{Target}'. RawItem={RawItem}",
                        domainName,
                        domainTableId?.ToString() ?? "<null>",
                        sourceValue,
                        targetValue,
                        item.ToJsonString());
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(itemCompanyCode))
                {
                    await uow.DomainMappings.UpsertApprovedAsync(
                        providerDhsCode,
                        itemCompanyCode!,
                        domainName!,
                        domainTableId.Value,
                        sourceValue!,
                        targetValue!,
                        now,
                        ct);
                    upserted++;
                }
                else
                {
                    foreach (var cc in companyCodes)
                    {
                        await uow.DomainMappings.UpsertApprovedAsync(
                            providerDhsCode,
                            cc,
                            domainName!,
                            domainTableId.Value,
                            sourceValue!,
                            targetValue!,
                            now,
                            ct);
                        upserted++;
                    }
                }
            }

            _logger.LogInformation(
                "Approved domain mappings upsert complete. ProviderDhsCode={ProviderDhsCode}, Items={Items}, Upserted={Upserted}, Skipped={Skipped}",
                providerDhsCode, mappings.Count, upserted, skipped);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert approved domain mappings from provider configuration.");
        }
    }

    private async Task TryUpsertMissingDomainMappingsFromConfigAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        JsonObject payload,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var missing = GetCaseInsensitive(payload, "missingDomainMappings") as JsonArray;
            if (missing is null || missing.Count == 0)
            {
                _logger.LogInformation("Provider configuration contains no missingDomainMappings to upsert. ProviderDhsCode={ProviderDhsCode}", providerDhsCode);
                return;
            }

            // determine company codes (similar to approved mappings)
            var companyCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var providerPayers = GetCaseInsensitive(payload, "providerPayers") as JsonArray;

            if (providerPayers is not null)
            {
                foreach (var p in providerPayers.OfType<JsonObject>())
                {
                    var cc = GetString(p, "companyCode");
                    if (!string.IsNullOrWhiteSpace(cc))
                        companyCodes.Add(cc!);
                }
            }

            if (companyCodes.Count == 0)
                companyCodes.Add("DEFAULT");

            var upserted = 0;
            var skipped = 0;

            foreach (var item in missing.OfType<JsonObject>())
            {
                var domainName =
                    GetString(item, "domainTableName") ??
                    GetString(item, "domainName") ??
                    GetString(item, "domain") ??
                    "";

                var domainTableId =
                    GetInt(item, "domainTableId") ??
                    GetInt(item, "domainTableID") ??
                    GetInt(item, "domainId");

                var sourceValue =
                    GetString(item, "providerCodeValue") ??
                    GetString(item, "sourceValue") ??
                    GetString(item, "providerValue") ??
                    "";

                if (string.IsNullOrWhiteSpace(domainName) ||
                    domainTableId is null ||
                    string.IsNullOrWhiteSpace(sourceValue))
                {
                    skipped++;
                    continue;
                }

                foreach (var cc in companyCodes)
                {
                    await uow.MissingDomainMappings.UpsertAsync(
                        providerDhsCode,
                        cc,
                        domainName!,
                        domainTableId.Value,
                        sourceValue!,
                        DiscoverySource.Api,
                        now,
                        ct);
                    upserted++;
                }
            }

            _logger.LogInformation(
                "Missing domain mappings upsert complete. ProviderDhsCode={ProviderDhsCode}, Items={Items}, Upserted={Upserted}, Skipped={Skipped}",
                providerDhsCode, missing.Count, upserted, skipped);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert missing domain mappings from provider configuration.");
        }
    }

    private void RedactProviderSecrets(JsonObject payload)
    {
        var providerInfo = GetCaseInsensitive(payload, "providerInfo") as JsonObject;
        if (providerInfo is null)
            return;

        RemoveCaseInsensitive(providerInfo, "connectionString");
        RemoveCaseInsensitive(providerInfo, "password");
        RemoveCaseInsensitive(providerInfo, "clientSecret");
        RemoveCaseInsensitive(providerInfo, "secret");
        RemoveCaseInsensitive(providerInfo, "apiKey");
    }

    private async Task<ProviderConfigurationSnapshot> FallbackAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        ProviderConfigCacheRow? anyCache,
        DateTimeOffset now,
        string error,
        CancellationToken ct)
    {
        if (anyCache is not null)
        {
            var stale = anyCache with
            {
                ExpiresUtc = now,
                LastError = error
            };

            await uow.ProviderConfigCache.UpsertAsync(stale, ct);

            // Ensure PayerProfile is derived from the best-known cached provider configuration even when refresh fails.
            await TryUpsertPayerProfilesFromCachedJsonAsync(uow, providerDhsCode, stale.ConfigJson, now, ct);

            await uow.CommitAsync(ct);

            return BuildSnapshot(stale, fromCache: true, isStale: true);
        }

        var placeholder = new ProviderConfigCacheRow(
            ProviderDhsCode: providerDhsCode,
            ConfigJson: BuildPlaceholderConfigJson(),
            FetchedUtc: now,
            ExpiresUtc: now.AddMinutes(PlaceholderRetryTtlMinutes),
            ETag: null,
            LastError: error);

        await uow.ProviderConfigCache.UpsertAsync(placeholder, ct);
        await uow.CommitAsync(ct);

        return BuildSnapshot(placeholder, fromCache: true, isStale: true);
    }

    private static string BuildPlaceholderConfigJson()
    {
        var obj = new JsonObject
        {
            ["providerInfo"] = new JsonObject(),
            ["providerPayers"] = new JsonArray(),
            ["domainMappings"] = new JsonArray()
        };

        return obj.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private async Task TryUpsertPayerProfilesFromConfigAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        JsonObject payload,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var payers = GetCaseInsensitive(payload, "providerPayers") as JsonArray;
            if (payers is null || payers.Count == 0)
            {
                _logger.LogInformation("Provider configuration contains no providerPayers to upsert. ProviderDhsCode={ProviderDhsCode}", providerDhsCode);
                return;
            }

            var rows = new List<PayerProfileRow>(payers.Count);
            var companyCodesFromServer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in payers)
            {
                if (node is not JsonObject item)
                    continue;

                var companyCode = GetString(item, "companyCode");
                if (string.IsNullOrWhiteSpace(companyCode))
                    continue;

                // Swagger shows "payerId" (int). Older payloads may use "payerCode" (string).
                var payerId = GetInt(item, "payerId");
                var payerCode = GetString(item, "payerCode") ?? (payerId?.ToString());

                var payerNameEn = GetString(item, "payerNameEn");
                var parentPayerNameEn = GetString(item, "parentPayerNameEn");

                // Prefer payerNameEn; fallback to parent name if that's all we have.
                var payerName = !string.IsNullOrWhiteSpace(payerNameEn) ? payerNameEn : parentPayerNameEn;

                rows.Add(new PayerProfileRow(
                    ProviderDhsCode: providerDhsCode,
                    CompanyCode: companyCode!,
                    PayerCode: payerCode,
                    PayerName: payerName,
                    IsActive: true));

                companyCodesFromServer.Add(companyCode!);
            }

            if (rows.Count == 0)
                return;

            await uow.Payers.UpsertManyAsync(rows, now, ct);

            // Deactivate rows that disappeared from the server (same "refresh" semantics as config cache refresh).
            var active = await uow.Payers.ListActiveAsync(providerDhsCode, ct);
            foreach (var row in active)
            {
                if (!companyCodesFromServer.Contains(row.CompanyCode))
                    await uow.Payers.SetActiveAsync(providerDhsCode, row.CompanyCode, false, now, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist provider payers into SQLite. ProviderDhsCode={ProviderDhsCode}", providerDhsCode);
        }
    }

    private async Task TryUpsertPayerProfilesFromCachedJsonAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        string configJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var payload = TryExtractPayloadObject(configJson);
            if (payload is null)
                return;

            await TryUpsertPayerProfilesFromConfigAsync(uow, providerDhsCode, payload, now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist provider payers from cached provider configuration JSON. ProviderDhsCode={ProviderDhsCode}", providerDhsCode);
        }
    }

    private static ProviderConfigurationSnapshot BuildSnapshot(ProviderConfigCacheRow row, bool fromCache, bool isStale)
    {
        var parsed = ParseSubset(row.ConfigJson);

        return new ProviderConfigurationSnapshot(
            ProviderDhsCode: row.ProviderDhsCode,
            ConfigJson: row.ConfigJson,
            FetchedUtc: row.FetchedUtc.UtcDateTime,
            ExpiresUtc: row.ExpiresUtc.UtcDateTime,
            FromCache: fromCache,
            IsStale: isStale,
            LastError: row.LastError,
            Parsed: parsed);
    }

    private static ProviderConfigurationParsed ParseSubset(string configJson)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(configJson); }
        catch { return new ProviderConfigurationParsed(Array.Empty<ProviderPayerDto>(), null); }

        var payload = node as JsonObject;
        if (payload is not null && payload.TryGetPropertyValue("data", out var dataNode) && dataNode is JsonObject dataObj)
            payload = dataObj;

        if (payload is null)
            return new ProviderConfigurationParsed(Array.Empty<ProviderPayerDto>(), null);

        var providerPayers = new List<ProviderPayerDto>();
        var domainMappings = GetCaseInsensitive(payload, "domainMappings") as JsonArray;

        if (GetCaseInsensitive(payload, "providerPayers") is JsonArray payersArr)
        {
            foreach (var item in payersArr.OfType<JsonObject>())
            {
                var companyCode = GetString(item, "companyCode") ?? "";
                if (string.IsNullOrWhiteSpace(companyCode))
                    continue;

                var payerCode = GetString(item, "payerCode") ?? (GetInt(item, "payerId")?.ToString());
                var payerNameEn = GetString(item, "payerNameEn");
                var parentPayerNameEn = GetString(item, "parentPayerNameEn");

                providerPayers.Add(new ProviderPayerDto(companyCode, payerCode, payerNameEn, parentPayerNameEn));
            }
        }

        return new ProviderConfigurationParsed(providerPayers, domainMappings);
    }

    private static JsonObject? TryExtractPayloadObject(string httpJson)
    {
        try
        {
            var root = JsonNode.Parse(httpJson) as JsonObject;
            if (root is null)
                return null;

            if (GetCaseInsensitive(root, "data") is JsonObject dataObj)
                return dataObj;

            return root;
        }
        catch
        {
            return null;
        }
    }

    private sealed record GeneralConfiguration(
        int LeaseDurationSeconds,
        int StreamAIntervalSeconds,
        int ResumePollIntervalSeconds,
        int ApiTimeoutSeconds,
        int ConfigCacheTtlMinutes,
        int FetchIntervalMinutes,
        int ManualRetryCooldownMinutes);

    private static bool TryReadGeneralConfiguration(JsonObject payload, out GeneralConfiguration gc)
    {
        gc = default!;

        var general = GetCaseInsensitive(payload, "generalConfiguration") as JsonObject;
        if (general is null)
            return false;

        var leaseDurationSeconds = GetInt(general, "leaseDurationSeconds") ?? 0;
        var streamAIntervalSeconds = GetInt(general, "streamAIntervalSeconds") ?? 0;
        var resumePollIntervalSeconds = GetInt(general, "resumePollIntervalSeconds") ?? 0;
        var apiTimeoutSeconds = GetInt(general, "apiTimeoutSeconds") ?? 0;
        var configCacheTtlMinutes = GetInt(general, "configCacheTtlMinutes") ?? 0;
        var fetchIntervalMinutes = GetInt(general, "fetchIntervalMinutes") ?? 0;
        var manualRetryCooldownMinutes = GetInt(general, "manualRetryCooldownMinutes") ?? 0;

        gc = new GeneralConfiguration(
            leaseDurationSeconds,
            streamAIntervalSeconds,
            resumePollIntervalSeconds,
            apiTimeoutSeconds,
            configCacheTtlMinutes,
            fetchIntervalMinutes,
            manualRetryCooldownMinutes);

        return true;
    }

    private static JsonNode? GetCaseInsensitive(JsonObject obj, string key)
    {
        foreach (var kvp in obj)
        {
            if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return null;
    }

    private static void RemoveCaseInsensitive(JsonObject obj, string key)
    {
        var toRemove = obj
            .Select(kvp => kvp.Key)
            .FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (toRemove is not null)
            obj.Remove(toRemove);
    }

    private static string? GetString(JsonObject obj, string key)
    {
        var node = GetCaseInsensitive(obj, key);
        if (node is null)
            return null;

        if (node is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s))
                return s;

            return v.ToString();
        }

        return node.ToString();
    }

    private static int? GetInt(JsonObject obj, string key)
    {
        var node = GetCaseInsensitive(obj, key);
        if (node is null)
            return null;

        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i))
                return i;

            if (v.TryGetValue<long>(out var l))
                return checked((int)l);

            if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed))
                return parsed;
        }

        return null;
    }
}
