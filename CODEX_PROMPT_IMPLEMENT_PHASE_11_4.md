# Codex Prompt — Implement Beeexy Phase 11.4

Implement **Phase 11.4 — Shared Read-Only Profile + Scope Evaluator** in the Beeexy backend repository.

This task is strictly limited to **Phase 11.4 only**.

Do **not** implement Phase 11.5 or any later Phase 11 behavior, even if Phase 11.4 finishes successfully before the session ends.

Use the current authoritative implementation plan:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Before changing code, read the complete Phase 11 section, completed Phase 11.1–11.3 subsections, the formal Phase 11.4 subsection, and the existing Phase 3/4/5/9/10 read and authorization boundaries relevant to shared health data.

Preserve all previously completed behavior from Phases 1–10 and Phase 11.1–11.3.

Follow the existing Beeexy architecture, naming conventions, layering, Problem Details conventions, PostgreSQL/EF Core patterns, privacy/logging rules, configuration style, OpenAPI conventions, and testing style.

---

# Primary Objective

Implement exactly:

`GET /api/v1/shared-access/profile`

The endpoint must:

- authenticate with the dedicated Phase 11.3 `ShareAccess` credential;
- resolve the associated `ShareGrant`;
- re-check current ShareGrant state on every request;
- fail if the grant is revoked, expired, missing, corrupt, reserved, or otherwise invalid;
- enforce the exact stored grant scope/items;
- build only the allow-listed shared health projection;
- return no data outside the stored share scope/items;
- remain strictly read-only;
- record privacy-safe access activity only if the authoritative Phase 11.4 plan requires it;
- introduce no Phase 11.5+ behavior.

This is the first subphase exposing shared patient health data to an unauthenticated recipient, so scope isolation, IDOR resistance, privacy, and projection allow-listing are critical.

---

# Strict Scope Boundary

Phase 11.4 includes:

- `GET /api/v1/shared-access/profile`;
- ShareAccess authentication;
- current grant revalidation;
- scope evaluation;
- canonical shared health snapshot/projection boundary;
- executable `FullProfile`;
- executable `PreTriage`;
- executable `SpecificRecords`;
- reserved/fail-closed `Case`;
- reserved/fail-closed `Visit`;
- read-only enforcement;
- access-event append behavior if required;
- OpenAPI;
- focused tests and directly related regressions.

Phase 11.4 does **not** include:

- revoke endpoint;
- activity listing endpoint;
- share-expiry worker;
- export creation;
- Beeexy JSON artifact generation;
- PDF;
- FHIR export;
- artifact storage;
- artifact download;
- frontend shared-profile UI;
- QR rendering.

---

# Authentication and Grant Revalidation

Use the dedicated `ShareAccess` scheme introduced in Phase 11.3.

Do not accept a normal account Bearer token as a substitute.

For every request:

1. validate the ShareAccess token;
2. extract only the stable grant identity/claims required by the current design;
3. resolve the ShareGrant from persistence;
4. verify token/grant consistency;
5. verify grant still exists;
6. verify grant is not revoked;
7. verify grant is not expired;
8. verify token expiry has not elapsed;
9. verify stored scope is executable and consistent;
10. only then build the shared projection.

A previously issued token must stop working immediately after grant revocation or expiry.

Do not rely only on JWT expiry.

The ShareGrant remains the server-side source of truth.

---

# Approved Scope Vocabulary

Retain:

- `FullProfile`
- `Case`
- `PreTriage`
- `Visit`
- `SpecificRecords`

Executable now:

- `FullProfile`
- `PreTriage`
- `SpecificRecords`

Reserved / fail closed:

- `Case`
- `Visit`

Never silently fall back from a reserved/invalid scope to another scope.

Do not invent Case semantics.

Do not substitute appointments for Visit.

---

# Scope Evaluator

Implement a reusable boundary equivalent to:

`IShareScopeEvaluator`

or the repository-appropriate equivalent.

Responsibilities:

- enforce the stored ShareGrant scope;
- enforce exact ShareGrantItem membership;
- filter every projected source;
- prevent cross-scope access;
- prevent cross-patient access;
- fail closed for missing/corrupt/unsupported items;
- fail closed for reserved scopes.

Do not trust request-supplied scope selectors.

The GET endpoint must not accept a scope override or record-selection override.

---

# Canonical Shared Health Projection

Introduce or complete one provider-neutral, allow-listed canonical shared-health projection boundary suitable for later reuse by:

- Phase 11.4 FullProfile;
- future Phase 11.6 Beeexy JSON;
- future Phase 11.7 PDF.

Do not generate export bytes in this phase.

The projection must be:

- deterministic;
- read-only;
- allow-listed;
- built from existing approved read boundaries;
- free of provider/internal/audit secrets;
- free of raw database-row leakage.

Do not independently create multiple inconsistent definitions of FullProfile.

FHIR is not part of this projection path.

---

# FullProfile

For MVP, `FullProfile` means the patient's shareable health profile, not every database row or product feature.

Include only, when present and currently valid:

- approved basic PatientProfile demographics;
- Clinical History;
- completed patient-owned/claimed Pre-Triage;
- Phase 9 Symptom Diary entries and separately presented approved informational/warning-sign content;
- patient-visible/succeeded Second Opinion results;
- minimum immutable version/provenance metadata needed to interpret shared records correctly.

Explicitly exclude:

- full AI Conversations;
- AI user/assistant messages;
- prompts/system prompts;
- provider/model configuration;
- raw provider output;
- rejected AI output;
- safety/audit internals;
- authentication/session data;
- Account IDs;
- refresh sessions;
- capabilities/tokens/hashes;
- storage identities;
- scheduling/appointment history;
- doctor/clinic directory data;
- notifications/preferences;
- source/import paths;
- logs/telemetry.

Build explicit DTOs/projections.

Do not serialize EF/domain entities directly.

---

# Demographics

Use only already-approved PatientProfile demographic fields.

Do not invent new fields.

Preserve truthful nullability/incomplete-profile behavior.

Do not expose Account IDs, authorization reason, creator/revoker identities, or unrelated user preferences.

---

# Clinical History

Reuse existing Phase 5 read/query boundaries.

Include only patient-visible records appropriate to the canonical FullProfile projection.

Do not bypass established Clinical History semantics with broad raw-table reads.

Do not mutate Clinical History.

Do not expose internal amendment/audit metadata unless already part of the approved patient-visible contract.

---

# Pre-Triage

## FullProfile

Include only completed patient-owned/claimed Pre-Triage records.

Do not expose:

- temporary sessions;
- abandoned sessions;
- anonymous unclaimed data;
- capabilities;
- AI extraction metadata;
- provider output;
- dormant future-clinical rule artifacts;
- fields forbidden by the neutral demo contract.

## PreTriage scope

Return only the exact Pre-Triage record(s) authorized by the grant/item model.

Do not expose unrelated Clinical History, Symptom Diary, Second Opinion, or other FullProfile content.

Missing/cross-patient/inconsistent item relationships must fail closed.

---

# Symptom Diary

For FullProfile, reuse Phase 9 approved read/content boundaries.

Preserve exact:

- immutable submitted values;
- package/version provenance;
- separately presented approved informational/warning-sign content.

Do not:

- interpret entries;
- compare entries;
- derive worsening/improvement;
- match answers to warnings;
- recommend;
- escalate;
- schedule reminders;
- invoke AI.

Sharing must not change Phase 9 safety semantics.

---

# Second Opinion

For FullProfile, include only existing patient-visible succeeded/displayable Second Opinion results supported by the current Phase 10 model.

Do not include:

- full AI Conversations;
- prompts;
- raw provider output;
- rejected output;
- safety audit internals;
- provider/model metadata.

Do not regenerate or reinterpret results.

Share the existing immutable patient-visible result only.

---

# Full AI Conversations — Strict Exclusion

Do not expose free-form AI Conversation history.

Add focused negative tests proving absence of:

- conversation threads;
- user messages;
- assistant messages;
- prompt context;
- system prompts;
- provider responses.

---

# SpecificRecords

`SpecificRecords` must expose only exact resources represented by stored `ShareGrantItem` rows.

Requirements:

- every item belongs to the ShareGrant;
- every referenced source belongs to the grant patient;
- every resource category is explicitly supported by the current approved model;
- missing/corrupt/cross-patient references fail closed;
- sibling/unlisted records do not leak;
- no "same patient => include all" shortcut;
- no caller-supplied item list at read time.

Use the exact resource-category vocabulary already defined by the current plan/implementation.

Do not invent new categories.

If a stored category is structural but not executable yet, fail closed.

---

# Reserved Scopes

## Case

Fail closed.

Do not implement Case projection.

Do not reinterpret as FullProfile/PreTriage/SpecificRecords.

## Visit

Fail closed until Phase 13.

Do not implement Visit projection.

Do not substitute scheduling/appointment data.

---

# Read-Only Enforcement

The recipient path is strictly read-only.

ShareAccess must not authorize:

- patient PATCH;
- care relationship mutations;
- Pre-Triage answer/complete/claim;
- Symptom Diary POST;
- AI conversation/Second Opinion execution;
- appointment request/cancel/reschedule;
- share create/revoke;
- export creation;
- notification mutation.

Add focused scheme-isolation tests on representative write endpoints.

Avoid redundant exhaustive duplication if central authorization tests already prove the boundary.

---

# ShareAccessed Event

Follow the authoritative Phase 11.4 plan.

If successful profile access is the correct moment to append `ShareAccessed`, record only minimal privacy-safe metadata.

Do not store:

- capability;
- capability hash;
- share-access token;
- clinical payload;
- full IP;
- full User-Agent;
- raw request headers.

Do not implement the patient-facing activity listing endpoint yet.

That belongs to Phase 11.5.

---

# Error / Problem Details

Follow the current plan and backend conventions.

Cover safely:

- missing/invalid/expired ShareAccess token;
- missing grant;
- revoked grant;
- expired grant;
- token/grant mismatch;
- reserved scope;
- invalid/corrupt scope/item state;
- internal unexpected failure.

Do not reveal hidden patient/resource existence.

Do not use verbose error messages that leak scope contents or record IDs.

Use `401`, `403`, concealed `404`, `422`, and safe `500` only according to the existing approved contract.

---

# Response Design

Return an explicit allow-listed shared profile DTO.

Include only:

- minimal grant/share context if required;
- stored scope;
- shared patient-facing data allowed by scope;
- safe provenance/version information.

Do not return:

- capability;
- capability hash;
- access token;
- creator Account ID;
- storage metadata;
- audit internals;
- internal source paths;
- unrestricted raw JSON;
- internal provider data.

Keep the contract suitable for later reuse by Beeexy JSON/PDF snapshot generation.

---

# Persistence / EF Core

Prefer no schema change.

Reuse the existing Phase 11 sharing tables.

Do not create export/storage schema changes in this phase.

If no EF change:

- do not create an empty migration;
- verify no pending model changes.

If a genuinely necessary read/audit field or index is missing:

- make the smallest additive change;
- justify it;
- run focused migration/persistence validation.

---

# OpenAPI

Add exactly:

`GET /api/v1/shared-access/profile`

Document the dedicated ShareAccess security requirement and safe response/error schemas.

Do not add:

- revoke endpoint;
- activity endpoint;
- export endpoints.

Verify no Phase 11.5+ route appears.

OpenAPI path count should change only as expected from the Phase 11.3 baseline.

---

# Mandatory Testing Policy — Optimize Execution Time

This policy is mandatory.

Codex must create or update **all tests necessary** to correctly cover Phase 11.4 while minimizing unnecessary execution time.

## During implementation

Run only focused tests related to:

- modified sharing projection code;
- ShareAccess authentication;
- grant revalidation;
- scope evaluator;
- FullProfile;
- PreTriage;
- SpecificRecords;
- directly reused/modified Phase 4/5/9/10 read boundaries;
- access-event behavior;
- OpenAPI;
- privacy/authorization behavior.

Prefer narrow test filters/projects.

Do not repeatedly execute entire repository-wide suites.

## Mandatory closing validation

Before marking Phase 11.4 complete, run at minimum:

1. all new Phase 11.4 tests;
2. affected sharing-module tests;
3. directly related Phase 11.1–11.3 regressions;
4. directly affected ShareAccess auth/token tests;
5. directly affected Phase 4 Pre-Triage read/projection tests;
6. directly affected Phase 5 Clinical History read tests;
7. directly affected Phase 9 Symptom Diary read/content tests;
8. directly affected Phase 10 Second Opinion read/safety tests if used;
9. OpenAPI/contract tests;
10. persistence/migration tests only if EF Core/PostgreSQL changes;
11. builds for affected projects;
12. EF pending-model check;
13. migration checks when applicable;
14. formatting / whitespace / `git diff --check`.

## Global suite policy

**Do not automatically run the full repository unit suite or full repository integration suite as a Phase 11.4 completion criterion.**

Full regression is reserved for:

- final Phase 11 closure;
- explicit user request;
- or a genuinely high-risk transversal change requiring global coverage.

If Codex believes a global suite is essential before Phase 11 closure, it may run it only after briefly justifying why focused coverage is insufficient.

Do not run global suites merely out of habit.

## Historical expectation failures

If many failures share one obsolete historical expectation:

1. identify the common cause;
2. determine whether Phase 11.4 legitimately changes the contract;
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

Historical tests may change only when Phase 11.4 legitimately changes the expected contract.

---

# Focused Test Requirements

Create focused coverage for at least:

## ShareAccess and grant revalidation

- valid token + active grant => success;
- missing token;
- invalid signature;
- wrong issuer/audience;
- expired token;
- missing grant;
- revoked grant after token issuance;
- expired grant after token issuance;
- token/grant mismatch;
- normal Account Bearer does not substitute.

## FullProfile

Verify inclusion where fixtures exist:

- approved demographics;
- Clinical History;
- completed Pre-Triage;
- Symptom Diary;
- approved patient-visible Second Opinion results.

Verify exclusion:

- AI Conversations;
- prompts/messages;
- provider/model data;
- rejected/raw AI output;
- auth/session data;
- Account IDs;
- scheduling/appointments;
- directory data;
- notifications/preferences;
- internal logs/audit;
- temporary Pre-Triage;
- anonymous unclaimed records;
- source/import paths.

## PreTriage scope

- only exact authorized Pre-Triage content;
- no Clinical History leakage;
- no Symptom Diary leakage;
- no Second Opinion leakage;
- no unrelated Pre-Triage record leakage;
- missing/cross-patient item fails closed.

## SpecificRecords

- exact item A returns only A;
- sibling item B is excluded;
- cross-patient reference fails closed;
- missing reference fails closed;
- corrupt/unsupported category fails closed;
- request cannot override stored items.

## Reserved scopes

- Case fails closed;
- Visit fails closed;
- no FullProfile fallback;
- no appointment substitution.

## Read-only enforcement

Representative tests proving ShareAccess cannot:

- PATCH patient;
- write Symptom Diary;
- execute AI;
- mutate appointment;
- create/revoke share.

## Access event, if implemented

- successful profile access appends the correct safe event;
- no secret/clinical payload stored;
- event is immutable;
- retry/idempotency behavior follows contract;
- failed access does not create a misleading successful event.

## Privacy/logging

- no access token in logs;
- no capability/hash in logs;
- no raw shared profile payload in technical logs;
- safe Problem Details;
- no hidden-record details leaked.

## OpenAPI

- profile route exists;
- GET only;
- ShareAccess security scheme;
- correct response schema;
- no internal/secret fields;
- no Phase 11.5+ endpoints.

---

# Direct Regression Set

Run focused regressions for dependencies actually touched, including as applicable:

- Phase 11.1 lifecycle/persistence;
- Phase 11.2 scopes/items;
- Phase 11.3 ShareAccess token validation;
- Phase 3 authorization assumptions;
- Phase 4 completed Pre-Triage reads;
- Phase 5 Clinical History reads;
- Phase 9 Symptom Diary reads/content;
- Phase 10 Second Opinion patient-visible results;
- OpenAPI auth-scheme generation.

Do not run unrelated modules unless modified dependencies make them directly relevant.

---

# Build Requirements

Build affected projects only, including directly/transitively changed Domain/Application/Infrastructure/API/test projects.

Use repository-standard Debug/locked restore conventions.

Build failures must be resolved before completion.

Do not require a global test run.

---

# EF / Migration Validation

If no EF change:

- confirm no pending model changes;
- do not create an empty migration.

If EF/PostgreSQL changes:

- create the smallest additive migration;
- run focused migration tests;
- apply/rollback/reapply where repository conventions require;
- confirm no pending model changes.

---

# Implementation Plan Update

If and only if Phase 11.4 implementation and required focused validation pass:

Update:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Mark only Phase 11.4 complete.

Record factual implementation details:

- profile endpoint;
- ShareAccess authentication;
- current grant revalidation;
- scope evaluator;
- canonical shared snapshot/projection;
- FullProfile allow-list;
- PreTriage isolation;
- SpecificRecords exact-item isolation;
- reserved-scope behavior;
- AI Conversation exclusion;
- read-only enforcement;
- access-event behavior if implemented;
- migration status.

Record only tests/checks actually executed.

Explicitly state which global suites were **not** executed.

Use wording equivalent to:

`The complete repository-wide unit and integration suites were intentionally not run for Phase 11.4 under the subphase testing policy; full regression is reserved for Phase 11 closure unless explicitly requested.`

Do not fabricate counts.

End with:

**Phase 11.5 has not started.**

---

# Explicitly Out of Scope

Do not implement Phase 11.5:

- `POST /api/v1/shares/{id}/revoke`;
- `GET /api/v1/shares/{id}/activity`;
- expiry background worker;
- patient-facing activity listing.

Do not implement Phase 11.6+:

- export creation;
- Beeexy JSON artifacts;
- PDF;
- FHIR export;
- private artifact storage adapter;
- artifact download.

Also do not implement:

- frontend shared-profile UI;
- QR rendering;
- recipient accounts;
- provider portal;
- manager sharing authority;
- Case semantics;
- Visit sharing;
- Phase 13;
- new clinical evaluation;
- AI reinterpretation;
- new FHIR mappings.

---

# Non-Negotiable Rules

1. Implement only Phase 11.4.
2. Add exactly one new endpoint: `GET /api/v1/shared-access/profile`.
3. Require the dedicated ShareAccess credential.
4. Revalidate ShareGrant state on every request.
5. Enforce exact stored scope/items.
6. FullProfile must be allow-listed.
7. Full AI Conversations must not be shared.
8. PreTriage scope must not leak other modules.
9. SpecificRecords must expose exact items only.
10. Case and Visit fail closed.
11. Recipient access is read-only.
12. Do not implement revoke/activity endpoint yet.
13. Do not implement export behavior yet.
14. Do not add new FHIR mappings.
15. Follow existing repository architecture/conventions.
16. Do not weaken/delete/skip tests.
17. Do not run global unit/integration suites automatically unless justified by transversal risk.
18. Do not mark Phase 11.4 complete unless required focused closing validation passes.

---

# Expected Final Codex Response

When finished, report concisely:

1. What Phase 11.4 implemented.
2. Main files modified.
3. Exact endpoint contract added.
4. ShareAccess authentication and grant revalidation behavior.
5. Scope evaluator behavior.
6. FullProfile inclusion/exclusion behavior.
7. PreTriage and SpecificRecords isolation behavior.
8. Reserved-scope behavior.
9. Access-event behavior, if implemented.
10. Any persistence/migration changes, or confirmation none were needed.
11. Tests executed.
12. Results of each executed test group.
13. Builds/checks executed.
14. OpenAPI path count.
15. EF pending-model status.
16. Global unit/integration suites **not executed** under the subphase testing policy.
17. Any remaining risk/validation intentionally deferred to final Phase 11 regression.
18. Confirmation the implementation plan was updated.
19. Confirmation that **Phase 11.5 was not started**.

If required focused validation fails, do not claim Phase 11.4 complete. Report the exact blocker and the smallest relevant failing test group.
