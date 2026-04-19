# Destination Schema and Mapping

## 1. Destination schema — `[DHS-NPHIES]`

All four vendors land into the same local SQL Server database on the agent host. Central invariant: `DHSClaim_Header.ProIdClaim` (int) is the claim identity; every child carries `ProIdClaim` as FK. Identifier convention: SQL names (underscored) in SPs / mapping matrices / `.sql` files; EF names (PascalCase) in DTOs — EF handles the conversion.

Source of truth: [`NPHIES_DB_Documentation (1).md`](NPHIES_DB_Documentation%20%281%29.md). §2 of [`DHS_SyncLayer_Plan.md`](DHS_SyncLayer_Plan.md) is a normalized copy with cardinality and `ClaimBundle` contract.

### 1.1 `DHSClaim_Header` / `DhsclaimHeader` (parent)

PK: `ProIdClaim` (int).

| Column | Type | Notes |
|---|---|---|
| `ProIdClaim` | int | PK, identity |
| `DoctorCode` | string | joins to `DhsDoctor.DoctorCode` |
| `PatientName` | string | |
| `PatientNo` | string | |
| `MemberId` | string | insurance member id |
| `ClaimType` | string | |
| `ClaimedAmount` | decimal | recomputed post-load (`RecomputeHeaderTotals`) |
| `InvoiceNumber` | string | used for child-parent rebinding |
| `InvoiceDate` | datetime | |
| `BatchNo` | string | |
| `BatchStartDate` | datetime | primary date filter column |
| `BatchEndDate` | datetime | |
| `IsFetched` | bool | agent-side sync flag |
| `IsSync` | bool | agent-side sync flag |

Plus the full column set defined in `NPHIES_DB_Documentation (1).md` lines 17–33 and extended in the per-vendor mapping matrices (e.g. `TotalDiscount`, `TotalDeductible`, `TotalNetAmount`, `Height`, `Weight`, `Pulse`, `Nationality`, admission/discharge fields, encounter/triage/emergency fields).

### 1.2 Child tables

| Table | PK | FK | Notes |
|---|---|---|---|
| `DHSService_Details` / `DhsserviceDetail` | `ServiceId` | `ProIdClaim` | line items; key columns `ServiceCode`, `ServiceDescription`, `LineClaimedAmount`, `NetAmount`, plus `LineItemDiscount`, `CoInsurance`, `PatientVatAmount`, PBM fields |
| `DHSDiagnosis_Details` / `DhsdiagnosisDetail` | `DiagnosisId` | `ProIdClaim` | `DiagnosisCode`, `DiagnosisDesc`, `DiagnosisTypeID`, `IllnessTypeIndicator` |
| `DHSLab_Details` / `DhslabDetail` | `LabId` | `ProIdClaim` | `LabTestName`, `LabResult`, `VisitDate`, `LabProfile`, `LabUnits`, `LabLow`, `LabHigh`, `LabSection` |
| `DHSRadiology_Details` / `DhsradiologyDetail` | `RadiologyId` | `ProIdClaim` | `ServiceCode`, `RadiologyResult`, `VisitDate`, `ClinicalData` |
| `DHS_Attachment` / `DhsAttachment` | `AttachmentId` | `ProIdClaim` | `Location`, `AttachmentType`, `ContentType`, `FileSizeInByte` — uploaded via separate pipeline, not embedded in `ClaimBundle` |
| `DHS_ACHI` / `DhsAchi` | `Achiid` | `ProIdClaim` | `Achicode` |
| `DHS_Doctor` / `DhsDoctor` | `DocId` | — | `DoctorName`, `Specialty`, `DoctorCode` — rejoined to header via `DoctorCode` |
| `DHS_OpticalVitalSigns` / `DhsOpticalVitalSign` | `SerialNo` | `ProIdClaim` | `RightEyeSphereDistance`, `LeftEyeSphereDistance`, plus full optical measurement set |
| `DHS_ItemDetails` / `DhsItemDetail` | `SerialNo` | `FkOpticSerialNo` → `DhsOpticalVitalSign.SerialNo` | grandchild |

### 1.3 Cardinality

```
DhsclaimHeader (1)
 ├── DhsAchi (N)
 ├── DhsAttachment (N)                  separate upload pipeline
 ├── DhsdiagnosisDetail (N)
 ├── DhslabDetail (N)
 ├── DhsradiologyDetail (N)
 ├── DhsserviceDetail (N)
 └── DhsOpticalVitalSign (N)
       └── DhsItemDetail (N)
```

### 1.4 `ClaimBundle` output contract

The pipeline emits a `ClaimBundle` JSON per claim and dispatches it to the backend API.

| Destination table | `ClaimBundle` section | Shape |
|---|---|---|
| `DhsclaimHeader` | `claimHeader` | JsonObject |
| `DhsserviceDetail` | `serviceDetails` | JsonArray, `proidclaim` emitted as string |
| `DhsdiagnosisDetail` | `diagnosisDetails` | JsonArray |
| `DhslabDetail` | `labDetails` | JsonArray |
| `DhsradiologyDetail` | `radiologyDetails` | JsonArray |
| `DhsAchi` | `achi` | JsonArray |
| `DhsDoctor` | `doctors` | JsonArray |
| `DhsOpticalVitalSign` + `DhsItemDetail` | `opticalVitalSigns` | JsonArray with nested items |
| `DhsAttachment` | — | not in bundle; uploaded separately |

Full contract in [`DHS_SyncLayer_Plan.md` §2.4](DHS_SyncLayer_Plan.md).

---

## 2. Mapping strategy

### 2.1 How each vendor is mapped

Every vendor produces the same `ClaimBundle` contract from a different source shape. Mapping is expressed in three artifacts per vendor:

1. **Mapping Matrix** (markdown) — per-column source → destination rows with transforms, filters, post-load effects. Parity reference against the legacy SP.
2. **Descriptor JSON** (in the Playbook) — the runtime artifact the pipeline executes. Names transforms and post-load rules from a shared catalog.
3. **Vendor `.sql` files** (authored during Phase 2+) — one per entity (`header.oracle.sql`, `services.sqlserver.sql`, …). Perform column aliasing inside the SELECT so most name/type drift collapses at the query layer.

Pipeline run: Count → EnumerateKeys → FetchBatch (via `.sql`) → Transform (from catalog) → ChildRebind → PostLoadRules → Emit `ClaimBundle`. Defined in [`DHS_SyncLayer_Plan.md` §3–§4](DHS_SyncLayer_Plan.md).

Shared catalog (the only legal names a descriptor may reference — validation gates 4 and 5 reject everything else):

| Catalog | Location | Entries |
|---|---|---|
| Transform catalog | [`VendorDescriptors.md` §3.1](DHS_SyncLayer_VendorDescriptors.md) | `Upper`, `Lower`, `Truncate`, `NullToZero`, `NullToDefault`, `NullIfEquals`, `ClampInt`, `ReplaceChar`, `RegexReplace`, `RemoveSpecialChars`, `NumericCleanup`, `DateValidate`, `Ceiling`, `FirstCharMap`, `CaseMap`, `Constant`, `FromHeader`, `FromColumn`, `CopayPercentage` |
| Post-load rule catalog | [`VendorDescriptors.md` §4](DHS_SyncLayer_VendorDescriptors.md) | `RecomputeHeaderTotals`, `DeleteServicesByCompanyCode`, `NullIfEqualsOnHeader`, EMR back-fill, member-id match, SFDA DiagnosisCode back-fill |
| Validation gates | [`VendorDescriptors.md` §6.1](DHS_SyncLayer_VendorDescriptors.md) | 9 gates: schema, identifier whitelist, identifier format, transform existence, rule existence, rebind coherence, tier coherence, access-mode coherence (7a, per-entity), override coherence, dry-run compilation |

### 2.2 Per-vendor location and status

Per-vendor content is uniformly structured in [`DHS_SyncLayer_VendorPlaybook.md`](DHS_SyncLayer_VendorPlaybook.md). Each section carries: `X.1 Source`, `X.2 Mapping Matrix`, `X.3 Descriptor`, and for Excys also `X.4 Onboarding status`. Raw legacy SPs are under [`PLAN/VEND/<vendor>/`](VEND/).

| Vendor | Playbook § | Lines | Tier | Source topology | Outstanding |
|---|---|---|---|---|---|
| Marshal | §1 | 17–188 | 2 | Oracle, schema `NAFD`, base tables, 4-part linked-server legacy | — |
| AFI | §2 | 190–371 | 2 | SQL Server, same-server `DHSA.dbo.*`, tables | — |
| BilalSoft | §3 | 373–632 | 2 or 3 (Q1 open) | SQL Server linked server `BILAL.medical_net.*`, views, grouped headers (`LAG()` + 14-day gap) | Q1 — Tier 2 (CTE + `LAG()` in SQL) vs Tier 3 (C# grouper) |
| Excys | §4 | 634–817 | 2 | Oracle, schema `DHS`, views `*_V`, `OPENQUERY` legacy | Q13 — views vs base tables; default = views to preserve legacy-SP parity |

---

## 3. File map

```
PLAN/
├── _SCHEMA_AND_MAPPING_INDEX.md        this file
├── NPHIES_DB_Documentation (1).md      authoritative destination schema (EF)
├── DHS_SyncLayer_Plan.md               main plan; §2 schema summary, §3–4 pipeline
├── DHS_SyncLayer_VendorPlaybook.md     per-vendor specs (§1 Marshal, §2 AFI, §3 BilalSoft, §4 Excys)
├── DHS_SyncLayer_VendorDescriptors.md  catalog (transforms §3, rules §4) + gates §6.1
├── DHS_SyncLayer_OpenQuestions.md      Q1–Q15 (Q4 closed) with phase-blocker table
├── DHS_SyncLayer_GapsForReview.md      gap analysis
└── VEND/{MARSHAL,AFI,BILAL,exeys}/     legacy SP + mapping doc per vendor
```
