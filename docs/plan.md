# DHS Integration Agent — Sync Layer Implementation Plan

**Date:** 2026-04-19  
**Status:** APPROVED FOR IMPLEMENTATION  
**Replaces:** `IProviderTablesAdapter` / `ProviderTablesAdapter` (hardcoded table-name approach)

---

## 1. Design Principles

| Principle | Decision |
|---|---|
| **No Dapper** | `DbDataReader` is used directly. Dapper's compile-time property binding adds zero value when output is a `JsonObject`. |
| **No hand-authored SQL files on disk** | SQL is built at runtime by `DynamicSqlBuilder` from the column manifest declared in the descriptor. Custom SQL overrides are stored as a blob in the `VendorDescriptor` SQLite row — not in the project directory. |
| **No recompilation for schema changes** | Descriptor JSON is stored in SQLite and updated over the cloud config API. A schema change at the vendor is handled by updating the descriptor — zero release cycle. |
| **Database-agnostic** | `ISqlDialect` abstracts all engine-specific syntax. The pipeline selects the correct dialect at runtime based on `dbEngine` in the descriptor. |
| **Canonical names at the reader, not after** | `DescriptorDrivenReader` resolves source column name → canonical name inside the reader loop. No post-read rename pass is needed. |
| **Schema validation before data flows** | Gates run once per session start (not per batch) and block data flow until the vendor schema matches the manifest. |
| **Post-load rules are declarative** | The `PostLoadRuleEngine` interprets a JSON rule list from the descriptor. No vendor-specific C# code except BilalSoft's `HeaderGrouper`. |

---

## 2. Project Structure

### New Projects

```
src/
  DHSIntegrationAgent.Sync/
    DHSIntegrationAgent.Sync.csproj
    Pipeline/
      IClaimExtractionPipeline.cs       ← replaces IProviderTablesAdapter
      ClaimExtractionPipeline.cs        ← orchestrator
      DescriptorResolver.cs             ← loads descriptor from SQLite / cloud
    Sql/
      ISqlDialect.cs
      SqlServerDialect.cs
      OracleDialect.cs
      MySqlDialect.cs
      DynamicSqlBuilder.cs
    Sources/
      IClaimSource.cs
      StandardClaimSource.cs            ← scenarios 1, 2, 3
      CustomSqlClaimSource.cs           ← scenario 4
      FlatSourceAssembler.cs            ← denormalized view → 7 entity arrays
    Grouping/
      HeaderGrouper.cs                  ← 14-day follow-up grouping
    Validation/
      SchemaValidator.cs
      SchemaValidationResult.cs
    Rules/
      PostLoadRuleEngine.cs
      Rules/                            ← one file per rule type

  DHSIntegrationAgent.Sync.Mapper/
    DHSIntegrationAgent.Sync.Mapper.csproj
    ColumnManifest.cs                   ← manifest model + lookup
    DescriptorDrivenReader.cs           ← DbDataReader → JsonObject with rename
    TypeCoercionMap.cs                  ← canonical field → CLR type mapping
    CanonicalSchema.cs                  ← all canonical field names as constants
```

### Modified Projects

| Project | Change |
|---|---|
| `DHSIntegrationAgent.Workers` | `FetchStageService` ctor: swap `IProviderTablesAdapter` → `IClaimExtractionPipeline` |
| `DHSIntegrationAgent.Adapters` | Retains `ClaimBundleBuilder`, `DbReaderJsonExtensions`. `ProviderTablesAdapter` deprecated in Phase 1, removed in Phase 2. |
| `DHSIntegrationAgent.Persistence` | New migration: `VendorDescriptor` table + `SourceCursor` column on `Claim`. |
| `DHSIntegrationAgent.Bootstrapper` | Register new services; remove `ProviderTablesAdapter` registration. |

### Project References

```
Sync.Mapper  (no project refs — pure logic, only System.Text.Json)
     ↑
   Sync  →  Contracts, Domain, Application, Adapters (for ClaimBundleBuilder)
     ↑
  Workers
```

---

## 3. VendorDescriptor — Full JSON Schema

This is the authoritative shape of the descriptor JSON blob stored in the `VendorDescriptor` SQLite table. Every field is mandatory unless marked `optional`.

```jsonc
{
  // ── Identity ──────────────────────────────────────────────────────────────
  "vendorId": "MARSHAL",                   // matches ProviderDhsCode
  "schemaVersion": "1.0.0",               // SemVer; Gate 1 checks major version
  "dbEngine": "oracle",                    // "sqlserver" | "oracle" | "mysql"

  // ── Topology ──────────────────────────────────────────────────────────────
  // scenario 1: "tableToTable"
  // scenario 2: "viewToTable"
  // scenario 3: "flatView"
  // scenario 4: "customSql"
  "topology": "viewToTable",

  // ── Source configuration ──────────────────────────────────────────────────
  // Used in scenarios 1, 2, 3. Each value is a table or view name
  // (optionally schema-qualified, e.g. "DHS.DHSCLAIM_HEADER_V").
  "sources": {
    "header":    "DHS.DHSCLAIM_HEADER_V",
    "doctor":    "DHS.DHSCLAIM_DOCTOR_V",
    "service":   "DHS.DHSCLAIM_SERVICE_V",
    "diagnosis": "DHS.DHSCLAIM_DIAGNOSIS_V",
    "lab":       "DHS.DHSCLAIM_LAB_V",
    "radiology": "DHS.DHSCLAIM_RADIOLOGY_V",
    "attachment": "DHS.DHSCLAIM_ATTACHMENT_V"
  },

  // ── Custom SQL override (topology = "customSql" only) ─────────────────────
  // One entry per entity. The SQL must produce a result set with columns
  // matching the canonical names declared in the entity's columnManifest.
  // Supports named parameters: :ProIdClaim (Oracle), @ProIdClaim (SQL Server).
  // optional
  "customSql": {
    "header":    "SELECT ... FROM ... WHERE ProIdClaim = :ProIdClaim",
    "service":   "SELECT ... FROM ... WHERE ProIdClaim = :ProIdClaim"
    // omitted entities fall back to DynamicSqlBuilder using sources[entity]
  },

  // ── Filter configuration ──────────────────────────────────────────────────
  "filter": {
    "claimKeyColumn":     "ProIdClaim",        // primary key used in WHERE … IN (…)
    "dateColumn":         "InvoiceDate",       // used for batch date range
    "companyCodeColumn":  "CompanyCode",       // used in COUNT / paging queries
    "dateFilterScope": {
      // "symmetric"  = date >= start AND date <= end  (default, all vendors)
      // "upperOnly"  = date <= end only (Excys doctor asymmetric filter)
      "doctor": "upperOnly"
      // omit entity key to use "symmetric"
    }
  },

  // ── Paging ────────────────────────────────────────────────────────────────
  "paging": {
    "pageSize":       100,        // overrides cloud-config default
    "cursorStrategy": "integer",  // "integer" | "composite"
    // required when cursorStrategy = "composite":
    "compositeKey": ["patientno", "doctorcode", "claimtype", "treatmentfromdate"]
  },

  // ── Column manifests ─────────────────────────────────────────────────────
  // One entry per entity. Key = canonical name (destination). Value = source
  // column name in the vendor DB. The reader resolves source → canonical.
  // Columns listed with null value are declared as skipColumns (not selected).
  "columnManifests": {
    "header": {
      "ProIdClaim":       "PROID_CLAIM",
      "CompanyCode":      "COMPANY_CODE",
      "InvoiceDate":      "INVOICE_DATE",
      "PatientName":      "PATIENT_NAME",
      "MemberID":         "MEMBER_ID",
      "TotalNetAmount":   "NET_AMOUNT",
      "ClaimedAmount":    "CLAIMED_AMOUNT",
      "TotalDiscount":    "DISCOUNT",
      "TotalDeductible":  "DEDUCTIBLE",
      "ClaimType":        "CLAIM_TYPE",
      "SomeOldColumn":    null         // skipColumn — excluded from SELECT
    },
    "doctor": {
      "ProIdClaim":       "PROID_CLAIM",
      "DoctorName":       "DOCTOR_NAME",
      "LicenseNo":        "LICENSE_NO"
    },
    "service": {
      "ProIdClaim":       "PROID_CLAIM",
      "ServiceCode":      "SVC_CODE",
      "ServiceDate":      "SVC_DATE",
      "Quantity":         "QTY",
      "UnitPrice":        "UNIT_PRICE",
      "NetAmount":        "NET_AMT"
    },
    "diagnosis": { /* … */ },
    "lab":       { /* … */ },
    "radiology": { /* … */ },
    "attachment": { /* … */ }
  },

  // ── Flat view decomposition (topology = "flatView" only) ─────────────────
  // Describes how to split rows from a single flat view into 7 entity arrays.
  // Each entity declares which columns belong to it. The assembler groups rows
  // by ProIdClaim, extracts entity sub-objects, and deduplicates header/doctor.
  // optional
  "flatView": {
    "source": "DHS.CLAIMS_FLAT_V",
    "entityColumns": {
      "header":    ["ProIdClaim", "CompanyCode", "InvoiceDate", "PatientName"],
      "doctor":    ["DoctorName", "LicenseNo"],
      "service":   ["ServiceCode", "ServiceDate", "Quantity", "UnitPrice"]
      // …
    }
  },

  // ── Grouping (BilalSoft 14-day follow-up) ─────────────────────────────────
  "grouping": {
    "strategy":      "none",     // "none" | "followupGap"
    "followupDays":  14,         // ignored when strategy = "none"
    "groupKeyColumns": ["PatientNo", "DoctorCode", "ClaimType"],
    "sortColumn":    "TreatmentFromDate"
  },

  // ── Post-load rules ───────────────────────────────────────────────────────
  // Applied in declaration order to the assembled JsonObject bundle.
  "postLoadRules": [
    {
      "rule": "DeleteServicesByCompanyCode",
      "params": { "companyCodes": ["100002"], "zeroAmountOnly": true }
    },
    {
      "rule": "NullIfColumnEquals",
      "params": { "entity": "header", "column": "TriageDate", "ifValue": " " }
    },
    {
      "rule": "RecomputeHeaderTotals",
      "params": {
        "netAmountColumn":    "TotalNetAmount",
        "claimedColumn":      "ClaimedAmount",
        "discountColumn":     "TotalDiscount",
        "deductibleColumn":   "TotalDeductible",
        "serviceNetColumn":   "NetAmount",
        "serviceClaimedColumn": "ClaimedAmount"
      }
    },
    {
      "rule": "ClampHeaderField",
      "params": { "column": "Pulse", "min": 0, "max": 300 }
    },
    {
      "rule": "SetInvestigationResult",
      "params": { "labEntity": "lab", "resultColumn": "Result" }
    }
    // additional rules: BackfillEmrFields, ForceServiceField,
    //   BackfillDiagnosisCodeFromSource, ForceNull, BackfillMemberId
  ],

  // ── Distinct-filter overrides ─────────────────────────────────────────────
  // Some vendors require SELECT DISTINCT on specific entities.
  // optional
  "distinctEntities": ["doctor"],

  // ── Validation gates ──────────────────────────────────────────────────────
  "validationGates": {
    "runOnSessionStart": true,   // default: true
    "runBeforeFirstBatch": true  // redundant with above; belt-and-suspenders
  }
}
```

---

## 4. SQLite Migrations

### Migration M-001: VendorDescriptor table

```sql
CREATE TABLE IF NOT EXISTS VendorDescriptor (
    VendorId          TEXT    NOT NULL PRIMARY KEY,
    SchemaVersion     TEXT    NOT NULL,
    DbEngine          TEXT    NOT NULL,           -- 'sqlserver'|'oracle'|'mysql'
    DescriptorJson    TEXT    NOT NULL,           -- full JSON blob above
    CreatedAt         TEXT    NOT NULL,
    UpdatedAt         TEXT    NOT NULL,
    UpdatedBy         TEXT    NOT NULL,           -- 'cloud' | 'ops:<username>'
    IsActive          INTEGER NOT NULL DEFAULT 1
);
```

### Migration M-002: SourceCursor on Claim

```sql
ALTER TABLE Claim ADD COLUMN SourceCursor TEXT NULL;
-- SourceCursor is the opaque resume point for paged extraction.
-- Integer cursors store the last-seen ProIdClaim as a decimal string.
-- Composite cursors store pipe-delimited key values (see section 9.3).
```

---

## 5. ISqlDialect — Database Abstraction

```csharp
// DHSIntegrationAgent.Sync/Sql/ISqlDialect.cs
public interface ISqlDialect
{
    // Parameter placeholder: "@Name" (SQL Server/MySQL) or ":Name" (Oracle)
    string Param(string name);

    // Paging: generates the WHERE clause fragment for keyset pagination.
    // integerCursor: "AND {keyCol} > @LastSeen"  (SQL Server)
    //                "AND {keyCol} > :LastSeen"   (Oracle)
    string KeysetPage(string keyColumn);

    // TOP / FETCH: limits result set to n rows.
    // SQL Server: "TOP (@PageSize)" before column list
    // Oracle/MySQL: appended as "FETCH FIRST :PageSize ROWS ONLY"
    (string prefix, string suffix) RowLimitClause();

    // Identifier quoting: [col] (SQL Server), "col" (Oracle/PostgreSQL), `col` (MySQL)
    string Quote(string identifier);

    // Null-safe date comparison: GETDATE() | SYSDATE | NOW()
    string CurrentTimestamp();

    // INFORMATION_SCHEMA path for column metadata.
    // SQL Server / MySQL: INFORMATION_SCHEMA.COLUMNS
    // Oracle: ALL_TAB_COLUMNS (different column names)
    string ColumnMetadataQuery(string schema, string table);
}
```

### Implementations

**SqlServerDialect** — uses `@Param`, `TOP (@PageSize)`, `[identifier]`, `GETDATE()`, `INFORMATION_SCHEMA.COLUMNS`  
**OracleDialect** — uses `:Param`, `FETCH FIRST :PageSize ROWS ONLY`, `"identifier"`, `SYSDATE`, `ALL_TAB_COLUMNS`  
**MySqlDialect** — uses `@Param`, `LIMIT @PageSize`, `` `identifier` ``, `NOW()`, `INFORMATION_SCHEMA.COLUMNS`

Dialect is resolved by `ISqlDialectFactory.ForEngine(string dbEngine)` — registered as a keyed singleton per engine string.

---

## 6. DynamicSqlBuilder

`DynamicSqlBuilder` takes a column manifest (source-name → canonical-name map) plus a dialect and produces a valid SELECT statement. It is the only place SQL strings are constructed; no string interpolation appears outside this class.

```csharp
// DHSIntegrationAgent.Sync/Sql/DynamicSqlBuilder.cs
public sealed class DynamicSqlBuilder
{
    private readonly ISqlDialect _dialect;

    // Builds:
    //   SELECT src_col AS canonical, ... FROM {source}
    //   WHERE {filterCol} = {dialect.Param("CompanyCode")}
    //     AND {dateCol} >= {dialect.Param("StartDate")}
    //     AND {dateCol} <= {dialect.Param("EndDate")}
    //     AND {keyCol} IN ({dialect.Param("k0")}, {dialect.Param("k1")}, ...)
    //   [{distinctPrefix}]
    public string BuildEntityQuery(
        string sourceName,
        IReadOnlyDictionary<string, string?> manifest,   // canonical → source (null = skip)
        string keyColumn,
        IReadOnlyList<int> claimKeys,
        bool distinct = false);

    // Builds the header paging query with keyset pagination.
    public string BuildPagedHeaderQuery(
        string sourceName,
        string keyColumn,
        string dateColumn,
        string companyCodeColumn,
        ISqlDialect dialect);

    // Builds the COUNT query.
    public string BuildCountQuery(
        string sourceName,
        string dateColumn,
        string companyCodeColumn);
}
```

**Rules enforced by DynamicSqlBuilder:**
- Columns where manifest value is `null` are excluded from the SELECT list (skipColumns).
- All identifiers are quoted via `dialect.Quote()`.
- Parameter names follow dialect placeholder format.
- `DISTINCT` is prepended to the SELECT column list when `distinct = true`.
- IN-list parameters are positional: `p0`, `p1`, … `pN` to avoid single-parameter reuse.

---

## 7. DescriptorDrivenReader (Sync.Mapper)

`DescriptorDrivenReader` wraps the existing `DbReaderJsonExtensions.ReadRowAsJsonObject()` pattern but resolves column names through the manifest at read time.

```csharp
// DHSIntegrationAgent.Sync.Mapper/DescriptorDrivenReader.cs
public static class DescriptorDrivenReader
{
    // Reads one row into a JsonObject keyed by CANONICAL names.
    // Columns absent from the manifest are dropped (never written to output).
    // Canonical names with null source are never selected, so never appear here.
    public static JsonObject ReadRow(
        DbDataReader reader,
        ColumnManifest manifest,
        TypeCoercionMap typeMap)
    {
        var obj = new JsonObject();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.IsDBNull(i)) continue;     // nulls omitted — ClaimBundleBuilder handles missing keys

            var sourceName  = reader.GetName(i);
            var canonical   = manifest.ResolveCanonical(sourceName);
            if (canonical is null) continue;       // not in manifest → drop

            var clrType     = typeMap.GetTargetType(canonical);
            var value       = reader.GetValue(i);
            obj[canonical]  = CoerceToJsonNode(value, clrType);
        }
        return obj;
    }

    public static async Task<JsonArray> ReadAllRowsAsync(
        DbDataReader reader,
        ColumnManifest manifest,
        TypeCoercionMap typeMap,
        CancellationToken ct)
    {
        var arr = new JsonArray();
        while (await reader.ReadAsync(ct))
            arr.Add(ReadRow(reader, manifest, typeMap));
        return arr;
    }
}
```

### ColumnManifest model

```csharp
// DHSIntegrationAgent.Sync.Mapper/ColumnManifest.cs
public sealed class ColumnManifest
{
    // Internal: source column name (upper-cased) → canonical name
    private readonly Dictionary<string, string> _sourceToCanonical;

    // Resolved from descriptor "columnManifests"[entity]
    // Skips entries where value is null.
    public static ColumnManifest FromDescriptor(
        IDictionary<string, string?> manifestSection);

    // Returns null if source column is not in manifest.
    public string? ResolveCanonical(string sourceColumnName);

    // Returns source column names for SELECT list (non-null values).
    public IReadOnlyList<(string source, string canonical)> Columns { get; }
}
```

### TypeCoercionMap

Canonical field name → expected CLR type. Declared as a static dictionary in `CanonicalSchema.cs`. Replaces the hardcoded switch in `ClaimBundleBuilder`. Coercion is applied in `DescriptorDrivenReader` so by the time data reaches `ClaimBundleBuilder` it is already correctly typed.

```csharp
// DHSIntegrationAgent.Sync.Mapper/TypeCoercionMap.cs
public sealed class TypeCoercionMap
{
    private static readonly Dictionary<string, Type> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProIdClaim"]       = typeof(int),
        ["TotalNetAmount"]   = typeof(decimal),
        ["ClaimedAmount"]    = typeof(decimal),
        ["TotalDiscount"]    = typeof(decimal),
        ["TotalDeductible"]  = typeof(decimal),
        ["InvoiceDate"]      = typeof(DateTime),
        ["Quantity"]         = typeof(decimal),
        ["UnitPrice"]        = typeof(decimal),
        // … all canonical fields
    };

    public Type? GetTargetType(string canonicalName);
}
```

---

## 8. Four Topology Scenarios

### 8.1 Scenario 1 — Table-to-Table (1:1)

**Example vendor:** AFI (SQL Server)

- `topology = "tableToTable"`
- `sources` declares 7 table names.
- `DynamicSqlBuilder.BuildEntityQuery()` is called per entity with that entity's manifest.
- Each entity query is executed sequentially (SQL Server supports independent queries; Oracle does not support `QueryMultiple`).
- Reader: `DescriptorDrivenReader.ReadAllRowsAsync()`.
- Groups results by ProIdClaim key into `Dictionary<int, JsonArray>`.

### 8.2 Scenario 2 — View-to-Table

**Example vendors:** Marshal (Oracle), Excys (Oracle)

- `topology = "viewToTable"`
- `sources` entries point to vendor views (`*_V`).
- Processing is identical to Scenario 1 at the pipeline level.
- The view absorbs internal complexity (joins, renames, expressions).
- **Oracle-specific:** Each entity query is issued as a separate `DbCommand`; no attempt is made to use `QueryMultiple` (which Oracle does not support).

### 8.3 Scenario 3 — Denormalized Flat View

**When used:** Vendor exposes a single view where each row contains all entities' data in a flat structure (e.g., one row per service line repeating header columns).

- `topology = "flatView"`
- `flatView.source` names the single view.
- `flatView.entityColumns` maps entity name → list of canonical column names that belong to it.
- `FlatSourceAssembler` reads the view once, groups rows by `ProIdClaim`, then for each group:
  - Extracts the `header` sub-object from the first row (no duplication).
  - Extracts the `doctor` sub-object from the first row where doctor columns are non-null.
  - Accumulates `service`, `diagnosis`, `lab`, etc. arrays across all rows.
- Output: standard `ProviderClaimBundleRaw` identical to Scenarios 1 and 2.

```csharp
// DHSIntegrationAgent.Sync/Sources/FlatSourceAssembler.cs
public sealed class FlatSourceAssembler
{
    public async Task<IReadOnlyList<ProviderClaimBundleRaw>> AssembleAsync(
        DbConnection conn,
        VendorDescriptor descriptor,
        ColumnManifest flatManifest,
        IReadOnlyList<int> claimKeys,
        TypeCoercionMap typeMap,
        ISqlDialect dialect,
        CancellationToken ct);
}
```

### 8.4 Scenario 4 — Custom SQL

**When used:** No well-structured tables or views; vendor provides custom SQL or a stored procedure call.

- `topology = "customSql"`
- `customSql` object contains one SQL string per entity.
- `CustomSqlClaimSource` substitutes the claim key parameter and executes the SQL as-is.
- Each custom SQL statement is validated at session start via Gate 9 (dry-run `WHERE 1=0` + `GetSchemaTable()` to confirm column presence).
- Entities omitted from `customSql` fall back to `DynamicSqlBuilder` using `sources[entity]`.

---

## 9. IClaimExtractionPipeline — The Replacement Seam

This interface replaces `IProviderTablesAdapter` as the injection point in `FetchStageService`.

```csharp
// DHSIntegrationAgent.Sync/Pipeline/IClaimExtractionPipeline.cs
public interface IClaimExtractionPipeline
{
    // Equivalent to CountClaimsAsync — used for pre-batch dialog.
    Task<int> CountClaimsAsync(
        string providerDhsCode, string companyCode,
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct);

    // Equivalent to ListClaimKeysAsync but returns ResumeCursor instead of int?
    Task<ClaimPage> GetNextPageAsync(
        string providerDhsCode, string companyCode,
        DateTimeOffset start, DateTimeOffset end,
        int pageSize, ResumeCursor? cursor, CancellationToken ct);

    // Fetches full bundles for a page of claim keys.
    Task<IReadOnlyList<ProviderClaimBundleRaw>> FetchBundlesAsync(
        string providerDhsCode,
        IReadOnlyList<int> claimKeys,
        CancellationToken ct);

    // For domain scanning (unchanged semantics).
    Task<IReadOnlyList<ScannedDomainValue>> GetDistinctDomainValuesAsync(
        string providerDhsCode, string companyCode,
        DateTimeOffset start, DateTimeOffset end,
        IReadOnlyList<BaselineDomain> domains, CancellationToken ct);

    // Validates vendor schema against descriptor manifest. Throws on mismatch.
    Task ValidateSchemaAsync(string providerDhsCode, CancellationToken ct);
}
```

### ResumeCursor

```csharp
// DHSIntegrationAgent.Sync/Pipeline/ResumeCursor.cs
public sealed record ResumeCursor(string Value)
{
    // Integer strategy: "42157" (last seen ProIdClaim as string)
    public static ResumeCursor FromInt(int lastSeen) => new(lastSeen.ToString());

    // Composite strategy: pipe-delimited key values
    // Fields in compositeKey order, pipe-delimited.
    public static ResumeCursor FromComposite(params string[] parts) =>
        new(string.Join("|", parts));

    public int AsInt() => int.Parse(Value);

    public string[] AsComposite() => Value.Split('|');
}

public sealed record ClaimPage(
    IReadOnlyList<int> ClaimKeys,
    ResumeCursor? NextCursor,  // null when last page
    bool IsLastPage);
```

**`FetchStageService` change:** Replace the `IProviderTablesAdapter _tablesAdapter` field with `IClaimExtractionPipeline _pipeline`. All internal calls to `_tablesAdapter.*` are updated to their `_pipeline.*` equivalents. The `SourceCursor` column on the `Claim` row is persisted after each page so that interrupted batches resume mid-stream.

---

## 10. Schema Validation Gates

Validation runs once per session start (before the first batch executes). Results are cached per `providerDhsCode` for the session lifetime. A validation failure sets batch status to `Failed` with a structured error and blocks all further extraction for that provider.

### Gate matrix

| Gate | Topologies | Method |
|---|---|---|
| G1 — Descriptor version | All | Parse `schemaVersion`; reject if major version mismatch |
| G2 — Engine reachability | All | Open connection, execute `SELECT 1` |
| G3 — Source existence (tables/views) | 1, 2 | Query `INFORMATION_SCHEMA.TABLES` / `ALL_OBJECTS` |
| G4 — Column presence per entity | 1, 2 | Compare `INFORMATION_SCHEMA.COLUMNS` / `ALL_TAB_COLUMNS` against manifest |
| G5 — Column type compatibility | 1, 2 | Warn (not block) if source column type differs from coercion target |
| G6 — Flat view source existence | 3 | As G3 for `flatView.source` |
| G7 — Flat view column presence | 3 | As G4 for `flatView.source` |
| G8 — Custom SQL dry-run | 4 | Execute each `customSql` with `WHERE 1=0`; call `GetSchemaTable()`; verify canonical columns present |
| G9 — Post-load rule params | All | Validate rule JSON schema; confirm referenced columns exist in manifest |

```csharp
// DHSIntegrationAgent.Sync/Validation/SchemaValidator.cs
public sealed class SchemaValidator
{
    public async Task<SchemaValidationResult> ValidateAsync(
        DbConnection conn,
        VendorDescriptor descriptor,
        ISqlDialect dialect,
        CancellationToken ct);
}

public sealed record SchemaValidationResult(
    bool IsValid,
    IReadOnlyList<SchemaValidationIssue> Issues);

public sealed record SchemaValidationIssue(
    string Gate,
    string Entity,
    string Column,      // empty string if issue is at entity level
    string Severity,    // "Error" | "Warning"
    string Message);
```

---

## 11. HeaderGrouper — 14-Day Follow-up Logic

`HeaderGrouper` is a pure static class. It is activated only when `descriptor.grouping.strategy == "followupGap"`. When strategy is `"none"`, the pipeline passes claim bundles through unchanged.

```csharp
// DHSIntegrationAgent.Sync/Grouping/HeaderGrouper.cs
public static class HeaderGrouper
{
    // Takes a flat list of claim bundles sorted by groupKeyColumns + sortColumn.
    // Groups consecutive rows where the gap between sortColumn dates is <= followupDays.
    // Returns new claim bundles where:
    //   - The first bundle in each group keeps its ProIdClaim.
    //   - Subsequent bundles in the group are merged into the first:
    //       serviceDetails, diagnosisDetails, labDetails, radiologyDetails are concatenated.
    //       claimHeader fields are taken from the group-leader row.
    // This is a pure transformation — no DB access.
    public static IReadOnlyList<ProviderClaimBundleRaw> Group(
        IReadOnlyList<ProviderClaimBundleRaw> bundles,
        GroupingConfig config);
}

public sealed record GroupingConfig(
    string[] GroupKeyColumns,
    string SortColumn,
    int FollowupDays);
```

**BilalSoft composite cursor interaction:** Because grouping merges multiple rows, the resume cursor must advance past all rows in the last emitted group. The pipeline calculates the max `sortColumn` value in the last group and stores that as the cursor anchor. The next page query starts after that date.

---

## 12. Post-Load Rule Engine

```csharp
// DHSIntegrationAgent.Sync/Rules/PostLoadRuleEngine.cs
public sealed class PostLoadRuleEngine
{
    // Applies all rules in descriptor.postLoadRules to the assembled bundle.
    // Rules are applied in declaration order.
    // Returns the modified bundle (JsonObject is mutated in place).
    public ProviderClaimBundleRaw Apply(
        ProviderClaimBundleRaw bundle,
        IReadOnlyList<PostLoadRule> rules);
}
```

Each rule is a class implementing `IPostLoadRule`:

```csharp
public interface IPostLoadRule
{
    string RuleName { get; }  // matches descriptor "rule" field
    void Apply(ProviderClaimBundleRaw bundle, JsonObject ruleParams);
}
```

### Rule implementations (one file each under `Rules/`)

| Rule name | Action |
|---|---|
| `DeleteServicesByCompanyCode` | Remove rows from `serviceDetails` where `CompanyCode` matches params list; optionally only when `NetAmount == 0`. |
| `NullIfColumnEquals` | Set `entity.column = null` when its current string value equals `params.ifValue`. |
| `RecomputeHeaderTotals` | Sum `NetAmount` over `serviceDetails` and write result into `claimHeader.TotalNetAmount` etc. |
| `ClampHeaderField` | Set `claimHeader.column = null` if value < min or > max. |
| `SetInvestigationResult` | Derive `Result` field in `lab` rows from source logic. |
| `BackfillEmrFields` | Copy EMR identifier fields from header into each service row. |
| `ForceServiceField` | Set a fixed value on all service rows (e.g. force `InvestigationType = "Lab"`). |
| `BackfillDiagnosisCodeFromSource` | Copy primary diagnosis code from header into each diagnosis row when absent. |
| `ForceNull` | Set a named field to null unconditionally (handles Excys/BilalSoft quirks). |
| `BackfillMemberId` | Derive member ID from patient ID when member ID is absent. |

---

## 13. Cloud Config Integration Flow

The descriptor JSON is delivered as part of the existing provider configuration API response. No new endpoint is needed.

```
Client login
  └─ GET /api/Provider/GetProviderConfigration/{providerDhsCode}
       └─ payload includes { ..., "vendorDescriptor": { <full descriptor JSON> } }
            └─ LoginViewModel.cs caches response to SQLite
                 └─ Migration M-001 row: INSERT OR REPLACE INTO VendorDescriptor
```

### DescriptorResolver

```csharp
// DHSIntegrationAgent.Sync/Pipeline/DescriptorResolver.cs
public sealed class DescriptorResolver
{
    // 1. Read VendorDescriptor row from SQLite for providerDhsCode.
    // 2. Deserialize DescriptorJson into VendorDescriptor model.
    // 3. Cache in memory for session lifetime (ConcurrentDictionary).
    // 4. Throw DescriptorNotFoundException if row absent (forces re-login).
    public async Task<VendorDescriptor> ResolveAsync(
        string providerDhsCode, CancellationToken ct);
}
```

The cloud-config API payload must add the `vendorDescriptor` field. The `LoginViewModel` persistence path already handles arbitrary JSON fields from the payload — adding `vendorDescriptor` requires only that `VendorDescriptor` is written to the new SQLite table during the login flow.

---

## 14. FetchStageService Changes (Workers layer)

Only the constructor and call sites change. The batch status lifecycle, BcrId creation, domain scanning, and dispatch handoff are unchanged.

```csharp
// Before:
private readonly IProviderTablesAdapter _tablesAdapter;

// After:
private readonly IClaimExtractionPipeline _pipeline;
```

Key method replacements:

| Old call | New call |
|---|---|
| `_tablesAdapter.CountClaimsAsync(...)` | `_pipeline.CountClaimsAsync(...)` |
| `_tablesAdapter.ListClaimKeysAsync(..., lastSeenClaimKey)` | `_pipeline.GetNextPageAsync(..., ResumeCursor? cursor)` |
| `_tablesAdapter.GetClaimBundlesRawBatchAsync(...)` | `_pipeline.FetchBundlesAsync(...)` |
| `_tablesAdapter.GetDistinctDomainValuesAsync(...)` | `_pipeline.GetDistinctDomainValuesAsync(...)` |

**New call added:** `await _pipeline.ValidateSchemaAsync(providerDhsCode, ct)` — called once at the start of `ProcessBatchAsync` when `batch.Status == Draft`. Result is written to a new `ValidationIssue` SQLite table; on failure the batch transitions to `Failed` with the structured issue list.

**Cursor persistence:** After each page, `FetchStageService` writes `ResumeCursor.Value` to `Claim.SourceCursor`. On recovery, the cursor is read back and passed to `GetNextPageAsync`.

---

## 15. Bootstrapper Registration

```csharp
// DHSIntegrationAgent.Bootstrapper — AddDhsIntegrationAgent()
services.AddSingleton<ISqlDialectFactory, SqlDialectFactory>();
services.AddSingleton<DynamicSqlBuilder>();
services.AddSingleton<TypeCoercionMap>();
services.AddSingleton<SchemaValidator>();
services.AddSingleton<PostLoadRuleEngine>();
services.AddSingleton<HeaderGrouper>();    // static class; registration optional
services.AddSingleton<DescriptorResolver>();
services.AddSingleton<FlatSourceAssembler>();

// Scoped pipeline — one instance per extraction session
services.AddScoped<IClaimExtractionPipeline, ClaimExtractionPipeline>();

// Remove:
// services.AddSingleton<IProviderTablesAdapter, ProviderTablesAdapter>();
```

---

## 16. Rollout Phases

### Phase 0 — Security fix + migrations (no behavior change)

- Fix `ProviderConfigurationService.cs:386-387` (remove hardcoded `Password=root`).
- Apply migration M-001 (`VendorDescriptor` table).
- Apply migration M-002 (`Claim.SourceCursor` column).
- `VendorDescriptor` table is seeded but pipeline is not yet wired.

**Deliverable:** Clean build, all existing tests pass.

### Phase 1 — Framework + AFI (SQL Server, Scenario 1)

- Implement `ISqlDialect` + `SqlServerDialect`.
- Implement `DynamicSqlBuilder` (SQL Server path only).
- Implement `ColumnManifest`, `DescriptorDrivenReader`, `TypeCoercionMap`.
- Implement `IClaimExtractionPipeline` + `ClaimExtractionPipeline`.
- Implement `ResumeCursor` (integer strategy).
- Implement `SchemaValidator` Gates G1–G5.
- Implement `PostLoadRuleEngine` with full rule set.
- Wire `IClaimExtractionPipeline` into `FetchStageService`.
- **Remove** `IProviderTablesAdapter` from DI; keep class file (deprecated, not deleted yet).
- AFI descriptor JSON authored and seeded.
- Integration test: full fetch cycle against AFI SQL Server fixture.

**Deliverable:** AFI extraction working end-to-end through new pipeline.

### Phase 2 — Marshal (Oracle, Scenario 2)

- Implement `OracleDialect`.
- Implement `DynamicSqlBuilder` Oracle path (`:Param`, `FETCH FIRST`).
- Implement `SchemaValidator` Oracle metadata path (G3/G4 via `ALL_TAB_COLUMNS`).
- Oracle-specific: sequential entity queries (no `QueryMultiple`).
- Marshal descriptor JSON authored and seeded.
- Integration test: full fetch cycle against Marshal Oracle fixture.

**Deliverable:** Marshal extraction working.

### Phase 3 — Excys (Oracle, Scenario 2 with asymmetric date filter)

- Extend `filter.dateFilterScope` support in `DynamicSqlBuilder`.
- `"doctor": "upperOnly"` means `date <= end` only (no lower bound).
- `distinctEntities: ["doctor"]` support in `DynamicSqlBuilder`.
- Excys descriptor JSON authored and seeded (views `*_V`, zero-amount service cleanup rule).
- Integration test.

**Deliverable:** Excys extraction working.

### Phase 4 — BilalSoft (SQL Server, Scenario 1 + Grouping)

- Implement `ResumeCursor` composite strategy.
- Implement `HeaderGrouper` with 14-day gap logic.
- Implement `SchemaValidator` Gate G9 (post-load rule params).
- BilalSoft descriptor JSON authored and seeded (grouping enabled, bug-fix rules).
- Integration test including grouping.

**Deliverable:** BilalSoft extraction working with grouping.

### Phase 5 — MySQL support

- Implement `MySqlDialect`.
- Implement `DynamicSqlBuilder` MySQL path (`LIMIT`, backtick quoting).
- No current vendor uses MySQL; this is readiness for future onboarding.

### Phase 6 — Flat view + Custom SQL

- Implement `FlatSourceAssembler` (Scenario 3).
- Implement `CustomSqlClaimSource` + Gate G8 dry-run (Scenario 4).
- Delete `ProviderTablesAdapter.cs` and `IProviderTablesAdapter.cs`.
- Remove deprecated registration.

---

## 17. Testing Strategy

### Unit tests (DHSIntegrationAgent.Tests.Unit)

| Component | What to test |
|---|---|
| `DynamicSqlBuilder` | SQL output matches expected string per dialect; skipColumns excluded; DISTINCT prefix; parameter names correct |
| `ColumnManifest` | Lookup returns canonical; null-value entries excluded from Columns |
| `DescriptorDrivenReader` | Source columns map to canonical names; null rows omitted; unknown columns dropped |
| `TypeCoercionMap` | All canonical fields map to expected CLR type |
| `HeaderGrouper` | Groups by 14-day gap; single-visit claims pass through unchanged; gap boundary: 14 days = same group, 15 days = new group |
| `PostLoadRuleEngine` | Each rule: apply to fixture bundle, assert mutation |
| `SchemaValidationResult` | IsValid false when any Error-severity issue present |

### Integration tests (DHSIntegrationAgent.Tests.Integration)

- SQL Server fixture: in-memory SQL LocalDB with AFI schema seeded.
- Oracle fixture: Testcontainers Oracle image (or mock via `FakeDbConnection`).
- Per-vendor: seed descriptor + data → run `IClaimExtractionPipeline` → assert `ProviderClaimBundleRaw` shape.
- Schema drift: alter fixture table (drop column) → assert Gate G4 raises `SchemaValidationIssue`.
- Grouping: seed BilalSoft rows at 3-day, 14-day, 15-day gaps → assert correct group boundaries.

---

## 18. Key Invariants — Do Not Break

1. `ClaimBundleBuilder` receives `ProviderClaimBundleRaw` with canonical names. It must not be changed to accept source names.
2. `serviceDetails.proidclaim` is a **string** in the canonical bundle. `DescriptorDrivenReader` must coerce it to string for the service entity even though `TypeCoercionMap` maps `ProIdClaim → int` for header/other entities. This special case is declared in `CanonicalSchema.cs` as `ServiceProIdClaimIsString = true`.
3. `provider_dhsCode` and `bCR_Id` are **not** injected by the pipeline. They are injected immediately before `SendClaim` in Workers StreamB/StreamC — this contract is unchanged.
4. Connection strings are never stored locally. The pipeline reads connection strings from the SQLite-cached `ProviderConfigurationSnapshot`. The `VendorDescriptor` row contains schema/mapping metadata only — no credentials.
5. Oracle does not support `QueryMultiple`. All Oracle entity fetches must be separate `DbCommand` executions within the same open `DbConnection`.
