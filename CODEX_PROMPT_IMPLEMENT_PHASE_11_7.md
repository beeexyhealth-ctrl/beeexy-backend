# Codex Prompt — Implement Beeexy Phase 11.7

Implement **Phase 11.7 — Human-Readable PDF + Phase-6 FHIR JSON + Artifact Download** in the Beeexy backend repository.

This task is strictly limited to **Phase 11.7 only**.

Do **not** implement Phase 11.8 closure behavior beyond the focused validation required to complete 11.7.

Use the current authoritative implementation plan:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Before changing code, read:
- the complete Phase 11 section;
- completed Phase 11.1–11.6 subsections;
- the formal Phase 11.7 subsection;
- the current `ExportArtifact` model/persistence;
- the Phase 11.4 canonical shared health snapshot;
- the Phase 11.6 Beeexy JSON export pipeline;
- the existing Phase 6 FHIR export/validation pipeline;
- the current ShareAccess token and ShareGrant scope/item model;
- Phase 11.5 revocation/expiry/current-grant revalidation behavior;
- current patient authorization patterns;
- existing private artifact storage abstractions;
- existing privacy/logging and OpenAPI conventions.

Preserve all previously completed behavior from Phases 1–10 and Phase 11.1–11.6.

Follow the existing Beeexy architecture, naming conventions, layering, Problem Details conventions, PostgreSQL/EF Core patterns, privacy/logging rules, storage/configuration conventions, OpenAPI conventions, and testing style.

---

# Primary Objective

Complete the Phase 11 export surface by implementing:

1. human-readable PDF export;
2. validated FHIR JSON export by delegating exclusively to Phase 6;
3. artifact content download.

The existing export creation endpoint is:

`POST /api/v1/patients/{id}/exports`

Extend it so the final supported formats are:
- `BeeexyJson`
- `Pdf`
- `FhirJson`

Add exactly one new endpoint:

`GET /api/v1/exports/{id}/content`

This subphase must also implement:
- patient/account Bearer download;
- ShareAccess recipient download only when the artifact is explicitly covered by the ShareGrant scope/items;
- correct media types;
- revocation/expiry revalidation;
- safe Downloaded activity behavior where required;
- focused tests and directly related regressions.

Do not pull Phase 11.8 global closure forward.

---

# Strict Scope Boundary

Phase 11.7 includes:
- PDF renderer implementation;
- PDF creation through the existing POST endpoint;
- FHIR JSON creation through the existing POST endpoint;
- strict Phase 6 FHIR delegation;
- artifact download endpoint;
- correct media types;
- private artifact retrieval;
- patient-authorized download;
- ShareAccess-authorized download with explicit artifact authorization;
- current ShareGrant revalidation;
- Downloaded activity behavior if defined;
- focused tests;
- OpenAPI.

Phase 11.7 does **not** include:
- Phase 11.8 full repository regression;
- new FHIR mappings;
- new clinical semantics;
- frontend export UI;
- recipient accounts;
- provider portal;
- manager sharing authority;
- Case semantics;
- Visit sharing;
- Phase 13 behavior.

---

# Existing Export Creation Endpoint

Reuse and extend:

`POST /api/v1/patients/{id}/exports`

Do not create another export-creation endpoint.

Preserve the Phase 11.6 request/response/idempotency contract unless the authoritative plan explicitly requires a narrow correction.

Supported formats after 11.7:
- `BeeexyJson`
- `Pdf`
- `FhirJson`

Do not add other formats.

---

# PDF Export

Implement a server-side PDF renderer behind a provider-neutral abstraction such as:

`IPdfExportRenderer`

or the repository-equivalent interface.

Application/Domain must not depend on the concrete PDF library.

Use the Phase 11.4 canonical shared health snapshot as the source.

Do not independently reconstruct patient data for PDF.

Do not add clinical interpretation.

The PDF must be an immutable rendered snapshot.

---

# PDF Content

Include, when present in the canonical approved snapshot:
- Beeexy branding/title;
- patient name;
- approved basic demographics;
- generated timestamp;
- Clinical History;
- completed Pre-Triage;
- Symptom Diary;
- patient-visible Second Opinion results;
- minimal useful version/provenance metadata;
- concise export/disclaimer text;
- page/footer metadata where appropriate.

Do not include:
- full AI Conversations;
- prompts/messages;
- provider/model metadata;
- raw/rejected AI output;
- Account IDs;
- auth/session data;
- capability/token/hash;
- storage paths/keys;
- audit/security internals;
- arbitrary raw JSON dumps.

Do not embed a QR code in the PDF for MVP.

---

# PDF Renderer Selection

Select a .NET-compatible server-side PDF library only after inspecting:
- repository compatibility;
- licensing;
- runtime/deployment suitability;
- Linux/container suitability;
- deterministic/non-interactive rendering behavior.

Prefer a library that:
- does not require browser automation;
- does not require external SaaS;
- can be abstracted cleanly behind the application interface.

Do not introduce a headless-browser dependency unless already justified by the repository and plan.

Document the selected library and license consideration in the implementation-plan update.

---

# FHIR JSON — Strict Phase 6 Delegation

This is non-negotiable:

**Phase 11.7 MUST NOT implement a new FHIR mapping path.**

FHIR JSON export must delegate exclusively to the existing Phase 6 export/validation pipeline.

Do not:
- create a new mapper;
- duplicate Phase 6 mapping logic;
- add new FHIR resources;
- map Symptom Diary to new resources unless Phase 6 already supports it;
- map Second Opinion into new resources;
- invent profile URLs;
- invent terminology;
- claim validity without Phase 6 validation;
- silently fall back to Beeexy JSON.

If Phase 6 cannot truthfully produce a validated export for the request, return the documented safe `422`.

---

# FHIR Content Boundary

FHIR JSON may be narrower than Beeexy JSON/PDF.

That is acceptable.

Do not force Phase 9/10 content into FHIR if Phase 6 does not support it.

Use only Phase 6-supported validated resources.

---

# FHIR Artifact Generation

When `FhirJson` is requested:

1. authorize patient;
2. invoke existing Phase 6 pipeline;
3. obtain validated FHIR JSON bytes;
4. fail safely if mapping/validation is unavailable;
5. compute checksum over exact bytes;
6. store bytes privately;
7. persist immutable ExportArtifact metadata;
8. preserve Phase 11.6 idempotency behavior.

Do not reserialize validated FHIR JSON afterward in a way that could invalidate it.

---

# Media Types

Use exact repository/Phase 6 conventions.

At minimum:
- Beeexy JSON: existing Phase 11.6 media type;
- PDF: `application/pdf` unless the plan specifies otherwise;
- FHIR JSON: exact Phase 6 media type.

Do not invent a competing FHIR media type.

Download must return the persisted correct media type.

---

# Artifact Download Endpoint

Implement exactly:

`GET /api/v1/exports/{id}/content`

Do not add any other Phase 11 endpoint.

Return the exact immutable stored bytes.

Do not regenerate content from current patient state.

---

# Bearer Download Authorization

Normal account Bearer may download only when existing patient authority permits access to the artifact's patient.

Do not use:
- Beeexy ID;
- artifact UUID knowledge;
- Account ID from request;
- storage key knowledge

as authority.

Use concealed `404` for foreign/inaccessible artifacts where required by current conventions.

---

# ShareAccess Download Authorization

ShareAccess recipient download is allowed only if:

1. ShareAccess token is valid;
2. backing ShareGrant exists;
3. grant is not revoked;
4. grant is not expired;
5. token/grant relationship is valid;
6. artifact belongs to same patient;
7. artifact is **explicitly authorized by the grant scope/items**.

Important:

`FullProfile` does **not** automatically grant access to every historical ExportArtifact belonging to the patient.

Do not infer:
- same patient => all exports;
- FullProfile => all artifacts;
- SpecificRecords => all exports.

Use explicit stored ShareGrantItem/artifact authorization.

If the item model needs an explicit artifact category/reference, add only the narrowest support required by the authoritative Phase 11.7 plan.

Do not redesign the whole scope model.

---

# Explicit Artifact Authorization

A recipient may download an artifact only when that artifact is explicitly covered by the grant.

Use the existing typed ShareGrantItem representation if available.

If a new item category such as `ExportArtifact` is required, add only the minimal additive support.

Do not add wildcard authorization.

Do not automatically add future exports to old grants.

---

# ShareAccess Revalidation

Every ShareAccess download must re-check current ShareGrant state.

A previously issued token must stop working after:
- revoke;
- expiry.

Reuse Phase 11.5 locking/revalidation behavior where appropriate.

Do not rely only on JWT expiry.

---

# Artifact Status

Download must fail safely if artifact is not completed/readable.

Expected behavior:
- missing/foreign => concealed `404` where appropriate;
- incomplete/pending => `409` if that is the current plan contract;
- failed/unavailable => safe lifecycle-specific error;
- expired/deleted bytes => safe unavailable/not-found behavior;
- completed => exact stored bytes.

Do not regenerate missing bytes.

---

# Private Storage Retrieval

Use `IPrivateArtifactStorage` or current equivalent.

Do not expose private paths/keys.

Do not redirect to local filesystem paths.

Do not add public static-file serving.

Do not expose bucket/object identifiers.

Preserve cancellation and safe stream disposal.

---

# Download Response

Return:
- exact stored bytes/stream;
- correct `Content-Type`;
- safe filename/content-disposition only if current conventions support it;
- privacy-safe cache behavior such as `Cache-Control: no-store` where appropriate.

Do not include sensitive metadata in headers.

---

# Downloaded Activity Event

If the authoritative plan assigns it here, append a safe `Downloaded` event for successful ShareAccess recipient downloads.

Patient-facing activity may later expose only:
- event type;
- timestamp;
- safe resource category;
- success outcome.

Do not store:
- token;
- capability/hash;
- full IP;
- full User-Agent;
- artifact bytes;
- storage key/path;
- clinical payload.

Do not create successful Downloaded events for failed authorization.

Preserve event idempotency semantics where applicable.

---

# Export Creation Idempotency

Preserve Phase 11.6 idempotency across all formats.

Required:
- exact replay returns same logical artifact;
- incompatible same-key reuse => `409`;
- same key + different format => conflict;
- concurrent same-key requests converge;
- distinct keys may create distinct artifacts.

Renderer failures must not poison idempotency state as successful.

Use PostgreSQL/database authority where needed.

---

# Storage / Persistence Failure Semantics

For PDF/FHIR creation, preserve Phase 11.6 compensation rules.

Do not leave:
- completed metadata without bytes;
- orphaned bytes where cleanup is feasible;
- invalid checksum metadata;
- partial artifact marked ready.

If storage succeeds but DB commit fails, clean up according to the existing transaction pattern.

---

# Persistence / EF Core

Prefer reusing existing Phase 11 schema.

A migration is allowed only if 11.7 genuinely requires:
- explicit ExportArtifact grant-item category support;
- missing status/index/constraint;
- another narrow persistence change.

If no model change:
- do not create an empty migration;
- verify no pending model changes.

If migration is required:
- smallest additive migration;
- preserve existing data;
- focused migration tests;
- apply/rollback/reapply where conventions require;
- confirm no pending model changes.

Do not add 11.8-only governance tables.

---

# Error / Problem Details

Follow existing conventions.

Export creation:
- malformed => `400`;
- unauthenticated => `401`;
- inaccessible patient => concealed `404`;
- mapping unavailable => `422`;
- idempotency conflict => `409`;
- safe generation/storage failure => contract-consistent safe error;
- unexpected => `500`.

Artifact download:
- missing auth => `401`;
- invalid ShareAccess => `401`;
- revoked/expired share => `401` or approved recipient-safe equivalent;
- valid share token but artifact out of scope => `403`;
- foreign/inaccessible artifact => concealed `404`;
- incomplete artifact => `409`;
- missing/deleted content => safe unavailable/not-found behavior;
- unexpected => safe `500`.

Do not reveal:
- hidden patient existence;
- storage location;
- capability details;
- internal FHIR diagnostics;
- library/provider stack traces.

---

# OpenAPI

Complete the POST export contract for:
- BeeexyJson;
- Pdf;
- FhirJson.

Add exactly one new path:

`GET /api/v1/exports/{id}/content`

Document:
- supported authentication/security behavior;
- media/content response;
- safe errors;
- no internal storage fields.

Do not add Phase 11.8 endpoints.

OpenAPI path count should change only as expected from Phase 11.6 baseline.

---

# Security / Privacy

Non-negotiable:
- no artifact bytes in logs;
- no PDF content in logs;
- no FHIR payload in logs;
- no private storage key/path in API/logs;
- no capability/hash in logs;
- no ShareAccess/Bearer token in logs;
- no cross-patient download;
- no UUID-only authority;
- no Beeexy-ID authority;
- ShareAccess download requires explicit artifact authorization;
- FullProfile does not auto-authorize historical artifacts;
- revoked/expired grant blocks download;
- no public artifact storage;
- no new FHIR mapping;
- no clinical reinterpretation.

Add focused privacy tests where practical.

---

# Mandatory Testing Policy — Optimize Execution Time

Codex must create/update **all tests necessary** to correctly cover Phase 11.7 while minimizing unnecessary execution time.

## During implementation

Run only focused tests related to:
- PDF renderer;
- Phase 6 FHIR delegation;
- export creation;
- artifact download;
- storage retrieval;
- ShareAccess artifact authorization;
- patient authorization;
- lifecycle revalidation;
- Downloaded event;
- OpenAPI;
- persistence changes if any.

Prefer narrow filters/projects.

Do not repeatedly run full repository-wide unit/integration suites.

## Mandatory closing validation

Before marking Phase 11.7 complete, run at minimum:
1. all new Phase 11.7 tests;
2. affected export/sharing module tests;
3. directly related Phase 11.6 export regressions;
4. directly related Phase 11.4 canonical snapshot regressions;
5. directly related Phase 11.5 revoke/expiry regressions;
6. directly affected Phase 6 FHIR export/validation tests;
7. PDF renderer focused tests;
8. artifact storage/download focused tests;
9. ShareAccess explicit-artifact authorization tests;
10. OpenAPI/contract tests;
11. persistence/migration tests only if EF/PostgreSQL changes;
12. builds for affected projects;
13. EF pending-model check;
14. migration checks when applicable;
15. formatting / whitespace / `git diff --check`.

## Global suite policy

**Do not automatically run the full repository-wide unit or integration suites as a Phase 11.7 completion criterion.**

Full regression is reserved for:
- Phase 11.8 final closure;
- explicit user request;
- or a genuinely high-risk transversal change requiring broader validation.

If Codex believes a global suite is essential before 11.8, it must briefly justify why focused coverage is insufficient.

Do not run global suites merely out of habit.

## Historical expectation failure policy

If many tests fail due to one obsolete shared expectation:
1. identify the root cause;
2. determine whether Phase 11.7 legitimately changes the contract;
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

Historical tests may change only where Phase 11.7 legitimately changes the expected contract.

---

# Focused Test Requirements

Create focused coverage for at least:

## PDF creation
- authorized creation success;
- PDF accepted;
- correct media type;
- valid PDF bytes;
- expected human-readable sections where practical;
- canonical snapshot reused;
- no full AI Conversations;
- no hidden/internal fields;
- checksum matches exact bytes;
- retention preserved;
- idempotency preserved;
- renderer failure does not create false completed artifact.

## FHIR creation
- authorized FHIR success when Phase 6 can map/validate;
- exact delegation to Phase 6;
- validated output stored unchanged;
- correct FHIR media type;
- checksum matches exact bytes;
- Phase 6 unavailable => `422`;
- validation failure => safe approved failure;
- no new Phase 11 FHIR mapper exists;
- unsupported Phase 9/10 content is not invented into FHIR.

## Bearer download
- authorized patient downloads completed artifact;
- foreign/inaccessible artifact concealed;
- exact bytes returned;
- correct media type;
- no regeneration;
- incomplete => `409`;
- missing content safe failure;
- no private path exposure.

## ShareAccess download
- explicitly authorized artifact => success;
- same-patient unlisted artifact => `403`;
- FullProfile without explicit artifact item => denied;
- wrong-patient artifact => denied/concealed;
- revoked grant => denied;
- expired grant => denied;
- expired ShareAccess token => denied;
- token/grant mismatch => denied;
- UUID-only knowledge => denied;
- Beeexy-ID knowledge => denied.

## Artifact item isolation
- artifact A item authorizes A only;
- B remains denied;
- corrupt/missing item fails closed;
- cross-patient item fails closed.

## Downloaded event
If implemented:
- successful ShareAccess download creates safe event;
- safe resource category;
- no secret/token/IP/User-Agent/storage path;
- failed auth does not create a false success event.

## Storage
- exact bytes retrieved;
- cancellation/disposal safe;
- private path remains non-public;
- no traversal;
- no storage-key leakage.

## OpenAPI
- POST documents all three formats;
- download route exists;
- content types correct;
- auth requirements correct;
- no internal storage fields;
- no Phase 11.8 route.

---

# Direct Regression Set

Run focused regressions for:
- Phase 11.1 ExportArtifact persistence;
- Phase 11.3 ShareAccess auth;
- Phase 11.4 canonical snapshot;
- Phase 11.5 lifecycle/revalidation;
- Phase 11.6 Beeexy JSON/idempotency/storage;
- Phase 6 FHIR export/validation;
- Phase 3 patient authority;
- OpenAPI auth/content type generation.

Do not run unrelated modules unless directly affected.

---

# Build / EF / Migration

Build affected projects only.

Use repository-standard Debug/locked restore conventions.

If no EF change:
- confirm no pending model changes;
- do not create an empty migration.

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

Mark only Phase 11.7 complete.

Record factual details:
- PDF renderer abstraction and selected library;
- PDF content behavior;
- Phase 6 FHIR delegation;
- confirmation no new FHIR mapper exists;
- final export format behavior;
- artifact download endpoint;
- Bearer download authorization;
- ShareAccess explicit artifact authorization;
- lifecycle revalidation;
- Downloaded event behavior if implemented;
- media types;
- persistence/migration status.

Record only tests/checks actually executed.

Explicitly state which global suites were **not** executed.

Use wording equivalent to:

`The complete repository-wide unit and integration suites were intentionally not run for Phase 11.7 under the subphase testing policy; full regression is reserved for Phase 11.8 closure unless explicitly requested.`

Do not fabricate counts.

End with:

**Phase 11.8 has not started.**

---

# Explicitly Out of Scope

Do not implement Phase 11.8 final closure tasks:
- repository-wide full regression;
- final complete authorization matrix;
- final broad security audit;
- final broad concurrency closure;
- final full migration-chain proof beyond focused needs;
- final Phase 11 acceptance sign-off.

Also do not implement:
- frontend export/download UI;
- recipient accounts;
- provider portal;
- manager sharing authority;
- Case semantics;
- Visit sharing;
- new clinical logic;
- AI reinterpretation;
- new FHIR mappings;
- public/signed artifact URLs unless already explicitly approved elsewhere.

---

# Non-Negotiable Rules

1. Implement only Phase 11.7.
2. Extend existing export POST; do not create another export-creation endpoint.
3. Add exactly one new endpoint: `GET /api/v1/exports/{id}/content`.
4. Implement PDF from the canonical Phase 11.4 snapshot.
5. FHIR delegates exclusively to Phase 6.
6. Do not add a new FHIR mapper.
7. Download exact stored immutable bytes; do not regenerate.
8. Bearer authorization remains patient-scoped.
9. ShareAccess download requires explicit artifact authorization.
10. FullProfile does not auto-authorize historical artifacts.
11. Revalidate ShareGrant on every ShareAccess download.
12. Revoked/expired grants block download.
13. Preserve private storage; no public artifact paths/URLs.
14. Do not weaken/delete/skip tests.
15. Do not automatically run global unit/integration suites unless justified by transversal risk.
16. Do not mark complete unless required focused validation passes.
17. Do not start Phase 11.8.

---

# Expected Final Codex Response

When finished, report concisely:

1. What Phase 11.7 implemented.
2. Main files modified.
3. PDF renderer abstraction and selected concrete library.
4. PDF content behavior.
5. FHIR delegation behavior and confirmation no new mapper was added.
6. Exact export/download endpoint contracts.
7. Media types.
8. Bearer download authorization behavior.
9. ShareAccess explicit-artifact authorization behavior.
10. Revocation/expiry download revalidation behavior.
11. Downloaded activity behavior, if implemented.
12. Persistence/migration changes or confirmation none were needed.
13. Tests executed.
14. Results of each executed test group.
15. Builds/checks executed.
16. OpenAPI path count.
17. EF pending-model status.
18. Global unit/integration suites **not executed** under the subphase policy.
19. Remaining risks/validation intentionally deferred to Phase 11.8.
20. Confirmation the implementation plan was updated.
21. Confirmation that **Phase 11.8 was not started**.

If required focused validation fails, do not claim Phase 11.7 complete. Report the exact blocker and smallest relevant failing test group.
