using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Providers;
﻿using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Persistence.Repositories;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Domain.WorkStates;
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
    private readonly Application.Abstractions.IDomainMappingClient _domainMappingClient;

    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public ProviderConfigurationService(
        ILogger<ProviderConfigurationService> logger,
        ISqliteUnitOfWorkFactory uowFactory,
        ProviderConfigurationClient client,
        IColumnEncryptor encryptor,
        Application.Abstractions.IDomainMappingClient domainMappingClient)
    {
        _logger = logger;
        _uowFactory = uowFactory;
        _client = client;
        _encryptor = encryptor;
        _domainMappingClient = domainMappingClient;
    }

    public async Task RefreshDomainMappingsAsync(string providerDhsCode, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var result = await _domainMappingClient.GetProviderDomainMappingsWithMissingAsync(providerDhsCode, ct);

        if (!result.Succeeded || result.Data == null)
        {
            var err = result.Message ?? "Failed to refresh domain mappings from API.";
            _logger.LogWarning("RefreshDomainMappingsAsync failed: {Error}", err);
            return;
        }

        await using var uow = await _uowFactory.CreateAsync(ct);

        if (result.Data.DomainMappings != null)
        {
            foreach (var m in result.Data.DomainMappings)
            {
                await uow.DomainMappings.UpsertApprovedFromProviderConfigAsync(
                    providerDhsCode,
                    m.DomTable_ID,
                    m.ProviderDomainCode ?? $"DomTable_{m.DomTable_ID}",
                    m.ProviderDomainCode ?? "",
                    m.ProviderDomainValue ?? "",
                    m.DhsDomainValue,
                    m.IsDefault,
                    m.CodeValue,
                    m.DisplayValue,
                    now,
                    ct);
            }
        }

        if (result.Data.MissingDomainMappings != null)
        {
            foreach (var m in result.Data.MissingDomainMappings)
            {
                await uow.DomainMappings.UpsertMissingFromProviderConfigAsync(
                    providerDhsCode,
                    m.DomainTableId,
                    m.DomainTableName ?? $"DomTable_{m.DomainTableId}",
                    m.ProviderCodeValue ?? "",
                    m.ProviderNameValue ?? "",
                    m.DomainTableName,
                    now,
                    ct);
            }
        }

        await uow.CommitAsync(ct);
        _logger.LogInformation("Successfully refreshed domain mappings for {ProviderDhsCode}", providerDhsCode);
    }

    public async Task<ProviderConfigurationSnapshot> LoadAsync(CancellationToken ct)
    {
        await _loadGate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;

            string providerDhsCode;
            int lastKnownTtlMinutes;
            ProviderConfigCacheRow? anyCache;

            // Keep SQLite transactions short:
            // - Read settings/cache in one quick unit-of-work
            // - Perform HTTP call outside of SQLite transaction
            // - Persist results in a fresh write unit-of-work
            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                var settings = await uow.AppSettings.GetAsync(ct);

                providerDhsCode = settings.ProviderDhsCode ?? "";
                if (string.IsNullOrWhiteSpace(providerDhsCode))
                    throw new InvalidOperationException("AppSettings.ProviderDhsCode is required to load provider configuration.");

                lastKnownTtlMinutes = settings.ConfigCacheTtlMinutes <= 0 ? DefaultTtlMinutes : settings.ConfigCacheTtlMinutes;

                // If cached & valid: return cached, but also ensure derived tables are populated from cached JSON (safe, deterministic).
                var valid = await uow.ProviderConfigCache.GetValidAsync(providerDhsCode, now, ct);
                if (valid is not null)
                {
                    await TryUpsertPayerProfilesFromCachedJsonAsync(uow, providerDhsCode, valid.ConfigJson, now, ct);
                    await TryUpsertDomainMappingsFromCachedJsonAsync(uow, providerDhsCode, valid.ConfigJson, now, ct);

                    await uow.CommitAsync(ct);
                    return BuildSnapshot(valid, fromCache: true, isStale: false);
                }

                anyCache = await uow.ProviderConfigCache.GetAnyAsync(providerDhsCode, ct);
            }

            // ✅ THIS IS THE REAL CLIENT IN YOUR SOLUTION
            var http = await _client.GetAsync(providerDhsCode, anyCache?.ETag, ct);

            await using var uow = await _uowFactory.CreateAsync(ct);

            // 304 Not Modified: extend TTL, and re-derive tables from cached config json
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

                await TryUpsertPayerProfilesFromCachedJsonAsync(uow, providerDhsCode, refreshed.ConfigJson, now, ct);
                await TryUpsertDomainMappingsFromCachedJsonAsync(uow, providerDhsCode, refreshed.ConfigJson, now, ct);

                await uow.CommitAsync(ct);
                return BuildSnapshot(refreshed, fromCache: true, isStale: false);
            }

            // 200 OK: parse payload, persist provider profile + payers + approved+missing domain mappings, store sanitized config in cache
            if (http.Success && !string.IsNullOrWhiteSpace(http.Json))
            {
                var payload = TryExtractPayloadObject(http.Json!);
                if (payload is null)
                {
                    return await FallbackAsync(uow, providerDhsCode, anyCache, now,
                        "Provider config JSON missing expected 'data' payload.", ct);
                }

                await TryUpsertProviderProfileFromConfigAsync(uow, providerDhsCode, payload, now, ct);

                // Persist APPROVED + MISSING domain mappings into SQLite DomainMapping table
                await TryUpsertApprovedDomainMappingsFromConfigAsync(uow, providerDhsCode, payload, now, ct);
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

                // redact secrets before storing config json
                RedactProviderSecrets(payload);

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

    private async Task<ProviderConfigurationSnapshot> FallbackAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        ProviderConfigCacheRow? anyCache,
        DateTimeOffset now,
        string error,
        CancellationToken ct)
    {
        // If we have any cached config, keep using it (stale), but update error
        if (anyCache is not null)
        {
            var stale = anyCache with { LastError = error };

            await uow.ProviderConfigCache.UpsertAsync(stale, ct);

            await TryUpsertPayerProfilesFromCachedJsonAsync(uow, providerDhsCode, stale.ConfigJson, now, ct);
            await TryUpsertDomainMappingsFromCachedJsonAsync(uow, providerDhsCode, stale.ConfigJson, now, ct);

            await uow.CommitAsync(ct);
            return BuildSnapshot(stale, fromCache: true, isStale: true);
        }

        // No cache at all => placeholder cache with very short TTL so it retries soon
        var placeholder = new ProviderConfigCacheRow(
            ProviderDhsCode: providerDhsCode,
            ConfigJson: BuildEmptyPlaceholderConfigJson(),
            FetchedUtc: now,
            ExpiresUtc: now.AddMinutes(PlaceholderRetryTtlMinutes),
            ETag: null,
            LastError: error);

        await uow.ProviderConfigCache.UpsertAsync(placeholder, ct);
        await uow.CommitAsync(ct);

        return BuildSnapshot(placeholder, fromCache: false, isStale: true);
    }

    private ProviderConfigurationSnapshot BuildSnapshot(ProviderConfigCacheRow row, bool fromCache, bool isStale)
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
        catch { return new ProviderConfigurationParsed(Array.Empty<ProviderPayerDto>(), null, null); }

        var payload = node as JsonObject;
        if (payload is not null && payload.TryGetPropertyValue("data", out var dataNode) && dataNode is JsonObject dataObj)
            payload = dataObj;

        if (payload is null)
            return new ProviderConfigurationParsed(Array.Empty<ProviderPayerDto>(), null, null);

        var providerPayers = new List<ProviderPayerDto>();

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

        var domainMappings = GetCaseInsensitive(payload, "domainMappings") as JsonArray;
        var missingDomainMappings = GetCaseInsensitive(payload, "missingDomainMappings") as JsonArray;

        return new ProviderConfigurationParsed(providerPayers, domainMappings, missingDomainMappings);
    }

    private static string BuildEmptyPlaceholderConfigJson()
    {
        var obj = new JsonObject
        {
            ["providerInfo"] = new JsonObject(),
            ["providerPayers"] = new JsonArray(),
            ["domainMappings"] = new JsonArray(),
            ["missingDomainMappings"] = new JsonArray()
        };
        return obj.ToJsonString();
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

            var encrypted = await _encryptor.EncryptAsync(Encoding.UTF8.GetBytes(connectionString!), ct);

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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert ProviderProfile from provider configuration.");
        }
    }

    private async Task TryUpsertPayerProfilesFromCachedJsonAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        string cachedConfigJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var payload = TryExtractPayloadObject(cachedConfigJson);
            if (payload is null)
                return;

            await TryUpsertPayerProfilesFromConfigAsync(uow, providerDhsCode, payload, now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert payer profiles from cached provider configuration JSON.");
        }
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
            if (GetCaseInsensitive(payload, "providerPayers") is not JsonArray payersArr)
                return;

            var rows = new List<PayerProfileRow>();

            foreach (var item in payersArr.OfType<JsonObject>())
            {
                var companyCode = GetString(item, "companyCode") ?? "";
                if (string.IsNullOrWhiteSpace(companyCode))
                    continue;

                var payerCode = GetString(item, "payerCode") ?? (GetInt(item, "payerId")?.ToString());
                var payerName =
                    GetString(item, "payerName") ??
                    GetString(item, "payerNameEn") ??
                    GetString(item, "parentPayerNameEn");

                rows.Add(new PayerProfileRow(
                    ProviderDhsCode: providerDhsCode,
                    CompanyCode: companyCode,
                    PayerCode: payerCode,
                    PayerName: payerName,
                    IsActive: true));
            }

            if (rows.Count == 0)
                return;

            await uow.Payers.UpsertManyAsync(rows, now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert payer profiles from provider configuration.");
        }
    }


    private async Task TryUpsertDomainMappingsFromCachedJsonAsync(
        ISqliteUnitOfWork uow,
        string providerDhsCode,
        string cachedConfigJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var payload = TryExtractPayloadObject(cachedConfigJson);
            if (payload is null)
                return;

            await TryUpsertApprovedDomainMappingsFromConfigAsync(uow, providerDhsCode, payload, now, ct);
            await TryUpsertMissingDomainMappingsFromConfigAsync(uow, providerDhsCode, payload, now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert domain mappings from cached provider configuration JSON.");
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
                return;

            var upserted = 0;
            var skipped = 0;

            foreach (var item in mappings.OfType<JsonObject>())
            {
                // Swagger keys
                var domTableId = GetInt(item, "domTable_ID");
                var providerDomainCode = GetString(item, "providerDomainCode") ?? "";
                var providerDomainValue = GetString(item, "providerDomainValue") ?? "";
                var dhsDomainValue = GetInt(item, "dhsDomainValue");
                var isDefault = GetBool(item, "isDefault");
                var codeValue = GetString(item, "codeValue");
                var displayValue = GetString(item, "displayValue");

                if (domTableId is null || dhsDomainValue is null || string.IsNullOrWhiteSpace(providerDomainValue))
                {
                    skipped++;
                    continue;
                }

                var domainName = !string.IsNullOrWhiteSpace(providerDomainCode)
                    ? providerDomainCode
                    : $"DomTable_{domTableId.Value}";

                await uow.DomainMappings.UpsertApprovedFromProviderConfigAsync(
                    providerDhsCode: providerDhsCode,
                    domTableId: domTableId.Value,
                    domainName: domainName,
                    providerDomainCode: providerDomainCode,
                    providerDomainValue: providerDomainValue,
                    dhsDomainValue: dhsDomainValue.Value,
                    isDefault: isDefault,
                    codeValue: codeValue,
                    displayValue: displayValue,
                    utcNow: now,
                    cancellationToken: ct);

                upserted++;
            }

            _logger.LogInformation(
                "Upserted approved domainMappings from provider config. ProviderDhsCode={ProviderDhsCode}, Upserted={Upserted}, Skipped={Skipped}",
                providerDhsCode, upserted, skipped);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert approved domainMappings from provider configuration.");
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
                return;

            var upserted = 0;
            var skipped = 0;

            foreach (var item in missing.OfType<JsonObject>())
            {
                // Swagger keys
                var domainTableId = GetInt(item, "domainTableId");
                var domainTableName = GetString(item, "domainTableName");
                var providerCodeValue = GetString(item, "providerCodeValue") ?? "";
                var providerNameValue = GetString(item, "providerNameValue") ?? providerCodeValue;

                if (domainTableId is null || string.IsNullOrWhiteSpace(providerCodeValue))
                {
                    skipped++;
                    continue;
                }

                var domainName = !string.IsNullOrWhiteSpace(domainTableName)
                    ? domainTableName!
                    : $"DomTable_{domainTableId.Value}";

                await uow.DomainMappings.UpsertMissingFromProviderConfigAsync(
                    providerDhsCode: providerDhsCode,
                    domainTableId: domainTableId.Value,
                    domainName: domainName,
                    providerCodeValue: providerCodeValue,
                    providerNameValue: providerNameValue,
                    domainTableName: domainTableName,
                    utcNow: now,
                    cancellationToken: ct);

                upserted++;
            }

            _logger.LogInformation(
                "Upserted missingDomainMappings from provider config. ProviderDhsCode={ProviderDhsCode}, Upserted={Upserted}, Skipped={Skipped}",
                providerDhsCode, upserted, skipped);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert missingDomainMappings from provider configuration.");
        }
    }

    private static JsonObject? TryExtractPayloadObject(string httpJson)
    {
        try
        {
            var root = JsonNode.Parse(httpJson) as JsonObject;
            if (root is null)
                return null;

            if (root.TryGetPropertyValue("data", out var dataNode) && dataNode is JsonObject dataObj)
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

    private void RedactProviderSecrets(JsonObject payload)
    {
        RemoveCaseInsensitive(payload, "connectionString");
        RemoveCaseInsensitive(payload, "dbConnectionString");
        RemoveCaseInsensitive(payload, "password");
        RemoveCaseInsensitive(payload, "apiKey");

        if (GetCaseInsensitive(payload, "providerInfo") is JsonObject providerInfo)
        {
            RemoveCaseInsensitive(providerInfo, "connectionString");
            RemoveCaseInsensitive(providerInfo, "dbConnectionString");
            RemoveCaseInsensitive(providerInfo, "password");
        }
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

    private static bool? GetBool(JsonObject obj, string key)
    {
        var node = GetCaseInsensitive(obj, key);
        if (node is null)
            return null;

        if (node is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var b))
                return b;

            if (v.TryGetValue<int>(out var i))
                return i != 0;

            if (v.TryGetValue<string>(out var s))
            {
                if (bool.TryParse(s, out var parsedBool)) return parsedBool;
                if (int.TryParse(s, out var parsedInt)) return parsedInt != 0;
            }
        }

        return null;
    }
}
