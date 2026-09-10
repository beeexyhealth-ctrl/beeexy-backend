# Codex Prompt — Implement Beeexy Phase 11.6

Implement **Phase 11.6 — Export Foundation + Beeexy JSON** in the Beeexy backend repository.

This task is strictly limited to **Phase 11.6 only**.

Do **not** implement Phase 11.7 or Phase 11.8 behavior, even if Phase 11.6 finishes successfully before the session ends.

Use the current authoritative implementation plan:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Before changing code, read:
- the complete Phase 11 section;
- completed Phase 11.1–11.5 subsections;
- the formal Phase 11.6 subsection;
- the current `ExportArtifact` domain/persistence model from Phase 11.1;
- the canonical shared health snapshot/projection introduced in Phase 11.4;
- existing patient authorization patterns from Phase 3;
- existing idempotency conventions from Phase 11.2 and earlier phases;
- existing private file/storage abstractions if any;
- existing background/retention conventions if any.

Preserve all previously completed behavior from Phases 1–10 and Phase 11.1–11.5.

Follow the existing Beeexy architecture, naming conventions, layering, Problem Details conventions, PostgreSQL/EF Core patterns, privacy/logging rules, storage/configuration conventions, OpenAPI conventions, and testing style.

---

# Primary Objective

Implement exactly:

`POST /api/v1/patients/{id}/exports`

Phase 11.6 must establish the common immutable export pipeline and fully implement **Beeexy JSON** export.

This subphase includes:
- patient-authorized export creation;
- canonical snapshot reuse;
- Beeexy JSON rendering;
- immutable bytes;
- checksum generation;
- private artifact persistence;
- `ExportArtifact` lifecycle metadata;
- configurable retention metadata;
- idempotency/concurrency;
- safe API contract;
- focused tests;
- directly related regressions.

Do not implement export download yet.

Do not implement PDF or FHIR generation yet.

---

# Strict Scope Boundary

Phase 11.6 includes:
- `POST /api/v1/patients/{id}/exports`;
- Beeexy JSON as the executable format;
- export request validation;
- authorization;
- canonical snapshot reuse;
- Beeexy JSON renderer;
- checksum;
- private artifact storage abstraction/use;
- Development/Test local private storage adapter;
- `ExportArtifact` persistence;
- retention metadata;
- export creation idempotency/concurrency;
- OpenAPI;
- focused tests.

Phase 11.6 does **not** include:
- `GET /api/v1/exports/{id}/content`;
- PDF generation;
- PDF library selection;
- Phase 6 FHIR export execution;
- share-token artifact download;
- patient-facing export download;
- export download activity;
- QR behavior;
- frontend export UI;
- long-term legal/compliance retention policy.

---

# Authentication and Authorization

Authentication:
- normal account Bearer required.

Authorization:
- preserve the export-generation patient authority already defined by the authoritative implementation plan;
- do not invent broader authority;
- do not use Beeexy ID as authorization;
- do not treat UUID knowledge as authority;
- inaccessible patient IDs follow concealed `404` behavior where the existing patient-scoped contract requires it.

Important: the Primary-only restriction from Phase 11.2 applies to **external share creation**. Do not automatically copy that restriction to exports if the plan still defines exports using existing patient authority.

---

# Request Contract

Implement the smallest exact request contract required by the current plan.

At minimum identify:
- export format;
- idempotency input according to repository conventions;
- approved snapshot/source selection if any.

Executable format in this subphase:
- `BeeexyJson`

PDF and FHIR JSON must not be generated yet.

If the final stable schema already accepts PDF/FHIR enum values, return the documented safe format-unavailable behavior without implementing those renderers.

Do not accept caller-supplied:
- patient ownership;
- creator/account identity;
- timestamps;
- artifact ID;
- checksum;
- storage URI/key;
- retention expiry;
- internal status;
- arbitrary clinical payload;
- raw snapshot JSON.

The server builds the snapshot.

---

# Canonical Snapshot Reuse

Reuse the canonical shared health snapshot/projection from Phase 11.4.

Do not create a second independently maintained FullProfile definition.

Beeexy JSON must derive from the same approved health snapshot used by shared FullProfile.

This must prevent divergence among:
- shared web profile;
- Beeexy JSON;
- future PDF.

The snapshot remains:
- allow-listed;
- deterministic;
- read-only;
- patient-authorized;
- provider-neutral.

Do not serialize EF entities or raw database rows directly.

FHIR remains a separate Phase 6-only path.

---

# Beeexy JSON Content

Beeexy JSON is a Beeexy-native immutable export.

Include only the canonical approved health snapshot plus safe envelope metadata such as:
- export format/version;
- generatedAt;
- snapshot version;
- approved provenance metadata;
- canonical health snapshot payload.

Do not include:
- capability;
- capability hash;
- ShareAccess token;
- Bearer token;
- Account IDs;
- private storage URI/key;
- audit/security internals;
- logs;
- full AI Conversations;
- prompts;
- provider/model configuration;
- raw/rejected AI output;
- backend-only fields.

Use an explicit stable DTO/serialization contract.

Do not serialize EF/domain entities directly.

---

# Determinism

Generated JSON should be deterministic enough for stable artifact checksum semantics.

Control:
- property naming;
- ordering where relevant;
- UTC timestamps;
- null handling;
- format/version metadata;
- encoding.

Checksum stability is over the concrete generated artifact bytes.

Document/test the actual behavior.

---

# ExportArtifact

Reuse the Phase 11.1 `ExportArtifact` model.

Persist, as already modeled:
- artifact ID;
- patient ID;
- format;
- media type;
- checksum;
- private storage identity;
- created time;
- retention/deletion eligibility;
- lifecycle/status metadata if applicable;
- snapshot/source identity if part of current design.

Do not expose storage identity publicly.

Artifact bytes are immutable after successful creation.

Later patient/source changes must not rewrite an old artifact.

---

# Checksum

Generate a cryptographic checksum over the **exact stored artifact bytes**.

Use the repository-approved primitive; SHA-256 is acceptable if no conflicting existing convention exists.

Tests must prove:
- checksum matches stored bytes;
- content mutation changes checksum;
- returned metadata matches persisted checksum.

Do not checksum a different representation than the stored bytes.

---

# Private Artifact Storage

Use a provider-neutral abstraction equivalent to `IPrivateArtifactStorage` or the repository's existing equivalent.

Application/Domain must not depend on a vendor SDK.

## Development/Test

Implement or complete a local private filesystem adapter.

Requirements:
- not served through ASP.NET static files;
- no public URL;
- configured private root;
- safe generated storage keys;
- no path traversal;
- atomic write behavior where practical;
- cancellation-aware;
- suitable for automated tests.

## Production

Keep the provider-neutral abstraction/configuration boundary.

Do not force a vendor into Domain/Application.

Do not implement S3/R2/Azure unless the authoritative Phase 11.6 plan explicitly requires it now.

Do not add signed/public URLs.

Download belongs to Phase 11.7.

---

# Storage Failure Semantics

Artifact creation must fail safely if storage fails.

Avoid:
- DB says completed while bytes are missing;
- permanently orphaned bytes after DB failure where cleanup is feasible;
- partial content treated as completed.

Use the smallest reliable transaction/compensation/status pattern consistent with the existing architecture.

Do not over-engineer distributed transactions.

Add focused tests for the chosen failure path.

---

# Retention

Approved MVP retention:

**30 days, configurable**

Phase 11.6 must represent retention truthfully.

Use server-derived retention/deletion-eligibility metadata.

Do not accept arbitrary client retention.

Do not present 30 days as a permanent legal/compliance rule.

Artifact expiry must never delete patient/source clinical records.

Implement an actual cleanup worker only if the formal Phase 11.6 plan explicitly assigns it here; otherwise persist eligibility metadata only.

---

# Idempotency

Export creation must be retry-safe.

Reuse existing Beeexy idempotency infrastructure where appropriate.

Fingerprint must cover the logical export request, including:
- patient;
- format;
- approved snapshot/source-selection inputs.

Required behavior:
- first request creates one logical artifact;
- exact replay returns same logical artifact/result according to existing API conventions;
- incompatible same-key reuse => `409`;
- concurrent same-key requests converge safely;
- distinct keys may create distinct immutable snapshots.

Use PostgreSQL uniqueness/transactions where needed.

Do not rely on process-local locking as final authority.

---

# Snapshot Timing

Define one clear logical snapshot point.

After successful storage:
- later patient changes do not mutate old artifact;
- source changes/deletion do not silently regenerate artifact;
- checksum remains tied to stored bytes.

Do not implement live/dynamic export content.

---

# Response Contract

Return safe artifact metadata only.

Likely fields:
- exportArtifactId;
- format;
- mediaType;
- checksum;
- createdAt;
- retentionExpiresAt/deleteEligibleAt equivalent;
- status if part of approved contract.

Do not return:
- raw bytes;
- private storage key/path;
- bucket information;
- capability;
- share token;
- Account ID;
- audit internals.

Preserve `201 Created` if defined by the plan.

Do not add a download/content URL unless explicitly defined by the current contract.

---

# Error / Problem Details

Follow backend conventions.

Expected safe behavior:
- malformed JSON => `400`;
- unauthenticated => `401`;
- inaccessible patient => concealed `404`;
- unsupported/unavailable format => `422`;
- invalid selection => `422`;
- incompatible idempotency reuse => `409`;
- storage/generation failure => safe contract-specific failure;
- unexpected => safe `500`.

Never leak private storage paths, stack traces, provider details, or hidden patient existence.

---

# OpenAPI

Add exactly:

`POST /api/v1/patients/{id}/exports`

Document:
- Bearer security;
- request schema;
- Beeexy JSON executable format;
- safe response metadata;
- expected `201`, `400`, `401`, concealed `404`, `409`, `422`, safe `500`.

Do not add:

`GET /api/v1/exports/{id}/content`

Do not add Phase 11.7 routes.

OpenAPI path count should change only as expected from the Phase 11.5 baseline.

---

# FHIR Boundary

Phase 11.6 does **not** implement FHIR export.

Strict rule:

**Phase 11 must not create a new FHIR mapping path.**

Phase 11.7 will delegate FHIR JSON exclusively to Phase 6.

Do not:
- modify Phase 6 mappings;
- create a new mapper;
- add new resources for Symptom Diary/Second Opinion;
- introduce another FHIR pipeline.

If FHIR is accepted by the stable request schema, return safe unavailable behavior for this subphase.

---

# PDF Boundary

Do not implement PDF generation.

Do not select/install a PDF library.

Do not create PDF/HTML templates.

Leave concrete PDF work to Phase 11.7.

---

# Share Recipient Download Boundary

Do not implement share-token artifact access.

Do not authorize ShareAccess token for export creation.

Do not implement artifact ShareGrantItem checks yet.

That belongs to Phase 11.7.

---

# Persistence / EF Core

Reuse existing `sharing.export_artifacts`.

Only change EF/PostgreSQL if Phase 11.6 truly requires missing fields/constraints/indexes.

If no model change:
- do not create an empty migration;
- verify no pending model changes.

If persistence changes:
- smallest additive migration;
- preserve existing data;
- focused migration tests;
- apply/rollback/reapply where repository conventions require;
- confirm no pending model changes.

Potential justified changes include idempotency metadata, retention indexes, or artifact status constraints only if genuinely missing.

---

# Security / Privacy

Non-negotiable:
- no raw clinical payload in logs;
- no artifact bytes in logs;
- no private storage key/path in public API;
- no bearer/share tokens in logs;
- no capability/hash in logs;
- no direct entity serialization;
- no cross-patient export;
- no Beeexy-ID authority;
- no UUID-only authority;
- immutable artifact bytes;
- checksum over exact stored bytes;
- private storage only;
- no public static-file exposure.

Add focused privacy/logging tests where practical.

---

# Mandatory Testing Policy — Optimize Execution Time

Codex must create or update **all tests necessary** to correctly cover Phase 11.6 while minimizing unnecessary execution time.

## During implementation

Run only focused tests related to:
- export use case;
- canonical snapshot reuse;
- Beeexy JSON renderer;
- private storage adapter;
- checksum;
- ExportArtifact persistence;
- idempotency/concurrency;
- patient authorization;
- OpenAPI;
- directly modified snapshot/read modules.

Prefer narrow test filters/projects.

Do not repeatedly execute full repository-wide unit/integration suites.

## Mandatory closing validation

Before marking Phase 11.6 complete, run at minimum:
1. all new Phase 11.6 tests;
2. affected sharing/export module tests;
3. directly related Phase 11.1 ExportArtifact regressions;
4. directly related Phase 11.4 canonical snapshot regressions;
5. relevant Phase 11.2 idempotency regressions if reused;
6. directly affected patient authorization regressions;
7. local private storage tests;
8. checksum/immutability tests;
9. OpenAPI/contract tests;
10. persistence/migration tests only if EF/PostgreSQL changes;
11. builds for affected projects;
12. EF pending-model check;
13. migration checks when applicable;
14. formatting / whitespace / `git diff --check`.

## Global suite policy

**Do not automatically run the full repository-wide unit or integration suites as a Phase 11.6 completion criterion.**

Full regression is reserved for:
- final Phase 11 closure;
- explicit user request;
- or a genuinely high-risk transversal change requiring broader validation.

If Codex believes a global suite is essential, it must briefly justify why focused coverage is insufficient.

Do not run global suites merely out of habit.

## Historical expectation failure policy

If many tests fail due to one obsolete shared expectation:
1. identify the common root cause;
2. determine whether Phase 11.6 legitimately changes the contract;
3. update only if justified;
4. rerun the smallest affected group first;
5. do not immediately run the full repository.

## Test integrity

Do not:
- delete tests;
- disable tests;
- skip tests;
- reduce assertions;
- weaken coverage;
- modify historical tests merely to force green.

Historical tests may change only when Phase 11.6 legitimately changes the expected contract.

---

# Focused Test Requirements

Create focused coverage for at least:

## Authorization
- authorized patient export success;
- inaccessible/foreign patient concealed;
- Beeexy ID grants nothing;
- UUID knowledge grants nothing;
- ShareAccess token cannot create exports;
- normal Bearer required.

## Beeexy JSON
- valid creation;
- canonical snapshot reused;
- expected allow-listed content present;
- excluded/internal content absent;
- full AI Conversations absent;
- valid JSON;
- stable explicit format/version metadata;
- UTC timestamps;
- no EF/internal fields.

## Artifact immutability
- stored bytes match produced bytes;
- later patient/source changes do not rewrite old artifact;
- old checksum unchanged;
- new export may create new artifact.

## Checksum
- matches exact stored bytes;
- changed bytes => different checksum;
- persisted checksum equals returned metadata.

## Private storage
- writes under configured private root;
- no public static-file exposure;
- safe generated key;
- no path traversal;
- cancellation/failure safe.

## Storage failure
- no false completed artifact;
- compensation/status behavior matches design;
- no private path leak.

## Retention
- 30-day configured retention represented correctly;
- server-derived;
- client cannot override;
- source records unaffected.

## Idempotency
- first create;
- exact replay;
- incompatible same key => `409`;
- concurrent same key => one logical artifact;
- distinct keys => distinct artifacts;
- no orphan/duplicate completed artifacts.

## Unsupported formats
Until Phase 11.7:
- PDF safely unavailable if accepted by schema;
- FHIR JSON safely unavailable if accepted by schema;
- no rendering/mapping side effect.

## OpenAPI
- POST export route exists;
- Bearer security correct;
- safe schemas;
- storage internals absent;
- no download endpoint;
- no Phase 11.7+ route.

---

# Direct Regression Set

Run focused regressions for dependencies actually touched:
- Phase 11.1 ExportArtifact domain/persistence;
- Phase 11.2 idempotency infrastructure if reused;
- Phase 11.4 canonical shared snapshot;
- Phase 3 patient authority;
- Phase 4/5/9/10 reads only if canonical snapshot code is modified;
- configuration/startup validation;
- OpenAPI route/auth generation.

Do not run unrelated modules unless directly affected.

---

# Build / EF / Migration

Build affected projects only, including directly/transitively changed Domain/Application/Infrastructure/API/test projects.

Use repository-standard Debug/locked restore conventions.

If no EF change:
- confirm no pending model changes;
- no empty migration.

If EF/PostgreSQL changes:
- smallest additive migration;
- focused migration tests;
- apply/rollback/reapply where conventions require;
- confirm no pending model changes.

---

# Implementation Plan Update

If and only if implementation and required focused validation pass:

Update:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Mark only Phase 11.6 complete.

Record factual implementation details:
- export creation endpoint;
- actual authorization contract;
- request/response contract;
- canonical snapshot reuse;
- Beeexy JSON renderer/version;
- checksum behavior;
- private storage abstraction/adapter;
- retention behavior;
- idempotency/concurrency;
- persistence/migration status;
- explicit confirmation that PDF/FHIR/download are not implemented.

Record only tests/checks actually executed.

Explicitly state which global suites were **not** executed.

Use wording equivalent to:

`The complete repository-wide unit and integration suites were intentionally not run for Phase 11.6 under the subphase testing policy; full regression is reserved for Phase 11 closure unless explicitly requested.`

Do not fabricate counts.

End with:

**Phase 11.7 has not started.**

---

# Explicitly Out of Scope

Do not implement Phase 11.7:
- PDF generation;
- concrete PDF renderer/library selection;
- Phase 6 FHIR export execution;
- `GET /api/v1/exports/{id}/content`;
- patient download;
- ShareAccess download;
- explicit artifact ShareGrantItem authorization;
- Downloaded activity event.

Do not implement Phase 11.8 final closure.

Also exclude:
- frontend export UI;
- signed/public URLs;
- recipient accounts;
- provider portal;
- new clinical logic;
- AI reinterpretation;
- new FHIR mappings;
- Case semantics;
- Visit sharing.

---

# Non-Negotiable Rules

1. Implement only Phase 11.6.
2. Add exactly one endpoint: `POST /api/v1/patients/{id}/exports`.
3. Beeexy JSON is the only fully executable export format now.
4. Reuse the Phase 11.4 canonical snapshot.
5. Do not serialize EF/domain entities directly.
6. Store immutable bytes privately.
7. Checksum covers exact stored bytes.
8. Retention is 30 days by approved configurable policy.
9. Do not expose storage identity.
10. Do not implement download yet.
11. Do not implement PDF yet.
12. Do not implement FHIR export yet.
13. Do not create a new FHIR mapping path.
14. Follow existing repository architecture/conventions.
15. Do not weaken/delete/skip tests.
16. Do not automatically run global unit/integration suites unless justified by transversal risk.
17. Do not mark complete unless required focused validation passes.

---

# Expected Final Codex Response

When finished, report concisely:

1. What Phase 11.6 implemented.
2. Main files modified.
3. Exact export endpoint contract.
4. Authorization behavior.
5. Canonical snapshot reuse.
6. Beeexy JSON contract/version behavior.
7. Checksum behavior.
8. Private storage implementation/configuration.
9. Retention behavior.
10. Idempotency/concurrency behavior.
11. Persistence/migration changes or confirmation none were needed.
12. Tests executed.
13. Results of each executed test group.
14. Builds/checks executed.
15. OpenAPI path count.
16. EF pending-model status.
17. Global unit/integration suites **not executed** under the subphase policy.
18. Remaining risk/validation deferred to final Phase 11 regression.
19. Confirmation the implementation plan was updated.
20. Confirmation that **Phase 11.7 was not started**.

If required focused validation fails, do not claim Phase 11.6 complete. Report the exact blocker and smallest relevant failing test group.
