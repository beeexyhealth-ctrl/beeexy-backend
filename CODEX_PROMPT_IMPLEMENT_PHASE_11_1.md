# Codex Prompt — Implement Beeexy Phase 11.1

Implement **Phase 11.1 — Sharing and Export Domain + Persistence Foundation** in the Beeexy backend repository.

This task is intentionally limited to **Phase 11.1 only**.

Do **not** implement Phase 11.2 or any later Phase 11 behavior, even if Phase 11.1 completes successfully before the session ends.

Use the current authoritative implementation plan:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Read the complete Phase 11 section and the formally defined Phase 11.1 subsection before changing code.

Preserve all previously completed behavior from Phases 1–10.

---

# Primary Objective

Create the domain and PostgreSQL persistence foundation required for secure sharing and export artifacts, without exposing any new Phase 11 HTTP endpoint.

Phase 11.1 must leave the backend ready for later subphases to implement:

- share creation,
- QR/capability exchange,
- read-only shared access,
- revocation/expiry,
- share activity,
- Beeexy JSON,
- PDF,
- validated Phase 6 FHIR export,

but **none of those HTTP workflows are part of this task**.

---

# Mandatory Scope

Implement or finalize the Phase 11.1 foundation for:

- `ShareGrant`
- `ShareGrantItem`
- `ShareAccessEvent`
- `ExportArtifact`

Use the existing modular-monolith architecture and the `sharing` module/schema boundary.

The solution must remain consistent with:

- `Beeexy.Domain`
- `Beeexy.Application`
- `Beeexy.Infrastructure`
- `Beeexy.Api`
- `Beeexy.Tests.Unit`
- `Beeexy.Tests.Integration`

Do not introduce a new service/microservice.

---

# Approved Phase 11 Domain Vocabulary

Retain the exact Phase 11 scope vocabulary:

- `FullProfile`
- `Case`
- `PreTriage`
- `Visit`
- `SpecificRecords`

Important:

- `FullProfile`, `PreTriage`, and `SpecificRecords` are the executable MVP scopes in later subphases.
- `Case` and `Visit` are reserved/fail-closed for later work.
- Phase 11.1 may represent all five values structurally.
- Phase 11.1 must not implement scope execution/projection logic yet.
- Do not silently map one scope to another.

---

# Approved MVP Sharing Decisions That Must Influence the Model

The Phase 11 plan already resolved the following decisions and Phase 11.1 must support them structurally:

## Share lifetime

- Default later Phase 11 behavior: 24 hours.
- Maximum later Phase 11 behavior: 7 days.
- Permanent shares are not supported in MVP.

Phase 11.1 does not need to expose request validation or HTTP behavior, but the persistence/domain model must support a required finite expiry boundary.

Do not hardcode endpoint policy behavior into persistence when a later application policy should own it.

---

## Share authority

MVP external share creation will later be Primary Patient only.

Do not implement manager/caregiver external share authority.

Phase 11.1 should not add sharing authorization behavior yet.

Do not change Phase 3 authorization semantics.

---

## Capability

The model must support:

- cryptographically random capability generation later,
- plaintext returned only at creation time later,
- hash-only persistence,
- reusable capability while active,
- no one-time consumption requirement,
- revocation/expiry preventing later access.

Phase 11.1 must not generate or return an actual share capability through HTTP.

If a generator/hasher interface or primitive is introduced, keep it provider-neutral and do not create a Phase 11 endpoint.

---

## Share access token

Later Phase 11.3 will issue a 15-minute maximum read-only access token.

Phase 11.1 should not issue tokens.

Do not add token endpoints, JWT generation flows, or recipient authentication behavior now.

---

## Export retention

MVP export artifact retention is:

**30 days, configurable**

Phase 11.1 persistence must be able to represent artifact creation and retention/deletion eligibility cleanly.

Do not implement cleanup/background deletion yet unless the exact Phase 11.1 plan explicitly requires only a persistence primitive.

The actual retention worker belongs to a later subphase/closure as defined by the plan.

---

# Domain Requirements

Design the smallest truthful Phase 11.1 domain model that supports the approved plan.

## ShareGrant

A `ShareGrant` should represent an external, read-only sharing grant for one patient.

It must support the persisted data required for later subphases, including at minimum:

- stable UUID identity,
- patient ownership/reference,
- creator/account reference as approved by the architecture,
- scope,
- secure capability hash,
- creation timestamp,
- finite expiry timestamp,
- optional revocation timestamp / revocation metadata,
- lifecycle state derivable or represented truthfully,
- audit metadata consistent with the repository's conventions.

Use existing shared primitives/value-object patterns where appropriate.

Do not duplicate existing account/patient identity concepts.

### Invariants

At minimum:

- patient is required,
- creator identity is required if the approved schema requires creator audit,
- capability hash is required,
- expiry must be a valid instant after creation,
- plaintext capability is never stored,
- revocation is irreversible for MVP,
- already revoked grant cannot become active again,
- expiry/revocation must be distinguishable,
- share recipient has no mutation authority in the domain model.

Do not invent reactivation.

Do not invent recipient accounts.

Do not invent one-time capability consumption.

---

## ShareGrantItem

`ShareGrantItem` exists to support later granular sharing.

It must allow an exact grant to reference explicitly shared resources/items without encoding speculative clinical behavior.

At minimum structurally support:

- stable UUID identity,
- ShareGrant FK,
- item/resource category or supported source identity representation,
- exact referenced resource UUID/source identity as appropriate,
- deterministic uniqueness rules where needed,
- timestamps/audit fields consistent with the codebase.

Do not invent clinical semantics for `Case` or `Visit`.

Do not implement granular frontend behavior.

Do not create projections.

Avoid a schema that forces future Phase 13 `Visit` sharing to redesign the aggregate.

---

## ShareAccessEvent

`ShareAccessEvent` is immutable append-only audit/activity data.

It should support later safe event concepts equivalent to:

- `ShareCreated`
- `ShareAccessed`
- `ShareRevoked`
- `ShareExpired`
- export/download activity where needed later

Phase 11.1 only needs the domain/persistence foundation.

Do not add patient-facing activity endpoint yet.

Do not store:

- plaintext capability,
- capability hash in event payloads unless strictly unavoidable internally,
- JWT/share-access token,
- raw clinical payloads,
- prompts,
- AI provider data,
- full IP address,
- full User-Agent,
- unrestricted arbitrary JSON blobs containing sensitive content.

Prefer typed/minimized event metadata.

Events should be append-only.

---

## ExportArtifact

`ExportArtifact` represents an immutable generated export artifact snapshot.

It must support later:

- Beeexy JSON,
- PDF,
- Phase 6 validated FHIR JSON.

Represent export format in a stable enum/value object.

At minimum persist:

- stable UUID identity,
- patient identity,
- format,
- media type or equivalent safe representation,
- checksum,
- private storage identity/key/URI,
- creation timestamp,
- retention/deletion eligibility timestamp or equivalent,
- lifecycle/status metadata if required for later `incomplete -> complete/failed` handling,
- source/snapshot identity only if needed for reproducibility and already supported by the plan.

Important:

- storage identity is internal only,
- artifact content is not stored in normal logs,
- artifact is an immutable snapshot,
- source clinical data is not owned by/deleted with the artifact,
- deleting/expiring an artifact later must never delete the patient or source records.

Do not generate export bytes in Phase 11.1.

Do not introduce PDF or FHIR renderer implementation yet.

---

# Database Requirements

Use PostgreSQL schema:

`sharing`

Implement the Phase 11.1 persistence foundation for:

- `sharing.share_grants`
- `sharing.share_grant_items`
- `sharing.share_access_events`
- `sharing.export_artifacts`

Use EF Core migrations only.

Follow existing repository naming and schema conventions.

---

## Required database characteristics

Use:

- UUID primary keys,
- correct foreign keys,
- `timestamptz` for instants,
- restrictive deletion for permanent/audit data unless an existing project convention clearly requires another safe behavior,
- indexes justified by later lookup/security behavior,
- uniqueness constraints required by invariants,
- no plaintext capability column.

At minimum consider indexes for:

- share patient + creation time,
- capability hash lookup,
- active/expiry lookup,
- grant item lookup,
- event grant + event time,
- export patient + creation time,
- export retention/expiry eligibility.

Do not over-index without justification.

---

# Capability Hash Persistence

The database must store only a capability hash.

No field named or functioning as:

- `Capability`
- `RawToken`
- `PlainToken`
- `ShareToken`

may persist the plaintext capability.

If the repository already has a secure token-hash value object/pattern from earlier phases, reuse it where appropriate instead of inventing a parallel security primitive.

Do not log capability hashes unnecessarily.

---

# Export Storage Identity

The persistence model may store an internal storage key/URI/location required by the future `IPrivateArtifactStorage`.

This value must be treated as infrastructure/internal metadata.

Do not expose it in public DTOs.

No static/public filesystem route should be added.

---

# Application / Infrastructure Boundaries

Introduce only the interfaces and repository boundaries that are genuinely required by Phase 11.1.

Possible boundaries include equivalents of:

- `IShareGrantRepository`
- `IShareAccessEventRepository`
- `IExportArtifactRepository`
- `IShareCapabilityHasher`
- `IShareCapabilityGenerator`
- `IShareScopeEvaluator`
- `IPrivateArtifactStorage`
- export renderer abstraction(s)

However:

- do not create unused speculative abstractions merely to match names,
- follow existing Beeexy repository/application patterns,
- if a boundary is not required until 11.2+ and adding it now would create dead architecture, leave it to the later subphase,
- Phase 11.1 should prioritize truthful domain + persistence foundations.

No concrete PDF renderer.

No concrete production object-storage provider.

No Phase 6 FHIR adapter changes.

No QR library.

---

# API / OpenAPI

## Strict rule

**Phase 11.1 introduces zero new HTTP endpoints.**

Do not add:

- `/api/v1/shares`
- `/api/v1/shared-access/exchange`
- `/api/v1/shared-access/profile`
- `/api/v1/patients/{id}/exports`
- `/api/v1/exports/{id}/content`
- any other Phase 11 route.

Do not add placeholder controllers/endpoints.

OpenAPI path count must remain unchanged after Phase 11.1.

Add an explicit test if needed to prove no Phase 11 route appears.

---

# Authentication / Authorization

Do not change authentication behavior.

Do not add recipient auth.

Do not add share access JWT issuance.

Do not broaden Phase 3 manager authority.

Do not add new sharing permissions to managers.

Do not accept Beeexy ID as authority anywhere.

Phase 11.1 is persistence/domain only.

---

# FHIR

Phase 11.1 has **no FHIR implementation work**.

Do not:

- modify Phase 6 mappers,
- create a new FHIR mapper,
- add a new FHIR SDK,
- generate Bundle/QuestionnaireResponse/etc.,
- create FHIR export bytes,
- alter Andrea mappings.

Later Phase 11.7 must delegate exclusively to Phase 6.

Preserve that boundary.

---

# PDF

Phase 11.1 has **no PDF generation work**.

Do not install/select a PDF library unless absolutely required by the authoritative Phase 11.1 plan, which should not be necessary.

Do not create HTML/PDF templates.

Do not render files.

---

# Storage

Phase 11.1 may define persistence metadata and, only if the architecture genuinely requires it now, a provider-neutral private-storage interface.

Do not implement:

- Cloudflare R2,
- AWS S3,
- Azure Blob,
- public static storage,
- signed public URLs,
- production bucket provisioning.

Do not make vendor decisions in Domain.

---

# Logging / Privacy

Follow the backend-wide privacy policy.

Tests must prove where practical that Phase 11.1 structures/logging do not expose:

- capability plaintext,
- tokens,
- private artifact paths in user-facing data,
- patient clinical content,
- raw export content,
- raw AI content,
- provider metadata.

Technical logs should contain only privacy-minimized IDs/categories as already established by the codebase.

---

# Migration Requirements

Create the minimum additive EF Core migration required for Phase 11.1.

The migration must:

- create the `sharing` schema if needed,
- create the four required tables,
- create exact FK/check/unique/index rules,
- not modify unrelated Phase 1–10 tables unless technically necessary and justified,
- preserve all existing data,
- apply cleanly from the full existing migration chain,
- roll back cleanly,
- reapply cleanly.

After implementation:

- EF must report no pending model changes.

Do not manually edit generated migration metadata in unsafe ways.

---

# Testing Requirements

Phase 11.1 is not complete without focused domain, persistence, migration, architecture, and regression tests.

Use real PostgreSQL/Testcontainers where the existing project uses them.

---

## Domain tests

Cover at minimum:

### ShareGrant

- valid creation,
- required patient/creator/hash/expiry,
- invalid expiry,
- revocation transition,
- repeated revocation behavior according to domain design,
- no reactivation,
- expired vs revoked distinction,
- finite expiry support,
- supported scope values including reserved values,
- no plaintext capability property intended for persistence.

### ShareGrantItem

- valid exact grant association,
- uniqueness/invariant behavior,
- no cross-grant corruption,
- no speculative `Case`/`Visit` semantics.

### ShareAccessEvent

- immutable event creation,
- allowed event category representation,
- append-only behavior,
- privacy-minimized metadata.

### ExportArtifact

- valid format/media/checksum/storage metadata,
- supported format vocabulary for Beeexy JSON/PDF/FHIR JSON,
- immutable metadata as designed,
- retention eligibility support,
- no source-clinical ownership/deletion coupling.

---

## PostgreSQL integration tests

Cover at minimum:

- all four tables created,
- schema is `sharing`,
- FK integrity,
- capability hash uniqueness/lookup behavior as designed,
- no plaintext capability column,
- restrictive delete behavior,
- grant item uniqueness,
- event persistence/order,
- export artifact persistence,
- checksum/storage metadata round trip,
- patient/export lookup indexes or query behavior where relevant,
- retention timestamp round trip,
- invalid constrained rows rejected by PostgreSQL,
- full chain migration.

---

## Migration tests

Require:

- clean full-chain apply,
- Phase 11.1 rollback,
- reapply,
- no corruption of Phase 1–10 schema,
- EF no pending changes.

---

## Architecture/safety tests

Add tests/static assertions as appropriate proving:

- no Phase 11 HTTP endpoint exists,
- no new Phase 11 FHIR mapper exists,
- no QR implementation exists,
- no public artifact serving exists,
- no plaintext capability persistence contract exists.

Avoid brittle text-scanning if stronger architectural tests already exist.

---

# Regression Requirements

Run the complete existing backend test suite after focused tests pass.

At minimum ensure no regression in:

- identity/authentication,
- patient authorization,
- Pre-Triage,
- Clinical History,
- FHIR,
- Phase 7 directory,
- Phase 8 scheduling,
- Phase 9 Symptom Diary,
- Phase 10 AI.

Phase 11.1 must not alter existing public behavior.

---

# Build / Quality Gate

Before marking Phase 11.1 complete, run the repository-standard equivalents of:

```bash
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
git diff --check
```

Also run:

- focused Phase 11.1 tests,
- full PostgreSQL integration suite,
- migration tests,
- OpenAPI regression,
- EF pending model check.

Use the repository's exact existing commands/configuration where they differ.

No test may be skipped to claim completion.

---

# Implementation Plan Update After Successful Completion

If and only if the implementation and all required verification pass:

Update the Phase 11.1 subsection inside:

`IMPLEMENTATION_PLAN_09-09-2026-1601.md`

Add concise factual status documentation following the style already used by completed subphases elsewhere in the plan.

Include:

## Phase 11.1 status

`COMPLETE (<actual date>)`

## Implementation summary

Document only what was actually implemented, including:

- domain entities/value objects/enums,
- schema/tables,
- migration name,
- constraints/indexes,
- hash-only capability persistence,
- export artifact foundation,
- any interfaces/repositories actually added,
- explicit confirmation that no endpoint/export rendering/FHIR/QR behavior was introduced.

## Verification summary

Record exact facts from the run:

- build result,
- focused unit count,
- focused integration count,
- complete unit count,
- complete PostgreSQL integration count,
- migration verification,
- OpenAPI path count unchanged,
- EF pending model result,
- formatting result,
- `git diff --check`.

Do not invent test counts.

Do not mark later subphases started.

End with a concise statement that:

**Phase 11.2 has not started.**

---

# Explicitly Out of Scope

Do not implement any of the following in this task:

## Phase 11.2+

- `POST /api/v1/shares`
- `GET /api/v1/shares`
- capability returned to frontend
- public share URL generation
- idempotent share creation HTTP flow

## Phase 11.3

- capability exchange endpoint
- 15-minute share-access token
- recipient JWT/token issuance
- rate limiting for exchange

## Phase 11.4

- shared profile projection
- `FullProfile` composition
- PreTriage sharing
- SpecificRecords projection
- scope evaluation execution

## Phase 11.5

- revoke endpoint
- activity endpoint
- expiry background worker
- access/revoke concurrency workflow

## Phase 11.6

- Beeexy JSON generation
- export command endpoint
- filesystem artifact adapter
- storage writes
- retention cleanup worker

## Phase 11.7

- PDF generation
- PDF library selection
- FHIR export generation
- artifact download endpoint
- share-token download authorization

## Phase 11.8

- final Phase 11 acceptance closure

Also out of scope:

- frontend work,
- QR rendering,
- Phase 12 notifications,
- Phase 13 Visit implementation,
- new clinical behavior,
- new AI behavior,
- new FHIR mappings,
- production compliance claims.

---

# Non-Negotiable Rules

1. Do not implement beyond Phase 11.1.
2. Do not add HTTP endpoints.
3. Do not persist plaintext capabilities.
4. Do not broaden manager/caregiver authorization.
5. Do not add recipient accounts.
6. Do not add a FHIR mapper.
7. Do not generate PDF/JSON/FHIR export files yet.
8. Do not add public artifact storage.
9. Do not modify source clinical records.
10. Do not invent `Case` or `Visit` semantics.
11. Do not change unrelated completed phases.
12. Do not mark Phase 11.1 complete unless all required verification passes.

---

# Expected Final Codex Response

When finished, report concisely:

1. What Phase 11.1 domain/persistence components were implemented.
2. The exact migration created, if any.
3. Important constraints/indexes/security guarantees.
4. Focused test results.
5. Full test results.
6. OpenAPI path count and confirmation that it did not change.
7. EF pending-model status.
8. Formatting/whitespace verification.
9. Confirmation that the implementation plan was updated with factual Phase 11.1 completion evidence.
10. Confirmation that **Phase 11.2 was not started**.

If any required verification fails, do not claim Phase 11.1 complete. Report the exact remaining blocker instead.
