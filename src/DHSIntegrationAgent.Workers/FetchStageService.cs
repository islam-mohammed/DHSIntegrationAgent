using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Claims;
using DHSIntegrationAgent.Domain.Claims;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Contracts.Claims;
using DHSIntegrationAgent.Contracts.DomainMapping;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Domain.WorkStates;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class FetchStageService : IFetchStageService
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IProviderTablesAdapter _tablesAdapter;
    private readonly IBatchClient _batchClient;
    private readonly IDomainMappingClient _domainMappingClient;
    private readonly ISystemClock _clock;
    private readonly ILogger<FetchStageService> _logger;

    public FetchStageService(
        ISqliteUnitOfWorkFactory uowFactory,
        IProviderTablesAdapter tablesAdapter,
        IBatchClient batchClient,
        IDomainMappingClient domainMappingClient,
        ISystemClock clock,
        ILogger<FetchStageService> logger)
    {
        _uowFactory = uowFactory;
        _tablesAdapter = tablesAdapter;
        _batchClient = batchClient;
        _domainMappingClient = domainMappingClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task ProcessBatchAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        _logger.LogInformation("Processing batch {BatchId} for {CompanyCode} {MonthKey}", batch.BatchId, batch.CompanyCode, batch.MonthKey);

        string? bcrId = batch.BcrId;
        string? payerCode = batch.PayerCode;

        // 1. Resolve payer settings and PayerCode if missing
        if (string.IsNullOrWhiteSpace(payerCode))
        {
            await using var uow = await _uowFactory.CreateAsync(ct);
            var payerProfile = await uow.Payers.GetByCompanyCodeAsync(batch.ProviderDhsCode, batch.CompanyCode, ct);
            if (payerProfile != null)
            {
                payerCode = payerProfile.PayerCode;
                // Update batch with resolved PayerCode
                await uow.Batches.UpdateStatusAsync(batch.BatchId, batch.BatchStatus, null, null, _clock.UtcNow, ct, payerCode);
                await uow.CommitAsync(ct);
            }
        }

        // 2. Ensure BcrId exists
        if (string.IsNullOrWhiteSpace(bcrId))
        {
            progress.Report(new WorkerProgressReport("StreamA", "Creating New Batch on Server", BatchId: batch.BatchId));

            var startDate = batch.StartDateUtc ?? ParseMonthKey(batch.MonthKey);
            var endDate = batch.EndDateUtc ?? startDate.AddMonths(1).AddTicks(-1);
            var totalClaimsCount = await _tablesAdapter.CountClaimsAsync(batch.ProviderDhsCode, batch.CompanyCode, startDate, endDate, ct);

            var request = new CreateBatchRequestItem(batch.CompanyCode, startDate, endDate, totalClaimsCount, batch.ProviderDhsCode);
            var result = await _batchClient.CreateBatchAsync(new[] { request }, ct);

            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to create batch on backend: {result.ErrorMessage}");

            bcrId = result.BatchId.ToString();

            await using var uow = await _uowFactory.CreateAsync(ct);
            await uow.Batches.SetBcrIdAsync(batch.BatchId, bcrId, _clock.UtcNow, ct);
            await uow.CommitAsync(ct);
        }

        // 3. Fetch and Stage Claims
        var batchStartDate = batch.StartDateUtc ?? ParseMonthKey(batch.MonthKey);
        var batchEndDate = batch.EndDateUtc ?? batchStartDate.AddMonths(1).AddTicks(-1);
        var totalClaims = await _tablesAdapter.CountClaimsAsync(batch.ProviderDhsCode, batch.CompanyCode, batchStartDate, batchEndDate, ct);

        progress.Report(new WorkerProgressReport("StreamA", $"Fetching 0 of {totalClaims} from HIS system", BatchId: batch.BatchId, ProcessedCount: 0, TotalCount: totalClaims, BcrId: bcrId));

        int? lastSeen = null;
        const int PageSize = 100;

        // SQLite "database is locked" mitigation:
        // - Never hold a SQLite transaction open across awaited I/O (provider DB reads, CPU build, etc.)
        // - Persist in small, write-only transactions.
        const int MaxClaimsPerTx = 25;

        var builder = new ClaimBundleBuilder();
        int processedCount = 0;
        var domains = BaselineDomainScanner.GetBaselineDomains();

        // Optimization: Pre-group domains by section and lowercase field name for O(N) discovery.
        var domainLookup = domains
            .GroupBy(d => d.FieldPath.Split('.')[0])
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(d => d.FieldPath.Split('.')[1].ToLowerInvariant())
                      .ToDictionary(
                          gg => gg.Key,
                          gg => gg.ToList()
                      )
            );

        var discoveredValues = new HashSet<(string DomainName, int DomainTableId, string Value)>();

        while (true)
        {
            var keys = await _tablesAdapter.ListClaimKeysAsync(
                batch.ProviderDhsCode,
                batch.CompanyCode,
                batchStartDate,
                batchEndDate,
                PageSize,
                lastSeen,
                ct);

            if (keys.Count == 0)
                break;

            // Fetch full bundles in batch (WBS 3.2 efficiency optimization)
            var rawBundles = await _tablesAdapter.GetClaimBundlesRawBatchAsync(batch.ProviderDhsCode, keys, ct);

            // Build payloads OUTSIDE any SQLite transaction.
            var workItems = new List<StageWorkItem>(rawBundles.Count);

            foreach (var rawBundle in rawBundles)
            {
                if (ct.IsCancellationRequested) break;

                var buildResult = builder.Build(new CanonicalClaimParts(
                    rawBundle.Header,
                    rawBundle.Services,
                    rawBundle.Diagnoses,
                    rawBundle.Labs,
                    rawBundle.Radiology,
                    rawBundle.OpticalVitalSigns,
                    DhsDoctors: rawBundle.DhsDoctors
                ), batch.CompanyCode);

                byte[]? payloadBytes = null;
                string? sha256 = null;

                if (buildResult.Succeeded && buildResult.Bundle is not null)
                {
                    // In-memory domain discovery (optimization)
                    DiscoverDomainValues(buildResult.Bundle, domainLookup, discoveredValues);

                    // Injected fields per your request
                    buildResult.Bundle.ClaimHeader.Remove("provider_dhsCode");
                    buildResult.Bundle.ClaimHeader["providerCode"] = batch.ProviderDhsCode;
                    if (long.TryParse(bcrId, out var bcrIdLong))
                        buildResult.Bundle.ClaimHeader["bCR_Id"] = bcrIdLong;

                    var payloadJson = buildResult.Bundle.ToJsonString();
                    payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                    sha256 = ComputeSha256(payloadBytes);
                }

                workItems.Add(new StageWorkItem(
                    ProIdClaim: rawBundle.ProIdClaim,
                    BuildResult: buildResult,
                    PayloadBytes: payloadBytes,
                    Sha256: sha256));

                processedCount++;
                lastSeen = rawBundle.ProIdClaim;

                if (processedCount % 10 == 0 || processedCount == totalClaims)
                {
                    double fetchPercentage = ((double)processedCount / totalClaims) * 40;
                    progress.Report(new WorkerProgressReport("StreamA", $"Fetching {processedCount} of {totalClaims} from HIS system", Percentage: fetchPercentage, BatchId: batch.BatchId, ProcessedCount: processedCount, TotalCount: totalClaims));
                }
            }

            // Ensure lastSeen is updated even if some bundles were skipped (shouldn't happen with batch fetch)
            if (keys.Count > 0)
                lastSeen = keys[^1];

            // Persist to SQLite in small, write-only transactions.
            for (var i = 0; i < workItems.Count; i += MaxClaimsPerTx)
            {
                if (ct.IsCancellationRequested) break;

                var chunk = workItems.Skip(i).Take(MaxClaimsPerTx).ToList();

                await using var uow = await _uowFactory.CreateAsync(ct);

                foreach (var item in chunk)
                {
                    var buildResult = item.BuildResult;

                    if (buildResult.Succeeded && buildResult.Bundle is not null && item.PayloadBytes is not null && item.Sha256 is not null)
                    {
                        await uow.Claims.UpsertStagedAsync(
                            batch.ProviderDhsCode,
                            item.ProIdClaim,
                            batch.CompanyCode,
                            batch.MonthKey,
                            batch.BatchId,
                            bcrId,
                            _clock.UtcNow,
                            ct);

                        await uow.ClaimPayloads.UpsertAsync(
                            new ClaimPayloadRow(
                                new ClaimKey(batch.ProviderDhsCode, item.ProIdClaim),
                                item.PayloadBytes,
                                item.Sha256,
                                1,
                                _clock.UtcNow,
                                _clock.UtcNow),
                            ct);
                    }

                }

                await uow.CommitAsync(ct);
            }

            if (keys.Count < PageSize)
                break;
        }

        // 4. Missing mappings discovery (posting is separate)
        progress.Report(new WorkerProgressReport("StreamA", "Domain Mapping Scan", Percentage: 40, BatchId: batch.BatchId));

        if (discoveredValues.Count > 0)
        {
            int discoveredInRun = 0;
            int scannedCount = 0;

            // Batch upserts to keep transactions short.
            const int MappingTxBatchSize = 200;
            var buffer = new List<ScannedDomainValue>(MappingTxBatchSize);

            foreach (var item in discoveredValues)
            {
                if (ct.IsCancellationRequested) break;

                buffer.Add(new ScannedDomainValue(item.DomainName, item.DomainTableId, item.Value));
                scannedCount++;

                if (scannedCount % 50 == 0)
                {
                    double scanPercentage = 40 + ((double)scannedCount / discoveredValues.Count) * 20;
                    progress.Report(new WorkerProgressReport("StreamA", "Domain Mapping Scan", Percentage: scanPercentage, BatchId: batch.BatchId));
                }

                if (buffer.Count >= MappingTxBatchSize)
                {
                    discoveredInRun += await UpsertMissingMappingsAsync(batch.ProviderDhsCode, buffer, ct);
                    buffer.Clear();
                }
            }

            if (buffer.Count > 0)
                discoveredInRun += await UpsertMissingMappingsAsync(batch.ProviderDhsCode, buffer, ct);

            if (discoveredInRun > 0)
                progress.Report(new WorkerProgressReport("StreamA", $"DETECTED_MISSING_DOMAINS:{discoveredInRun}"));
        }

        progress.Report(new WorkerProgressReport("StreamA", "Domain Mapping Scan Complete", Percentage: 60, BatchId: batch.BatchId));

        // 5. Mark batch ready for sender
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Ready, true, null, _clock.UtcNow, ct);
            await uow.CommitAsync(ct);
        }

        progress.Report(new WorkerProgressReport("StreamA", $"Batch {batch.BatchId} staged successfully. {processedCount} claims processed.", BatchId: batch.BatchId, ProcessedCount: processedCount, TotalCount: totalClaims));
    }

    private void DiscoverDomainValues(
        ClaimBundle bundle,
        Dictionary<string, Dictionary<string, List<BaselineDomain>>> domainLookup,
        HashSet<(string DomainName, int DomainTableId, string Value)> discovered)
    {
        ProcessSection(bundle.ClaimHeader, "claimHeader", domainLookup, discovered);

        foreach (var item in bundle.ServiceDetails.OfType<JsonObject>())
            ProcessSection(item, "serviceDetails", domainLookup, discovered);

        foreach (var item in bundle.DiagnosisDetails.OfType<JsonObject>())
            ProcessSection(item, "diagnosisDetails", domainLookup, discovered);

        foreach (var item in bundle.DhsDoctors.OfType<JsonObject>())
            ProcessSection(item, "dhsDoctors", domainLookup, discovered);
    }

    private static void ProcessSection(
        JsonObject obj,
        string sectionName,
        Dictionary<string, Dictionary<string, List<BaselineDomain>>> domainLookup,
        HashSet<(string DomainName, int DomainTableId, string Value)> discovered)
    {
        if (!domainLookup.TryGetValue(sectionName, out var sectionDomains))
            return;

        foreach (var kv in obj)
        {
            if (kv.Value == null) continue;

            var keyLower = kv.Key.ToLowerInvariant();
            if (sectionDomains.TryGetValue(keyLower, out var domains))
            {
                var val = kv.Value.ToString().Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(val)) continue;

                foreach (var domain in domains)
                {
                    discovered.Add((domain.DomainName, domain.DomainTableId, val));
                }
            }
        }
    }

    private async Task<int> UpsertMissingMappingsAsync(string providerDhsCode, IReadOnlyList<ScannedDomainValue> values, CancellationToken ct)
    {
        int discovered = 0;

        await using var uow = await _uowFactory.CreateAsync(ct);

        foreach (var mm in values)
        {
            if (await uow.DomainMappings.ExistsAsync(providerDhsCode, mm.DomainTableId, mm.Value, ct))
                continue;

            await uow.DomainMappings.UpsertDiscoveredAsync(
                providerDhsCode,
                mm.DomainName,
                mm.DomainTableId,
                mm.Value,
                DiscoverySource.Scanned,
                _clock.UtcNow,
                ct,
                providerNameValue: mm.Value,
                domainTableName: mm.DomainName);

            discovered++;
        }

        await uow.CommitAsync(ct);
        return discovered;
    }

    public async Task PostMissingMappingsAsync(string providerDhsCode, IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        _logger.LogInformation("Checking for missing domain mappings to post for provider {ProviderDhsCode}...", providerDhsCode);

        IReadOnlyList<MissingDomainMappingRow> eligible;
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            eligible = await uow.DomainMappings.ListEligibleForPostingAsync(providerDhsCode, ct);
        }

        if (eligible.Count == 0)
        {
            progress.Report(new WorkerProgressReport("StreamA", "No missing domain mappings to post.", Percentage: 70));
            return;
        }

        progress.Report(new WorkerProgressReport("StreamA", $"Posting {eligible.Count} missing domain mappings for {providerDhsCode}...", Percentage: 60));

        const int BatchSize = 500;
        for (int i = 0; i < eligible.Count; i += BatchSize)
        {
            if (ct.IsCancellationRequested) break;

            double postPercentage = 60 + ((double)i / eligible.Count) * 10;
            progress.Report(new WorkerProgressReport("StreamA", $"Posting domain mappings...", Percentage: postPercentage));

            var itemsToPost = eligible.Skip(i).Take(BatchSize).ToList();
            var items = itemsToPost
                .Select(x => new MismappedItem(x.SourceValue, x.ProviderNameValue ?? x.SourceValue, x.DomainTableId))
                .ToList();

            var request = new InsertMissMappingDomainRequest(providerDhsCode, items);

            try
            {
                var result = await _domainMappingClient.InsertMissMappingDomainAsync(request, ct);

                await using var uow = await _uowFactory.CreateAsync(ct);

                if (result.Succeeded)
                {
                    foreach (var row in itemsToPost)
                        await uow.DomainMappings.UpdateMissingStatusAsync(row.MissingMappingId, MappingStatus.Posted, _clock.UtcNow, _clock.UtcNow, ct);
                }
                else
                {
                    _logger.LogWarning("Failed to post domain mapping batch for {ProviderDhsCode}: {Message}", providerDhsCode, result.Message);
                    foreach (var row in itemsToPost)
                        await uow.DomainMappings.UpdateMissingStatusAsync(row.MissingMappingId, MappingStatus.PostFailed, _clock.UtcNow, null, ct);
                }

                await uow.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error posting domain mapping batch for {ProviderDhsCode}", providerDhsCode);

                await using var uow = await _uowFactory.CreateAsync(ct);
                foreach (var row in itemsToPost)
                    await uow.DomainMappings.UpdateMissingStatusAsync(row.MissingMappingId, MappingStatus.PostFailed, _clock.UtcNow, null, ct);
                await uow.CommitAsync(ct);
            }
        }

        progress.Report(new WorkerProgressReport("StreamA", "Domain mapping posting complete", Percentage: 70));
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private DateTimeOffset ParseMonthKey(string monthKey)
    {
        if (monthKey.Length != 6
            || !int.TryParse(monthKey.Substring(0, 4), out var year)
            || !int.TryParse(monthKey.Substring(4, 2), out var month))
        {
            throw new ArgumentException("Invalid MonthKey format. Expected YYYYMM.", nameof(monthKey));
        }
        return new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed record StageWorkItem(
        int ProIdClaim,
        ClaimBundleBuildResult BuildResult,
        byte[]? PayloadBytes,
        string? Sha256
    );
}
