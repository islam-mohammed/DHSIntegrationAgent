# DHS Integration Agent — Vendor Descriptors

**Document 2 of 3** in the Sync Layer planning set:

- `DHS_SyncLayer_Plan.md` — overview, architecture, implementation plan
- **This document** — descriptor model, transform catalog, governance
- `DHS_SyncLayer_VendorPlaybook.md` — per-vendor specs

This document specifies the framework machinery that vendor descriptors depend on. It is reference material for implementers and for anyone authoring or reviewing a descriptor.

> **Scope update (2026-04-17):** the sync layer supports **two database > engines** — Oracle (Marshal, Excys) and SQL Server (AFI, BilalSoft). > Every descriptor declares its engine via `source.engine`.

Identifier > validation (§6), quoting, and parameter markers all branch per engine. > **SQL-level aliasing** (the SQL-aliasing approach) is the primary mapping > mechanism — column-to-canonical-name mapping lives inside the vendor > `SELECT` statements, not inside the `transforms` JSON.

The transform > catalog (§3) is now a **minimal** set reserved for transforms that > cannot be expressed cleanly in SQL (preserved bug quirks, cross-column > arithmetic with asymmetric guards, etc.).

See Plan §3.4 and §4.12.

---

## 1. Descriptor concept

A **vendor descriptor** is a JSON document that tells the generic pipeline how to extract claim data from one vendor's source database. It pairs with one or more dialect-specific `.sql` files that perform the per-row mapping through column aliases. Tiers:

- **Tier 1** — pure JSON descriptor against a generic templated SQL (only when source columns already match the canonical model 1:1; rare in practice).
- **Tier 2** — JSON descriptor + vendor-owned `.sql` files with explicit aliases, dialect-specific casts, and null-default handling. **Default tier for current vendors.**
- **Tier 3** — JSON descriptor + C# plugin class implementing `IClaimSource` and optionally `IVendorTransform`. Reserved for logic SQL cannot express cleanly.

Descriptors are loaded from two sources, in order:

1. **Shipped descriptors** — `.vendor.json` files embedded as resources in the Sync assembly or placed in the agent's `VendorDescriptors/` folder. These are released with the agent binary. 2. **SQLite overrides** — rows in the `VendorDescriptor` table on the agent's local database. A row with the same `providerCode` as a shipped descriptor overrides it for this specific site.

Override precedence: SQLite > shipped file. Validation rules (§6) apply identically to both. Per team direction, connection strings, batch sizes, and per-client tuning are **not** stored in the descriptor — they come from the cloud configuration API and are cached in SQLite separately (Plan §4.13).

---

## 2. JSON schema

The full JSON Schema lives at `Descriptor/DescriptorSchema/vendor.schema.json`. This section summarizes the structure with a worked example.

### 2.1 Tier 1 descriptor (Marshal example, abbreviated)

```json
{
  "schemaVersion": 1,
  "providerCode": "MARSHAL",
  "displayName": "Marshal HIS",
  "tier": 1,
  "source": {
    "databaseAlias": "DHS..[NAFD]",
    "engine": "sqlserver",
    "tables": {
      "header":      { "name": "[DHSCLAIM_HEADER]",       "keyColumn": "proidclaim" },
      "service":     { "name": "[DHSSERVICE_DETAILS]",    "parentKeyColumn": "proidclaim" },
      "diagnosis":   { "name": "[DHSDIAGNOSIS_DETAILS]",  "parentKeyColumn": "proidclaim" },
      "lab":         { "name": "[DHSLAB_DETAILS]",        "parentKeyColumn": "proidclaim" },
      "radiology":   { "name": "[DHSRADIOLOGY_DETAILS]",  "parentKeyColumn": "proidclaim" },
      "doctor":      { "name": "[DHS_DOCTOR]",            "parentKeyColumn": "proidclaim" }
    },
    "attachments": {
      "name": "[DHS_ATTACHMENT]",
      "parentKeyColumn": "proidclaim"
    },
    "dateColumn":  "batchStartDate",
    "companyCodeColumn": "companycode"
  },
  "resume": {
    "cursorStrategy": "sourceIntKey",
    "cursorColumn": "proidclaim"
  },
  "preview": {
    "countStrategy": "headerRowCount",
    "financialStrategy": "headerSumColumns"
  },
  "childRebind": [
    { "child": "service",    "strategy": "parentKey" },
    { "child": "diagnosis",  "strategy": "parentKey" },
    { "child": "lab",        "strategy": "parentKey" },
    { "child": "radiology",  "strategy": "parentKey" },
    { "child": "doctor",     "strategy": "parentKey" }
  ],
  "columnAliases": {
    "header": { "ENCOUNTERTYPEID": "EnconuterTypeID" }
  },
  "transforms": {
    "header": [
      { "column": "DiagnosisCode",       "steps": ["Upper"] },
      { "column": "ClaimedAmount",       "steps": ["NullToZero"] },
      { "column": "TotalNetAmount",      "steps": ["NullToZero"] },
      { "column": "TotalDiscount",       "steps": ["NullToZero"] },
      { "column": "TotalDeductible",     "steps": ["NullToZero"] },
      { "column": "RespiratoryRate",     "steps": ["NumericCleanup"] },
      { "column": "BloodPressure",       "steps": ["NumericCleanup"] },
      { "column": "Pulse",               "steps": ["NumericCleanup"] },
      { "column": "DurationOFIllness",   "steps": ["NumericCleanup"] },
      { "column": "DOB",                 "steps": ["DateValidate"] }
    ],
    "service": [
      { "column": "LineClaimedAmount", "steps": ["NullToZero"] },
      { "column": "CoInsurance",       "steps": ["NullToZero"] },
      { "column": "NetAmount",         "steps": ["NullToZero"] }
    ],
    "diagnosis": [
      { "column": "DiagnosisCode", "steps": ["Upper"] }
    ],
    "lab": [
      { "column": "LabProfile",  "steps": [{ "name": "Truncate", "length": 200 }] },
      { "column": "LabTestName", "steps": [{ "name": "Truncate", "length": 30 }] },
      { "column": "LabResult",   "steps": [{ "name": "Truncate", "length": 200 }] },
      { "column": "LabUnits",    "steps": [{ "name": "Truncate", "length": 50 }] },
      { "column": "LabLow",      "steps": [{ "name": "Truncate", "length": 50 }] },
      { "column": "LabHigh",     "steps": [{ "name": "Truncate", "length": 50 }] },
      { "column": "LabSection",  "steps": [{ "name": "Truncate", "length": 50 }] }
    ],
    "radiology": [
      {
        "column": "RadiologyResult",
        "steps": ["RemoveSpecialChars", { "name": "Truncate", "length": 1024 }]
      }
    ]
  },
  "postLoadRules": [
    { "rule": "RecomputeHeaderTotals" },
    { "rule": "NullIfColumnEquals", "target": "header.Height", "value": "0" },
    { "rule": "NullIfColumnEquals", "target": "header.Weight", "value": "0" },
    {
      "rule": "DeleteServicesByCompanyCode",
      "companyCode": "12",
      "condition": "LineClaimedAmount == 0"
    }
  ]
}
```

### 2.2 Tier 2 descriptor (hypothetical vendor with an unusual shape)

Identical shape to Tier 1 except `customSql` references SQL files:

```json
{
  "schemaVersion": 1,
  "providerCode": "EXAMPLE",
  "tier": 2,
  "customSql": {
    "fetchBundleBatch": "custom/fetchBundleBatch.sql",
    "count":            "custom/count.sql",
    "enumerateKeys":    "custom/enumerateKeys.sql"
  },
  "source": { "engine": "sqlserver", "dateColumn": "...", ... },
  "transforms": { ... },
  "postLoadRules": [ ... ]
}
```

The SQL files live alongside the descriptor and are shipped with the agent (Tier 2 is not runtime-editable). The descriptor still drives transforms and post-load rules declaratively.

#### 2.2.1 `skipColumns` (optional, Tier 2 only)

Descriptors may declare a top-level `skipColumns` object to mark destination columns the pipeline intentionally does not populate, preserving parity with legacy SPs that commented them out. Shape:

```json
"skipColumns": {
  "header":  ["OxygenSaturation"],
  "service": ["PatientVatAmount", "ServiceEventType"]
}
```

Keys are entity names from `source.tables`; values are destination column names. The parity reporter (`ParityReporter`) treats these as expected absences, not as missing data. Validation gate 1 permits this property. No runtime effect on the `.sql` files; it exists only for parity reporting and diffing.

### 2.3 Tier 3 descriptor (BilalSoft)

A thin handoff:

```json
{
  "schemaVersion": 1,
  "providerCode": "BILAL",
  "tier": 3,
  "pluginType": "DHSIntegrationAgent.Sync.Vendors.BilalSoft.BilalClaimSource",
  "notes": "Tier 3: view-based source with grouped-header synthesis. See VendorPlaybook.md §BilalSoft."
}
```

The descriptor serves two purposes even for Tier 3:

1. It keeps vendor registration uniform — every vendor is listed in the same directory.
2. It carries site-override capability (e.g., connection-string hints) that a plugin may consume.

---

## 3. Transform catalog

Closed set of named transforms. Every transform implements `ITransformStep.Apply(row, ctx)`. Descriptors reference transforms by name. New patterns that appear in future vendors extend this catalog; descriptors never invent their own.

### 3.1 Catalog

| Name | Parameters | SQL-equivalent behavior |
|---|---|---|
| `Upper` | — | `UPPER(value)` |
| `Lower` | — | `LOWER(value)` |
| `Truncate` | `length` | `LEFT(value, length)` |
| `NullToZero` | — | `ISNULL(value, 0)` for numeric columns |
| `NullToDefault` | `default` | `ISNULL(value, default)` |
| `NullIfEquals` | `value` | `CASE WHEN x = value THEN NULL ELSE x END` |
| `ClampInt` | `min`, `max`, `nullDefault?` | Clamp numeric into `[min, max]`; if `nullDefault` given, map null to that |
| `ReplaceChar` | `find`, `replace` | `REPLACE(value, find, replace)` — replace is a non-null string |
| `RegexReplace` | `pattern`, `replace` | Regex replacement |
| `RemoveSpecialChars` | — | Equivalent to `dbo.RemoveSpecialChars`; specific character class documented inline in the catalog entry |
| `NumericCleanup` | `maxInt?` | Strip non-digit characters; result is null if no digits; clamp to `maxInt` if provided. Equivalent to `dbo.GetNumeric` / `udf_GetNumeric` |
| `DateValidate` | — | Invalid-date string → null; valid date passes through |
| `Ceiling` | — | `Math.Ceiling(value)` |
| `FirstCharMap` | `map{}`, `default` | Map first character of value to a replacement string; if not in map, return `default`. Example: `{"1":"1","2":"2"}` with default `"3"` gives Bilal's `patientidtype` rule |
| `CaseMap` | `map{}`, `default` | Full-value equality map; if not in map, return `default`. Example: `{"CONSULT":2,"Medcine":1}` with default `2` gives Bilal's `TreatmentTypeIndicator` |
| `Constant` | `value` | Returns the constant regardless of input. Reserved tokens `@FromDate` and `@ToDate` resolve to the batch parameters; any other `@`-prefixed token fails validation. Used for hardcoded defaults. |
| `FromHeader` | `column` | Copies a value from the current claim's header DTO into a service-line (or other child) DTO. Supports AFI's `ch.PreAuthId` → `ss.PreAuthId` pattern. Validation: `column` must exist on the header DTO. |
| `FromColumn` | `column` | Renames a column within the same DTO — e.g. AFI's `DiagnosisType ← DiagnosisTypeID` remap. Validation: `column` must exist on the same row being transformed. |
| `CopayPercentage` | `numeratorColumn`, `denominatorColumns`, `guardColumns` | See §3.2 below — this one has a deliberate quirk to preserve. |

### 3.2 CopayPercentage transform — preserving Bilal's SP quirk

Bilal's SP computes `copay` as:

```sql
CASE WHEN (lineclaimedamount = 0
           OR claimedamount - lineitemdiscount = 0)
     THEN 0
     ELSE (coinsurance / (lineclaimedamount - lineitemdiscount)) * 100
END
```

Notice the asymmetry: the **guard** checks `claimedamount - lineitemdiscount`, but the **denominator** uses `lineclaimedamount - lineitemdiscount`. These are not the same column. When `lineclaimedamount == lineitemdiscount` but `claimedamount != lineitemdiscount`, the guard passes and the division is by zero.

This is a bug in the SP, but the new pipeline must preserve it for parity during migration. The `CopayPercentage` transform therefore accepts the guard columns and denominator columns separately:

```json
{
  "column": "copay",
  "steps": [{
    "name": "CopayPercentage",
    "numeratorColumn": "coinsurance",
    "denominatorColumns": ["lineclaimedamount", "lineitemdiscount"],
    "guardColumns": ["lineclaimedamount", {"minus": ["claimedamount", "lineitemdiscount"]}]
  }]
}
```

Implementation:

1. Evaluate each guard expression; if any is zero, return 0.
2. Otherwise evaluate the denominator expression.
3. If the denominator is zero despite the guard passing, return 0 and emit a `ValidationIssue` warning (the parity-preserved path does the division and `/0` raises; we prefer to log and return 0).

The `{"minus": ["a", "b"]}` notation is a small expression language limited to `minus`, `plus`, `mul`, `div` over column references. It avoids a full expression evaluator while covering every arithmetic quirk visible in the three SPs.

This is the one place in the descriptor where expression evaluation appears.

### 3.3 Transform composition

`steps` is an ordered list. Each step receives the output of the previous one. Example from Marshal's radiology:

```json
{ "column": "RadiologyResult",
  "steps": ["RemoveSpecialChars", { "name": "Truncate", "length": 1024 }] }
```

First strip special chars, then truncate. Order matters.

---

## 4. Post-load rule catalog

Named rules applied after all records in a page have been transformed. A rule sees the collection of `ClaimBundle`s for the page plus the context (company code, date range, provider).

| Name | Parameters | Behavior |
|---|---|---|
| `RecomputeHeaderTotals` | — | `header.ClaimedAmount = SUM(services.LineClaimedAmount)`; same pattern for `TotalDiscount`, `TotalDeductible`, `TotalNetAmount`. Always applied; recommended to list explicitly for clarity |
| `NullIfColumnEquals` | `target` (e.g. `header.Height`), `value` | Scoped per-claim version of the global SQL `UPDATE` that sets a column to null when it equals a value |
| `ClampHeaderField` | `field`, `min`, `max`, `nullDefault?` | AFI's `Pulse` / `DurationOFIllness` pattern |
| `DeleteServicesByCompanyCode` | `companyCode`, `condition` (DSL) | Strips services from claims where `header.companyCode == companyCode` and the service satisfies the condition. Condition DSL: simple comparison expressions over service columns (e.g. `LineClaimedAmount == 0`, `LineClaimedAmount <= 0`). No boolean operators needed for current vendors |
| `SetInvestigationResult` | `trueValue`, `falseValue`, `predicate` | Sets `header.InvestigationResult`; predicate DSL: `labsExist`, `radiologyExist`, `labsOrRadiologyExist`. Bilal uses the last one with `trueValue="IRA", falseValue="NA"` |
| `BackfillEmrFields` | `mappings[]` | Per-field EMR back-fill as Bilal does from `#TempEMR`. Mapping: `{ from: "sourceTable.column", to: "header.column", transform?: "TransformName" }`. Combined with `NullIfColumnEquals` for `"0.00" → null` pattern |
| `ForceServiceField` | `field`, `value`, `condition` | Bilal's `PBMDuration = '1' where ServiceGategory IN ('ER','OTHERS')`. Condition DSL supports `column IN [list]` |
| `BackfillDiagnosisCodeFromSource` | `sourceTable`, `joinColumn`, `predicate` | Bilal's SFDA `DiagnosisCode` back-fill from `#TempDiagnosis` via `diagnosis_ser_no` |
| `ForceNull` | `target` (e.g. `header.TriageDate`) | Unconditionally sets the named field to NULL. Preserves Bilal's `REPLACE(col, ' ', NULL)` always-NULL legacy behaviour for `TriageDate` and `CauseOfDeath` (SQL Server returns NULL when `REPLACE`'s third argument is NULL). Removed if Q5c flips to "fix" |
| `BackfillMemberId` | `sourceView`, `joinColumn`, `targetColumn`, `collation?` | Joins the claim header to an auxiliary source view (e.g. Bilal's `dhspatientinformationview`) on `joinColumn` and populates `targetColumn` (typically `header.memberid`). Optional `collation` applied to the join keys. Generalizes Bilal's membership back-fill |

Rules execute in descriptor order. Any rule that fails on a specific claim writes a `ValidationIssue` and allows the pipeline to continue.

---

## 5. Rebind model

Child records (services, diagnoses, labs, radiology, doctors) must be tied back to their parent claim after header processing. Strategy is **per child**, not per vendor — one vendor can use different strategies for different children.

### 5.1 Rebind strategies

| Strategy | Semantics | Used by |
|---|---|---|
| `parentKey` | Join on source parent key column (e.g. `proidclaim`). Simplest case | Marshal all children; AFI doctor |
| `invoice` | Join on `invoiceNumber` with optional collation | — |
| `invoicePatientCompany` | Compound key: `invoiceNumber + patientNo + companyCode` with per-column collation | AFI service/diagnosis/lab/radiology |
| `itemReference` | Join child column `X` to parent column `itemRefernce` | Bilal labs, Bilal radiology |
| `computedGroup` | Join on synthesized `HeaderGroupId` produced by a Tier 3 plugin | Bilal services (via plugin; descriptor has `"strategy": "plugin"`) |

### 5.2 Rebind descriptor shape

```json
"childRebind": [
  {
    "child": "service",
    "strategy": "invoicePatientCompany",
    "parentColumns":  { "invoice": "InvoiceNumber", "patient": "PatientNo", "company": "CompanyCode" },
    "childColumns":   { "invoice": "InvoiceNumber", "patient": "PatientNo", "company": "CompanyCode" },
    "collations": {
      "invoice": "Arabic_CI_AS",
      "patient": "SQL_Latin1_General_CP1_CI_AS",
      "company": "SQL_Latin1_General_CP1_CI_AS"
    }
  },
  {
    "child": "lab",
    "strategy": "itemReference",
    "parentColumn": "itemRefernce",
    "childColumn":  "invoicenumber"
  }
]
```

### 5.3 Collation handling in C#

Rebinding happens in memory against Dapper-materialized DTOs, not in SQL joins. The `collation` field drives a per-column string comparer:

| Descriptor value | C# comparer |
|---|---|
| `"default"` or absent | `StringComparer.Ordinal` |
| `"ordinalIgnoreCase"` | `StringComparer.OrdinalIgnoreCase` |
| `"Arabic_CI_AS"` | A named helper `SaudiNameEquals` — normalizes Arabic diacritics, hamza, ta marbuta, and uses invariant-culture case-insensitive comparison after normalization |
| `"SQL_Latin1_General_CP1_CI_AS"` | `StringComparer.InvariantCultureIgnoreCase` after trim |

The helper implementations live in `Sync/Pipeline/CollationHelpers.cs`. New collations added to the catalog, not invented per descriptor.

---

## 6. Validation gates

`DescriptorValidator` runs at agent startup and whenever a descriptor is added or changed. A descriptor that fails any gate is rejected with a human-readable error; the agent refuses to use it.

### 6.1 Gates

All gates are **dialect-aware** — they branch on the descriptor's `source.engine` field (`sqlserver` or `oracle`).

1. **Schema-level.** JSON validates against `vendor.schema.json`. Type errors, missing required fields, and unknown properties fail here. The `source.engine` enum is required and restricted to `sqlserver` or `oracle` (MySQL / PostgreSQL placeholders in `IDbEngineFactory` are not yet implemented — a descriptor with `engine: mysql|postgres` fails validation with a clear "engine not yet supported" message).

2. **Identifier whitelist.** Every table and column name in `source.tables`, `source.dateColumn`, `source.companyCodeColumn`, `childRebind.*.columns`, and `transforms.*.column` is checked against the engine's metadata view at load time:

    - SQL Server → `INFORMATION_SCHEMA.COLUMNS` / `INFORMATION_SCHEMA.TABLES`.
    - Oracle → `ALL_TAB_COLUMNS` / `ALL_TABLES` (fall back to `USER_*` if the login owns the schema).

    Identifiers are compared case-insensitively on SQL Server and case-sensitively on Oracle (matching Oracle's storage semantics for unquoted identifiers). Unknown identifiers fail.

3. **Identifier format.** Identifiers match `^\[?"?[A-Za-z_][A-Za-z0-9_.]*"?\]?$`. Anything containing other quote characters, semicolons, whitespace, or reserved SQL keywords (dialect-specific reserved-word list) fails. Quoting rules:

    - SQL Server allows bracket quoting `[X]` only when the inner name matches the format regex.
    - Oracle allows double-quoted `"X"` only when the inner name matches and preserves case. Unquoted Oracle identifiers are stored upper-case and the validator normalizes before comparison.

4. **Transform existence.** Every transform `name` resolves to a registered entry in `ITransformRegistry`. Every parameter satisfies that transform's parameter schema. The catalog is minimal under the SQL-aliasing model — most per-row conversions are expressed in the vendor `.sql` file and never touch the registry.

5. **Post-load rule existence.** Same as transforms, against `IPostLoadRuleRegistry`.

6. **Rebind coherence.** Every `child` in `childRebind` exists in `source.tables`. Every column referenced by a rebind strategy exists on its table. Collations are one of the known catalog values. On Oracle, collation clauses are rejected — Oracle uses NLS at the session / column level, not per-comparison clauses, so Oracle vendors set `collation: null` or rely on `SaudiNameEquals` in C# for Arabic normalization.

7. **Tier coherence.** Tier 1 descriptors must not contain `customSql` or `pluginType`. Tier 2 must contain `customSql`. Tier 3 must contain `pluginType` and that type must exist in loaded assemblies. Additionally, vendor `.sql` files under Tier 2 must be named `<entity>.<dialect>.sql` (for example `header.oracle.sql`, `services.sqlserver.sql`); a mismatch between the declared engine and the file extension fails.

7a. **Access-mode coherence.** Each entry under `source.tables` (and `source.attachments`) may declare an optional `accessMode` field with value `"table"` (default) or `"view"`. This is a **per-entity** field, not a top-level flag, because real vendors mix views and base tables (BilalSoft reads from `dhsclaimsdataview` and `dhspatientinformationview`; Excys reads from `DHS.*_V`; a future vendor might pull headers from a view and children from base tables). For entities declaring `accessMode: "view"`, the identifier whitelist (gate 2) additionally consults `ALL_VIEWS` (Oracle) / `INFORMATION_SCHEMA.VIEWS` (SQL Server). An entity that names a physical table but declares `accessMode: "view"` (or vice-versa) emits a **validation warning**, not a hard failure — the implementer may still proceed with the warning acknowledged (covers cases where the vendor maintains a table and a view with the same name, which Oracle permits under separate schemas). Hard failure only occurs when the named object does not exist in either metadata view.

8. **Override coherence.** A SQLite override cannot elevate tier (a Tier 1 override cannot add `customSql`) and cannot change `engine`. Tier elevations and engine changes require a code release.

9. **Dry-run compilation.** The pipeline compiles the descriptor into SQL templates and binding plans without executing them. Compilation errors (for example a transform that references a nonexistent column, or a `.sql` file with a parameter marker that does not match the engine's style — `@name` for SQL Server, `:name` for Oracle) fail the gate.

### 6.2 Validation reporting

Each gate failure reports:

- Gate name
- Descriptor source (file path or SQLite row id)
- Offending path within the JSON (e.g. `transforms.lab[2].steps[0]`)
- Specific error message

Failures are written to the agent's log AND to a `DescriptorValidationIssue` SQLite table for dashboard surfacing. The UI shows a red banner at agent startup if any descriptor failed validation, with a link to the details.

---

## 7. Descriptor lifecycle and override governance

The SQLite override path lets site operators modify descriptors without waiting for a release. This is powerful and therefore needs guardrails.

### 7.1 Extended SQLite schema

The existing `ProviderExtractionConfig` table is replaced by a new `VendorDescriptor` table with richer provenance:

| Column | Type | Purpose |
|---|---|---|
| `ProviderCode` | TEXT PRIMARY KEY | Vendor identifier |
| `DescriptorJson` | TEXT | The JSON document itself |
| `SchemaVersion` | INTEGER | Descriptor schema version (matches `schemaVersion` field inside the JSON) |
| `BaseVersion` | TEXT NULL | Hash of the shipped descriptor this override was derived from; null if no shipped base exists |
| `Checksum` | TEXT | SHA-256 of `DescriptorJson` — detects silent corruption |
| `Provenance` | TEXT | One of `shipped`, `override`, `site_authored` |
| `UpdatedBy` | TEXT | Operator identifier (from `IUserContext`) |
| `UpdatedUtc` | TEXT | ISO-8601 UTC timestamp |
| `LastValidatedUtc` | TEXT NULL | When `DescriptorValidator` last ran on this row |
| `LastValidationResult` | TEXT NULL | `ok` / `failed:<reason>` |
| `IsActive` | INTEGER | 1 = used by pipeline; 0 = paused (override-disable without delete) |

Migration from the existing `ProviderExtractionConfig`:

- For each existing row, synthesize an equivalent Tier 1 descriptor JSON (table-name fields map directly). Store `BaseVersion = null`, `Provenance = 'override'`.
- The legacy `Custom*Sql` columns migrate to Tier 2 descriptor SQL files where non-null, or are dropped if null (see `Plan.md` §3.11).
- Old table is left in place, read-only, for one release as a fallback.

### 7.2 Update semantics

When a shipped descriptor is released for a vendor whose SQLite row already exists:

1. The agent compares shipped descriptor's hash to the site row's `BaseVersion`.
2. If equal, the site is unmodified — replace silently, update `BaseVersion`, re-validate.
3. If different, the site has a local override. The agent does **not** automatically replace; it writes a `DescriptorUpgradeConflict` row surfacing:

- Old base version
- New shipped version
- Current site override version
- A three-way-diff view (base vs site, base vs new, site vs new)

4. An operator resolves the conflict via the UI:

- "Accept shipped" — replace override, keep `BaseVersion` pinned to new.
- "Keep site" — leave override in place, update `BaseVersion` to new.
- "Merge" — operator edits the resulting JSON; validation runs.

This is a lift from standard configuration-management merge semantics applied to descriptors. The goal: no silent loss of site customization on upgrade, no silent divergence from intended behavior.

### 7.3 Authorization

- `UpdatedBy` is populated from the existing `IUserContext` (already used by `BatchRepository` for `CreatedByUserName` etc.).
- The UI exposes descriptor editing only to users with an explicit operator role (role model TBD; at minimum, gated behind an explicit toggle in `AppSettings`).
- Every edit writes an audit row (new `DescriptorAuditLog` table: who, when, old checksum, new checksum). Never deleted.

### 7.4 Rollback

- Every descriptor change is an insert into `DescriptorAuditLog`, so history is available.
- "Revert to previous" UI action replaces the active descriptor with the one identified by a previous audit row's `NewChecksum`.
- Audit trail is append-only. Revert does not delete history; it adds a new row whose content matches an older one.

### 7.5 Related cloud-config cache (not part of VendorDescriptor)

Descriptors describe **structure** (tables, columns, transforms, rules). They do **not** carry connection credentials or per-client runtime tuning; those come from the cloud-config API on login (Plan §4.13) and live in a separate SQLite table so rotation and auditing stay orthogonal to schema changes.

Proposed shape (exact fields pending team API contract — see `DHS_SyncLayer_OpenQuestions.md` Q3):

| Column | Type | Purpose |
| --- | --- | --- |
| `VendorCode` | TEXT PRIMARY KEY | Matches `VendorDescriptor.ProviderCode` |
| `Engine` | TEXT | `sqlserver` / `oracle` — must match the descriptor's engine |
| `ConnectionStringEncrypted` | BLOB | DPAPI-encrypted connection string (host / port / service name / user / pwd) |
| `BatchSize` | INTEGER | Rows per page for extraction; default 100; range 50–500 |
| `IsEnabled` | INTEGER | 1 = agent pulls this vendor; 0 = paused |
| `LastSyncedUtc` | TEXT NULL | Last successful cloud-config fetch |
| `ExtraJson` | TEXT NULL | Forward-compatible bag for fields the API may add |

Operational rules:

- The agent refuses to start extraction for a vendor with no row in this table.
- If the cloud-config API call on login fails, the agent continues with cached values and surfaces a warning banner.
- Secrets rotation: a new `ConnectionStringEncrypted` from the API replaces the cached value in-place; old DPAPI blobs are overwritten.

---

## 8. Tooling

Four tools, built as part of Phase 1 (`Plan.md` §3.11). The tools are not optional — they are what makes the descriptor path operationally safe at scale. A descriptor is only as useful as the confidence that authoring it didn't silently break production.

### 8.1 `DescriptorGenerator`

Connects to a vendor source DB with read-only credentials, introspects `INFORMATION_SCHEMA`, and emits a draft Tier 1 descriptor with obvious column matches pre-filled. The support engineer reviews, fills in transforms and post-load rules, and saves.

- Input: source DB connection + a hint about the integration type (`tables` / `views` — from `ProviderProfile.IntegrationType`).
- Output: valid JSON that passes the schema gate; transform and post-load sections empty for manual completion.
- Does not attempt to guess transforms; that is domain knowledge a human must supply.

### 8.2 `DescriptorTester`

Runs the pipeline against a vendor DB for a small date range without writing to the agent's SQLite or making API calls. Dumps emitted `ClaimBundle` JSON to a local folder for inspection.

- Input: descriptor + source credentials + (provider code, company code, date range).
- Output: folder of `{claimId}.json` files, one per emitted bundle, plus a summary file listing any `ValidationIssue`s.
- PHI policy: output folder is under the agent's local-app-data path and honors `IColumnEncryptor` — files are written encrypted, the UI renders them decrypted in memory only. See `Plan.md` §4 open question #8.

### 8.3 `DescriptorValidator`

The validation engine described in §6. Runs at agent startup, on descriptor edit, and on release upgrade. Exposed as a standalone CLI for CI integration: a PR that adds or edits a shipped descriptor runs the validator against a schema-only dry-run (no DB connection needed for gates 1, 3, 4, 5, 7).

### 8.4 `ParityReporter`

Runs the new pipeline and the legacy `ProviderTablesAdapter` path side-by-side for the same batch and emits a diff.

- Input: batch key + both paths' access credentials.
- Output: per-claim diff report — which fields differ, by how much, with which confidence. Summary statistics at the top: row count parity, financial total parity, attachment count parity.
- Used during migration as the gating signal for flipping feature flags.
- PHI policy: same as `DescriptorTester`.

---

## 9. Appendix — Code-style checklist for the implementation PR

Verbatim from `Plan.md` §3.10:

- [ ] New project `DHSIntegrationAgent.Sync.csproj` targets `net10.0`, `Nullable enable`, `ImplicitUsings enable`.
- [ ] Every public interface has an XML doc comment.
- [ ] Every implementation is `internal sealed` unless it needs to be public.
- [ ] Every DTO is a `sealed record` with init-only properties.
- [ ] Every async method accepts a `CancellationToken`.
- [ ] No inline connection strings, no secrets in files — use `IProviderDbFactory` (already does DPAPI decrypt in-memory).
- [ ] `SyncServiceCollectionExtensions.AddDhsSync(configuration)` is called from `DhsIntegrationAgentServiceCollectionExtensions.AddDhsIntegrationAgent` after `AddDhsAdapters`.
- [ ] WBS tag in class-level comment where the class anchors a WBS deliverable.
- [ ] Tests live in a parallel `tests/DHSIntegrationAgent.Sync.Tests/` project, mirroring the existing `tests/` layout.
- [ ] No global Dapper type handlers except UTC datetime round-trip (see `Plan.md` §3.6).
- [ ] All SQL in embedded `.sql` resource files, not inline strings.
- [ ] `DescriptorValidator` runs as a hosted service at startup and fails the agent if any active descriptor fails validation.

---

## 10. Related documents

- **`DHS_SyncLayer_Plan.md`** — overview, architecture, implementation plan, open questions.
- **`DHS_SyncLayer_VendorPlaybook.md`** — Marshal Tier 1 descriptor (full), AFI Tier 1 descriptor (full), BilalSoft Tier 3 design (grouping, resume, preview, transforms, post-load), migration checklist template.
