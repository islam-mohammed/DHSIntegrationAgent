# DHS Integration Agent — Junior Developer Onboarding

_Generated 2026-02-10 1513 UTC
This document consolidates all text from the attached Knowledge Base Word (.docx) files and swagger.json into one developer-friendly guide.
Where the currently attached solution (`DHSIntegrationAgent.zip`) already implements behavior, the code is treated as the source of truth for those implemented parts.

## Decisions (confirmed)
- SQLite encryption-at-rest SQLCipher (target)
- Packaging MSIX (target)

 Note The current solution codebase still uses AES-GCM column encryption + DPAPI and does not yet include MSIX packaging artifacts. This guide calls that out explicitly so you can code against the real repo state today, while tracking the agreed target changes.


## 1) What this application does
The DHS Integration Agent is a Windows WPF desktop agent that
- Connects to a provider’s local database (tablesviewscustom SQL)
- Extracts claim-related data and builds a canonical payload
- Talks to a backend API (gzip JSON supported) to fetch batch requests, send claims, manage domain mappings, and resumeretry
- Persists operational state in a local SQLite DB (staging, dispatch, logs, settings)
- Provides a UI for setuplogin, dashboard, batches, mapping resolution, attachments, and diagnostics.


## 2) Quick start (developer)
### Prerequisites
- Visual Studio with WPF workload
- .NET SDK matching the repo TargetFramework (currently `net10.0-windows` in the App project)
- Access to the backend API base URL (Swagger provided) and a provider DHS code

### Build & run
### 1. Open the solution (`DHSIntegrationAgent.slnx`).
### 2. Set `DHSIntegrationAgent.App` as startup.
### 3. Configure `ApiBaseUrl` (see Configuration below).
### 4. Run.

### First-run behavior
- The app will createupgrade SQLite via migrations.
- Secrets must not be stored in jsonenv; they belong in secure SQLite settings.


## 3) Solution structure (as implemented)
Projects (current repo)
- `DHSIntegrationAgent.Contracts` DTOs and contract types used across layers (should align with swagger DTO shapes).
- `DHSIntegrationAgent.Domain` core domain entitiesvalue objects.
- `DHSIntegrationAgent.Application` use-cases, abstractions, business rules, service interfaces.
- `DHSIntegrationAgent.Infrastructure` SQLite persistence, HTTP client implementations, security implementations.
- `DHSIntegrationAgent.Adapters` provider DB adapters + claim bundle builder.
- `DHSIntegrationAgent.Workers` worker engine (currently stubbed).
- `DHSIntegrationAgent.App` WPF UI (MVVM).

Key implemented files
- Encryption `srcDHSIntegrationAgent.InfrastructureSecurityAesGcmColumnEncryptor.cs`
- KeyRing `srcDHSIntegrationAgent.InfrastructureSecuritySqliteKeyRing.cs`
- Migrations `srcDHSIntegrationAgent.InfrastructurePersistenceSqliteSqliteMigrations.cs`
- Gzip `srcDHSIntegrationAgent.InfrastructureHttpGzipJsonHttpContent.cs`
- BatchClient `srcDHSIntegrationAgent.InfrastructureHttpclientsBatchClient.cs`
- ClaimsClient `srcDHSIntegrationAgent.InfrastructureHttpclientsClaimsClient.cs`
- AuthClient `srcDHSIntegrationAgent.InfrastructureHttpclientsAuthClient.cs`
- WorkerStub `srcDHSIntegrationAgent.WorkersWorkerEngineStub.cs`
- Appsettings `srcDHSIntegrationAgent.Appappsettings.json`


## 4) Configuration & secrets (implemented behavior)
Configuration sources (highest priority last)
### 1. `appsettings.json`  `appsettings.Development.json` (non-secret)
### 2. Environment variables prefixed with `DHSAGENT_` (non-secret)
### 3. SQLite `AppSettings`  secure settings (DPAPI-protected blobs)

Non-secret examples
- `ApiBaseUrl` (required)
- `ApiTimeoutSeconds`
- `Worker` intervals

Secrets must NOT live in jsonenvlogs, including
- Provider DB connection stringspasswords
- Azure Blob SAS URLs
- Tokens

### Important repo currently enforces secret policy
The current code contains checks that `AzureBlobSasUrl` must not be set via JSONenv.

### How to set API BaseUrl for dev
Use env var nesting convention
- `DHSAGENT_Api__BaseUrl=httpsbackend.example`


## 5) Backend API (swagger.json)
### Endpoints
 Method  Path  Tag 
 ---  ---  --- 
 POST  apiAuthenticationlogin  Authentication 
 POST  apiBatchCreateBatchRequest  Batch 
 POST  apiBatchDeleteBatch  Batch 
 GET  apiBatchGetBatchRequest{providerDhsCode}  Batch 
 GET  apiClaimsGetHISProIdClaimsByBcrId{bcrId}  Claims 
 POST  apiClaimsSendClaim  Claims 
 GET  apiDashboardGetDashboardData{providerDhsCode}  Dashboard 
 GET  apiDomainMappingGetMissingDomainMappings{providerDhsCode}  DomainMapping 
 GET  apiDomainMappingGetProviderDomainMapping{providerDhsCode}  DomainMapping 
 GET  apiDomainMappingGetProviderDomainMappingsWithMissing{providerDhsCode}  DomainMapping 
 POST  apiDomainMappingInsertMissMappingDomain  DomainMapping 
 GET  apiHealthCheckAPIHealth  Health 
 GET  apiProviderGetProviderCompanyCodes{providerDhsCode}  Provider 
 GET  apiProviderGetProviderConfigration{providerDhsCode}  Provider 
 GET  apiProviderGetProviderInfo{providerDhsCode}  Provider 

### DTO shape rule (important)
Any data stored in SQLite (and migrations) should match the backend responserequest DTO shapes defined in Swagger.
This keeps caching and staging compatible with API evolution.


## 6) SQLite persistence (spec + implemented)
The app uses SQLite for
- app settings + secure settings
- provider profilesconfig cache
- extraction config
- staging claimspayloads
- dispatch queuesitems
- domain mappings + validation issues
- attachments
- API call logs + diagnostics

### Current implementation note
- Migrations and repositories are already present in `DHSIntegrationAgent.InfrastructurePersistenceSqlite`.
- PHI fields are currently stored as encrypted BLOBs using AES-GCM.

### Target decision SQLCipher
You selected SQLCipher as the target encryption-at-rest mechanism. Migration plan
### 1. Introduce SQLCipher provider (either `SQLitePCLRaw.bundle_e_sqlcipher` or vendor supported package).
### 2. Update connection factory to open encrypted DB with key.
### 3. Replace column-encryption usage with whole-file encryption where appropriate.
### 4. Provide one-time migration strategy (read old DB, write new SQLCipher DB), or start fresh for dev.

 Until that migration is implemented, the repo still uses AES-GCM column encryption.


## 7) Worker engine & Streams (spec vs current code)
### Spec (from Stream Specs)
The system describes Stream A–D behavior for
- fetchingstaging batch requests
- sending claims
- retrying failures
- resuming by BCR id  requeue
- attachments (with endpoint TBD)

### Current code status
- Worker engine exists as `WorkerEngineStub` (startstop only).
- Stream processors are not yet implemented.

Implication for a junior dev
- UI and persistence exist, API clients exist, but the background automation loop is not wired up to perform Stream A–D end-to-end.


## 8) Provider DB integration (spec)
Provider DB adapters support
- Connecting to provider DB engines
- Reading tablesviews
- Running custom SQL templates
- Building a canonical ClaimBundle

Current code includes adapter scaffolding in `DHSIntegrationAgent.Adapters`.


## 9) Domain Mapping (spec + API)
Domain Mapping workflow involves
- Fetching provider domain mappings
- Detecting missing mappings
- Posting missing mapping resolutions back to backend
- Refreshing approved mappings

Swagger endpoints
- `GET apiDomainMappingGetProviderDomainMapping{providerDhsCode}`
- `GET apiDomainMappingGetMissingDomainMappings{providerDhsCode}`
- `POST apiDomainMappingInsertMissMappingDomain`

UI surfaces mapping views (`DomainMappingsView`, `MappingDomainView`).


## 10) Attachments (spec + current status)
The system includes an attachments concept and UI view.
- The plan notes an attachments metadata endpoint is TBD.
- The repo includes attachment persistence and a WPF view, but end-to-end uploadingposting is not fully implemented.

Target decision MSIX packaging will affect file system access (local storage paths) and should be validated once packaging work begins.


## 11) Security & PHI handling (KB + implemented)
Key requirements
- PHI must be protected at rest and in transit
- No secrets in appsettingsenvlogs
- Logging must avoid PHI leakage
- Use secure defaults (timeouts, retries, validation)

Current implementation
- Column encryption exists (AES-GCM) and uses DPAPI-protected key material.
- ApiCall logging exists; ensure it is sanitized.

Target decision
- Move to SQLCipher whole-file encryption for SQLite.


## 12) Observability & support
The app supports
- Structured logging
- Persisted ApiCall logs for diagnostics
- Support bundle export (as per Observability spec)

UI includes Diagnostics and Logs views.


## 13) Packaging (target)
You selected MSIX packaging.
Implementation notes
- Add `Package.appxmanifest` and MSIX packaging project or configure msbuild packaging.
- Ensure signing strategy is documented (dev self-signed vs enterprise cert).
- Validate local data paths MSIX apps should use LocalAppDataapp container safe locations.
- Validate auto-update approach (AppInstaller or enterprise deployment tool).

Current repo status packaging artifacts not yet present.


## 14) Rules update (KB)
The KB includes `rules_update.docx` which contains incremental rule changes. This document’s content is included verbatim in the Appendix.

Developer guidance
- Treat rules_update as additive overrides to earlier specs.
- When implemented behavior exists in code, code wins.


## 15) Current implementation snapshot (solution overrides)
### Implemented (in current code)
- Layered solution structure Contracts  Domain  Application  Infrastructure  Adapters  Workers  App (WPF).
- SQLite persistence layer with migrations, unit-of-work, repositories for batchesclaimsdispatchdomain mappingsattachmentsapi call logs.
- Column-level PHI encryption implemented using AES-GCM + DPAPI key ring (note you selected SQLCipher as target; see decisions).
- HTTP client stack with optional gzip JSON for POST requests; typed clients for Login, Provider Configuration, Batch, Claims, Domain Mapping, Resume.
- WPF MVVM shell with views for SetupLoginDashboardBatchesCreateBatchDomainMappingsMappingDomainAttachmentsDiagnosticsLogsSettings.
- Basic diagnosticslog viewing models, ApiCall logging plumbing.
- Worker engine currently a stub (StartStop only); stream processors not yet implemented.

### Not yet implemented (gaps vs spec)
- Worker engine orchestration + Stream A–D processors.
- SQLCipher migration.
- MSIX packaging.
- Attachments metadata endpoint integration (still TBD per plan).



---
# Appendix A — Full Knowledge Base source text (verbatim extract)
The following sections contain the extracted text from the KB Word documents, included in full to preserve details.



# PRD_v### 1.0.docx

## PRD — Provider-Side Data Agent (WPF)


### ### 1. Purpose
Build a provider-side desktop agent (single user, single provider per installation) that extracts claims from the provider HIS database, stages them locally, submits them to backend queue in packets of ### 1..40, validates and logs issues without blocking sending, posts missing domain mappings, confirms completion via Resume endpoint, and uploads attachments after completion.
### ### 2. Scope
In scope WPF UI, background worker engine (Streams A–D), provider DB extraction, local SQLite coordination DB, API integration, gzip HTTP, retryresumerequeue, attachment upload to Azure Blob.
Out of scope Multi-provider per user (future), backend queue consumer implementation, domain mapping approval workflow on backend.
### ### 3. Key constraints & assumptions
Single-user solution for a single provider (at this time).
Thousands of claims per run; fetch interval default 5 minutes (configurable).
Contains PHI → strict security encrypt at rest, avoid payload logging.
Providers claim id (ProIdClaim) is always INT and maps to an INT field in provider DB. Custom integrations may use different column names but still int.
SendClaim returns only IDs inserted into the queue successfully; no duplicate checks.
Downstream Azure DB insertion is idempotent → requeue can safely send duplicates.
### ### 4. Personas
Operator (Provider staff) uses WPF, runs fetchsending, monitors batch health, triggers manual packet retries, manages settings.
SupportIT uses logs and diagnostics to export to troubleshoot connectivity, payload issues, or mapping gaps.
### ### 5. UX Screens (minimum set)
First-run Setup screen GroupID + ProviderDhsCode (one-time; editable in Settings).
Login screen Email + Password (user must login every time app opens).
Dashboard providerpayer selection, schedule status, counts (stagedsentcompletedfailed), last run times.
Batch list filter by CompanyCodeMonth, statuses, HasResume flag.
Batch details dispatch history, failed packets, manual retry button (packet-level), resume status.
Domain mapping monitor missing mappings discoveredposted counts (non-blocking).
Attachments view upload queue, failures, retries, OnlineURL.
Diagnostics ApiCallLog view, export support bundle (no PHI).
Settings screen GroupID, ProviderDhsCode, fetch interval, config cache TTL, lease duration, retry schedule, manual retry cooldown, storage locations.
### ### 6. Authentication and Setup
#### ### 6.1 First-run Setup (one time until changed)
Triggered when AppSettings row does not exist.
User inputs GroupID, ProviderDhsCode.
Persist locally in SQLite AppSettings.
User may change later via Settings screen.
### 6.2 Login (every startup)
No persistent session; user must login each time app opens.
Login request body uses saved GroupID.
Success condition HTTP 200 and succeeded=true.
{
   email ,
   groupID string,
   password string
 }
### ### 7. Provider configuration loading + caching
After successful login, app calls GetProviderConfigration(providerDhsCode).
Cache provider configuration locally for 1 day (configurable).
On app restart, if cache is valid, it may be used immediately; a background refresh may run to update it.
Config includes providerInfo (with connectionStringtoken), providerPayers, and domainMappings baseline.
{
   providerInfo {
     provider_ID 0,
     providerCCHI string,
     providerNameAr string,
     providerNameEn string,
     connectionString string,
     providerDHSCode string,
     providerToken string,
     providerVendor string,
     integrationType string,
     dataBaseType string
   },
   providerPayers [
     {
       payerId 0,
       companyCode string,
       payerNameEn string,
       parentPayerNameEn string
     }
   ],
   domainMappings [
     {
       domTable_ID 0,
       providerDomainCode string,
       providerDomainValue string,
       dhsDomainValue 0,
       isDefault true,
       codeValue string,
       displayValue string
     }
   ]
 }
### ### 8. Scheduling and Streams (high level)
Stream A (FetchStage) fetch ALL claims for CompanyCode + DateRange, stage locally, validatelog, discover + post missing mappings, create batchBcrId.
Stream B (Sender) send staged claims in packets of ### 1..40 gzip, mark Enqueued or Failed based on success list, record Dispatch history.
Stream C (Retry) auto retry at 136 minutes (max 3), manual retry packets with 10-minute debounce, never blocks Stream AB.
Stream D (ResumeRequeue) poll by BcrId, mark Completed, diff incompletes, resend incompletes in packets of ### 1..40, then attachments after Completed.
### ### 9. Functional requirements (FR)
#### FR-1 First-run Setup
##### FR- 1.1 Show Setup screen if GroupIDProviderDhsCode not stored.
##### FR- 1.2 Persist GroupID + ProviderDhsCode locally.
##### FR-1.3 Allow updating values via Settings later.
#### FR-2 Login
##### FR-2.1 Require login on each app start.
##### FR- 2.2 Send login body with groupID from local setting.
##### FR 2.3 Gate all streams behind successful login.
#### FR-3 Load provider configuration
##### FR 3.1 Load config via API using ProviderDhsCode.
##### FR 3.2 Cache config for TTL (default 1 day).
##### FR 3.3 Store provider payers and baseline domain mappings locally.
#### FR-4 Fetchstage
##### FR 4.1 Fetch by CompanyCode + DateRange (BatchStartDate..BatchEndDate).
##### FR 4.2 Extract and build ClaimBundle payload per claim (header + detail arrays).
##### FR 4.3 Stage Claim + ClaimPayload locally; never call SendClaim in Stream A.
##### FR 4.4 Log validation issues without blocking sending.
#### FR-5 Domain mapping discovery + posting
##### FR 5.1 Extract distinct mapping fields per doc-defined list (from staged claims).
##### FR 5.2 Compare against local DomainMapping and baseline mappings from config.
##### FR 5.3 Post missing mappings to InsertMissMappingDomain using gzip.
##### FR 5.4 Posting missing mappings MUST NOT block Stream B sending.
#### FR-6 Batch management
##### FR 6.1 Create or reuse Batch for (ProviderDhsCode, CompanyCode, MonthKey).
##### FR 6.2 Obtain BcrId via CreateBatchRequest before sending claims for that batch.
#### FR-7 Send claims (queue insertion)
##### FR 7.1 Send in packets of ### 1..40 ClaimBundles (gzip).
##### FR 7.2 Inject provider_dhsCode and bCR_Id into each claimHeader before sending.
##### FR 7.3 Treat successClaimsProidClaim[] as authoritative queue insertion result.
##### FR 7.4 Mark claims not present in success list as Failed and schedule retry.
#### FR-8 Retry
##### FR 8.1 Automatic retry for Failed claims 136 minutes, max 3 tries.
##### FR 8.2 Manual retry retries a packet (### 1..40), not claim-by-claim; enforce 10-minute debounce.
##### FR 8.3 Retry never blocks sending of new NotSent claims.
#### FR-9 ResumeRequeue
##### FR 9.1 Stream D polls GetHISProIdClaimsByBcrId(BcrId) for batches with HasResume.
##### FR 9.2 Mark returned claim IDs as Completed.
##### FR 9.3 Diff incompletes and resend them via SendClaim in packets of ### 1..40 until completion converges.
#### FR-10 Attachments (post completion)
##### FR-10.1 Only process attachments for Completed claims.
##### R-10.2 Upload file to Azure Blob first, produce URL.
##### FR-10.3 Set OnlineURL in attachment JSON then send to attachment metadata endpoint (TODO when provided).
##### FR-10.4 Handle 3 scenarios path file  bytes in Location  base64 in AttachBit; validate file signature before upload for bytesbase6### 4.
### 10. Journeys (step-by-step)
#### Journey 1 — Startup + Setup + Login + Config
User opens WPF.
If no AppSettings show Setup (GroupID, ProviderDhsCode) - Save - proceed.
Show Login screen (email, password). App injects groupID from AppSettings.
POST apiAuthenticationlogin. On success, continue; else stay on login.
Load provider configuration (cache hit if not expired; else fetch) using ProviderDhsCode.
Start worker engine (Streams A–D). Show Dashboard.
#### Journey 2 — Smooth success (fetch - send - complete)
Operator selects CompanyCode and DateRange, clicks Run.
Stream A fetches all claims from provider DB for that scope and stages them locally.
Stream B starts dispatching NotSent claims in packets of ### 1..40; each request is gzip.
SendClaim returns successClaimsProidClaim[]. Stream B marks those Enqueued.
Stream D polls Resume (when HasResume) and marks Completed; when all completed, batch is Completed.
Attachments workflow starts for completed claims.
#### Journey 3 — Partial SendClaim success + retry
Stream B sends 40 claims; response success list contains only subset.
Those in success list are marked Enqueued; others are marked Failed and scheduled for retry.
Stream C retries failed claims at 136 minutes up to 3 times; if still failing, they remain Failed for manual retry.
#### Journey 4 — Manual packet retry
User opens Batch details; clicks Retry Packet.
App selects up to 40 Failed claims for that batch and runs a Retry dispatch.
App enforces 10-minute debounce for repeated manual retries.
Results apply the same success-list rule.
#### Journey 5 — Resume + requeue incompletes
Stream D polls GetHISProIdClaimsByBcrId.
Returned IDs become Completed.
Incompletes (claims not yet completed) are resent via SendClaim in packets of ### 1..40.
Because downstream is idempotent, duplicates are safe; continue polling until all completed.
### 1### 1. Acceptance criteria (developer-ready)
User cannot access dashboardstreams unless login succeeded.
GroupID + ProviderDhsCode are collected once, persisted, and editable.
SendClaim requests are always gzip and always include provider_dhsCode and bCR_Id.
Claims not in success list are scheduled for retry; claims in success list are Enqueued.
Retry schedule matches 136 minutes for max 3 auto tries; manual retry packet has debounce.
Sending continues even when validation issues exist or missing mappings are discovered.
Resume marks completion; requeue incompletes works and converges.
Attachment uploads happen after completion and populate OnlineURL before posting metadata.

# Architecture_v### 1.0.docx

## Architecture + Streams A–D Spec


### ### 1. System context
Client WPF desktop app (MVVM) hosting worker engine in-process.
Source Provider HIS database (tablesviewscustom).
Local coordination SQLite (state machines, leases, payload cache, dispatch history, logs).
Backend APIs auth, provider configuration, batch management, queue insertion (SendClaim), resume confirmation, domain mapping, (later) attachment metadata.
Storage Azure Blob for attachment binaries.
### ### 2. Component interactions (notation)
Notation '-' request; '-' response; '[SQLite]' indicates local persistence; '[Provider DB]' indicates source query.
WPF UI - Auth API Login(email, groupID, password)
 WPF UI - Provider API GetProviderConfigration(providerDhsCode) [cached 1 day]
 Stream A - Provider DB Fetch claims by CompanyCode + DateRange
 Stream A - [SQLite] Upsert Claim + ClaimPayload + ValidationIssue; discover+post DomainMapping gaps (gzip)
 Stream A - Batch API CreateBatchRequest - [SQLite] Batch.BcrId
 Stream BCD - Claims API SendClaim([### 1..40 ClaimBundle], gzip) - successClaimsProidClaim[]
 Stream D - Claims API GetHISProIdClaimsByBcrId(bcrId) - confirmed IDs
 Attachments - Blob Upload - URL - Attachment metadata API (TODO)
### ### 3. Runtime hosting model
The app is a desktop WPF process, not a Windows Service.
Background work runs in-process using hosted background services (Task-based loops) started after login.
Each stream is independently cancellable (CancellationToken) and supports graceful shutdown.
UI communicates with streams via commandsevents and reads state from SQLite (no in-memory shared state as source of truth).
### ### 4. MVVM + background streams in WPF (how it should work)
AppShell starts with SetupLogin navigation.
After login, create a WorkerHost (singleton) that starts Streams A–D.
Streams run on background threads (Task.Run  PeriodicTimer). UI thread never blocked.
Use a local event bus (e.g., IObservable or Mediator) for UI refresh triggers, but always re-read from SQLite for authoritative state.
Settings updates (GroupIDProviderDhsCodeintervals) write to SQLite and streams pick changes on next loop (or via a reload signal).
### ### 5. Data flow (end-to-end)
#### ### 5.1 Setup + Login + Config
On first run Setup screen saves GroupID + ProviderDhsCode into SQLite.AppSettings.
User logs in; GroupID is injected into login body.
On login success, load provider configuration (cached for 1 day). Store config JSON in ProviderConfigCache.
Config provides provider DB connectionStringtoken, company codespayers, and baseline domainMappings.
#### ### 5.2 FetchStage
Stream A reads CompanyCode + DateRange from user selection (or schedule).
Stream A queries Provider DB for header + details and composes ClaimBundle per ProIdClaim.
Stream A writes Claim (state NotSent) and ClaimPayload (payload JSON), plus ValidationIssue.
Stream A scans distinct domain mapping fields and compares vs local mapping set; writes missing DomainMapping entries and posts them in background (gzip).
Stream A ensures Batch exists and obtains BcrId (CreateBatchRequest).
#### ### 5.3 SendRetryResume
Stream B selects NotSent claims up to 40, leases them, builds packet, injects providerDhsCode + BcrId, sends gzip SendClaim.
Stream B updates per-claim status based on success list Enqueued vs Failed (retryable). Creates Dispatch and DispatchItem.
Stream C independently retries Failed claims due now (136 mins, max 3). Manual retry triggers a packet dispatch under cooldown.
Stream D polls Resume marks Completed claims and requeues incompletes via SendClaim in packets until completion converges.
### ### 6. State machines
#### ### 6.1 Claim state machine
EnqueueStatus
   NotSent - InFlight - Enqueued
                   - Failed - (Retry) - InFlight - Enqueued
 CompletionStatus
   Unknown - Completed   (only via Resume endpoint)
Important Enqueued means 'inserted to queue successfully'. Completed means 'confirmed inserted into Azure DB'.
#### ### 6.2 Batch state machine
Draft (created locally)
   - Ready (BcrId obtained)
   - Sending (dispatch loop active)
   - Enqueued (everything was sent at least once; completion pending)
   - HasResume (Resume polling  requeue incompletes)
   - Completed
### ### 7. Concurrency controls (leasing)
Claims are leased to prevent Stream B, C, and D from acting on the same claim concurrently.
Lease fields Claim.LockedBy and Claim.InFlightUntilUtc.
Acquisition must be atomic update row only if InFlightUntilUtc is null or  now AND EnqueueStatus is eligible.
LockedBy values 'Sender', 'Retry', 'Requeue'.
If app crashes, leases naturally expire; work becomes eligible again.
### ### 8. Observability and logging
ApiCallLog records every API call with correlation ID, timings, HTTP status, and safe error text (no PHI).
DispatchDispatchItem provide a full audit of every SendClaim packet (which IDs were sent, what succeeded).
ValidationIssue stores diagnostics discovered during staging (does not block sending).
UI provides filters for EnqueueStatus, CompletionStatus, BatchStatus, and errors.
### ### 9. Security
Encrypt ClaimPayload at rest (PHI). Consider SQLCipher or app-layer encryption using DPAPI.
Attachment bytesbase64 must be encrypted if stored locally.
Never log payload JSON or file bytes; logs must contain only IDs and sizestimings.
Connection strings stored encrypted; decrypt only in-memory.
All HTTP over TLS; include strict certificate validation.
### 10. Attachments architecture
Attachment processing begins after a claim becomes Completed.
Three input scenarios
1) AttachmentType=file Location is localUNC path (credentials may be needed).
2) AttachmentType=byte Location carries bytes; validate signature.
3) AttachBit carries base64 bytes; decode and validate signature.
Upload to Azure Blob - obtain URL - set OnlineURL - POST metadata to backend endpoint (TODO when provided).

# Domain_Mapping_Spec_v### 1.0.docx

## Domain Mapping Specification + Wizard UX

### ### 1. Purpose and non-negotiable rules
This document defines the exact behavior of Domain Mapping in the Provider-Side Data Agent, including the mapping scan (distinct extraction), comparison rules, missing mapping lifecycle, and the Mapping Wizard UX. It is intended to eliminate developer guesswork.
Sending NEVER stops because of missing mappings or validation issues. Domain mapping is operationaldiagnostic and feeds back to the backend mapping service in parallel.
The mapping scan runs in Stream A (FetchStage) andor a dedicated 'Mapping Poster' loop that is allowed to run concurrently.
Mapping is provider + payer scoped (ProviderCodeProviderDhsCode + CompanyCode).
### ### 2. Definitions
Source value the raw value extracted from the provider DB and written into the staged ClaimPayload JSON.
Target value the DHS canonical value that the backend expects (the normalized value).
Domain a named mapping space (e.g., Gender, ClaimType, VisitType, Nationality).
FieldPath a canonical JSON path in ClaimBundle where the source value is located (e.g., claimHeader.patientGender).
Important conditional required fields are not enforced locally because provider-side enumsvalues may not match DHS values until mapping is applied. Domain mapping exists to bridge those differences.
### ### 3. Authoritative source of mapping rules
The authoritative list of domains and their associated field paths comes from the Provider Configuration API (LoadProviderConfiguration). The agent must treat the response as the source of truth for which fields to scan and which domains to post.
Provider configuration is loaded by ProviderDhsCode and cached for 1 day (configurable) or until application restart (per latest note).
The configuration includes domainMappings[] (DomainMappingDto). Even if the DTO expands later, the agent relies only on the subset needed DomainName + FieldPath (and optional hints).
If domainMappings[] is empty or unavailable, the agent uses a baseline fallback list (Section ### 4.2) and logs a warning.
### ### 4. Exact list of fields used for domain mapping scan
#### ### 4.1 Primary list configuration-driven
The agent scans EXACTLY the FieldPath entries returned in ProviderConfig.domainMappings[]. For each configured FieldPath, it performs distinct extraction across the staged claims in the current run (CompanyCode + DateRange).
Scope ProviderDhsCode + CompanyCode (payer) + current fetch run.
Granularity distinct values (case-sensitive by default; see normalization rules below).
Location values are extracted from staged ClaimPayload JSON (canonical ClaimBundle), not re-read from provider DB during posting.
#### ### 4.2 Baseline fallback list (used only if config is missing)
If configuration does not provide domainMappings[], the agent uses the following baseline scan list. This list is intentionally conservative and covers the most common DHS enum-like fields observed in the payload model.
claimHeader.patientGender  (Domain Gender)
claimHeader.claimType      (Domain ClaimType)
claimHeader.visitType      (Domain VisitType)
claimHeader.subscriberRelationship (Domain SubscriberRelationship)
claimHeader.maritalStatus  (Domain MaritalStatus)
claimHeader.nationality    (Domain Nationality)
claimHeader.patientIdType  (Domain PatientIdType)
claimHeader.admissionType  (Domain AdmissionType)
claimHeader.encounterStatus (Domain EncounterStatus)
claimHeader.triageCategoryTypeID (Domain TriageCategory)
serviceDetails[].serviceType (Domain ServiceType)
serviceDetails[].serviceEventType (Domain ServiceEventType)
serviceDetails[].pharmacistSelectionReason (Domain PharmacistSelectionReason)
serviceDetails[].pharmacistSubstitute (Domain PharmacistSubstitute)
diagnosisDetails[].diagnosisType (Domain DiagnosisType)
diagnosisDetails[].onsetConditionTypeID (Domain ConditionOnset)
Operational note as soon as the backend configuration is available, it overrides this fallback list. Do not hardcode additional fields without backend alignment.
### ### 5. Distinct extraction rules
Distinct extraction means we compute the set of unique SourceValues for each configured FieldPath across all staged claims in the run.
Nullempty handling ignore null and empty-string values (do not create mappings).
Trim apply Trim() to string values before distinct comparison.
Case handling preserve original casing for storage, but compare using a normalized key (Trim + case-fold) to avoid duplicates caused by casing inconsistencies.
Array fields for FieldPaths with [] (e.g., serviceDetails[].serviceType), extract values from every element in the array and distinct them.
Type handling for int-like fields, convert to string for storage in DomainMapping.SourceValue, but keep a DataTypeHint when possible (optional).
### ### 6. Comparison rule providerraw → DHS canonical values
Comparison happens purely in SQLite using the DomainMapping table. For each (DomainName, SourceValue) observed in the staged claims, the agent checks if a mapping already exists.
Lookup key (ProviderCode, CompanyCode, DomainName, SourceValue).
If a row exists with TargetValue NOT NULL and MappingStatus indicates ApprovedResolved, then the mapping is considered available.
If a row exists but TargetValue is NULL (or status MissingPosted), it is considered missingpending and will be posted if not posted recently.
If no row exists, it is created locally with MappingStatus=Missing and DiscoveredUtc=now.
Important the agent does NOT block sending if a mapping is missing. Missing mapping presence is used to drive posting + user visibility only.
### ### 7. Missing mapping lifecycle (end-to-end)
A missing mapping follows the lifecycle below. The lifecycle is designed to be robust across crashes and to avoid repeated posting spam.
Discovered Stream A finds a SourceValue for a configured FieldPath with no matching local mapping row. Insert DomainMapping row with MappingStatus=Missing.
Stored locally DomainMapping row persists in SQLite for offline continuity and to prevent repeated rediscovery.
Posted (gzip) A background poster sends batches of missing items to InsertMissMappingDomain using gzip. On success MappingStatus=Posted and LastPostedUtc=now.
ResolvedApproved A subsequent configuration refresh andor explicit domain mapping fetch updates TargetValue and sets MappingStatus=Approved (or equivalent).
Lifecycle status meanings
Missing discovered locally, not yet sent to backend (or last post attempt failed and retry is due).
Posted sent to backend successfully; waiting for approvalavailability of TargetValue.
Approved TargetValue is available locally and can be used for transformationdisplay. (Exact naming can follow your enum list; the behavior above is the contract.)
### ### 8. Posting missing domain mappings (gzip) - behavior contract
Missing domain mappings must be posted in parallel and must never block sending.
Owner Stream A may trigger posting, but posting should run as a dedicated internal loop ('Mapping Poster') so it can retry independently.
Eligibility DomainMapping rows with MappingStatus=Missing OR (MappingStatus=Posted but TargetValue still null and backend asks to re-post) depending on policy; default Missing only.
Throttling avoid reposting the same item too frequently (e.g., do not repost if LastPostedUtc  X minutes; X configurable).
Transport POST InsertMissMappingDomain with gzip request body.
Logging every post call is logged to ApiCallLog; failures update DomainMapping.NotesLastUpdatedUtc and schedule later retry.
Note If the backend provides an endpoint to fetch approved mappings, refresh them on the provider config cache refresh interval (1 day  app restart) andor on user request via the wizard.
### ### 9. Mapping Wizard UX (user flow)
The Mapping Wizard exists for visibility and operations. It helps users understand missing mappings, optionally request updates, and track resolution. It does not gate sending.
#### ### 9.1 Entry points
Automatic after a fetch run, if any Missing mappings exist for the selected CompanyCode, show a non-blocking banner 'Mappings pending' with a 'Review' button.
Manual Settings → Domain Mapping Wizard (always accessible).
#### ### 9.2 Wizard pages (recommended)
Scope selection Provider + CompanyCode (payer) + optional DateRange filter for statistics (does not affect sending).
Summary counts by DomainName and status (MissingPostedApproved).
Details grid for a selected DomainName, list SourceValue, Status, DiscoveredUtc, LastPostedUtc, TargetValue (if available), and sample occurrences count.
Actions 'Post missing now' (forces immediate gzip post), 'Refresh from server' (re-load provider config andor fetch approved mappings if supported), 'Export for support' (CSV).
Completion show current state and remind that sending continues regardless.
#### ### 9.3 What the wizard changes in subsequent runs
It does not stop or start streams. Streams always run based on timers and user startstop controls.
If the wizard triggers 'Post missing now', it schedules immediate posting attempts (updates DomainMapping.LastPostedUtc on success).
If the wizard triggers 'Refresh from server', it refreshes local DomainMapping.TargetValue for any resolved items and updates MappingStatus to Approved.
When mappings become Approved, future runs will show fewer Missing items, but sending behavior remains unchanged.
#### 10. Local SQLite tables involved (readwrite contract)
This section names the local tables touched by domain mapping behavior. The exact physical schema is defined in the SQLite Data Model Spec; this section clarifies usage.
DomainMapping primary store for mappings, lifecycle status, timestamps, and TargetValue.
ValidationIssue used to record mapping-related diagnostics (optional). For example 'Missing mapping for Domain=Gender, value=MALE'. Severity does not block sending.
ApiCallLog logs gzip posts to InsertMissMappingDomain and any refreshfetch mapping calls.
ClaimPayload used as the source of truth for extracting values (distinct scan uses staged payload JSON).
#### 1### 1. Crash recovery expectations
If the app crashes during scan DomainMapping rows already inserted remain; scan can re-run safely (idempotent insert via unique key).
If the app crashes during posting ApiCallLog will show the attempt; DomainMapping remains Missing and can be retried later.
Posting loop must be restart-safe it selects eligible rows by statustimestamps and continues.
#### 1### 2. Developer acceptance checklist
Given a provider config with domainMappings[], the agent scans only those FieldPaths (plus fallback only when config missing).
Distinct extraction ignores nullempty values and normalizes whitespacecasing for dedupe.
New missing values create DomainMapping rows with status Missing (unique constraint prevents duplicates).
Posting uses gzip and is logged; failures do not block Stream B sending.
Wizard shows MissingPostedApproved and can trigger 'Post now' and 'Refresh'.
Approved mappings reduce Missing counts in later runs, but sending continues regardless.


# SQLite_Data_Model_Spec_v### 1.0.docx

## SQLite Data Model Spec
Tables, fields, indexes, and status enums (List format)

### ### 1. Overview
SQLite is the local system-of-record for stagingoutbox state, retries, resumerequeue, and diagnostics.
PHI is present in ClaimPayload and attachment content; those fields MUST be encrypted at rest (AES-GCM recommended).
All write operations that change work-state must be transactional.
All streams communicate via SQLite; no direct in-memory coupling is required.
### ### 2. Naming and typing conventions
Utc timestamps stored as ISO 8601 text or SQLite datetime (implementation choice), always UTC.
Provider identity store ProviderDhsCode (string) as the primary provider key; keep ProviderCode optionally for future multi-provider installations.
Claim identity ProIdClaim is ALWAYS an int (even if some JSON samples show string).
Leasing LockedBy + InFlightUntilUtc prevents two workers sendingretrying the same claim concurrently.
### ### 3. Updates included in v0.4
NEW AppSettings (singleton) to store one-time setup values GroupID + ProviderDhsCode, plus configurable intervals (fetch, cache TTL, manual retry cooldown).
NEW ProviderConfigCache to cache provider configuration (TTL default 1 day).
UPDATE Attachment model clarified to support 3 source scenarios (path  bytes in location  base64 in AttachBit) + OnlineURL after blob upload.
UPDATE Claim model includes fields for resume diff + requeue scheduling (NextRequeueUtc  RequeueAttemptCount).
### ### 4. Status enums (authoritative)
#### ### 4.1 EnqueueStatus (Claim.EnqueueStatus)
0 = NotSent — staged locally, never successfully sent to SendClaim.
1 = InFlight — leased by SenderRetryRequeue worker.
2 = Enqueued — SendClaim succeeded (agent equivalent of isFetched=true).
3 = Failed — SendClaim failed; eligible for Stream C retry.
#### ### 4.2 CompletionStatus (Claim.CompletionStatus)
0 = Unknown — not yet confirmed by GetHISProIdClaimsByBcrId.
1 = Completed — confirmed inserted downstream.
#### ### 4.3 BatchStatus (Batch.BatchStatus)
0 = Draft — local batch created; BcrId may be null; staging in progress.
1 = Ready — BcrId exists; batch can be sent.
2 = Sending — Stream B actively dispatching packets (transient UI state).
3 = Enqueued — all known staged claims have been sent at least once; completion not confirmed.
4 = HasResume — completion confirmation needed andor incompletes exist.
5 = Completed — all claims confirmed Completed.
6 = Failed — terminal batch-level failure (rare).
#### ### 4.4 MappingStatus (DomainMapping.MappingStatus)
0 = Missing — discovered; not posted yet.
1 = Posted — submitted to InsertMissMappingDomain (gzip).
2 = Approved — mapping is available.
3 = PostFailed — post failed; eligible for retry.
#### ### 4.5 DispatchType (Dispatch.DispatchType)
0 = NormalSend — Stream B.
1 = RetrySend — Stream C.
2 = RequeueIncomplete — Stream D resend after resume diff.
#### ### 4.6 DispatchStatus (Dispatch.DispatchStatus)
0 = Ready — created but not yet sent (optional).
1 = InFlight — request in progress.
2 = Succeeded — all items succeeded.
3 = Failed — request failed or all items failed.
4 = PartiallySucceeded — mixed successfail.
#### ### 4.7 UploadStatus (Attachment.UploadStatus)
0 = NotStaged — discovered but not staged.
1 = Staged — bytes staged locally (encrypted).
2 = Uploading — upload in progress (leased).
3 = Uploaded — blob upload done; OnlineURL set.
4 = Failed — uploadpost failed; eligible for retry.
#### ### 4.8 AttachmentSourceType (Attachment.AttachmentSourceType)
0 = FilePath — LocationPath contains localUNC path (may need credentials).
1 = RawBytesInLocation — LocationBytesEncrypted holds bytes (validate file before upload).
2 = Base64InAttachBit — AttachBitBase64Encrypted holds base64 bytes (validate file before upload).
### ### 5. Tables
#### ### 5.1 AppMeta
Purpose — Singleton table for DB bookkeeping and troubleshooting.
Written by — App startupshutdown; DB migration routine.
Read by — App startup to validate schema version + show diagnostics.
Fields (details)
Id (PK, always 1) ensures one row only.
SchemaVersion prevent opening DB with older client.
CreatedUtc audit.
LastOpenedUtc detect crash last run if too old.
ClientInstanceId stable GUID per install; useful for correlating logs.
Notes — No business logic depends on this table.
#### ### 5.2 AppSettings (NEW)
Purpose — Singleton table for one-time setup + runtime configuration toggles.
Written by — First Run Setup screen; Settings screen; app defaults initializer.
Read by — Login screen; worker engine scheduler; config cache logic.
Fields (details)
Id (PK, always 1) ensures one row only.
GroupID one-time setup; used ONLY in login request body.
ProviderDhsCode one-time setup; used for config APIs and injected into every SendClaim payload.
ConfigCacheTtlMinutes default 1440 (1 day).
FetchIntervalMinutes default ### 5.
ManualRetryCooldownMinutes default 10.
LeaseDurationSeconds default 120 (example).
CreatedUtc, UpdatedUtc.
#### ### 5.3 ProviderProfile
Purpose — Stores provider-level HIS DB connection configuration (secure).
Written by — Provider onboardingsetup; Settings screen.
Read by — Provider DB connection factory; adapter selection.
Fields (details)
ProviderCode (PK) reserved for future use; can be a constant for single-provider installs.
ProviderDhsCode backend identifier used by APIs and payload injection.
DbEngine selects ADO.NET provider + dialect.
IntegrationType Tables  Views  Custom.
EncryptedConnectionString never store plaintext.
EncryptionKeyId key identifier for rotation.
IsActive soft disable.
CreatedUtc, UpdatedUtc.
#### ### 5.4 ProviderExtractionConfig
Purpose — Configures how to query claim keys and details for TablesViewsCustom integrations.
Written by — Provider onboardingsetup.
Read by — Provider DB adapters (Stream A).
Fields (details)
ProviderCode (PK).
ClaimKeyColumnName mandatory for Custom when not named ProIdClaim; maps to int.
HeaderSourceName, DetailsSourceName optional tablesviews.
CustomHeaderSql, CustomServiceSql, CustomDiagnosisSql, CustomLabSql, CustomRadiologySql, CustomOpticalSql optional SQL templates.
Notes, UpdatedUtc.
#### ### 5.5 ProviderConfigCache (NEW)
Purpose — Cache GET ProviderConfigration response for 1 day (configurable).
Written by — Config loader after login.
Read by — Stream A mapping scan; UI (payers); diagnostics.
Fields (details)
ProviderDhsCode (PK).
ConfigJson raw JSON snapshot (no PHI expected).
FetchedUtc, ExpiresUtc.
ETag (optional), LastError (optional).
#### ### 5.6 PayerProfile
Purpose — Local payercompany list per provider (supports overridesdisable).
Written by — Config loader; Settings screen.
Read by — Stream A run selection; batch creation.
Fields (details)
PayerId (PK).
ProviderDhsCode.
CompanyCode used for provider DB filtering + payload.
PayerCode used by CreateBatchRequest when required.
PayerName (optional), IsActive, CreatedUtc, UpdatedUtc.
Indexes — UNIQUE(ProviderDhsCode, CompanyCode); IX(ProviderDhsCode, IsActive).
#### ### 5.7 DomainMapping
Purpose — Stores domain value mapping and missing mapping queue; posting never blocks sending.
Written by — Stream A scan; missing mapping poster; config loader.
Read by — Mapping wizard UI; diagnostics; scan dedupe.
Fields (details)
DomainMappingId (PK).
ProviderDhsCode, CompanyCode.
DomainName, DomainTableId.
SourceValue, TargetValue (nullable).
MappingStatus, DiscoveredUtc, LastPostedUtc, LastUpdatedUtc, Notes.
Indexes — UNIQUE(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue); IX(MappingStatus); IX(DomainName).
#### ### 5.8 Batch
Purpose — Submission grouping per providercompanymonth; stores backend BcrId and resume state.
Written by — Stream A (ensurecreate); Stream B (status); Stream D (completion).
Read by — UI; Stream B packet grouping; Stream D resume polling.
Fields (details)
BatchId (PK).
ProviderDhsCode, CompanyCode, PayerCode.
MonthKey (YYYYMM).
BcrId (nullable until created).
BatchStatus, HasResume.
CreatedUtc, UpdatedUtc, LastError (optional).
Indexes — UNIQUE(ProviderDhsCode, CompanyCode, MonthKey); IX(BatchStatus); IX(BcrId).
#### ### 5.9 Claim
Purpose — Claim-level state machine supporting async fetchsendretryresumerequeue.
Written by — Stream ABCD.
Read by — Stream BCD; UI.
Fields (details)
ProviderDhsCode + ProIdClaim (PK).
CompanyCode, MonthKey.
BatchId (FK), BcrId (cached).
EnqueueStatus, CompletionStatus.
LockedBy, InFlightUntilUtc (leasing).
AttemptCount, NextRetryUtc, LastError.
LastResumeCheckUtc.
RequeueAttemptCount, NextRequeueUtc, LastRequeueError.
LastEnqueuedUtc.
FirstSeenUtc, LastUpdatedUtc.
Indexes — IX(ProviderDhsCode, CompanyCode, MonthKey); IX(EnqueueStatus, NextRetryUtc); IX(CompletionStatus); IX(BatchId); IX(InFlightUntilUtc); IX(EnqueueStatus, CompletionStatus, NextRequeueUtc).
#### ### 5.10 ClaimPayload
Purpose — Stores ClaimBundle JSON for deterministic resend; avoids re-reading provider DB.
Written by — Stream A.
Read by — Stream BCD.
Fields (details)
ProviderDhsCode + ProIdClaim (PK).
PayloadJson (encrypted; PHI).
PayloadSha256, PayloadVersion.
CreatedUtc, UpdatedUtc.
Indexes — IX(PayloadSha256).
#### ### 5.11 Dispatch
Purpose — One SendClaim request (### 1..40). More than logging sequence control + replay + crash recovery.
Written by — Stream BCD.
Read by — UI diagnostics; recovery routine.
Fields (details)
DispatchId (PK, UUID).
ProviderDhsCode, BatchId (FK), BcrId.
SequenceNo (per batch for normal sending).
DispatchType, DispatchStatus.
AttemptCount, NextRetryUtc (optional).
RequestSizeBytes, RequestGzip, HttpStatusCode, LastError.
CorrelationId.
CreatedUtc, UpdatedUtc.
Indexes — UNIQUE(BatchId, SequenceNo); IX(DispatchStatus, NextRetryUtc); IX(BatchId).
#### ### 5.12 DispatchItem
Purpose — Which claims were in which dispatch + per-claim outcome for partial success.
Written by — Stream BCD.
Read by — Audit UI; response processing.
Fields (details)
DispatchId + ProviderDhsCode + ProIdClaim (PK).
ItemOrder.
ItemResult (optional enum 0 unknown, 1 success, 2 fail).
ErrorMessage (optional).
Indexes — IX(ProviderDhsCode, ProIdClaim); IX(DispatchId).
#### ### 5.13 Attachment
Purpose — Attachment lifecycle; processed only after claim Completed; uploaded to blob then posted to backend endpoint (TODO).
Written by — Stream D attachment worker.
Read by — Stream D; UI.
Fields (details)
AttachmentId (PK, UUID).
ProviderDhsCode, ProIdClaim (FK).
AttachmentSourceType.
LocationPath (for FilePath).
LocationBytesEncrypted (for RawBytesInLocation).
AttachBitBase64Encrypted (for Base64InAttachBit).
FileName, ContentType, SizeBytes, Sha25### 6.
OnlineURL (set after blob upload).
UploadStatus, AttemptCount, NextRetryUtc, LastError.
CreatedUtc, UpdatedUtc.
Indexes — IX(ProviderDhsCode, ProIdClaim); IX(UploadStatus, NextRetryUtc); UNIQUE(ProviderDhsCode, ProIdClaim, Sha256) when Sha256 present.
#### ### 5.14 ValidationIssue
Purpose — Validation diagnostics (completenessmappingparse). By policy, never blocks sending.
Written by — Stream A.
Read by — UI; diagnostics exports.
Fields (details)
ValidationIssueId (PK).
ProviderDhsCode, ProIdClaim (nullable).
IssueType, FieldPath, RawValue (optional).
Message, IsBlocking (severity only).
CreatedUtc, ResolvedUtc (optional), ResolvedBy (optional).
Indexes — IX(ProviderDhsCode, ProIdClaim); IX(IssueType, IsBlocking).
#### ### 5.15 ApiCallLog
Purpose — API observability without PHI.
Written by — HttpClient wrapper.
Read by — Diagnostics screensupport bundle.
Fields (details)
ApiCallLogId (PK).
ProviderDhsCode (optional).
EndpointName, CorrelationId.
RequestUtc, ResponseUtc, DurationMs.
HttpStatusCode, Succeeded, ErrorMessage (no PHI).
RequestBytes, ResponseBytes.
WasGzipRequest.
Indexes — IX(EndpointName, RequestUtc).
### ### 6. Relationships (logical)
ProviderProfile.ProviderDhsCode links to ProviderConfigCache, PayerProfile, DomainMapping, Batch, Claim, Dispatch, ApiCallLog, Attachment, ValidationIssue.
Batch.BatchId - Claim.BatchId.
Claim (ProviderDhsCode, ProIdClaim) - ClaimPayload, DispatchItem, Attachment, ValidationIssue.
Dispatch.DispatchId - DispatchItem.DispatchId.
### ### 7. Crash recovery expectations
Any row with InFlightUntilUtc  now is abandoned and eligible for reprocessing.
Leasing must be done atomically using a single UPDATE predicate.
On startup, recovery routine should mark stale InFlight dispatches and release leases by expiry.
Retry timing must be UTC; auto retry is 136 minutes (max 3 attempts), then manual packet retry with 10-minute debounce.
### ### 8. Security notes (PHI)
Encrypt at rest ClaimPayload.PayloadJson and attachment bytesbase64 fields.
Never store PHI in ApiCallLog or Dispatch error fields.
Store network credentials outside SQLite (Windows Credential Manager), only reference identifiers in SQLite if needed.



# Stream_Specs_Behavior_Contracts_v### 1.0.docx

## Stream Specs (Behavior Contracts)


### 0. Scope and goals
Goal give developers exact runtime behavior so implementation can proceed without guessing.
Applies to WPF desktop agent hosting a worker engine and using local SQLite as the coordination store.
The agent never stops sending batches because of validation issues or missing domain mapping; those are logged and posted in parallel.
Scale assumptions thousands of claims per run; PHI is present; gzip for POST requests.
### 1. Preconditions and configuration (applies to all streams)
#### 1.1 First-run one-time setup
On very first application start (no AppSettings saved) show Setup screen with exactly two fields
GroupID (string) — used ONLY in Login request body.
ProviderDhsCode (string) — used to load provider configuration and injected into every SendClaim payload.
Persist both values in SQLite AppSettings (singleton).
User can change them later from Settings screen (this re-runs config load on next login).
#### 1.2 Login (every app start; no remembered session)
User must log in every time the WPF app opens (no auto-login session).
Login request (POST apiAuthenticationlogin, gzip)
{email ..., groupID AppSettings.GroupID, password ...}
Login succeeds only if HTTP 200 and response.succeeded == true.
#### 1.3 Provider configuration loading and caching
After successful login load provider configuration using ProviderDhsCode GET apiProviderGetProviderConfigration{providerDhsCode}.
Cache the config in SQLite ProviderConfigCache for ConfigCacheTtlMinutes (default 1440 = 1 day).
Cache rules
If cached config exists and not expired = use it (no API call).
If expired or missing = refresh via API; if refresh fails = continue running using last cached config (if any) and log failure (ApiCallLog).
Config data used by Stream A payers list (companyCode, payerCode), domainMappings baseline, providerInfo.
#### 1.4 Shared technical contracts
Gzip All POST requests MUST be sent with gzip-compressed JSON body (SendClaim, CreateBatchRequest, InsertMissMappingDomain, Login, DeleteBatch; others if POST is added later).
CorrelationId generate a GUID per outbound request and record it in ApiCallLog; for SendClaim also store it in Dispatch.CorrelationId.
PHI handling ClaimPayload.PayloadJson and attachment bytesbase64 MUST be encrypted at rest.
Leasing all streams that send must lease claim rows (LockedBy + InFlightUntilUtc) to prevent collisions.
### 2. Shared state machine definitions
#### 2.1 Claim.EnqueueStatus (int)
0 NotSent — staged locally; never successfully sent to SendClaim.
1 InFlight — leased by a worker (Sender  Retry  Requeue) right now.
2 Enqueued — SendClaim succeeded; equivalent to isFetched=true locally (sent to queue).
3 Failed — SendClaim failed; eligible for Stream C retry.
#### 2.2 Claim.CompletionStatus (int)
0 Unknown — not yet confirmed by GetHISProIdClaimsByBcrId.
1 Completed — confirmed inserted downstream; do not requeue again.
#### 2.3 Leasing fields (anti-collision contract)
Fields Claim.LockedBy (string) + Claim.InFlightUntilUtc (UTC datetime).
Lease acquisition MUST be atomic one UPDATE that sets LockedBy and InFlightUntilUtc only when
EnqueueStatus in eligible set AND (InFlightUntilUtc is null OR InFlightUntilUtc  now).
Lease duration AppSettings.LeaseDurationSeconds (default 120).
Lease release on completion, set LockedBy=null and InFlightUntilUtc=null; update EnqueueStatusNextRetryUtc as needed.
Crash recovery if the app crashes, any InFlightUntilUtc that expires makes rows eligible again without manual intervention.
#### 2.4 Batch status (int)
0 Draft — local batch exists; staging may still be running; BcrId may be null.
1 Ready — BcrId exists; batch can be dispatched.
2 Sending — Stream B currently dispatching packets (UI progress state).
3 Enqueued — all known staged claims have been sent at least once; completion not yet confirmed.
4 HasResume — resume poll required andor incompletes detected.
5 Completed — all claims confirmed.
6 Failed — batch-level terminal failure (rare).
### 3. Stream A — FetchStage (CompanyCode + DateRange)
Behavior summary
Fetch all claims for selected CompanyCode and DateRange (startend) from Provider HIS DB.
Transform to canonical ClaimBundle JSON and stage locally (Claim + ClaimPayload).
Run completeness validation (unconditional only) and write ValidationIssue (does NOT block sending).
Extract distinct domain-mapping fields and compare to existing DomainMapping; add Missing rows.
Post missing domain mapping items via gzip in parallel (never blocks sending).
Create or reuse Batch and ensure BcrId exists via CreateBatchRequest.
#### 3.1 Trigger and cadence
Runs on scheduler every FetchIntervalMinutes (default 5), configurable.
User can also run it manually for a specific CompanyCode + DateRange.
Stream A must not be blocked by Stream BCD (only DB coordination).
#### 3.2 Inputs
Provider HIS DB connection (from ProviderProfile).
Selected CompanyCode + DateRange (BatchStartDate..BatchEndDate) from UI run configuration.
Provider configuration cache (provider payers + mappings) from ProviderConfigCache (refresh if expired).
#### 3.3 Data access (SQLite)
Reads AppSettings, ProviderProfile, ProviderExtractionConfig, ProviderConfigCache, DomainMapping (for comparison), Batch (to reuse), Claim (to avoid duplicate staging).
Writes ApiCallLog, Batch, Claim, ClaimPayload, ValidationIssue, DomainMapping.
#### 3.4 Core workflow (step-by-step)
Resolve payer settings for CompanyCode (from config cache or PayerProfile override). Obtain PayerCode for batch creation.
Query Provider HIS DB for all claim keys (ProIdClaim int) matching CompanyCode + DateRange.
For each claim key, extract header + all detail sets needed to build one ClaimBundle (canonical payload).
Buildnormalize payload ensure ProIdClaim is int; inject CompanyCode; DO NOT inject ProviderDhsCodeBcrId yet (that is done by SenderRequeue right before sending).
Stage Claim row (UPSERT by ProviderDhsCode + ProIdClaim)
If claim is new = set EnqueueStatus=NotSent, CompletionStatus=Unknown, AttemptCount=0, FirstSeenUtc=now.
If claim already exists = do NOT downgrade its state (e.g., keep EnqueuedCompleted); only update MonthKeyBatchId if missing and update LastUpdatedUtc.
Stage ClaimPayload (UPSERT) store encrypted PayloadJson and PayloadSha25### 6.
Run completeness validation (unconditional required fields only). Write ValidationIssue rows as diagnostics (IsBlocking indicates severity only).
Domain-mapping scan compute distinct values for the configured mapping fields across the staged claims; for each unseen (DomainTableId, SourceValue) insert DomainMapping row with MappingStatus=Missing.
Batch ensure createreuse Batch by ProviderDhsCode + CompanyCode + MonthKey (derived from DateRange start or claim header InvoiceDate per spec). If batch has no BcrId = call CreateBatchRequest.
When CreateBatchRequest returns success = store Batch.BcrId and set BatchStatus=Ready (or keep Draft until enough staged). Log in ApiCallLog.
#### 3.5 Missing domain mapping posting (parallel, non-blocking)
Responsibility Stream A owns discovery AND posting of missing domain mappings.
Posting endpoint POST apiDomainMappingInsertMissMappingDomain (gzip).
Request body { providerDhsCode, missedDomainItems [{ providerCodeValue, domainValue, domainTableId }] }.
Posting cadence after scan, attempt to post all MappingStatus in (Missing, PostFailed) in batches (size configurable; default 500).
On successful API response
Mark insertedskipped items as MappingStatus=Posted and set LastPostedUtc.
Mark failed items as MappingStatus=PostFailed; they remain eligible for future attempts.
IMPORTANT posting MUST NOT block staging or sending; failures are logged (ApiCallLog) and retried next Stream A run.
### 4. Stream B — Sender (Normal sending)
Behavior summary
Continuously picks staged claims (EnqueueStatus=NotSent) and sends in gzip packets of 1..40 to SendClaim.
Creates Dispatch + DispatchItem for every request and processes per-claim successfailure lists.
Marks claims Enqueued or Failed accordingly.
Setsmaintains BatchStatus transitions and HasResume flag when needed.
#### 4.1 Trigger and cadence
Runs continuously while app is open (hosted worker), independent from Stream A.
Never blocks Stream A; uses leasing to avoid collisions with Stream CD.
#### 4.2 Data access (SQLite)
Reads AppSettings (ProviderDhsCode), Claim, ClaimPayload, Batch (BcrId), ValidationIssue (diagnostics only), DomainMapping (diagnostics only).
Writes Claim, Batch, Dispatch, DispatchItem, ApiCallLog.
### 4.3 Eligibility rules (IMPORTANT)
Eligibility is ONLY based on work-state, not validationmapping
Pick claims where EnqueueStatus=NotSent AND CompletionStatus=Unknown.
Ignore ValidationIssue  missing DomainMapping for eligibility (those are for logs and parallel posting).
Use leasing to ensure Stream C and Stream D cannot pick the same claims simultaneously.
#### 4.4 Core workflow (step-by-step)
Select up to 40 eligible claims ordered by (BatchId, ProIdClaim) or similar stable ordering.
Lease them atomically set EnqueueStatus=InFlight, LockedBy='Sender', InFlightUntilUtc=now+lease.
Load staged ClaimPayload for those 1..40 claims.
Before sending inject ProviderDhsCode and Batch Request Id (BcrId) into every claimHeader
provider_dhsCode = AppSettings.ProviderDhsCode
bCR_Id = Batch.BcrId (must exist)
Create Dispatch row (DispatchType=NormalSend) + DispatchItem rows (ItemOrder ### 1..N).
Send request POST apiClaimsSendClaim (gzip) with JSON array [ClaimBundle...].
Process response (HTTP 200 & succeeded==true) use data.successClaimsProidClaim[] and data.failClaimsProidClaim[].
For each success proId update Claim = EnqueueStatus=Enqueued, LastEnqueuedUtc=now, AttemptCount unchanged, clear LockedByInFlightUntilUtc.
For each failed proId update Claim = EnqueueStatus=Failed, AttemptCount += 1, NextRetryUtc=now+1min (first retry), LastError=message, clear lease.
Update Dispatch = DispatchStatus SucceededFailedPartiallySucceeded, HttpStatusCode, UpdatedUtc; store per-item results in DispatchItem.
Batch transitions
When first send starts BatchStatus - Sending.
When all currently staged NotSent claims are processed at least once BatchStatus - Enqueued.
If backend indicates 'has_resume' (from batch status APIs, or policy trigger) set Batch.HasResume=1 and BatchStatus - HasResume.
### 5. Stream C — Retry (Independent automatic + manual packet retry)
Behavior summary
Automatically retries failed sends 3 times with backoff 1  3  6 minutes.
After 3 automatic retries, claims stay Failed; user can trigger manual retry (packet-level) with 10-minute debounce.
Retry never blocks normal sending; Stream B continues in parallel.
#### 5.1 Automatic retry rules
Applies to Claim rows where EnqueueStatus=Failed and NextRetryUtc = now and AttemptCount in {1,2,3}.
Backoff schedule based on AttemptCount AFTER failure
After first failure = NextRetryUtc = now + 1 minute.
After second failure = NextRetryUtc = now + 3 minutes.
After third failure = NextRetryUtc = now + 6 minutes.
After third retry failure (AttemptCount becomes 4) = stop automatic retries set NextRetryUtc = null.
#### 5.2 Manual retry rules (packet-level only)
User cannot retry claim-by-claim; only retry a packet (### 1..40).
Manual retry uses either
Option A (preferred) re-send a selected failed Dispatch (same 1..40 claims) as DispatchType=RetrySend.
Option B build a packet from up to 40 Failed claims within a selected batch.
Debounce do not allow another manual retry for the same batchpacket within ManualRetryCooldownMinutes (default 10).
Implementation note debounce can be enforced by checking Dispatch.CreatedUtc for DispatchType=RetrySend created within last cooldown window.
#### 5.3 Data access (SQLite)
Reads Claim (Failed + due), ClaimPayload, DispatchDispatchItem (for packet reconstruction), Batch, AppSettings.
Writes Claim (state, attempts), Dispatch, DispatchItem, ApiCallLog.
#### 5.4 Core workflow (step-by-step)
Select up to 40 due Failed claims OR reconstruct from a failed Dispatch (manual).
Lease them atomically set EnqueueStatus=InFlight, LockedBy='Retry', InFlightUntilUtc=now+lease.
Inject ProviderDhsCode + BcrId into each claimHeader (same as Stream B).
Create Dispatch (DispatchType=RetrySend) + DispatchItems.
POST apiClaimsSendClaim (gzip).
Process response
Success proIds = set EnqueueStatus=Enqueued, clear lease, set LastEnqueuedUtc.
Failed proIds = keep EnqueueStatus=Failed, AttemptCount += 1, set NextRetryUtc per schedule (or null if manual beyond cap), set LastError, clear lease.
Update DispatchDispatchItems and ApiCallLog.
### 6. Stream D — ResumeCompletion + Requeue incompletes + Attachments
Behavior summary
Poll completion via GetHISProIdClaimsByBcrId for batches with HasResume=1 (or BatchStatus=HasResume).
Mark returned hisProIds as Completed.
Diff the batch claim list vs returned list; schedule requeue for incompletes and resend them in packets of 1..40 to SendClaim.
After claim becomes Completed, process its attachments upload to Azure Blob, set OnlineURL, then POST to attachment endpoint (TODO).
#### 6.1 Completion polling
Pick batches where HasResume=1 (and BcrId is not null).
Call GET apiClaimsGetHISProIdClaimsByBcrId{bcrId}.
Response data.hisProIds is the list that is confirmed inserted downstream.
Mark matching claims CompletionStatus=Completed, LastResumeCheckUtc=now.
If all claims in the batch are Completed = set BatchStatus=Completed and HasResume=0.
#### 6.2 Detect incompletes (diff) and schedule requeue
Compute IncompleteClaims = {claims in batch with EnqueueStatus=Enqueued AND CompletionStatus=Unknown} - {hisProIds}.
For each incomplete claim schedule requeue by setting NextRequeueUtc=now (or now+small jitter) and increment RequeueAttemptCount when a requeue send is attempted.
Requeue sends must be packetized in 1..40 to SendClaim, gzip.
Before requeue send, inject provider_dhsCode and bCR_Id into claimHeader exactly like Stream B.
#### 6.3 Requeue send (resend incompletes in 40s)
Select up to 40 claims due for requeue EnqueueStatus=Enqueued AND CompletionStatus=Unknown AND NextRequeueUtc = now.
Lease them atomically using LockedBy='Requeue' and InFlightUntilUtc (do not change EnqueueStatus away from Enqueued; keep semantics distinct).
Create Dispatch with DispatchType=RequeueIncomplete and DispatchItems.
POST apiClaimsSendClaim (gzip).
Process response
If proId appears in successClaimsProidClaim = keep EnqueueStatus=Enqueued and update LastEnqueuedUtc (it was re-enqueued).
If proId appears in failClaimsProidClaim = set NextRequeueUtc = now + 3 minutes (configurable) and set LastRequeueError.
Clear lease fields and update Dispatch statuses.
Resume poll remains the authority to set CompletionStatus=Completed.
#### 6.4 Attachments (processed only after claim is Completed)
Attachment processing is out-of-band and does not block claim sendingrequeue.
Trigger when a claim transitions to CompletionStatus=Completed, enqueue its attachments (if any) for upload.
Three input scenarios (from DHS_Attachment tablejson)
Scenario 1 AttachmentType='file' and DHS_Attachment.Location contains a localUNC path (may require credentials). Read from path and validate file.
Scenario 2 AttachmentType='byte' and DHS_Attachment.Location contains raw bytes. Validate that bytes represent a valid file before upload.
Scenario 3 AttachBit contains base64 (byte[]) in DHS_Attachment.AttachBit. Validate bytes represent a valid file before upload.
Upload destination Azure Blob Storage. After upload, obtain URL and set OnlineURL in local attachment record and outgoing JSON.
Post attachments to backend endpoint TODO (endpoint + request body will be provided later).
Local tracking use Attachment.UploadStatus with leasing and retry (similar to claim retries).
### 7. Crash recovery expectations (all streams)
On app startup
Mark any Claim rows with InFlightUntilUtc  now as eligible again by clearing LockedByInFlightUntilUtc and restoring EnqueueStatus (NotSentFailedEnqueued as appropriate).
Mark any Dispatch rows left in InFlight as Failed with LastError='App restart during request' (diagnostics).
If app crashes mid-dispatch, claims may remain InFlight until InFlightUntilUtc expires.
On restart, streams resume by selecting eligible work NotSentFailedIncomplete where lease expires.
Dispatch records remain as audit; if a dispatch died mid-flight, it can be marked Failed by a watchdog or simply left as InFlight with last update timestamp for diagnostics.
### 8. Minimal API contract summary (from API examples)
POST apiAuthenticationlogin (gzip) = 200 + succeeded==true to login.
GET apiProviderGetProviderConfigration{providerDhsCode} = config payload (providerInfo, providerPayers, domainMappings). Cached for 1 day.
POST apiBatchCreateBatchRequest (gzip) = returns resultItems with bcrId per request.
POST apiClaimsSendClaim (gzip) = response data.successClaimsProidClaim[] and data.failClaimsProidClaim[].
GET apiClaimsGetHISProIdClaimsByBcrId{bcrId} = response data.hisProIds[].
POST apiDomainMappingInsertMissMappingDomain (gzip) = returns insertedskippedfailed items.
### 9. Non-negotiable rules (from business)
Fetching must never be blocked by sending, retry, missing mappings, or validation.
Sending must never be blocked by validation issues or missing domain mappings.
SendClaim inserts into queue only; backend may reject duplicates (treat as non-fatal; completion is still confirmed by Resume).
Retryable = RequestedIds − successClaimsProidClaim[].
Resume is the authority for completion; requeue incomplete is safe because downstream insertion is idempotent.
Claims are sent in packets of ### 1...40 (gzip).
Missing domain mapping post must be gzip and must run in parallel (never blocks sending).
ProviderDhsCode must be injected per SendClaim request (and BcrIdbatch request id too).
GroupID is used ONLY in Login request body; stored locally and required for every app start (no session).
### 10. Timing rules
Fetch interval every 5 minutes by default (configurable).
Auto retry backoff 1 minute, then 3, then 6 (max 3 auto retries).
Manual retry packet-level, with 10-minute debounce per batchaction (configurable).
Lease duration default 120 seconds (configurable).
Resume polling configurable (e.g., every 2–5 minutes) for batches with HasResume=### 1.
Provider configuration cache TTL default 1 day (configurable) and refresh on app restart  provider login.


# Integration_Adapters_v### 1.0.docx

## Provider Integration Guide (Adapters)
How to onboard a new provider using Tables  Views  Custom SQL without changing core stream logic

### 1. Purpose and contract
This document defines the technical contract for integrating a provider HIS database into the Provider-Side Data Agent. The goal is that an average developer can onboard a new provider by configuration and adapter wiring without touching the core stream logic (Streams A–D).
Non-goals
This document does not define UI screens, authorization policies, or backend database internals.
This document does not prescribe a specific ORM; the provider DB layer is read-only and can use ADO.NETDapperEF Core (read-only) as the team prefers.
This document does not include implementation code; it defines shapes, rules, and validation expectations.
### 2. Key concepts (what streams assume)
#### 2.1 Canonical ClaimBundle
All integration modes (Tables  Views  Custom SQL) must produce the same canonical in-memory structure per claim ClaimBundle.
ClaimBundle.claimHeader (single object)
ClaimBundle.serviceDetails[]
ClaimBundle.diagnosisDetails[]
ClaimBundle.labDetails[]
ClaimBundle.radiologyDetails[]
ClaimBundle.opticalVitalSigns[]
The worker streams never query the provider DB directly. They operate only on staged ClaimPayload JSON and Claim state in SQLite.
#### 2.2 Claim identity ProIdClaim (always int)
ProIdClaim is the internal claim key used in SQLite, sending, retry, and resume diffing.
ProIdClaim must always be an int. Even if some upstream DTO shows it as string, the agent treats it as int and stores it as int.
In Custom integrations, the provider claim key column may have a different name, but it must still be numericint-compatible.
Note hisProIdClaim is not an invoice number and may not match provider identifiers. The provider-side claim key that we stage and send is ProIdClaim (int).
#### 2.3 Sending vs completion (important for integration)
SendClaim enqueues claims only. A claim is 'Enqueued' when SendClaim returns the claim ID in successClaimsProidClaim[].
Completion is confirmed only by Resume (GetHISProIdClaimsByBcrId).
Requeue of incompletes is safe because downstream insertion is idempotent.
#### 2.4 Stream A fetch size vs send packet size
Stream A fetches all claims for CompanyCode + DateRange (not 40-by-40).
Stream BCD send claims to SendClaim in packets of ### 1..40 using gzip.
#### 3. Supported DB engines and connection strategy
#### 3.1 Supported engines
SQL Server (baseline).
Extensible PostgreSQL  Oracle  MySQL can be added later via provider factories if required.
#### 3.2 Secure connection storage and runtime usage
Provider connection strings are stored encrypted at rest in SQLite (ProviderProfile.EncryptedConnectionString).
Decrypt only in-memory when creating a connection. Never log decrypted material.
All provider DB access is read-only and must use parameterized queries (no string concatenation).
Use connection pooling and short-lived connections (open → query → close).
Apply command timeout and make it configurable.
#### 4. Integration modes (Tables vs Views vs Custom SQL)
IntegrationType determines how the adapter reads the data source. The adapter produces canonical ClaimBundle and a claim ID list.
#### 4.1 Tables integration
Use when the provider exposes stable normalized tables for claim header and details.
Configured objects HeaderTable, ServiceTable, DiagnosisTable, LabTable, RadiologyTable, OpticalTable, AttachmentTable (optional).
Each table must have a claim key column (usually ProIdClaim, but configurable).
Stream A pattern list claim IDs from HeaderTable filtered by CompanyCode + DateRange, then for each claim ID read header + each detail table by claim key.
Expected provider-side indexes (recommended)
Header (CompanyCode, InvoiceDate) or (CompanyCode, ClaimDate) plus claim key column.
Each detail table index on claim key column.
#### 4.2 Views integration
Use when the provider exposes read-only views. Two supported styles Entity Views (recommended) and Single Denormalized View (allowed with extra grouping rules).
A) Entity views (recommended)
Separate views per entity vw_ClaimHeader, vw_ServiceDetails, vw_DiagnosisDetails, vw_LabDetails, vw_RadiologyDetails, vw_OpticalVitalSigns, vw_Attachments.
Each view must expose the claim key column. Best practice alias it as ProIdClaim.
Mapping is identical to Tables mode (each view behaves like a table).
B) Single denormalized view (allowed)
One view returns header + repeated detail columns across rows (row explosion).
Adapter must group rows into arrays and de-duplicate detail records using stable natural keys (e.g., ServiceID, DiagnosisID) or a computed row signature.
Risk duplicates and performance. Use only if provider cannot supply entity views.
Recommendation prefer entity views where possible to keep mapping deterministic and debugging simpler.
#### 4.3 Custom SQL templates integration
Use when tablesviews cannot be used directly or when claim key column naming differs. Admin stores SQL templates in SQLite; the adapter executes templates with parameters.
Two supported custom template styles
Style 1 (preferred) templates return canonical column aliases (ProIdClaim, InvoiceDate, CompanyCode, etc.). Mapping is straightforward.
Style 2 (flexible) templates return provider column names; a ProviderColumnMap configuration maps provider columns to canonical fields.
Template safety rules (required)
Templates must be SELECT-only.
Templates must be parameterized (CompanyCode, StartDate, EndDate, ClaimKey as needed).
Templates must not modify provider DB (no INSERTUPDATEDELETEMERGE).
Templates should be stable and versioned; changes should bump UpdatedUtc and be tested via schema probe (see Section 10).
### 5. ProIdClaim mapping when the provider field name differs
Problem in Custom integrations, the provider claim key column might not be named ProIdClaim (but remains an int).
#### 5.1 Configuration knob ClaimKeyColumnName
ProviderExtractionConfig (or per-entity ProviderSourceConfig) stores ClaimKeyColumnName.
During extraction, the adapter reads ClaimKeyColumnName and maps it into canonical claimHeader.proIdClaim (int).
This enables integration without hardcoding provider naming in code.
#### 5.2 Best practice always alias to ProIdClaim
In viewscustom SQL, alias the provider claim key to ProIdClaim.
ClaimKeyColumnName still remains useful as a validationprobe check and for providers that cannot alias.
### 6. Query patterns (conceptual shapes)
This section describes what each query must return, not the SQL syntax.
#### 6.1 List Claim IDs by CompanyCode + DateRange
Input parameters CompanyCode, StartDate, EndDate.
Output a list of claim keys (int) representing ProIdClaim.
Performance prefer keyset paging for large ranges (ClaimKey  lastSeen ORDER BY ClaimKey).
Date field use the configured batch date field (recommended InvoiceDate).
#### 6.2 Read Claim Header by claim key
Input parameter ClaimKey (int).
Output shape one row of header fields.
Must include claim key (int) and CompanyCode, and at least one date field (InvoiceDate or equivalent) to compute MonthKey.
#### 6.3 Read detail entities by claim key
Input parameter ClaimKey (int).
Output shape 0..N rows per entity type.
Each detail row must include claim key (int) and enough fields to populate the corresponding ClaimBundle array items.
De-duplication if the source does not provide a stable row ID (ServiceIDDiagnosisID), the adapter must dedupe by a stable signature of key fields.
#### 6.4 Attachments query pattern (post-completion scope)
Attachments are sent in a separate request for completed claims.
Attachment source can be from provider DB tablesviews or from custom SQL templates.
Output shape must include ProIdClaim (int), AttachmentType, Location, andor AttachBit fields as available.
### 7. Required minimum fields to build ClaimBundle
Even if the backend supports many fields, the agent requires a minimum to stage and operate deterministically.
#### 7.1 Minimum required for staging (Stream A)
claimHeader.proIdClaim (int) - required.
claimHeader.companyCode (string) - required (used in fetchingbatching).
A date field to compute MonthKey (YYYYMM) - required; recommended invoiceDate.
If other fields are missing, record ValidationIssue as diagnostics but continue staging and sending (business rule).
#### 7.2 Fields injected before each SendClaim (Streams BCD)
claimHeader.provider_dhsCode - from local AppSettings.ProviderDhsCode.
claimHeader.bCR_Id - from Batch.BcrId (batch request id).
Injection happens immediately before sending (so staged payload remains deterministic and stable).
#### 7.3 Details arrays
Each details collection may be empty if the provider has no records for that entity.
If details exist, include them; each item should include ProIdClaim for traceability.
### 8. Domain mapping extraction contract (for missing mapping posting)
Stream A must extract distinct values for configured domain mapping fields (e.g., gender, claim type, visit type) and compare to local DomainMapping coverage.
The list of mapping-check fields is driven by provider configuration payload (ProviderConfigCache.domainMappings) andor a local configured list of FieldPaths.
Distinct extraction should be done across the staged claims for the run (CompanyCode + DateRange).
If a distinct value is not mapped locally, insert DomainMapping row with MappingStatus=Missing and post it using gzip to InsertMissMappingDomain.
Posting missing mappings never blocks sending; failures are logged and retried later.
### 9. Recommended local SQLite schema updates (to support robust adapters)
Your current schema supports basic tablesviewscustom SQL, but to make integrations predictable and maintainable (especially Custom and hybrid providers), the following additive changes are recommended.
#### 9.1 New table ProviderSourceConfig (recommended)
Purpose define, per entity type, whether data comes from a Table, View, or Custom SQL template, and how to locate it.
Key fields (conceptual) ProviderCode, EntityType, SourceKind, SourceName (tableview), TemplateName (custom), ClaimKeyColumnName (optional override), UpdatedUtc.
#### 9.2 New table ProviderSqlTemplate (recommended for Custom mode)
Purpose store named SQL templates with a clear parameter contract and auditing.
Key fields ProviderCode, TemplateName, SqlText, ParameterContract (expected parameters), UpdatedUtc, Notes.
#### 9.3 New table ProviderColumnMap (required for Custom Style 2)
Purpose map provider output column names to canonical ClaimBundle field paths when templates cannot alias canonically.
Key fields ProviderCode, EntityType, CanonicalFieldPath, ProviderColumnName, DataTypeHint, IsMinimumRequired, TransformHint, UpdatedUtc.
#### 9.4 AppSettings (ensure present)
Store GroupID and ProviderDhsCode in SQLite.
GroupID is used only in Login request body.
ProviderDhsCode is used to load provider configuration (cached) and is injected into every SendClaim payload.
### 10. Schema probe and validation (prevent surprises)
A schema probe is a non-business operation that validates the provider integration configuration before a full run.
Verify all configured sources exist (tablesviews) or templates exist (custom).
Verify ClaimKeyColumnName exists and is int-compatible in each configured entity source.
Verify minimum required fields can be produced (Section 7).
Optionally run a sample extraction for N claims (e.g., 5) and store diagnostics in ValidationIssueApiCallLog.
Recommendation run schema probe on first provider setup and after any changes in Settings.
### 11. Attachments integration (post-completion)
Attachments are in scope and are sent only after claims are completed (confirmed by Resume). Attachments are uploaded to Azure Blob and then posted to an endpoint (endpoint TBD).
#### 11.1 Three supported attachment source scenarios
Scenario 1 AttachmentType indicates file path. Location contains a local path or networkUNC path (may require credentials). Read file from path and validate it's a real file.
Scenario 2 AttachmentType indicates raw bytes stored in Location. Validate bytes represent a real file before upload.
Scenario 3 AttachBit contains base64 (byte[]) content. Decode and validate bytes represent a real file before upload.
#### 11.2 Output contract
Upload to Azure Blob Storage and obtain a URL.
Set OnlineURL in the local attachment record and in the outgoing attachment JSON before posting to backend endpoint (TODO).
Attachment processing is out-of-band and must not block claim sending.
### 12. Onboarding checklist (step-by-step)
Create ProviderProfile (ProviderCode, DbEngine, IntegrationType, encrypted connection string).
Createconfirm AppSettings (GroupID + ProviderDhsCode).
Load provider configuration via API using ProviderDhsCode and cache it.
Configure extraction sources tablesviewscustom templates for each entity and set ClaimKeyColumnName.
If Custom Style 2 configure ProviderColumnMap for each entity.
Run schema probe confirm claim key, minimum required fields, and template safety.
Run a small test extraction for a narrow DateRange; confirm claims stage correctly and can be sent.
Confirm missing domain mapping extraction and gzip posting works (and does not block sending).
Confirm Resume diff + Requeue incompletes works for HasResume batches.
Confirm attachment extraction + Blob upload + OnlineURL setting works (endpoint posting remains TODO).
Appendix A - Suggested enums for adapter configuration
A.1 EntityType
ClaimIdList (used for list-by-range)
ClaimHeader
ServiceDetails
DiagnosisDetails
LabDetails
RadiologyDetails
OpticalVitalSigns
Attachments
A.2 SourceKind
Table - SourceName points to a table name.
View - SourceName points to a view name.
CustomTemplate - TemplateName references a ProviderSqlTemplate row.
A.3 IntegrationType (high-level)
Tables - all entities are sourced from tables.
Views - all entities are sourced from views.
Custom - all entities use templates andor column maps.
Hybrid - mix of tableviewtemplate per entity (enabled by ProviderSourceConfig).
A.4 ColumnMap DataTypeHint (for parsingnormalization)
Int, Decimal, Bool, String
Date (no time), DateTime (UTC recommended)
Json (if a provider stores a blob that must be carried as-is)
End of document.


# Implementation_Blueprint_Coding_Guide_v### 1.0.docx

## Implementation Blueprint (Coding Guide)

### 1. Goal and scope
This guide is the implementation blueprint for the dev team. It specifies the solution structure, key design patterns, concurrency model in WPF, “definition of done” per stream, and a testing strategy. It assumes the already-approved PRD, Architecture+Streams spec, SQLite Data Model Spec, Stream Specs (Behavior Contracts), Provider Integration Guide, and Domain Mapping Spec.
PHI handling treat staged payloads and attachments as sensitive. Encrypt at rest (SQLite) and protect logs.
Never block sending because of validations or missing domain mappings. Domain mapping posting runs in parallel.
Stream A fetches by CompanyCode + DateRange (all relevant claims); Streams BCD send in ### 1..40 gzip packets.
ProIdClaim is always int.
### 2. Proposed solution structure (projects and layers)
Use a clean architecture approach UI depends on Application layer; Application depends on Domain; Infrastructure and Adapters implement interfaces.
#### 2.1 Visual Studio solution layout
ProviderAgent.App (WPF)
ProviderAgent.Application
ProviderAgent.Domain
ProviderAgent.Infrastructure (SQLite, HTTP, crypto, logging)
ProviderAgent.Adapters (Provider DB engines + mapping templates)
ProviderAgent.Workers (Streams A–D + supporting loops)
ProviderAgent.Contracts (DTOs for backend endpoints; generatedhand-written)
ProviderAgent.Tests.Unit
ProviderAgent.Tests.Integration
ProviderAgent.Tests.Contract
#### 2.2 Layer responsibilities (what goes where)
WPF App (UI layer)
Login screen (email + password; GroupID used only here).
One-time setup screen on first login GroupID + ProviderDhsCode; persisted to SQLite AppSettings; editable via Settings later.
Main dashboard provider status, batch list, claim counts, last run timestamps, diagnostics.
Manual retry action is packet-level (0..40) for a batch with cooldowndebounce.
Mapping Wizard UI (non-blocking).
No business logic beyond orchestration and view models.
Application layer
Use cases  orchestrators StartStop agent, Trigger fetch now, Trigger manual retry, Open mapping wizard, Refresh config.
Stream coordination (start hosted workers, stop via cancellation).
Domain services composition (validation, mapping scan, payload injection).
Defines interfaces repositories, unit-of-work, clocktime provider, HTTP clients, provider adapters.
Domain layer
Entities + state machines Batch, Claim, Dispatch, DomainMapping, ValidationIssue, Attachment.
Enums EnqueueStatus, CompletionStatus, BatchStatus, MappingStatus, DispatchStatus, UploadStatus, etc.
Domain services claim lifecycle transitions, lease semantics, retry scheduling policy (136), debounce policy.
Policy objects config cache policy (1 dayapp restart), resend incompletes policy, attachment validation policy.
Infrastructure layer
SQLite access repositories + migrations; encryption at rest for PHI columns (ClaimPayload, Attachments).
HTTP clients loginconfigload, CreateBatchRequest, SendClaim (gzip), GetHISProIdClaimsByBcrId, InsertMissMappingDomain (gzip), Attachments endpoint (TODO).
Loggingtelemetry structured logs + ApiCallLog persistence; correlation IDs.
Crypto AES-GCM for PHI blobs; key rotation hooks (KeyId) if needed.
Adapters layer (Provider DB access)
Provider DB engine factories (SQL Server baseline; extensible).
Integration modes Tables  Views  Custom SQL templates  Hybrid.
Extraction functions list claim IDs by CompanyCode+DateRange, read header, read details, read attachments (post-completion).
No dependencies on streams; adapters are pure data access + mapping to canonical shapes.
Workers layer
Hosted worker engine inside WPF process (no Windows Service required).
Stream A FetchStage (periodic every N minutes).
Stream B Sender (continuous work loop).
Stream C Retry (continuous work loop).
Stream D ResumeRequeue (polling loop).
Supporting loops MissingMappingPoster, AttachmentUploader (post-completion), ConfigRefresher.
### 3. Key design patterns (non-negotiable)
#### 3.1 Outbox + state machine (SQLite as the source of truth)
SQLite stores the authoritative state of each claim and each dispatch. Workers are stateless; they select work from SQLite, acquire leases, perform side effects (HTTP calls), then persist results.
Claim is a state machine split into EnqueueStatus (NotSentInFlightEnqueuedFailed) and CompletionStatus (UnknownCompleted).
DispatchDispatchItem provide packet-level audit, partial success handling, and operational diagnosis.
DomainMapping lifecycle is persisted and posting is idempotent via unique keys.
Leases prevent collisions between SenderRetryRequeue and provide crash recovery.
#### 3.2 Repository pattern
One repository per aggregate root BatchRepository, ClaimRepository, DispatchRepository, DomainMappingRepository, ValidationIssueRepository, AttachmentRepository, SettingsRepository, ApiCallLogRepository.
Repositories expose only the queries needed by workers (e.g., SelectEligibleClaimsForSend(limit=40)).
Avoid generic repositories; keep explicit queries so behavior stays readable.
#### 3.3 Unit of Work (transaction boundaries)
Lease acquisition and state transitions must be atomic and transaction-protected in SQLite.
A UoW wraps select candidates → update with lease → create dispatch rows → commit.
After HTTP call, another UoW wraps update dispatch status + per-claim results + release leases.
#### 3.4 Idempotency patterns
SendClaim is idempotent at downstream DB insertion level (safe to requeue incompletes).
Client must avoid local duplicate rows via PKsunique constraints (ProviderCode+ProIdClaim).
Dispatch uniqueness (BatchId+SequenceNo) prevents duplicate packet numbering.
### 4. Threading & concurrency approach in WPF
#### 4.1 Hosted worker model (recommended)
Use a “hosted worker engine” inside the WPF process (similar to IHostedService), started after successful login and provider selection. Each stream runs on background Tasks with CancellationTokens.
WorkerEngine starts StreamA scheduler, StreamB loop, StreamC loop, StreamD loop, and supporting loops.
All background code uses asyncawait; no blocking calls on the UI thread.
Workers report progress via thread-safe event aggregator or IProgressT; ViewModels marshal to Dispatcher for UI updates.
Stop button triggers CancellationTokenSource.Cancel() and waits for graceful shutdown (with timeout).
#### 4.2 Concurrency constraints
Only one instance per stream is required (single-user + single-provider).
No stream may “lock” SQLite for long durations. Keep transactions short.
SenderRetryRequeue must not collide enforced by lease fields InFlightUntilUtc + LockedBy in Claim table.
Config refresh and mapping posting must be concurrency-safe and read-only regarding Claim state.
#### 4.3 Cancellation & responsiveness
Every loop observes CancellationToken and returns quickly on cancellation.
HTTP calls use cancellation tokens and timeouts.
Database operations are cancellable at the loop boundary (SQLite driver might not cancel mid-query; keep queries small).
### 5. Cross-cutting implementation rules
#### 5.1 Configuration caching (ProviderConfigCache)
Provider configuration is loaded via API using ProviderDhsCode.
Cache duration 1 day configurable OR until application restart (per latest note); choose the effective policy and document it in Settings.
If config load fails log ApiCallLog + show banner; continue with last cached config if available.
Config change triggers app restart, user triggers refresh, cache expiry.
#### 5.2 Gzip transport
All SendClaim requests are gzip POST with JSON array (#### 1..40).
InsertMissMappingDomain posting is gzip POST.
Log requestresponse sizes, status codes, duration, and safe error messages (never PHI).
#### 5.3 Claim payload injection
Before every SendClaim call (Streams BCD), inject claimHeader.provider_dhsCode and claimHeader.bCR_Id into each ClaimBundle.
ProviderDhsCode comes from AppSettings; bCR_Id comes from Batch.BcrId for that claim’s batch.
This injection happens in-memory and does not require rewriting ClaimPayload unless you choose to persist a sent-payload snapshot.
#### 5.4 Logging
ApiCallLog one row per API call (endpoint name, correlationId, timestamps, durations, status).
Local structured logs include correlationId, stream name, batchId, proIdClaim ranges, dispatchId.
Never log PHI payload or attachment bytes; log only identifiers and sizes.
### 6. Stream-by-stream “Definition of Done” checklists
#### 6.1 Stream A — FetchStage (DoD)
Given CompanyCode + DateRange, adapter fetches ALL eligible claim IDs from provider DB.
For each claim ID reads header + details and builds canonical ClaimBundle JSON.
Ensures ProIdClaim is int and stored as int in ClaimClaimPayload.
Upserts Claim (NotSent, Completion Unknown) without regressing Completed claims.
Upserts ClaimPayload (deterministic payload).
Runs unconditional completeness validation and writes ValidationIssue (severity only).
Extracts distinct domain mapping scan fields from staged payload, compares to local DomainMapping, inserts Missing items.
Ensures Batch exists and has BcrId via CreateBatchRequest when needed (MonthKey derived).
Triggers (or schedules) missing mapping posting loop; does not block staging.
All operations are restart-safe and idempotent.
#### 6.2 Stream B — Sender (DoD)
Selects up to 40 eligible claims (NotSent + Completion Unknown + lease expired) in a stable order.
Acquires leases atomically (EnqueueStatus=InFlight, LockedBy=Sender, InFlightUntilUtc).
Loads ClaimPayloads and injects provider_dhsCode + bCR_Id for each claim.
Creates Dispatch + DispatchItems and posts gzip array to SendClaim.
Processes successClaimsProidClaim[] response marks each claim Enqueued or Failed (with retry schedule) and clears lease.
Updates Dispatch status (SucceededPartiallySucceededFailed), stores Http status + last error.
Never checks domain mapping or validation as a gate; always continues sending.
Handles duplicates rejected by backend gracefully (treat as failure per response rule).
#### 6.3 Stream C — Retry (DoD)
Selects Failed claims due for auto-retry NextRetryUtc = now and AttemptCount  3 and lease expired.
Uses backoff schedule 1 min, 3 min, 6 min (max 3).
Acquires lease atomically with LockedBy=Retry; sends 1..40 gzip packet using same SendClaim processing.
After max 3 auto retries claims remain Failed and are eligible only for manual retry packets.
Manual retry action retries by packet (0..40 claims) and enforces batch-level debounce (default 10 min configurable).
Retry loop never blocks Sender; both run concurrently.
#### 6.4 Stream D — ResumeRequeue (DoD)
Polls batches with HasResume=1 and valid BcrId at configurable interval.
Calls GetHISProIdClaimsByBcrId(bcrId) and marks returned claim IDs CompletionStatus=Completed.
Computes incomplete set as batch claims minus returned list (Completion Unknown).
Resends incompletes via SendClaim in packets of 1..40 gzip (Requeue) using leases (LockedBy=Requeue).
After resend, continues polling until all batch claims are Completed, then marks Batch Completed and HasResume=0.
ResumeRequeue does not interfere with Stream C retries due to lease discipline.
#### 6.5 Supporting loop — Missing mapping poster (DoD)
Selects DomainMapping rows with status Missing (and not recently posted) and posts them gzip to InsertMissMappingDomain.
On success marks Posted + LastPostedUtc. On failure keeps Missing and schedules later.
Never blocks sending or staging.
Posting payload mapping rule providerCodeValue == providerNameValue; domainTableId == domTable_ID (the scanned enum-like value).
#### 6.6 Supporting loop — Attachments (post-completion) (DoD)
Triggers only when claim becomes Completed (confirmed by resume).
Extracts attachments from provider source; supports three scenarios file path (Location), bytes in Location, base64 bytes in AttachBit.
Validates bytes represent a real file before upload.
Uploads to Azure Blob, sets OnlineURL, then posts metadata to attachment endpoint (TODO endpointDTO).
Runs asynchronously and never blocks SendClaim.
### 7. Testing strategy summary
#### 7.1 Unit testing (fast, deterministic)
State transitions Claim enqueuecompletion transitions; retry schedule (136); manual debounce policy.
Lease acquisitionrelease logic (including crash expiry behavior).
Domain mapping scan distinct extraction from payload JSON, normalization rules, unique-key behavior.
Injection provider_dhsCode and bCR_Id into outgoing payload without mutating stored payload (unless designed).
Attachment validation detect valid file types from bytes and reject invalid content.
#### 7.2 Integration testing (SQLite + worker loops)
SQLite migrations createupgrade path; schema version checks; encryption wrappers.
Repository queries eligible selection for SenderRetryRequeue; Batch creation and month grouping; Dispatch creation.
Crash recovery simulation leave claims InFlight, restart loop, verify reclaim after InFlightUntilUtc.
Concurrency Sender and Retry run simultaneously; verify leases prevent double-sending.
#### 7.3 Contract testing (HTTP endpoints)
Validate requestresponse DTOs against the API endpoint JSON examples document.
SendClaim verify successClaimsProidClaim[] parsing and the 'retryable = requested − success' rule.
InsertMissMappingDomain gzip encoding and payload correctness (providerCodeValue == providerNameValue; domainTableId == domTable_ID).
GetHISProIdClaimsByBcrId parsing and diff computation.
Login uses GroupID only here; expects HTTP 200 with success=true; no persisted user session.
#### 7.4 Performance & resilience tests (non-functional)
Thousands of claims verify Stream A can stage without memory blow-ups (stream processing + batching DB writes).
Network instability verify retries, timeouts, and logging work without deadlocks.
PHI safety verify logs contain no payloadbytes; verify encrypted columns are not stored in plaintext.
### 8. Implementation notes (practical choices)
Use short-lived SQLite connections and short transactions.
Prefer explicit SQL for repositories to control performance and avoid hidden ORM behavior (or use Dapper with explicit SQL).
Use one CorrelationId per dispatch to trace ApiCallLog + local logs.
Keep adapter queries configurable (tablesviewstemplates) and validate via schema probe before first run.
Appendix A - Suggested internal interfaces (names only)
IProviderConfigClient, IAuthClient, IClaimsApiClient, IDomainMappingApiClient, IResumeApiClient, IAttachmentApiClient (TODO)
IProviderAdapterFactory, IProviderClaimsAdapter
IClock, IGuidProvider, ICompressionService (gzip)
IEncryptionService (AES-GCM), IKeyProvider
IUnitOfWork
IClaimRepository, IBatchRepository, IDispatchRepository, IDomainMappingRepository, IValidationIssueRepository, IAttachmentRepository, ISettingsRepository, IApiCallLogRepository
IWorker (StartAsyncStopAsync), IWorkerEngine



# Observability_and_Support

## Observability & Support


### 1. Purpose and operating principles
This playbook explains what telemetry is captured locally, how to troubleshoot common issues, and how to export a PHI-safe support bundle. It assumes the app uses a local encrypted SQLite DB and streams A–D (FetchStage, Sender, Retry, ResumeRequeue).
PHI must never be included in support artifacts. Do not export ClaimPayload or attachment contents.
DispatchDispatchItemApiCallLog provide enough information to diagnose most failures without inspecting payload content.
Use CorrelationId to stitch together related events across API calls and local dispatch history.
### 2. What data is captured (and why)
#### 2.1 Dispatch (packet-level history)
Dispatch represents a single outbound SendClaim request containing #### 1..40 claims. It is the primary unit for diagnosing send pipeline health.
Captures WHEN a packet was attempted, WHICH batch it belonged to, WHAT kind of attempt it was (Normal vs Retry vs Requeue), and the high-level result.
Acts as the audit trail to prove 'these 40 claim IDs were attempted at this time' (without payload).
Key fields operators care about DispatchId, ProviderCode, BatchId, BcrId, SequenceNo, DispatchType, DispatchStatus, AttemptCount, NextRetryUtc, RequestSizeBytes, RequestGzip, HttpStatusCode, LastError, CreatedUtc, UpdatedUtc.
#### 2.2 DispatchItem (per-claim membership of a Dispatch)
DispatchItem links each ProIdClaim to a Dispatch. It explains which claims were in which packet and the per-item outcome when applicable.
Allows answering 'Why is claim X still Failed' → check its latest DispatchItem to see which dispatch last attempted it and whether it was included in successProIdClaims[].
Supports partial success behavior backend returns successProIdClaims[]; anything not in the list is retryable.
Key fields DispatchId, ProviderCode, ProIdClaim, ItemOrder, ItemResult, ErrorMessage (sanitized).
#### 2.3 ApiCallLog (PHI-safe HTTP call ledger)
ApiCallLog captures each API call (SendClaim, CreateBatchRequest, LoadProviderConfiguration, InsertMissMappingDomain, GetHISProIdClaimsByBcrId, attachment-related endpoints). It must remain PHI-free by design.
Captures endpoint name, correlationId, timings, sizes, status code, success flag, and sanitized error.
Used to diagnose networkservice issues, performance, throttling, auth errors, and gzip issues.
Key fields EndpointName, CorrelationId, RequestUtc, ResponseUtc, DurationMs, HttpStatusCode, Succeeded, ErrorMessage (sanitized), RequestBytes, ResponseBytes, ProviderCode.
#### 2.4 How these tables work together (mental model)
Pick a batch (BatchIdBcrId).
Look at Dispatch rows for that BatchId sequence, statuses, and errors.
For a specific claim find its latest DispatchItem → get DispatchId → inspect Dispatch + related ApiCallLog (by correlationIdtime).
For resume look at ApiCallLog for GetHISProIdClaimsByBcrId and compare against Claim.CompletionStatus counts.
### 3. Common failure scenarios and where to look
#### 3.1 Provider configuration can’t load
Symptoms login ok but app can’t start streams; provider selection fails; config cache missingexpired; mappings list empty unexpectedly.
Where to look ApiCallLog where EndpointName=LoadProviderConfiguration (or equivalent).
Check HTTP status (401403 auth, 404 wrong providerDhsCode, 500 backend), DurationMs spikes (network).
Check local settings ProviderDhsCode configured; cache TTL not exceeded; app restart behavior expected.
Operator action re-enter ProviderDhsCode in Settings; retry; collect support bundle.
#### 3.2 CreateBatchRequest fails  no BcrId
Symptoms claims are staged but sender can’t inject bCR_Id; batch remains DraftReady without BcrId; HasResume never triggers.
Where to look ApiCallLog EndpointName=CreateBatchRequest.
Check request gzip enabled, auth, throttling, backend validation errors.
Related Batch table row for CompanyCode+MonthKey; verify BcrId is null; Dispatch history may be absent because Sender requires BcrId injection.
Operator action retry CreateBatchRequest; if backend down, keep staging; sending may pause only if BcrId is hard-required.
#### 3.3 SendClaim network failuretimeouts
Symptoms DispatchStatus=Failed; HttpStatusCode null or 408504; backlog grows; Retry starts scheduling.
Where to look Dispatch (LastError, UpdatedUtc), ApiCallLog EndpointName=SendClaim (DurationMs, status).
Check TLScert issues, proxy, DNS, backend outage.
Expected behavior claims move to Failed with NextRetryUtc; Sender continues on new staged claims.
#### 3.4 SendClaim returns 200 but partial success
Symptoms HttpStatusCode=200 but some claims still Failed; DispatchStatus=PartiallySucceeded; Retryable = requested - successProIdClaims[].
Where to look DispatchItems for that DispatchId (which claims were included), plus claim statuses.
Check successProIdClaims[] size vs packet size; verify diff logic; errors are 'Not in success list' or backend per-claim errors (if provided).
Expected behavior succeed list claims - Enqueued; others - Failed with retry schedule 136.
#### 3.5 Retry backlog grows (auto retry not catching up)
Symptoms many claims with EnqueueStatus=Failed and NextRetryUtc in past; DispatchType=RetrySend frequent; failure rate steady.
Where to look Claim backlog counts (Failed + due), DispatchApiCallLog for frequent failures.
Check whether failures are systemic (authendpoint down) vs data-specific.
Operator action if systemic, pause sending or wait for backend recovery (policy); if data-specific, continue sending and rely on manual retry after investigation.
#### 3.6 Resume lag  claims stuck Enqueued but not Completed
Symptoms Enqueued claims remain CompletionStatus=Unknown for long time; batch HasResume stays on; resume poll shows partial list.
Where to look ApiCallLog EndpointName=GetHISProIdClaimsByBcrId; Batch.HasResume; Claim.LastResumeCheckUtc.
Expected behavior Stream D diffs incompletes and requeues them in packets of 40, without blocking sending.
Operator action confirm resume polling interval; check backend availability; collect metrics for resume lag.
#### 3.7 Attachment uploadpost issues
Symptoms claims Completed but attachments remain pending; OnlineURL missing; attachment post endpoint returns errors.
Where to look ApiCallLog for Blob upload step and attachment metadata POST (endpoint TBD).
Check attachment type scenario (file path  bytes in location  base64 in AttachBit), file validation failures, network share access issues.
Expected behavior attachments are independent; failures should not regress claim completion; retry attachment pipeline separately.
### 4. Support bundle export (PHI-safe)
Support bundle is a single ZIP produced from the app UI that enables troubleshooting without PHI.
#### 4.1 Contents (allowed)
App versionbuild info, OS version, machine id (non-PII if possible).
Configuration metadata (no secrets) ProviderDhsCode, integration type, DB engine (but not connection strings).
SQLite extracts (PHI-safe tables only) AppMeta, ProviderProfile (redactedencrypted fields), ProviderExtractionConfig (without credentials), Batch, Claim (status-only columns), Dispatch, DispatchItem, DomainMapping (optional), ApiCallLog.
Aggregated metrics snapshots (Section 5).
Recent sanitized app logs (if file-based logging is used) with strict PHI redaction enforced.
#### 4.2 Contents (forbidden)
ClaimPayload.PayloadJson or any payload fragments.
Attachment content, base64, or local file paths that contain patient identifiers (prefer hash).
Provider DB connection strings or credentials.
Any user passwords or auth tokens.
#### 4.3 Export mechanics
Export from UI Settings → Support → 'Export Support Bundle'.
Allow choosing timeframe (last 24h  7d) to limit size.
Run a PHI-scanredaction pass on text logs before including them.
Include a manifest.json describing included files and redaction policy version.
### 5. Operational metrics (definitions and how to compute)
Metrics must be PHI-free and computed from status tables + dispatch history. They can be shown in UI and included in the support bundle.
#### 5.1 Throughput
Claims staged per hour count new Claim rows FirstSeenUtc in window.
Claims sent per hour count DispatchItems in succeededpartially succeeded dispatches per hour.
Packets per hour count Dispatch rows created per hour.
#### 5.2 Failure rate
Packet failure rate % DispatchStatus=Failed over total dispatches in window.
Claim failure rate % claims whose latest EnqueueStatus=Failed over total touched claims in window.
Partial success rate % DispatchStatus=PartiallySucceeded.
#### 5.3 Retry backlog
Due retry count Failed claims where NextRetryUtc = now and AttemptCount = 3.
Aging distribution of time since LastUpdatedUtc for Failed claims.
Manual retry wait batches currently in manual debounce cooldown (if tracked).
#### 5.4 Resume lag
Enqueued-not-completed count EnqueueStatus=Enqueued AND CompletionStatus=Unknown.
Oldest unknown completion age now - min(LastEnqueuedUtc) where CompletionStatus=Unknown.
Batches awaiting resume count Batch where HasResume=1 and not Completed.
Resume effectiveness % of incompletes that become Completed after requeue attempts.
### 6. Operator runbook (quick decision tree)
Is the problem 'nothing is sending' → Check ApiCallLog for LoadProviderConfiguration and CreateBatchRequest first.
Is the problem 'sending but many failures' → Check Dispatch + ApiCallLog for SendClaim patterns (status codes, duration).
Is the problem 'stuck Enqueued' → Check resume polling calls and HasResume batches.
Is the problem 'attachments missing' → Check attachment uploadpost calls and attachment type scenarios.
If unresolved → export support bundle and escalate with correlationIds and recent dispatch ids.
### 7. Glossary
Dispatch one outbound packet (### 1..40 claims).
DispatchItem membership of claims within a dispatch.
ApiCallLog PHI-safe record of an HTTP call and its timings.
CorrelationId GUID used to correlate calls and operations.
HasResume indicates batch requires completion confirmation via resume flow.
Resume lag time between Enqueued and Completed confirmation.



# Security_and_Compliance_PHI

## Security & Compliance (PHI) Specification
Safe-by-design requirements for the Provider-Side Data Agent (WPF + SQLite + Backend APIs)

### 1. Scope and threat model
This document defines mandatory security requirements for handling PHI (Protected Health Information) in the Provider-Side Data Agent. It covers local storage (SQLite), transport (API calls), secrets handling, logging, and lifecycle cleanup.
Assumptions
Runs on a single user workstation for a single provider at a time.
Handles thousands of claims; performance must not compromise security.
PHI exists in memory, on disk (SQLite), and in network transit.
Network is treated as hostile (MITM risk). Backend endpoints are trusted by contract.
Out of scope
Cloudbackend internal controls (except transport requirements from this client).
OS hardening policies and workstation governance (recommended separately).
Legal interpretation (HIPAAGDPR); this is an engineering specification.
### 2. PHI handling rules
#### 2.1 What is PHI in this system
Treat ALL ClaimBundle payload content as PHI unless explicitly proven otherwise. The following are always PHI or PHI-adjacent and must be protected
Patient identifiers patientName, patientNo, memberID, patientID, iDno, mobileNo, policyHolderName, policyHolderNo.
Clinical information clinicalData, diagnosisDetails, labDetails, radiologyDetails, chiefComplaint, dischargeSummary, treatmentPlan, patientHistory, physicalExamination, historyOfPresentIllness.
Care-linked dates admissionDate, dischargeDate, visitDate, diagnosisDate, proRequestDate, invoiceDate, triageDate.
Attachments file bytesbase64, file paths, and any URLs that grant access to files (SAS URLs).
Any record that links to PHI ProIdClaim in the context of a claim is sensitive.
Operational identifiers (sensitive but not PHI by themselves) providerCode, providerDhsCode, companyCode, bCR_Id, batch numbers. Protect them but do not treat as PHI.
#### 2.2 What must never be logged
Full requestresponse bodies for claims, mapping posts, resume results containing PHI, or attachments.
Any patient identifiers or clinical fields.
Any attachment bytesbase64 or decoded content.
Provider DB plaintext connection strings, passwords, network share credentials.
Encryption keys, DPAPI blobs, or key derivation material.
Allowed logging (safe observability)
CorrelationId, EndpointName, timestamps, durationMs, HTTP status, requestresponse byte sizes.
Counts stagedsentsucceededfailedcompleted, missing mappings discoveredposted.
ProIdClaim log only if explicitly enabled by a Support Mode toggle; default is redacted or hashed.
Errors sanitized messages that do not echo payload content.
#### 2.3 Redaction and hashing standard
Redact replace with '[REDACTED]'.
Hash SHA-256(value + per-install salt) and log only first 12 chars for correlation.
Truncate for safe operational text (e.g., exception summaries), truncate and remove digits where feasible.
### 3. SQLite encryption approach
#### 3.1 Classification by table
PHI ClaimPayload.PayloadJson, Attachment contentpathsOnlineURL, ValidationIssue.RawValue (if it contains PHI).
Sensitive ProviderProfile (encrypted connection string), ProviderSqlTemplateColumnMap (if present), DomainMapping (may include medical codes).
OperationalPHI-free by design Batch, Claim state, Dispatch, DispatchItem, ApiCallLog (must remain PHI-free).
#### 3.2 Approved encryption approaches (choose one)
Option A — SQLCipher (recommended)
Encrypts the entire SQLite database file.
Pros simplest to reason about; reduces risk of accidental plaintext writes.
Cons dependency and overhead; requires careful key handling.
Option B — Column-level AES-256-GCM (allowed)
Encrypt PHI columns only (PayloadJson, attachment bytespathurl, etc.) using AES-256-GCM.
Pros operational tables remain queryable in plaintext.
Cons higher complexity; greater risk of mistakes; must manage per-record nonce + auth tag.
#### 3.3 Key storage (Windows DPAPI)
Generate a random 256-bit master key on first run.
Protect the master key using Windows DPAPI (CurrentUser scope) and store only the protected blob in SQLite (AppSettingsAppMeta).
Decrypt DPAPI blob only in-memory when opening DB or encryptingdecrypting PHI.
Maintain an EncryptionKeyIdKeyId to support rotation.
#### 3.4 Key rotation plan
Store KeyId + KeyCreatedUtc. Keep 'active' and 'previous' keys for decrypting existing rows.
Rotate generate new key → mark active → re-encrypt PHI rows in background (or lazily on read) → retire old key when safe.
Rotation must not block sending; treat as maintenance.
### 4. Secrets handling
#### 4.1 Provider connection strings
Store encrypted at rest (ProviderProfile.EncryptedConnectionString).
Decrypt only for connection creation; keep plaintext in memory for minimal time.
Never log plaintext or include it in support exports.
Allow updates via Settings (credential rotation).
#### 4.2 Backend auth and user login
User logs in on every app start (no auto-login). Never store user password.
If backend issues tokens prefer memory-only. If refresh tokens must be stored, protect with DPAPI and keep minimal metadata.
GroupID is used only in the Login request body; store GroupID locally (DPAPI-protected if treated as secret) and never log it.
### 5. Transport security
#### 5.1 TLS
TLS 1.2+ only.
Validate server certificates using OS trust store.
Use HttpClient with timeouts and cancellation; avoid infinite hangs.
Use correlation IDs for tracing without payload logging.
#### 5.2 Gzip
Gzip compress large POST requests SendClaim, InsertMissMappingDomain, and Attachment metadata post (endpoint TBD).
Use Content-Encoding gzip and Content-Type applicationjson.
Gzip occurs inside TLS; acceptable and required for performance.
#### 5.3 Certificate pinning (optional)
If required by deployment pin public key hash for leaf or intermediate.
Support dual pins (current + next) for rotation.
Fail closed when pinning is enabled and pin mismatch occurs.
### 6. Data retention & cleanup policy
#### 6.1 Retention objectives
Minimize local PHI while preserving resiliency (retryresume).
Keep troubleshooting PHI-free prefer dispatch metadata + ApiCallLog, not payloads.
Support user-initiated cleanup from UI.
#### 6.2 Recommended lifecycle (defaults, configurable)
ClaimPayload (PHI) retain until CompletionStatus=Completed AND attachments (if any) are uploaded + posted successfully; then purge or archive encrypted.
Attachment staged files purge immediately after blob upload + metadata post success (or within 24 hours max).
Operational history (BatchDispatchApiCallLog) retain 30–90 days (PHI-free by design).
DomainMapping retain until admin cleanup (treat as sensitive).
#### 6.3 Purge mechanics
Delete staged attachment files; overwrite where feasible before delete.
For SQLite run VACUUM after large PHI deletions to reduce remnants on disk (make configurable due to SSD wear).
If using SQLCipher consider rekey + VACUUM for stronger cleanup.
### 7. UIUX security requirements
Login screen on every app start; do not remember user password.
First-run Setup screen captures GroupID and ProviderDhsCode; editable via Settings.
Diagnostics must be PHI-free. Any support export must be PHI-free by design.
Do not display raw payload JSON by default; gate any deep diagnostics behind Support Mode with warnings.
### 8. Logging and observability (PHI-safe)
Use ApiCallLog as the primary local store of PHI-free observability.
Log every API call with correlation id, endpoint name, requestresponse timestamps, duration, bytes, status, and success flag.
Sanitize errors (strip payload content).
Never store payload bodies in ApiCallLog.
### 9. Developer acceptance checklist
No claim payload or attachment bytes are logged anywhere.
SQLite at-rest encryption implemented via SQLCipher or AES-GCM; keys are DPAPI-protected.
Provider DB connection strings are encrypted at rest and never logged.
All backend calls use TLS #### 1.2+; gzip used where required.
Retention policy purges PHI after completion + attachments success.
Support exports contain no PHI (only counts, timings, IDs hashedredacted).
End of document.



# rules_update

Domain Mappings + Missing Domain Mappings + ScanRefresh Behavior
Please review the domain-related Knowledge Base documents and update them to reflect the following changes
1) Load approved + missing domain mappings on login (cached)
On user login, the application must load both
Approved domain mappings data.domainMappings
Missing domain mappings data.missingDomainMappings
Source endpoint
GET apiProviderGetProviderConfigration{providerDHSCode}
Both lists must be displayed in the WPF UI in separate tabs
Tab 1 Approved (domainMappings)
Tab 2 Missing (missingDomainMappings)
Caching rules must match the existing configuration caching strategy (same cache + persistence approach used for other provider configuration items).

2) Refresh button updates cache + persisted config for both lists
The UI must include a Refresh button that refreshes both lists and updates cache + persisted configuration.
Source endpoint
GET apiProviderGetMissingAndDomainMapping{providerDHSCode}
Returns the same payload shape as the configuration endpoint.
Important merge rule Refresh must not override locally discovered missing mappings created by scanning (see section 4). The UI must show those newly discovered missing items even if the server response doesn’t contain them yet.

3) Before First stream run first of all we will show a user a message with the total number of claims in this batch from the provider HIS system based on the integration type ,and the financial amounts validation based on the integration type. In the first implementation is table to table integration so in the DHSClaim_Header table the sum(TotalNetAmount) = sum(ClaimedAmount) - sum(TotalDiscount) - sum(TotalDeductible) and should show a user a message with the total number of claims  and if the financial amounts is valid or not  based on the batch company code, the batch start date and  the batch end date. The user should confirm this message before the stream run.   
During the first stream run (or after claim transformation to JSON per integration type—either is acceptable), the system must scan the provider HIS database based on integration type and filter results by
batch company code
batch start date
batch end date
The scan covers these claim fieldsdomains
claimHeader.patientGender (Gender)
claimHeader.claimType (ClaimType)
claimHeader.visitType (VisitType)
claimHeader.subscriberRelationship (SubscriberRelationship)
claimHeader.maritalStatus (MaritalStatus)
claimHeader.nationality (Nationality)
claimHeader.patientIdType (PatientIdType)
claimHeader.admissionType (AdmissionType)
claimHeader.encounterStatus (EncounterStatus)
claimHeader.triageCategoryTypeID (TriageCategory)
serviceDetails[].serviceType (ServiceType)
serviceDetails[].serviceEventType (ServiceEventType)
serviceDetails[].pharmacistSelectionReason (PharmacistSelectionReason)
serviceDetails[].pharmacistSubstitute (PharmacistSubstitute)
diagnosisDetails[].diagnosisType (DiagnosisType)
diagnosisDetails[].onsetConditionTypeID (ConditionOnset)

4) Scan logic (example patientGender domainTableId = 12)
Example logic for patientGender (domainTableId = 12)
Read distinct values of claimHeader.patientGender from the scan source (tableJSON stage).
Compare each distinct value to the approved domain mappings where
domTable_ID = 12
check whether the value exists in providerDomainValue of the approved mappings
If a distinct value is not found in approved mappings
Check if it already exists in the local SQLite domain mapping store
If yes → skip
If no → insert into SQLite and add it to a new missing items list for posting
After scanning all domains, post missing items to
POST apiDomainMappingInsertMissMappingDomain
Request body
```json
{
  providerDhsCode string,
  mismappedItems [
    {
      providerCodeValue f,
      providerNameValue f,
      domainTableId 12
    },
    {
      providerCodeValue P,
      providerNameValue P,
      domainTableId 14
    }
  ]
}
```
5) KB update output required
After scanning the KB documents, please provide
Which documentssections need updating
Exactly what textcontract needs to be replaced or added
Any conflictsgaps found (e.g., payload naming, SQLite schema support for providerNameValue)



---
# Appendix B — swagger.json (verbatim)
```json
{
  openapi ### 3.0.1,
  info {
    title HISToMotalabatechAPI,
    version ### 1.0
  },
  paths {
    apiAuthenticationlogin {
      post {
        tags [
          Authentication
        ],
        requestBody {
          content {
            applicationjson {
              schema {
                $ref #componentsschemasLoginRequestDto
              }
            },
            textjson {
              schema {
                $ref #componentsschemasLoginRequestDto
              }
            },
            application+json {
              schema {
                $ref #componentsschemasLoginRequestDto
              }
            }
          }
        },
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              }
            }
          },
          401 {
            description Unauthorized,
            content {
              textplain {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasLoginResponseDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiBatchGetBatchRequest{providerDhsCode} {
      get {
        tags [
          Batch
        ],
        parameters [
          {
            name providerDhsCode,
            in path,
            required true,
            schema {
              type string
            }
          },
          {
            name Month,
            in query,
            schema {
              type integer,
              format int32
            }
          },
          {
            name Year,
            in query,
            schema {
              type integer,
              format int32
            }
          },
          {
            name PayerId,
            in query,
            schema {
              type integer,
              format int32
            }
          },
          {
            name PageNumber,
            in query,
            schema {
              type integer,
              format int32
            }
          },
          {
            name PageSize,
            in query,
            schema {
              type integer,
              format int32
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasBatchCreationRequestDtoPagedResultDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiBatchCreateBatchRequest {
      post {
        tags [
          Batch
        ],
        requestBody {
          content {
            applicationjson {
              schema {
                $ref #componentsschemasCreateBatchRequestDto
              }
            },
            textjson {
              schema {
                $ref #componentsschemasCreateBatchRequestDto
              }
            },
            application+json {
              schema {
                $ref #componentsschemasCreateBatchRequestDto
              }
            }
          }
        },
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasCreateBatchResponseDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiBatchDeleteBatch {
      post {
        tags [
          Batch
        ],
        requestBody {
          content {
            applicationjson {
              schema {
                $ref #componentsschemasDeleteBatchRequestDto
              }
            },
            textjson {
              schema {
                $ref #componentsschemasDeleteBatchRequestDto
              }
            },
            application+json {
              schema {
                $ref #componentsschemasDeleteBatchRequestDto
              }
            }
          }
        },
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasBooleanBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiClaimsGetHISProIdClaimsByBcrId{bcrId} {
      get {
        tags [
          Claims
        ],
        parameters [
          {
            name bcrId,
            in path,
            required true,
            schema {
              type integer,
              format int64
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasClaimHISProIdDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiClaimsSendClaim {
      post {
        tags [
          Claims
        ],
        requestBody {
          content {
            applicationjson {
              schema {
                type array,
                items {
                  $ref #componentsschemasClaimDto
                }
              }
            },
            textjson {
              schema {
                type array,
                items {
                  $ref #componentsschemasClaimDto
                }
              }
            },
            application+json {
              schema {
                type array,
                items {
                  $ref #componentsschemasClaimDto
                }
              }
            }
          }
        },
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasSendClaimResultDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiDashboardGetDashboardData{providerDhsCode} {
      get {
        tags [
          Dashboard
        ],
        parameters [
          {
            name providerDhsCode,
            in path,
            required true,
            schema {
              type string
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasDashboardDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiDomainMappingGetProviderDomainMapping{providerDhsCode} {
      get {
        tags [
          DomainMapping
        ],
        parameters [
          {
            name providerDhsCode,
            in path,
            required true,
            schema {
              type string
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasDomainMappingDtoListBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiDomainMappingGetMissingDomainMappings{providerDhsCode} {
      get {
        tags [
          DomainMapping
        ],
        parameters [
          {
            name providerDhsCode,
            in path,
            required true,
            schema {
              type string
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasMissingDomainMappingDtoListBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiDomainMappingGetProviderDomainMappingsWithMissing{providerDhsCode} {
      get {
        tags [
          DomainMapping
        ],
        parameters [
          {
            name providerDhsCode,
            in path,
            required true,
            schema {
              type string
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderDomainMappingsDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiDomainMappingInsertMissMappingDomain {
      post {
        tags [
          DomainMapping
        ],
        requestBody {
          content {
            applicationjson {
              schema {
                $ref #componentsschemasInsertMissMappingDomainRequestDto
              }
            },
            textjson {
              schema {
                $ref #componentsschemasInsertMissMappingDomainRequestDto
              }
            },
            application+json {
              schema {
                $ref #componentsschemasInsertMissMappingDomainRequestDto
              }
            }
          }
        },
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasInsertMissMappingDomainResultDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiHealthCheckAPIHealth {
      get {
        tags [
          Health
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasObjectBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiProviderGetProviderInfo{providerDhsCode} {
      get {
        tags [
          Provider
        ],
        parameters [
          {
            name providerDhsCode,
            in path,
            required true,
            schema {
              type string
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderInfoDtoBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiProviderGetProviderCompanyCodes{providerDhsCode} {
      get {
        tags [
          Provider
        ],
        parameters [
          {
            name providerDhsCode,
            in path,
            required true,
            schema {
              type string
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderPayerDtoListBaseResponse
                }
              }
            }
          }
        }
      }
    },
    apiProviderGetProviderConfigration{providerDhsCode} {
      get {
        tags [
          Provider
        ],
        parameters [
          {
            name providerDhsCode,
            in path,
            required true,
            schema {
              type string
            }
          }
        ],
        responses {
          200 {
            description Success,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              }
            }
          },
          204 {
            description No Content,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              }
            }
          },
          400 {
            description Bad Request,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              }
            }
          },
          500 {
            description Server Error,
            content {
              textplain {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              },
              applicationjson {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              },
              textjson {
                schema {
                  $ref #componentsschemasProviderConfigrationDtoBaseResponse
                }
              }
            }
          }
        }
      }
    }
  },
  components {
    schemas {
      BatchCreationRequestDto {
        type object,
        properties {
          bcrId {
            type integer,
            format int64
          },
          bcrCreatedOn {
            type string,
            format date-time,
            nullable true
          },
          bcrMonth {
            type integer,
            format int32,
            nullable true
          },
          bcrYear {
            type integer,
            format int32,
            nullable true
          },
          userName {
            type string,
            nullable true
          },
          payerNameEn {
            type string,
            nullable true
          },
          payerNameAr {
            type string,
            nullable true
          },
          companyCode {
            type string,
            nullable true
          },
          midTableTotalClaim {
            type integer,
            format int32,
            nullable true
          },
          batchStatus {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      BatchCreationRequestDtoPagedResultDto {
        type object,
        properties {
          data {
            type array,
            items {
              $ref #componentsschemasBatchCreationRequestDto
            },
            nullable true
          },
          pageNumber {
            type integer,
            format int32
          },
          pageSize {
            type integer,
            format int32
          },
          totalCount {
            type integer,
            format int32
          },
          totalPages {
            type integer,
            format int32,
            readOnly true
          },
          hasPreviousPage {
            type boolean,
            readOnly true
          },
          hasNextPage {
            type boolean,
            readOnly true
          }
        },
        additionalProperties false
      },
      BatchCreationRequestDtoPagedResultDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasBatchCreationRequestDtoPagedResultDto
          }
        },
        additionalProperties false
      },
      BatchPayerChartDto {
        type object,
        properties {
          batchId {
            type integer,
            format int64
          },
          createdOn {
            type string,
            format date-time,
            nullable true
          },
          month {
            type integer,
            format int32,
            nullable true
          },
          year {
            type integer,
            format int32,
            nullable true
          },
          batchStatus {
            type string,
            nullable true
          },
          payerNameEn {
            type string,
            nullable true
          },
          payerNameAr {
            type string,
            nullable true
          },
          totalClaims {
            type integer,
            format int32,
            nullable true
          }
        },
        additionalProperties false
      },
      BatchRequestItemDto {
        type object,
        properties {
          companyCode {
            type string,
            nullable true
          },
          batchStartDate {
            type string,
            format date-time
          },
          batchEndDate {
            type string,
            format date-time
          },
          totalClaims {
            type integer,
            format int32,
            nullable true
          },
          providerDhsCode {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      BatchStatusChartDto {
        type object,
        properties {
          statusName {
            type string,
            nullable true
          },
          statusDisplay {
            type string,
            nullable true
          },
          count {
            type integer,
            format int32
          },
          percentage {
            type number,
            format double
          }
        },
        additionalProperties false
      },
      BooleanBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            type boolean
          }
        },
        additionalProperties false
      },
      ClaimDto {
        type object,
        properties {
          claimHeader {
            $ref #componentsschemasDHSClaimHeaderDto
          },
          opticalVitalSigns {
            type array,
            items {
              $ref #componentsschemasDHSOpticalVitalSignDto
            },
            nullable true
          },
          diagnosisDetails {
            type array,
            items {
              $ref #componentsschemasDHSDiagnosisDetailDto
            },
            nullable true
          },
          labDetails {
            type array,
            items {
              $ref #componentsschemasDHSLabDetailDto
            },
            nullable true
          },
          radiologyDetails {
            type array,
            items {
              $ref #componentsschemasDHSRadiologyDetailDto
            },
            nullable true
          },
          serviceDetails {
            type array,
            items {
              $ref #componentsschemasDHSServiceDetailDto
            },
            nullable true
          }
        },
        additionalProperties false
      },
      ClaimHISProIdDto {
        type object,
        properties {
          hisProIdClaim {
            type array,
            items {
              type integer,
              format int32
            },
            nullable true
          }
        },
        additionalProperties false
      },
      ClaimHISProIdDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasClaimHISProIdDto
          }
        },
        additionalProperties false
      },
      CommonType {
        type object,
        properties {
          id {
            type string,
            nullable true
          },
          code {
            type string,
            nullable true
          },
          name {
            type string,
            nullable true
          },
          ksaID {
            type string,
            nullable true
          },
          ksaCode {
            type string,
            nullable true
          },
          ksaName {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      CreateBatchRequestDto {
        type object,
        properties {
          batchRequests {
            type array,
            items {
              $ref #componentsschemasBatchRequestItemDto
            },
            nullable true
          }
        },
        additionalProperties false
      },
      CreateBatchResponseDto {
        type object,
        properties {
          createdBatchIds {
            type array,
            items {
              type integer,
              format int64
            },
            nullable true
          }
        },
        additionalProperties false
      },
      CreateBatchResponseDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasCreateBatchResponseDto
          }
        },
        additionalProperties false
      },
      DHSClaimHeaderDto {
        type object,
        properties {
          proIdClaim {
            type integer,
            format int32
          },
          doctorCode {
            type string,
            nullable true
          },
          proRequestDate {
            type string,
            format date-time,
            nullable true
          },
          patientName {
            type string,
            nullable true
          },
          patientNo {
            type string,
            nullable true
          },
          memberID {
            type string,
            nullable true
          },
          preAuthID {
            type string,
            nullable true
          },
          specialty {
            type string,
            nullable true
          },
          clinicalData {
            type string,
            nullable true
          },
          claimType {
            type string,
            nullable true
          },
          referInd {
            type boolean,
            nullable true
          },
          emerInd {
            type boolean,
            nullable true
          },
          submitBy {
            type string,
            nullable true
          },
          payTo {
            type string,
            nullable true
          },
          temperature {
            type string,
            nullable true
          },
          respiratoryRate {
            type string,
            nullable true
          },
          bloodPressure {
            type string,
            nullable true
          },
          height {
            type string,
            nullable true
          },
          weight {
            type string,
            nullable true
          },
          pulse {
            type string,
            nullable true
          },
          internalNotes {
            type string,
            nullable true
          },
          companyCode {
            type string,
            nullable true
          },
          policyHolderName {
            type string,
            nullable true
          },
          policyHolderNo {
            type string,
            nullable true
          },
          class {
            type string,
            nullable true
          },
          invoiceNumber {
            type string,
            nullable true
          },
          invoiceDate {
            type string,
            format date-time,
            nullable true
          },
          treatmentCountryCode {
            type string,
            nullable true
          },
          destinationCode {
            type integer,
            format int32,
            nullable true
          },
          providerCode {
            type string,
            nullable true
          },
          claimedAmount {
            type number,
            format double,
            nullable true
          },
          claimedAmountSAR {
            type number,
            format double,
            nullable true
          },
          totalNetAmount {
            type number,
            format double,
            nullable true
          },
          totalDiscount {
            type number,
            format double,
            nullable true
          },
          totalDeductible {
            type number,
            format double,
            nullable true
          },
          benType {
            type string,
            nullable true
          },
          benHead {
            type string,
            nullable true
          },
          provider_dhsCode {
            type integer,
            format int32,
            nullable true
          },
          dhsm {
            type boolean,
            nullable true
          },
          batchNo {
            type string,
            nullable true
          },
          batchDate {
            type string,
            format date-time,
            nullable true
          },
          batchStartDate {
            type string,
            format date-time,
            nullable true
          },
          batchEndDate {
            type string,
            format date-time,
            nullable true
          },
          patientID {
            type string,
            nullable true
          },
          dob {
            type string,
            format date-time,
            nullable true
          },
          age {
            type integer,
            format int32,
            nullable true
          },
          nationality {
            type string,
            nullable true
          },
          durationOFIllness {
            type integer,
            format int32,
            nullable true
          },
          chiefComplaint {
            type string,
            nullable true
          },
          maritalStatus {
            type string,
            nullable true
          },
          patientGender {
            type string,
            nullable true
          },
          subscriberRelationship {
            type string,
            nullable true
          },
          visitType {
            type string,
            nullable true
          },
          plan_Type {
            type string,
            nullable true
          },
          msvRef {
            type string,
            nullable true
          },
          userID {
            type integer,
            format int32,
            nullable true
          },
          issueNo {
            type string,
            nullable true
          },
          userName {
            type string,
            nullable true
          },
          payerRef {
            type string,
            nullable true
          },
          dhsc {
            type string,
            nullable true
          },
          errorMessage {
            type string,
            nullable true
          },
          mobileNo {
            type string,
            nullable true
          },
          pmaUserName {
            type string,
            nullable true
          },
          admissionDate {
            type string,
            format date-time,
            nullable true
          },
          dischargeDate {
            type string,
            format date-time,
            nullable true
          },
          admissionNo {
            type string,
            nullable true
          },
          admissionSpecialty {
            type string,
            nullable true
          },
          admissionType {
            type string,
            nullable true
          },
          roomNumber {
            type string,
            nullable true
          },
          bedNumber {
            type string,
            nullable true
          },
          dischargeSpecialty {
            type string,
            nullable true
          },
          lengthOfStay {
            type string,
            nullable true
          },
          admissionWeight {
            type string,
            nullable true
          },
          dischargeMode {
            type string,
            nullable true
          },
          iDno {
            type string,
            nullable true
          },
          emergencyArrivalCode {
            type string,
            nullable true
          },
          emergencyStartDate {
            type string,
            format date-time,
            nullable true
          },
          emergencyWaitingTime {
            type string,
            nullable true
          },
          triageDate {
            type string,
            format date-time,
            nullable true
          },
          admissionTypeID {
            type string,
            nullable true
          },
          dischargeDepositionsTypeID {
            type string,
            nullable true
          },
          enconuterTypeID {
            type string,
            nullable true
          },
          careTypeID {
            type string,
            nullable true
          },
          triageCategoryTypeID {
            type string,
            nullable true
          },
          emergencyDepositionTypeID {
            type string,
            nullable true
          },
          significantSigns {
            type string,
            nullable true
          },
          mainSymptoms {
            type string,
            nullable true
          },
          dischargeSummary {
            type string,
            nullable true
          },
          otherConditions {
            type string,
            nullable true
          },
          encounterStatus {
            type string,
            nullable true
          },
          encounterNo {
            type string,
            nullable true
          },
          priority {
            type string,
            nullable true
          },
          patientIdType {
            type string,
            nullable true
          },
          actIncidentCode {
            type string,
            nullable true
          },
          episodeNumber {
            type string,
            nullable true
          },
          cchi {
            type string,
            nullable true
          },
          branchID {
            type string,
            nullable true
          },
          newBorn {
            type boolean,
            nullable true
          },
          oxygenSaturation {
            type string,
            nullable true
          },
          birthWeight {
            type string,
            nullable true
          },
          isFetched {
            type boolean,
            nullable true
          },
          isSync {
            type boolean,
            nullable true
          },
          fetchDate {
            type string,
            format date-time,
            nullable true
          },
          relatedClaim {
            type integer,
            format int64,
            nullable true
          },
          bCR_Id {
            type integer,
            format int64,
            nullable true
          },
          patientOccupation {
            type string,
            nullable true
          },
          causeOfDeath {
            type string,
            nullable true
          },
          treatmentPlan {
            type string,
            nullable true
          },
          patientHistory {
            type string,
            nullable true
          },
          physicalExamination {
            type string,
            nullable true
          },
          historyOfPresentIllness {
            type string,
            nullable true
          },
          patientReligion {
            type string,
            nullable true
          },
          morphologyCode {
            type string,
            nullable true
          },
          investigationResult {
            type string,
            nullable true
          },
          snomedCTCode {
            type string,
            nullable true
          },
          ventilationhours {
            type integer,
            format int32,
            nullable true
          },
          fK_ClaimType_ID {
            $ref #componentsschemasCommonType
          },
          fK_ClaimSubtype_ID {
            $ref #componentsschemasCommonType
          },
          fK_GenderId {
            $ref #componentsschemasCommonType
          },
          fK_PatientIDType_ID {
            $ref #componentsschemasCommonType
          },
          fK_MaritalStatus_ID {
            $ref #componentsschemasCommonType
          },
          fK_Dept_ID {
            $ref #componentsschemasCommonType
          },
          fK_Nationality_ID {
            $ref #componentsschemasCommonType
          },
          fK_BirthCountry_ID {
            $ref #componentsschemasCommonType
          },
          fK_BirthCity_ID {
            $ref #componentsschemasCommonType
          },
          fK_Religion_ID {
            $ref #componentsschemasCommonType
          },
          fK_AdmissionSpecialty_ID {
            $ref #componentsschemasCommonType
          },
          fK_DischargeSpeciality_ID {
            $ref #componentsschemasCommonType
          },
          fK_DischargeDisposition_ID {
            $ref #componentsschemasCommonType
          },
          fK_EncounterAdmitSource_ID {
            $ref #componentsschemasCommonType
          },
          fK_EncounterClass_ID {
            $ref #componentsschemasCommonType
          },
          fK_EncounterStatus_ID {
            $ref #componentsschemasCommonType
          },
          fK_ReAdmission_ID {
            $ref #componentsschemasCommonType
          },
          fK_EmergencyArrivalCode_ID {
            $ref #componentsschemasCommonType
          },
          fK_ServiceEventType_ID {
            $ref #componentsschemasCommonType
          },
          fK_IntendedLengthOfStay_ID {
            $ref #componentsschemasCommonType
          },
          fK_TriageCategory_ID {
            $ref #componentsschemasCommonType
          },
          fK_DispositionCode_ID {
            $ref #componentsschemasCommonType
          },
          fK_InvestigationResult_ID {
            type integer,
            format int32,
            nullable true
          },
          fK_VisitType_ID {
            $ref #componentsschemasCommonType
          },
          fK_PatientOccupation_Id {
            $ref #componentsschemasCommonType
          },
          fK_SubscriberRelationship_ID {
            $ref #componentsschemasCommonType
          },
          hisProIdClaim {
            type integer,
            format int32,
            nullable true
          }
        },
        additionalProperties false
      },
      DHSDiagnosisDetailDto {
        type object,
        properties {
          diagnosisID {
            type integer,
            format int32
          },
          diagnosisCode {
            type string,
            nullable true
          },
          diagnosisDesc {
            type string,
            nullable true
          },
          proIdClaim {
            type integer,
            format int32
          },
          errorMessage {
            type string,
            nullable true
          },
          dhsi {
            type integer,
            format int32,
            nullable true
          },
          diagnosisTypeID {
            type string,
            nullable true
          },
          diagnosisDate {
            type string,
            format date,
            nullable true
          },
          onsetConditionTypeID {
            type string,
            nullable true
          },
          illnessTypeIndicator {
            type string,
            nullable true
          },
          diagnosisType {
            type string,
            nullable true
          },
          fK_DiagnosisType_ID {
            type integer,
            format int32,
            nullable true
          },
          fK_ConditionOnset_ID {
            type integer,
            format int32,
            nullable true
          },
          diagnosis_Cloud_Desc {
            type string,
            nullable true
          },
          ageLow {
            type string,
            nullable true
          },
          ageHigh {
            type string,
            nullable true
          },
          isUnacceptPrimaryDiagnosis {
            type boolean,
            nullable true
          },
          genderID {
            type integer,
            format int32,
            nullable true
          },
          diagnosis_Order {
            type integer,
            format int32,
            nullable true
          },
          diagnosis_Date {
            type string,
            format date-time,
            nullable true
          },
          fK_DiagnosisOnAdmission_ID {
            $ref #componentsschemasCommonType
          },
          isMorphologyRequired {
            type boolean,
            nullable true
          },
          isRTAType {
            type boolean,
            nullable true
          },
          isWPAType {
            type boolean,
            nullable true
          }
        },
        additionalProperties false
      },
      DHSLabDetailDto {
        type object,
        properties {
          labID {
            type integer,
            format int32
          },
          labProfile {
            type string,
            nullable true
          },
          labTestName {
            type string,
            nullable true
          },
          labResult {
            type string,
            nullable true
          },
          labUnits {
            type string,
            nullable true
          },
          labLow {
            type string,
            nullable true
          },
          labHigh {
            type string,
            nullable true
          },
          labSection {
            type string,
            nullable true
          },
          visitDate {
            type string,
            format date-time,
            nullable true
          },
          proIdClaim {
            type integer,
            format int32
          },
          dhsi {
            type integer,
            format int32,
            nullable true
          },
          resultDate {
            type string,
            format date-time,
            nullable true
          },
          serviceCode {
            type string,
            nullable true
          },
          loincCode {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      DHSOpticalVitalSignDto {
        type object,
        properties {
          serialNo {
            type integer,
            format int32
          },
          rightEyeSphere_Distance {
            type string,
            nullable true
          },
          rightEyeSphere_Near {
            type string,
            nullable true
          },
          rightEyeCylinder_Distance {
            type string,
            nullable true
          },
          rightEyeCylinder_Near {
            type string,
            nullable true
          },
          rightEyeAxis_Distance {
            type string,
            nullable true
          },
          rightEyeAxis_Near {
            type string,
            nullable true
          },
          rightEyePrism_Distance {
            type string,
            nullable true
          },
          rightEyePrism_Near {
            type string,
            nullable true
          },
          rightEyePD_Distance {
            type string,
            nullable true
          },
          rightEyePD_Near {
            type string,
            nullable true
          },
          rightEyeBifocal {
            type string,
            nullable true
          },
          leftEyeSphere_Distance {
            type string,
            nullable true
          },
          leftEyeSphere_Near {
            type string,
            nullable true
          },
          leftEyeCylinder_Distance {
            type string,
            nullable true
          },
          leftEyeCylinder_Near {
            type string,
            nullable true
          },
          leftEyeAxis_Distance {
            type string,
            nullable true
          },
          leftEyeAxis_Near {
            type string,
            nullable true
          },
          leftEyePrism_Distance {
            type string,
            nullable true
          },
          leftEyePrism_Near {
            type string,
            nullable true
          },
          leftEyePD_Distance {
            type string,
            nullable true
          },
          leftEyePD_Near {
            type string,
            nullable true
          },
          leftEyeBifocal {
            type string,
            nullable true
          },
          vertex {
            type string,
            nullable true
          },
          proIdClaim {
            type integer,
            format int32,
            nullable true
          },
          serviceCode {
            type string,
            nullable true
          },
          rightEyePower_Distance {
            type number,
            format double,
            nullable true
          },
          rightEyePower_Near {
            type number,
            format double,
            nullable true
          },
          leftEyePower_Distance {
            type number,
            format double,
            nullable true
          },
          leftEyePower_Near {
            type number,
            format double,
            nullable true
          },
          prismDirection {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      DHSRadiologyDetailDto {
        type object,
        properties {
          radiologyID {
            type integer,
            format int32
          },
          serviceCode {
            type string,
            nullable true
          },
          serviceDescription {
            type string,
            nullable true
          },
          clinicalData {
            type string,
            nullable true
          },
          radiologyResult {
            type string,
            nullable true
          },
          visitDate {
            type string,
            format date-time,
            nullable true
          },
          proIdClaim {
            type integer,
            format int32,
            nullable true
          },
          dhsi {
            type integer,
            format int32,
            nullable true
          },
          resultDate {
            type string,
            format date-time,
            nullable true
          }
        },
        additionalProperties false
      },
      DHSServiceDetailDto {
        type object,
        properties {
          serviceID {
            type integer,
            format int32
          },
          itemRefernce {
            type string,
            nullable true
          },
          treatmentFromDate {
            type string,
            format date-time,
            nullable true
          },
          treatmentToDate {
            type string,
            format date-time,
            nullable true
          },
          numberOfIncidents {
            type number,
            format double,
            nullable true
          },
          lineClaimedAmount {
            type number,
            format double,
            nullable true
          },
          coInsurance {
            type number,
            format double,
            nullable true
          },
          coPay {
            type number,
            format double,
            nullable true
          },
          lineItemDiscount {
            type number,
            format double,
            nullable true
          },
          netAmount {
            type number,
            format double,
            nullable true
          },
          serviceCode {
            type string,
            nullable true
          },
          serviceDescription {
            type string,
            nullable true
          },
          mediCode {
            type string,
            nullable true
          },
          toothNo {
            type string,
            nullable true
          },
          submissionReasonCode {
            type integer,
            format int32,
            nullable true
          },
          treatmentTypeIndicator {
            type integer,
            format int32,
            nullable true
          },
          serviceGategory {
            type string,
            nullable true
          },
          proIdClaim {
            type string,
            nullable true
          },
          errorMessage {
            type string,
            nullable true
          },
          vatIndicator {
            type boolean,
            nullable true
          },
          vatPercentage {
            type number,
            format double,
            nullable true
          },
          patientVatAmount {
            type number,
            format double,
            nullable true
          },
          netVatAmount {
            type number,
            format double,
            nullable true
          },
          cashAmount {
            type number,
            format double,
            nullable true
          },
          pbmDuration {
            type integer,
            format int32,
            nullable true
          },
          pbmTimes {
            type integer,
            format int32,
            nullable true
          },
          pbmUnit {
            type integer,
            format int32,
            nullable true
          },
          pbmPer {
            type string,
            nullable true
          },
          pbmUnitType {
            type string,
            nullable true
          },
          serviceEventType {
            type string,
            nullable true
          },
          ardrg {
            type string,
            nullable true
          },
          serviceType {
            type string,
            nullable true
          },
          preAuthId {
            type string,
            nullable true
          },
          doctorCode {
            type string,
            nullable true
          },
          unitPrice {
            type number,
            format double,
            nullable true
          },
          discPercentage {
            type number,
            format double,
            nullable true
          },
          workOrder {
            type string,
            format date-time,
            nullable true
          },
          pharmacistSelectionReason {
            type string,
            nullable true
          },
          pharmacistSubstitute {
            type string,
            nullable true
          },
          scientificCode {
            type string,
            nullable true
          },
          diagnosisCode {
            type string,
            nullable true
          },
          scientificCodeAbsenceReason {
            type string,
            nullable true
          },
          maternity {
            type boolean,
            nullable true
          },
          service_NphiesServiceCode {
            type string,
            nullable true
          },
          service_SystemURL {
            type string,
            nullable true
          },
          service_NphiesServiceType {
            type string,
            nullable true
          },
          nphiesServiceTypeID {
            type integer,
            format int32,
            nullable true
          },
          service_LongDescription {
            type string,
            nullable true
          },
          service_ShortDescription {
            type string,
            nullable true
          },
          isUnlistedCode {
            type boolean,
            nullable true
          },
          daysSupply {
            type integer,
            format int32,
            nullable true
          },
          priceListPrice {
            type number,
            format double,
            nullable true
          },
          priceListDiscount {
            type number,
            format double,
            nullable true
          },
          fK_ServiceType_ID {
            $ref #componentsschemasCommonType
          },
          fK_PharmacistSubstitute_ID {
            $ref #componentsschemasCommonType
          },
          fK_PharmacistSelectionReason_ID {
            $ref #componentsschemasCommonType
          },
          hasResult {
            type boolean,
            nullable true
          },
          factor {
            type number,
            format double,
            nullable true
          },
          tax {
            type number,
            format double,
            nullable true
          }
        },
        additionalProperties false
      },
      DashboardDto {
        type object,
        properties {
          providerDhsCode {
            type string,
            nullable true
          },
          totalBatches {
            type integer,
            format int32
          },
          batchStatusChart {
            type array,
            items {
              $ref #componentsschemasBatchStatusChartDto
            },
            nullable true
          },
          batchPayerChart {
            type array,
            items {
              $ref #componentsschemasBatchPayerChartDto
            },
            nullable true
          }
        },
        additionalProperties false
      },
      DashboardDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasDashboardDto
          }
        },
        additionalProperties false
      },
      DeleteBatchRequestDto {
        type object,
        properties {
          batchId {
            type integer,
            format int64
          }
        },
        additionalProperties false
      },
      DomainMappingDto {
        type object,
        properties {
          domTable_ID {
            type integer,
            format int32,
            nullable true
          },
          providerDomainCode {
            type string,
            nullable true
          },
          providerDomainValue {
            type string,
            nullable true
          },
          dhsDomainValue {
            type integer,
            format int32,
            nullable true
          },
          isDefault {
            type boolean,
            nullable true
          },
          codeValue {
            type string,
            nullable true
          },
          displayValue {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      DomainMappingDtoListBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            type array,
            items {
              $ref #componentsschemasDomainMappingDto
            },
            nullable true
          }
        },
        additionalProperties false
      },
      GeneralConfigurationDto {
        type object,
        properties {
          leaseDurationSeconds {
            type integer,
            format int32,
            nullable true
          },
          streamAIntervalSeconds {
            type integer,
            format int32,
            nullable true
          },
          resumePollIntervalSeconds {
            type integer,
            format int32,
            nullable true
          },
          apiTimeout {
            type integer,
            format int32,
            nullable true
          },
          configCacheTtlMinutes {
            type integer,
            format int32,
            nullable true
          },
          fetchIntervalMinutes {
            type integer,
            format int32,
            nullable true
          },
          manualRetryCooldownMinutes {
            type integer,
            format int32,
            nullable true
          }
        },
        additionalProperties false
      },
      InsertMissMappingDomainRequestDto {
        type object,
        properties {
          providerDhsCode {
            type string,
            nullable true
          },
          mismappedItems {
            type array,
            items {
              $ref #componentsschemasMismappedDomainItemDto
            },
            nullable true
          }
        },
        additionalProperties false
      },
      InsertMissMappingDomainResultDto {
        type object,
        properties {
          totalItems {
            type integer,
            format int32
          },
          insertedCount {
            type integer,
            format int32
          },
          skippedCount {
            type integer,
            format int32
          },
          skippedItems {
            type array,
            items {
              type string
            },
            nullable true
          }
        },
        additionalProperties false
      },
      InsertMissMappingDomainResultDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasInsertMissMappingDomainResultDto
          }
        },
        additionalProperties false
      },
      LoginRequestDto {
        required [
          email,
          password
        ],
        type object,
        properties {
          email {
            minLength 1,
            type string,
            format email
          },
          groupID {
            type string,
            nullable true
          },
          password {
            minLength 6,
            type string
          }
        },
        additionalProperties false
      },
      LoginResponseDto {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            nullable true
          }
        },
        additionalProperties false
      },
      LoginResponseDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasLoginResponseDto
          }
        },
        additionalProperties false
      },
      MismappedDomainItemDto {
        type object,
        properties {
          providerCodeValue {
            type string,
            nullable true
          },
          providerNameValue {
            type string,
            nullable true
          },
          domainTableId {
            type integer,
            format int32
          }
        },
        additionalProperties false
      },
      MissingDomainMappingDto {
        type object,
        properties {
          providerCodeValue {
            type string,
            nullable true
          },
          providerNameValue {
            type string,
            nullable true
          },
          domainTableId {
            type integer,
            format int32
          },
          domainTableName {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      MissingDomainMappingDtoListBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            type array,
            items {
              $ref #componentsschemasMissingDomainMappingDto
            },
            nullable true
          }
        },
        additionalProperties false
      },
      ObjectBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            nullable true
          }
        },
        additionalProperties false
      },
      ProviderConfigrationDto {
        type object,
        properties {
          providerInfo {
            $ref #componentsschemasProviderInfoDto
          },
          providerPayers {
            type array,
            items {
              $ref #componentsschemasProviderPayerDto
            },
            nullable true
          },
          domainMappings {
            type array,
            items {
              $ref #componentsschemasDomainMappingDto
            },
            nullable true
          },
          missingDomainMappings {
            type array,
            items {
              $ref #componentsschemasMissingDomainMappingDto
            },
            nullable true
          },
          generalConfiguration {
            $ref #componentsschemasGeneralConfigurationDto
          }
        },
        additionalProperties false
      },
      ProviderConfigrationDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasProviderConfigrationDto
          }
        },
        additionalProperties false
      },
      ProviderDomainMappingsDto {
        type object,
        properties {
          domainMappings {
            type array,
            items {
              $ref #componentsschemasDomainMappingDto
            },
            nullable true
          },
          missingDomainMappings {
            type array,
            items {
              $ref #componentsschemasMissingDomainMappingDto
            },
            nullable true
          }
        },
        additionalProperties false
      },
      ProviderDomainMappingsDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasProviderDomainMappingsDto
          }
        },
        additionalProperties false
      },
      ProviderInfoDto {
        type object,
        properties {
          provider_ID {
            type integer,
            format int32
          },
          providerCCHI {
            type string,
            nullable true
          },
          providerNameAr {
            type string,
            nullable true
          },
          providerNameEn {
            type string,
            nullable true
          },
          connectionString {
            type string,
            nullable true
          },
          providerDHSCode {
            type string,
            nullable true
          },
          providerToken {
            type string,
            nullable true
          },
          providerVendor {
            type string,
            nullable true
          },
          integrationType {
            type string,
            nullable true
          },
          dataBaseType {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      ProviderInfoDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasProviderInfoDto
          }
        },
        additionalProperties false
      },
      ProviderPayerDto {
        type object,
        properties {
          payerId {
            type integer,
            format int32,
            nullable true
          },
          companyCode {
            type string,
            nullable true
          },
          payerNameEn {
            type string,
            nullable true
          },
          parentPayerNameEn {
            type string,
            nullable true
          }
        },
        additionalProperties false
      },
      ProviderPayerDtoListBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            type array,
            items {
              $ref #componentsschemasProviderPayerDto
            },
            nullable true
          }
        },
        additionalProperties false
      },
      SendClaimResultDto {
        type object,
        properties {
          successClaimsProidClaim {
            type array,
            items {
              type integer,
              format int32
            },
            nullable true
          },
          totalClaims {
            type integer,
            format int32
          },
          successCount {
            type integer,
            format int32
          },
          failureCount {
            type integer,
            format int32
          }
        },
        additionalProperties false
      },
      SendClaimResultDtoBaseResponse {
        type object,
        properties {
          succeeded {
            type boolean
          },
          statusCode {
            type integer,
            format int32
          },
          message {
            type string,
            nullable true
          },
          errors {
            type array,
            items {
              type string
            },
            nullable true
          },
          data {
            $ref #componentsschemasSendClaimResultDto
          }
        },
        additionalProperties false
      }
    }
  }
}
```
