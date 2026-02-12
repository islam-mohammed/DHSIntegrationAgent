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
            {
                throw new InvalidOperationException($"Failed to create batch on backend: {result.ErrorMessage}");
            }

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
        int pageSize = 100;
        bool hasMore = true;
        int processedCount = 0;

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
                        // Injected fields as per user request
                       
                        buildResult.Bundle!.ClaimHeader["provider_dhsCode"] = batch.ProviderDhsCode;
                       
                        if (long.TryParse(bcrId, out var bcrIdLong))
                        {
                            buildResult.Bundle!.ClaimHeader["bCR_Id"] = bcrIdLong;
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

                        // Persist issues (diagnostics)
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

        // 4. Handle Missing Mappings
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

            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                foreach (var mm in distinctValues)
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
                progress.Report(new WorkerProgressReport("StreamA", $"DETECTED_MISSING_DOMAINS:{discoveredInRun}"));
            }
        }

        // 5. Update Batch Status to Ready (meaning staged and BcrId obtained, ready for Sender)
        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Ready, true, null, _clock.UtcNow, ct);
            await uow.CommitAsync(ct);
        }

        progress.Report(new WorkerProgressReport("StreamA", $"Batch {batch.BatchId} staged successfully. {processedCount} claims processed."));
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
            var items = itemsToPost.Select(x => new MismappedItem(x.SourceValue, x.ProviderNameValue ?? x.SourceValue, x.DomainTableId)).ToList();

            var request = new InsertMissMappingDomainRequest(providerDhsCode, items);

            try
            {
                var result = await _domainMappingClient.InsertMissMappingDomainAsync(request, ct);

                await using var uow = await _uowFactory.CreateAsync(ct);
                if (result.Succeeded)
                {
                    foreach (var row in itemsToPost)
                    {
                        await uow.DomainMappings.UpdateMissingStatusAsync(row.MissingMappingId, MappingStatus.Posted, _clock.UtcNow, _clock.UtcNow, ct);
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to post domain mapping batch for {ProviderDhsCode}: {Message}", providerDhsCode, result.Message);
                    foreach (var row in itemsToPost)
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
                foreach (var row in itemsToPost)
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
