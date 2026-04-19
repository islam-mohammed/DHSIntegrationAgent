# DHS Integration Agent — Sync Layer Plan

**Document 1 of 4** in the Sync Layer planning set:

- **This document** — overview, architecture, implementation plan.
- `DHS_SyncLayer_VendorDescriptors.md` — descriptor model, transform catalog, governance.
- `DHS_SyncLayer_VendorPlaybook.md` — per-vendor specs and migration checklists.
- `DHS_SyncLayer_OpenQuestions.md` — consolidated questions still awaiting confirmation.

This is the source of truth. Earlier drafts are deprecated.

---

## 0. TL;DR

**Four vendor sources** feed the same canonical destination schema (`[DHS-NPHIES]`) but live on **two different database engines**:

| Vendor | Source engine | Legacy access | New access |
| --- | --- | --- | --- |
| **Marshal** | **Oracle** | SQL Server linked server (`DHS..[NAFD]`) | Direct Oracle connection |
| **Excys** | **Oracle** (inferred; confirmation pending) | SQL Server linked server `DHS` via `OPENQUERY` (Oracle views `DHS.*_V`) | Direct Oracle connection |
| **AFI** | **SQL Server** | Same-server DB (`DHSA.dbo.*`) | Direct SQL Server connection |
| **BilalSoft** | **SQL Server** | SQL Server linked server (`BILAL.medical_net.*`) | Direct SQL Server connection |

The legacy `USP_FetchClaims` stored procedures run on the SQL Server destination and reach each vendor through different paths. The new agent connects **directly** to each vendor's source DB on the same local network — **no linked servers**. The SP bodies remain only as documentation of the extraction and transform logic to be ported into the new pipeline.

The existing `DHSIntegrationAgent` has a clean downstream pipeline (`ClaimBundle` → `FetchStageService` → local SQLite → API) but reads from **post-SP staging tables** populated by an on-site DBA. The goal is to replace those SPs with C# running inside the agent, with vendor differences isolated at the vendor level.

### Recommended architecture

Tiered descriptor-first pipeline with **SQL-level aliasing** as the primary mapping mechanism (confirmed by the team):

- One generic `IClaimExtractionPipeline` orchestrates `Count → EnumerateKeys → FetchBatch → Transform → Emit ClaimBundle`.
- Each vendor ships a vendor-specific SQL file that performs column aliasing inside the `SELECT` (e.g. `SELECT encountertypeid AS EncounterTypeID`). This collapses most name and type mismatches at the source layer and works identically for Oracle and SQL Server.
- Cross-row and post-load rules (recompute totals, clamp, null-if-equals, delete by company code) stay in descriptors because they span rows.

### Tiering

- **Tier 1** — JSON descriptor + generic templated SQL. Rare; only for vendors whose source columns already match the canonical model 1:1.
- **Tier 2** — JSON descriptor + vendor-owned `.sql` files with explicit aliases and dialect-specific casts. **Default tier for current vendors.**
- **Tier 3** — C# plugin. Reserved for logic SQL cannot express cleanly (see Open Questions Q1 on BilalSoft).

### Vendor tier assignments

- Marshal → Tier 2 (Oracle).
- Excys → Tier 2 (Oracle — inferred, confirmation pending).
- AFI → Tier 2 (SQL Server).
- BilalSoft → Tier 2 or Tier 3 (SQL Server, pending decision — see Open Questions Q1).

Descriptor model, transform catalog, and override governance live in `DHS_SyncLayer_VendorDescriptors.md`. Per-vendor concrete designs live in `DHS_SyncLayer_VendorPlaybook.md`.

---

## 1. Business Logic per Vendor

Full details, transformations, and exact SQL behaviour live in the Playbook. This section captures only the shape of variance and the tier classification.

### 1.1 Shared contract across all legacy SPs

Excys's stored procedure was delivered 2026-04-18 (`PLAN/VEND/exeys/`). The shape below is **confirmed** for all four vendors.

| Dimension | All four vendors |
| --- | --- |
| Signature | `USP_FetchClaims(@InsuranceCode, @_FromDate, @_ToDate)` (all `nvarchar(50)`) |
| Date format | Input is `dd/MM/yyyy`, converted internally to `yyyyMMdd` |
| First action | `exec sp_deletebatch @InsuranceCode, @_FromDate, @_ToDate` — wipes the previous batch for this key before reload |
| Destination | `[DHS-NPHIES].dbo.DHSClaim_Header` plus child tables |
| Child → header rebind | By `InvoiceNumber` (collation-aware) after header insert |
| Final step | Recompute header totals as `SUM(...)` over destination service rows |

The variance is entirely upstream of the destination insert. That is the seam the new sync layer targets.

### 1.2 Variance summary

All four vendor specs now delivered. Excys is very likely Oracle (engine confirmation pending DBA / cloud-config), reads through views (`*_V`), not base tables — a meaningful departure from Marshal's pattern.

| Dimension | Marshal | Excys | AFI | BilalSoft |
| --- | --- | --- | --- | --- |
| Source engine | Oracle (confirmed) | Oracle (inferred — pending confirmation) | SQL Server (confirmed) | SQL Server (confirmed) |
| Legacy access path | Linked server `DHS..[NAFD]` (4-part names, base tables) | Linked server `DHS` via `OPENQUERY` (views `DHS.*_V`) | Same-server DB `DHSA.dbo.*` | Linked server `BILAL.medical_net.*` |
| Source topology | Tables, 1:1 with destination | **Views** (`DHSCLAIM_HEADER_V`, etc.), 1:1 with destination | Tables, 1:1 with destination | Views, denormalized across service lines |
| Header grouping | None — one source row per header | None — one source row per header | None | `LAG()` + 14-day gap + patient/doctor/claimtype change |
| Child→header rebind | `InvoiceNumber` (collation-insensitive in C#) | `InvoiceNumber` (collation-insensitive in C#; legacy SP applied `COLLATE SQL_Latin1_General_CP1_CI_AS` on the SQL-Server destination side) | `InvoiceNumber + PatientNo + CompanyCode` | Per-child: some `invoiceNumber`, some `itemRefernce`, some computed group id |
| Transformation weight | Light (truncations, UPPER, NULL→0) | Light (truncations, `CEILING` for PBM, UPPER for diagnosis, flag normalization `F`→`0`) | Medium (clamps, constants, LEFT) | Heavy (CASE trees, UDFs, percentage formulas, cross-table back-fills) |
| Source key column | `proidclaim` (stable int) | `proidclaim` (stable int) | `proidclaim` (stable int) | No stable source key — header identity is synthesized by grouping |
| Doctor-table date quirk | None | **Asymmetric**: lower bound on `batchStartDate`, upper bound on `invoicedate` (captured via `dateColumnOverride` + `dateFilterMode: upperBoundOnly`) | Same quirk as Excys (via `dateColumnOverride`) | N/A (grouped) |
| Post-load rules | `RecomputeHeaderTotals`, `Height/Weight='0'`→NULL, delete services for `CompanyCode='12'` | `RecomputeHeaderTotals`, delete zero-amount services for `CompanyCode='100002'` | `RecomputeHeaderTotals`, Pulse clamp, DurationOFIllness clamp, delete for `CompanyCode='INS02'` | `RecomputeHeaderTotals`, EMR back-fill, member-id match, SFDA DiagnosisCode back-fill, IRA/NA flag, delete for `CompanyCode='1241020001'` |
| Collation needs | Default (Oracle NLS) | Default (Oracle NLS); invoice rebind is collation-insensitive in C# | `Arabic_CI_AS` + `Latin1_General` | `Arabic_CI_AS` + `Latin1` |
| Tier | 2 | 2 | 2 | 2 or 3 (see Open Questions Q1) |

The SQL aliasing model collapses mapping differences at the query layer, so the transform catalog does not need Tier-1-vs-Tier-2 slicing for most vendors — every vendor ships a hand-authored `SELECT` per entity.

---

## 2. Destination Schema — NPHIES

The destination database (`[DHS-NPHIES]`) is the target all four vendors load into. This section summarizes the schema derived from `NPHIES_DB_Documentation (1).md` and is the source of truth for column names, types, and cardinalities.

### 2.1 Central invariant

`DhsclaimHeader.ProIdClaim` is the int primary key of every claim. Every child table carries `ProIdClaim` as a foreign key, with one exception: `DhsItemDetail` is a grandchild linked via `DhsOpticalVitalSign.SerialNo`.

### 2.2 Tables

**`DhsclaimHeader`** — the main claim record.

- PK: `ProIdClaim` (int).
- Key columns: `DoctorCode`, `PatientName`, `PatientNo`, `MemberId`, `ClaimType`, `ClaimedAmount`, `InvoiceNumber`, `InvoiceDate`, `BatchNo`, `BatchStartDate`, `BatchEndDate`, `IsFetched`, `IsSync`.

**`DhsserviceDetail`** — service line items.

- PK: `ServiceId`. FK: `ProIdClaim`.
- Key columns: `ServiceCode`, `ServiceDescription`, `LineClaimedAmount`, `NetAmount`.

**`DhsdiagnosisDetail`** — diagnoses.

- PK: `DiagnosisId`. FK: `ProIdClaim`.
- Key columns: `DiagnosisCode`, `DiagnosisDesc`.

**`DhslabDetail`** — lab test results.

- PK: `LabId`. FK: `ProIdClaim`.
- Key columns: `LabTestName`, `LabResult`, `VisitDate`.

**`DhsradiologyDetail`** — radiology results.

- PK: `RadiologyId`. FK: `ProIdClaim`.
- Key columns: `ServiceCode`, `RadiologyResult`, `VisitDate`.

**`DhsAttachment`** — file attachments for claims.

- PK: `AttachmentId`. FK: `ProIdClaim`.
- Key columns: `Location`, `AttachmentType`, `ContentType`, `FileSizeInByte`.

**`DhsAchi`** — ACHI procedure codes.

- PK: `Achiid`. FK: `ProIdClaim`.
- Key columns: `Achicode`.

**`DhsDoctor`** — doctor reference.

- PK: `DocId`.
- Key columns: `DoctorName`, `Specialty`, `DoctorCode` (links back to `DhsclaimHeader.DoctorCode`).

**`DhsOpticalVitalSign`** — optical measurements.

- PK: `SerialNo`. Also carries `ProIdClaim`.
- Key columns: `RightEyeSphereDistance`, `LeftEyeSphereDistance`.

**`DhsItemDetail`** — grandchild of optical details.

- PK: `SerialNo`. Carries `ProIdClaim`.
- FK to parent optical: `FkOpticSerialNo` → `DhsOpticalVitalSign.SerialNo`.

### 2.3 Cardinality tree

```text
DhsclaimHeader (1)
 ├── DhsAchi (N)
 ├── DhsAttachment (N)          ← separate upload pipeline, not embedded in ClaimBundle
 ├── DhsdiagnosisDetail (N)
 ├── DhslabDetail (N)
 ├── DhsradiologyDetail (N)
 ├── DhsserviceDetail (N)
 └── DhsOpticalVitalSign (N)
       └── DhsItemDetail (N)
```

### 2.4 Mapping to canonical `ClaimBundle`

| Destination table | `ClaimBundle` section | `proidclaim` type in payload |
| --- | --- | --- |
| `DhsclaimHeader` | `claimHeader` (JsonObject) | — |
| `DhsserviceDetail` | `serviceDetails` (JsonArray) | **string** (intentional) |
| `DhsdiagnosisDetail` | `diagnosisDetails` (JsonArray) | int |
| `DhslabDetail` | `labDetails` (JsonArray) | int |
| `DhsradiologyDetail` | `radiologyDetails` (JsonArray) | int |
| `DhsOpticalVitalSign` | `opticalVitalSigns` (JsonArray) | int |
| `DhsDoctor` | `dhsDoctors` (JsonArray) | int |
| `DhsAttachment` | uploaded separately; back-filled as `attachments[]` with `onlineUrl` only | n/a |
| `DhsAchi` | fetched but not in the canonical bundle today | n/a |
| `DhsItemDetail` | fetched but not in the canonical bundle today | n/a |

### 2.5 Sync flags

`IsFetched` and `IsSync` live on the destination side. The agent tracks its own workflow state in local SQLite (`Contracts/Persistence/BatchRow` plus `Domain/WorkStates`); the two state spaces are distinct.

---

## 3. Architectural Ideas

Four architectural options were considered. Option D is recommended. The other three are documented so the rationale for rejecting them is visible.

### 3.1 Option A — Keep SPs, call them through Dapper

Each vendor keeps its `USP_FetchClaims`; the agent calls `connection.ExecuteAsync("USP_FetchClaims", args, commandType: StoredProcedure)`.

This does not solve the underlying problem. SPs stay divergent, still need on-site DBA deployment, and remain hard to test. Dapper is decorative here. **Rejected.**

### 3.2 Option B — Pure metadata engine

Everything — grouping rules, CASE trees, function transforms, post-load rules — lives in configuration rows. One engine interprets them.

This works for 60–70% of vendors. For the remaining 30–40% (BilalSoft-class) it forces the team to build a SQL-in-YAML interpreter. Testing and debugging degrade sharply. **Rejected as a default;** the ideas survive inside Option D's Tier 1.

### 3.3 Option C — One plugin class per vendor

Generic pipeline with one `IClaimSource` implementation per vendor. Every vendor ships a C# class.

With 3 vendors this is clean. With 20, it is 20 PRs and 20 releases for differences that are often just "the table name is X instead of Y." The agent dev team becomes a bottleneck for every new site. **Rejected as the default;** the plugin mechanism survives as Tier 3 in Option D.

### 3.4 Option D — Tiered descriptor-first with SQL aliasing (recommended)

Same generic pipeline as Option C, but onboarding is tiered and the per-row column mapping lives in SQL rather than in a runtime transform engine.

| Tier | What you write | Who writes it | Ship path |
| --- | --- | --- | --- |
| 1 | Pure JSON descriptor against a generic templated SQL. Only for vendors whose source columns already match the canonical model 1:1. | Support engineer or DBA | JSON-only; no release |
| 2 | JSON descriptor plus vendor-owned `.sql` files with explicit column aliases, dialect-specific casts, and null-default handling. | Support engineer + DBA | JSON + SQL; DBA PR |
| 3 | JSON descriptor plus a C# plugin class implementing `IClaimSource` (and optionally `IVendorTransform`). Reserved for logic SQL cannot express cleanly. | Agent dev team | Normal release cycle |

In practice, **Tier 2 is the expected default** for current vendors because every vendor has column-name differences, typos, or dialect-specific null handling that are easier to encode in SQL than in transform JSON. The tiered model scales because the declarative surface (JSON) covers cross-row behaviour (post-load rules, rebind strategies, cache policies) while the per-row column shape lives in a file the DBA can read and review.

See §4.11 for the SQL aliasing conventions that make vendor SQL files interchangeable across engines.

### 3.5 Recommendation

**Option D.** Design details in §4. Descriptor model lives in `DHS_SyncLayer_VendorDescriptors.md`. Per-vendor concrete specs live in `DHS_SyncLayer_VendorPlaybook.md`.

---

## 4. Implementation Plan

### 4.1 Scope of impact — three consumers, not one

The existing `IProviderTablesAdapter` is consumed by three production subsystems, each with different needs. The sync layer replacement must serve all three.

| Consumer | Today's use | Sync layer obligation |
| --- | --- | --- |
| `FetchStageService` (Workers) | Pages claim keys, fetches full bundles, hands them to `ClaimBundleBuilder` | Stream `ClaimBundle`s from the pipeline; resume-safe |
| `AttachmentDispatchService` (Workers) | Calls `GetAttachmentsForBatchAsync` to list attachment metadata for upload | Separate attachment pipeline — see §4.5 |
| `BatchCreationOrchestrator` (WPF UI) | Calls `CountClaimsAsync` and `GetFinancialSummaryAsync` before creating a batch, to show the user a validation dialog | Pre-batch summary path — see §4.4 |

The new abstraction decomposes into three callable surfaces, not one:

```text
IClaimExtractionPipeline     — used by FetchStageService
IClaimAttachmentExtractor    — used by AttachmentDispatchService
IClaimExtractionPreview      — used by BatchCreationOrchestrator
```

All three share the same underlying `IClaimSource` per vendor plus the same `DapperConnectionFactory`, but they expose different operations to different callers. A single monolithic interface would force the UI to pay the cost of full bundle construction just to show a count.

### 4.2 Project layer

New project `src/DHSIntegrationAgent.Sync/`:

```text
DHSIntegrationAgent.Sync/
├── Abstractions/
│   ├── IClaimSource.cs
│   ├── IClaimSourceRegistry.cs
│   ├── IVendorTransform.cs
│   ├── IClaimExtractionPipeline.cs
│   ├── IClaimExtractionPreview.cs
│   ├── IClaimAttachmentExtractor.cs
│   ├── ITransformStep.cs
│   ├── IPostLoadRule.cs
│   └── ExtractionContext.cs
├── Pipeline/
│   ├── ClaimExtractionPipeline.cs
│   ├── ClaimExtractionPreview.cs
│   ├── ClaimAttachmentExtractor.cs
│   ├── BundleAssembler.cs
│   ├── BatchKey.cs
│   ├── SourceClaimKey.cs
│   └── ResumeCursor.cs
├── Dto/
│   ├── ClaimHeaderDto.cs
│   ├── ServiceLineDto.cs
│   ├── DiagnosisDto.cs
│   ├── LabDto.cs
│   ├── RadiologyDto.cs
│   ├── AttachmentDto.cs
│   └── DoctorDto.cs
├── Dapper/
│   ├── DapperConnectionFactory.cs
│   ├── DapperTypeHandlers.cs
│   ├── IdentifierValidator.cs
│   └── EmbeddedSqlLoader.cs
├── Descriptor/
│   ├── VendorDescriptor.cs
│   ├── DescriptorLoader.cs
│   ├── DescriptorValidator.cs
│   └── DescriptorSchema/vendor.schema.json
├── Transforms/
│   ├── ITransformRegistry.cs
│   ├── TransformRegistry.cs
│   └── Catalog/
├── PostLoad/
│   ├── IPostLoadRuleRegistry.cs
│   └── Catalog/
├── GenericSource/
│   ├── MetadataClaimSource.cs
│   └── MetadataTransformRunner.cs
├── Vendors/
│   └── BilalSoft/
│       ├── BilalClaimSource.cs
│       ├── BilalHeaderGrouper.cs
│       ├── BilalResumeCursor.cs
│       ├── BilalPreview.cs
│       ├── BilalTransform.cs
│       └── Sql/
├── VendorDescriptors/
│   ├── marshal.vendor.json
│   ├── afi.vendor.json
│   ├── bilalsoft.vendor.json
│   └── excys.vendor.json
├── Tooling/
│   ├── DescriptorGenerator.cs
│   ├── DescriptorTester.cs
│   └── ParityReporter.cs
└── DependencyInjection/
    └── SyncServiceCollectionExtensions.cs
```

**Project references:** `Contracts`, `Domain`, `Application`. Sync does not reference `Adapters` (though `Adapters` consumers are rewired to Sync — see §4.10).

### 4.3 Resume and identity model

The current resume model uses `lastSeen = MaxProIdClaim` paging, relying on a stable integer claim key in the source (`FetchStageService.cs:118, 179-186`). This works for Marshal and AFI, where source headers have native `ProIdClaim` values.

**It does not work for BilalSoft**, which synthesizes claim identity by grouping service lines on (patient, doctor, claim type, 14-day gap).

Introduce `ResumeCursor` as an abstraction over claim identity:

```csharp
sealed record ResumeCursor(string Value);
// opaque string; format is vendor-defined
// ordered; the pipeline only needs "greater than last seen"
```

Per-vendor concrete encodings:

- **Marshal / AFI / Excys** (Tier 2 via `MetadataClaimSource`) — cursor value is the source `ProIdClaim` as a decimal string. Same behaviour as today, wrapped in the abstraction.
- **BilalSoft** — cursor value is a composite key derived from the grouping dimensions (see Playbook §3 for the exact encoding). Must be deterministic across re-runs of the same source data. If the source data changes mid-batch, the cursor's contract says "best-effort monotonic; do not assume perfect dedupe."

`SourceClaimKey` is redefined as `(ResumeCursor Cursor, string InvoiceNumber)`. The cursor is for paging; the invoice number is for rebinding children and for human-readable logs.

**Staging implications.** The local SQLite `Claims` table today stores provider `ProIdClaim` as the claim identity. This must widen to store the opaque cursor string alongside (or instead of) the int key:

- Add `SourceCursor TEXT NULL` column to the `Claims` table.
- Tier 2 vendors populate both (cursor + int key).
- Tier 3 vendors populate cursor only; int key is nullable.
- `GetMaxProIdClaimAsync` becomes `GetMaxCursorAsync(providerCode)` with per-vendor ordering.

### 4.4 Pre-batch validation

`BatchCreationOrchestrator` currently calls `CountClaimsAsync` and `GetFinancialSummaryAsync` before creating a batch and displays a blocking confirmation dialog with counts and financial totals.

For Tier 2 vendors with 1:1 source headers (Marshal, AFI, Excys), this maps cleanly. **For BilalSoft, pre-batch validation is more expensive**: claim count requires grouping, and financial totals require post-grouping aggregation. The UI cannot afford a long pre-batch query.

Three tactics, composed:

1. **Cheap approximate count first.** `IClaimExtractionPreview.EstimateAsync` returns a lower-bounded claim count computed from the source without grouping. For BilalSoft, this is the number of distinct `(patientno, doctorcode, claimtype)` tuples in the date range — computable with a single aggregate query and always ≤ the true grouped count (one tuple can produce multiple groups when there are ≥14-day gaps between visits). UI shows "at least N claims" instantly.
2. **Exact count on demand, backgrounded.** If the user clicks "Show exact count", `IClaimExtractionPreview.ComputeExactAsync` runs the full grouping in the background and updates the dialog when ready.
3. **Financial totals follow the same pattern.** For Tier 2 the totals are a `SUM()` query over source headers. For BilalSoft the totals are computed from raw service lines without grouping (financial aggregation is associative — the total net amount for a grouped claim equals the sum of its service-line net amounts regardless of grouping). This means BilalSoft's financial preview is fast even though its count is slow.

The UI dialog changes from "show blocking summary" to "show available numbers, compute refinements asynchronously, let the user proceed when satisfied." This is a real UX change, not just an internal refactor. Confirm with the support lead before Phase 5 (see Open Questions Q11).

### 4.5 Attachment pipeline — explicitly separate

Attachments are a separate concern from claim extraction. `AttachmentDispatchService` reads attachment rows directly from the provider and uploads their content to blob storage. It never builds a `ClaimBundle`.

In the sync layer:

- `IClaimAttachmentExtractor` is its own interface, fed by the same `IClaimSource` but calling a dedicated method:

  ```csharp
  IAsyncEnumerable<AttachmentDto> EnumerateAttachmentsAsync(BatchKey key, CancellationToken ct);
  ```

- The `IClaimSource` interface exposes attachments as a separate method, not as a field on the claim bundle. Claim bundles do not carry attachment content or binary payloads; they carry claim data only.
- Descriptor has a distinct `attachments` section, not nested under `transforms.header`.
- The main claim read fetches seven result sets (header, services, diagnoses, labs, radiology, optical, doctors). Attachments come through a separate query.

This matches the existing separation (`FetchStageService` ↔ `AttachmentDispatchService`), so there are no downstream contract changes.

### 4.6 Dapper usage — scoped and deliberate

Dapper is used only for provider-DB reads. Local SQLite repositories keep their current hand-rolled pattern (`SqliteRepositoryBase`).

**Role 1 — typed materialization with per-DTO behaviour.**

```csharp
var headers = await conn.QueryAsync<ClaimHeaderDto>(
    _sql.FetchHeaders,
    new {
        CompanyCode = key.CompanyCode,
        StartDate   = key.StartDateUtc.UtcDateTime,
        EndDate     = key.EndDateUtc.UtcDateTime
    });
```

DTOs use Dapper's default column-to-property mapping. Columns with source names that differ from the destination are aliased in SQL, not in code.

**Role 2 — multi-result read in one round-trip.** `QueryMultipleAsync` with the seven claim result sets. This replaces today's ten separate round-trips in `ProviderTablesAdapter.GetClaimBundlesRawBatchAsync`.

**Role 3 — parameterized `IN` via list expansion.**

```csharp
// SQL Server: WHERE ProIdClaim IN @Keys
// Oracle:     WHERE ProIdClaim IN :Keys
param: new { Keys = pageKeys.Select(k => k.SourceId).ToArray() }
```

**No global type handlers.** Registering a global `decimal? → 0` handler would erase vendor-specific semantics: Marshal wraps financials in `NVL(...)`, AFI does not. Registering a global `string → int` handler would erase the intentional string/int split for `proidclaim` in different sections of the canonical `ClaimBundle` (see `ClaimBundleBuilder.cs`).

Instead:

- DTO properties are typed to match source column types exactly.
- Null-to-default behaviour is expressed in the vendor SQL file via `NVL` (Oracle) / `ISNULL` (SQL Server).
- The only globally registered handler is UTC round-trip for `DateTime` — schema-independent and purely about clock correctness.

### 4.7 Identifier validation — the real SQL risk

Earlier drafts framed the SQL risk as injection via the IN-clause keys. That was the wrong emphasis; the keys are ints and bounded.

**The real risk** is that `tableName` and `keyCol` in the current `ProviderTablesAdapter` (`FetchHeaderBatchAsync` at lines 500–515, 534–551, 568–582) are interpolated directly into SQL strings from values that originate in SQLite-stored `ProviderExtractionConfig`. The descriptor model expands this risk: operators can edit descriptors at runtime on a deployed site, and a bad or malicious descriptor turns identifier interpolation into an injection vector.

Mitigations. The validator accepts an `engine` hint per vendor and picks the correct metadata source and quoting style:

1. **Identifier whitelist.** Every table and column name in a descriptor is validated against the engine's metadata view at load time:
   - SQL Server → `INFORMATION_SCHEMA.COLUMNS` / `INFORMATION_SCHEMA.TABLES`.
   - Oracle → `ALL_TAB_COLUMNS` / `ALL_TABLES` (use `USER_*` if the agent's login owns the schema; `ALL_*` otherwise).
2. **Identifier format checks.** Identifiers must match `^[A-Za-z_][A-Za-z0-9_]*$` (with optional `schema.` prefix). Anything containing quotes, semicolons, whitespace, or reserved SQL keywords is rejected. Quoting rules differ per engine:
   - SQL Server → bracket quoting `[X]` allowed only when `X` itself matches the format regex.
   - Oracle → double-quoted `"X"` allowed only when `X` itself matches the format regex. Unquoted Oracle identifiers are normalized to upper-case per Oracle's rules, so the descriptor must match that.
3. **Parameterize where possible, quote where not.** The pipeline templates SQL using string substitution for identifiers (after validation) and Dapper parameters for values. There is no runtime path where a value becomes SQL. Oracle bind-parameter style is `:name`; SQL Server is `@name`.
4. **No raw SQL from descriptors.** Descriptors cannot inject `customSql` snippets that are executed as-is. Tier 2's `.sql` files are shipped alongside the descriptor (file-system resources, code-reviewed). Runtime overrides can change table/column names and transform arguments only, not SQL bodies.

Detailed rules live in `DHS_SyncLayer_VendorDescriptors.md` §6.

### 4.8 Poison-record handling

The current code has a subtle asymmetry:

- `FetchStageService` drops failed-build bundles silently (the claim is not staged; the batch continues) — see `FetchStageService.cs:214-244`.
- `AttachmentDispatchService` throws on an upload failure, which can terminate the worker loop — see `AttachmentDispatchService.cs:173-183`.

The sync layer standardizes on **per-record isolation with explicit issue persistence**:

1. A transformation or assembly failure on one claim writes a `ValidationIssue` row (using the existing `ValidationIssueRepository`) with the claim's source cursor, the failing step, and the exception message.
2. The batch continues processing other claims.
3. The batch cannot be marked `Ready` while unresolved validation issues exist for it — an explicit dashboard surface lists them for operator review.
4. An attachment failure writes a `ValidationIssue` tied to the owning claim, does not throw, and does not block the batch.

This lifts the existing `ValidationIssue` plumbing into a first-class error channel for the sync layer rather than a vestigial table.

### 4.9 Idempotency — replacing `sp_deletebatch` semantics

Every vendor SP starts with `exec sp_deletebatch @InsuranceCode, @FromDate, @ToDate`. That procedure wipes all destination rows for the given key before reload. Semantically: **SP runs are replace-all for a `(CompanyCode, DateRange)` window**, not upsert.

The new agent models this differently: staged claims live in local SQLite and are updated incrementally. Resume-after-interruption is a first-class feature. These are not semantically equivalent to "delete-and-reload."

Decisions:

1. **Re-run semantics.** If a batch is re-run for the same `(ProviderDhsCode, CompanyCode, DateRange)`, the new pipeline marks existing staged claims for the batch as `Superseded` and emits fresh ones with new cursors. The API-side dispatch path already treats staged-claim state transitions correctly; `Superseded` means "don't dispatch this, a newer version of the same claim exists."
2. **Resume semantics.** Partial runs resume via the `ResumeCursor` (§4.3). No delete-and-reload on resume.
3. **Source-side state.** The new pipeline never writes to the provider DB. `sp_deletebatch`'s side effect on the destination staging tables simply disappears — those tables are no longer written to.
4. **Operator-initiated reload.** If an operator wants a genuine replay-from-scratch, the UI exposes "Reset and re-fetch" which explicitly clears staged claims for the batch and restarts the cursor. This mirrors the existing "recreate batch" path in `BatchCreationOrchestrator`.

### 4.10 Alignment with existing code patterns

Three deviations from existing solution conventions, each contained to the Sync project:

1. **Dapper for provider DB reads.** Local SQLite keeps its current hand-rolled `SqliteRepositoryBase` pattern unchanged.
2. **No Unit of Work for provider DB reads.** Reads are streaming and stateless; UoW would be ceremony. Factory-per-batch pattern (same as existing `IProviderDbFactory`).
3. **Embedded `.sql` resource files** instead of inline SQL strings. DBAs review SQL independently of C#.

Three new conventions introduced:

1. **Catalog registries** for transforms and post-load rules (`ITransformRegistry`, `IPostLoadRuleRegistry`).
2. **JSON descriptor loading** with a file-then-SQLite override chain.
3. **Explicit plugin registration** via `services.AddClaimSource<T>("CODE")`.

**Existing layering.** The solution's current Clean-Architecture separation is aspirational at some seams. `Workers` references `Adapters` directly (not just `Application` abstractions), and `ClaimBundleBuilder` is constructed with `new` in `FetchStageService.cs:133` rather than injected. The sync layer does not attempt to fix those seams wholesale; it introduces its own abstractions cleanly and leaves existing direct references in place. `ClaimBundleBuilder` usage migrates to DI-registered when the pipeline assembles bundles.

**Multi-engine is a day-one requirement.** Two Oracle vendors (Marshal, Excys) and two SQL Server vendors (AFI, BilalSoft). The existing `IDbEngineFactory` has `MySqlEngineFactory`, `OracleEngineFactory`, and `PostgreSqlEngineFactory` stubs that throw `NotImplementedException`; of those, **`OracleEngineFactory` must be implemented in Phase 1** (using the `Oracle.ManagedDataAccess.Core` NuGet package — pure managed driver, no Instant Client dependency). MySQL and PostgreSQL remain stubs until a vendor actually requires them.

Dialect differences the sync layer absorbs:

| Concern | SQL Server | Oracle |
| --- | --- | --- |
| NULL default | `ISNULL(x, 0)` | `NVL(x, 0)` |
| Substring | `LEFT(x, n)` / `SUBSTRING(x, 1, n)` | `SUBSTR(x, 1, n)` |
| Current timestamp | `GETDATE()` / `SYSUTCDATETIME()` | `SYSDATE` / `SYSTIMESTAMP` |
| Paged read | `SELECT TOP (n) ...` | `FETCH FIRST n ROWS ONLY` (12c+) |
| Date literal parse | `CONVERT(date, '20260101')` | `TO_DATE('20260101','YYYYMMDD')` |
| Regex strip non-digits | CLR UDF / `TRANSLATE` | `REGEXP_REPLACE(x, '[^0-9]', '')` |
| Parameter marker | `@name` | `:name` |
| Identifier quoting | `[name]` | `"name"` (case-sensitive) |
| Schema metadata | `INFORMATION_SCHEMA.*` | `ALL_TAB_COLUMNS` / `USER_TAB_COLUMNS` |

Vendor `.sql` files stay in the native dialect of their source. Cross-dialect concerns (parameter markers, null-default defaults on result DTOs) are centralized in `DapperConnectionFactory` and a thin `ISqlDialect` helper.

Full code-style checklist lives in `DHS_SyncLayer_VendorDescriptors.md` §9.

### 4.11 SQL aliasing conventions

Every vendor SQL file performs name and type mapping inside the `SELECT` so that the Dapper DTO populates by convention with zero code-level alias tables. Conventions:

1. **Alias every source column to the canonical (NPHIES destination) name** using `AS <CanonicalName>`. Casing matches the destination schema (e.g. `EncounterTypeID`, not `encountertypeid`).
2. **Apply cheap, per-row transforms in SQL**, not in the post-load stage:
   - Null defaults (`NVL` / `ISNULL`).
   - Truncation (`SUBSTR` / `LEFT`) where the destination length is shorter.
   - Case normalization (`UPPER` / `LOWER`).
   - Literal constants (`'SAR' AS TreatmentCountryCode`).
   - Numeric cleanup helpers (`REGEXP_REPLACE` on Oracle; equivalent UDF on SQL Server).
3. **Keep cross-row rules in descriptors**, not SQL:
   - `RecomputeHeaderTotals` (needs post-insert aggregates across services).
   - `ClampHeaderField` on Pulse and DurationOFIllness.
   - `DeleteServicesByCompanyCode`.
   - `SetInvestigationResult` (needs lab/radiology presence check).
   - Member-ID, EMR, and SFDA diagnosis back-fills (need joins to auxiliary tables fetched separately).
4. **Preserve known quirks explicitly.** Buggy behaviour we decide to replicate (see Open Questions Q5) lives in the SQL with a code comment pointing back to the decision, so future DBAs do not "fix" it silently.
5. **Dialect file pairing.** Each vendor folder contains one dialect-specific SQL file per entity (`header.oracle.sql`, `services.sqlserver.sql`, etc.). The descriptor's `customSql` map picks files by entity name; the engine is implicit from the vendor's `source.engine` field.

The runtime "transform catalog" shrinks from the full list to a handful that SQL cannot express cleanly (e.g. the `CopayPercentage` parity quirk for BilalSoft, the `REPLACE(x,' ',NULL)` behaviour flag). See `DHS_SyncLayer_VendorDescriptors.md` §3 for the revised catalog.

### 4.12 Configuration flow — Cloud API → SQLite cache

The operational flow for connection strings and per-client tuning is **already implemented** in the current codebase. Confirmed by inspecting the source:

1. Operator logs into the WPF app — triggers `LoginViewModel.cs:129`.
2. App calls `GET /api/Provider/GetProviderConfigration/{providerDhsCode}` (and `GetProviderInfo/{providerDhsCode}`) via `ProviderConfigurationClient` (`Infrastructure/Http/Clients/ProviderConfigurationClient.cs`).
3. API reads the client's record from a cloud-hosted **PostgreSQL** configuration DB.
4. Response is parsed by `ProviderConfigurationService` (`Infrastructure/Providers/ProviderConfigurationService.cs`) and cached into local SQLite — DPAPI-encrypted for the connection string.
5. ETag-based refresh runs continuously via `ProviderConfigurationRefreshHostedService`.

The JSON response shape the current consumer reads:

- `providerInfo` — `connectionString`, `dbEngine` (`sqlserver` / `oracle`), `integrationType`, `blobStorageConnectionString`, `containerName`.
- `providerPayers[]` — `companyCode`, `payerCode`, `payerId`, `payerNameEn`, `parentPayerNameEn`.
- `domainMappings[]` — `providerDomainCode`, `providerDomainValue`, `codeValue`, `displayValue`.
- `missingDomainMappings[]` — `domainTableName`, `providerCodeValue`, `providerNameValue`.
- `generalConfiguration` — object (shape TBD from consumer path).

The existing `IProviderDbFactory` is the correct seam for the sync layer to read connection strings from; it reuses the decryption path and caches per-provider handles.

**Known bug (confirmed by code inspection, not a design question).** `ProviderConfigurationService.cs:386-387` has the correct line commented out and a hardcoded test value overriding it:

```csharp
//var connectionString = GetString(providerInfo, "connectionString");
var connectionString = "Server=.\\SQLEXPRESS;Initial Catalog=DHSA;User Id=root;Password=root;...";
```

The surrounding infrastructure (DPAPI encryption, SQLite persistence, refresh hosted service) is intact — only the input source is wrong. The fix is a two-line change: uncomment the real line and delete the override.

**Phase 0 — Configuration cleanup (prerequisite).**

- Apply the two-line fix in `ProviderConfigurationService.cs:386-387`.
- Audit the `generalConfiguration` payload for fields the sync layer needs but does not read today (per-vendor batch size, retry/timeout, feature flags) — see Open Questions Q3.
- Extend the local SQLite schema if new fields must be cached. The existing `ProviderProfile` + `ProviderExtractionConfig` rows already hold most of what the sync layer needs; only add columns if Q3's answer requires them.
- Add a startup guard: the agent refuses to begin extraction for a vendor without a cached connection string. No silent fallback to localhost.

See `DHS_SyncLayer_VendorDescriptors.md` §7.5 for the proposed cache-table schema (now positioned as an addendum to the existing `ProviderProfile`, not a replacement).

### 4.13 Rollout and phasing

The rollout is phased by consumer and by vendor, with feature flags gating each step. Phase durations are removed from the plan by design; the gating constraint is parity-cycle completion, not calendar time.

**Phase 0 — configuration cleanup (prerequisite).** See §4.12. Blocks Phase 1.

**Phase 1 — framework, Oracle driver, and AFI (SQL Server).**

- Build `IClaimSource`, `IClaimExtractionPipeline`, `IClaimExtractionPreview`, `IClaimAttachmentExtractor`, `BundleAssembler`, the post-load rule catalog, `DapperConnectionFactory`, and the thin `ISqlDialect` helper.
- Implement `OracleEngineFactory` using `Oracle.ManagedDataAccess.Core`. Smoke-test against a Marshal Oracle snapshot.
- Build `DescriptorValidator`, `DescriptorGenerator`, `DescriptorTester`, and `ParityReporter`. The tools are not optional — they are what makes the descriptor path operationally safe at scale. Validator gates are dialect-aware (§4.7).
- Migrate AFI to a Tier 2 descriptor with a vendor-owned `.sql` file. Run `ParityReporter` against a month of production data. Differences must be zero or explicitly documented as bug fixes.
- Behind `Sync:UsePluginPipeline:AFI` feature flag, run the new pipeline in parallel with the legacy path on one pilot site. Compare counts, financial sums, and emitted bundles. Flip the flag after a clean parity cycle.

**Phase 2 — Marshal (Oracle) onboarding.** Depends on Phase 1.

- Author `marshal.vendor.json` plus `marshal/*.oracle.sql` files. Translate the T-SQL logic from the legacy linked-server SP into native Oracle.
- Run `ParityReporter` against the legacy SP output for the same date range.
- Parallel-run plus flag flip identical to Phase 1.

**Phase 3 — Excys (Oracle) onboarding.** Depends on Phase 2 (Oracle driver).

- Spec delivered 2026-04-18 — see `PLAN/VEND/exeys/` and Playbook §4.
- **Inferred Oracle** (engine confirmation pending from DBA / cloud-config connection string, per Playbook §4.1), schema `DHS`, reads through views (`DHSCLAIM_HEADER_V`, etc.) — **not** base tables. This is the key difference from Marshal and is captured by per-entity `accessMode: "view"` on each table under `source.tables` (see VendorDescriptors §6.1 gate 7a).
- Residual blocker: Open Question **Q13** (views vs base tables for the new direct-Oracle pipeline). Default is "use the views" to match legacy SP behaviour exactly.
- Author `excys.vendor.json` plus `excys/*.oracle.sql` files using the mapping matrix in Playbook §4.2.
- Capture the `CompanyCode='100002'` zero-amount cleanup as a descriptor `postLoadRule` (see Q7 for the policy).
- Same parity-run rollout as Phase 2.

**Phase 4 — BilalSoft.** Can overlap with Phase 2/3.

- Tier 2 vs Tier 3 is decided by Open Questions Q1. If Tier 3: implement `BilalClaimSource`, `BilalHeaderGrouper` (pure function, unit-testable independent of DB), `BilalResumeCursor`, `BilalPreview`, `BilalTransform`. If Tier 2: the grouping is a CTE plus `LAG()` plus `SUM() OVER` window in the SQL file; C# stays generic.
- See `DHS_SyncLayer_VendorPlaybook.md` §3 for the full spec.
- Same parallel-run rollout as earlier phases.

**Phase 5 — hardening.** Can overlap with Phase 4.

- Descriptor override governance: versioning, checksum, provenance, signed-by on SQLite rows, merge/upgrade logic. See `DHS_SyncLayer_VendorDescriptors.md` §7.
- Provider-extraction observability: per-query timing, per-rule application counts, descriptor version in logs, parity diff summaries.
- UI rework for asynchronous pre-batch validation (§4.4).
- Poison-record dashboard surface.

**Phase 6 — legacy retirement.**

- Once all four active vendors are on the new pipeline, mark `ProviderTablesAdapter` `[Obsolete]` and remove in a subsequent release.
- Drop the `Custom*Sql` columns from `ProviderExtractionConfig` or migrate them into the Tier 2 descriptor path (see Open Questions Q12).

---

## 5. Open questions

Open questions and confirmed decisions live in `DHS_SyncLayer_OpenQuestions.md`. Summary:

- **Blockers (Q1–Q3):** BilalSoft Tier 2 vs Tier 3, SQL aliasing scope, Cloud-config API missing fields. (Q4 closed 2026-04-18 — Excys spec delivered.)
- **Important (Q5–Q6):** Four parity-vs-fix behaviours in the legacy SPs, Logic-implementation granularity.
- **Good-to-have (Q7–Q13):** Hardcoded company codes (now including Excys's `100002`), collation catalog, override audit model, UX shift, `Custom*Sql` migration, PHI in parity artifacts, and **Q13** — Excys views vs base tables for the direct-Oracle pipeline.

Two items previously listed were closed after code inspection and no longer need team input:

- The cloud-config API is confirmed live (`GET /api/Provider/GetProviderConfigration/{providerDhsCode}`), called on login by `LoginViewModel.cs:129`. Only the missing-field audit remains as Q3.
- The hardcoded `Server=.\SQLEXPRESS; Password=root` in `ProviderConfigurationService.cs:386-387` is a confirmed dev bug (the real call is commented out on line 386). Lands in Phase 0, no decision needed.

---

## 6. Related documents

- `DHS_SyncLayer_VendorDescriptors.md` — descriptor JSON schema, transform catalog, post-load rule catalog, per-child rebind model, override governance, validation gates, tooling.
- `DHS_SyncLayer_VendorPlaybook.md` — per-vendor specs. Marshal (Tier 2, Oracle, base tables in schema `NAFD`), AFI (Tier 2, SQL Server, same-server DB `DHSA`), BilalSoft (Tier 2 or 3, SQL Server linked server), Excys (Tier 2, Oracle, views `DHS.*_V`). Includes the Mapping Matrix for each vendor.
- `DHS_SyncLayer_OpenQuestions.md` — consolidated open items awaiting team confirmation plus already-confirmed decisions.
- `NPHIES_DB_Documentation (1).md` — destination schema reference; authoritative for column names, types, and parent-child cardinalities.
- `PLAN/VEND/<vendor>/USP_FetchClaims.sql` plus `USP_FetchClaims_Mapping.md` — legacy stored procedures and their mapping notes. Source of truth for the logic to replicate.
