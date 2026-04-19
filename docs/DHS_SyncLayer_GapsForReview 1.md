# Sync Layer — Production Readiness Gaps

Additions to the Sync Layer plan to close production-readiness gaps before and during rollout. Each item contains: the risk, a real-world scenario, the proposed solution, and success criteria.

---

## 🔴 Blockers (before Phase 1 flip)

### G1 — Rollback Plan

**Risk.** The plan defines forward migration only (parallel run → flag flip → new pipeline). There is no defined path to roll back to the legacy `USP_FetchClaims` path after the flag has been flipped and the new pipeline has written claims in the new schema.

**Scenario.** Phase 2 flips the `Sync:UsePluginPipeline:MARSHAL` flag. Over four weeks the new pipeline stages ~12,000 claims with `SourceCursor` populated and `ProIdClaim` possibly null. A defect is then discovered in `RecomputeHeaderTotals` (e.g. it miscounts when `DeleteServicesByCompanyCode` fires after the recompute). Turning the flag off is insufficient: the legacy path reads `Claims.ProIdClaim` as a non-null int and cannot process rows whose identity lives in `SourceCursor`. Disabling the flag without a migration either leaves staged claims in limbo or causes duplicates when the legacy path re-fetches the same date range.

**Proposed solution.**

1. **Dual-read capability on local repositories.** Claim repositories accept both `ProIdClaim` and `SourceCursor` as identity, with a single helper `GetResumeKey()` returning whichever is populated. Both identity columns remain nullable in SQLite.
2. **Backward migration script.** A tool that, for undispatched claims of a given vendor, rewrites `SourceCursor → ProIdClaim` when the cursor is integer-parseable, and deletes + re-fetches claims whose cursor is not integer-parseable (BilalSoft-style composite cursors). Deletion is scoped to undispatched claims only; dispatched history is preserved.
3. **Extended parallel-run window.** Legacy path remains in hot-standby (code path enabled, queries not issued) for two weeks after the flip, then cold-standby for one additional month, before final removal.
4. **`Sync:UseLegacyAdapter:<VENDOR>` feature flag.** Orthogonal to `UsePluginPipeline`. Allows re-enabling the legacy adapter without a release.
5. **Rollback runbook.** Documented procedure with explicit steps: stop agent → run migration tool → flip flags → verify counts → restart → validate.

**Success criteria.**
- A vendor can be rolled back to legacy within 30 minutes with zero data loss on undispatched claims.
- The rollback procedure is tested on a staging environment before each vendor flip.
- Every feature flag flip is paired with a tested reverse procedure.

---

### G2 — SQLite Disaster Recovery

**Risk.** The agent stores `Claims`, resume cursors, `VendorDescriptor`, `DescriptorAuditLog`, `ValidationIssue`, and encrypted connection strings in a single local SQLite file. This is a single point of failure. Loss or corruption causes inconsistent state with the cloud API (missed claims, duplicate submissions), loss of audit history, and extended downtime.

**Scenarios.**

- **Drive failure.** The agent machine's disk fails overnight mid-batch. After replacement, the agent starts from scratch with no cursors; claims already submitted to the API get re-fetched and re-submitted.
- **SQLite corruption.** A power loss during a WAL commit leaves the database file in a malformed state. Startup fails with `database disk image is malformed`.
- **Ransomware or accidental deletion.** The agent's data folder is encrypted or deleted.

**Proposed solution.**

1. **Enable WAL mode with tuned PRAGMAs at startup.**

   ```csharp
   PRAGMA journal_mode = WAL;
   PRAGMA synchronous = NORMAL;
   PRAGMA busy_timeout = 5000;
   ```
   WAL reduces corruption risk on crash and improves concurrent read/write throughput. A periodic `PRAGMA wal_checkpoint(TRUNCATE)` runs on a timer (e.g. hourly) to bound WAL file size.

2. **Integrity check at startup.** The agent runs `PRAGMA integrity_check` before accepting work. If the result is not `ok`, the agent refuses to start, raises a critical alert, and writes a diagnostic dump. No silent continuation on a corrupted database.

3. **Daily encrypted backups to Azure Blob Storage.** A `SqliteBackupService` (hosted service) runs once per day, uses SQLite's online backup API (`SqliteConnection.BackupDatabase`) to produce a consistent snapshot without stopping the agent, encrypts it with the existing DPAPI / column encryptor, and uploads it to the configured `blobStorageConnectionString` under a path like `agent-backups/{providerCode}/{yyyyMMdd}/agent.db`. Retention: 30 days rolling, older blobs deleted.

4. **API-side idempotency keys.** The cloud API adds a unique constraint on `(ProviderCode, SourceCursor)` for claims and rejects duplicates at ingestion. This makes re-fetch on recovery safe — duplicates at the boundary become no-ops rather than data pollution. This change is independent of the agent release and can ship in Phase 1 from the API side.

5. **Documented restore procedure.** A runbook covers: stop agent → download latest intact backup → run integrity check → replace database file → start agent → verify cursors resume correctly → confirm no duplicate submissions (protected by idempotency key).

**Success criteria.**
- **RTO** (time to restore service): ≤ 30 minutes from detected corruption to running agent.
- **RPO** (acceptable data loss window): ≤ 24 hours (daily backup cadence).
- Integrity check detects 100% of corrupted databases before the agent accepts new work.
- Duplicate claim submissions during recovery are rejected at the API layer.

---

### G3 — Load Testing Baseline

**Risk.** The plan specifies "batch size 50–300" without supporting measurements. Peak volume per vendor, per-claim memory footprint, acceptable source-DB impact, and cloud-API throughput ceilings are all unquantified. First production site could reveal memory exhaustion, source-DB contention with the vendor's HIS users, or API rate-limit thrashing.

**Scenarios.**

- **Memory exhaustion.** A clinic produces ~3,000 claims/day. Each `ClaimBundle` averages ~50 KB in memory with header + seven child arrays + Dapper materialization overhead. A naive implementation that holds a full page's bundles plus their DTOs before yielding can exceed 500 MB heap under GC pressure, leading to `OutOfMemoryException`.
- **Source-DB impact.** An unindexed `SELECT ... WHERE batchStartDate BETWEEN ...` on a live HIS can take 60+ seconds and hold shared locks, slowing front-office staff. Without measuring, this is discovered only when the vendor complains.
- **API throttling.** The agent submits claims faster than the API's rate limit. Without client-side throttling, the agent either floods the API and gets 429s, or enters a retry storm.

**Proposed solution.**

1. **Baseline measurement per vendor** (before Phase 1 gate):

    | Metric | Required |
    |---|---|
    | Claims/day average | yes |
    | Claims/day peak (90th percentile) | yes |
    | Services per claim, attachments per claim | yes |
    | Source DB CPU % during legacy SP run | yes |
    | Source DB concurrent-user count during business hours | yes |
    | Longest query time observed on legacy path | yes |

2. **Load test suite** under `tests/DHSIntegrationAgent.Sync.LoadTests/` with:
   - `Pipeline_Handles_10k_Claims_Within_Memory_Budget` — asserts heap stays under 500 MB while streaming 10,000 synthetic claims.
   - `Pipeline_Throughput_MeetsThreshold` — asserts ≥ 50 claims/sec sustained throughput.
   - `ResumeCursor_Correct_After_Kill` — kills the process mid-batch; restart yields identical final result.

3. **Descriptor-level performance limits.**

   ```json
   "performance": {
     "maxBatchSize": 300,
     "maxConcurrentQueries": 2,
     "queryTimeoutSeconds": 60,
     "memoryBudgetMB": 500,
     "throttleAfterConsecutiveFailures": 5
   }
   ```
   Enforced by the pipeline using `SemaphoreSlim` for concurrency, a per-command timeout on Dapper calls, and a memory-pressure gate that delays next-page fetch when heap exceeds budget.

4. **Client-side rate limiting on API submission** using `System.Threading.RateLimiting.TokenBucketRateLimiter` configured from the cloud-config response. Prevents thundering-herd on retry.

**Success criteria.**
- Load test passes at 10× measured peak volume per vendor before the Phase 1 flag flip.
- Memory stays under 500 MB for the largest realistic batch.
- Source DB extra CPU impact observed in staging < 5%.
- Zero API 429 responses during sustained production load.

---

## 🟡 Important (Phase 5 — Hardening)

### G4 — Schema Drift Monitoring

**Risk.** `DescriptorValidator` runs at startup and on descriptor edits. Once the agent is running, vendor-side schema changes (renamed columns, added/dropped columns, changed data types) go undetected until the first affected batch fails.

**Scenario.** A vendor DBA renames `encountertypeid` to `encounter_type_id` as part of a naming-convention cleanup. The next batch fails at query time with `ORA-00904: invalid identifier`. Root-cause investigation takes hours; claims back up in a failure state.

**Proposed solution.**

1. **Periodic schema re-validation** — a `SchemaDriftDetector` background service runs the identifier whitelist and column-existence checks every 6 hours against each active descriptor's source DB. On mismatch, raises a high-severity alert and marks the descriptor's `LastValidatedUtc` + `LastValidationResult` fields.

2. **Schema snapshots and diffing.** After successful validation, persist a lightweight snapshot `(tableName, columnName, dataType, isNullable, maxLength)` per vendor. Next check compares current metadata to the snapshot and classifies differences:

   | Change type | Severity |
   |---|---|
   | Added column | Info |
   | Nullable column becomes NOT NULL | Warning |
   | Removed column used by descriptor | Critical |
   | Data type changed on used column | Critical |
   | Max length reduced below descriptor truncation target | Critical |

3. **Optional DDL-trigger signal on vendor side** (when DBA access permits): a trigger in the vendor schema writes to a `dhs_schema_changes` table on any DDL affecting the integrated tables. The agent polls this table and reacts within minutes instead of hours.

**Success criteria.**
- Mean Time To Detect schema change: < 6 hours without DDL triggers, < 15 minutes with.
- Zero false positives over a one-month observation window.
- Every drift alert carries the specific column and the classification severity.

---

### G5 — Retry and Dead-Letter Logic

**Risk.** The plan mentions per-record isolation via `ValidationIssue` but does not specify retry counts, backoff, circuit breaking, or handling of batches that fail repeatedly. Without these, transient errors become permanent losses, the source DB is hammered during outages, and operators have no organized queue of items needing attention.

**Scenarios.**

- **Transient network blip.** A 10-second connectivity drop to Oracle kills the current query. Without retry, the batch fails and a ValidationIssue is logged; the claim is lost until the next scheduled fetch, which may miss it entirely if the date window has advanced.
- **Scheduled maintenance.** The source HIS goes down nightly for two hours. The agent generates 120 log entries/minute of failed attempts with no backoff.
- **Poison batch.** A single malformed source row breaks the transform for its entire batch. Every retry fails identically. Without a quarantine mechanism, the batch loops forever or is silently skipped.

**Proposed solution.**

1. **Descriptor-level resilience configuration.**

   ```json
   "resilience": {
     "retry": {
       "maxAttempts": 3,
       "backoffSeconds": [5, 30, 300],
       "retryableExceptions": [
         "OracleException:ORA-12170",
         "OracleException:ORA-03135",
         "SqlException:-2",
         "HttpRequestException"
       ]
     },
     "circuitBreaker": {
       "failureRateThreshold": 0.5,
       "samplingWindowSeconds": 60,
       "minimumThroughput": 5,
       "breakDurationSeconds": 300
     },
     "deadLetter": {
       "quarantineAfterAttempts": 3
     }
   }
   ```

2. **Polly-based resilience pipeline** wrapping `IClaimSource`. Retry policy applies exponential backoff with jitter. Circuit breaker opens on sustained failure, preventing retry storms against a down source.

3. **Dead-letter queue.** A `FailedBatches` SQLite table tracks batches that exceeded retry budget: batch key, attempt count, first + last failure timestamps, last error, stack trace, status (`Pending`, `Quarantined`, `Resolved`), resolver identity. Operator-accessible UI lists quarantined batches with `Replay` and `Skip` actions.

4. **Classification of exceptions.** `OracleException` / `SqlException` codes partitioned into retryable (network, deadlock, timeout) vs non-retryable (invalid identifier, permission denied, constraint violation). Non-retryable exceptions skip retry and go directly to dead-letter.

**Success criteria.**
- Transient errors convert to permanent failures in < 1% of cases.
- Circuit breaker prevents retry storms: during a simulated 2-hour source outage, retry attempts count stays below 20.
- Every quarantined batch appears in the operator dashboard within 60 seconds.
- Replay action on a quarantined batch succeeds when the root cause is resolved.

---

### G6 — Monitoring, Logging, and Alerting (concrete spec)

**Risk.** Phase 5 lists "provider-extraction observability" as a line item without defining metrics, logs, health checks, or alert rules. Without a concrete spec, the agent ships as a black box: problems are reported by end-users before they are detected internally, and root-cause investigation requires ad-hoc log grepping.

**Proposed solution.**

1. **Metrics (OpenTelemetry `Meter`).**

   | Metric | Type | Tags |
   |---|---|---|
   | `dhs_sync_claims_processed_total` | Counter | `provider`, `status` |
   | `dhs_sync_claims_failed_total` | Counter | `provider`, `reason` |
   | `dhs_sync_batch_duration_seconds` | Histogram | `provider` |
   | `dhs_sync_query_duration_seconds` | Histogram | `provider`, `entity` |
   | `dhs_sync_pending_batches` | UpDownCounter | `provider` |
   | `dhs_sync_memory_bytes` | ObservableGauge | — |
   | `dhs_sync_parity_diff_percent` | ObservableGauge | `provider` |

   Exporter: configurable; defaults to Prometheus scrape endpoint for self-hosted and OTLP for cloud.

2. **Structured logging (Serilog, already in use) with correlation scope.** Every batch wraps its work in a `BeginScope` with `BatchId`, `Provider`, `TraceId` properties so all downstream logs inherit the context. Log levels: `Information` for batch lifecycle events, `Warning` for retryable failures, `Error` for non-retryable failures, `Critical` for corruption or configuration errors.

3. **Distributed tracing.** `ActivitySource` spans per batch and per page, with `provider`, `batch.key`, `entity`, and result-status tags. Enables end-to-end tracing from agent → API → downstream services.

4. **Health checks (ASP.NET Core `IHealthCheck`).**

   | Check | Verifies |
   |---|---|
   | `source_db` | Read-only ping query succeeds within 5s per active vendor |
   | `cloud_api` | `/api/Provider/Ping` returns 200 within 5s |
   | `local_db` | SQLite integrity check + WAL size within bounds |
   | `descriptors` | All active descriptors pass validation |
   | `disk_space` | Data folder has > 1 GB free |

   Exposed on `/health` with detailed per-check JSON and HTTP status reflecting worst case.

5. **Alert rules (exporter-agnostic definitions).**

   | Alert | Condition | Severity |
   |---|---|---|
   | `AgentDown` | No metrics received for > 15 min | Critical |
   | `BatchFailureRateHigh` | `claims_failed / claims_processed > 5%` over 1h | High |
   | `ParityDrift` | `parity_diff_percent > 1%` | High |
   | `QueueBacklog` | `pending_batches > 100` | Medium |
   | `SchemaValidationFailed` | Any descriptor validation fails | Critical |
   | `DiskSpaceLow` | Data folder < 1 GB free | High |
   | `DeadLetterGrowth` | Quarantined batches > 10 in 24h | Medium |

6. **In-app status dashboard.** A WPF page in the existing UI showing: agent status, uptime, per-vendor last-sync timestamp, 1-hour claim throughput and failure rate, active alerts, quarantined batch count. Refresh rate: 10 seconds.

**Success criteria.**
- **MTTD** (Mean Time To Detect) for agent failures: < 5 minutes.
- **MTTR** (Mean Time To Resolve) for common incidents: < 30 minutes with runbooks.
- ≥ 99% of failures detected internally before any external report.
- Dashboard loads in < 2 seconds and updates every 10 seconds.
- All alerts carry enough context to start investigation without opening the codebase.

---

## 📊 Summary

| # | Item | Priority | Target Phase | Est. Effort |
|---|---|---|---|---|
| G1 | Rollback plan | 🔴 Blocker | Before Phase 2 flip | 2 weeks |
| G2 | SQLite disaster recovery | 🔴 Blocker | Phase 1 (idempotency) + Phase 5 (backup) | 1 week |
| G3 | Load testing baseline | 🔴 Blocker | Phase 1 gate | 1 week |
| G4 | Schema drift monitoring | 🟡 Important | Phase 5 | 3 days |
| G5 | Retry and dead-letter | 🟡 Important | Phase 5 | 1 week |
| G6 | Monitoring and alerting | 🟡 Important | Phase 5 | 1 week |

---

## Items intentionally out of scope for now

- **Multi-tenancy inside a single vendor.** No current vendor multiplexes clinics under one schema. Will be reconsidered if a future vendor requires it.
- **Descriptor schema v2 migration.** `schemaVersion: 1` is sufficient for all currently known vendors; migration strategy will be designed when v2 requirements emerge.
- **Descriptor hot-reload.** Agent restart on descriptor change is acceptable given current edit frequency (weeks to months between edits).
- **Full GDPR / data-retention framework.** `Open Questions Q12` covers PHI in parity artifacts, which is the immediate concern. Broader data-retention design deferred.
- **Attachment size and corruption handling.** Existing `AttachmentDispatchService` is stable; enhancements deferred until an actual failure mode is observed.

---

## Required changes to existing plan documents if these items are adopted

1. **`DHS_SyncLayer_Plan.md` §4.13 (Phasing).**
   - Phase 1 exit criteria gain "load-test suite passes" (G3).
   - Phase 2 prerequisites gain "rollback procedure documented and staging-verified" (G1).
   - Phase 5 expanded with concrete subsections for G4, G5, G6 instead of single-line bullets.

2. **`DHS_SyncLayer_OpenQuestions.md`.**
   - New **Q16** — "Rollback strategy: migration script vs keep-both-paths vs re-fetch."
   - New **Q17** — "Load-test thresholds: who owns defining per-vendor peak volume and acceptable source-DB impact?"

3. **New file `DHS_SyncLayer_Operations.md`** covering:
   - Backup and restore runbook (G2).
   - Disaster recovery procedure (G2).
   - Rollback runbook (G1).
   - Monitoring dashboard specification (G6).
   - Alert-routing configuration (G6).
