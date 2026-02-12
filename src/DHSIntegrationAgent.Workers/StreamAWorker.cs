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
        IReadOnlyList<BatchRow> batchesToProcess;
        var providerCodes = new HashSet<string>();

        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            // Stream A processes Draft batches (newly created) and Ready batches (manually triggered or BCR ensure needed).
            var draftBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Draft, ct);
            var readyBatches = await uow.Batches.ListByStatusAsync(BatchStatus.Ready, ct);

            var all = new List<BatchRow>(draftBatches.Count + readyBatches.Count);
            all.AddRange(draftBatches);
            all.AddRange(readyBatches);
            batchesToProcess = all;
        }

        foreach (var batch in batchesToProcess)
        {
            if (ct.IsCancellationRequested) break;

            providerCodes.Add(batch.ProviderDhsCode);

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

        // Parallel/Non-blocking posting of missing mappings
        foreach (var providerCode in providerCodes)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await PostMissingMappingsAsync(providerCode, progress, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background mapping poster failed for provider {ProviderDhsCode}", providerCode);
                }
            }, ct);
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

            ISqliteUnitOfWork? uow = null;
            int claimsInTx = 0;
            const int MaxClaimsPerTx = 100;

            try
            {
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

                    if (uow == null) uow = await _uowFactory.CreateAsync(ct);

                    if (buildResult.Succeeded)
                    {
                        // Scan for missing mappings
                        var scannedValues = _scanner.Scan(buildResult.Bundle!);
                        foreach (var sv in scannedValues)
                        {
                            missingMappings.Add(sv);
                        }

                        // Stage locally
                        await uow.Claims.UpsertStagedAsync(
                            batch.ProviderDhsCode,
                            key,
                            batch.CompanyCode,
                            batch.MonthKey,
                            batch.BatchId,
                            bcrId,
                            _clock.UtcNow,
                            ct);

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

                        // Persist issues
                        foreach (var issue in buildResult.Issues)
                        {
                            await uow.ValidationIssues.InsertAsync(new ValidationIssueRow(
                                batch.ProviderDhsCode,
                                buildResult.ProIdClaim,
                                issue.IssueType,
                                issue.FieldPath,
                                issue.RawValue,
                                issue.Message,
                                issue.IsBlocking,
                                _clock.UtcNow
                            ), ct);
                        }
                    }
                    else
                    {
                        // Record blocking issues even if build failed
                        foreach (var issue in buildResult.Issues)
                        {
                            await uow.ValidationIssues.InsertAsync(new ValidationIssueRow(
                                batch.ProviderDhsCode,
                                key,
                                issue.IssueType,
                                issue.FieldPath,
                                issue.RawValue,
                                issue.Message,
                                issue.IsBlocking,
                                _clock.UtcNow
                            ), ct);
                        }
                    }

                    processedCount++;
                    lastSeen = key;

                    claimsInTx++;
                    if (claimsInTx >= MaxClaimsPerTx)
                    {
                        await uow.CommitAsync(ct);
                        await uow.DisposeAsync();
                        uow = null;
                        claimsInTx = 0;
                    }
                }
            }
            finally
            {
                if (uow != null)
                {
                    await uow.CommitAsync(ct);
                    await uow.DisposeAsync();
                }
            }

            if (keys.Count < pageSize) hasMore = false;
        }

        // 3. Handle Missing Mappings
        if (missingMappings.Count > 0)
        {
            progress.Report(new WorkerProgressReport(Id, $"Scanning for missing domain mappings..."));

            int discoveredInRun = 0;

            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                foreach (var mm in missingMappings)
                {
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

                    discoveredInRun++;
                }
                await uow.CommitAsync(ct);
            }

            if (discoveredInRun > 0)
            {
                progress.Report(new WorkerProgressReport(Id, $"DETECTED_MISSING_DOMAINS:{discoveredInRun}"));
            }
        }

        // 4. Update Batch Status to Ready (meaning staged and BcrId obtained, ready for Sender)
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Ready, true, null, _clock.UtcNow, ct);
            await uow.CommitAsync(ct);
        }

        progress.Report(new WorkerProgressReport(Id, $"Batch {batch.BatchId} staged successfully. {processedCount} claims processed."));
    }

    private async Task PostMissingMappingsAsync(string providerDhsCode, IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        _logger.LogInformation("Checking for missing domain mappings to post for provider {ProviderDhsCode}...", providerDhsCode);

        IReadOnlyList<MissingDomainMappingRow> eligible;
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            eligible = await uow.DomainMappings.ListEligibleForPostingAsync(providerDhsCode, ct);
        }

        if (eligible.Count == 0) return;

        progress.Report(new WorkerProgressReport(Id, $"Posting {eligible.Count} missing domain mappings for {providerDhsCode}..."));

        const int BatchSize = 500;
        for (int i = 0; i < eligible.Count; i += BatchSize)
        {
            if (ct.IsCancellationRequested) break;

            var batch = eligible.Skip(i).Take(BatchSize).ToList();
            var items = batch.Select(x => new MismappedItem(x.SourceValue, x.ProviderNameValue ?? x.SourceValue, x.DomainTableId)).ToList();

            var request = new InsertMissMappingDomainRequest(providerDhsCode, items);

            try
            {
                var result = await _domainMappingClient.InsertMissMappingDomainAsync(request, ct);

                await using var uow = await _uowFactory.CreateAsync(ct);
                if (result.Succeeded)
                {
                    foreach (var row in batch)
                    {
                        await uow.DomainMappings.UpdateMissingStatusAsync(row.MissingMappingId, MappingStatus.Posted, _clock.UtcNow, _clock.UtcNow, ct);
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to post domain mapping batch for {ProviderDhsCode}: {Message}", providerDhsCode, result.Message);
                    foreach (var row in batch)
                    {
                        await uow.DomainMappings.UpdateMissingStatusAsync(row.MissingMappingId, MappingStatus.PostFailed, _clock.UtcNow, null, ct);
                    }
                }
                await uow.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error posting domain mapping batch for {ProviderDhsCode}", providerDhsCode);
                await using var uow = await _uowFactory.CreateAsync(ct);
                foreach (var row in batch)
                {
                    await uow.DomainMappings.UpdateMissingStatusAsync(row.MissingMappingId, MappingStatus.PostFailed, _clock.UtcNow, null, ct);
                }
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
        if (monthKey.Length != 6 || !int.TryParse(monthKey.Substring(0, 4), out var year) || !int.TryParse(monthKey.Substring(4, 2), out var month))
        {
            throw new ArgumentException("Invalid MonthKey format. Expected YYYYMM.", nameof(monthKey));
        }
        return new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
