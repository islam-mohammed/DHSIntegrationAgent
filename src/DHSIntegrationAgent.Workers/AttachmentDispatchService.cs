using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Tables;
using DHSIntegrationAgent.Application.Abstractions;
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
            IReadOnlyList<ClaimKey> claimKeys;
            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                batch = await uow.Batches.GetByIdAsync(batchId, ct);
                if (batch == null) return;
                claimKeys = await uow.Claims.ListByBatchAsync(batchId, ct);
            }

            if (claimKeys.Count == 0)
            {
                progress.Report(new WorkerProgressReport("StreamC", "No claims found in batch", BatchId: batchId, Percentage: 100));
                return;
            }

            _logger.LogInformation("Processing attachments for batch {BatchId} ({ClaimCount} claims)", batchId, claimKeys.Count);

            int processedClaims = 0;
            foreach (var key in claimKeys)
            {
                if (ct.IsCancellationRequested) break;

                // 1. Fetch attachments from HIS for this claim
                var rawBundle = await _tablesAdapter.GetClaimBundleRawAsync(key.ProviderDhsCode, key.ProIdClaim, ct);
                if (rawBundle?.Attachments != null && rawBundle.Attachments.Count > 0)
                {
                    var onlineUrls = new List<(string AttachmentId, string OnlineUrl)>();

                    foreach (var attNode in rawBundle.Attachments)
                    {
                        if (attNode is not JsonObject attObj) continue;

                        var row = AttachmentMapper.MapToAttachmentRow(key.ProviderDhsCode, key.ProIdClaim, attObj, _clock.UtcNow);
                        if (row == null) continue;

                        // Check if already uploaded
                        // For simplicity, we just try to upload if status is not Uploaded in our DB
                        // But wait, we might not even have it in our DB yet.

                        await using (var uow = await _uowFactory.CreateAsync(ct))
                        {
                            await uow.Attachments.UpsertAsync(row, ct);
                            await uow.CommitAsync(ct);
                        }

                        try
                        {
                            var onlineUrl = await _attachmentService.UploadAsync(row, ct);
                            await using (var uow = await _uowFactory.CreateAsync(ct))
                            {
                                await uow.Attachments.UpdateStatusAsync(row.AttachmentId, UploadStatus.Uploaded, onlineUrl, null, _clock.UtcNow, null, ct);
                                await uow.CommitAsync(ct);
                            }
                            onlineUrls.Add((row.AttachmentId, onlineUrl));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to upload attachment {AttachmentId}", row.AttachmentId);
                            await using (var uow = await _uowFactory.CreateAsync(ct))
                            {
                                await uow.Attachments.UpdateStatusAsync(row.AttachmentId, UploadStatus.Failed, null, ex.Message, _clock.UtcNow, TimeSpan.FromMinutes(5), ct);
                                await uow.CommitAsync(ct);
                            }
                        }
                    }

                    if (onlineUrls.Count > 0)
                    {
                        // 2. Update ClaimPayload JSON and Notify Backend
                        await UpdateClaimAndNotifyBackendAsync(key, onlineUrls, ct);
                    }
                }

                processedClaims++;
                if (processedClaims % 5 == 0 || processedClaims == claimKeys.Count)
                {
                    double percentage = 10 + ((double)processedClaims / claimKeys.Count * 90);
                    progress.Report(new WorkerProgressReport("StreamC", $"Uploading attachments: {processedClaims}/{claimKeys.Count} claims", BatchId: batchId, Percentage: percentage, ProcessedCount: processedClaims, TotalCount: claimKeys.Count));
                }
            }

            progress.Report(new WorkerProgressReport("StreamC", "Attachment processing complete", BatchId: batchId, Percentage: 100));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process attachments for batch {BatchId}", batchId);
            progress.Report(new WorkerProgressReport("StreamC", $"Error: {ex.Message}", BatchId: batchId, IsError: true));
        }
    }

    private async Task UpdateClaimAndNotifyBackendAsync(ClaimKey key, List<(string AttachmentId, string OnlineUrl)> onlineUrls, CancellationToken ct)
    {
        await using var uow = await _uowFactory.CreateAsync(ct);
        var payloadRow = await uow.ClaimPayloads.GetAsync(key, ct);
        if (payloadRow == null) return;

        var node = JsonNode.Parse(payloadRow.PayloadJsonPlaintext);
        if (node is not JsonObject bundleObj) return;

        var attachmentsArray = bundleObj["attachments"] as JsonArray ?? new JsonArray();

        foreach (var (attId, url) in onlineUrls)
        {
            // Try to find existing attachment in JSON
            var existing = attachmentsArray.FirstOrDefault(x => x?["attachmentId"]?.ToString() == attId) as JsonObject;
            if (existing != null)
            {
                existing["onlineUrl"] = url;
            }
            else
            {
                var attRows = await uow.Attachments.GetByClaimAsync(key.ProviderDhsCode, key.ProIdClaim, ct);
                var row = attRows.FirstOrDefault(r => r.AttachmentId == attId);
                if (row != null)
                {
                    attachmentsArray.Add(new JsonObject
                    {
                        ["attachmentId"] = row.AttachmentId,
                        ["fileName"] = row.FileName,
                        ["contentType"] = row.ContentType,
                        ["sizeBytes"] = row.SizeBytes,
                        ["onlineUrl"] = url
                    });
                }
            }
        }

        bundleObj["attachments"] = attachmentsArray;
        var updatedJson = bundleObj.ToJsonString();
        var updatedBytes = System.Text.Encoding.UTF8.GetBytes(updatedJson);
        var updatedSha = ComputeSha256(updatedBytes);

        // 1. Update local DB
        await uow.ClaimPayloads.UpsertAsync(payloadRow with { PayloadJsonPlaintext = updatedBytes, PayloadSha256 = updatedSha }, ct);
        await uow.CommitAsync(ct);

        // 2. Notify Backend
        var result = await _attachmentClient.UpdateAttachmentsAsync(key.ProviderDhsCode, key.ProIdClaim, attachmentsArray.ToJsonString(), ct);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Failed to notify backend about attachments for claim {ProIdClaim}: {Error}", key.ProIdClaim, result.ErrorMessage);
        }
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

}
