# Codex Prompt — Implement Beeexy Phase 11.3

Implement **Phase 11.3 — Anonymous Capability Exchange + Short-Lived Read-Only Access Token** in the Beeexy backend repository.

This task is strictly limited to **Phase 11.3 only**.

Do **not** implement Phase 11.4 or any later Phase 11 behavior, even if Phase 11.3 finishes successfully before the session ends.

Use the current authoritative implementation plan:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Before changing code, read the complete Phase 11 section, the completed Phase 11.1 and 11.2 subsections, the formal Phase 11.3 subsection, and the relevant existing authentication/token/security infrastructure from earlier phases.

Preserve all previously completed behavior from Phases 1–10 and Phase 11.1–11.2.

Follow the existing Beeexy architecture, naming conventions, layering, Problem Details conventions, PostgreSQL/EF Core patterns, testing style, privacy/logging rules, configuration style, and OpenAPI conventions.

---

# Primary Objective

Implement exactly:

`POST /api/v1/shared-access/exchange`

The endpoint must allow a recipient without a Beeexy account to exchange a valid external share capability for a short-lived, read-only share-access token.

Phase 11.3 must include everything required for that endpoint to work correctly and securely:

- capability input validation,
- secure hash resolution,
- current ShareGrant lifecycle validation,
- exact expiry enforcement,
- rate limiting,
- reusable capability behavior,
- short-lived read-only access token issuance,
- safe error handling,
- privacy-safe logging,
- OpenAPI contract,
- focused tests,
- directly related regression tests.

Do not implement shared profile retrieval yet.

---

# Strict Scope Boundary

Phase 11.3 includes:

- anonymous capability exchange,
- current ShareGrant validation,
- read-only share-access token issuance,
- exchange rate limiting,
- token claims/scope metadata needed by later Phase 11.4,
- minimal authentication scheme/policy infrastructure required to represent the share-access token safely,
- configuration and validation required for the token,
- focused endpoint/security tests.

Phase 11.3 does **not** include:

- shared profile projection,
- FullProfile composition,
- PreTriage projection,
- SpecificRecords projection,
- share revoke endpoint,
- share activity endpoint,
- share expiry worker,
- exports,
- PDF,
- Beeexy JSON,
- FHIR export,
- artifact download,
- frontend/QR rendering.

---

# Authoritative Product Decisions

## Capability

The external share capability:

- is cryptographically random,
- was created in Phase 11.2,
- is persisted only as a secure SHA-256 digest,
- is reusable while the ShareGrant remains active,
- is not a one-time token,
- must never be persisted in plaintext,
- must never be logged,
- must never be accepted from query string as the required exchange credential.

Knowledge of ShareGrant UUID, Patient UUID, Beeexy ID, Account ID, or the frontend share URL does not grant recipient access without the capability.

## Share-access token lifetime

Maximum lifetime: **15 minutes**.

Effective expiration must be:

`min(now + 15 minutes, ShareGrant.ExpiresAt)`

Do not issue a token with a lifetime extending beyond the grant's expiry.

Use the existing server clock abstraction.

## Current grant-state enforcement

A share-access token must not be treated as permanently authoritative until its own expiry.

Later recipient operations must re-check the current ShareGrant state.

The token must therefore carry only enough stable technical identity to resolve the corresponding ShareGrant later.

Do not embed the shared clinical dataset into the token.

Do not make later revocation impossible by relying only on self-contained claims.

## Read-only semantics

The share-access token must not grant:

- account authentication,
- Primary Patient authority,
- manager authority,
- patient mutation,
- Clinical History mutation,
- Pre-Triage mutation,
- Symptom Diary creation,
- AI execution,
- scheduling mutation,
- share creation,
- share revocation,
- export creation.

Use a distinct authentication scheme/policy/credential type if consistent with the existing architecture.

---

# Endpoint Contract

## POST /api/v1/shared-access/exchange

Authentication: public/anonymous.

Authorization: possession of a valid share capability.

Recommended request shape:

- `capability`

Do not accept:

- ShareGrant ID as authority,
- patientId,
- BeeexyId,
- accountId,
- scope override,
- expiry override,
- access-token lifetime override,
- token claims,
- creator identity,
- recipient identity,
- internal hashes.

Capability must arrive in the POST body or equivalent non-URL transport.

Do not require it in query string or route path.

---

# Exchange Flow

Implement a cohesive use case equivalent to `ExchangeShareCapability`:

1. Validate request shape.
2. Apply endpoint rate limiting.
3. Validate capability representation only as needed.
4. Hash the presented capability using the exact Phase 11.2-compatible algorithm.
5. Resolve ShareGrant by stored digest.
6. Validate grant existence.
7. Validate not revoked.
8. Validate not expired.
9. Validate otherwise exchangeable lifecycle state.
10. Compute effective expiry = `min(now + 15 minutes, ShareGrant.ExpiresAt)`.
11. Issue a short-lived read-only share-access token.
12. Return only safe token metadata.

No shared health data is returned in Phase 11.3.

---

# Capability Hash Matching

Reuse the exact secure hash representation introduced by Phase 11.1/11.2.

Do not introduce a second incompatible hashing format.

Prefer direct indexed lookup by hash.

Do not scan all grants.

Do not persist the presented capability.

Do not create a new plaintext token column.

---

# ShareGrant Validation

Reject at minimum:

- nonexistent hash,
- malformed capability,
- revoked grant,
- expired grant,
- corrupt/inconsistent lifecycle state,
- unsupported/non-exchangeable lifecycle state.

Do not reveal whether a capability matched a specific patient/share.

Use generic recipient-safe unauthorized behavior.

Target status behavior:

- invalid/expired/revoked => `401`
- throttled => `429`

Follow safe Problem Details conventions.

---

# Token Design

Reuse existing Beeexy signing/token infrastructure where appropriate.

The share-access token must remain logically distinct from normal account access tokens.

Include only minimal claims required for later Phase 11.4 resolution, such as equivalents of:

- credential type = share access,
- ShareGrant ID,
- issued-at,
- expiry,
- unique token identifier if needed,
- immutable scope identifier if useful as defense-in-depth,
- issuer/audience compatible with existing auth architecture.

Do not include:

- patient demographics,
- Clinical History,
- Pre-Triage content,
- Symptom Diary content,
- Second Opinion content,
- capability,
- capability hash,
- Beeexy ID,
- unnecessary Account IDs,
- storage metadata.

The ShareGrant remains the server-side source of truth.

---

# Authentication Scheme / Policy Boundary

If later endpoints need a dedicated share-access authentication scheme, Phase 11.3 may add the minimal infrastructure now.

Requirements:

- distinguish normal account Bearer tokens from share-access tokens,
- share tokens must not satisfy normal account/patient authorization policies,
- account access tokens must not replace the missing capability on exchange,
- preserve existing Phase 2 authentication behavior,
- preserve existing OpenAPI Bearer behavior for account endpoints.

Avoid broad auth refactors.

Prefer the smallest additive design.

---

# Rate Limiting

The exchange endpoint must be rate limited.

Reuse existing ASP.NET Core/Beeexy rate-limiting infrastructure if available.

Do not build a parallel custom limiter unless necessary.

The limiter must protect against brute-force capability guessing.

Do not log submitted capability values during throttling.

Tests must cover successful requests below threshold, throttling, safe `429`, and no capability leakage.

---

# Response Contract

Return safe exchange metadata only.

Likely fields:

- accessToken,
- tokenType if required by repository convention,
- expiresAt or expiresIn,
- optionally scope if explicitly approved and useful.

Do not return:

- capability,
- capability hash,
- patient health data,
- creator Account ID,
- private metadata,
- activity log,
- share URL.

Follow existing Beeexy DTO conventions.

---

# Error Contract

Follow backend-wide Problem Details.

Expected behavior:

- `400`: malformed JSON
- `401`: missing/invalid/malformed capability, nonexistent capability, revoked grant, expired grant, otherwise non-exchangeable grant
- `429`: rate limited
- `500`: safe unexpected failure
- `422`: only if existing request validation conventions require it and it does not create a capability oracle

Do not use distinct error bodies that reveal whether the capability matched a real grant.

---

# Audit / ShareAccessEvent

If the authoritative Phase 11.3 plan requires an exchange event, record only privacy-safe metadata.

Do not store capability, capability hash, access token, patient clinical data, full IP, or full User-Agent.

If `ShareAccessed` is intended for actual shared-profile reads rather than exchange, defer it to Phase 11.4.

Do not invent duplicate event semantics.

---

# Persistence / EF Core

Prefer reusing the existing Phase 11.1/11.2 schema.

Phase 11.3 should not require a migration unless a genuinely necessary persistent field/index is missing.

If no EF model change is necessary:

- do not create an empty migration,
- verify EF reports no pending model changes.

If a migration is required:

- keep it minimal and additive,
- justify it in the final response,
- run focused persistence/migration validation.

Do not create token-session tables unless the established architecture genuinely requires them.

---

# Configuration

Add only configuration required for Phase 11.3, such as:

- share-access token issuer/audience,
- signing configuration reuse,
- 15-minute maximum lifetime policy,
- exchange rate-limit policy.

Reuse existing secret/configuration infrastructure.

Do not hardcode secrets.

Do not break unrelated environments/tests.

---

# Security / Privacy Requirements

Non-negotiable:

- no capability plaintext in DB,
- no capability plaintext in logs,
- no capability hash in normal logs,
- no share access token in logs,
- no request-body logging of capability,
- no secret in Problem Details,
- no secret in tracing/telemetry,
- no query-string capability requirement,
- no patient/clinical data in access token,
- no account authority granted by share token,
- no scope escalation,
- no Beeexy ID authority,
- no UUID-only authority,
- no recipient write authority.

Add focused tests where practical.

---

# OpenAPI

Add exactly one new operation:

`POST /api/v1/shared-access/exchange`

Document:

- public/no normal Bearer requirement,
- request schema,
- success response,
- `400`,
- `401`,
- `429`,
- safe `500`,
- `422` only if actually used.

Do not add:

- `/api/v1/shared-access/profile`
- revoke endpoint
- activity endpoint
- export endpoints

Verify path count changes only as expected from the Phase 11.2 baseline.

---

# Mandatory Testing Policy — Optimize Execution Time

This policy is mandatory.

Codex must create or update **all tests necessary** to cover Phase 11.3 correctly, but it must optimize execution time and avoid automatic full-suite runs during this subphase.

## During implementation

Run only focused tests related to:

- modified files/modules,
- sharing capability exchange,
- authentication/token infrastructure touched,
- rate limiting,
- Problem Details,
- OpenAPI contracts.

Prefer narrow test filters and affected test projects.

Do not run the entire global unit suite after every change.

Do not run the entire global integration suite after every change.

## Mandatory closing validation

Before marking Phase 11.3 complete, run at minimum:

1. all new Phase 11.3 tests;
2. affected `sharing` module tests;
3. directly related Phase 11.1–11.2 regressions;
4. directly affected authentication/token tests;
5. rate-limiting tests for the new endpoint;
6. OpenAPI/contract tests because API surface changes;
7. persistence/migration tests only if EF Core/PostgreSQL changes;
8. builds for affected projects;
9. EF pending-model check;
10. migration checks when applicable;
11. formatting / whitespace / `git diff --check` when appropriate.

## Global suite policy

**Do not automatically run the entire repository-wide unit suite or integration suite as a Phase 11.3 completion criterion.**

Full regression is reserved for:

- Phase 11 final closure,
- explicit user request,
- or an unusually high-risk transversal change that genuinely requires it.

If Codex believes a global suite is essential because Phase 11.3 changed a highly transversal security/authentication component, it may run it, but must briefly justify why focused affected-module/regression coverage is insufficient.

Do not run a global suite merely out of habit.

## Historical expectation failures

If many tests fail because of one obsolete historical expectation:

1. identify the common root cause;
2. determine whether Phase 11.3 legitimately changes that contract;
3. update that expectation only if justified;
4. rerun the smallest affected test group first;
5. do not immediately rerun the whole global suite.

## Test integrity

Do not:

- delete tests,
- disable tests,
- skip tests,
- reduce assertions,
- weaken coverage,
- modify historical tests merely to force green.

Historical tests may change only when Phase 11.3 legitimately changes the expected contract.

---

# Focused Test Requirements

Create focused coverage for at least:

## Exchange success

- valid active capability => success
- returned token valid under share-access scheme
- token expiry <= 15 minutes
- token expiry <= ShareGrant expiry
- near-expiry grant shortens token lifetime
- no health data returned

## Capability failures

- missing capability
- malformed capability
- random/wrong capability
- nonexistent hash
- revoked grant
- expired grant
- corrupt/non-exchangeable lifecycle state
- grant UUID without capability
- patient UUID without capability
- Beeexy ID without capability

## Capability reuse

- same valid capability can be exchanged multiple times while active
- exchange does not consume/rewrite grant
- each issued token obeys policy
- grant remains authoritative

## Token isolation

- share token cannot authenticate as normal Account bearer
- share token cannot satisfy Primary Patient authorization
- normal Account bearer does not replace missing share capability
- token contains only allow-listed claims
- no clinical data in token

## Rate limiting

- success below threshold
- repeated invalid exchange throttled
- safe `429`
- no capability leakage through rate-limit path

## Privacy

- capability absent from captured logs
- hash absent from normal logs
- returned token absent from logs
- Problem Details contain no secret/internal state
- request body not logged

## OpenAPI

- exchange route exists
- correct method
- no normal Bearer requirement
- documented response codes
- no hash/internal fields
- no Phase 11.4+ route introduced

---

# Direct Regression Set

Run focused regressions for the dependencies most likely to be affected:

- Phase 2 token creation/validation behavior
- invalid bearer handling
- Phase 3 authorization policies
- Phase 11.1 ShareGrant lifecycle/persistence
- Phase 11.2 share creation/capability hashing
- Phase 11.2 share listing secrecy
- existing rate-limiter infrastructure
- startup/config validation
- OpenAPI auth scheme generation

Do not run unrelated clinical/scheduling/AI suites unless a modified dependency makes them directly relevant.

---

# Build Requirements

Build affected projects only, at minimum those changed directly or transitively.

Use repository-standard Debug/locked restore conventions.

Do not require a whole-solution global test run.

Build failures must be resolved before completion.

---

# EF / Migration Validation

If no EF change:

- confirm no pending model changes,
- do not create an empty migration.

If EF/PostgreSQL changes:

- create smallest additive migration,
- run focused migration tests,
- run apply/rollback/reapply if repository conventions require it,
- confirm no pending model changes.

---

# Implementation Plan Update

If and only if Phase 11.3 implementation and required focused closing validations pass:

Update:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Mark only Phase 11.3 complete.

Record factual implementation details:

- exchange endpoint,
- request/response contract,
- capability hash lookup,
- reusable capability behavior,
- lifecycle checks,
- 15-minute effective token policy,
- share-access auth scheme/policy,
- rate limiting,
- privacy protections,
- migration/config changes if any,
- explicit confirmation no shared profile endpoint exists.

Record only tests/checks actually executed.

Explicitly state which global suites were **not** executed.

Example wording:

`The complete repository-wide unit and integration suites were intentionally not run for Phase 11.3 under the subphase testing policy; full regression is reserved for Phase 11 closure unless explicitly requested.`

Do not fabricate counts.

End with:

**Phase 11.4 has not started.**

---

# Explicitly Out of Scope

Do not implement Phase 11.4:

- `GET /api/v1/shared-access/profile`
- FullProfile projection
- Clinical History sharing
- PreTriage shared projection
- Symptom Diary shared projection
- Second Opinion projection
- SpecificRecords projection

Do not implement Phase 11.5:

- revoke endpoint
- activity endpoint
- expiry worker

Do not implement Phase 11.6+:

- export creation
- Beeexy JSON
- PDF
- FHIR export
- private artifact storage adapter
- artifact download

Also do not implement:

- frontend QR rendering
- QR image generation
- recipient accounts
- provider portal
- manager sharing authority
- Case semantics
- Visit execution
- Phase 13
- new clinical behavior
- new AI behavior
- new FHIR mappings

---

# Non-Negotiable Rules

1. Implement only Phase 11.3.
2. Add exactly one new Phase 11 endpoint: `POST /api/v1/shared-access/exchange`.
3. Capability remains reusable while grant is active.
4. Capability is never persisted or logged in plaintext.
5. Share-access token maximum lifetime is 15 minutes.
6. Effective token expiry cannot exceed ShareGrant expiry.
7. Later recipient operations must be able to re-check current ShareGrant state.
8. Share token is read-only and distinct from normal account authority.
9. Revoked/expired grant exchange fails safely.
10. Rate limiting is mandatory.
11. No shared profile endpoint yet.
12. No revoke/activity endpoint yet.
13. No export behavior yet.
14. No FHIR/PDF/storage implementation yet.
15. Follow existing repository architecture and conventions.
16. Do not weaken/delete/skip tests to save time.
17. Do not run global unit/integration suites automatically unless justified by transversal risk.
18. Do not mark Phase 11.3 complete unless required focused closing validation passes.

---

# Expected Final Codex Response

When finished, report concisely:

1. What Phase 11.3 implemented.
2. Main files modified.
3. Exact endpoint contract added.
4. Capability validation/hash behavior.
5. Share-access token design and effective expiry behavior.
6. Rate-limiting behavior.
7. Any migration/configuration changes, or confirmation none were needed.
8. Tests executed.
9. Results of each executed test group.
10. Builds/checks executed.
11. OpenAPI path count.
12. EF pending-model status.
13. Global unit/integration suites **not executed** under the subphase testing policy.
14. Any remaining risk/validation intentionally deferred to final Phase 11 regression.
15. Confirmation the implementation plan was updated.
16. Confirmation that **Phase 11.4 was not started**.

If a required focused validation fails, do not claim Phase 11.3 complete. Report the exact blocker and the smallest relevant failing test group.
