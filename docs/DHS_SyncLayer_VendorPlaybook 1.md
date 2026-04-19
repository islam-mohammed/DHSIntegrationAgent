# DHS Integration Agent — Vendor Playbook

**Document 3 of 3** in the Sync Layer planning set:

- `DHS_SyncLayer_Plan.md` — overview, architecture, implementation plan
- `DHS_SyncLayer_VendorDescriptors.md` — descriptor model, transform catalog, governance
- **This document** — per-vendor specs and migration checklists

This document is implementer-facing. It contains the concrete descriptor JSON for each vendor, the algorithmic notes for BilalSoft (tier pending), the full Excys spec (delivered 2026-04-18, reads Oracle views `DHS.*_V`), and a repeatable onboarding checklist.

**Scope change (2026-04-17):** the four in-scope vendors are **Marshal (Oracle)**, **Excys (Oracle)**, **AFI (SQL Server)** and **BilalSoft (SQL Server)**. The `USP_FetchClaims` stored procedures documented in `PLAN/VEND/*/USP_FetchClaims.sql` are the **legacy** ETL — they run on the SQL Server destination and reach vendor sources through linked servers (Marshal / BilalSoft) or same-server references (AFI).

The new pipeline connects **directly** to each vendor's source DB; the SPs remain only as behaviour documentation.

---

## 1. Marshal (Tier 2, Oracle)

### 1.1 Source

- Engine: **Oracle** (confirmed by the team)
- Legacy access: SQL Server linked server `DHS..[NAFD]` — **removed** in the new pipeline; agent connects to Oracle directly.
- Schema (exact owner/schema name in Oracle TBD — confirm with DBA; names below are the Oracle-side identifiers the legacy SP addresses through the linked server, re-used here for the direct connection):
  - Tables: `DHSCLAIM_HEADER`, `DHSSERVICE_DETAILS`, `DHSDIAGNOSIS_DETAILS`, `DHSLAB_DETAILS`, `DHSRADIOLOGY_DETAILS`, `DHS_ATTACHMENT`, `DHS_DOCTOR`
- Source key: `proidclaim` (numeric; maps to int in the canonical bundle)
- Date column: `batchStartDate`
- Oracle connection: read from SQLite cache (populated by the cloud-config API flow — see Plan §4.13). Connection string must specify host, port, and service name; no TNS names file dependency.
- Identifier casing: Oracle unquoted identifiers are upper-case. The legacy SP uses the linked-server's case-insensitive behaviour, which masks this. The Oracle-direct SQL must use the exact schema-level casing (confirm with DBA whether tables are created unquoted and therefore `UPPER` by default, or created quoted in mixed case).

### 1.2 Mapping Matrix — Marshal → NPHIES

| Destination (NPHIES) | Source column | Source table | Transform / alias | Post-load rule |
| --- | --- | --- | --- | --- |
| DHSClaim_Header.patientname | patientname | DHSCLAIM_HEADER | direct | — |
| DHSClaim_Header.patientno | patientno | DHSCLAIM_HEADER | direct | — |
| DHSClaim_Header.memberid | memberid | DHSCLAIM_HEADER | direct | — |
| DHSClaim_Header.claimtype | claimtype | DHSCLAIM_HEADER | direct | — |
| DHSClaim_Header.ClaimedAmount | ClaimedAmount | DHSCLAIM_HEADER | NVL(ClaimedAmount, 0) AS ClaimedAmount | RecomputeHeaderTotals |
| DHSClaim_Header.TotalNetAmount | TotalNetAmount | DHSCLAIM_HEADER | NVL(TotalNetAmount, 0) AS TotalNetAmount | RecomputeHeaderTotals |
| DHSClaim_Header.TotalDiscount | TotalDiscount | DHSCLAIM_HEADER | NVL(TotalDiscount, 0) AS TotalDiscount | RecomputeHeaderTotals |
| DHSClaim_Header.TotalDeductible | TotalDeductible | DHSCLAIM_HEADER | NVL(TotalDeductible, 0) AS TotalDeductible | RecomputeHeaderTotals |
| DHSClaim_Header.DOB | DOB | DHSCLAIM_HEADER | date-validated alias | — |
| DHSClaim_Header.FetchDate | runtime | n/a | SYSDATE AS FetchDate | — |
| DHSClaim_Header.Height | Height | DHSCLAIM_HEADER | direct | NullIfColumnEquals(Height, '0') |
| DHSClaim_Header.Weight | Weight | DHSCLAIM_HEADER | direct | NullIfColumnEquals(Weight, '0') |
| DHSClaim_Header.EnconuterTypeID | ENCOUNTERTYPEID | DHSCLAIM_HEADER | ENCOUNTERTYPEID AS EnconuterTypeID | — |
| DHSService_Details.LineClaimedAmount | LineClaimedAmount | DHSSERVICE_DETAILS | NVL(LineClaimedAmount, 0) AS LineClaimedAmount | DeleteServicesByCompanyCode('12', LineClaimedAmount == 0) |
| DHSService_Details.CoInsurance | CoInsurance | DHSSERVICE_DETAILS | NVL(CoInsurance, 0) AS CoInsurance | — |
| DHSService_Details.CoPay | CoPay | DHSSERVICE_DETAILS | NVL(CoPay, 0) AS CoPay | — |
| DHSService_Details.LineItemDiscount | LineItemDiscount | DHSSERVICE_DETAILS | NVL(LineItemDiscount, 0) AS LineItemDiscount | — |
| DHSService_Details.NetAmount | NetAmount | DHSSERVICE_DETAILS | NVL(NetAmount, 0) AS NetAmount | — |
| DHSService_Details.PatientVatAmount | PatientVatAmount | DHSSERVICE_DETAILS | NVL(PatientVatAmount, 0) AS PatientVatAmount | — |
| DHSService_Details.NetVatAmount | NetVatAmount | DHSSERVICE_DETAILS | NVL(NetVatAmount, 0) AS NetVatAmount | — |
| DHSDiagnosis_Details.DiagnosisCode | DiagnosisCode | DHSDIAGNOSIS_DETAILS | UPPER(DiagnosisCode) AS DiagnosisCode | — |
| DHSDiagnosis_Details.DiagnosisDesc | DiagnosisDesc | DHSDIAGNOSIS_DETAILS | direct | — |
| DHSLab_Details.LabProfile | LabProfile | DHSLAB_DETAILS | SUBSTR(LabProfile, 1, 200) AS LabProfile | — |
| DHSLab_Details.LabTestName | LabTestName | DHSLAB_DETAILS | SUBSTR(LabTestName, 1, 30) AS LabTestName | — |
| DHSLab_Details.LabResult | LabResult | DHSLAB_DETAILS | SUBSTR(LabResult, 1, 200) AS LabResult | — |
| DHSRadiology_Details.RadiologyResult | RadiologyResult | DHSRADIOLOGY_DETAILS | SUBSTR(REGEXP_REPLACE(RadiologyResult, '[^[:print:]]', ''), 1, 1024) AS RadiologyResult | — |
| DHS_Attachment.Location | Location | DHS_ATTACHMENT | direct | — |
| DHS_Attachment.AttachmentType | AttachmentType | DHS_ATTACHMENT | direct | — |
| DHS_Doctor.doctorname | doctorname | DHS_DOCTOR | direct | — |
| DHS_Doctor.doctorcode | doctorcode | DHS_DOCTOR | direct | — |

### 1.3 Descriptor

Shipped as `VendorDescriptors/marshal.vendor.json`.

```json
{
  "schemaVersion": 1,
  "providerCode": "MARSHAL",
  "displayName": "Marshal HIS",
  "tier": 2,
  "source": {
    "databaseAlias": "NAFD (Oracle)",
    "engine": "oracle",
    "schema": "NAFD",
    "tables": {
      "header":     { "name": "DHSCLAIM_HEADER",      "keyColumn": "PROIDCLAIM" },
      "service":    { "name": "DHSSERVICE_DETAILS",   "parentKeyColumn": "PROIDCLAIM" },
      "diagnosis":  { "name": "DHSDIAGNOSIS_DETAILS", "parentKeyColumn": "PROIDCLAIM" },
      "lab":        { "name": "DHSLAB_DETAILS",       "parentKeyColumn": "PROIDCLAIM" },
      "radiology":  { "name": "DHSRADIOLOGY_DETAILS", "parentKeyColumn": "PROIDCLAIM" },
      "doctor":     { "name": "DHS_DOCTOR",           "parentKeyColumn": "PROIDCLAIM" }
    },
    "attachments": { "name": "DHS_ATTACHMENT", "parentKeyColumn": "PROIDCLAIM" },
    "dateColumn": "BATCHSTARTDATE",
    "companyCodeColumn": "COMPANYCODE"
  },
  "customSql": {
    "fetchHeaderBatch": "marshal/header.oracle.sql",
    "fetchServicesForKeys": "marshal/services.oracle.sql",
    "fetchDiagnosisForKeys": "marshal/diagnosis.oracle.sql",
    "fetchLabForKeys": "marshal/lab.oracle.sql",
    "fetchRadiologyForKeys": "marshal/radiology.oracle.sql",
    "fetchDoctorForKeys": "marshal/doctor.oracle.sql",
    "enumerateKeys": "marshal/enumerate_keys.oracle.sql",
    "count": "marshal/count.oracle.sql",
    "financialSummary": "marshal/financial_summary.oracle.sql",
    "fetchAttachments": "marshal/attachments.oracle.sql"
  },
  "resume": {
    "cursorStrategy": "sourceIntKey",
    "cursorColumn": "proidclaim"
  },
  "preview": {
    "countStrategy": "headerRowCount",
    "financialStrategy": "headerSumColumns",
    "financialColumns": {
      "claimedAmount":   "ClaimedAmount",
      "totalNetAmount":  "TotalNetAmount",
      "totalDiscount":   "TotalDiscount",
      "totalDeductible": "TotalDeductible"
    }
  },
  "childRebind": [
    { "child": "service",   "strategy": "parentKey" },
    { "child": "diagnosis", "strategy": "parentKey" },
    { "child": "lab",       "strategy": "parentKey" },
    { "child": "radiology", "strategy": "parentKey" },
    { "child": "doctor",    "strategy": "parentKey" }
  ],
  "columnAliases": {
    "header": { "ENCOUNTERTYPEID": "EnconuterTypeID" }
  },
  "transforms": {
    "header": [
      { "column": "ClaimedAmount",     "steps": ["NullToZero"] },
      { "column": "TotalNetAmount",    "steps": ["NullToZero"] },
      { "column": "TotalDiscount",     "steps": ["NullToZero"] },
      { "column": "TotalDeductible",   "steps": ["NullToZero"] },
      { "column": "RespiratoryRate",   "steps": ["NumericCleanup"] },
      { "column": "BloodPressure",     "steps": ["NumericCleanup"] },
      { "column": "Pulse",             "steps": ["NumericCleanup"] },
      { "column": "DurationOFIllness", "steps": ["NumericCleanup"] },
      { "column": "DOB",               "steps": ["DateValidate"] }
    ],
    "service": [
      { "column": "LineClaimedAmount", "steps": ["NullToZero"] },
      { "column": "CoInsurance",       "steps": ["NullToZero"] },
      { "column": "CoPay",             "steps": ["NullToZero"] },
      { "column": "LineItemDiscount",  "steps": ["NullToZero"] },
      { "column": "NetAmount",         "steps": ["NullToZero"] },
      { "column": "PatientVatAmount",  "steps": ["NullToZero"] },
      { "column": "NetVatAmount",      "steps": ["NullToZero"] }
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
      { "column": "RadiologyResult",
        "steps": ["RemoveSpecialChars", { "name": "Truncate", "length": 1024 }] }
    ]
  },
  "postLoadRules": [
    { "rule": "RecomputeHeaderTotals" },
    { "rule": "NullIfColumnEquals", "target": "header.Height", "value": "0" },
    { "rule": "NullIfColumnEquals", "target": "header.Weight", "value": "0" },
    { "rule": "DeleteServicesByCompanyCode",
      "companyCode": "12",
      "condition": "LineClaimedAmount == 0" }
  ]
}
```

### 1.4 Intentional behavior differences vs. the SP

- **Engine change.** The legacy SP runs on SQL Server via a linked server to the Oracle source. The new pipeline connects to Oracle directly and issues PL/SQL-style queries. T-SQL idioms in the SP (`ISNULL` → `NVL`, `LEFT` → `SUBSTR`, `CONVERT(date,…)` → `TO_DATE(…,'YYYYMMDD')`, `GETDATE()` → `SYSDATE`, `TOP n` → `FETCH FIRST n ROWS ONLY`) are translated in the Marshal SQL files.
- **`TriageDate`** is commented out in the SP; the new pipeline loads it. If this breaks downstream expectations, add `{ "rule": "ForceNull", "target": "header.TriageDate" }` to `postLoadRules`. Confirm with ops.
- **`Height='0' → NULL` and `Weight='0' → NULL`** are applied scoped to the current batch's claims in the new pipeline, not globally across the destination table. Stricter behavior; any old zero-string values from prior runs are unchanged until those batches are re-processed. Open question — see `Plan.md` §4 #7 on whether to preserve the legacy unscoped behaviour for parity.

### 1.5 Open items for Marshal

- Confirm Oracle schema/owner name (the legacy linked server masked this — the Oracle direct connection needs the exact schema).
- Confirm Oracle identifier casing policy (upper-case unquoted tables is the Oracle default; legacy SQL names were bracket-quoted case-insensitively).
- Confirm Marshal date column is `BATCHSTARTDATE` on the Oracle side (legacy SP reads `batchStartDate` through the linked server with SQL Server's case-insensitive matching).

---

## 2. AFI (Tier 2, SQL Server)

### 2.1 Source

- Engine: **SQL Server** (confirmed by the team)
- Legacy access: same SQL Server, different database (`DHSA.dbo.*`). **No** linked server involved.
- Database in legacy: `DHSA`
- Tables: identical names to destination (`DHSClaim_Header`, etc.); only the database is different
- Source key: `proidclaim` (int)
- Date column: `batchStartDate`
- Quirk: doctor child uses `invoicedate` as its date filter, not `batchStartDate`. Handled via a per-child `dateColumn` override.
- New-pipeline access: a direct SQL Server connection (read-only login) to the `DHSA` database with the connection string sourced from the cloud-config API → SQLite cache (see Plan §4.13).

### 2.2 Mapping Matrix — AFI → NPHIES

| Destination (NPHIES) | Source column | Source table | Transform / alias | Post-load rule |
| --- | --- | --- | --- | --- |
| DHSClaim_Header.patientname | patientname | DHSA.dbo.DHSClaim_Header | direct | — |
| DHSClaim_Header.patientno | patientno | DHSA.dbo.DHSClaim_Header | direct | — |
| DHSClaim_Header.batchStartDate | @_FromDate | procedure input | CONVERT(date, @From_Dt_fmt) AS batchStartDate | — |
| DHSClaim_Header.BatchEndDate | @_ToDate | procedure input | CONVERT(date, @To_Date_fmt) AS BatchEndDate | — |
| DHSClaim_Header.MaritalStatus | MaritalStatus | DHSA.dbo.DHSClaim_Header | LEFT(MaritalStatus, 2) AS MaritalStatus | — |
| DHSClaim_Header.SubscriberRelationship | constant | n/a | '6' AS SubscriberRelationship | — |
| DHSClaim_Header.FetchDate | runtime | n/a | GETDATE() AS FetchDate | — |
| DHSClaim_Header.ClaimedAmount | ClaimedAmount | DHSA.dbo.DHSClaim_Header | direct at insert | RecomputeHeaderTotals |
| DHSClaim_Header.TotalNetAmount | TotalNetAmount | DHSA.dbo.DHSClaim_Header | direct at insert | RecomputeHeaderTotals |
| DHSClaim_Header.TotalDiscount | TotalDiscount | DHSA.dbo.DHSClaim_Header | direct at insert | RecomputeHeaderTotals |
| DHSClaim_Header.TotalDeductible | TotalDeductible | DHSA.dbo.DHSClaim_Header | direct at insert | RecomputeHeaderTotals |
| DHSClaim_Header.Pulse | Pulse | DHSA.dbo.DHSClaim_Header | direct | ClampHeaderField(Pulse, 40, 200, 80) |
| DHSClaim_Header.DurationOFIllness | DurationOFIllness | DHSA.dbo.DHSClaim_Header | direct | ClampHeaderField(DurationOFIllness, 1, 365) |
| DHSService_Details.ToothNo | ToothNo | DHSA.dbo.DHSService_Details | LEFT(ToothNo, 2) AS ToothNo | — |
| DHSService_Details.PBMUnit | PBMUnit | DHSA.dbo.DHSService_Details | CEILING(PBMUnit) AS PBMUnit | — |
| DHSService_Details.PreAuthId | PreAuthId | DHSA.dbo.DHSClaim_Header | ch.PreAuthId AS PreAuthId | — |
| DHSService_Details.DoctorCode | DoctorCode | DHSA.dbo.DHSClaim_Header | ch.DoctorCode AS DoctorCode | — |
| DHSService_Details.LineClaimedAmount | LineClaimedAmount | DHSA.dbo.DHSService_Details | direct | DeleteServicesByCompanyCode('INS02', LineClaimedAmount <= 0) |
| DHSDiagnosis_Details.DiagnosisCode | DiagnosisCode | DHSA.dbo.DHSDiagnosis_Details | UPPER(DiagnosisCode) AS DiagnosisCode | — |
| DHSDiagnosis_Details.DiagnosisType | DiagnosisTypeID | DHSA.dbo.DHSDiagnosis_Details | DiagnosisTypeID AS DiagnosisType | — |
| DHSLab_Details.LabProfile | LabProfile | DHSA.dbo.DHSLab_Details | LEFT(LabProfile, 200) AS LabProfile | — |
| DHSLab_Details.LabTestName | LabTestName | DHSA.dbo.DHSLab_Details | LEFT(LabTestName, 30) AS LabTestName | — |
| DHSLab_Details.LabResult | LabResult | DHSA.dbo.DHSLab_Details | LEFT(LabResult, 200) AS LabResult | — |
| DHSRadiology_Details.RadiologyResult | RadiologyResult | DHSA.dbo.DHSRadiology_Details | LEFT(RadiologyResult, 1024) AS RadiologyResult | — |
| DHS_Attachment.Location | Location | DHSA.dbo.DHS_Attachment | direct | — |
| DHS_Doctor.doctorcode | DoctorCode | DHSA.dbo.DHS_Doctor | direct | doctor date-filter override (invoicedate <= @To_Date_fmt) |

### 2.3 Descriptor

Shipped as `VendorDescriptors/afi.vendor.json`.

```json
{
  "schemaVersion": 1,
  "providerCode": "AFI",
  "displayName": "AFI HIS",
  "tier": 2,
  "source": {
    "databaseAlias": "DHSA",
    "engine": "sqlserver",
    "tables": {
      "header":     { "name": "DHSClaim_Header",       "keyColumn": "proidclaim" },
      "service":    { "name": "DHSService_Details",    "parentKeyColumn": "proidclaim" },
      "diagnosis":  { "name": "DHSDiagnosis_Details",  "parentKeyColumn": "proidclaim" },
      "lab":        { "name": "DHSLab_Details",        "parentKeyColumn": "proidclaim" },
      "radiology":  { "name": "DHSRadiology_Details",  "parentKeyColumn": "proidclaim" },
      "doctor":     { "name": "DHS_Doctor",
                      "parentKeyColumn": "proidclaim",
                      "dateColumnOverride": "invoicedate",
                      "dateFilterMode": "upperBoundOnly" }
    },
    "attachments": { "name": "DHS_Attachment", "parentKeyColumn": "proidclaim" },
    "dateColumn": "batchStartDate",
    "companyCodeColumn": "companycode"
  },
  "customSql": {
    "fetchHeaderBatch": "afi/header.sqlserver.sql",
    "fetchServicesForKeys": "afi/services.sqlserver.sql",
    "fetchDiagnosisForKeys": "afi/diagnosis.sqlserver.sql",
    "fetchLabForKeys": "afi/lab.sqlserver.sql",
    "fetchRadiologyForKeys": "afi/radiology.sqlserver.sql",
    "fetchDoctorForKeys": "afi/doctor.sqlserver.sql",
    "enumerateKeys": "afi/enumerate_keys.sqlserver.sql",
    "count": "afi/count.sqlserver.sql",
    "financialSummary": "afi/financial_summary.sqlserver.sql",
    "fetchAttachments": "afi/attachments.sqlserver.sql"
  },
  "resume": {
    "cursorStrategy": "sourceIntKey",
    "cursorColumn": "proidclaim"
  },
  "preview": {
    "countStrategy": "headerRowCount",
    "financialStrategy": "headerSumColumns",
    "financialColumns": {
      "claimedAmount":   "ClaimedAmount",
      "totalNetAmount":  "TotalNetAmount",
      "totalDiscount":   "TotalDiscount",
      "totalDeductible": "TotalDeductible"
    }
  },
  "childRebind": [
    {
      "child": "service",
      "strategy": "invoicePatientCompany",
      "parentColumns": { "invoice": "InvoiceNumber", "patient": "PatientNo", "company": "CompanyCode" },
      "childColumns":  { "invoice": "InvoiceNumber", "patient": "PatientNo", "company": "CompanyCode" },
      "collations": {
        "invoice": "Arabic_CI_AS",
        "patient": "Arabic_CI_AS",
        "company": "Arabic_CI_AS"
      }
    },
    {
      "child": "diagnosis",
      "strategy": "invoicePatientCompany",
      "parentColumns": { "invoice": "InvoiceNumber", "patient": "PatientNo", "company": "CompanyCode" },
      "childColumns":  { "invoice": "InvoiceNumber", "patient": "PatientNo", "company": "CompanyCode" },
      "collations": {
        "invoice": "SQL_Latin1_General_CP1_CI_AS",
        "patient": "SQL_Latin1_General_CP1_CI_AS",
        "company": "SQL_Latin1_General_CP1_CI_AS"
      }
    },
    {
      "child": "lab",
      "strategy": "invoicePatientCompany",
      "columns": { "invoice": "invoicenumber", "patient": "patientno", "company": "companycode" },
      "collations": {
        "invoice": "SQL_Latin1_General_CP1_CI_AS",
        "patient": "SQL_Latin1_General_CP1_CI_AS",
        "company": "SQL_Latin1_General_CP1_CI_AS"
      }
    },
    {
      "child": "radiology",
      "strategy": "invoicePatientCompany",
      "columns": { "invoice": "invoicenumber", "patient": "patientno", "company": "companycode" },
      "collations": {
        "invoice": "SQL_Latin1_General_CP1_CI_AS",
        "patient": "SQL_Latin1_General_CP1_CI_AS",
        "company": "SQL_Latin1_General_CP1_CI_AS"
      }
    },
    {
      "child": "attachments",
      "strategy": "invoicePatientCompany",
      "columns": { "invoice": "invoicenumber", "patient": "patientno", "company": "companycode" },
      "collations": {
        "invoice": "SQL_Latin1_General_CP1_CI_AS",
        "patient": "SQL_Latin1_General_CP1_CI_AS",
        "company": "SQL_Latin1_General_CP1_CI_AS"
      }
    },
    { "child": "doctor",    "strategy": "parentKey" }
  ],
  "transforms": {
    "header": [
      { "column": "batchStartDate",        "steps": [{ "name": "Constant", "value": "@FromDate" }] },
      { "column": "BatchEndDate",          "steps": [{ "name": "Constant", "value": "@ToDate" }] },
      { "column": "MaritalStatus",         "steps": [{ "name": "Truncate", "length": 2 }] },
      { "column": "SubscriberRelationship","steps": [{ "name": "Constant", "value": "6" }] }
    ],
    "service": [
      { "column": "ToothNo", "steps": [{ "name": "Truncate", "length": 2 }] },
      { "column": "PBMUnit", "steps": ["Ceiling"] },
      { "column": "PreAuthId",  "steps": [{ "name": "FromHeader", "column": "PreAuthId" }] },
      { "column": "DoctorCode", "steps": [{ "name": "FromHeader", "column": "DoctorCode" }] }
    ],
    "diagnosis": [
      { "column": "DiagnosisCode", "steps": ["Upper"] },
      { "column": "DiagnosisType", "steps": [{ "name": "FromColumn", "column": "DiagnosisTypeID" }] }
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
      { "column": "RadiologyResult", "steps": [{ "name": "Truncate", "length": 1024 }] }
    ]
  },
  "postLoadRules": [
    { "rule": "RecomputeHeaderTotals" },
    { "rule": "ClampHeaderField", "field": "Pulse",
      "min": 40, "max": 200, "nullDefault": 80 },
    { "rule": "ClampHeaderField", "field": "DurationOFIllness",
      "min": 1, "max": 365 },
    { "rule": "DeleteServicesByCompanyCode",
      "companyCode": "INS02",
      "condition": "LineClaimedAmount <= 0" }
  ]
}
```

### 2.4 Notes on AFI-specific catalog entries

Two new transform-catalog entries needed to support AFI declaratively:

- **`Constant` with parameter-derived values**. `"@FromDate"` and `"@ToDate"` are reserved tokens that resolve to the batch's start and end dates. The transform catalog accepts these tokens for `Constant.value`. Any other `@`-prefixed token fails validation.
- **`FromHeader` transform**. Copies a value from the current claim's header DTO into a service-line DTO. This supports AFI's `ch.PreAuthId` → `ss.PreAuthId` pattern. Parameters: `column` (on header). Validation gate enforces that the column exists on the header DTO.
- **`FromColumn` transform**. Renames a column within the same DTO — covers AFI's `DiagnosisType ← DiagnosisTypeID` remap. Parameters: `column` (source column name within the same row).

### 2.5 Intentional behavior differences vs. the SP

- **`Pulse` / `DurationOFIllness` clamps** are scoped per-claim in the new pipeline, not applied globally across the destination. Same caveat as Marshal's Height/Weight.
- **Doctor child's `invoicedate` filter** is preserved by the `dateColumnOverride` field on the doctor table config. Matches the SP exactly.

---

## 3. BilalSoft (Tier 2 or Tier 3 — pending §4 decision, SQL Server)

BilalSoft's tier is pending: the team's latest direction says Tier 1 + Tier 2. The header-grouping logic (§3.4) is algorithmically heavy but expressible in a SQL CTE with `LAG()` + `SUM() OVER`, so a Tier 2 fit is possible. This section keeps the Tier 3 plugin design below as the **reference algorithm the SQL must reproduce**; if the decision lands on Tier 2, the C# types described here collapse into a single `bilal/header_groups.sqlserver.sql` file that the pipeline issues before the per-page fetch.

### 3.1 Source shape

Engine: **SQL Server** (confirmed by the team).

Legacy access: linked server `BILAL.medical_net.dbo.*` — **removed** in the new pipeline. The agent connects directly to the `medical_net` database on Bilal's SQL Server with a dedicated `ProviderProfile` and credentials sourced from the cloud-config API → SQLite cache (see Plan §4.13).

Views on Bilal's SQL Server (unprefixed in the new pipeline):

- `dhsclaimsdataview` — one row per service line, with header columns repeated on every line.
- `dhspatientinformationview` — patient → membership mapping.
- `dhsemrinformation` — EMR vitals keyed by invoice.
- `dhsdiagnosisdataview` — diagnosis keyed by invoice and `diagnosis_ser_no`.
- `dhslabdataview` — lab results keyed by invoice.
- `dhsradiologyview` — radiology results keyed by `recit`.

Data is denormalized across service lines; claim header identity is synthesized by grouping. There is no stable source `ProIdClaim`.

### 3.2 Mapping Matrix — BilalSoft → NPHIES

| Destination (NPHIES) | Source column | Source table | Transform / alias | Post-load rule |
| --- | --- | --- | --- | --- |
| DHSClaim_Header.patientname | patientname | dhsclaimsdataview | REPLACE(patientname, '0', '') AS patientname | — |
| DHSClaim_Header.patientid | patientid | dhsclaimsdataview | REPLACE(patientid, '.', '') AS patientid | — |
| DHSClaim_Header.patientidtype | patientid | dhsclaimsdataview | first-char CASE map (1/2/else) | — |
| DHSClaim_Header.TriageDate | triagedate | dhsclaimsdataview | REPLACE(TriageDate, ' ', NULL) (**bug — always returns NULL**) | parity-preserved |
| DHSClaim_Header.CauseOfDeath | CauseOfDeath | dhsclaimsdataview | REPLACE(CauseOfDeath, ' ', NULL) (**bug — always returns NULL**) | parity-preserved |
| DHSClaim_Header.BatchNo | @_FromDate | procedure input | derived @BatNo | — |
| DHSClaim_Header.FetchDate | runtime | n/a | GETDATE() AS FetchDate | — |
| DHSClaim_Header.claimedamount | aggregate | DHSService_Details | insert from grouped header seed | RecomputeHeaderTotals |
| DHSClaim_Header.totaldiscount | aggregate | DHSService_Details | insert from grouped header seed | RecomputeHeaderTotals |
| DHSClaim_Header.totaldeductible | aggregate | DHSService_Details | insert from grouped header seed | RecomputeHeaderTotals |
| DHSClaim_Header.totalnetamount | aggregate | DHSService_Details | insert from grouped header seed | RecomputeHeaderTotals |
| DHSClaim_Header.memberid | membership | dhspatientinformationview | back-fill join on patient code | BackfillMemberId |
| DHSClaim_Header.height | height | dhsemrinformation | EMR back-fill | NullIfColumnEquals(Height, '0.00') |
| DHSClaim_Header.weight | weight | dhsemrinformation | EMR back-fill | NullIfColumnEquals(Weight, '0.00') |
| DHSClaim_Header.temperature | temperature | dhsemrinformation | EMR back-fill | NullIfColumnEquals(Temperature, '0.00') |
| DHSClaim_Header.durationofillness | durationofillness | dhsemrinformation | dbo.udf_GetNumeric(durationofillness) | — |
| DHSClaim_Header.InvestigationResult | child existence | derived from lab/radiology | CASE WHEN labs/radiology exist THEN 'IRA' ELSE 'NA' END | SetInvestigationResult(IRA, NA) |
| DHSService_Details.copay | coinsurance, lineclaimedamount, lineitemdiscount, claimedamount | dhsclaimsdataview | asymmetric copay formula; Guard asymmetry preserved — see OpenQuestions §5a | — |
| DHSService_Details.servicecode | servicecode2 | dhsclaimsdataview | LEFT(servicecode2, 50) AS servicecode | — |
| DHSService_Details.servicedescription | servicedescription | dhsclaimsdataview | LEFT(servicedescription, 200) AS servicedescription | — |
| DHSService_Details.treatmenttypeindicator | servicegategory | dhsclaimsdataview | CASE tree (CONSULT/SERVICES=2, Medcine=1, DENT=5, LAB=3, X-RAY=4) | — |
| DHSService_Details.PBMDuration | PBMDuration | dhsclaimsdataview | numeric cleanup and max-int cap | ForceServiceField(PBMDuration='1' when ServiceGategory in ER/OTHERS) |
| DHSService_Details.NumberOfIncidents | numberofincidents | dhsclaimsdataview | min clamp to 1 | — |
| DHSService_Details.DiagnosisCode | diagnosis_code | dhsdiagnosisdataview | SFDA back-fill by diagnosis/service join | BackfillDiagnosisCodeFromSource |
| DHSService_Details.LineClaimedAmount | lineclaimedamount | dhsclaimsdataview | direct | DeleteServicesByCompanyCode('1241020001', LineClaimedAmount == 0) |
| DHS_Doctor.doctorname | doctorname | dhsclaimsdataview | first row per AssignedProIdClaim | — |
| DHS_Doctor.doctorcode | doctorcode | dhsclaimsdataview | first row per AssignedProIdClaim | — |
| dhsdiagnosis_details.diagnosiscode | diagnosis_code | dhsdiagnosisdataview | direct | dedupe by (AssignedProIdClaim, diagnosis_code) |
| dhslab_details.labprofile | labprofile | dhslabdataview | LEFT(labprofile,100) AS labprofile | — |
| dhslab_details.labtestname | labtestname | dhslabdataview | LEFT(labtestname,50) AS labtestname | — |
| DHSRadiology_Details.ServiceCode | Service_no | dhsradiologyview | direct | — |
| DHSRadiology_Details.RadiologyResult | xray_report | dhsradiologyview | CAST(xray_report AS varchar(1000)) | — |

### 3.3 Plugin files

```
Vendors/BilalSoft/
├── BilalClaimSource.cs           implements IClaimSource
├── BilalHeaderGrouper.cs         pure function: service rows → groups
├── BilalResumeCursor.cs          synthetic cursor encoding
├── BilalPreview.cs               implements IClaimExtractionPreview overrides
├── BilalTransform.cs             implements IVendorTransform
└── Sql/
    ├── CountPatientDoctorTuples.sql  (fast lower-bound count)
    ├── CountGroupedExact.sql         (expensive exact count)
    ├── SumFinancials.sql             (financial totals, no grouping needed)
    ├── EnumerateKeys.sql             (pages of grouping-key tuples)
    ├── FetchDetailsForKeys.sql       (main claim data for a page)
    ├── FetchPatientInfo.sql
    ├── FetchEmr.sql
    ├── FetchDiagnosis.sql
    ├── FetchLab.sql
    └── FetchRadiology.sql
```

### 3.4 `BilalHeaderGrouper` — pure function specification

Signature:

```
static IReadOnlyList<HeaderGroup> GroupIntoHeaders(
    IReadOnlyList<BilalServiceLineRow> sortedLines,
    int followupDays)
```

Where:

- `BilalServiceLineRow` has `patientno`, `doctorcode`, `claimtype`, `treatmentfromdate`, and all other source columns.
- `HeaderGroup` has `GroupId` (synthetic int), `HeaderRow` (the first row of the group — header source values come from it), `Lines` (all rows in the group, used for services).

Algorithm: 1. **Sort** by `(patientno, doctorcode, treatmentfromdate, claimtype)`. Stable sort, matches the SP's `ORDER BY` at `BilalSoft/USP_FetchClaims.sql:191`. 2. **Walk sequentially.** For each line, decide if it starts a new group:

- If it's the first line, start a new group (id 0).
- Otherwise, start a new group when any of:
- `patientno != previous.patientno`, OR
- `doctorcode != previous.doctorcode`, OR
- `claimtype != previous.claimtype`, OR
- `(treatmentfromdate - previous.treatmentfromdate).Days >= followupDays`
- Else append to current group. 3. **Critical boundary**: a gap of exactly `followupDays` (14) starts a new group. The SP uses `>= @Followup`. Unit tests must cover 13, 14, and 15-day gaps explicitly.

This function is pure, deterministic, and has no DB dependency. It is the primary unit-test target for Bilal.

### 3.5 `BilalResumeCursor` — synthetic cursor encoding

Bilal has no stable source `ProIdClaim`. The cursor is an **order-preserving composite string** built from the grouper's sort dimensions:

```text
cursor = patientno + "|" + doctorcode + "|" + claimtype + "|" + earliestTreatmentDate.ToString("yyyyMMdd")
```

Each field is normalized before concatenation so lexicographic order matches the grouper's sort:

- Strings are trimmed, lower-cased, and padded to a fixed width (patientno → 20 chars, doctorcode → 20, claimtype → 10) using a delimiter that does not appear in the source (tab `\t` or unit separator `\u001F`).
- `earliestTreatmentDate` is formatted `yyyyMMdd` so lexicographic order matches calendar order.

Properties:

- **Deterministic** — same grouping dimensions always produce the same cursor.
- **Order-preserving** — lexicographic order on the concatenated string matches the grouper's sort order of `(patientno, doctorcode, claimtype, treatmentfromdate)`. **Do not hash the cursor;** hashes are not order-preserving, so paging via `cursor > @LastSeen` would skip rows unpredictably.
- **Opaque to consumers** — treated as a string, not parsed by the pipeline.
- If a stable shorter identifier is needed for logs or for a SQLite index, store a separate `SHA-256(cursor)` column alongside the cursor itself. The hash is for identity lookup only, never for ordering.

The cursor is written to the `Claims.SourceCursor` column on staging (see `Plan.md` §3.4).

**Resume semantics**: on resume, the pipeline fetches `GetMaxCursorAsync(BILAL, batchId)` and passes it to `BilalClaimSource.EnumerateKeyPagesAsync`. The source enumerator skips any group whose cursor is lexicographically ≤ the last-seen cursor.

**Caveat (documented)**: if the source data changes between resumes (new service lines inserted for an already-processed claim window), grouping may produce different boundaries on the second run.

The cursor is best-effort monotonic under stable source data. Operators who suspect source drift during a long-running batch should use "Reset and re-fetch" rather than resume. This limitation is not worse than the SP's behavior — the SP simply deletes and reloads, which has the same problem hidden inside `sp_deletebatch`.

### 3.6 `BilalPreview` — pre-batch validation overrides

`BatchCreationOrchestrator` calls `IClaimExtractionPreview.EstimateAsync` before creating a batch. Default implementation (Tier 1/2) queries source headers directly. Bilal overrides:

**Approximate count** (always shown in UI):

```sql
-- CountPatientDoctorTuples.sql
SELECT COUNT(DISTINCT patientno + '|' + doctorcode + '|' + claimtype)
FROM dhsclaimsdataview
WHERE Inc_Acc_No = @InsuranceCode
  AND treatmentfromdate >= @FromDate
  AND treatmentfromdate <  @ToDate + 1
```

This is a **lower bound** for true grouped count (a single patient-doctor-claimtype tuple can produce multiple groups if there are ≥14-day gaps). The UI shows this as "At least N claims" with a button "Compute exact count."

**Exact count on demand**:

```sql
-- CountGroupedExact.sql
-- Rerun the grouping in SQL using LAG()/SUM() window functions.
-- Slow. Runs in background; UI shows spinner.
WITH base AS (
  SELECT patientno, doctorcode, claimtype, treatmentfromdate
  FROM dhsclaimsdataview
  WHERE Inc_Acc_No = @InsuranceCode
    AND treatmentfromdate >= @FromDate
    AND treatmentfromdate <  @ToDate + 1
),
sorted AS (
  SELECT *, ROW_NUMBER() OVER (ORDER BY patientno, doctorcode, treatmentfromdate, claimtype) AS rn
  FROM base
),
boundaries AS (
  SELECT rn,
         CASE
           WHEN patientno   <> LAG(patientno,   1, '') OVER (ORDER BY rn)
             OR doctorcode  <> LAG(doctorcode,  1, '') OVER (ORDER BY rn)
             OR claimtype   <> LAG(claimtype,   1, '') OVER (ORDER BY rn)
             OR DATEDIFF(day, LAG(treatmentfromdate, 1, treatmentfromdate) OVER (ORDER BY rn),
                              treatmentfromdate) >= 14
           THEN 1 ELSE 0
         END AS IsNewGroup
  FROM sorted
)
SELECT SUM(IsNewGroup) FROM boundaries
```

**Financial totals** (always shown, fast — associativity):

```sql
-- SumFinancials.sql
SELECT SUM(lineclaimedamount) AS TotalClaimed,
       SUM(lineitemdiscount)  AS TotalDiscount,
       SUM(coinsurance)       AS TotalDeductible,
       SUM(netamount)         AS TotalNetAmount
FROM dhsclaimsdataview
WHERE Inc_Acc_No = @InsuranceCode
  AND treatmentfromdate >= @FromDate
  AND treatmentfromdate <  @ToDate + 1
```

Financial aggregation is associative — grouped totals equal raw service-line totals — so financials are fast even for Bilal.

### 3.7 `BilalTransform` — per-record and per-batch logic

Per-record (`TransformAsync`):

- `patientname = Replace('0', "")`
- `patientid = Replace('.', "")`
- `patientidtype`: `patientid[0]` → `"1" → "1"`, `"2" → "2"`, else `"3"`
- `TreatmentTypeIndicator`: CASE over `servicegategory` — `CONSULT/SERVICES → 2`, `Medcine → 1`, `DENT → 5`, `LAB → 3`, `X-RAY → 4`, else `2`
- `copay` via `CopayPercentage` transform with the preserved guard quirk (see `VendorDescriptors.md` §3.2). Explicitly:
- Numerator column: `coinsurance`
- Denominator: `lineclaimedamount - lineitemdiscount`
- Guard: `lineclaimedamount == 0 OR (claimedamount - lineitemdiscount) == 0`
- Note the asymmetry: guard checks `claimedamount`, denominator uses `lineclaimedamount`. Preserved as-is.
- `PBMDuration`: `NumericCleanup` with `maxInt = 2147483647`
- `NumberOfIncidents`: `ClampInt(min=1, max=null)`
- `TriageDate`: **preserve legacy NULL-always behaviour for parity** (SP uses `REPLACE(x, ' ', NULL)` which in SQL Server always returns NULL because the replacement arg is NULL). Implemented via a `ForceNull` post-load rule on this column. Final decision pending Open Questions Q5c — if the team decides to fix instead of preserve, remove the `ForceNull` rule and the field passes through as-is. See Mapping Matrix §3.2.
- `CauseOfDeath`: same as `TriageDate` — preserved as always-NULL for parity, same `ForceNull` rule, same dependency on Q5c.
- Constants: `TreatmentCountryCode = "SAR"`, `Priority = "Normal"`, `SubmissionReasonCode = "01"`, `MediCode = ""`

Per-batch (`FinalizeBatchAsync`):

1. **`RecomputeHeaderTotals`** — aggregate service lines into header totals. 2. **Member-ID back-fill** from `dhspatientinformationview`:

- Join on `patientno ↔ patientcode` with `SaudiNameEquals` comparer (Arabic collation).
- Overwrite `header.memberid`. 3. **EMR back-fill** from `dhsemrinformation`:
- Join on `invoiceNumber`.
- Back-fill `height`, `weight`, `temperature`, `respiratoryrate`, `bloodpressure`, `pulse`, `durationofillness`, `internalnotes`.
- For each, apply `NullIfColumnEquals(value: "0.00")`. 4. **SFDA `DiagnosisCode` back-fill** (this was missed in an earlier plan draft):
- For services where `ServiceType == "SFDA"`:
- Look up diagnosis from `#TempDiagnosis` via `diagnosis_ser_no` and match by `itemRefernce`/`servicecode2` (collation-aware) back to the service line.
- Overwrite `service.DiagnosisCode`.
- This lives in C# because it involves multi-table join logic specific to Bilal's source shape. 5. **`ForceServiceField`**: `PBMDuration = "1"` where `ServiceGategory IN ("ER","OTHERS")`. 6. **`SetInvestigationResult`**: `trueValue="IRA", falseValue="NA", predicate="labsOrRadiologyExist"`. 7. **`DeleteServicesByCompanyCode`**: `companyCode="1241020001", condition="LineClaimedAmount == 0"`.

### 3.8 Bilal-specific rebind strategies

Each child uses a different rebind path because the source uses different keys in different views:

| Child | Strategy | Parent column | Child column |
|---|---|---|---|
| service | `computedGroup` | Synthetic `HeaderGroupId` | Synthetic `HeaderGroupId` |
| doctor | `computedGroup` (dedupe by `AssignedProIdClaim`) | Synthetic `HeaderGroupId` | Synthetic `HeaderGroupId` |
| diagnosis | `invoice` | `header.InvoiceNumber` | `dhsdiagnosisdataview.invoicenumber` |
| lab | `itemReference` | `service.itemRefernce` | `dhslabdataview.invoicenumber` |
| radiology | `itemReference` | `service.itemRefernce` | `dhsradiologyview.recit` |

Note: Bilal labs join the child's `invoicenumber` column to the *parent service's* `itemRefernce`. This is not a typo — the view names happen to mismatch the join semantics. Preserved as-is.

### 3.9 Bilal migration checklist

- [ ] `BilalHeaderGrouper` unit tests: 13/14/15-day gap boundaries; patient change; doctor change; claimtype change; combined boundary conditions.
- [ ] `BilalResumeCursor` unit tests: determinism; order stability; collisions across distinct inputs.
- [ ] `BilalPreview` integration test: approximate count is ≤ exact count on a sample dataset.
- [ ] `BilalTransform.CopayPercentage` test: guard quirk preserved. Case where `lineclaimedamount - lineitemdiscount == 0` but `claimedamount - lineitemdiscount != 0` returns 0 (not throws).
- [ ] SFDA `DiagnosisCode` back-fill test.
- [ ] `InvestigationResult` IRA/NA test with and without labs/radiology.
- [ ] `ParityReporter` clean run against one parity-cycle dataset.
- [ ] Tier 2 vs Tier 3 decision (`Plan.md` §4 #1) resolved.
- [ ] UI preview rework tested with support ops.

---

## 4. Excys (Tier 2, Oracle)

Spec delivered 2026-04-18 (`PLAN/VEND/exeys/USP_FetchClaims.sql` + `USP_FetchClaims_Mapping.md`). The legacy SP runs on the SQL Server destination and reaches Excys's Oracle source through a linked server named `DHS` using `OPENQUERY(...)` against **Oracle views** (`DHS.DHSCLAIM_HEADER_V`, `DHS.DHSSERVICE_DETAILS_V`, etc.) — not base tables. This is a critical departure from Marshal's pattern (four-part names against `NAFD` base tables).

### 4.1 Known facts

- **Engine:** Oracle (**inferred** from `OPENQUERY` usage against `_V`-suffixed views under schema `DHS` — a pattern typical of Oracle linked servers, where four-part naming is prohibitively slow. **Final confirmation pending** from the cloud-config API connection-string metadata or DBA sign-off, per the same standard Marshal / AFI / BilalSoft went through ("confirmed by the team"). Treat engine as "very likely Oracle" until that confirmation lands.)
- **Source schema:** `DHS` (schema name used in `OPENQUERY` call, *not* `NAFD` as used by Marshal).
- **Access surface:** **views** with `_V` suffix (`DHSCLAIM_HEADER_V`, `DHSSERVICE_DETAILS_V`, `DHSDIAGNOSIS_DETAILS_V`, `DHSLAB_DETAILS_V`, `DHSRADIOLOGY_DETAILS_V`, `DHS_ATTACHMENT_V`, `DHS_DOCTOR_V`). Whether the new direct-Oracle pipeline queries these views or their underlying base tables is **Open Question Q13** (see OpenQuestions §13).
- **Parent key:** `proidclaim` (same name as Marshal).
- **Invoice rebinding:** destination-side parent-child relink is by `InvoiceNumber`. The legacy SP applies `COLLATE SQL_Latin1_General_CP1_CI_AS` on both sides of the join, but this is a **SQL Server–side construct** executed on the destination (Oracle does not support per-comparison `COLLATE` clauses). In the new direct-Oracle pipeline, collation-insensitive invoice matching is performed in C# during in-memory rebinding (see VendorDescriptors §5.3), not in the Oracle query.
- **Date column:** `batchStartDate` for header and most children; **doctor uses `invoicedate` for the upper bound only** (asymmetric filter — see §4.2 doctor row and descriptor's `dateColumnOverride`).
- **Hardcoded company-code cleanup constant:** `'100002'` — the SP deletes zero-amount service lines only for this company. Captured as a `DeleteServicesByCompanyCode` post-load rule (see Q7 for the policy on hardcoded codes).
- **Commented-out columns** (not loaded by the legacy SP): header `OxygenSaturation`, service `PatientVatAmount`, service `ServiceEventType`. Declared in the descriptor's `skipColumns` so parity reporting does not flag them as missing.
- **Financial aggregation:** after service insert, the SP runs an UPDATE that overwrites header `ClaimedAmount`, `TotalDiscount`, `TotalDeductible`, and `TotalNetAmount` with SUMs over the loaded service rows (SP lines 360–374). Captured as a `RecomputeHeaderTotals` post-load rule.

### 4.2 Mapping Matrix — Excys → NPHIES

| Destination (NPHIES) | Source column / expression | Source view | Transform / alias | Post-load rule |
| --- | --- | --- | --- | --- |
| `DHSClaim_Header.memberid` | `REPLACE(memberid, ' ', '')` | `DHSCLAIM_HEADER_V` | Strip spaces | — |
| `DHSClaim_Header.PreAuthid` | `REPLACE(PreAuthid, ' ', '')` | `DHSCLAIM_HEADER_V` | Strip spaces | — |
| `DHSClaim_Header.referind` | `CASE WHEN referind = 'F' THEN '0' ELSE '1' END` | `DHSCLAIM_HEADER_V` | Flag normalization | — |
| `DHSClaim_Header.emerind` | `CASE WHEN emerind = 'F' THEN '0' ELSE '1' END` | `DHSCLAIM_HEADER_V` | Flag normalization | — |
| `DHSClaim_Header.FetchDate` | `GETDATE()` | *(system)* | Load timestamp default | — |
| `DHSClaim_Header.OxygenSaturation` | *(not loaded)* | `DHSCLAIM_HEADER_V` | Skip — legacy SP comments it out | — |
| `DHSService_Details.PBMDuration` | `CEILING(PBMDuration)` | `DHSSERVICE_DETAILS_V` | Round up to integer | — |
| `DHSService_Details.PBMTimes` | `CEILING(PBMTimes)` | `DHSSERVICE_DETAILS_V` | Round up to integer | — |
| `DHSService_Details.PBMUnit` | `CEILING(PBMUnit)` | `DHSSERVICE_DETAILS_V` | Round up to integer | — |
| `DHSService_Details.PreAuthId` | `ch.PreAuthId` | `DHSCLAIM_HEADER_V` | Taken from header (not service) | — |
| `DHSService_Details.DoctorCode` | `ch.DoctorCode` | `DHSCLAIM_HEADER_V` | Taken from header (not service) | — |
| `DHSService_Details.ErrorMessage` | `ss.ErrorMessage` | `DHSSERVICE_DETAILS_V` | Explicit alias (both views expose the column) | — |
| `DHSService_Details.ServiceEventType` | *(not loaded)* | `DHSSERVICE_DETAILS_V` | Skip — legacy SP comments it out | — |
| `DHSDiagnosis_Details.DiagnosisCode` | `UPPER(DiagnosisCode)` | `DHSDIAGNOSIS_DETAILS_V` | Uppercase normalization | — |
| `DHSDiagnosis_Details.DiagnosisType` | `DiagnosisTypeID` | `DHSDIAGNOSIS_DETAILS_V` | Column renamed in destination | — |
| `DHSLab_Details.LabProfile` | `LEFT(LabProfile, 200)` | `DHSLAB_DETAILS_V` | Truncate to 200 chars | — |
| `DHSLab_Details.LabTestName` | `LEFT(LabTestName, 30)` | `DHSLAB_DETAILS_V` | Truncate to 30 chars | — |
| `DHSLab_Details.LabResult` | `LEFT(LabResult, 200)` | `DHSLAB_DETAILS_V` | Truncate to 200 chars | — |
| `DHSLab_Details.LabUnits` / `LabLow` / `LabHigh` / `LabSection` | `LEFT(col, 50)` | `DHSLAB_DETAILS_V` | Truncate to 50 chars | — |
| `DHSRadiology_Details.RadiologyResult` | `LEFT(RadiologyResult, 1024)` | `DHSRADIOLOGY_DETAILS_V` | Truncate to 1024 chars | — |
| `DHSService_Details.PatientVatAmount` | *(not loaded)* | `DHSSERVICE_DETAILS_V` | Skip — legacy SP comments it out at lines 273 / 315 | — |
| `DHS_Doctor` — date filter | `batchStartDate >= @From_Dt_fmt` AND `invoicedate <= @To_Date_fmt` | `DHSCLAIM_HEADER_V` | **Asymmetric filter on doctor only**: lower bound on `batchStartDate`, upper bound on `invoicedate`. Declared via `dateColumnOverride: "invoicedate"` + `dateFilterMode: "upperBoundOnly"`. Matches AFI's doctor quirk. | — |
| `DHS_Doctor` — dedupe | `SELECT DISTINCT` | `DHS_DOCTOR_V` | Descriptor sets `distinct: true` on doctor entity (SP line 476). | — |
| `DHSClaim_Header.ClaimedAmount` | `SUM(LineClaimedAmount)` over destination services | *(aggregate)* | — | Recalculated by `RecomputeHeaderTotals` post-load rule |
| `DHSClaim_Header.TotalDiscount` | `SUM(LineItemDiscount)` | *(aggregate)* | — | Recalculated by `RecomputeHeaderTotals` post-load rule |
| `DHSClaim_Header.TotalDeductible` | `SUM(CoInsurance)` | *(aggregate)* | — | Recalculated by `RecomputeHeaderTotals` post-load rule |
| `DHSClaim_Header.TotalNetAmount` | `SUM(NetAmount)` | *(aggregate)* | — | Recalculated by `RecomputeHeaderTotals` post-load rule |
| Zero-amount cleanup | — | `DHSService_Details` | — | Executed by `DeleteServicesByCompanyCode` post-load rule with `companyCode: "100002"`, `condition: "LineClaimedAmount == 0"` |

All other columns inside each table are direct 1:1 mappings by column order (see `PLAN/VEND/exeys/USP_FetchClaims_Mapping.md` §6 for the full per-column list).

### 4.3 Descriptor

```json
{
  "schemaVersion": 1,
  "providerCode": "EXCYS",
  "displayName": "Excys HIS",
  "tier": 2,
  "source": {
    "databaseAlias": "Excys-Oracle",
    "engine": "oracle",
    "schema": "DHS",
    "tables": {
      "header":     { "name": "DHSCLAIM_HEADER_V",     "keyColumn": "proidclaim", "accessMode": "view" },
      "service":    { "name": "DHSSERVICE_DETAILS_V",  "parentKeyColumn": "proidclaim", "accessMode": "view" },
      "diagnosis":  { "name": "DHSDIAGNOSIS_DETAILS_V","parentKeyColumn": "proidclaim", "accessMode": "view" },
      "lab":        { "name": "DHSLAB_DETAILS_V",      "parentKeyColumn": "proidclaim", "accessMode": "view" },
      "radiology":  { "name": "DHSRADIOLOGY_DETAILS_V","parentKeyColumn": "proidclaim", "accessMode": "view" },
      "doctor":     { "name": "DHS_DOCTOR_V",          "parentKeyColumn": "proidclaim", "accessMode": "view",
                      "dateColumnOverride": "invoicedate",
                      "dateFilterMode": "upperBoundOnly",
                      "distinct": true }
    },
    "attachments":  { "name": "DHS_ATTACHMENT_V",      "parentKeyColumn": "proidclaim", "accessMode": "view" },
    "dateColumn": "batchStartDate",
    "companyCodeColumn": "companycode"
  },
  "resume": {
    "cursorStrategy": "sourceIntKey",
    "cursorColumn": "proidclaim"
  },
  "preview": {
    "countStrategy": "headerRowCount",
    "financialStrategy": "headerSumColumns",
    "financialColumns": {
      "claimedAmount":   "ClaimedAmount",
      "totalNetAmount":  "TotalNetAmount",
      "totalDiscount":   "TotalDiscount",
      "totalDeductible": "TotalDeductible"
    }
  },
  "childRebind": [
    { "child": "service",    "strategy": "parentKey" },
    { "child": "diagnosis",  "strategy": "parentKey" },
    { "child": "lab",        "strategy": "parentKey" },
    { "child": "radiology",  "strategy": "parentKey" },
    { "child": "attachments","strategy": "parentKey" },
    { "child": "doctor",     "strategy": "parentKey" }
  ],
  "customSql": {
    "fetchHeaderBatch":      "excys/header.oracle.sql",
    "fetchServicesForKeys":  "excys/services.oracle.sql",
    "fetchDiagnosisForKeys": "excys/diagnosis.oracle.sql",
    "fetchLabForKeys":       "excys/lab.oracle.sql",
    "fetchRadiologyForKeys": "excys/radiology.oracle.sql",
    "fetchDoctorForKeys":    "excys/doctor.oracle.sql",
    "enumerateKeys":         "excys/enumerate_keys.oracle.sql",
    "count":                 "excys/count.oracle.sql",
    "financialSummary":      "excys/financial_summary.oracle.sql",
    "fetchAttachments":      "excys/attachments.oracle.sql"
  },
  "transforms": {
    "header": [
      { "column": "memberid",   "steps": [{ "name": "ReplaceChar", "find": " ", "replace": "" }] },
      { "column": "PreAuthid",  "steps": [{ "name": "ReplaceChar", "find": " ", "replace": "" }] },
      { "column": "referind",   "steps": [{ "name": "CaseMap", "map": { "F": "0" }, "default": "1" }] },
      { "column": "emerind",    "steps": [{ "name": "CaseMap", "map": { "F": "0" }, "default": "1" }] }
    ],
    "service": [
      { "column": "PBMDuration", "steps": ["Ceiling"] },
      { "column": "PBMTimes",    "steps": ["Ceiling"] },
      { "column": "PBMUnit",     "steps": ["Ceiling"] },
      { "column": "PreAuthId",   "steps": [{ "name": "FromHeader", "column": "PreAuthId" }] },
      { "column": "DoctorCode",  "steps": [{ "name": "FromHeader", "column": "DoctorCode" }] }
    ],
    "diagnosis": [
      { "column": "DiagnosisCode", "steps": ["Upper"] },
      { "column": "DiagnosisType", "steps": [{ "name": "FromColumn", "column": "DiagnosisTypeID" }] }
    ],
    "lab": [
      { "column": "LabProfile",   "steps": [{ "name": "Truncate", "length": 200 }] },
      { "column": "LabTestName",  "steps": [{ "name": "Truncate", "length": 30 }] },
      { "column": "LabResult",    "steps": [{ "name": "Truncate", "length": 200 }] },
      { "column": "LabUnits",     "steps": [{ "name": "Truncate", "length": 50 }] },
      { "column": "LabLow",       "steps": [{ "name": "Truncate", "length": 50 }] },
      { "column": "LabHigh",      "steps": [{ "name": "Truncate", "length": 50 }] },
      { "column": "LabSection",   "steps": [{ "name": "Truncate", "length": 50 }] }
    ],
    "radiology": [
      { "column": "RadiologyResult", "steps": [{ "name": "Truncate", "length": 1024 }] }
    ]
  },
  "postLoadRules": [
    { "rule": "RecomputeHeaderTotals" },
    {
      "rule": "DeleteServicesByCompanyCode",
      "companyCode": "100002",
      "condition": "LineClaimedAmount == 0"
    }
  ],
  "skipColumns": {
    "header":  ["OxygenSaturation"],
    "service": ["PatientVatAmount", "ServiceEventType"]
  }
}
```

Descriptor notes:

- `postLoadRules` use named rules from `IPostLoadRuleRegistry` (`RecomputeHeaderTotals`, `DeleteServicesByCompanyCode`); no raw SQL in descriptors (VendorDescriptors §6.1 gate 5).
- `RecomputeHeaderTotals` captures the SP's UPDATE (lines 360–374) that recomputes `ClaimedAmount/TotalDiscount/TotalDeductible/TotalNetAmount` from service sums after service insert.
- Doctor entity: `dateColumnOverride: "invoicedate"`, `dateFilterMode: "upperBoundOnly"` (SP line 501–502 uses `batchStartDate >= @From` AND `invoicedate <= @To`; same quirk as AFI), and `distinct: true` (SP line 476 uses `SELECT DISTINCT`).
- `PatientVatAmount` in `skipColumns.service` — SP comments it out at lines 273 and 315 alongside `ServiceEventType`.
- `accessMode` is per-table, value `"view"`, to accommodate real-world mixed access (BilalSoft reads views too).
- No `invoiceJoinCollation` on `source`: Oracle does not support per-comparison `COLLATE` clauses. The legacy SP's `COLLATE SQL_Latin1_General_CP1_CI_AS` runs on the SQL Server destination side of the linked-server join; in the new direct-Oracle pipeline, collation-insensitive invoice matching moves to C# in-memory rebinding (VendorDescriptors §5.3).

### 4.4 Onboarding status

- [x] `USP_FetchClaims.sql` obtained (`PLAN/VEND/exeys/USP_FetchClaims.sql`).
- [x] `USP_FetchClaims_Mapping.md` obtained (`PLAN/VEND/exeys/USP_FetchClaims_Mapping.md`).
- [~] Engine: **inferred Oracle** from SP patterns; awaiting "confirmed by the team" sign-off from DBA or cloud-config connection string (same standard as Marshal / AFI / BilalSoft).
- [x] Schema confirmed: `DHS` (not `NAFD` — Excys is *not* a pure sibling of Marshal).
- [x] Parent key confirmed: `proidclaim`.
- [x] Date column confirmed: `batchStartDate` for header and most children; `invoicedate` for doctor upper-bound only (asymmetric).
- [x] Company-code cleanup constant confirmed: `'100002'`.
- [x] Financial aggregation rule captured: `RecomputeHeaderTotals` post-load rule.
- [x] Commented-out column list captured: `OxygenSaturation` (header), `PatientVatAmount` + `ServiceEventType` (service).
- [ ] **Open — Q13:** views vs base tables for the new pipeline (the legacy SP uses views; base tables may offer better pagination/index behaviour but require DBA access for discovery).
- [ ] **Open — vendor-side:** Oracle connection details (host, port, service name, credentials) flowing through the cloud-config API. Engine confirmation lands with this.
- [ ] **Open — destination-side:** confirm NPHIES destination team accepts `provider_dhsCode` for Excys claims.

Phase 3 (Excys) is **unblocked for design** once Q13 is resolved. Vendor-side connection details can land during Phase 3 execution.

---

## 5. Per-vendor onboarding checklist (template)

For every new vendor, the first step is classification:

> Does the vendor's logic need a `for` loop, state across rows, or > conditional branching beyond a CASE/switch? If no → Tier 1 or

2. If > yes → Tier 3.

### 5.1 Tier 1 vendor

- [ ] Obtain read-only credentials to vendor DB.
- [ ] Author `{Vendor}_Mapping.md` in the existing style (see `Marshal/USP_FetchClaims_Mapping.md` for the format).
- [ ] Run `DescriptorGenerator` against the vendor DB; save the draft `{vendor}.vendor.json`.
- [ ] Fill in `transforms` and `postLoadRules` from the mapping doc, selecting entries from the catalog in `VendorDescriptors.md` §3 and §4.
- [ ] Fill in `childRebind` per-child. For each child, identify the SP's JOIN columns and collations.
- [ ] Run `DescriptorValidator` — fix any validation errors.
- [ ] Run `DescriptorTester` against the vendor DB for a week of data. Inspect emitted `ClaimBundle` JSON.
- [ ] Iterate descriptor until output matches expectations.
- [ ] PR: add `VendorDescriptors/{vendor}.vendor.json`. Validator runs in CI.
- [ ] Deploy to one pilot site. Flip `Sync:UsePluginPipeline:{VENDOR} = true`.
- [ ] Run `ParityReporter` for one parity cycle. Expect diff → zero.
- [ ] Flip flag site-wide. Retire SP.

### 5.2 Tier 2 vendor

Same as Tier 1, plus:

- [ ] Author `.sql` files for any non-generic queries, under `VendorDescriptors/{vendor}/sql/`.
- [ ] Reference them from the descriptor's `customSql` section.
- [ ] DBA reviews the SQL files in the PR.

### 5.3 Tier 3 vendor

- [ ] Obtain read-only credentials. Author mapping doc.
- [ ] Add folder `Vendors/{Vendor}/` with the conventional files: `{Vendor}ClaimSource.cs`, `{Vendor}Transform.cs`, and any helpers (grouper, resume cursor encoder, preview override).
- [ ] Add minimal Tier 3 descriptor (`{vendor}.vendor.json` with `tier: 3` and `pluginType`).
- [ ] Register in DI: `services.AddClaimSource<{Vendor}ClaimSource, {Vendor}Transform>("{CODE}")`.
- [ ] Unit-test all pure helpers independently of the DB.
- [ ] Integration-test against a seeded vendor DB snapshot.
- [ ] Run `ParityReporter` for one parity cycle.
- [ ] Feature-flag rollout same as Tier 1.

---

## 6. Related documents

- **`DHS_SyncLayer_Plan.md`** — overview, architecture, implementation plan, rollout phasing, open questions.
- **`DHS_SyncLayer_VendorDescriptors.md`** — descriptor JSON schema, transform catalog, post-load rule catalog, rebind model, validation gates, descriptor lifecycle / override governance, tooling.
- **`DHS_SyncLayer_OpenQuestions.md`** — consolidated list of questions pending product ops / ops confirmation before the plan can be treated as complete.
- **`NPHIES_DB_Documentation (1).md`** — destination schema reference. Authoritative for column names, types, and parent-child cardinalities.
- **`PLAN/VEND/{VENDOR}/USP_FetchClaims.sql` + `USP_FetchClaims_Mapping.md`** — legacy stored procedures and their mapping notes. Source of truth for the **logic** to replicate (not the **syntax** — these run on SQL Server destination via linked servers, while the new pipeline talks to each source directly).
