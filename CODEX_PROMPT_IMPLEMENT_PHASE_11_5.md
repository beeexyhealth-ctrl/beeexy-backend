# Codex Prompt — Implement Beeexy Phase 11.5

Implement **Phase 11.5 — Share Revocation + Expiry + Patient-Facing Activity** in the Beeexy backend repository.

This task is strictly limited to **Phase 11.5 only**.

Do **not** implement Phase 11.6 or any later Phase 11 behavior, even if Phase 11.5 finishes successfully before the session ends.

Use the current authoritative implementation plan:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Before changing code, read:
- the complete Phase 11 section;
- completed Phase 11.1–11.4 subsections;
- the formal Phase 11.5 subsection;
- the existing Phase 3 patient authority patterns;
- the Phase 11.2 share creation/listing contracts;
- the Phase 11.3 ShareAccess token/exchange flow;
- the Phase 11.4 shared-profile revalidation/access-event behavior.

Preserve all previously completed behavior from Phases 1–10 and Phase 11.1–11.4.

Follow the existing Beeexy architecture, naming conventions, layering, Problem Details conventions, PostgreSQL/EF Core patterns, privacy/logging rules, background-job conventions, OpenAPI conventions, and testing style.

---

# Primary Objective

Implement exactly:

`POST /api/v1/shares/{id}/revoke`

`GET /api/v1/shares/{id}/activity`

and the non-HTTP expiry/reconciliation behavior required by the authoritative Phase 11.5 plan.

Phase 11.5 must ensure:
- the Primary Patient can revoke a share;
- revocation is idempotent;
- expired shares are recognized/reconciled safely;
- previously issued ShareAccess tokens stop working once the grant is revoked/expired;
- patient-facing activity is available with privacy-safe metadata;
- concurrent revoke/access/exchange/expiry behavior is correct;
- no export behavior is introduced yet.

---

# Strict Scope Boundary

Phase 11.5 includes:
- share revocation endpoint;
- share activity endpoint;
- grant lifecycle transition/reconciliation;
- expiry processing/boundary;
- concurrency control around revoke/access/exchange/expiry;
- patient-facing safe activity projection;
- directly related audit/event persistence behavior;
- OpenAPI;
- focused tests and directly related regressions.

Phase 11.5 does **not** include:
- export creation;
- Beeexy JSON;
- PDF;
- FHIR export;
- artifact storage;
- artifact download;
- frontend share UI;
- provider portal;
- recipient identity/accounts;
- Case semantics;
- Visit sharing;
- Phase 13 behavior.

---

# POST /api/v1/shares/{id}/revoke

Authentication: normal account Bearer required.

Authorization:
- Primary Patient always has authority to revoke their own shares.
- Creator may revoke only while retaining valid sharing authority.
- Because Phase 11.2 creation is Primary-only, the normal MVP creator is the Primary Patient.
- Do not broaden manager/caregiver sharing authority.
- Do not accept Beeexy ID as authority.
- UUID knowledge alone grants no authority.
- Use concealed `404` for absent/foreign grants according to existing ownership conventions.

Revocation semantics:
- irreversible in MVP;
- idempotent;
- server-timestamped;
- atomic;
- concurrency-safe.

First successful revoke:
- transitions grant to revoked;
- persists revocation timestamp/metadata;
- appends exactly one safe `ShareRevoked` event if required by the current design;
- does not delete grant/history/patient data.

Repeated authorized revoke:
- returns idempotent success;
- preserves original revocation timestamp;
- creates no duplicate revoke event.

Do not implement reactivation.

After commit:
- Phase 11.3 exchange must reject the capability;
- Phase 11.4 profile must reject previously issued ShareAccess tokens;
- future share-based download must remain compatible with this lifecycle rule.

Prefer current-grant revalidation over a JWT blacklist.

---

# GET /api/v1/shares/{id}/activity

Authentication: normal account Bearer required.

Authorization:
- current Primary Patient / authorized owner of the ShareGrant;
- no managed-patient authority merely from Phase 3 management;
- no foreign-grant access;
- no Beeexy-ID authority;
- UUID knowledge grants nothing;
- absent/foreign => concealed `404`.

Return only approved patient-facing metadata.

Supported concepts may include:
- Created
- Accessed
- Downloaded
- Revoked
- Expired

Expose only events that actually exist. Do not fabricate `Downloaded` before later export/download implementation.

Safe fields may include:
- event type;
- timestamp;
- safe outcome/category;
- high-level action/resource category if already approved.

Never expose:
- capability;
- capability hash;
- ShareAccess token;
- bearer token;
- full IP;
- full User-Agent;
- raw headers;
- Account IDs not approved for display;
- storage IDs;
- provider internals;
- clinical payloads;
- raw audit JSON.

Use deterministic ordering. Follow the exact plan if defined; otherwise use a stable repository-consistent order such as `occurredAt ASC, id ASC`.

Do not invent pagination unless the authoritative Phase 11.5 contract requires it.

---

# Expiry Boundary

Approved share lifetime already exists:
- default 24 hours;
- max 7 days;
- finite expiry only.

Use exact server-clock semantics:
- `now < ExpiresAt` => still valid;
- `now >= ExpiresAt` => expired/ineligible.

Use existing clock abstraction and UTC instants.

If expiry is derived and no persisted transition is needed, do not add unnecessary mutation.

If the current plan/model requires `ShareExpired` event reconciliation:
- implement an idempotent non-HTTP application service/background worker;
- process bounded batches;
- deterministic ordering;
- append `ShareExpired` exactly once;
- preserve `ExpiresAt`;
- preserve ShareGrant;
- preserve activity history;
- never delete patient/source data.

Reuse existing background-service patterns. Avoid broad infrastructure invention.

If a worker is implemented:
- cancellation-aware;
- retry-safe;
- no duplicate expiry events across workers/runs;
- privacy-safe logs;
- bounded batches;
- no unrelated-module processing.

---

# Concurrency Requirements

Explicitly handle:
- revoke vs exchange;
- revoke vs profile access;
- revoke vs repeated revoke;
- expiry vs exchange;
- expiry vs profile access;
- expiry vs revoke;
- concurrent expiry workers if applicable.

Use PostgreSQL/database concurrency controls where required.

Do not rely only on process-local locks.

If access/exchange completed before revoke commit, preserve truthful ordering. Do not retroactively fabricate denial.

Once revoke/expiry wins, new access must fail.

Preserve Phase 11.4 deterministic/idempotent `ShareAccessed` behavior.

---

# Application Use Cases

Implement cohesive use cases equivalent to:
- `RevokeShare`
- `ListShareActivity`
- `ExpireShares` / expiry reconciliation service

Add only supporting repositories/queries/locking needed.

Do not implement:
- `GenerateExport`
- `DownloadExport`

---

# Persistence / EF Core

Prefer reusing Phase 11.1–11.4 schema.

Only change EF/PostgreSQL if a genuinely necessary lifecycle field, event uniqueness mechanism, expiry index, or concurrency constraint is missing.

If no model change:
- no empty migration;
- verify no pending model changes.

If migration required:
- minimal/additive;
- justify it;
- run focused migration tests.

Do not add export-related fields.

---

# Error / Problem Details

Follow current Beeexy conventions.

Revoke:
- unauthenticated => `401`
- malformed UUID => existing safe routing behavior
- absent/foreign => concealed `404`
- first revoke => `204`
- repeat revoke => `204`
- invalid/corrupt state => safe `409`/`422` only if authoritative plan defines it
- unexpected => safe `500`

Activity:
- unauthenticated => `401`
- absent/foreign => concealed `404`
- success => `200`
- empty => `200` with empty list if possible
- bad pagination => `422` only if pagination exists
- unexpected => safe `500`

Do not leak owner identity or hidden-resource existence.

---

# OpenAPI

Add exactly:
- `POST /api/v1/shares/{id}/revoke`
- `GET /api/v1/shares/{id}/activity`

Document Bearer security and safe schemas/statuses.

Do not add export routes.

OpenAPI path count should change only as expected from the Phase 11.4 baseline.

---

# Security / Privacy

Non-negotiable:
- no capability in logs;
- no capability hash in normal logs;
- no ShareAccess token in logs;
- no bearer token in logs;
- no full IP/User-Agent in patient-facing activity;
- no raw clinical payload in activity;
- no cross-patient activity leakage;
- no manager sharing-authority expansion;
- no Beeexy-ID authority;
- no UUID-only authority;
- revocation/expiry reflected immediately by recipient access checks;
- no deletion of source health data.

---

# Mandatory Testing Policy — Optimize Execution Time

Codex must create/update **all tests necessary** to cover Phase 11.5 correctly, while minimizing unnecessary execution time.

## During implementation
Run only focused tests related to:
- ShareGrant lifecycle;
- revoke endpoint;
- activity endpoint;
- expiry service/worker;
- ShareAccess revalidation;
- Phase 11.3 exchange;
- Phase 11.4 profile access;
- concurrency/locking;
- OpenAPI;
- persistence changes if any.

Prefer narrow filters/projects.

Do not repeatedly run full repository suites.

## Mandatory closing validation
Before marking complete, run at minimum:
1. all new Phase 11.5 tests;
2. affected sharing-module tests;
3. directly related Phase 11.1–11.4 regressions;
4. revoke vs exchange focused integration tests;
5. revoke vs profile-access focused integration tests;
6. expiry vs exchange/profile focused tests;
7. activity projection/privacy tests;
8. OpenAPI/contract tests;
9. persistence/migration tests only if EF/PostgreSQL changes;
10. builds for affected projects;
11. EF pending-model check;
12. migration checks when applicable;
13. formatting / whitespace / `git diff --check`.

## Global suite policy

**Do not automatically run the full repository-wide unit or integration suites as a Phase 11.5 completion criterion.**

Full regression is reserved for:
- final Phase 11 closure;
- explicit user request;
- or a genuinely high-risk transversal change requiring broader validation.

If Codex believes a full suite is essential before final closure, it may run it only after briefly justifying why focused coverage is insufficient.

Do not run global suites merely out of habit.

## Historical expectation failure policy

If many tests fail due to one shared obsolete expectation:
1. identify the common root cause;
2. determine whether Phase 11.5 legitimately changes the contract;
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

Historical tests may change only where Phase 11.5 legitimately changes the expected contract.

---

# Focused Test Requirements

Create focused coverage for at least:

## Revoke
- Primary Patient success;
- absent grant concealed;
- foreign grant concealed;
- Beeexy-ID non-authority;
- UUID-only non-authority;
- first revoke transition;
- repeat revoke idempotent;
- stable revocation timestamp;
- exactly one revoke event;
- no clinical/source deletion.

## Revocation invalidation
- exchange works before revoke;
- exchange fails after revoke;
- profile works before revoke;
- previously issued token fails after revoke.

## Expiry
- before exact boundary active;
- at exact boundary expired;
- after boundary expired;
- expired capability exchange fails;
- issued token fails after grant expiry even if JWT itself has not expired;
- expiry event exactly once if implemented;
- repeated expiry processing safe;
- revoke vs expiry produces truthful final lifecycle/event history.

## Activity
- Created visible if current model records it;
- Accessed visible;
- Revoked visible;
- Expired visible if implemented;
- Downloaded not fabricated yet;
- deterministic ordering;
- no secret/token/hash;
- no full IP/User-Agent;
- no clinical payload;
- cross-account isolation.

## Concurrency
Use real PostgreSQL where needed:
- revoke/revoke;
- revoke/exchange;
- revoke/profile;
- expire/exchange;
- expire/profile;
- expire/revoke;
- two expiry workers if applicable.

## OpenAPI
- revoke route exists;
- activity route exists;
- Bearer security;
- expected statuses;
- no internal/secret fields;
- no Phase 11.6+ routes.

---

# Direct Regression Set

Run focused regressions for:
- Phase 11.1 lifecycle/persistence;
- Phase 11.2 creation/listing/idempotency;
- Phase 11.3 exchange/reusable capability/rate limit;
- Phase 11.4 grant revalidation/profile/access events;
- Phase 3 Primary Patient authority;
- existing background-worker conventions if reused;
- OpenAPI auth/route generation.

Do not run unrelated modules unless directly affected.

---

# Build / EF / Migration

Build affected projects only, using repository-standard Debug/locked restore conventions.

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

Mark only Phase 11.5 complete.

Record factual details:
- revoke endpoint;
- activity endpoint;
- Primary-only revoke authority;
- idempotent revoke behavior;
- expiry/reconciliation implementation;
- concurrency/locking strategy;
- immediate recipient-access invalidation;
- activity DTO/privacy;
- migration status.

Record only tests/checks actually executed.

Explicitly state global suites were **not** executed under the subphase policy.

Use wording equivalent to:

`The complete repository-wide unit and integration suites were intentionally not run for Phase 11.5 under the subphase testing policy; full regression is reserved for Phase 11 closure unless explicitly requested.`

Do not fabricate counts.

End with:

**Phase 11.6 has not started.**

---

# Explicitly Out of Scope

Do not implement Phase 11.6:
- `POST /api/v1/patients/{id}/exports`;
- Beeexy JSON;
- artifact storage writes;
- export retention worker.

Do not implement Phase 11.7:
- PDF;
- FHIR export;
- `GET /api/v1/exports/{id}/content`;
- share-token artifact download.

Do not implement Phase 11.8 closure.

Also exclude:
- frontend activity/revoke UI;
- QR rendering;
- recipient accounts;
- provider portal;
- manager sharing authority;
- Case semantics;
- Visit sharing;
- Phase 13;
- new clinical logic;
- AI reinterpretation;
- new FHIR mappings.

---

# Non-Negotiable Rules

1. Implement only Phase 11.5.
2. Add exactly two endpoints: revoke + activity.
3. Revocation is Primary-Patient authorized and idempotent.
4. Revocation must immediately block future recipient access after commit.
5. Expiry uses exact server-clock boundary.
6. Expiry processing is idempotent if implemented.
7. Do not delete ShareGrant or activity history.
8. Do not delete patient/source clinical data.
9. Patient-facing activity is privacy-minimized.
10. Do not broaden manager/caregiver sharing authority.
11. Do not implement exports yet.
12. Do not add new FHIR mappings.
13. Follow existing repository architecture/conventions.
14. Do not weaken/delete/skip tests.
15. Do not run global unit/integration suites automatically unless justified by transversal risk.
16. Do not mark complete unless required focused validation passes.

---

# Expected Final Codex Response

When finished, report concisely:

1. What Phase 11.5 implemented.
2. Main files modified.
3. Exact endpoint contracts added.
4. Revocation authority and idempotency behavior.
5. Expiry/reconciliation behavior.
6. Concurrency behavior.
7. Patient-facing activity contract.
8. Any persistence/migration changes, or confirmation none were needed.
9. Tests executed.
10. Results of each executed test group.
11. Builds/checks executed.
12. OpenAPI path count.
13. EF pending-model status.
14. Global unit/integration suites **not executed** under the subphase policy.
15. Any remaining risk/validation deferred to final Phase 11 regression.
16. Confirmation the implementation plan was updated.
17. Confirmation that **Phase 11.6 was not started**.

If required focused validation fails, do not claim Phase 11.5 complete. Report the exact blocker and the smallest relevant failing test group.
