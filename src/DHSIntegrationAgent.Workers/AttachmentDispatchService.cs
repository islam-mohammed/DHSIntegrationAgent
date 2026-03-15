using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Application.Abstractions;
using System.Collections.Concurrent;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Application.Security;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Domain.WorkStates;
using DHSIntegrationAgent.Adapters.Claims;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class AttachmentDispatchService : IAttachmentDispatchService
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IProviderTablesAdapter _tablesAdapter;
    private readonly IAttachmentService _attachmentService;
    private readonly IAttachmentClient _attachmentClient;
    private readonly ISystemClock _clock;
    private readonly ILogger<AttachmentDispatchService> _logger;

    public AttachmentDispatchService(
        ISqliteUnitOfWorkFactory uowFactory,
        IProviderTablesAdapter tablesAdapter,
        IAttachmentService attachmentService,
        IAttachmentClient attachmentClient,
        ISystemClock clock,
        ILogger<AttachmentDispatchService> logger)
    {
        _uowFactory = uowFactory;
        _tablesAdapter = tablesAdapter;
        _attachmentService = attachmentService;
        _attachmentClient = attachmentClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task ProcessAttachmentsForBatchesAsync(IEnumerable<long> batchIds, IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        var tasks = batchIds.Select(id => ProcessSingleBatchAttachmentsAsync(id, progress, ct));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessSingleBatchAttachmentsAsync(long batchId, IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        try
        {
            BatchRow? batch;
            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                batch = await uow.Batches.GetByIdAsync(batchId, ct);
            }
            if (batch == null) return;

            var startDate = batch.StartDateUtc ?? ParseMonthKey(batch.MonthKey);
            var endDate = batch.EndDateUtc ?? startDate.AddMonths(1).AddTicks(-1);

            _logger.LogInformation("Fetching attachments for batch {BatchId}...", batchId);

            // Preparation: Fetch all raw attachments directly
            var rawAttachments = await _tablesAdapter.GetAttachmentsForBatchAsync(
                batch.ProviderDhsCode,
                batch.CompanyCode,
                startDate,
                endDate,
                ct);

            if (rawAttachments.Count == 0)
            {
                progress.Report(new WorkerProgressReport("StreamC", "No attachments found for batch", BatchId: batchId, Percentage: 100));
                return;
            }

            // Fetch existing attachments for the batch to detect already uploaded ones
            IReadOnlyList<AttachmentRow> existingAttachments;
            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                existingAttachments = await uow.Attachments.GetByBatchAsync(batchId, ct);
            }

            var uploadedAttachments = existingAttachments
                .Where(a => a.UploadStatus == UploadStatus.Uploaded && !string.IsNullOrWhiteSpace(a.OnlineUrlPlaintext))
                .ToDictionary(a => a.AttachmentId, a => a);

            // Map and Group by ProIdClaim
            var claimGroups = rawAttachments
                .Select(att => (Att: att, Row: AttachmentMapper.MapToAttachmentRow(batch.ProviderDhsCode, GetProIdClaim(att), att, _clock.UtcNow)))
                .Where(x => x.Row != null)
                .GroupBy(x => x.Row!.ProIdClaim)
                .ToList();

            int allMappedAttachments = claimGroups.Sum(g => g.Count());
            int totalClaimsWithAttachments = claimGroups.Count;

            // Calculate how many need to actually be uploaded
            int alreadyUploadedCount = claimGroups.SelectMany(g => g).Count(x => uploadedAttachments.ContainsKey(x.Row!.AttachmentId));
            int totalAttachmentsToUpload = allMappedAttachments - alreadyUploadedCount;

            int uploadedCount = alreadyUploadedCount;
            int notifiedClaimsCount = 0;

            if (totalAttachmentsToUpload == 0)
            {
                _logger.LogInformation("All {AttachmentCount} attachments for {ClaimCount} claims in batch {BatchId} are already uploaded.", allMappedAttachments, totalClaimsWithAttachments, batchId);
                // We'll skip reporting the "0 of 0" uploading progress.
            }
            else
            {
                _logger.LogInformation("Processing {AttachmentCount} remaining attachments for {ClaimCount} claims in batch {BatchId} ({AlreadyUploaded} already uploaded)", totalAttachmentsToUpload, totalClaimsWithAttachments, batchId, alreadyUploadedCount);
            }

            // Stage 1: Upload & Update Payload (0% -> 70%)
            var claimAttachmentDetails = new ConcurrentDictionary<int, List<(AttachmentRow Row, string? Remarks, string OnlineUrl, bool IsNew)>>();
            int processedAttachmentsCount = 0;
            int failedAttachmentsCount = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 10,
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(claimGroups, parallelOptions, async (group, cancellationToken) =>
            {
                var proIdClaim = group.Key;
                var attachmentInfos = new List<(AttachmentRow Row, string? Remarks, string OnlineUrl, bool IsNew)>();

                foreach (var (att, initialRow) in group)
                {
                    var row = initialRow;

                    // Extract remarks
                    string? remarks = null;
                    if (att.TryGetPropertyValue("remarks", out var remNode) && remNode != null)
                    {
                        remarks = remNode.ToString().Trim('"');
                    }

                    if (uploadedAttachments.TryGetValue(row!.AttachmentId, out var existingUploadedRow))
                    {
                        // Already uploaded, skip uploading and just add to infos
                        attachmentInfos.Add((existingUploadedRow, remarks, existingUploadedRow.OnlineUrlPlaintext!, false));
                        continue; // go to next attachment
                    }

                    // Upsert Staged
                    await using (var uow = await _uowFactory.CreateAsync(cancellationToken))
                    {
                        await uow.Attachments.UpsertAsync(row!, cancellationToken);
                        await uow.CommitAsync(cancellationToken);
                    }

                    try
                    {
                        // Upload
                        (var onlineUrl, var sizeBytes) = await _attachmentService.UploadAsync(row!, cancellationToken);

                        // Update local row with new size
                        row = row! with { SizeBytes = sizeBytes };

                        // Update Uploaded
                        await using (var uow = await _uowFactory.CreateAsync(cancellationToken))
                        {
                            await uow.Attachments.UpdateStatusAsync(row!.AttachmentId, UploadStatus.Uploaded, onlineUrl, null, _clock.UtcNow, null, cancellationToken, sizeBytes);
                            await uow.CommitAsync(cancellationToken);
                        }

                        attachmentInfos.Add((row!, remarks, onlineUrl, true));
                        Interlocked.Increment(ref uploadedCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to upload attachment {AttachmentId}", row!.AttachmentId);
                        Interlocked.Increment(ref failedAttachmentsCount);
                        await using (var uow = await _uowFactory.CreateAsync(cancellationToken))
                        {
                            await uow.Attachments.UpdateStatusAsync(row!.AttachmentId, UploadStatus.Failed, null, ex.Message, _clock.UtcNow, TimeSpan.FromMinutes(5), cancellationToken);
                            await uow.CommitAsync(cancellationToken);
                        }
                    }
                    finally
                    {
                        int processed = Interlocked.Increment(ref processedAttachmentsCount);
                        int failed = Volatile.Read(ref failedAttachmentsCount);
                        int succeeded = Volatile.Read(ref uploadedCount) - alreadyUploadedCount; // Current actual success

                        // Report Progress (0-70%) based ONLY on the items we are actually uploading
                        if (totalAttachmentsToUpload > 0)
                        {
                            double percentage = (double)processed / totalAttachmentsToUpload * 70.0;
                            progress.Report(new WorkerProgressReport("StreamC",
                                $"Uploading attachments: {processed} of {totalAttachmentsToUpload}" + (failed > 0 ? $" ({failed} failed)" : ""),
                                BatchId: batchId,
                                Percentage: percentage,
                                ProcessedCount: succeeded,
                                TotalCount: totalAttachmentsToUpload,
                                FailedCount: failed));
                        }
                    }
                }

                if (attachmentInfos.Count > 0)
                {
                    claimAttachmentDetails[proIdClaim] = attachmentInfos;
                    var uploadInfos = attachmentInfos.Select(x => (x.Row, x.Remarks, x.OnlineUrl)).ToList();
                    await UpdateClaimPayloadAsync(new ClaimKey(batch.ProviderDhsCode, proIdClaim), uploadInfos, cancellationToken);
                }
            });

            // Stage 2: Send Notification (70% -> 100%)
            int claimsToNotify = claimAttachmentDetails.Count;
            int notifiedAttachmentsCount = uploadedCount;

            if (claimsToNotify == 0)
            {
                progress.Report(new WorkerProgressReport("StreamC", "Attachment processing complete. No successful uploads to notify.",
                    BatchId: batchId,
                    Percentage: 100,
                    ProcessedCount: uploadedCount,
                    TotalCount: totalAttachmentsToUpload > 0 ? totalAttachmentsToUpload : allMappedAttachments,
                    FailedCount: failedAttachmentsCount));
                return;
            }

            await Parallel.ForEachAsync(claimAttachmentDetails, parallelOptions, async (kvp, cancellationToken) =>
            {
                var proIdClaim = kvp.Key;
                var attachmentInfos = kvp.Value;

                var attachmentDtos = new List<AttachmentDto>();
                foreach(var info in attachmentInfos)
                {
                     attachmentDtos.Add(new AttachmentDto(
                        AttachmentType: info.Row.ContentType ?? "application/octet-stream",
                        FileSizeInByte: info.Row.SizeBytes ?? 0,
                        OnlineURL: info.OnlineUrl,
                        Remarks: info.Remarks,
                        Location: info.Row.LocationPathPlaintext,
                        ContentType: info.Row.ContentType ?? "application/octet-stream"
                    ));
                }

                // Notify Backend
                var request = new UploadAttachmentRequest(proIdClaim, attachmentDtos);
                var result = await _attachmentClient.SendAttachmentAsync(request, cancellationToken);

                if (!result.Succeeded)
                {
                    int affectedCount = totalAttachmentsToUpload > 0 ? attachmentInfos.Count(x => x.IsNew) : attachmentInfos.Count;
                    Interlocked.Add(ref failedAttachmentsCount, affectedCount);
                    Interlocked.Add(ref notifiedAttachmentsCount, -affectedCount);
                    _logger.LogWarning("Failed to notify backend about attachments for claim {ProIdClaim}: {Error}", proIdClaim, result.ErrorMessage);
                }

                int notified = Interlocked.Increment(ref notifiedClaimsCount);
                int currentFailedAttachments = Volatile.Read(ref failedAttachmentsCount);
                int currentNotifiedAttachments = Volatile.Read(ref notifiedAttachmentsCount);

                // Report Progress (70% -> 100%)
                double percentage = 70.0 + ((double)notified / claimsToNotify * 30.0);

                int reportedTotalCount = totalAttachmentsToUpload > 0 ? totalAttachmentsToUpload : allMappedAttachments;
                // If we are showing remaining attachments, we should also calculate currentNotifiedAttachments correctly.
                // We'll show the actual success count of *newly* processed items.
                int reportedProcessedCount = totalAttachmentsToUpload > 0 ? currentNotifiedAttachments - alreadyUploadedCount : currentNotifiedAttachments;
                if (reportedProcessedCount < 0) reportedProcessedCount = 0;

                // We report attachment counts so the UI denominator (Total Attachments) remains consistent
                progress.Report(new WorkerProgressReport("StreamC",
                    $"Sending attachment notifications: {notified} of {claimsToNotify}",
                    BatchId: batchId,
                    Percentage: percentage,
                    ProcessedCount: reportedProcessedCount,
                    TotalCount: reportedTotalCount,
                    FailedCount: currentFailedAttachments));
            });

            progress.Report(new WorkerProgressReport("StreamC", "Attachment processing complete",
                BatchId: batchId,
                Percentage: 100,
                ProcessedCount: totalAttachmentsToUpload > 0 ? notifiedAttachmentsCount - alreadyUploadedCount : notifiedAttachmentsCount,
                TotalCount: totalAttachmentsToUpload > 0 ? totalAttachmentsToUpload : allMappedAttachments,
                FailedCount: failedAttachmentsCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process attachments for batch {BatchId}", batchId);
            progress.Report(new WorkerProgressReport("StreamC", $"Error: {ex.Message}", BatchId: batchId, IsError: true));
        }
    }

    private int GetProIdClaim(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("ProIdClaim", out var node) && node != null && int.TryParse(node.ToString(), out var val))
            return val;
        // Fallback for case sensitivity or schema variations if necessary, though SQL query usually returns standard casing
        if (obj.TryGetPropertyValue("proIdClaim", out node) && node != null && int.TryParse(node.ToString(), out val))
            return val;
        return 0;
    }

    private async Task UpdateClaimPayloadAsync(ClaimKey key, List<(AttachmentRow Row, string? Remarks, string OnlineUrl)> attachmentInfos, CancellationToken ct)
    {
        await using var uow = await _uowFactory.CreateAsync(ct);
        var payloadRow = await uow.ClaimPayloads.GetAsync(key, ct);
        if (payloadRow == null) return;

        var node = JsonNode.Parse(payloadRow.PayloadJsonPlaintext);
        if (node is not JsonObject bundleObj) return;

        var attachmentsArray = bundleObj["attachments"] as JsonArray ?? new JsonArray();

        foreach (var info in attachmentInfos)
        {
            var attId = info.Row.AttachmentId;
            var url = info.OnlineUrl;

            var existing = attachmentsArray.FirstOrDefault(x => x?["attachmentId"]?.ToString() == attId) as JsonObject;
            if (existing != null)
            {
                existing["onlineUrl"] = url;
            }
            else
            {
                attachmentsArray.Add(new JsonObject
                {
                    ["attachmentId"] = attId,
                    ["fileName"] = info.Row.FileName,
                    ["contentType"] = info.Row.ContentType,
                    ["sizeBytes"] = info.Row.SizeBytes,
                    ["onlineUrl"] = url
                });
            }
        }

        bundleObj["attachments"] = attachmentsArray;
        var updatedJson = bundleObj.ToJsonString();
        var updatedBytes = System.Text.Encoding.UTF8.GetBytes(updatedJson);
        var updatedSha = ComputeSha256(updatedBytes);

        // Update local DB
        await uow.ClaimPayloads.UpsertAsync(payloadRow with { PayloadJsonPlaintext = updatedBytes, PayloadSha256 = updatedSha }, ct);
        await uow.CommitAsync(ct);
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<int> CountAttachmentsAsync(BatchRow batch, CancellationToken ct)
    {
        var startDate = batch.StartDateUtc ?? ParseMonthKey(batch.MonthKey);
        var endDate = batch.EndDateUtc ?? startDate.AddMonths(1).AddTicks(-1);

        return await _tablesAdapter.CountAttachmentsAsync(
            batch.ProviderDhsCode,
            batch.CompanyCode,
            startDate,
            endDate,
            ct);
    }

    private DateTimeOffset ParseMonthKey(string monthKey)
    {
        if (string.IsNullOrWhiteSpace(monthKey) || monthKey.Length != 6
            || !int.TryParse(monthKey.Substring(0, 4), out var year)
            || !int.TryParse(monthKey.Substring(4, 2), out var month))
        {
            throw new ArgumentException($"Invalid MonthKey format: '{monthKey}'. Expected YYYYMM.", nameof(monthKey));
        }
        return new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
