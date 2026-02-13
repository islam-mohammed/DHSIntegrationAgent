using System.Security.Cryptography;
using System.Text;
using DHSIntegrationAgent.Adapters.Claims;
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
            progress.Report(new WorkerProgressReport("StreamA", $"Obtaining BcrId for batch {batch.BatchId}..."));

            var startDate = batch.StartDateUtc ?? ParseMonthKey(batch.MonthKey);
            var endDate = batch.EndDateUtc ?? startDate.AddMonths(1).AddTicks(-1);
            var totalClaims = await _tablesAdapter.CountClaimsAsync(batch.ProviderDhsCode, batch.CompanyCode, startDate, endDate, ct);

            var request = new CreateBatchRequestItem(batch.CompanyCode, startDate, endDate, totalClaims, batch.ProviderDhsCode);
            var result = await _batchClient.CreateBatchAsync(new[] { request }, ct);

            if (!result.Succeeded)
                throw new InvalidOperationException($"Failed to create batch on backend: {result.ErrorMessage}");

            bcrId = result.BatchId.ToString();

            await using var uow = await _uowFactory.CreateAsync(ct);
            await uow.Batches.SetBcrIdAsync(batch.BatchId, bcrId, _clock.UtcNow, ct);
            await uow.CommitAsync(ct);
        }

        // 3. Fetch and Stage Claims
        progress.Report(new WorkerProgressReport("StreamA", $"Fetching claims for batch {batch.BatchId}..."));

        var batchStartDate = batch.StartDateUtc ?? ParseMonthKey(batch.MonthKey);
        var batchEndDate = batch.EndDateUtc ?? batchStartDate.AddMonths(1).AddTicks(-1);

        int? lastSeen = null;
        const int PageSize = 100;

        // SQLite "database is locked" mitigation:
        // - Never hold a SQLite transaction open across awaited I/O (provider DB reads, CPU build, etc.)
        // - Persist in small, write-only transactions.
        const int MaxClaimsPerTx = 25;

        var builder = new ClaimBundleBuilder();
        int processedCount = 0;

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

            // Build payloads OUTSIDE any SQLite transaction.
            var workItems = new List<StageWorkItem>(keys.Count);

            foreach (var key in keys)
            {
                if (ct.IsCancellationRequested) break;

                var rawBundle = await _tablesAdapter.GetClaimBundleRawAsync(batch.ProviderDhsCode, key, ct);
                if (rawBundle == null)
                {
                    lastSeen = key;
                    continue;
                }

                var buildResult = builder.Build(new CanonicalClaimParts(
                    rawBundle.Header,
                    rawBundle.Services,
                    rawBundle.Diagnoses,
                    rawBundle.Labs,
                    rawBundle.Radiology,
                    rawBundle.OpticalVitalSigns
                ), batch.CompanyCode);

                byte[]? payloadBytes = null;
                string? sha256 = null;

                if (buildResult.Succeeded && buildResult.Bundle is not null)
                {
                    // Injected fields per your request
                    buildResult.Bundle.ClaimHeader["provider_dhsCode"] = batch.ProviderDhsCode;
                    if (long.TryParse(bcrId, out var bcrIdLong))
                        buildResult.Bundle.ClaimHeader["bCR_Id"] = bcrIdLong;

                    var payloadJson = buildResult.Bundle.ToJsonString();
                    payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                    sha256 = ComputeSha256(payloadBytes);
                }

                workItems.Add(new StageWorkItem(
                    ProIdClaim: key,
                    BuildResult: buildResult,
                    PayloadBytes: payloadBytes,
                    Sha256: sha256));

                processedCount++;
                lastSeen = key;
            }

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
        progress.Report(new WorkerProgressReport("StreamA", "Scanning for missing domain mappings..."));

         var domains = BaselineDomainScanner.GetBaselineDomains();
        var distinctValues = await _tablesAdapter.GetDistinctDomainValuesAsync(
            batch.ProviderDhsCode,
            batch.CompanyCode,
            batchStartDate,
            batchEndDate,
            domains,
            ct);

        if (distinctValues.Count > 0)
        {
            int discoveredInRun = 0;

            // Batch upserts to keep transactions short.
            const int MappingTxBatchSize = 200;
            var buffer = new List<ScannedDomainValue>(MappingTxBatchSize);

            foreach (var mm in distinctValues)
            {
                if (ct.IsCancellationRequested) break;

                buffer.Add(mm);

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

        // 5. Mark batch ready for sender
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Ready, true, null, _clock.UtcNow, ct);
            await uow.CommitAsync(ct);
        }

        progress.Report(new WorkerProgressReport("StreamA", $"Batch {batch.BatchId} staged successfully. {processedCount} claims processed."));
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

        if (eligible.Count == 0) return;

        progress.Report(new WorkerProgressReport("StreamA", $"Posting {eligible.Count} missing domain mappings for {providerDhsCode}..."));

        const int BatchSize = 500;
        for (int i = 0; i < eligible.Count; i += BatchSize)
        {
            if (ct.IsCancellationRequested) break;

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
