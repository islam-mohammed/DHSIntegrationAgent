# Jules Prompt Pack — DHS Integration Agent — Implement From Complete Spec

*Generated:* 2026-03-01 10:53:30Z UTC

Provide Jules with:
- swagger.json
- README_KB_ZERO_QUESTIONS_SPEC_COMPLETE.md

## Non‑Negotiable Constraints

- Implement all user journeys (§3) and end-to-end workflows (§4).
- Implement SQLite tables exactly as §6 (no missing columns).
- Implement leasing and state transitions exactly as §10.
- Implement domain mapping scan/post/enrich exactly as §11.
- Implement observability and support bundle export exactly as §13.
- Implement attachments manual upload + automatic retry exactly as §4.6/4.7.
- No PHI in logs/support bundle. Encrypt payloads and sensitive fields at rest.

## Implementation Milestones (Journey‑driven)

### M1: Foundation: WPF shell, navigation, DI, configuration, logging (PHI-safe)
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M2: SQLite: migrations + repositories + UnitOfWork; implement all tables from §6
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M3: Security: encryption at rest (payload + sensitive fields); DPAPI key ring
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M4: HTTP: swagger clients + auth handler + ApiCallLog handler
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M5: Provider Config: cache + upsert payers/mappings/settings (§4.1)
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M6: Adapters: DbEngine factory + Tables/Views/Custom SQL modes (§8); include schema probe and template safety
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M7: ClaimBundleBuilder + sanitization + payload staging (§9, §4.2)
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M8: Streams A/B/C/D per §4 and §10; include manual packet retry per §4.4
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M9: Attachments manual stream + Attachment Retry Stream per §4.6/4.7
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M10: Domain Mapping wizard journey + posting and refresh per §3.5 and §11
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M11: Diagnostics journey: ApiCallLog viewer + Dispatch viewer + Support Bundle export (§3.7, §13)
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

### M12: Acceptance tests per §14
- Deliver full code + tests; no placeholders.
- Ensure the milestone compiles and passes tests.

## Prompt Template (Copy/Paste per milestone)

**Goal:** <Milestone title>

**Spec sections to implement:** <e.g., §3.7, §13>

**Rules:** no PHI logs; encrypt at rest; MVVM boundaries; leasing

**Deliverables:** full implementation for all changed files + tests

**Acceptance:** list verification steps from §14
