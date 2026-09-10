# Codex Prompt — Implement Beeexy Phase 11.2

Implement **Phase 11.2 — Primary-Patient Share Creation + Share Listing** in the Beeexy backend repository.

This task is intentionally limited to **Phase 11.2 only**.

Do **not** implement Phase 11.3 or any later Phase 11 behavior, even if Phase 11.2 completes successfully before the session ends.

Use the current authoritative implementation plan:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Read the complete Phase 11 section, the completed Phase 11.1 subsection, and the formal Phase 11.2 subsection before changing code.

Preserve all previously completed behavior from Phases 1–10 and Phase 11.1.

---

# Primary Objective

Add the first public Phase 11 sharing API surface:

- create a secure external share for the current patient's own Primary Patient profile,
- list that patient's shares safely,
- generate a cryptographically random capability,
- persist only its hash,
- return the plaintext capability exactly at creation time,
- enforce the approved 24-hour default / 7-day maximum expiry policy,
- support idempotent share creation,
- return a frontend-safe share URL suitable for QR rendering,
- expose no recipient-access behavior yet.

Phase 11.2 must not implement capability exchange or shared profile access.

---

# Endpoints to Implement

Implement exactly:

`POST /api/v1/shares`

`GET /api/v1/shares`

Do not add any other Phase 11 endpoint.

OpenAPI should change only by adding these two operations on the approved Phase 11 route surface.

---

# Authoritative Product Decisions

## Share creation authority

For MVP:

**Only the patient's own Primary Patient may create external shares.**

An Active Managed patient relationship does not grant external sharing authority.

Do not reinterpret Phase 3 management authority as sharing authority.
Do not add new caregiver/manager permissions.
Do not create a generic "authorized manager with sharing capability" path in MVP.

## Share duration

Approved:

- default lifetime: **24 hours**
- maximum lifetime: **7 days**
- shorter requested lifetimes are allowed
- non-expiring/permanent shares are not allowed

The server is authoritative.

## Public share URL

Approved production base URL:

`https://beeexy.ai/share`

Development/test must use configuration.

The backend response should use a frontend URL fragment convention, conceptually:

`https://beeexy.ai/share#<capability>`

Do not put the capability in the query string.
QR image generation remains frontend-only.

## Capability behavior

The capability:

- is cryptographically random,
- is returned in plaintext only on the first successful creation response,
- is persisted only as a secure hash,
- is reusable later while the grant remains active,
- is not a one-time token,
- must never appear in logs,
- must never be persisted in plaintext.

Do not issue share-access JWTs/tokens yet. That belongs to Phase 11.3.

## Executable scopes in Phase 11.2

The domain retains:

- `FullProfile`
- `Case`
- `PreTriage`
- `Visit`
- `SpecificRecords`

Allowed now:

- `FullProfile`
- `PreTriage`
- `SpecificRecords`

Reserved/unavailable:

- `Case`
- `Visit`

Reserved scopes must fail closed with a safe validation response. Never remap them to another scope.

---

# POST /api/v1/shares

## Purpose

Create one external read-only share grant for the authenticated account's own Primary Patient.

## Authentication

Bearer required.

## Authorization

Only the authenticated account's Primary Patient may be the subject.

Do not allow:

- managed patient selection,
- arbitrary patient UUID authority,
- Beeexy ID authority,
- subject Account ID input,
- manager relationship authority.

Prefer deriving the Primary Patient server-side if consistent with existing API design.
Never let the caller spoof creator/patient identity.

## Request

Define the smallest request contract required for Phase 11.2.

It may include only fields needed to create a share, such as:

- scope,
- optional requested lifetime/expiry within policy,
- idempotency key,
- explicit item selection only when required for `SpecificRecords`.

Reject caller-supplied:

- patient/account owner identity,
- creator identity,
- capability/token/hash,
- created/revoked timestamps,
- internal status,
- storage/export metadata,
- recipient identity,
- share-access token,
- activity/audit fields.

Reject unsupported/extra sensitive fields safely.

## Scope behavior

### FullProfile

Only create grant metadata. Do not build the shared profile projection yet.

### PreTriage

Only persist the exact grant scope and any exact source references required by the approved model. Do not expose shared content yet.

### SpecificRecords

If explicit `ShareGrantItem` rows are required, validate only structural requirements already defined by the plan/domain model.

Do not invent record categories, clinical semantics, or projections. Fail closed when exact semantics are not yet approved.

---

# Expiry Policy

Use the existing clock abstraction.

Server behavior:

- omitted lifetime => 24 hours
- requested lifetime <= 0 => reject
- requested lifetime > 7 days => reject
- permanent/no-expiry => reject
- expiry derived from server clock
- client cannot supply arbitrary server timestamps

Store `ExpiresAt` as an unambiguous instant.

---

# Idempotency

Use existing Beeexy idempotency conventions where available.

At minimum:

- first request with an idempotency key creates one share,
- exact replay returns the same logical grant result,
- incompatible reuse of the same idempotency key returns `409`,
- concurrent same-key requests converge to one persisted share,
- distinct keys may create distinct shares even if otherwise identical.

Persist a canonical request hash if the repository pattern requires it.

## Important security constraint

Do not weaken hash-only capability persistence just to make retries convenient.

If plaintext capability cannot be reconstructed after first persistence, do not invent recoverable plaintext storage.

Follow the authoritative Phase 11.2 plan for exact replay response semantics if it already resolves this. Otherwise choose the narrowest safe contract and document it explicitly in tests and the plan update.

---

# Capability Generation

Use a cryptographically secure RNG or an existing secure token generator pattern.

Requirements:

- sufficient entropy,
- URL-safe representation,
- no sequential/guessable data,
- no Patient ID/Beeexy ID embedded,
- no timestamp-derived secret,
- hash before persistence,
- no plaintext persistence.

---

# Share URL Construction

Use configuration-backed public share base URL.

Production value:

`https://beeexy.ai/share`

Construct:

`<baseUrl>#<capability>`

Do not:

- use `?token=...`,
- place capability in path,
- log the full URL,
- persist the full URL.

---

# POST Response

Return only safe creation metadata, likely including:

- shareGrantId,
- scope,
- createdAt,
- expiresAt,
- capability,
- shareUrl.

If SpecificRecords needs safe item metadata, include only approved references.

Never return:

- capability hash,
- Account IDs,
- internal creator IDs,
- audit internals,
- storage fields,
- share-access tokens.

---

# GET /api/v1/shares

## Purpose

List the authenticated Primary Patient's share grants.

## Authentication

Bearer required.

## Authorization

Current account / own Primary Patient only.

Do not list managed-patient or unrelated-account grants.

## Response

Return safe patient-facing metadata only, at minimum:

- shareGrantId,
- scope,
- status,
- createdAt,
- expiresAt,
- revokedAt if applicable,
- optional safe item count/scope metadata if already defined.

Never return:

- capability,
- capability hash,
- share URL containing capability,
- access token,
- creator Account ID,
- storage identifiers,
- technical audit metadata.

Ordering must be deterministic. Do not invent pagination unless the plan requires it.

---

# Share Status

Use the Phase 11.1 lifecycle representation.

List responses must truthfully distinguish at least:

- active,
- revoked,
- expired.

Do not create a second conflicting status model.

If expiry is derived, compute it deterministically using the server clock.
Do not mutate a share merely because it is listed unless the domain design explicitly requires persistence.

---

# Application Use Cases

Implement cohesive use cases equivalent to:

- `CreateShare`
- `ListShares`

Add only helper policies/services required for these flows, such as:

- current Primary Patient resolver usage,
- share lifetime policy,
- capability generator/hasher,
- canonical idempotency request hasher,
- share URL builder,
- safe response mapper,
- repository transaction boundary,
- privacy-safe `ShareCreated` event if required by the plan.

Do not implement:

- `ExchangeShareCapability`
- `BuildSharedProfile`
- `RevokeShare`
- `ExpireShares`
- `ListShareActivity`
- `GenerateExport`
- `DownloadExport`

---

# ShareCreated Event

If Phase 11.1 event infrastructure and the plan require it, append one safe `ShareCreated` event atomically with creation.

It must not contain capability, capability hash, full URL, or clinical content.

Idempotent replay must not create duplicate creation events.

If the authoritative plan defers this, do not invent it.

---

# Database / Persistence

Reuse the Phase 11.1 `sharing` schema and tables.

Do not create a redundant schema.

Add a migration only if genuinely required for Phase 11.2, for example idempotency metadata.

If no model change is needed:

- do not create an empty migration,
- verify EF reports no pending model changes.

Use the smallest necessary persistence change.

---

# Authentication / Authorization / IDOR

Reuse existing authentication/current-account resolution.

Explicitly test:

- missing bearer => `401`,
- malformed/invalid/expired bearer => existing `401` behavior,
- disabled account,
- missing/inconsistent Primary Patient invariant,
- Active Managed does not grant share creation,
- Revoked Managed does not grant share creation,
- reverse relationship grants nothing,
- Account A cannot create/list Account B's shares,
- Beeexy ID grants nothing,
- UUID knowledge grants nothing.

Use concealed `404` where existing patient-resource conventions require it.

---

# Privacy / Logging

Never log:

- capability,
- full share URL,
- capability hash,
- bearer token,
- raw sensitive request body,
- clinical content.

Safe logs may include privacy-minimized grant ID, scope category, result category, and timestamps.

Follow existing Beeexy logging conventions.

---

# OpenAPI

After Phase 11.2, expose exactly:

- POST `/api/v1/shares`
- GET `/api/v1/shares`

Document Bearer security, exact schemas, and safe status codes.

Expected statuses should include as applicable:

- `201`
- `200`
- `400`
- `401`
- concealed `404`
- `409`
- `422`
- safe `500`

No internal/hash fields in schemas.

Do not add:

- `/shared-access/exchange`
- `/shared-access/profile`
- revoke/activity endpoints
- export endpoints

---

# Error Expectations

## POST /shares

- malformed JSON => `400`
- unauthenticated => `401`
- inaccessible patient target if applicable => concealed `404`
- invalid scope => `422`
- reserved `Case`/`Visit` => `422`
- invalid lifetime => `422`
- unsupported/extra sensitive fields => `422`
- incompatible idempotency reuse => `409`
- safe unexpected failure => standard safe `500`

## GET /shares

- unauthenticated => `401`
- no shares => `200` with empty collection
- profile/account invariant failure => existing safe behavior

Do not leak unrelated resource existence.

---

# Testing Requirements

Phase 11.2 is not complete without focused application, real-PostgreSQL/API, security, OpenAPI, concurrency, and regression tests.

## Unit/Application tests

### CreateShare

Cover:

- Primary Patient success
- default 24h expiry
- custom shorter lifetime
- exact 7d maximum accepted
- >7d rejected
- zero/negative rejected
- permanent/no-expiry rejected
- `FullProfile` accepted
- `PreTriage` accepted
- `SpecificRecords` valid structural behavior
- `Case` rejected
- `Visit` rejected
- unknown scope rejected
- capability generated
- plaintext not persisted
- URL fragment construction
- configuration-backed base URL
- no query-string token
- server-derived timestamps
- idempotency first creation
- exact replay safe behavior
- incompatible key reuse conflict
- distinct keys => distinct grants
- ShareCreated exactly once if applicable

### ListShares

Cover:

- empty list
- one share
- multiple shares deterministic ordering
- active/revoked/expired mapping
- no capability/hash/share URL leakage
- current Primary Patient only

## PostgreSQL/API integration tests

Cover:

- one real grant persisted
- hash present, plaintext absent
- returned capability differs from persisted hash
- capability/hash uniqueness
- exact expiry persisted
- default/custom expiry
- reserved scopes fail before persistence
- concurrent same-key creation
- incompatible key reuse
- distinct-key concurrent creation
- grant items atomic where applicable
- rollback on child/event failure
- missing/malformed bearer
- disabled account
- Primary-only authority
- managed patient denied
- unrelated account IDOR
- Beeexy-ID non-authority
- listing ownership isolation
- response omits secret/internal fields
- privacy-safe logs
- safe Problem Details

## Capability security tests

Prove:

- sufficient entropy/length per implementation contract
- URL-safe encoding
- no predictable ID embedding
- no plaintext column
- no plaintext log
- no full URL log
- creation response is the only Phase 11.2 plaintext-return path
- GET never returns it

## Idempotency/concurrency tests

Use PostgreSQL where needed.

Prove:

- same-key concurrency yields one persisted grant
- no duplicate creation event
- conflicting reuse returns `409`
- distinct keys create distinct grants

Do not rely solely on process-local locks.

## OpenAPI tests

Verify:

- `/api/v1/shares` exists
- exactly GET + POST
- Bearer security
- expected response codes
- public schemas contain no internal/hash fields
- no Phase 11.3+ routes

OpenAPI path count should increase only as expected from the Phase 11.1 baseline.

---

# Regression Requirements

Run the full existing backend suite.

Preserve at minimum:

- Phase 2 authentication
- Phase 3 patient authority
- Phase 4 Pre-Triage
- Phase 5 Clinical History
- Phase 6 FHIR
- Phase 7 directory
- Phase 8 scheduling
- Phase 9 Symptom Diary
- Phase 10 AI
- Phase 11.1 persistence/migration behavior

Do not change unrelated public contracts.

---

# Migration Requirements

Prefer no new migration if Phase 11.1 already contains all necessary fields.

If Phase 11.2 requires additive idempotency fields/indexes:

- create one minimal migration,
- preserve existing rows,
- apply cleanly,
- rollback/reapply cleanly.

Do not create an empty migration.

EF must report no pending model changes.

---

# Build / Quality Gate

Before marking Phase 11.2 complete, run repository-standard equivalents of:

```bash
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
git diff --check
```

Also run:

- focused Phase 11.2 unit tests,
- focused real-PostgreSQL/API tests,
- Phase 11.1 regressions,
- migration tests,
- OpenAPI tests,
- full unit suite,
- full integration suite,
- EF pending-model check.

No skipped tests may be used to claim completion.

---

# Implementation Plan Update After Successful Completion

If and only if all required verification passes, update Phase 11.2 in:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Add factual completion documentation in the same style as completed subphases.

## Status

`COMPLETE (<actual date>)`

## Implementation summary

Document only what actually exists, including:

- the two endpoints,
- Primary-only authority,
- exact request/response contracts,
- supported/reserved scopes,
- default/max expiry behavior,
- capability generation/hash-only persistence,
- idempotency behavior,
- URL fragment behavior,
- listing projection,
- audit event behavior if implemented,
- migration changes if any,
- explicit confirmation that no recipient-access endpoint exists.

## Verification summary

Record exact real results:

- focused unit tests,
- focused PostgreSQL/API tests,
- full unit suite,
- full integration suite,
- migration verification,
- OpenAPI path count,
- EF pending-model result,
- build warnings/errors,
- formatting,
- `git diff --check`.

Do not invent counts.

End with:

**Phase 11.3 has not started.**

---

# Explicitly Out of Scope

Do not implement any of the following:

## Phase 11.3

- `POST /api/v1/shared-access/exchange`
- capability verification for recipient access
- exchange rate limiting
- 15-minute share access token
- recipient token issuance

## Phase 11.4

- `GET /api/v1/shared-access/profile`
- FullProfile data projection
- PreTriage shared projection
- SpecificRecords projection
- scope execution against patient records

## Phase 11.5

- revoke endpoint
- activity endpoint
- expiry worker
- access history endpoint

## Phase 11.6+

- export creation
- Beeexy JSON
- PDF
- FHIR export
- artifact storage
- artifact download

Also do not implement:

- QR image generation
- frontend work
- manager sharing permissions
- recipient accounts
- provider portal
- Phase 13 Visit behavior
- Case semantics
- new clinical logic
- new AI behavior
- new FHIR mappings

---

# Non-Negotiable Rules

1. Implement only Phase 11.2.
2. Add only POST/GET `/api/v1/shares`.
3. Primary Patient only can create/list external shares.
4. Do not grant sharing authority to managers.
5. Persist capability hash only.
6. Never log capability or full share URL.
7. Default lifetime is 24h.
8. Maximum lifetime is 7d.
9. `Case` and `Visit` fail closed.
10. No capability exchange yet.
11. No shared profile yet.
12. No export behavior yet.
13. No FHIR/PDF/storage implementation yet.
14. Do not modify unrelated phases.
15. Do not mark complete unless all required tests pass.

---

# Expected Final Codex Response

When finished, report concisely:

1. What was implemented for Phase 11.2.
2. Exact endpoint contracts added.
3. Final authority behavior.
4. Capability generation/hash behavior.
5. Expiry and idempotency behavior.
6. Any migration created, or confirmation none was needed.
7. Focused test results.
8. Full test results.
9. Final OpenAPI path count.
10. EF pending-model status.
11. Formatting/whitespace verification.
12. Confirmation that the implementation plan was updated.
13. Confirmation that **Phase 11.3 was not started**.

If any required verification fails, do not claim Phase 11.2 complete. Report the exact blocker instead.
