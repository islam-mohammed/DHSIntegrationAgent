# Sync Layer — Questions to Confirm

15 items (Q4 closed; Q1–Q3, Q5–Q15 active). Each has our default — reply "OK" to accept, or one line to flip it.

---

## 🚨 Blockers (3)

### 1. BilalSoft — Tier 2 SQL or Tier 3 C#?

BilalSoft groups service lines into claims (14-day gap + patient/doctor/claimtype change). Default: **Tier 3** (small C# grouper with unit tests). Flip to Tier 2 (CTE + `LAG()` in SQL)?

### 2. SQL aliasing — full or hybrid?

SQL handles the common cases (`UPPER`, `NVL`/`ISNULL`, `SUBSTR`, truncation). Quirks like BilalSoft's copay guard-asymmetry bug are cleaner in C#. Default: **hybrid** (SQL + ~5 named C# transforms). Flip to full SQL?

### 3. Cloud-config API — missing fields?

The API and its current JSON shape are already verified in code. The sync layer needs fields today's consumer does not read. Default set:

- `batchSize` (int, 50–300) — per-vendor page size
- `batchSize.tuningPolicy` — `static` (manual) or `adaptive` (agent adjusts based on observed latency / error rate)
- `retryPolicy` — max attempts, backoff, per-error-class overrides
- `connectionTimeoutSeconds` / `commandTimeoutSeconds`
- `enabled` (bool) — kill switch per vendor / per client without redeploy
- `pipelineVersion` (string) — pin a site to a known-good descriptor version

Approve this set, add more, or prune?

### ~~4. Excys — where is the spec?~~ ✅ CLOSED 2026-04-18

Spec delivered (`PLAN/VEND/exeys/USP_FetchClaims.sql` + mapping). Excys is Oracle (schema `DHS`) with Marshal-like structure but reads through **views** (`*_V` suffix) rather than base tables. See Playbook §4 for the full mapping. Replaced by **Q13** below.

---

## 🟠 Important (3)

### 13. Excys — views or base tables?

The legacy SP reads `DHS.DHSCLAIM_HEADER_V` etc. via `OPENQUERY`. The new direct-Oracle pipeline can query either the `*_V` views or the underlying base tables. Default: **use the views** (matches legacy SP behaviour exactly → lowest-risk parity + no DBA scavenger hunt for base-table names). Flip to "base tables" only if the DBA confirms (a) the base tables are accessible with our read-only credentials, (b) the views add non-trivial compute we want to bypass, and (c) there is no WHERE-clause filtering inside the view that we would silently lose. **Phase 3 blocker.**

### 5. Parity vs fix — 4 legacy-SP quirks

| # | Vendor | Behaviour | Default |
| --- | --- | --- | --- |
| a | BilalSoft | `REPLACE(patientname, '0', '')` strips every `0` from names | Fix |
| b | BilalSoft | `REPLACE(patientid, '.', '')` strips dots from IDs | Fix |
| c | BilalSoft | `REPLACE(TriageDate, ' ', NULL)` always returns NULL (SQL bug) | Preserve for parity |
| d | Marshal / AFI | Pulse clamp + Height/Weight `'0'→NULL` hit the **entire destination**, not just the current batch | Scope per-batch |

Approve all four, or flip any?

### 6. "Logic" — how detailed?

Schema (NPHIES) and Mapping (SQL aliases + vendor matrices) are covered. For Logic, default: **rule names + SP reference**, with pseudocode only for the multi-table back-fills (EMR, SFDA, member-id). Enough, or go all the way to C# signatures + tests?

---

## 🟡 Good-to-have (8)

Quick yes/no:

- **Q7** — Hardcoded `CompanyCode` deletes (`'12'`, `'INS02'`, `'1241020001'`, `'100002'`) live in descriptors, not code. OK?
- **Q8** — Catalogued collations: `default`, `Arabic_CI_AS`, `SQL_Latin1_General_CP1_CI_AS`. Any others to add?
- **Q9** — Descriptor site-edits: signed-by + append-only audit log + revert. OK for Ops?
- **Q10** — Pre-batch dialog: async "estimate first, exact on request" instead of blocking. OK to loop in the support lead?
- **Q11** — Legacy `Custom*Sql` columns (stored but unused): migrate into Tier 2 `.sql` files, deprecate silently, or reject at startup?
- **Q12** — `ParityReporter` PHI snapshots: in-memory only, or DPAPI-encrypted persistence?
- **Q14** — **Deployment topology.** The agent needs direct network access to each vendor DB (no linked servers). Default: **one agent VM per client site**, hosted on the client's LAN, reaching all in-scope vendor DBs the client has. Alternative: **one central VM per vendor** (agent-as-a-service). Also needs: firewall / ACL requirements on Oracle 1521 + SQL Server 1433, static outbound IP for cloud-config API, Windows Update / AV policy, backup / DR expectations for the SQLite cache. Who owns provisioning and the hardening baseline — DevOps, infra partner, or per-client IT?
- **Q15** — **Per-client performance tuning loop.** Q3 proposes `batchSize.tuningPolicy: static | adaptive`. If `adaptive`: what metrics does the agent emit (per-batch latency p50/p95, error rate, vendor-DB CPU observed via `sp_WhoIsActive` / `v$session`), where are they stored (local SQLite rolling window, cloud telemetry endpoint, both), and who decides when to tune — the agent (autonomous adjustment within bounds) or a human operator in a dashboard? Default: **static for Phase 0–3; add adaptive telemetry in Phase 5 (hardening)** with agent emitting metrics, human operator adjusting via cloud-config.

---

## ✅ Already settled — skip

4 vendors (2 Oracle: Marshal, Excys — 2 SQL Server: AFI, BilalSoft) · No linked servers · Batching 50–300 per client · Connection strings from cloud API → SQLite cache · SQL aliasing as primary mapping · Tier 1 + Tier 2 default (Tier 3 reserved, see Q1).

**Closed by code inspection — no team input needed:**

- Cloud-config API is live (`GET /api/Provider/GetProviderConfigration/{providerDhsCode}`), called on login by `LoginViewModel.cs:129`. Only the missing-field audit remains → Q3.
- Hardcoded `Server=.\SQLEXPRESS; Password=root` in `ProviderConfigurationService.cs:386-387` is a confirmed dev bug (the real line is commented out on 386). Two-line fix in Phase 0.

**Closed by vendor delivery — 2026-04-18:**

- Excys spec landed (`PLAN/VEND/exeys/`). Engine = Oracle, schema = `DHS`, reads through views. Original Q4 retired; residual sub-question split into Q13.

---

## Phase blockers

| Phase | Blocked on |
| --- | --- |
| 0 — config cleanup | Q3 (expanded field set) |
| 1 — framework + AFI | Q2, Q6, Q14 (deployment topology must be decided before first pilot) |
| 2 — Marshal (Oracle) | Q2 + Oracle schema confirmation (Playbook §1.1) |
| 3 — Excys | Q13 (views vs base tables) |
| 4 — BilalSoft | Q1, Q5 |
| 5 — hardening | Q15 (adaptive tuning telemetry model) |
