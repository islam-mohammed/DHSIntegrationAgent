# DHS Integration Agent — Zero‑Questions Master Specification (Complete)

*Generated:* 2026-03-01 10:53:30Z UTC

This document is intentionally exhaustive. It covers:
- All user journeys (UI flows)
- Full business rules
- Adapter behavior and provider database contracts
- Stream behaviors, leasing, crash recovery
- Domain mapping: scan, storage, posting, enrichment
- Observability, runbooks, and support bundle export
- Full SQLite schema and API schemas

It is written to allow a junior developer and Jules.google.com to implement the full application without follow-up questions.

## 0. How to Use This Spec

Read in this order:
1) §1–§4 to understand user journeys and end-to-end workflows.
2) §5–§7 for rules, data model, and state transitions.
3) §8–§11 for adapters, streams, domain mapping, and attachments.
4) §12–§14 for observability/support and acceptance tests.
5) Appendices provide full DTO and schema listings.

## 1. Product Scope and Business Objectives

The agent integrates provider HIS claims into DHS backend.

Non-negotiables:
- Offline-first: local SQLite is the work queue and source of truth for processing state.
- PHI safety: do not log payloads/attachments; encrypt sensitive local data.
- Provider-scoped domain mapping with two separate stores: ApprovedDomainMapping and MissingDomainMapping.
- Missing mapping discovery/posting must never block staging or sending.
- Attachments are processed by a dedicated manual stream (user-triggered for Completed batches) plus an automatic retry stream.

## 2. Architecture (Layers, Responsibilities, and Cross-Cutting Concerns)

### 2.1 Presentation (WPF + MVVM)
- Owns Views and ViewModels.
- ViewModels call Application use-cases only; they do not call HTTP clients directly and do not access SQLite directly.
- ViewModels receive state via query services/repositories exposed by Application.

### 2.2 Application (Use-cases)
- Orchestrates workflows and state transitions: onboarding, config refresh, batch processing, mapping, attachments.
- Enforces preconditions and invariants (e.g., provider config exists before engine start).
- Defines interfaces: persistence, HTTP, encryption, provider DB adapters, time, and background worker scheduling.

### 2.3 Domain
- Defines state machines and enums.
- Defines canonical identifiers and normalization rules.

### 2.4 Infrastructure
- SQLite migrations and repositories.
- Encryption/key handling.
- HTTP clients (swagger-aligned) + API call logging.
- Background worker host and scheduling.
- Azure Blob upload support.

### 2.5 Integration Adapters
- Provider DB connection factories by DbEngine.
- Adapters for integration modes (Tables / Views / Custom SQL).
- Conversion rules from DbDataReader rows to JSON nodes.

### 2.6 Cross-cutting concerns
- Cancellation: all long-running operations must respect CancellationToken.
- SQLite concurrency: no long transactions; avoid holding SQLite locks across awaited I/O.
- Idempotency: all retries must be safe (backend can return partial success).

## 3. User Journeys (All Screens, Fields, Actions, and Rules)

### 3.1 Setup / Onboarding

Goal: configure ProviderDhsCode, GroupId, and provider DB connectivity.

Inputs:
- ProviderDhsCode (string)
- GroupId (string)
- Optional (only if your provider uses file-path attachments on a network share):
  - Network share username
  - Network share password


Actions:
1) Save configuration locally.
2) Test Provider DB connection (read-only).
3) Test API health.

Rules:
- Never persist API tokens in SQLite.
- Never log connection strings.

### 3.2 Login

Goal: obtain token and bootstrap provider configuration cache.

Steps:
1) User enters email/password.
2) Call login endpoint.
3) Login returns no token; do not store any token.
4) Immediately call Provider Configuration and upsert local derived data.
5) Navigate to Dashboard.

### 3.3 Dashboard

Goal: provide operational overview (PHI-free counts/metrics).

Data sources:
- Local SQLite aggregates (batch/claim status counts).
- Backend dashboard endpoint (optional).

### 3.4 Batches

Goal: create and monitor batch processing and take operational actions.

Create Batch:
- Input: CompanyCode + StartDate/EndDate OR MonthKey.
- Output: local Batch row in Draft.

Batch actions:
- Start Engine (starts Stream A/B/C/D and Attachment Retry Stream).
- Stop Engine.
- Manual packet retry (resends an existing failed dispatch packet; cooldown enforced).
- Upload Attachments (manual stream) — only allowed when BatchStatus=Completed and HasResume is false.
- View Dispatch history (packet-level) and per-claim outcomes.
- Delete server batch request (if supported).

### 3.5 Domain Mapping Wizard

Goal: operational visibility and refresh of mappings.

UI tabs:
- Approved mappings (SQLite ApprovedDomainMapping)
- Missing mappings (SQLite MissingDomainMapping)

Actions:
- Refresh from backend (Provider Configuration refresh) then reload tables.
- Post missing now (optional) — invokes posting logic but never blocks sending.

Rules:
- ViewModel must not call HTTP clients directly.

### 3.6 Attachments

Goal: monitor and operate attachment upload pipeline.

Views:
- By batch and by claim.
- Show UploadStatus, AttemptCount, NextRetryUtc, LastError (PHI-safe).

Actions:
- Start manual upload for a completed batch.
- View failed items and upcoming retries.

### 3.7 Diagnostics / Support

Goal: allow troubleshooting without reading code.

Views:
- API call log (ApiCallLog) with correlation ID.
- Dispatch and DispatchItem summaries.
- Health check results.

Actions:
- Export PHI-safe support bundle ZIP.

## 4. End-to-End Workflows (Detailed, Step-by-Step)

### 4.1 Provider Configuration Cache + Upsert

1) Fetch Provider Configuration from backend.
2) Persist ProviderConfigCache with fetched time, expiry time (TTL), and ETag (if provided).
3) Upsert PayerProfile list.
4) Upsert ApprovedDomainMapping and MissingDomainMapping.
5) Persist generalConfiguration settings into AppSettings.
6) If refresh fails: use last cache and record PHI-safe warning; if cache is missing, block engine start.

### 4.2 Stream A — Fetch & Stage (Claims)

Preconditions:
- ProviderProfile configured and active.
- Provider Configuration cached.

Algorithm:
1) Select a Batch in Draft.
2) Update BatchStatus to Fetching.
3) Ensure backend BcrId exists by calling CreateBatchRequest (must include TotalClaims).
4) Page claim keys from provider DB using keyset paging.
5) Fetch claim parts in batch queries (header + each detail table) using IN-list batching.
6) Convert provider DB rows to JSON objects (see §8.4 conversion rules).
7) Build canonical ClaimBundle using ClaimBundleBuilder rules (§10).
8) Sanitize strings (remove invalid UTF-8 characters).
9) Stage Claim row (EnqueueStatus=NotSent) and ClaimPayload (encrypted at rest).
10) Discover missing domain values and upsert MissingDomainMapping.
11) Post missing mappings (non-blocking).
12) Mark BatchStatus Ready and set HasResume flag appropriately.

### 4.3 Stream B — Send (Normal dispatch)

Algorithm:
1) Lease up to 40 NotSent claims (atomic update: LockedBy='Sender', EnqueueStatus=InFlight).
2) Load and decrypt payloads.
3) Inject/override sending-time header fields: providerCode and bCR_Id.
4) Enrich payload using ApprovedDomainMapping (see §11).
5) Serialize packet to JSON array and gzip compress.
6) Call SendClaim.
7) Process response:
   - Success set = successClaimsProidClaim[]
   - Fail set = requestedIds − success set
8) Update Claim rows: success -> Enqueued; failures -> Failed with AttemptCount and NextRetryUtc.
9) Create Dispatch + DispatchItems audit rows with correlation ID.
10) Release leases.

### 4.4 Stream C — Automatic Retry + Manual Packet Retry

Automatic retry rules:
- Eligible: EnqueueStatus=Failed AND NextRetryUtc<=now AND AttemptCount in {1,2,3}.
- Schedule:
  - after first failure: +1 minute
  - after second: +3 minutes
  - after third: +6 minutes
  - after third retry failure: stop automatic retries (NextRetryUtc = null)

Manual packet retry rules:
- Retry is packet-level (1..40), not claim-by-claim.
- Preferred: re-send a selected failed Dispatch (same claims) as DispatchType=RetrySend.
- Cooldown: ManualRetryCooldownMinutes (default 10). Do not allow retry if a RetrySend dispatch exists within cooldown.

### 4.5 Stream D — Resume Completion + Requeue Incompletes

Resume:
1) Select batches where BatchStatus=HasResume and BcrId exists.
2) Call GetHISProIdClaimsByBcrId.
3) Mark returned claim ids as CompletionStatus=Completed.
4) If all claims completed: set BatchStatus=Completed and HasResume=false.

Requeue incompletes:
1) IncompleteClaims = claims in batch where EnqueueStatus=Enqueued AND CompletionStatus=Unknown.
2) Select up to 40 due for requeue (NextRequeueUtc<=now).
3) Lease with LockedBy='Requeue' (do not change EnqueueStatus away from Enqueued).
4) Send packet via SendClaim.
5) On success: keep Enqueued and update LastEnqueuedUtc; on failure: set NextRequeueUtc=now+3 minutes and set LastRequeueError.
6) Completion is still only via Resume.

### 4.6 Attachments — Manual Upload Stream (User Triggered)

Trigger:
- Operator clicks Upload Attachments on a Completed batch.

Algorithm:
1) Determine batch date range and company code.
2) Query provider DB for attachments for that batch.
3) For each attachment row:
   - Map to AttachmentRow using required fields (§8.6).
   - Upsert into local Attachment table as UploadStatus=Staged (must preserve AttemptCount/NextRetryUtc for existing rows).
4) Upload each staged attachment:
   - Source selection priority: LocationBytes -> AttachBitBase64 -> LocationPath.
   - If LocationPath is UNC, connect using NetworkUsername/NetworkPassword.
   - Upload to Blob path: ProviderDhsCode/ProIdClaim/FileName.
5) Update Attachment status:
   - Uploaded: store OnlineUrlEncrypted and SizeBytes.
   - Failed: store LastError and NextRetryUtc=now+5 minutes.
6) Update the ClaimPayload attachments[] list for that claim to include OnlineUrl.
7) Call UploadAttachment endpoint to register metadata.

### 4.7 Attachments — Automatic Retry Stream (Always On)

Goal: retry failed/staged attachments without user action.

Eligibility:
- UploadStatus in (Staged, Failed)
- NextRetryUtc is null or <= now

Race-free requirement:
- Retry worker and manual worker must atomically transition to Uploading before starting upload.

Algorithm:
1) Select eligible rows and atomically set UploadStatus=Uploading.
2) Upload + update to Uploaded.
3) Register metadata.
4) On failure: set Failed, AttemptCount++, NextRetryUtc per backoff schedule.
5) Crash recovery: any Uploading rows older than lease duration are reverted to Failed and scheduled.

## 5. Business Rules (Complete Catalog)

| Rule ID | Rule |
| --- | --- |
| BR-001 | No PHI in logs or support artifacts. Never export ClaimPayload or attachment bytes. |
| BR-002 | SQLite stores work-state. Backend is authoritative for completion confirmation (Resume). |
| BR-003 | SendClaim packets are 1..40 claims and must be gzip. |
| BR-004 | Leasing is mandatory and must be atomic. Never double-process a claim. |
| BR-005 | Retryable claims = requestedIds − successClaimsProidClaim[] (do not depend on a fail list). |
| BR-006 | Domain mapping is provider-scoped and split into Approved vs Missing tables. |
| BR-007 | Missing mapping posting never blocks staging or sending. |
| BR-008 | Attachments pipeline is independent from claim streams and has its own retry stream. |
| BR-009 | ViewModels do not call HTTP; use Application services. |
| BR-010 | Support bundle export must be PHI-safe and include only allowed tables/logs. |

## 6. Local SQLite Data Model (Tables, Fields, and Purpose)

This section lists every table and column (authoritative).

### Table: ApiCallLog

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| ApiCallLogId | INTEGER | YES | YES |  |
| ProviderDhsCode | TEXT | YES |  |  |
| EndpointName | TEXT | NO |  |  |
| CorrelationId | TEXT | YES |  |  |
| RequestUtc | TEXT | NO |  |  |
| ResponseUtc | TEXT | YES |  |  |
| DurationMs | INTEGER | YES |  |  |
| HttpStatusCode | INTEGER | YES |  |  |
| Succeeded | INTEGER | NO |  | 0 |
| ErrorMessage | TEXT | YES |  |  |
| RequestBytes | INTEGER | YES |  |  |
| ResponseBytes | INTEGER | YES |  |  |
| WasGzipRequest | INTEGER | NO |  | 0 |

### Table: AppMeta

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| Id | INTEGER | YES | YES |  |
| SchemaVersion | INTEGER | NO |  |  |
| CreatedUtc | TEXT | NO |  |  |
| LastOpenedUtc | TEXT | NO |  |  |
| ClientInstanceId | TEXT | NO |  |  |
| ActiveKeyId | TEXT | YES |  |  |
| ActiveKeyCreatedUtc | TEXT | YES |  |  |
| ActiveKeyDpapiBlob | BLOB | YES |  |  |
| PreviousKeyId | TEXT | YES |  |  |
| PreviousKeyCreatedUtc | TEXT | YES |  |  |
| PreviousKeyDpapiBlob | BLOB | YES |  |  |

### Table: ApprovedDomainMapping

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| DomainMappingId | INTEGER | YES | YES |  |
| ProviderDhsCode | TEXT | NO |  |  |
| DomainName | TEXT | NO |  |  |
| DomainTableId | INTEGER | NO |  |  |
| SourceValue | TEXT | NO |  |  |
| TargetValue | TEXT | NO |  |  |
| DiscoveredUtc | TEXT | NO |  |  |
| LastPostedUtc | TEXT | YES |  |  |
| LastUpdatedUtc | TEXT | NO |  |  |
| Notes | TEXT | YES |  |  |
| ProviderDomainCode | TEXT | YES |  |  |
| IsDefault | INTEGER | YES |  |  |
| CodeValue | TEXT | YES |  |  |
| DisplayValue | TEXT | YES |  |  |

### Table: AppSettings

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| Id | INTEGER | YES | YES |  |
| GroupID | TEXT | YES |  |  |
| ProviderDhsCode | TEXT | YES |  |  |
| ConfigCacheTtlMinutes | INTEGER | NO |  | 1440 |
| FetchIntervalMinutes | INTEGER | NO |  | 5 |
| ManualRetryCooldownMinutes | INTEGER | NO |  | 10 |
| LeaseDurationSeconds | INTEGER | NO |  | 120 |
| StreamAIntervalSeconds | INTEGER | NO |  | 900 |
| ResumePollIntervalSeconds | INTEGER | NO |  | 300 |
| ApiTimeoutSeconds | INTEGER | NO |  | 60 |
| CreatedUtc | TEXT | NO |  |  |
| UpdatedUtc | TEXT | NO |  |  |

### Table: Attachment

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| AttachmentId | TEXT | YES | YES |  |
| ProviderDhsCode | TEXT | NO |  |  |
| ProIdClaim | INTEGER | NO |  |  |
| AttachmentSourceType | INTEGER | NO |  |  |
| LocationPath | TEXT | YES |  |  |
| LocationBytesEncrypted | BLOB | YES |  |  |
| LocationPathEncrypted | BLOB | YES |  |  |
| AttachBitBase64Encrypted | BLOB | YES |  |  |
| FileName | TEXT | YES |  |  |
| ContentType | TEXT | YES |  |  |
| SizeBytes | INTEGER | YES |  |  |
| Sha256 | TEXT | YES |  |  |
| OnlineURL | TEXT | YES |  |  |
| OnlineUrlEncrypted | BLOB | YES |  |  |
| UploadStatus | INTEGER | NO |  |  |
| AttemptCount | INTEGER | NO |  | 0 |
| NextRetryUtc | TEXT | YES |  |  |
| LastError | TEXT | YES |  |  |
| CreatedUtc | TEXT | NO |  |  |
| UpdatedUtc | TEXT | NO |  |  |

Constraints / Foreign Keys:

- FOREIGN KEY (ProviderDhsCode, ProIdClaim) REFERENCES Claim(ProviderDhsCode, ProIdClaim) ON DELETE CASCADE

### Table: Batch

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| BatchId | INTEGER | YES | YES |  |
| ProviderDhsCode | TEXT | NO |  |  |
| CompanyCode | TEXT | NO |  |  |
| PayerCode | TEXT | YES |  |  |
| MonthKey | TEXT | NO |  |  |
| StartDateUtc | TEXT | YES |  |  |
| EndDateUtc | TEXT | YES |  |  |
| BcrId | TEXT | YES |  |  |
| BatchStatus | INTEGER | NO |  |  |
| HasResume | INTEGER | NO |  | 0 |
| CreatedUtc | TEXT | NO |  |  |
| UpdatedUtc | TEXT | NO |  |  |
| LastError | TEXT | YES |  |  |

### Table: Claim

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| ProviderDhsCode | TEXT | NO |  |  |
| ProIdClaim | INTEGER | NO |  |  |
| CompanyCode | TEXT | NO |  |  |
| MonthKey | TEXT | NO |  |  |
| BatchId | INTEGER | YES |  |  |
| BcrId | TEXT | YES |  |  |
| EnqueueStatus | INTEGER | NO |  |  |
| CompletionStatus | INTEGER | NO |  |  |
| LockedBy | TEXT | YES |  |  |
| InFlightUntilUtc | TEXT | YES |  |  |
| AttemptCount | INTEGER | NO |  | 0 |
| NextRetryUtc | TEXT | YES |  |  |
| LastError | TEXT | YES |  |  |
| LastResumeCheckUtc | TEXT | YES |  |  |
| RequeueAttemptCount | INTEGER | NO |  | 0 |
| NextRequeueUtc | TEXT | YES |  |  |
| LastRequeueError | TEXT | YES |  |  |
| LastEnqueuedUtc | TEXT | YES |  |  |
| FirstSeenUtc | TEXT | NO |  |  |
| LastUpdatedUtc | TEXT | NO |  |  |

Constraints / Foreign Keys:

- PRIMARY KEY (ProviderDhsCode, ProIdClaim)
- FOREIGN KEY (BatchId) REFERENCES Batch(BatchId) ON DELETE SET NULL

### Table: ClaimPayload

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| ProviderDhsCode | TEXT | NO |  |  |
| ProIdClaim | INTEGER | NO |  |  |
| PayloadJson | BLOB | NO |  |  |
| PayloadSha256 | TEXT | NO |  |  |
| PayloadVersion | INTEGER | NO |  | 1 |
| CreatedUtc | TEXT | NO |  |  |
| UpdatedUtc | TEXT | NO |  |  |

Constraints / Foreign Keys:

- PRIMARY KEY (ProviderDhsCode, ProIdClaim)
- FOREIGN KEY (ProviderDhsCode, ProIdClaim) REFERENCES Claim(ProviderDhsCode, ProIdClaim) ON DELETE CASCADE

### Table: Dispatch

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| DispatchId | TEXT | YES | YES |  |
| ProviderDhsCode | TEXT | NO |  |  |
| BatchId | INTEGER | NO |  |  |
| BcrId | TEXT | YES |  |  |
| SequenceNo | INTEGER | NO |  |  |
| DispatchType | INTEGER | NO |  |  |
| DispatchStatus | INTEGER | NO |  |  |
| AttemptCount | INTEGER | NO |  | 0 |
| NextRetryUtc | TEXT | YES |  |  |
| RequestSizeBytes | INTEGER | YES |  |  |
| RequestGzip | INTEGER | NO |  | 1 |
| HttpStatusCode | INTEGER | YES |  |  |
| LastError | TEXT | YES |  |  |
| CorrelationId | TEXT | YES |  |  |
| CreatedUtc | TEXT | NO |  |  |
| UpdatedUtc | TEXT | NO |  |  |

Constraints / Foreign Keys:

- FOREIGN KEY (BatchId) REFERENCES Batch(BatchId) ON DELETE CASCADE

### Table: DispatchItem

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| DispatchId | TEXT | NO |  |  |
| ProviderDhsCode | TEXT | NO |  |  |
| ProIdClaim | INTEGER | NO |  |  |
| ItemOrder | INTEGER | NO |  |  |
| ItemResult | INTEGER | YES |  |  |
| ErrorMessage | TEXT | YES |  |  |

Constraints / Foreign Keys:

- PRIMARY KEY (DispatchId, ProviderDhsCode, ProIdClaim)
- FOREIGN KEY (DispatchId) REFERENCES Dispatch(DispatchId) ON DELETE CASCADE
- FOREIGN KEY (ProviderDhsCode, ProIdClaim) REFERENCES Claim(ProviderDhsCode, ProIdClaim) ON DELETE CASCADE

### Table: MissingDomainMapping

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| MissingMappingId | INTEGER | YES | YES |  |
| ProviderDhsCode | TEXT | NO |  |  |
| DomainName | TEXT | NO |  |  |
| DomainTableId | INTEGER | NO |  |  |
| SourceValue | TEXT | NO |  |  |
| DiscoverySource | INTEGER | NO |  |  |
| domainStatus | INTEGER | NO |  |  |
| DiscoveredUtc | TEXT | NO |  |  |
| LastPostedUtc | TEXT | YES |  |  |
| LastUpdatedUtc | TEXT | NO |  |  |
| Notes | TEXT | YES |  |  |
| ProviderNameValue | TEXT | YES |  |  |
| DomainTableName | TEXT | YES |  |  |

### Table: PayerProfile

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| PayerId | INTEGER | YES | YES |  |
| ProviderDhsCode | TEXT | NO |  |  |
| CompanyCode | TEXT | NO |  |  |
| PayerCode | TEXT | YES |  |  |
| PayerName | TEXT | YES |  |  |
| IsActive | INTEGER | NO |  | 1 |
| CreatedUtc | TEXT | NO |  |  |
| UpdatedUtc | TEXT | NO |  |  |

### Table: ProviderConfigCache

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| ProviderDhsCode | TEXT | NO |  |  |
| ETag | TEXT | YES |  |  |
| ConfigJson | TEXT | NO |  |  |
| FetchedUtc | TEXT | NO |  |  |
| ExpiresUtc | TEXT | NO |  |  |
| LastError | TEXT | YES |  |  |

Constraints / Foreign Keys:

- PRIMARY KEY (ProviderDhsCode)

### Table: ProviderExtractionConfig

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| ProviderCode | TEXT | YES | YES |  |
| ClaimKeyColumnName | TEXT | YES |  |  |
| HeaderSourceName | TEXT | YES |  |  |
| DetailsSourceName | TEXT | YES |  |  |
| CustomHeaderSql | TEXT | YES |  |  |
| CustomServiceSql | TEXT | YES |  |  |
| CustomDiagnosisSql | TEXT | YES |  |  |
| CustomLabSql | TEXT | YES |  |  |
| CustomRadiologySql | TEXT | YES |  |  |
| CustomOpticalSql | TEXT | YES |  |  |
| Notes | TEXT | YES |  |  |
| UpdatedUtc | TEXT | NO |  |  |

### Table: ProviderProfile

| Column | Type | Nullable | PK | Default |
| --- | --- | --- | --- | --- |
| ProviderCode | TEXT | NO |  |  |
| ProviderDhsCode | TEXT | NO |  |  |
| DbEngine | TEXT | NO |  |  |
| IntegrationType | TEXT | NO |  |  |
| EncryptedConnectionString | BLOB | NO |  |  |
| EncryptionKeyId | TEXT | YES |  |  |
| IsActive | INTEGER | NO |  | 1 |
| CreatedUtc | TEXT | NO |  |  |
| UpdatedUtc | TEXT | NO |  |  |

Constraints / Foreign Keys:

- PRIMARY KEY (ProviderCode)

### Indexes

- CREATE UNIQUE INDEX UX_PayerProfile_Provider_Company ON PayerProfile(ProviderDhsCode, CompanyCode);
- CREATE INDEX IX_PayerProfile_Provider_IsActive ON PayerProfile(ProviderDhsCode, IsActive);
- CREATE UNIQUE INDEX UX_Batch_Provider_Company_Month ON Batch(ProviderDhsCode, CompanyCode, MonthKey, StartDateUtc, EndDateUtc);
- CREATE INDEX IX_Batch_Status ON Batch(BatchStatus);
- CREATE INDEX IX_Batch_BcrId ON Batch(BcrId);
- CREATE INDEX IX_Claim_Provider_Company_Month ON Claim(ProviderDhsCode, CompanyCode, MonthKey);
- CREATE INDEX IX_Claim_EnqueueStatus_NextRetryUtc ON Claim(EnqueueStatus, NextRetryUtc);
- CREATE INDEX IX_Claim_CompletionStatus ON Claim(CompletionStatus);
- CREATE INDEX IX_Claim_BatchId ON Claim(BatchId);
- CREATE INDEX IX_Claim_InFlightUntilUtc ON Claim(InFlightUntilUtc);
- CREATE INDEX IX_Claim_Enqueue_Completion_RequeueUtc ON Claim(EnqueueStatus, CompletionStatus, NextRequeueUtc);
- CREATE INDEX IX_ClaimPayload_PayloadSha256 ON ClaimPayload(PayloadSha256);
- CREATE UNIQUE INDEX UX_Dispatch_Batch_Sequence ON Dispatch(BatchId, SequenceNo);
- CREATE INDEX IX_Dispatch_Status_NextRetryUtc ON Dispatch(DispatchStatus, NextRetryUtc);
- CREATE INDEX IX_Dispatch_BatchId ON Dispatch(BatchId);
- CREATE INDEX IX_DispatchItem_Provider_ProIdClaim ON DispatchItem(ProviderDhsCode, ProIdClaim);
- CREATE INDEX IX_DispatchItem_DispatchId ON DispatchItem(DispatchId);
- CREATE INDEX IX_Attachment_Provider_ProIdClaim ON Attachment(ProviderDhsCode, ProIdClaim);
- CREATE INDEX IX_Attachment_Status_NextRetryUtc ON Attachment(UploadStatus, NextRetryUtc);
- CREATE UNIQUE INDEX UX_Attachment_Claim_Sha256 ON Attachment(ProviderDhsCode, ProIdClaim, Sha256) WHERE Sha256 IS NOT NULL AND Sha256 <> '';
- CREATE INDEX IX_ApiCallLog_Endpoint_RequestUtc ON ApiCallLog(EndpointName, RequestUtc);
- CREATE UNIQUE INDEX UX_ApprovedDomainMapping_Key ON ApprovedDomainMapping(ProviderDhsCode, DomainTableId, SourceValue);
- CREATE UNIQUE INDEX UX_MissingDomainMapping_Key ON MissingDomainMapping(ProviderDhsCode, DomainTableId, SourceValue);

## 7. State Machines (Enums)

### BatchStatus

| Name | Value |
| --- | --- |
| Draft | 0 |
| Ready | 1 |
| Sending | 2 |
| Enqueued | 3 |
| HasResume | 4 |
| Completed | 5 |
| Failed | 6 |
| Fetching | 7 |

### EnqueueStatus

| Name | Value |
| --- | --- |
| NotSent | 0 |
| InFlight | 1 |
| Enqueued | 2 |
| Failed | 3 |

### CompletionStatus

| Name | Value |
| --- | --- |
| Unknown | 0 |
| Completed | 1 |

### UploadStatus

| Name | Value |
| --- | --- |
| NotStaged | 0 |
| Staged | 1 |
| Uploading | 2 |
| Uploaded | 3 |
| Failed | 4 |

### DispatchType

| Name | Value |
| --- | --- |
| NormalSend | 0 |
| RetrySend | 1 |
| RequeueIncomplete | 2 |

### DispatchStatus

| Name | Value |
| --- | --- |
| Ready | 0 |
| InFlight | 1 |
| Succeeded | 2 |
| Failed | 3 |
| PartiallySucceeded | 4 |

### DispatchItemResult

| Name | Value |
| --- | --- |
| Unknown | 0 |
| Success | 1 |
| Fail | 2 |

### MappingStatus

| Name | Value |
| --- | --- |
| Missing | 0 |
| Posted | 1 |
| Approved | 2 |
| PostFailed | 3 |

### DiscoverySource

| Name | Value |
| --- | --- |
| Api | 0 |
| Scanned | 1 |

### AttachmentSourceType

| Name | Value |
| --- | --- |
| FilePath | 0 |
| RawBytesInLocation | 1 |
| Base64InAttachBit | 2 |

## 8. Integration Adapters (Detailed Implementation Contract)

### 8.1 Provider DB Connection Factory (DbEngine)

DbEngine is a string stored in ProviderProfile. A factory must:
- Load ProviderProfile for ProviderDhsCode.
- Decrypt connection string only in memory.
- Apply secure defaults when possible (e.g., SQL Server Encrypt=true, TrustServerCertificate=false).
- Use connection pooling.
- Use short-lived connections (open → query → close).
- Enforce a command timeout.

Supported engines (by design): sqlserver (required), oracle, postgresql, mysql.
Implementation requirement:
- Use DbProviderFactory per engine and parameter objects native to that provider.
- Abstract paging differences (TOP vs LIMIT/FETCH FIRST).

### 8.2 IntegrationType Modes (Tables / Views / Custom SQL)

High-level guidance (onboarding without changing stream logic):



Query patterns (what each query must return):



Template safety rules:



### 8.3 Tables Mode (Exact Query Shapes Used)

Default table names:

| Entity | Default Name |
| --- | --- |
| HeaderTable | DHSClaim_Header |
| DoctorTable | DHS_Doctor |
| ServiceTable | DHSService_Details |
| DiagnosisTable | DHSDiagnosis_Details |
| LabTable | DHSLab_Details |
| RadiologyTable | DHSRadiology_Details |
| AttachmentTable | DHS_Attachment |
| OpticalTable | DHS_OpticalVitalSign |
| ItemDetailsTable | DHS_ItemDetails |
| AchiTable | DHS_ACHI |

Default claim key column: `ProIdClaim`

Default header date column: `InvoiceDate`

Exact query templates (SQL Server shape; other engines must produce equivalent results):

#### Template 1

```sql
SELECT COUNT(1)
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate;
```

#### Template 2

```sql
SELECT COUNT(1)
FROM {DefaultAttachmentTable} a
INNER JOIN {headerTable} h ON a.{DefaultClaimKeyColumn} = h.{claimKeyCol}
WHERE h.CompanyCode = @CompanyCode
  AND h.{dateCol} >= @StartDate
  AND h.{dateCol} <= @EndDate;
```

#### Template 3

```sql
SELECT
    COUNT(1) AS TotalClaims,
    SUM(TotalNetAmount) AS TotalNetAmount,
    SUM(ClaimedAmount) AS TotalClaimedAmount,
    SUM(TotalDiscount) AS TotalDiscount,
    SUM(TotalDeductible) AS TotalDeductible
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate;
```

#### Template 4

```sql
SELECT TOP (@PageSize) {claimKeyCol}
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate
  AND (@LastSeen IS NULL OR {claimKeyCol} > @LastSeen)
ORDER BY {claimKeyCol} ASC;
```

#### Template 5

```sql
SELECT DISTINCT {columnName}
FROM {tableName}
WHERE CompanyCode = @CompanyCode
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate
  AND {columnName} IS NOT NULL;
```

#### Template 6

```sql
SELECT DISTINCT t.{columnName}
FROM {tableName} t
INNER JOIN {headerTable} h ON t.{claimKeyCol} = h.{claimKeyCol}
WHERE h.CompanyCode = @CompanyCode
  AND h.{dateCol} >= @StartDate
  AND h.{dateCol} <= @EndDate
  AND t.{columnName} IS NOT NULL;
```

#### Template 7

```sql
SELECT a.*
FROM {DefaultAttachmentTable} a
INNER JOIN {headerTable} h ON a.{DefaultClaimKeyColumn} = h.{claimKeyCol}
WHERE h.CompanyCode = @CompanyCode
  AND h.{dateCol} >= @StartDate
  AND h.{dateCol} <= @EndDate;
```

#### Template 8

```sql
SELECT *
FROM {tableName}
WHERE {keyCol} IN ({keyList})
```

#### Template 9

```sql
WITH CTE AS (
    SELECT *, ROW_NUMBER() OVER(PARTITION BY {keyCol} ORDER BY (SELECT NULL)) as rn
    FROM {tableName}
    WHERE {keyCol} IN ({keyList})
)
SELECT * FROM CTE WHERE rn = 1
```

#### Template 10

```sql
SELECT *
FROM {tableName}
WHERE {keyCol} IN ({keyList})
```

### 8.4 Row Conversion Rules (DbDataReader → JSON)

Every provider DB row is converted to a JSON object by reading every column and using the column name as the JSON property name.
Conversion mapping is conservative and deterministic:
- numeric types remain numeric
- DateTime/DateTimeOffset serialize as ISO strings
- byte[] becomes base64 string
- Guid becomes string
- other types become ToString()

This implies a strict contract:
- In Tables/Views mode, provider column names should already match canonical expected property names.
- In Custom SQL mode, SQL must alias columns to canonical names (recommended) or a separate column map must exist.

### 8.5 ClaimBundle Raw Assembly (How claims are assembled)

For each ProIdClaim, the adapter produces:
- Header (single object)
- DhsDoctors (array; single row wrapped into array)
- Services, Diagnoses, Labs, Radiology, Attachments, OpticalVitalSigns, ItemDetails, Achi (arrays)

Batch fetch optimization: fetch each entity using IN-list queries for a page of claim keys, then group rows by ProIdClaim.

### 8.6 Attachments Adapter Contract (Required Source Properties)

The provider attachments query must return JSON properties (case-insensitive) used by the mapper:

| Required property |
| --- |
| AttachBit |
| AttachmentID |
| AttachmentType |
| FileName |
| FileSizeInByte |
| attachmentID |
| location |


Supported source scenarios:
- FilePath (Location contains a file path; may be UNC)
- RawBytesInLocation (Location contains raw bytes)
- Base64InAttachBit (AttachBit contains base64)

## 9. Canonical Claim Payload (ClaimBundleBuilder Rules)

The ClaimBundleBuilder enforces these rules:
- claimHeader must exist.
- proIdClaim must be int-compatible. Canonical field name is `proIdClaim`.
- companyCode must exist; inject from batch if missing.
- monthKey is computed from a date field and stored in SQLite (payload stays canonical).
- Arrays exist (empty arrays allowed).
- proidclaim injection:
  - serviceDetails[].proidclaim is STRING
  - diagnosis/lab/radiology/optical/doctors[].proidclaim is INTEGER
- diagnosis date normalization: DiagnosisDate (or diagnosis_Date) normalized to yyyy-MM-dd.

Accepted header field candidates (case-insensitive):

### ProIdClaimFieldCandidates

| Candidates |
| --- |
| proIdClaim |
| ProIdClaim |
| claimId |
| ClaimId |

### CompanyCodeFieldCandidates

| Candidates |
| --- |
| companyCode |
| CompanyCode |

### DateFieldCandidatesForMonthKey

| Candidates |
| --- |
| invoiceDate |
| InvoiceDate |
| claimDate |
| ClaimDate |
| createdDate |
| CreatedDate |

### PatientNameFieldCandidates

| Candidates |
| --- |
| patientName |
| PatientName |
| name |
| Name |

### PatientIdFieldCandidates

| Candidates |
| --- |
| patientID |
| patientId |
| PatientID |
| PatientId |
| id |
| Id |

### InvoiceNumberFieldCandidates

| Candidates |
| --- |
| invoiceNumber |
| InvoiceNumber |
| billNo |
| BillNo |

### TotalNetAmountFieldCandidates

| Candidates |
| --- |
| totalNetAmount |
| TotalNetAmount |
| netAmount |
| NetAmount |

### ClaimedAmountFieldCandidates

| Candidates |
| --- |
| claimedAmount |
| ClaimedAmount |

## 10. Streams (Detailed Behaviors, Leasing, and Crash Recovery)

Leasing and crash recovery rules (must follow):

- Leasing: all streams that send must lease claim rows (LockedBy + InFlightUntilUtc) to prevent collisions.
- 2. Shared state machine definitions
- 2.1 Claim.EnqueueStatus (int)
- 0 NotSent — staged locally; never successfully sent to SendClaim.
- 1 InFlight — leased by a worker (Sender / Retry / Requeue) right now.
- 2 Enqueued — SendClaim succeeded; equivalent to isFetched=true locally (sent to queue).
- 3 Failed — SendClaim failed; eligible for Stream C retry.
- 2.2 Claim.CompletionStatus (int)
- 0 Unknown — not yet confirmed by GetHISProIdClaimsByBcrId.
- 1 Completed — confirmed inserted downstream; do not requeue again.
- 2.3 Leasing fields (anti-collision contract)
- Fields: Claim.LockedBy (string) + Claim.InFlightUntilUtc (UTC datetime).
- Lease acquisition MUST be atomic: one UPDATE that sets LockedBy and InFlightUntilUtc only when:
- EnqueueStatus in eligible set AND (InFlightUntilUtc is null OR InFlightUntilUtc < now).
- Lease duration: AppSettings.LeaseDurationSeconds (default 120).
- Lease release: on completion, set LockedBy=null and InFlightUntilUtc=null; update EnqueueStatus/NextRetryUtc as needed.
- Crash recovery: if the app crashes, any InFlightUntilUtc that expires makes rows eligible again without manual intervention.
- 2.4 Batch status (int)

Stream C rules (automatic + manual retry):

- 3 Failed — SendClaim failed; eligible for Stream C retry.
- 2.2 Claim.CompletionStatus (int)
- 0 Unknown — not yet confirmed by GetHISProIdClaimsByBcrId.
- 1 Completed — confirmed inserted downstream; do not requeue again.
- 2.3 Leasing fields (anti-collision contract)
- Fields: Claim.LockedBy (string) + Claim.InFlightUntilUtc (UTC datetime).
- Lease acquisition MUST be atomic: one UPDATE that sets LockedBy and InFlightUntilUtc only when:
- EnqueueStatus in eligible set AND (InFlightUntilUtc is null OR InFlightUntilUtc < now).
- Lease duration: AppSettings.LeaseDurationSeconds (default 120).
- Lease release: on completion, set LockedBy=null and InFlightUntilUtc=null; update EnqueueStatus/NextRetryUtc as needed.
- Crash recovery: if the app crashes, any InFlightUntilUtc that expires makes rows eligible again without manual intervention.
- 2.4 Batch status (int)
- 0 Draft — local batch exists; staging may still be running; BcrId may be null.
- 1 Ready — BcrId exists; batch can be dispatched.
- 2 Sending — Stream B currently dispatching packets (UI progress state).
- 3 Enqueued — all known staged claims have been sent at least once; completion not yet confirmed.
- 4 HasResume — resume poll required and/or incompletes detected.
- 5 Completed — all claims confirmed.
- 6 Failed — batch-level terminal failure (rare).
- 3. Stream A — Fetch/Stage (CompanyCode + DateRange)
- Behavior summary
- Fetch all claims for selected CompanyCode and DateRange (start/end) from Provider HIS DB.

Manual retry rules (cooldown and packet rules):

- 5.2 Manual retry rules (packet-level only)
- User cannot retry claim-by-claim; only retry a packet (1..40).
- Manual retry uses either:
- Option A (preferred): re-send a selected failed Dispatch (same 1..40 claims) as DispatchType=RetrySend.
- Option B: build a packet from up to 40 Failed claims within a selected batch.
- Debounce: do not allow another manual retry for the same batch/packet within ManualRetryCooldownMinutes (default 10).
- Implementation note: debounce can be enforced by checking Dispatch.CreatedUtc for DispatchType=RetrySend created within last cooldown window.

Implementation notes:
- Lease acquisition MUST be atomic.
- Lease duration is from AppSettings.LeaseDurationSeconds.
- On completion, clear lease fields.
- On startup, recover expired leases and mark in-flight dispatches accordingly.

## 11. Domain Mapping Specification (How it Works)

Domain mapping normalizes provider values to DHS values.

### 11.1 Storage model
- ApprovedDomainMapping: used for enrichment during sending.
- MissingDomainMapping: discovered values awaiting approval/posted.

### 11.2 Status enums
- MappingStatus: Missing/Posted/Approved/PostFailed.
- DiscoverySource: Api/Scanned.

### 11.3 Baseline scan list (authoritative)

| DomainName | DomainTableId | FieldPath |
| --- | --- | --- |
| PatientGender | Gender | claimHeader.patientGender |
| DoctorGender | Gender | claimHeader.doctorGender |
| ActIncidentCode | ActIncidentCode | claimHeader.actIncidentCode |
| ClaimType | ClaimType | claimHeader.claimType |
| VisitType | VisitType | claimHeader.visitType |
| SubscriberRelationship | SubscriberRelationship | claimHeader.subscriberRelationship |
| MaritalStatus | MaritalStatus | claimHeader.maritalStatus |
| PatientCountry | Country | claimHeader.nationality |
| DoctorCountry | Country | claimHeader.Nationality_Code |
| PatientIDType | PatientIdType | claimHeader.patientIdType |
| EncounterStatus | EncounterStatus | claimHeader.encounterStatus |
| CareType | CareType | claimHeader.careTypeID |
| EncounterType | EncounterType | claimHeader.enconuterTypeID |
| Religion | Religion | claimHeader.patientReligion |
| AdmissionType | AdmissionType | claimHeader.admissionTypeID |
| TriageCategory | TriageCategory | claimHeader.triageCategoryTypeID |
| Department | Department | claimHeader.BenHead |
| DischargeDisposition | DischargeDisposition | claimHeader.DischargeDepositionsTypeID |
| EncounterClass | EncounterClass | claimHeader.enconuterTypeId |
| EmergencyArrivalCode | EmergencyArrivalCode | claimHeader.emergencyArrivalCode |
| DispositionCode | DispositionCode | claimHeader.EmergencyDepositionTypeID |
| InvestigationResult | InvestigationResult | claimHeader.investigationResult |
| PatientOccupation | PatientOccupation | claimHeader.patientOccupation |
| Country | Country | claimHeader.Nationality |
| SubmissionReasonCode | SubmissionReasonCode | serviceDetails.submissionReasonCode |
| TreatmentTypeIndicator | TreatmentTypeIndicator | serviceDetails.treatmentTypeIndicator |
| ServiceType | ServiceType | serviceDetails.serviceType |
| ServiceEventType | ServiceEventType | serviceDetails.serviceEventType |
| Pharmacist Selection Reason | PharmacistSelectionReason | serviceDetails.pharmacistSelectionReason |
| Pharmacist Substitute | PharmacistSubstitute | serviceDetails.pharmacistSubstitute |
| DiagnosisType | DiagnosisType | diagnosisDetails.diagnosisTypeID |
| ExtendedDiagnosisTypeIndicator | ExtendedDiagnosisTypeIndicator | diagnosisDetails.DiagnosisTypeID |
| IllnessTypeIndicator | IllnessTypeIndicator | diagnosisDetails.illnessTypeIndicator |
| ConditionOnset | ConditionOnset | diagnosisDetails.onsetConditionTypeID |
| DoctorReligion | Religion | dhsDoctors.religion_Code |
| DoctorType | DoctorType | dhsDoctors.DoctorType_Code |

### 11.4 Missing mapping discovery workflow
1) During staging, scan the payload for each baseline field path.
2) Extract distinct non-empty values.
3) Upsert MissingDomainMapping rows (provider-scoped).
4) Post missing mappings using InsertMissMappingDomain (non-blocking).

### 11.5 Approved mapping refresh workflow
1) Refresh Provider Configuration.
2) Upsert ApprovedDomainMapping.
3) Enrichment immediately uses new mappings.

### 11.6 Enrichment rules (exact)
When mapping exists, write target field as object: `{ id: TargetValue, code: CodeValue, name: DisplayValue }`.

Enrichment lookup tables:

### claimHeader

| TargetField | DomainName | SourceField |
| --- | --- | --- |
| fK_ClaimType_ID | ClaimType | ClaimType |
| fK_GenderId | PatientGender | PatientGender |
| fK_PatientIDType_ID | PatientIDType | PatientIdType |
| fK_MaritalStatus_ID | MaritalStatus | MaritalStatus |
| fK_Dept_ID | Department | BenHead |
| fK_Nationality_ID | Country | Nationality |
| fK_DischargeDisposition_ID | DischargeDisposition | DischargeDepositionsTypeID |
| fK_EncounterClass_ID | EncounterClass | EnconuterTypeId |
| fK_EncounterStatus_ID | EncounterStatus | EncounterStatus |
| fK_EmergencyArrivalCode_ID | EmergencyArrivalCode | EmergencyArrivalCode |
| fK_ServiceEventType_ID | ServiceEventType | ServiceEventType |
| fK_TriageCategory_ID | TriageCategory | TriageCategoryTypeID |
| fK_DispositionCode_ID | DispositionCode | EmergencyDepositionTypeID |
| fK_InvestigationResult_ID | InvestigationResult | InvestigationResult |
| fK_VisitType_ID | VisitType | VisitType |
| fK_PatientOccupation_Id | PatientOccupation | PatientOccupation |
| fK_SubscriberRelationship_ID | SubscriberRelationship | SubscriberRelationship |

### serviceDetails[]

| TargetField | DomainName | SourceField |
| --- | --- | --- |
| fK_ServiceType_ID | ServiceType | serviceType |
| fK_PharmacistSubstitute_ID | Pharmacist Substitute | PharmacistSubstitute |
| fK_PharmacistSelectionReason_ID | Pharmacist Selection Reason | PharmacistSelectionReason |

### diagnosisDetails[]

| TargetField | DomainName | SourceField |
| --- | --- | --- |
| fK_DiagnosisOnAdmission_ID | ConditionOnset | DiagnosisOnAdmission |
| fK_DiagnosisType_ID | DiagnosisType | DiagnosisTypeID |

### dhsDoctors[]

| TargetField | DomainName | SourceField |
| --- | --- | --- |
| fK_Gender | DoctorGender | DoctorGender |
| fk_DoctorType | DoctorType | DoctorType_Code |
| fK_DoctorReligion | DoctorReligion | religion_Code |

## 12. API Contract (Every Endpoint: Purpose, Timing, Fields)

Swagger defines DTO shapes; this section defines invocation timing and local side-effects.

### POST /api/Authentication/login

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: LoginRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| email | string | email | NO | YES |
| groupID | string |  | YES | NO |
| password | string |  | NO | YES |

Response DTO: LoginResponseDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | LoginResponseDto |  | NO | NO |

---

### POST /api/Batch/CreateBatchRequest

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: CreateBatchRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| batchRequests | array<BatchRequestItemDto> |  | YES | NO |

Response DTO: CreateBatchResponseDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | CreateBatchResponseDto |  | NO | NO |

---

### POST /api/Batch/DeleteBatch

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: DeleteBatchRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| batchId | integer | int64 | NO | NO |

Response DTO: BooleanBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | boolean |  | NO | NO |

---

### GET /api/Batch/GetBatchRequest/{providerDhsCode}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: BatchCreationRequestDtoPagedResultDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | BatchCreationRequestDtoPagedResultDto |  | NO | NO |

---

### GET /api/Claims/GetHISProIdClaimsByBcrId/{bcrId}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: ClaimHISProIdDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | ClaimHISProIdDto |  | NO | NO |

---

### POST /api/Claims/SendClaim

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: array<ClaimDto>

Response DTO: SendClaimResultDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | SendClaimResultDto |  | NO | NO |

---

### GET /api/Dashboard/GetDashboardData/{providerDhsCode}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: DashboardDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | DashboardDto |  | NO | NO |

---

### GET /api/DomainMapping/GetMissingDomainMappings/{providerDhsCode}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: MissingDomainMappingDtoListBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | array<MissingDomainMappingDto> |  | YES | NO |

---

### GET /api/DomainMapping/GetProviderDomainMapping/{providerDhsCode}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: DomainMappingDtoListBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | array<DomainMappingDto> |  | YES | NO |

---

### GET /api/DomainMapping/GetProviderDomainMappingsWithMissing/{providerDhsCode}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: ProviderDomainMappingsDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | ProviderDomainMappingsDto |  | NO | NO |

---

### POST /api/DomainMapping/InsertMissMappingDomain

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: InsertMissMappingDomainRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| providerDhsCode | string |  | YES | NO |
| mismappedItems | array<MismappedDomainItemDto> |  | YES | NO |

Response DTO: InsertMissMappingDomainResultDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | InsertMissMappingDomainResultDto |  | NO | NO |

---

### GET /api/Health/CheckAPIHealth

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: ObjectBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | object |  | YES | NO |

---

### GET /api/Provider/GetProviderCompanyCodes/{providerDhsCode}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: ProviderPayerDtoListBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | array<ProviderPayerDto> |  | YES | NO |

---

### GET /api/Provider/GetProviderConfigration/{providerDhsCode}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: ProviderConfigrationDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | ProviderConfigrationDto |  | NO | NO |

---

### GET /api/Provider/GetProviderInfo/{providerDhsCode}

Purpose and timing are defined in §3 and §4 workflows.

Request DTO: (none)

Response DTO: ProviderInfoDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | ProviderInfoDto |  | NO | NO |

---

## 13. Observability, Support, and Runbooks (PHI-safe)

Key idea: you should be able to debug without payload inspection.

Dispatch/DispatchItem/ApiCallLog provide the primary diagnostic signals.

Support bundle export (PHI-safe):

- 4. Support bundle export (PHI-safe)
- Support bundle is a single ZIP produced from the app UI that enables troubleshooting without PHI.
- 4.1 Contents (allowed)
- App version/build info, OS version, machine id (non-PII if possible).
- Configuration metadata (no secrets): ProviderDhsCode, integration type, DB engine (but not connection strings).
- SQLite extracts (PHI-safe tables only): AppMeta, ProviderProfile (redacted/encrypted fields), ProviderExtractionConfig (without credentials), Batch, Claim (status-only columns), Dispatch, DispatchItem, DomainMapping (optional), ApiCallLog.
- Aggregated metrics snapshots (Section 5).
- Recent sanitized app logs (if file-based logging is used) with strict PHI redaction enforced.
- 4.2 Contents (forbidden)
- ClaimPayload.PayloadJson or any payload fragments.
- Attachment content, base64, or local file paths that contain patient identifiers (prefer hash).
- Provider DB connection strings or credentials.
- Any user passwords or auth tokens.
- 4.3 Export mechanics
- Export from UI: Settings → Support → 'Export Support Bundle'.
- Allow choosing timeframe (last 24h / 7d) to limit size.
- Run a PHI-scan/redaction pass on text logs before including them.
- Include a manifest.json describing included files and redaction policy version.

Forbidden contents:

- 5. Operational metrics (definitions and how to compute)
- Metrics must be PHI-free and computed from status tables + dispatch history. They can be shown in UI and included in the support bundle.
- 5.1 Throughput
- Claims staged per hour: count new Claim rows FirstSeenUtc in window.
- Claims sent per hour: count DispatchItems in succeeded/partially succeeded dispatches per hour.
- Packets per hour: count Dispatch rows created per hour.
- 5.2 Failure rate
- Packet failure rate: % DispatchStatus=Failed over total dispatches in window.
- Claim failure rate: % claims whose latest EnqueueStatus=Failed over total touched claims in window.
- Partial success rate: % DispatchStatus=PartiallySucceeded.

Export mechanics:

- 5.3 Retry backlog
- Due retry count: Failed claims where NextRetryUtc <= now and AttemptCount <= 3.
- Aging: distribution of time since LastUpdatedUtc for Failed claims.
- Manual retry wait: batches currently in manual debounce cooldown (if tracked).
- 5.4 Resume lag
- Enqueued-not-completed count: EnqueueStatus=Enqueued AND CompletionStatus=Unknown.
- Oldest unknown completion age: now - min(LastEnqueuedUtc) where CompletionStatus=Unknown.
- Batches awaiting resume: count Batch where HasResume=1 and not Completed.

Operational metrics definitions:

- 5. Operational metrics (definitions and how to compute)
- Metrics must be PHI-free and computed from status tables + dispatch history. They can be shown in UI and included in the support bundle.
- 5.1 Throughput
- Claims staged per hour: count new Claim rows FirstSeenUtc in window.
- Claims sent per hour: count DispatchItems in succeeded/partially succeeded dispatches per hour.
- Packets per hour: count Dispatch rows created per hour.
- 5.2 Failure rate
- Packet failure rate: % DispatchStatus=Failed over total dispatches in window.
- Claim failure rate: % claims whose latest EnqueueStatus=Failed over total touched claims in window.
- Partial success rate: % DispatchStatus=PartiallySucceeded.
- 5.3 Retry backlog
- Due retry count: Failed claims where NextRetryUtc <= now and AttemptCount <= 3.
- Aging: distribution of time since LastUpdatedUtc for Failed claims.
- Manual retry wait: batches currently in manual debounce cooldown (if tracked).
- 5.4 Resume lag
- Enqueued-not-completed count: EnqueueStatus=Enqueued AND CompletionStatus=Unknown.
- Oldest unknown completion age: now - min(LastEnqueuedUtc) where CompletionStatus=Unknown.
- Batches awaiting resume: count Batch where HasResume=1 and not Completed.

Operator runbook:

- 6. Operator runbook (quick decision tree)
- Is the problem 'nothing is sending'? → Check ApiCallLog for LoadProviderConfiguration and CreateBatchRequest first.
- Is the problem 'sending but many failures'? → Check Dispatch + ApiCallLog for SendClaim patterns (status codes, duration).
- Is the problem 'stuck Enqueued'? → Check resume polling calls and HasResume batches.
- Is the problem 'attachments missing'? → Check attachment upload/post calls and attachment type scenarios.
- If unresolved → export support bundle and escalate with correlationIds and recent dispatch ids.
- 7. Glossary
- Dispatch: one outbound packet (1..40 claims).
- DispatchItem: membership of claims within a dispatch.
- ApiCallLog: PHI-safe record of an HTTP call and its timings.
- CorrelationId: GUID used to correlate calls and operations.
- HasResume: indicates batch requires completion confirmation via resume flow.
- Resume lag: time between Enqueued and Completed confirmation.

## 14. Acceptance Tests (Must Pass)

- Onboarding: create ProviderProfile and ProviderExtractionConfig; test DB connection.
- Login: token obtained; provider configuration cached; payers/mappings upserted.
- Batch: create -> Stream A stages claims -> missing mappings discovered/posted -> Stream B sends -> Stream C retries -> Stream D completes.
- Domain mapping: new domain discovered -> appears in Missing -> posted -> approved arrives -> enrichment applied.
- Attachments: manual upload for completed batch -> upload to blob -> register -> failures auto retried by retry stream.
- Observability: ApiCallLog created for every API call; Dispatch/DispatchItem created for each send; support bundle exports without PHI.

## Appendix A — Swagger DTO Catalog (Field-by-Field)

### Schema: BatchCreationRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| bcrId | integer | int64 | NO | NO |
| bcrCreatedOn | string | date-time | YES | NO |
| bcrMonth | integer | int32 | YES | NO |
| bcrYear | integer | int32 | YES | NO |
| userName | string |  | YES | NO |
| payerNameEn | string |  | YES | NO |
| payerNameAr | string |  | YES | NO |
| companyCode | string |  | YES | NO |
| midTableTotalClaim | integer | int32 | YES | NO |
| batchStatus | string |  | YES | NO |

### Schema: BatchCreationRequestDtoPagedResultDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| data | array<BatchCreationRequestDto> |  | YES | NO |
| pageNumber | integer | int32 | NO | NO |
| pageSize | integer | int32 | NO | NO |
| totalCount | integer | int32 | NO | NO |
| totalPages | integer | int32 | NO | NO |
| hasPreviousPage | boolean |  | NO | NO |
| hasNextPage | boolean |  | NO | NO |

### Schema: BatchCreationRequestDtoPagedResultDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | BatchCreationRequestDtoPagedResultDto |  | NO | NO |

### Schema: BatchPayerChartDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| batchId | integer | int64 | NO | NO |
| createdOn | string | date-time | YES | NO |
| month | integer | int32 | YES | NO |
| year | integer | int32 | YES | NO |
| batchStatus | string |  | YES | NO |
| payerNameEn | string |  | YES | NO |
| payerNameAr | string |  | YES | NO |
| totalClaims | integer | int32 | YES | NO |

### Schema: BatchRequestItemDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| companyCode | string |  | YES | NO |
| batchStartDate | string | date-time | NO | NO |
| batchEndDate | string | date-time | NO | NO |
| totalClaims | integer | int32 | YES | NO |
| providerDhsCode | string |  | YES | NO |

### Schema: BatchStatusChartDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| statusName | string |  | YES | NO |
| statusDisplay | string |  | YES | NO |
| count | integer | int32 | NO | NO |
| percentage | number | double | NO | NO |

### Schema: BooleanBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | boolean |  | NO | NO |

### Schema: ClaimDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| claimHeader | DHSClaimHeaderDto |  | NO | NO |
| opticalVitalSigns | array<DHSOpticalVitalSignDto> |  | YES | NO |
| diagnosisDetails | array<DHSDiagnosisDetailDto> |  | YES | NO |
| labDetails | array<DHSLabDetailDto> |  | YES | NO |
| radiologyDetails | array<DHSRadiologyDetailDto> |  | YES | NO |
| serviceDetails | array<DHSServiceDetailDto> |  | YES | NO |

### Schema: ClaimHISProIdDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| hisProIdClaim | array<integer> |  | YES | NO |

### Schema: ClaimHISProIdDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | ClaimHISProIdDto |  | NO | NO |

### Schema: CommonType

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| id | string |  | YES | NO |
| code | string |  | YES | NO |
| name | string |  | YES | NO |
| ksaID | string |  | YES | NO |
| ksaCode | string |  | YES | NO |
| ksaName | string |  | YES | NO |

### Schema: CreateBatchRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| batchRequests | array<BatchRequestItemDto> |  | YES | NO |

### Schema: CreateBatchResponseDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| createdBatchIds | array<integer> |  | YES | NO |

### Schema: CreateBatchResponseDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | CreateBatchResponseDto |  | NO | NO |

### Schema: DHSClaimHeaderDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| proIdClaim | integer | int32 | NO | NO |
| doctorCode | string |  | YES | NO |
| proRequestDate | string | date-time | YES | NO |
| patientName | string |  | YES | NO |
| patientNo | string |  | YES | NO |
| memberID | string |  | YES | NO |
| preAuthID | string |  | YES | NO |
| specialty | string |  | YES | NO |
| clinicalData | string |  | YES | NO |
| claimType | string |  | YES | NO |
| referInd | boolean |  | YES | NO |
| emerInd | boolean |  | YES | NO |
| submitBy | string |  | YES | NO |
| payTo | string |  | YES | NO |
| temperature | string |  | YES | NO |
| respiratoryRate | string |  | YES | NO |
| bloodPressure | string |  | YES | NO |
| height | string |  | YES | NO |
| weight | string |  | YES | NO |
| pulse | string |  | YES | NO |
| internalNotes | string |  | YES | NO |
| companyCode | string |  | YES | NO |
| policyHolderName | string |  | YES | NO |
| policyHolderNo | string |  | YES | NO |
| class | string |  | YES | NO |
| invoiceNumber | string |  | YES | NO |
| invoiceDate | string | date-time | YES | NO |
| treatmentCountryCode | string |  | YES | NO |
| destinationCode | integer | int32 | YES | NO |
| providerCode | string |  | YES | NO |
| claimedAmount | number | double | YES | NO |
| claimedAmountSAR | number | double | YES | NO |
| totalNetAmount | number | double | YES | NO |
| totalDiscount | number | double | YES | NO |
| totalDeductible | number | double | YES | NO |
| benType | string |  | YES | NO |
| benHead | string |  | YES | NO |
| provider_dhsCode | integer | int32 | YES | NO |
| dhsm | boolean |  | YES | NO |
| batchNo | string |  | YES | NO |
| batchDate | string | date-time | YES | NO |
| batchStartDate | string | date-time | YES | NO |
| batchEndDate | string | date-time | YES | NO |
| patientID | string |  | YES | NO |
| dob | string | date-time | YES | NO |
| age | integer | int32 | YES | NO |
| nationality | string |  | YES | NO |
| durationOFIllness | integer | int32 | YES | NO |
| chiefComplaint | string |  | YES | NO |
| maritalStatus | string |  | YES | NO |
| patientGender | string |  | YES | NO |
| subscriberRelationship | string |  | YES | NO |
| visitType | string |  | YES | NO |
| plan_Type | string |  | YES | NO |
| msvRef | string |  | YES | NO |
| userID | integer | int32 | YES | NO |
| issueNo | string |  | YES | NO |
| userName | string |  | YES | NO |
| payerRef | string |  | YES | NO |
| dhsc | string |  | YES | NO |
| errorMessage | string |  | YES | NO |
| mobileNo | string |  | YES | NO |
| pmaUserName | string |  | YES | NO |
| admissionDate | string | date-time | YES | NO |
| dischargeDate | string | date-time | YES | NO |
| admissionNo | string |  | YES | NO |
| admissionSpecialty | string |  | YES | NO |
| admissionType | string |  | YES | NO |
| roomNumber | string |  | YES | NO |
| bedNumber | string |  | YES | NO |
| dischargeSpecialty | string |  | YES | NO |
| lengthOfStay | string |  | YES | NO |
| admissionWeight | string |  | YES | NO |
| dischargeMode | string |  | YES | NO |
| iDno | string |  | YES | NO |
| emergencyArrivalCode | string |  | YES | NO |
| emergencyStartDate | string | date-time | YES | NO |
| emergencyWaitingTime | string |  | YES | NO |
| triageDate | string | date-time | YES | NO |
| admissionTypeID | string |  | YES | NO |
| dischargeDepositionsTypeID | string |  | YES | NO |
| enconuterTypeID | string |  | YES | NO |
| careTypeID | string |  | YES | NO |
| triageCategoryTypeID | string |  | YES | NO |
| emergencyDepositionTypeID | string |  | YES | NO |
| significantSigns | string |  | YES | NO |
| mainSymptoms | string |  | YES | NO |
| dischargeSummary | string |  | YES | NO |
| otherConditions | string |  | YES | NO |
| encounterStatus | string |  | YES | NO |
| encounterNo | string |  | YES | NO |
| priority | string |  | YES | NO |
| patientIdType | string |  | YES | NO |
| actIncidentCode | string |  | YES | NO |
| episodeNumber | string |  | YES | NO |
| cchi | string |  | YES | NO |
| branchID | string |  | YES | NO |
| newBorn | boolean |  | YES | NO |
| oxygenSaturation | string |  | YES | NO |
| birthWeight | string |  | YES | NO |
| isFetched | boolean |  | YES | NO |
| isSync | boolean |  | YES | NO |
| fetchDate | string | date-time | YES | NO |
| relatedClaim | integer | int64 | YES | NO |
| bCR_Id | integer | int64 | YES | NO |
| patientOccupation | string |  | YES | NO |
| causeOfDeath | string |  | YES | NO |
| treatmentPlan | string |  | YES | NO |
| patientHistory | string |  | YES | NO |
| physicalExamination | string |  | YES | NO |
| historyOfPresentIllness | string |  | YES | NO |
| patientReligion | string |  | YES | NO |
| morphologyCode | string |  | YES | NO |
| investigationResult | string |  | YES | NO |
| snomedCTCode | string |  | YES | NO |
| ventilationhours | integer | int32 | YES | NO |
| fK_ClaimType_ID | CommonType |  | NO | NO |
| fK_ClaimSubtype_ID | CommonType |  | NO | NO |
| fK_GenderId | CommonType |  | NO | NO |
| fK_PatientIDType_ID | CommonType |  | NO | NO |
| fK_MaritalStatus_ID | CommonType |  | NO | NO |
| fK_Dept_ID | CommonType |  | NO | NO |
| fK_Nationality_ID | CommonType |  | NO | NO |
| fK_BirthCountry_ID | CommonType |  | NO | NO |
| fK_BirthCity_ID | CommonType |  | NO | NO |
| fK_Religion_ID | CommonType |  | NO | NO |
| fK_AdmissionSpecialty_ID | CommonType |  | NO | NO |
| fK_DischargeSpeciality_ID | CommonType |  | NO | NO |
| fK_DischargeDisposition_ID | CommonType |  | NO | NO |
| fK_EncounterAdmitSource_ID | CommonType |  | NO | NO |
| fK_EncounterClass_ID | CommonType |  | NO | NO |
| fK_EncounterStatus_ID | CommonType |  | NO | NO |
| fK_ReAdmission_ID | CommonType |  | NO | NO |
| fK_EmergencyArrivalCode_ID | CommonType |  | NO | NO |
| fK_ServiceEventType_ID | CommonType |  | NO | NO |
| fK_IntendedLengthOfStay_ID | CommonType |  | NO | NO |
| fK_TriageCategory_ID | CommonType |  | NO | NO |
| fK_DispositionCode_ID | CommonType |  | NO | NO |
| fK_InvestigationResult_ID | integer | int32 | YES | NO |
| fK_VisitType_ID | CommonType |  | NO | NO |
| fK_PatientOccupation_Id | CommonType |  | NO | NO |
| fK_SubscriberRelationship_ID | CommonType |  | NO | NO |
| hisProIdClaim | integer | int32 | YES | NO |

### Schema: DHSDiagnosisDetailDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| diagnosisID | integer | int32 | NO | NO |
| diagnosisCode | string |  | YES | NO |
| diagnosisDesc | string |  | YES | NO |
| proIdClaim | integer | int32 | NO | NO |
| errorMessage | string |  | YES | NO |
| dhsi | integer | int32 | YES | NO |
| diagnosisTypeID | string |  | YES | NO |
| diagnosisDate | string | date | YES | NO |
| onsetConditionTypeID | string |  | YES | NO |
| illnessTypeIndicator | string |  | YES | NO |
| diagnosisType | string |  | YES | NO |
| fK_DiagnosisType_ID | integer | int32 | YES | NO |
| fK_ConditionOnset_ID | integer | int32 | YES | NO |
| diagnosis_Cloud_Desc | string |  | YES | NO |
| ageLow | string |  | YES | NO |
| ageHigh | string |  | YES | NO |
| isUnacceptPrimaryDiagnosis | boolean |  | YES | NO |
| genderID | integer | int32 | YES | NO |
| diagnosis_Order | integer | int32 | YES | NO |
| diagnosis_Date | string | date-time | YES | NO |
| fK_DiagnosisOnAdmission_ID | CommonType |  | NO | NO |
| isMorphologyRequired | boolean |  | YES | NO |
| isRTAType | boolean |  | YES | NO |
| isWPAType | boolean |  | YES | NO |

### Schema: DHSLabDetailDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| labID | integer | int32 | NO | NO |
| labProfile | string |  | YES | NO |
| labTestName | string |  | YES | NO |
| labResult | string |  | YES | NO |
| labUnits | string |  | YES | NO |
| labLow | string |  | YES | NO |
| labHigh | string |  | YES | NO |
| labSection | string |  | YES | NO |
| visitDate | string | date-time | YES | NO |
| proIdClaim | integer | int32 | NO | NO |
| dhsi | integer | int32 | YES | NO |
| resultDate | string | date-time | YES | NO |
| serviceCode | string |  | YES | NO |
| loincCode | string |  | YES | NO |

### Schema: DHSOpticalVitalSignDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| serialNo | integer | int32 | NO | NO |
| rightEyeSphere_Distance | string |  | YES | NO |
| rightEyeSphere_Near | string |  | YES | NO |
| rightEyeCylinder_Distance | string |  | YES | NO |
| rightEyeCylinder_Near | string |  | YES | NO |
| rightEyeAxis_Distance | string |  | YES | NO |
| rightEyeAxis_Near | string |  | YES | NO |
| rightEyePrism_Distance | string |  | YES | NO |
| rightEyePrism_Near | string |  | YES | NO |
| rightEyePD_Distance | string |  | YES | NO |
| rightEyePD_Near | string |  | YES | NO |
| rightEyeBifocal | string |  | YES | NO |
| leftEyeSphere_Distance | string |  | YES | NO |
| leftEyeSphere_Near | string |  | YES | NO |
| leftEyeCylinder_Distance | string |  | YES | NO |
| leftEyeCylinder_Near | string |  | YES | NO |
| leftEyeAxis_Distance | string |  | YES | NO |
| leftEyeAxis_Near | string |  | YES | NO |
| leftEyePrism_Distance | string |  | YES | NO |
| leftEyePrism_Near | string |  | YES | NO |
| leftEyePD_Distance | string |  | YES | NO |
| leftEyePD_Near | string |  | YES | NO |
| leftEyeBifocal | string |  | YES | NO |
| vertex | string |  | YES | NO |
| proIdClaim | integer | int32 | YES | NO |
| serviceCode | string |  | YES | NO |
| rightEyePower_Distance | number | double | YES | NO |
| rightEyePower_Near | number | double | YES | NO |
| leftEyePower_Distance | number | double | YES | NO |
| leftEyePower_Near | number | double | YES | NO |
| prismDirection | string |  | YES | NO |

### Schema: DHSRadiologyDetailDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| radiologyID | integer | int32 | NO | NO |
| serviceCode | string |  | YES | NO |
| serviceDescription | string |  | YES | NO |
| clinicalData | string |  | YES | NO |
| radiologyResult | string |  | YES | NO |
| visitDate | string | date-time | YES | NO |
| proIdClaim | integer | int32 | YES | NO |
| dhsi | integer | int32 | YES | NO |
| resultDate | string | date-time | YES | NO |

### Schema: DHSServiceDetailDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| serviceID | integer | int32 | NO | NO |
| itemRefernce | string |  | YES | NO |
| treatmentFromDate | string | date-time | YES | NO |
| treatmentToDate | string | date-time | YES | NO |
| numberOfIncidents | number | double | YES | NO |
| lineClaimedAmount | number | double | YES | NO |
| coInsurance | number | double | YES | NO |
| coPay | number | double | YES | NO |
| lineItemDiscount | number | double | YES | NO |
| netAmount | number | double | YES | NO |
| serviceCode | string |  | YES | NO |
| serviceDescription | string |  | YES | NO |
| mediCode | string |  | YES | NO |
| toothNo | string |  | YES | NO |
| submissionReasonCode | integer | int32 | YES | NO |
| treatmentTypeIndicator | integer | int32 | YES | NO |
| serviceGategory | string |  | YES | NO |
| proIdClaim | string |  | YES | NO |
| errorMessage | string |  | YES | NO |
| vatIndicator | boolean |  | YES | NO |
| vatPercentage | number | double | YES | NO |
| patientVatAmount | number | double | YES | NO |
| netVatAmount | number | double | YES | NO |
| cashAmount | number | double | YES | NO |
| pbmDuration | integer | int32 | YES | NO |
| pbmTimes | integer | int32 | YES | NO |
| pbmUnit | integer | int32 | YES | NO |
| pbmPer | string |  | YES | NO |
| pbmUnitType | string |  | YES | NO |
| serviceEventType | string |  | YES | NO |
| ardrg | string |  | YES | NO |
| serviceType | string |  | YES | NO |
| preAuthId | string |  | YES | NO |
| doctorCode | string |  | YES | NO |
| unitPrice | number | double | YES | NO |
| discPercentage | number | double | YES | NO |
| workOrder | string | date-time | YES | NO |
| pharmacistSelectionReason | string |  | YES | NO |
| pharmacistSubstitute | string |  | YES | NO |
| scientificCode | string |  | YES | NO |
| diagnosisCode | string |  | YES | NO |
| scientificCodeAbsenceReason | string |  | YES | NO |
| maternity | boolean |  | YES | NO |
| service_NphiesServiceCode | string |  | YES | NO |
| service_SystemURL | string |  | YES | NO |
| service_NphiesServiceType | string |  | YES | NO |
| nphiesServiceTypeID | integer | int32 | YES | NO |
| service_LongDescription | string |  | YES | NO |
| service_ShortDescription | string |  | YES | NO |
| isUnlistedCode | boolean |  | YES | NO |
| daysSupply | integer | int32 | YES | NO |
| priceListPrice | number | double | YES | NO |
| priceListDiscount | number | double | YES | NO |
| fK_ServiceType_ID | CommonType |  | NO | NO |
| fK_PharmacistSubstitute_ID | CommonType |  | NO | NO |
| fK_PharmacistSelectionReason_ID | CommonType |  | NO | NO |
| hasResult | boolean |  | YES | NO |
| factor | number | double | YES | NO |
| tax | number | double | YES | NO |

### Schema: DashboardDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| providerDhsCode | string |  | YES | NO |
| totalBatches | integer | int32 | NO | NO |
| batchStatusChart | array<BatchStatusChartDto> |  | YES | NO |
| batchPayerChart | array<BatchPayerChartDto> |  | YES | NO |

### Schema: DashboardDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | DashboardDto |  | NO | NO |

### Schema: DeleteBatchRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| batchId | integer | int64 | NO | NO |

### Schema: DomainMappingDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| domTable_ID | integer | int32 | YES | NO |
| providerDomainCode | string |  | YES | NO |
| providerDomainValue | string |  | YES | NO |
| dhsDomainValue | integer | int32 | YES | NO |
| isDefault | boolean |  | YES | NO |
| codeValue | string |  | YES | NO |
| displayValue | string |  | YES | NO |

### Schema: DomainMappingDtoListBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | array<DomainMappingDto> |  | YES | NO |

### Schema: GeneralConfigurationDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| leaseDurationSeconds | integer | int32 | YES | NO |
| streamAIntervalSeconds | integer | int32 | YES | NO |
| resumePollIntervalSeconds | integer | int32 | YES | NO |
| apiTimeout | integer | int32 | YES | NO |
| configCacheTtlMinutes | integer | int32 | YES | NO |
| fetchIntervalMinutes | integer | int32 | YES | NO |
| manualRetryCooldownMinutes | integer | int32 | YES | NO |

### Schema: InsertMissMappingDomainRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| providerDhsCode | string |  | YES | NO |
| mismappedItems | array<MismappedDomainItemDto> |  | YES | NO |

### Schema: InsertMissMappingDomainResultDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| totalItems | integer | int32 | NO | NO |
| insertedCount | integer | int32 | NO | NO |
| skippedCount | integer | int32 | NO | NO |
| skippedItems | array<string> |  | YES | NO |

### Schema: InsertMissMappingDomainResultDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | InsertMissMappingDomainResultDto |  | NO | NO |

### Schema: LoginRequestDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| email | string | email | NO | YES |
| groupID | string |  | YES | NO |
| password | string |  | NO | YES |

### Schema: LoginResponseDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | object |  | YES | NO |

### Schema: LoginResponseDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | LoginResponseDto |  | NO | NO |

### Schema: MismappedDomainItemDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| providerCodeValue | string |  | YES | NO |
| providerNameValue | string |  | YES | NO |
| domainTableId | integer | int32 | NO | NO |

### Schema: MissingDomainMappingDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| providerCodeValue | string |  | YES | NO |
| providerNameValue | string |  | YES | NO |
| domainTableId | integer | int32 | NO | NO |
| domainTableName | string |  | YES | NO |

### Schema: MissingDomainMappingDtoListBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | array<MissingDomainMappingDto> |  | YES | NO |

### Schema: ObjectBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | object |  | YES | NO |

### Schema: ProviderConfigrationDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| providerInfo | ProviderInfoDto |  | NO | NO |
| providerPayers | array<ProviderPayerDto> |  | YES | NO |
| domainMappings | array<DomainMappingDto> |  | YES | NO |
| missingDomainMappings | array<MissingDomainMappingDto> |  | YES | NO |
| generalConfiguration | GeneralConfigurationDto |  | NO | NO |

### Schema: ProviderConfigrationDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | ProviderConfigrationDto |  | NO | NO |

### Schema: ProviderDomainMappingsDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| domainMappings | array<DomainMappingDto> |  | YES | NO |
| missingDomainMappings | array<MissingDomainMappingDto> |  | YES | NO |

### Schema: ProviderDomainMappingsDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | ProviderDomainMappingsDto |  | NO | NO |

### Schema: ProviderInfoDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| provider_ID | integer | int32 | NO | NO |
| providerCCHI | string |  | YES | NO |
| providerNameAr | string |  | YES | NO |
| providerNameEn | string |  | YES | NO |
| connectionString | string |  | YES | NO |
| providerDHSCode | string |  | YES | NO |
| providerToken | string |  | YES | NO |
| providerVendor | string |  | YES | NO |
| integrationType | string |  | YES | NO |
| dataBaseType | string |  | YES | NO |

### Schema: ProviderInfoDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | ProviderInfoDto |  | NO | NO |

### Schema: ProviderPayerDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| payerId | integer | int32 | YES | NO |
| companyCode | string |  | YES | NO |
| payerNameEn | string |  | YES | NO |
| parentPayerNameEn | string |  | YES | NO |

### Schema: ProviderPayerDtoListBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | array<ProviderPayerDto> |  | YES | NO |

### Schema: SendClaimResultDto

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| successClaimsProidClaim | array<integer> |  | YES | NO |
| totalClaims | integer | int32 | NO | NO |
| successCount | integer | int32 | NO | NO |
| failureCount | integer | int32 | NO | NO |

### Schema: SendClaimResultDtoBaseResponse

| Field | Type | Format | Nullable | Required |
| --- | --- | --- | --- | --- |
| succeeded | boolean |  | NO | NO |
| statusCode | integer | int32 | NO | NO |
| message | string |  | YES | NO |
| errors | array<string> |  | YES | NO |
| data | SendClaimResultDto |  | NO | NO |

