using System.Text.Json;
using System.Text.Json.Nodes;
using DHSIntegrationAgent.Adapters.Claims;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Application.Persistence;
using DHSIntegrationAgent.Contracts.Claims;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Domain.Claims;
using DHSIntegrationAgent.Domain.WorkStates;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Workers;

public sealed class DispatchService : IDispatchService
{
    private readonly ISqliteUnitOfWorkFactory _uowFactory;
    private readonly IClaimsClient _claimsClient;
    private readonly ISystemClock _clock;
    private readonly ILogger<DispatchService> _logger;

    public DispatchService(
        ISqliteUnitOfWorkFactory uowFactory,
        IClaimsClient claimsClient,
        ISystemClock clock,
        ILogger<DispatchService> logger)
    {
        _uowFactory = uowFactory;
        _claimsClient = claimsClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task ProcessBatchSenderAsync(BatchRow batch, IProgress<WorkerProgressReport> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(batch.BcrId))
        {
            _logger.LogWarning("Batch {BatchId} has no BcrId. Skipping sender.", batch.BatchId);
            return;
        }

        int totalClaimsInBatch = 0;
        int enqueuedCount = 0;

        await using (var uow = await _uowFactory.CreateAsync(ct))
        {
            var counts = await uow.Claims.GetBatchCountsAsync(batch.BatchId, ct);
            totalClaimsInBatch = counts.Total;
            enqueuedCount = counts.Enqueued;
        }

        // Report initial progress
        double initialPercentage = totalClaimsInBatch > 0 ? 55 + (((double)enqueuedCount / totalClaimsInBatch) * 45) : 100;
        progress.Report(new WorkerProgressReport(
            "StreamB",
            $"Sending claims {enqueuedCount} of {totalClaimsInBatch} claims",
            Percentage: initialPercentage,
            BatchId: batch.BatchId,
            ProcessedCount: enqueuedCount,
            TotalCount: totalClaimsInBatch));

        int packetCount = 0;

        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<ClaimKey> leased;
            int nextSeq;

            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                var leaseRequest = new ClaimLeaseRequest(
                    ProviderDhsCode: batch.ProviderDhsCode,
                    LockedBy: "Sender",
                    UtcNow: _clock.UtcNow,
                    LeaseUntilUtc: _clock.UtcNow.AddMinutes(5),
                    Take: 40,
                    EligibleEnqueueStatuses: new[] { EnqueueStatus.NotSent },
                    RequireRetryDue: false,
                    BatchId: batch.BatchId
                );

                leased = await uow.Claims.LeaseAsync(leaseRequest, ct);
                if (leased.Count == 0)
                {
                    if (batch.BatchStatus != BatchStatus.Enqueued)
                    {
                        await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Enqueued, null, null, _clock.UtcNow, ct);
                    }
                    progress.Report(new WorkerProgressReport(
                        "StreamB",
                        "Batch processing complete.",
                        Percentage: 100,
                        BatchId: batch.BatchId,
                        ProcessedCount: enqueuedCount,
                        TotalCount: totalClaimsInBatch));
                    await uow.CommitAsync(ct);
                    break;
                }

                nextSeq = await uow.Dispatches.GetNextSequenceNoAsync(batch.BatchId, ct);

                // Update batch status to Sending if it was Ready
                if (batch.BatchStatus == BatchStatus.Ready)
                {
                    await uow.Batches.UpdateStatusAsync(batch.BatchId, BatchStatus.Sending, null, null, _clock.UtcNow, ct);
                    batch = batch with { BatchStatus = BatchStatus.Sending };
                }

                await uow.CommitAsync(ct);
            }

            var dispatchId = Guid.NewGuid().ToString();
            var bundles = new List<JsonNode>(leased.Count);
            var dispatchItems = new List<DispatchItemRow>(leased.Count);

            // Load approved mappings for enrichment
            IReadOnlyList<ApprovedDomainMappingRow> approvedMappings;
            await using (var uowMappings = await _uowFactory.CreateAsync(ct))
            {
                approvedMappings = await uowMappings.DomainMappings.GetAllApprovedAsync(ct);
            }

            // Index mappings by (DomainTableId, SourceValue) for fast lookup.
            // DomainTableId is the most stable identifier across different HIS systems and backend versions.
            var mappingLookup = approvedMappings
                .GroupBy(m => (m.DomainTableId, m.SourceValue.Trim().ToLowerInvariant()))
                .ToDictionary(g => g.Key, g => g.First());

            await using (var uow = await _uowFactory.CreateAsync(ct))
            {
                for (int i = 0; i < leased.Count; i++)
                {
                    var key = leased[i];
                    var payloadRow = await uow.ClaimPayloads.GetAsync(key, ct);
                    if (payloadRow != null)
                    {
                        var node = JsonNode.Parse(payloadRow.PayloadJsonPlaintext);
                        if (node is JsonObject bundleObj)
                        {
                            // Inject/Override to ensure they match current batch
                            var header = bundleObj["claimHeader"]?.AsObject();
                            if (header != null)
                            {
                                header.Remove("provider_dhsCode");
                                header["providerCode"] = batch.ProviderDhsCode;
                                if (long.TryParse(batch.BcrId, out var bcrIdLong))
                                    header["bCR_Id"] = bcrIdLong;

                                EnrichClaimHeader(header, mappingLookup);
                            }

                            var serviceDetails = bundleObj["serviceDetails"]?.AsArray();
                            if (serviceDetails != null)
                                EnrichServiceDetails(serviceDetails, mappingLookup);

                            var diagnosisDetails = bundleObj["diagnosisDetails"]?.AsArray();
                            if (diagnosisDetails != null)
                            {
                                ClaimPayloadNormalizer.NormalizeDiagnosisDates(diagnosisDetails);
                                EnrichDiagnosisDetails(diagnosisDetails, mappingLookup);
                            }

                            var dhsDoctors = bundleObj["dhsDoctors"]?.AsArray();
                            if (dhsDoctors != null)
                                EnrichDoctorDetails(dhsDoctors, mappingLookup);


                            bundles.Add(bundleObj);
                        }
                    }

                    dispatchItems.Add(new DispatchItemRow(dispatchId, key.ProviderDhsCode, key.ProIdClaim, i + 1, DispatchItemResult.Unknown, null));
                }
            }

            if (bundles.Count == 0)
            {
                _logger.LogWarning("No bundles found for leased claims in batch {BatchId}. Releasing leases.", batch.BatchId);
                await using var uowRelease = await _uowFactory.CreateAsync(ct);
                await uowRelease.Claims.ReleaseLeaseAsync(leased, _clock.UtcNow, ct);
                await uowRelease.CommitAsync(ct);
                break;
            }

            // Create Dispatch record
            await using (var uowDispatch = await _uowFactory.CreateAsync(ct))
            {
                await uowDispatch.Dispatches.InsertDispatchAsync(
                    dispatchId,
                    batch.ProviderDhsCode,
                    batch.BatchId,
                    batch.BcrId,
                    nextSeq,
                    DispatchType.NormalSend,
                    DispatchStatus.InFlight,
                    _clock.UtcNow,
                    ct);

                await uowDispatch.DispatchItems.InsertManyAsync(dispatchItems, ct);
                await uowDispatch.CommitAsync(ct);
            }

            var jsonArray = ClaimBundleJsonPacket.ToJsonArray(bundles);
            packetCount++;

            // Report progress BEFORE sending the chunk
            int currentChunkStart = enqueuedCount + 1;
            int currentChunkEnd = enqueuedCount + leased.Count;
            double currentPercentage = totalClaimsInBatch > 0
                ? 55 + (((double)enqueuedCount / totalClaimsInBatch) * 45)
                : 100;

            progress.Report(new WorkerProgressReport(
                "StreamB",
                $"Sending claims {currentChunkStart}-{currentChunkEnd} of {totalClaimsInBatch}",
                Percentage: currentPercentage,
                BatchId: batch.BatchId,
                ProcessedCount: enqueuedCount,
                TotalCount: totalClaimsInBatch));

            try
            {
                var result = await _claimsClient.SendClaimAsync(jsonArray, ct);

                await using var uowResult = await _uowFactory.CreateAsync(ct);

                if (result.Succeeded)
                {
                    var successSet = result.SuccessClaimsProIdClaim.Select(id => (int)id).ToHashSet();
                    var failSet = result.FailClaimsProIdClaim.Select(id => (int)id).ToHashSet();

                    var successKeys = leased.Where(k => successSet.Contains(k.ProIdClaim)).ToList();
                    var failedKeys = leased.Where(k => failSet.Contains(k.ProIdClaim) || !successSet.Contains(k.ProIdClaim)).ToList();

                    if (successKeys.Count > 0)
                    {
                        await uowResult.Claims.MarkEnqueuedAsync(successKeys, batch.BcrId, _clock.UtcNow, ct);
                        enqueuedCount += successKeys.Count;
                    }

                    if (failedKeys.Count > 0)
                    {
                        await uowResult.Claims.MarkFailedAsync(failedKeys, "Failed at backend queue", _clock.UtcNow, TimeSpan.FromMinutes(1), ct);
                        await uowResult.Claims.IncrementAttemptAsync(failedKeys, _clock.UtcNow, ct);
                    }

                    var itemResults = leased.Select(k =>
                    {
                        var res = successSet.Contains(k.ProIdClaim) ? DispatchItemResult.Success : DispatchItemResult.Fail;
                        return (k, res, res == DispatchItemResult.Fail ? "Not in success list" : (string?)null);
                    }).ToList();

                    await uowResult.DispatchItems.UpdateItemResultAsync(dispatchId, itemResults, ct);

                    var finalStatus = successKeys.Count == leased.Count ? DispatchStatus.Succeeded :
                                      failedKeys.Count == leased.Count ? DispatchStatus.Failed :
                                      DispatchStatus.PartiallySucceeded;

                    await uowResult.Dispatches.UpdateDispatchResultAsync(dispatchId, finalStatus, result.HttpStatusCode, null, null, _clock.UtcNow, ct);
                }
                else
                {
                    await uowResult.Claims.MarkFailedAsync(leased, result.ErrorMessage ?? "SendClaim failed", _clock.UtcNow, TimeSpan.FromMinutes(1), ct);
                    await uowResult.Claims.IncrementAttemptAsync(leased, _clock.UtcNow, ct);
                    await uowResult.Dispatches.UpdateDispatchResultAsync(dispatchId, DispatchStatus.Failed, result.HttpStatusCode, result.ErrorMessage, null, _clock.UtcNow, ct);
                }

                await uowResult.CommitAsync(ct);

                // After send, update with final enqueued count for this batch
                double sendPercentage = totalClaimsInBatch > 0
                    ? 55 + (((double)enqueuedCount / totalClaimsInBatch) * 45)
                    : 100;

                progress.Report(new WorkerProgressReport(
                    "StreamB",
                    $"Sent {enqueuedCount} of {totalClaimsInBatch} claims",
                    Percentage: sendPercentage,
                    BatchId: batch.BatchId,
                    ProcessedCount: enqueuedCount,
                    TotalCount: totalClaimsInBatch));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send packet for batch {BatchId}", batch.BatchId);
                await using var uowEx = await _uowFactory.CreateAsync(ct);
                await uowEx.Claims.MarkFailedAsync(leased, ex.Message, _clock.UtcNow, TimeSpan.FromMinutes(1), ct);
                await uowEx.Claims.IncrementAttemptAsync(leased, _clock.UtcNow, ct);
                await uowEx.Dispatches.UpdateDispatchResultAsync(dispatchId, DispatchStatus.Failed, null, ex.Message, null, _clock.UtcNow, ct);
                await uowEx.CommitAsync(ct);
            }
        }

    }

    private void EnrichClaimHeader(JsonObject header, Dictionary<(int DomainTableId, string SourceValue), ApprovedDomainMappingRow> mappingLookup)
    {
        var baselineDomains = Application.Providers.BaselineDomainScanner.GetBaselineDomains();

        // Define fields to enrich in claimHeader
        var fields = new[]
        {
            ("fK_ClaimType_ID", "ClaimType", "ClaimType"),
            ("fK_GenderId", "PatientGender", "PatientGender"),
            ("fK_PatientIDType_ID", "PatientIDType", "PatientIdType"),
            ("fK_MaritalStatus_ID", "MaritalStatus", "MaritalStatus"),
            ("fK_Dept_ID", "Department", "BenHead"),
            ("fK_Nationality_ID", "Country", "Nationality"),
            ("fK_DischargeDisposition_ID", "DischargeDisposition", "DischargeDepositionsTypeID"),
            ("fK_EncounterClass_ID", "EncounterClass", "EnconuterTypeId"),
            ("fK_EncounterStatus_ID", "EncounterStatus", "EncounterStatus"),
            ("fK_EmergencyArrivalCode_ID", "EmergencyArrivalCode", "EmergencyArrivalCode"),
            ("fK_ServiceEventType_ID", "ServiceEventType", "ServiceEventType"),
            ("fK_TriageCategory_ID", "TriageCategory", "TriageCategoryTypeID"),
            ("fK_DispositionCode_ID", "DispositionCode", "EmergencyDepositionTypeID"),
            ("fK_InvestigationResult_ID", "InvestigationResult", "InvestigationResult"),
            ("fK_VisitType_ID", "VisitType", "VisitType"),
            ("fK_PatientOccupation_Id", "PatientOccupation", "PatientOccupation"),
            ("fK_SubscriberRelationship_ID", "SubscriberRelationship", "SubscriberRelationship")
        };

        foreach (var (targetField, domainName, sourceField) in fields)
        {
            var domain = baselineDomains.FirstOrDefault(d => string.Equals(d.DomainName, domainName, StringComparison.OrdinalIgnoreCase));
            if (domain == null) continue;

            if (TryGetMapping(header, sourceField, domain.DomainTableId, mappingLookup, out var mapping))
            {
                header[targetField] = new JsonObject
                {
                    // mapping.TargetValue corresponds to dhsDomainValue requested by user.
                    ["id"] = mapping.TargetValue,
                    ["code"] = mapping.CodeValue,
                    ["name"] = mapping.DisplayValue
                };
            }
        }
    }

    private void EnrichServiceDetails(JsonArray serviceDetails, Dictionary<(int DomainTableId, string SourceValue), ApprovedDomainMappingRow> mappingLookup)
    {
        var baselineDomains = Application.Providers.BaselineDomainScanner.GetBaselineDomains();

        var fields = new[]
        {
            ("fK_ServiceType_ID", "ServiceType", "serviceType"),
            ("fK_PharmacistSubstitute_ID", "Pharmacist Substitute", "PharmacistSubstitute"),
            ("fK_PharmacistSelectionReason_ID", "Pharmacist Selection Reason", "PharmacistSelectionReason")
        };

        foreach (var item in serviceDetails.OfType<JsonObject>())
        {
            foreach (var (targetField, domainName, sourceField) in fields)
            {
                var domain = baselineDomains.FirstOrDefault(d => string.Equals(d.DomainName, domainName, StringComparison.OrdinalIgnoreCase));
                if (domain == null) continue;

                if (TryGetMapping(item, sourceField, domain.DomainTableId, mappingLookup, out var mapping))
                {
                    item[targetField] = new JsonObject
                    {
                        // mapping.TargetValue corresponds to dhsDomainValue requested by user.
                        ["id"] = mapping.TargetValue,
                        ["code"] = mapping.CodeValue,
                        ["name"] = mapping.DisplayValue
                    };
                }
            }
        }
    }

    private void EnrichDiagnosisDetails(JsonArray diagnosisDetails, Dictionary<(int DomainTableId, string SourceValue), ApprovedDomainMappingRow> mappingLookup)
    {
        var baselineDomains = Application.Providers.BaselineDomainScanner.GetBaselineDomains();

        var fields = new[]
        {
            ("fK_DiagnosisOnAdmission_ID", "ConditionOnset", "DiagnosisOnAdmission")
        };

        foreach (var item in diagnosisDetails.OfType<JsonObject>())
        {
            foreach (var (targetField, domainName, sourceField) in fields)
            {
                var domain = baselineDomains.FirstOrDefault(d => string.Equals(d.DomainName, domainName, StringComparison.OrdinalIgnoreCase));
                if (domain == null) continue;

                if (TryGetMapping(item, sourceField, domain.DomainTableId, mappingLookup, out var mapping))
                {
                    item[targetField] = new JsonObject
                    {
                        // mapping.TargetValue corresponds to dhsDomainValue requested by user.
                        ["id"] = mapping.TargetValue,
                        ["code"] = mapping.CodeValue,
                        ["name"] = mapping.DisplayValue
                    };
                }
            }
        }
    }

    private void EnrichDoctorDetails(JsonArray dhsDoctors, Dictionary<(int DomainTableId, string SourceValue), ApprovedDomainMappingRow> mappingLookup)
    {
        var baselineDomains = Application.Providers.BaselineDomainScanner.GetBaselineDomains();

        var fields = new[]
        {
            ("fK_Gender", "DoctorGender", "DoctorGender"),
            ("fk_DoctorType", "DoctorType", "DoctorType_Code"),
            ("fK_DoctorReligion", "DoctorReligion", "religion_Code")
        };

        foreach (var item in dhsDoctors.OfType<JsonObject>())
        {
            foreach (var (targetField, domainName, sourceField) in fields)
            {
                var domain = baselineDomains.FirstOrDefault(d => string.Equals(d.DomainName, domainName, StringComparison.OrdinalIgnoreCase));
                if (domain == null) continue;

                if (TryGetMapping(item, sourceField, domain.DomainTableId, mappingLookup, out var mapping))
                {
                    item[targetField] = new JsonObject
                    {
                        // mapping.TargetValue corresponds to dhsDomainValue requested by user.
                        ["id"] = mapping.TargetValue,
                        ["code"] = mapping.CodeValue,
                        ["name"] = mapping.DisplayValue
                    };
                }
            }
        }
    }

    private bool TryGetMapping(
        JsonObject obj,
        string sourceField,
        int domainTableId,
        Dictionary<(int DomainTableId, string SourceValue), ApprovedDomainMappingRow> mappingLookup,
        out ApprovedDomainMappingRow mapping)
    {
        mapping = null!;
        // Case-insensitive lookup for property
        JsonNode? node = null;
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, sourceField, StringComparison.OrdinalIgnoreCase))
            {
                node = kv.Value;
                break;
            }
        }

        if (node == null) return false;

        var val = node.ToString().Trim();
        if (string.IsNullOrWhiteSpace(val)) return false;

        return mappingLookup.TryGetValue((domainTableId, val.ToLowerInvariant()), out mapping);
    }
}