# Technical Review of DHS Integration Agent Sync Layer Plan

The proposed transition from the legacy ETL implementation to the new Sync Layer has been thoroughly reviewed. The plan correctly identifies the pain points of the current architecture (linked servers, SP divergence, lack of observability) and proposes a multi-tiered solution.

Below is the comprehensive technical review based on a strict cross-check of the provided documents (`DHS_SyncLayer_Plan.md`, `DHS_SyncLayer_VendorDescriptors.md`, `DHS_SyncLayer_VendorPlaybook.md`, etc.) against the core specification (`README_KB_ZERO_QUESTIONS_SPEC_COMPLETE_v2.md`) and established system constraints.

---

## 1. Core Architecture & Strategic Alignment: Move to Dapper vs Current Implementation?

**Verdict: ARCHITECTURE APPROVED, DAPPER REJECTED.**

Moving to the new Sync Layer architecture (direct vendor connections, SQL aliasing, tiered JSON descriptors) is highly recommended and **superior to the current implementation** (which relies on legacy SPs, linked servers, and on-site DBA staging tables). The decoupling and modularization of vendor logic will significantly improve maintainability and operational safety.

However, the specific plan to use the **Dapper ORM** as the materialization layer must be rejected.

**Why Dapper is rejected and the current materialization code should be kept:**
*   **Memory Overhead & Strong-Typing Limitations:** The current adapter layer (`DHSIntegrationAgent.Adapters`) is explicitly designed to handle highly dynamic HIS vendor schemas by using raw ADO.NET (`DbDataReader`) to read arbitrary database rows directly into `JsonObject` dictionaries. This is highly memory-efficient for dynamic schemas.
*   **Architectural Constraint Violation:** The established system rules explicitly state: *"Do not use ORMs like Dapper for this mapping, as it introduces unnecessary memory overhead and strong-typing limitations."*
*   **Proposed Alternative:** The new Sync Layer pipeline must be refactored to use the **current implementation's `DbDataReader -> JsonObject` pipeline** instead of Dapper's strongly-typed DTO mapping (e.g., `conn.QueryAsync<ClaimHeaderDto>`). The SQL aliasing strategy still works perfectly with `JsonObject` mapping, meaning we get the benefits of the new architecture without the memory overhead of Dapper.

---

## 2. Cross-Check: Schema & Mapping Matrices

I have cross-referenced the canonical `ClaimBundle` contract (`_SCHEMA_AND_MAPPING_INDEX.md` and `README_KB_ZERO_QUESTIONS_SPEC_COMPLETE_v2.md` §11.6, Appendix A) against the mapping matrices in the `DHS_SyncLayer_VendorPlaybook.md`.

### ✅ Consistent Mappings

*   The canonical identity (`ProIdClaim`) is correctly mapped as an integer in the DB and normalized appropriately in the JSON (string for services, int for others).
*   The `RecomputeHeaderTotals` post-load rule correctly captures the required aggregation of `ClaimedAmount`, `TotalDiscount`, `TotalDeductible`, and `TotalNetAmount`.
*   Attachment separation is correctly handled. The canonical `ClaimBundleBuilder` only expects `onlineUrl` populated later, which aligns with the plan's separate `IClaimAttachmentExtractor`.

### ⚠️ Discrepancies & Missing Mappings

1.  **MonthKey Generation:**
    *   *Spec:* `README` §9 states `monthKey` is computed from a date field and stored in SQLite.
    *   *Plan:* The Sync Layer plan does not explicitly mention how or where `monthKey` is computed and injected during the extraction pipeline before landing in SQLite. This must be handled in the pipeline before handing off to local persistence.

2.  **Canonical Injectables (`providerCode`, `bCR_Id`, `provider_dhsCode`):**
    *   *Spec:* `README` §4.3 states `providerCode` and `bCR_Id` are injected at *sending time* (Stream B).
    *   *Plan:* The Sync Layer correctly does not attempt to map these during extraction. However, the system rules dictate that when injecting updated identifiers (`bCR_Id`, `providerCode`, `provider_dhsCode`), existing variations must be explicitly searched, stripped of symbols, and removed from the JSON header first. The new pipeline must ensure it does not accidentally fetch and preserve vendor-side columns that conflict with these injected keys.

3.  **Sanitization Rule (UTF-8):**
    *   *Spec:* `README` §4.2 Step 8 requires sanitizing strings (removing invalid UTF-8).
    *   *Plan:* The transform catalog includes `RemoveSpecialChars` and `NumericCleanup`, but lacks a global "Sanitize UTF-8" step across all strings. This should be added as a generic pre-processing step.

4.  **DateTime UTC Serialization:**
    *   *Constraint:* When serializing DateTime values into canonical claim JSON payloads, `DateTimeOffset` values must be explicitly converted to UTC (e.g., via `.UtcDateTime`) and formatted without a timezone suffix (e.g., `yyyy-MM-ddTHH:mm:ss.fff`).
    *   *Plan:* The plan mentions a global UTC round-trip handler for `DateTime` via Dapper (`Plan.md` §4.6), but since Dapper is rejected, this logic must be explicitly enforced in the `DbDataReader -> JsonObject` conversion layer.

5.  **AFI Pulse / DurationOfIllness Clamps:**
    *   *Matrix:* AFI applies `ClampHeaderField` for Pulse (40-200) and DurationOFIllness (1-365).
    *   *Feedback:* Ensure `ClampHeaderField` does not throw an exception if the clamp fails or receives unexpected data; it should clamp the value and emit a `ValidationIssue` to match the non-blocking staging requirement.

---

## 3. Feedback: System Context & Strictness

### 3.1 Over-engineered Sections

*   **BilalSoft Resume Cursor (Tier 3):** The composite string cursor (`patientno|doctorcode|claimtype|yyyyMMdd`) relies on lexicographic string comparison for pagination on SQL Server (`> @LastSeen`). This can be fragile due to collation differences between C# memory and the SQL engine.
    *   *Recommendation:* If BilalSoft remains Tier 3, ensure the C# `ResumeCursor` generation strictly enforces culture-invariant, ordinal formatting that perfectly matches the SQL Server collation used in the `EnumerateKeys.sql` query.

### 3.2 Insufficiently Strict Sections (Code Generation Risks)

*   **SQL Aliasing (Tier 2):** The plan relies heavily on manually authored `*.sql` files per vendor. While this gives DBAs control, it introduces a massive risk for typo-induced drift between the SQL alias (e.g., `EnconuterTypeID`) and the expected JSON property name.
    *   *Recommendation:* The `DescriptorValidator` (Gate 9 - Dry-run compilation) MUST be enhanced to not just check SQL syntax, but to execute a `SchemaOnly` query (e.g., `SET FMTONLY ON` for SQL Server) and assert that the returned column names *exactly match* the canonical property names defined in the schema.

*   **Descriptor Schema Validation:** The JSON schema (`vendor.schema.json`) must be strictly enforced. For automated code generation, the schema must forbid `additionalProperties` to prevent typo'd config fields from being silently ignored.

### 3.3 Clarity & Operational Safety

*   **Poison Record Handling (§4.8):** The move to per-record isolation (writing a `ValidationIssue` and continuing) is excellent and strictly aligns with the "never block staging" core rule.
*   **Idempotency (§4.9):** The shift from `sp_deletebatch` (replace-all) to `Superseded` incremental staging is correct for the offline-first SQLite model.
*   **Rollback Plan (G1):** The proposed backward migration script (`SourceCursor -> ProIdClaim`) in the Gaps document is well-thought-out.

---

## 4. Conclusion & Actionable Steps

The *logic* and *architecture* of the Sync Layer (decoupling, SQL aliasing, tiered JSON descriptors) are sound and highly recommended. It represents a significant improvement over the current implementation (legacy SPs/Linked Servers). However, the *implementation mechanism* for data mapping (Dapper ORM) must be rejected.

**Required Changes to the Plan before execution:**
1.  **Drop Dapper & Retain Current Mapping Implementation:** Replace all planned usage of Dapper with the current implementation's raw ADO.NET `DbDataReader -> JsonObject` extraction mechanism to comply with memory constraints and maintain efficient dynamic schema handling.
2.  **Add MonthKey Computation:** Explicitly define where `MonthKey` is computed and injected.
3.  **Strengthen Validation:** Ensure `DescriptorValidator` enforces exact alias-to-JSON property matching via schema-only dry runs.
4.  **UTC & Sanitization:** Enforce UTC formatting without timezone suffixes and UTF-8 sanitization at the `JsonObject` generation boundary.