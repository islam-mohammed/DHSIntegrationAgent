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

            // Index mappings by (DomainName, SourceValue) for fast lookup.
            // Use case-insensitive comparison for SourceValue.
            var mappingLookup = approvedMappings
                .GroupBy(m => (m.DomainName, m.SourceValue.Trim().ToLowerInvariant()))
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
                                EnrichDiagnosisDetails(diagnosisDetails, mappingLookup);

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

    private void EnrichClaimHeader(JsonObject header, Dictionary<(string DomainName, string SourceValue), ApprovedDomainMappingRow> mappingLookup)
    {
        // Define fields to enrich in claimHeader
        var fields = new[]
        {
            ("fK_ClaimType_ID", "ClaimType", "claimType"),
            ("fK_ClaimSubtype_ID", "ClaimSubtype", "claimSubtype"),
            ("fK_GenderId", "PatientGender", "patientGender"),
            ("fK_PatientIDType_ID", "PatientIDType", "patientIdType"),
            ("fK_MaritalStatus_ID", "MaritalStatus", "maritalStatus"),
            ("fK_Dept_ID", "Department", "BenHead"),
            ("fK_Nationality_ID", "Country", "nationality"),
            ("fK_BirthCountry_ID", "BirthCountry", "birthCountry"),
            ("fK_BirthCity_ID", "BirthCity", "birthCity"),
            ("fK_Religion_ID", "Religion", "patientReligion"),
            ("fK_AdmissionSpecialty_ID", "AdmissionSpecialty", "admissionSpecialty"),
            ("fK_DischargeSpeciality_ID", "DischargeSpeciality", "dischargeSpecialty"),
            ("fK_DischargeDisposition_ID", "DischargeDisposition", "DischargeDepositionsTypeID"),
            ("fK_EncounterAdmitSource_ID", "EncounterAdmitSource", "encounterAdmitSource"),
            ("fK_EncounterClass_ID", "EncounterClass", "enconuterTypeId"),
            ("fK_EncounterStatus_ID", "EncounterStatus", "encounterStatus"),
            ("fK_ReAdmission_ID", "ReAdmission", "reAdmission"),
            ("fK_EmergencyArrivalCode_ID", "EmergencyArrivalCode", "emergencyArrivalCode"),
            ("fK_ServiceEventType_ID", "ServiceEventType", "serviceEventType"),
            ("fK_IntendedLengthOfStay_ID", "IntendedLengthOfStay", "lengthOfStay"),
            ("fK_TriageCategory_ID", "TriageCategory", "triageCategoryTypeID"),
            ("fK_DispositionCode_ID", "DispositionCode", "EmergencyDepositionTypeID"),
            ("fK_InvestigationResult_ID", "InvestigationResult", "investigationResult"),
            ("fK_VisitType_ID", "VisitType", "visitType"),
            ("fK_PatientOccupation_Id", "PatientOccupation", "patientOccupation"),
            ("fK_SubscriberRelationship_ID", "SubscriberRelationship", "subscriberRelationship")
        };

        foreach (var (targetField, domainName, sourceField) in fields)
        {
            if (TryGetMapping(header, sourceField, domainName, mappingLookup, out var mapping))
            {
                if (targetField == "fK_InvestigationResult_ID")
                {
                    if (int.TryParse(mapping.TargetValue, out var intVal))
                        header[targetField] = intVal;
                }
                else
                {
                    header[targetField] = CreateCommonType(mapping);
                }
            }
        }
    }

    private void EnrichServiceDetails(JsonArray serviceDetails, Dictionary<(string DomainName, string SourceValue), ApprovedDomainMappingRow> mappingLookup)
    {
        var fields = new[]
        {
            ("fK_ServiceType_ID", "ServiceType", "serviceType"),
            ("fK_PharmacistSubstitute_ID", "Pharmacist Substitute", "pharmacistSubstitute"),
            ("fK_PharmacistSelectionReason_ID", "Pharmacist Selection Reason", "pharmacistSelectionReason")
        };

        foreach (var item in serviceDetails.OfType<JsonObject>())
        {
            foreach (var (targetField, domainName, sourceField) in fields)
            {
                if (TryGetMapping(item, sourceField, domainName, mappingLookup, out var mapping))
                {
                    item[targetField] = CreateCommonType(mapping);
                }
            }
        }
    }

    private void EnrichDiagnosisDetails(JsonArray diagnosisDetails, Dictionary<(string DomainName, string SourceValue), ApprovedDomainMappingRow> mappingLookup)
    {
        var fields = new[]
        {
            ("fK_DiagnosisOnAdmission_ID", "DiagnosisOnAdmission", "diagnosisOnAdmission"),
            ("fK_DiagnosisType_ID", "DiagnosisType", "diagnosisTypeID"),
            ("fK_ConditionOnset_ID", "ConditionOnset", "onsetConditionTypeID")
        };

        foreach (var item in diagnosisDetails.OfType<JsonObject>())
        {
            foreach (var (targetField, domainName, sourceField) in fields)
            {
                if (TryGetMapping(item, sourceField, domainName, mappingLookup, out var mapping))
                {
                    if (targetField == "fK_DiagnosisType_ID" || targetField == "fK_ConditionOnset_ID")
                    {
                        if (int.TryParse(mapping.TargetValue, out var intVal))
                            item[targetField] = intVal;
                    }
                    else
                    {
                        item[targetField] = CreateCommonType(mapping);
                    }
                }
            }
        }
    }

    private void EnrichDoctorDetails(JsonArray dhsDoctors, Dictionary<(string DomainName, string SourceValue), ApprovedDomainMappingRow> mappingLookup)
    {
        var fields = new[]
        {
            ("fK_Gender", "DoctorGender", "doctorGender"),
            ("fK_Nationality", "Country", "doctorNationality"),
            ("fK_Religion", "DoctorReligion", "religion_Code"),
            ("fK_PractitionerRoleType", "PractitionerRoleType", "practitionerRoleType"),
            ("fK_ClaimCareTeamRole", "ClaimCareTeamRole", "claimCareTeamRole")
        };

        foreach (var doctor in dhsDoctors.OfType<JsonObject>())
        {
            foreach (var (targetField, domainName, sourceField) in fields)
            {
                if (TryGetMapping(doctor, sourceField, domainName, mappingLookup, out var mapping))
                {
                    doctor[targetField] = CreateCommonType(mapping);
                }
            }
        }
    }

    private JsonObject CreateCommonType(ApprovedDomainMappingRow mapping)
    {
        return new JsonObject
        {
            ["id"] = mapping.TargetValue,
            ["code"] = mapping.CodeValue ?? mapping.TargetValue,
            ["name"] = mapping.DisplayValue ?? mapping.SourceValue,
            ["ksaID"] = null,
            ["ksaCode"] = null,
            ["ksaName"] = null
        };
    }

    private bool TryGetMapping(
        JsonObject obj,
        string sourceField,
        string domainName,
        Dictionary<(string DomainName, string SourceValue), ApprovedDomainMappingRow> mappingLookup,
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

        return mappingLookup.TryGetValue((domainName, val.ToLowerInvariant()), out mapping);
    }
}
