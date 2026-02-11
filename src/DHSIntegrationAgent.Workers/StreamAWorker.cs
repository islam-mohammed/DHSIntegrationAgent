using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DHSIntegrationAgent.Adapters.Claims;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Providers;
using DHSIntegrationAgent.Contracts.Claims;
using DHSIntegrationAgent.Contracts.DomainMapping;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Domain.WorkStates;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class StreamAWorker : IWorker
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IProviderTablesAdapter _tablesAdapter;
    private readonly IBatchClient _batchClient;
    private readonly IDomainMappingClient _domainMappingClient;
    private readonly BaselineDomainScanner _scanner;
    private readonly ISystemClock _clock;
    private readonly ILogger<StreamAWorker> _logger;

    public string Id => "StreamA";
    public string DisplayName => "Stream A: Fetch & Stage";

    public StreamAWorker(
        ISqliteUnitOfWorkFactory uowFactory,
        IProviderTablesAdapter tablesAdapter,
        IBatchClient batchClient,
        IDomainMappingClient domainMappingClient,
        BaselineDomainScanner scanner,
        ISystemClock clock,
        ILogger<StreamAWorker> logger)
    {
        _uowFactory = uowFactory;
        _tablesAdapter = tablesAdapter;
        _batchClient = batchClient;
        _domainMappingClient = domainMappingClient;
        _scanner = scanner;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        _logger.LogInformation("Stream A worker started.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessReadyBatchesAsync(progress, ct);
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stream A worker loop encountered an error.");
                progress.Report(new WorkerProgressReport(Id, $"Error: {ex.Message}", IsError: true));
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }

        _logger.LogInformation("Stream A worker stopped.");
    }

    private async Task ProcessReadyBatchesAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        IReadOnlyList<BatchRow> readyBatches;
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            readyBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Ready, ct);
        }

        foreach (var batch in readyBatches)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ProcessBatchAsync(batch, progress, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process batch {BatchId}.", batch.BatchId);
                await using var uow = await _uowFactory.CreateAsync(ct);
                await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Failed, null, ex.Message, _clock.UtcNow, ct);
                await uow.CommitAsync(ct);
            }
        }
    }

    private async Task ProcessBatchAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        _logger.LogInformation("Processing batch {BatchId} for {CompanyCode} {MonthKey}", batch.BatchId, batch.CompanyCode, batch.MonthKey);

        string? bcrId = batch.BcrId;

        // 1. Ensure BcrId exists
        if (string.IsNullOrWhiteSpace(bcrId))
        {
            progress.Report(new WorkerProgressReport(Id, $"Obtaining BcrId for batch {batch.BatchId}..."));

            // Need total claims count for CreateBatchRequest
            var startDate = batch.StartDateUtc ?? ParseMonthKey(batch.MonthKey);
            var endDate = batch.EndDateUtc ?? startDate.AddMonths(1).AddTicks(-1);
            var totalClaims = await _tablesAdapter.CountClaimsAsync(batch.ProviderDhsCode, batch.CompanyCode, startDate, endDate, ct);

            var request = new CreateBatchRequestItem(batch.CompanyCode, startDate, endDate, totalClaims, batch.ProviderDhsCode);
            var result = await _batchClient.CreateBatchAsync(new[] { request }, ct);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create batch on backend: {result.ErrorMessage}");
            }

            bcrId = result.BatchId.ToString();
            await using var uow = await _uowFactory.CreateAsync(ct);
            await uow.Batches.SetBcrIdAsync(batch.BatchId, bcrId, _clock.UtcNow, ct);
            await uow.CommitAsync(ct);
        }

        // 2. Fetch and Stage Claims
        progress.Report(new WorkerProgressReport(Id, $"Fetching claims for batch {batch.BatchId}..."));

        var batchStartDate = batch.StartDateUtc ?? ParseMonthKey(batch.MonthKey);
        var batchEndDate = batch.EndDateUtc ?? batchStartDate.AddMonths(1).AddTicks(-1);

        int? lastSeen = null;
        int pageSize = 100;
        bool hasMore = true;
        int processedCount = 0;

        var missingMappings = new HashSet<ScannedDomainValue>();

        while (hasMore)
        {
            var keys = await _tablesAdapter.ListClaimKeysAsync(batch.ProviderDhsCode, batch.CompanyCode, batchStartDate, batchEndDate, pageSize, lastSeen, ct);
            if (keys.Count == 0)
            {
                hasMore = false;
                break;
            }

            foreach (var key in keys)
            {
                var rawBundle = await _tablesAdapter.GetClaimBundleRawAsync(batch.ProviderDhsCode, key, ct);
                if (rawBundle == null) continue;

                var builder = new ClaimBundleBuilder();
                var buildResult = builder.Build(new CanonicalClaimParts(
                    rawBundle.Header,
                    rawBundle.Services,
                    rawBundle.Diagnoses,
                    rawBundle.Labs,
                    rawBundle.Radiology,
                    rawBundle.OpticalVitalSigns
                ), batch.CompanyCode);

                if (buildResult.Succeeded)
                {
                    // Scan for missing mappings
                    var scannedValues = _scanner.Scan(buildResult.Bundle!);
                    foreach (var sv in scannedValues)
                    {
                        missingMappings.Add(sv);
                    }

                    // Stage locally
                    await using var uow = await _uowFactory.CreateAsync(ct);
                    await uow.Claims.UpsertStagedAsync(
                        batch.ProviderDhsCode,
                        key,
                        batch.CompanyCode,
                        batch.MonthKey,
                        batch.BatchId,
                        bcrId,
                        _clock.UtcNow,
                        ct);

                    // Note: ClaimPayload storage usually happens in ClaimBundleBuilder or another service,
                    // but based on IClaimPayloadRepository, we should do it here if not already done.
                    var payloadJson = buildResult.Bundle!.ToJsonString();
                    var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                    var sha256 = ComputeSha256(payloadBytes);

                    await uow.ClaimPayloads.UpsertAsync(
                        new ClaimPayloadRow(
                            new ClaimKey(batch.ProviderDhsCode, key),
                            payloadBytes,
                            sha256,
                            1,
                            _clock.UtcNow,
                            _clock.UtcNow),
                        ct);

                    await uow.CommitAsync(ct);
                }

                processedCount++;
                lastSeen = key;
            }

            if (keys.Count < pageSize) hasMore = false;
        }

        // 3. Handle Missing Mappings
        if (missingMappings.Count > 0)
        {
            progress.Report(new WorkerProgressReport(Id, $"Scanning for missing domain mappings..."));

            var itemsToPost = new List<MismappedItem>();

            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                foreach (var mm in missingMappings)
                {
                    // Filter: Only post if NOT already in approved or missing tables
                    if (await uow.DomainMappings.ExistsAsync(batch.ProviderDhsCode, mm.DomainTableId, mm.Value, ct))
                    {
                        continue;
                    }

                    await uow.DomainMappings.UpsertDiscoveredAsync(
                        batch.ProviderDhsCode,
                        mm.DomainName,
                        mm.DomainTableId,
                        mm.Value,
                        DiscoverySource.Scanned,
                        _clock.UtcNow,
                        ct,
                        providerNameValue: mm.Value,
                        domainTableName: mm.DomainName);

                    itemsToPost.Add(new MismappedItem(mm.Value, mm.Value, mm.DomainTableId));
                }
                await uow.CommitAsync(ct);
            }

            if (itemsToPost.Count > 0)
            {
                // Notify UI about missing domains (this is part of the requirement)
                progress.Report(new WorkerProgressReport(Id, $"DETECTED_MISSING_DOMAINS:{itemsToPost.Count}"));

                // Post missing mappings to API
                progress.Report(new WorkerProgressReport(Id, $"Posting {itemsToPost.Count} missing domain mappings..."));
                await _domainMappingClient.InsertMissMappingDomainAsync(new InsertMissMappingDomainRequest(batch.ProviderDhsCode, itemsToPost), ct);
            }
        }

        // 4. Update Batch Status to Enqueued (meaning staged and ready for Sender)
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Enqueued, true, null, _clock.UtcNow, ct);
            await uow.CommitAsync(ct);
        }

        progress.Report(new WorkerProgressReport(Id, $"Batch {batch.BatchId} staged successfully. {processedCount} claims processed."));
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private DateTimeOffset ParseMonthKey(string monthKey)
    {
        if (monthKey.Length != 6 || !int.TryParse(monthKey.Substring(0, 4), out var year) || !int.TryParse(monthKey.Substring(4, 2), out var month))
        {
            throw new ArgumentException("Invalid MonthKey format. Expected YYYYMM.", nameof(monthKey));
        }
        return new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
