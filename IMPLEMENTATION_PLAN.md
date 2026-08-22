# Beeexy Backend Implementation Plan

**Plan status:** Approved requirements translated into executable phases. No implementation status is implied by this document.

## Source priority and unresolved inputs

This plan applies the following priority order:

1. The 86 confirmed product decisions.
2. Andrea's FHIR mappings/materials.
3. The approved backend domain analysis.
4. The legacy HTML prototype.

The prototype is reference material only. Its percentages, alerts, timers, lists, fake AI responses, doctor data, and other hardcoded behavior are not clinical or product requirements.

Andrea's FHIR Markdown materials are now present under `Backend/docs/fhir/`: `beeexy-coleccion-recursos.md`, `beeexy-provenance-device-ejemplo.md`, and `beeexy-riskassessment-ejemplo.md`. These files are the source of truth for Phase 6's exact FHIR mappings and requirements. Requirements not specified by those files remain explicit TBD items and must not be invented.

## Delivery priorities

- **MVP core:** Phases 1, 2, 4-8, 11, 12, and 14.
- **MVP should-have:** Phases 9 and 10 when their approved product/clinical inputs are available.
- **Conditional MVP:** Phase 3 (My Circle and Managed Patient Profiles), because caregiver/dependent workflows require additional authorization, consent, minor/adult, and legal decisions and are not required for the core MVP/demo; and Phase 13 (Visit Recording), because it is valuable but high risk.
- **Post-MVP:** Phase 15 and every capability explicitly deferred within earlier phases.

## Target architecture

Beeexy will be a modular monolith: one ASP.NET Core REST API, one PostgreSQL database, and clear in-process module boundaries. The initial solution will use:

- `Beeexy.Api` for HTTP, authentication middleware, OpenAPI, Problem Details, and composition.
- `Beeexy.Application` for commands, queries, authorization, validators, and provider interfaces.
- `Beeexy.Domain` for entities, value objects, invariants, and state transitions.
- `Beeexy.Infrastructure` for EF Core/PostgreSQL, identity implementation, files, background jobs, external adapters, and FHIR mapping.
- `Beeexy.Tests.Unit` and `Beeexy.Tests.Integration` for xUnit suites.

Module folders and PostgreSQL schemas will provide boundaries for `identity`, `patients`, `triage`, `history`, `care`, `directory`, `scheduling`, `ai`, `interoperability`, `sharing`, `notifications`, `visits`, and `audit`. These are not separate deployables. The design preserves future clinic/white-label evolution without adding premature tenant infrastructure or microservices.

### Cross-cutting implementation rules

- REST routes are versioned under `/api/v1`.
- Internal primary keys are UUIDs. `BeeexyId` is a separate unique patient identifier and is never accepted as authentication or sharing authority.
- Timestamps are stored as unambiguous instants (`timestamptz`). Clinic and user IANA timezone identifiers are retained where scheduling/display depends on them.
- API errors use Problem Details. Expected statuses are `400` malformed input, `401` missing/invalid authentication, `403` known resource but prohibited operation, concealed `404` for inaccessible patient-owned resources, `409` state/concurrency/idempotency conflict, `422` domain validation failure, and `429` rate limiting.
- All patient-scoped commands and queries call a shared authorization service. Controller/endpoint route checks alone are insufficient.
- Clinical records and AI results are immutable snapshots. Corrections create amendments or new versions.
- Database constraints are the final authority for uniqueness and appointment-slot reservation.
- External services are accessed through application interfaces with infrastructure adapters.
- FHIR packages and resource types remain inside the interoperability/infrastructure boundary; the domain does not reference FHIR.
- Logs exclude bearer tokens, capability tokens, raw documents/audio, prompts containing health information, and unnecessary clinical payloads.
- EF Core migrations are the only production schema-change mechanism.
- Every phase must build, start, apply migrations, pass all existing tests, and pass its new tests before being marked complete.

### Mandatory endpoint test matrix

Every endpoint introduced in every phase must have API/integration tests for:

- Successful request and response contract.
- Malformed and semantically invalid input.
- `401` when authentication is required.
- Authorization/ownership denial (`403` or concealed `404`) when applicable.
- Resource-not-found behavior.
- State, uniqueness, idempotency, and concurrency conflicts when applicable.
- Safe Problem Details that do not expose sensitive data or internals.

The phase-specific test sections below add required scenarios beyond this common matrix.

---

# Phase 1 — Backend and Database Foundation

**Priority:** MVP CORE
**Status:** COMPLETE (2026-08-19)
**Verification:** Debug build completed with 0 warnings and 0 errors; 51 tests passed (28 unit, 23 integration, 0 failed/skipped); all migrations applied to a fresh PostgreSQL 16 Testcontainer with only `__EFMigrationsHistory` present; Phase 1 health, OpenAPI, configuration, CORS, correlation, safe-error, and logging checks passed.

## 1. Objective

Create a compiling, runnable, migration-enabled modular-monolith foundation so every later phase can add a vertical slice without redesigning project boundaries, error handling, tests, or PostgreSQL setup.

## 2. Scope

- Solution/projects and dependency rules.
- ASP.NET Core composition, configuration validation, OpenAPI, Problem Details, correlation IDs, structured logging, CORS, and health checks.
- EF Core/Npgsql context and initial migration.
- Local PostgreSQL development configuration.
- xUnit unit/integration projects, PostgreSQL Testcontainers, and CI commands.

## 3. Explicitly Out of Scope

- Accounts, patients, clinical entities, product endpoints, authentication, external providers, and FHIR resources.

## 4. Domain Model

- Shared `EntityId`/UUID convention, `DomainError`, `DomainException`, time abstraction, and audit metadata primitives.
- No healthcare/product entity is introduced.
- Dependency invariant: Domain depends on nothing; Application depends on Domain; Infrastructure depends on Application/Domain; API composes Application/Infrastructure.

## 5. Database Changes

- Initial empty/foundation migration and EF migration history.
- No clinical tables.
- Connection configuration supplied through environment/secret providers; local-only example values may be documented but not treated as production secrets.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `GET /health/live` | None | Public | Confirm process liveness | `200` minimal JSON | `503` only for process-level failure |
| `GET /health/ready` | None | Public | Confirm PostgreSQL readiness | `200` minimal JSON | `503` when PostgreSQL is unavailable; no connection details |

## 7. Application / Use Cases

- Dependency-registration entry points.
- Exception-to-Problem-Details mapping.
- Correlation ID propagation.
- Startup configuration validation.
- Database migration workflow.

## 8. Authentication and Authorization

No authentication yet. Health endpoints reveal no product data or configuration.

## 9. Security and Privacy

- Production HTTPS/HSTS and restrictive environment-aware OpenAPI exposure.
- CORS allow-list for configured frontend origins.
- Secret/configuration separation and log redaction.
- No request-body logging by default.

## 10. External Integrations

- **IMPLEMENT NOW:** PostgreSQL.
- **INTERFACE/PLACEHOLDER:** none.
- **POST-MVP:** hosting-specific observability and secret-vault adapters.

## 11. FHIR Impact

None.

## 12. Tests

- Unit tests for shared error primitives and safe error mapping.
- Architecture tests for project dependency direction.
- Integration tests for both health endpoints, including unavailable PostgreSQL.
- Fresh PostgreSQL migration application test using Testcontainers.
- OpenAPI generation smoke test.
- Correlation-header and sensitive-log-redaction tests.
- Mandatory endpoint test matrix for both health endpoints.

## 13. Acceptance Criteria

- Solution restores and builds with zero errors.
- API starts with valid configuration and fails fast with invalid configuration.
- Initial migration applies to fresh PostgreSQL.
- Liveness, readiness, and OpenAPI behave as specified.
- Unit and integration suites pass.
- No product entity or endpoint has been implemented.

## 14. Dependencies

- Supported .NET LTS SDK, Docker-compatible engine, and PostgreSQL image.
- No product/clinical decision dependency.

## 15. Deferred / TBD Items

- Production hosting, backup/restore objectives, monitoring vendor, infrastructure topology, and secret manager.

---

# Phase 2 — Identity, Authentication, and Primary Patient Profile

**Priority:** MVP CORE

## 1. Objective

Provide secure email authentication, optionally enabled Google authentication, rotating sessions, and exactly one primary `PatientProfile` per MVP account.

## 2. Scope

- Passwordless email OTP based on the established product flow.
- Account provisioning on first verified sign-in.
- Access tokens and rotating refresh sessions.
- Optional Google identity adapter/configuration.
- Current-account and primary-profile read/update.
- User timezone and basic preferences.

## 3. Explicitly Out of Scope

- Apple authentication, passwords, caregiver-only accounts, dependent claiming, legal identity verification, complex account recovery, and administrative identity UI.

## 4. Domain Model

- Entities: `Account`, `PatientProfile`, `EmailAuthenticationChallenge`, `ExternalIdentity`, `RefreshSession`, `UserPreference`.
- Relationships: `Account` has one primary `PatientProfile` in MVP; `PatientProfile.AccountId` remains nullable/unique so dependent profiles and future claiming are possible.
- Value objects: normalized email, Beeexy ID, timezone, token hash.
- Statuses: account active/disabled; challenge pending/consumed/expired; refresh active/revoked/expired.
- Invariants: account/profile creation is atomic; Beeexy ID is immutable/non-secret and grants no access; demographic requirements come only from Andrea's materials.

## 5. Database Changes

- Tables: `identity.accounts`, `identity.email_authentication_challenges`, `identity.external_identities`, `identity.refresh_sessions`, `patients.patient_profiles`, `patients.user_preferences`.
- UUID PKs; unique normalized email; unique provider/subject; unique non-null profile `account_id`; unique `beeexy_id`.
- Store OTP/refresh-token hashes, expirations, attempt counts, consumed/revoked timestamps, created/updated timestamps.
- Index active refresh sessions and unconsumed challenge expiry.
- Migration creates account/profile transaction support without cascading deletion of future clinical data.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/auth/email/challenges` | None; rate limited | Public | Send one-time email code | `202` | Invalid email `422`; throttle `429`; enumeration-safe response |
| `POST /api/v1/auth/email/verify` | None | Possession of valid challenge/code | Verify/sign in or provision | `200` token pair + account summary | Invalid/expired `401`; consumed/replay `409`; attempt limit `429` |
| `POST /api/v1/auth/google` | None | Valid configured Google identity | Sign in/provision using Google | `200` token pair | Invalid identity `401`; provider disabled/unavailable `503` |
| `POST /api/v1/auth/refresh` | Refresh token | Owning active session | Rotate token pair | `200` | Expired/revoked/reused `401` |
| `POST /api/v1/auth/logout` | Bearer | Current session | Revoke session | `204` | Idempotent if already revoked |
| `GET /api/v1/auth/me` | Bearer | Current account | Account/profile reference | `200` | `401`; inconsistent profile `500` audited safely |
| `GET /api/v1/patients/me` | Bearer | Own primary profile | Read demographics | `200` | `401`, `404` |
| `PATCH /api/v1/patients/me` | Bearer | Own primary profile | Update permitted demographics/preferences | `200` | Invalid/TBD field `422`; concurrency `409` |

## 7. Application / Use Cases

- `RequestEmailChallenge`, `VerifyEmailChallenge`, `AuthenticateWithGoogle`, `RotateRefreshSession`, `LogoutSession`.
- `ProvisionAccountAndPrimaryProfile` transaction.
- `GetCurrentAccount`, `GetPrimaryProfile`, `UpdatePrimaryProfile`.
- Rate limiting, refresh reuse detection, and profile concurrency handling.

## 8. Authentication and Authorization

- Short-lived signed access token; opaque rotating refresh token stored hashed.
- OTP is short-lived, one-time, hashed, attempt-limited, and rate-limited by normalized email/IP.
- Google is enabled only with valid configuration.
- Profile endpoints use account-to-primary-profile ownership, never Beeexy ID authority.

## 9. Security and Privacy

- Generic challenge response prevents account enumeration.
- Tokens and OTPs never appear in logs.
- Refresh reuse revokes the affected session chain.
- Demographic changes are audited without logging previous sensitive values in technical logs.

## 10. External Integrations

- **IMPLEMENT NOW:** transactional authentication email through `IAuthenticationEmailSender` plus a non-production test adapter.
- **IMPLEMENT NOW IF CONFIGURED:** Google identity validation through `IExternalIdentityProvider`.
- **POST-MVP:** Apple and product email/SMS notification channels.

## 11. FHIR Impact

Patient demographics are internal source data only. Exact required fields and later FHIR representation remain governed by Andrea's materials.

## 12. Tests

- OTP success, expiry, attempts, replay, throttling, and enumeration resistance.
- Concurrent first sign-ins create one account/profile.
- Refresh rotation, revocation, and reuse detection.
- Google adapter success/failure/disabled tests.
- Profile validation, optimistic concurrency, and ownership tests.
- Database uniqueness tests for email, external identity, account profile, and Beeexy ID.
- Verify Beeexy ID cannot authenticate or retrieve data.
- Mandatory endpoint test matrix for all eight endpoints.

## 13. Acceptance Criteria

- Email sign-in works end to end and provisions exactly one primary profile.
- Sessions rotate/revoke securely.
- Google can be enabled without domain/application changes.
- Patient demographics follow available approved material; unresolved fields remain optional/configurable.
- All migrations and tests pass.

## 14. Dependencies

- Phase 1.
- Transactional email credentials for deployment; Google credentials only when enabled.
- Andrea's demographic specification for final field requirements.

## 15. Deferred / TBD Items

- Sex-at-birth/gender-identity policy, caregiver-only accounts, Apple, dependent claiming, and production identity/legal verification.

---

# Phase 3 — My Circle and Managed Patient Profiles

**Priority:** CONDITIONAL MVP
**Phase 3.1 status:** COMPLETE (2026-08-20)
**Phase 3.1 verification:** Debug build completed with 0 warnings and 0 errors; 276 tests passed (161 unit, 115 integration, 0 failed/skipped); the dedicated `Phase31CareRelationshipFoundation` migration applied on PostgreSQL 16, rolled back/reapplied successfully, and EF reported no pending model changes. No Phase 3 API endpoint, application use case, FHIR mapping, or authorization behavior was introduced.
**Phase 3.2 status:** COMPLETE (2026-08-20)
**Phase 3.2 implementation:** Added only the authenticated `POST /api/v1/care-relationships` vertical slice. The server derives the active manager account and its single primary `PatientProfile` from the bearer token, creates an unowned managed `PatientProfile` plus active `CareRelationship` in one explicit transaction, records the server-timestamped authorization attestation, returns a minimal `201` relationship/patient summary, maps validation and uniqueness failures to safe `422`/`409` responses, and emits privacy-safe creation/conflict audit events. Request-supplied manager or subject identity fields are rejected. No managed-patient account, authentication identity, or session is provisioned.
**Phase 3.2 verification:** Restore succeeded; Debug build completed with 0 warnings and 0 errors; 312 tests passed (182 unit, 130 PostgreSQL integration, 0 failed/skipped). Focused coverage passed for 20 application cases and 15 endpoint/PostgreSQL cases, including all supported relationship types, invalid/missing attestation, unauthenticated and disabled accounts, safe manager-invariant failure, forbidden identity input, uniqueness conflict mapping, atomic rollback without an orphan profile, multiple managed patients, and OpenAPI scope. EF reported no pending model changes, so no Phase 3.2 migration was added. Phase 3.3 and later Phase 3 endpoints remain deferred.
**Phase 3.3 status:** COMPLETE (2026-08-20)
**Phase 3.3 implementation:** Added only `ListAccessiblePatients`, `ListCareRelationships`, authenticated `GET /api/v1/patients`, and authenticated `GET /api/v1/care-relationships`. Accessible patients contain the current Account's primary profile first and only subjects reached through Active manager relationships; managed entries are ordered by relationship creation time and relationship ID, defensively deduplicated by patient identity, and include concise relationship context. Relationship history is scoped strictly to rows where the current primary profile is manager, includes both Active and Revoked records, and is ordered by creation time and relationship ID. Responses expose patient/relationship summaries without Account IDs, authentication state, creator/revoker IDs, or persistence metadata. No pagination was introduced because Phase 3 does not require it.
**Phase 3.3 verification:** Restore succeeded; Debug build completed with 0 warnings and 0 errors; 341 tests passed (195 unit, 146 PostgreSQL integration, 0 failed/skipped). Focused coverage passed for 7 `ListAccessiblePatients` cases, 6 `ListCareRelationships` cases, and 16 endpoint/PostgreSQL cases. The matrix verifies primary-only and empty states, Phase 3.2 creation-to-listing, Active/Revoked access separation, deterministic ordering, one manager with multiple subjects, multiple managers independently accessing one subject through separate relationships, exclusion of relationships where the current patient is only the subject, unrelated UUID/Beeexy-ID isolation, invalid authentication, disabled accounts, safe invariant failure, and exact OpenAPI scope. Six focused migration tests passed and EF reported no pending model changes, so no Phase 3.3 migration was added. Phase 3.4 shared patient authorization and all patient-by-ID/update/revocation behavior remain deferred.
**Phase 3.4 status:** COMPLETE (2026-08-20)
**Phase 3.4 implementation:** Added the internal `AuthorizePatientAccess` application service as the single shared authorization decision for future patient-by-ID operations. It resolves the authenticated Account's actual primary profile, grants `Primary` only for that profile, grants `Managed` only through an exact Active manager-to-subject `CareRelationship`, and otherwise returns one concealable `Denied` result for both absent and unauthorized targets. The targeted repository checks only target existence and the exact active relationship; it does not reuse collection listing. Denied decisions emit privacy-safe internal audit categories without changing the public result. No HTTP endpoint, database model change, record-sharing behavior, or patient read/update/revocation use case was introduced.
**Phase 3.4 verification:** Restore succeeded; Debug build completed with 0 warnings and 0 errors; 365 tests passed (208 unit, 157 PostgreSQL integration, 0 failed/skipped). Focused coverage passed for 13 unit cases and 11 PostgreSQL integration cases, including primary access, another Account's primary-profile denial, Active and Revoked relationships, independent authorization and revocation with multiple managers, subject-side and creator/identifier non-authority, indistinguishable absent/unauthorized results with safe internal audit categories, Phase 3.2 creation-to-authorization, and Phase 3.3 listing consistency. OpenAPI remains unchanged at 11 paths with no Phase 3.4 route. Six focused migration tests passed and EF reported no pending model changes, so no Phase 3.4 migration was added. Patient-by-ID read, managed-patient update, and relationship revocation remain deferred to later Phase 3 work.
**Phase 3.5 status:** COMPLETE (2026-08-21)
**Phase 3.5 implementation:** Added authenticated `GET /api/v1/patients/{patientId}` and the `GetPatientProfile` application use case. The use case delegates every access decision to Phase 3.4 `AuthorizePatientAccess`, returns primary and actively managed profiles through a targeted no-tracking profile repository, and maps both `Denied` authorization and a post-authorization disappearance race to the same concealed patient-not-found outcome. The response contains only patient-scoped `profileId` and `beeexyId`; authorization reason remains internal, and account-scoped preferences/version remain exclusive to `/api/v1/patients/me`. A GUID route constraint preserves the static `/patients/me` route and makes malformed UUID or Beeexy-ID paths return the existing routing-level `404`. OpenAPI route-constraint normalization was added so the documented detail operation retains its Bearer security requirement. No update, revocation, demographic, sharing, or database behavior was introduced.
**Phase 3.5 verification:** Restore and targeted formatting verification succeeded; Debug build completed with 0 warnings and 0 errors; 388 tests passed (218 unit, 170 PostgreSQL integration, 0 failed/skipped). Focused Phase 3.5 coverage passed for 10 unit cases and 13 endpoint/PostgreSQL cases. The matrix verifies own-primary and active-managed reads, identical public Problem Details for nonexistent and unauthorized real patients, Revoked denial with preserved patient/relationship rows, independent multiple-manager access with one-manager-only revocation, reverse-relationship denial, primary and managed cross-account IDOR denial, Beeexy-ID non-authority, missing/invalid bearer behavior, malformed UUID routing, safe invariant failure, Phase 3.2 creation-to-read, Phase 3.3 list-to-detail consistency, `/patients/me` routing, and the exact two-field response. All Phase 3.4 regressions passed separately (13 unit and 11 PostgreSQL cases). OpenAPI contains 12 paths and adds only the patient-detail GET with `200`, `401`, concealed `404`, and `500`; no patient-detail PATCH or relationship DELETE is present. Six focused migration tests passed and EF reported no pending model changes, so no Phase 3.5 migration was added. Authorized managed-patient update and relationship revocation remain deferred; Phase 3.6 has not started.
**Phase 3.6 historical status:** PRODUCT-DATA BLOCKED; CONSERVATIVE AUTHORIZATION BOUNDARY COMPLETE (superseded later on 2026-08-21 by the approved-demographics completion below)
**Phase 3.6 implementation:** The current `PatientProfile` contains no approved mutable demographic field and no patient-level concurrency token; the only existing mutable field is Account-scoped `UserPreference.Timezone`, which cannot validly apply to an unowned managed patient. Following Phase 3.6 Case C, added `UpdateManagedPatient` and authenticated `PATCH /api/v1/patients/{patientId}` as an authorization-first conservative boundary without inventing patient data. The use case delegates to Phase 3.4 `AuthorizePatientAccess`; nonexistent, unrelated, and Revoked targets produce the same concealed `404`, while Primary or Managed targets receive `422` because no patient field is currently available for mutation. Any supplied field—including identifiers, relationship metadata, `timezone`, speculative demographics, or `version`—is rejected as unsupported, and an empty patch receives a dedicated no-mutable-fields validation result. No write repository, success audit, patient concurrency mechanism, or `200`/`409` contract was fabricated. Existing `/patients/me` preference updates retain their independent versioned concurrency behavior.
**Phase 3.6 verification:** Restore and targeted formatting verification succeeded; Debug build completed with 0 warnings and 0 errors; 417 tests passed (235 unit, 182 PostgreSQL integration, 0 failed/skipped). Focused conservative Phase 3.6 coverage passed for 17 unit cases and 12 endpoint/PostgreSQL cases. The matrix verifies Primary and Active Managed authorization reach the `422` product-field boundary, exact nonexistent/unrelated/Revoked `404` equivalence, preserved patient/relationship/preference state, immutable and unsupported-field rejection, independent multiple-manager authorization with one-manager-only revocation, cross-account primary/managed IDOR denial, Beeexy-ID non-authority, bearer and route validation, Phase 3.2 creation-to-read-to-rejected-update consistency, safe invariant failure, and unchanged `/patients/me` success/stale-`409` behavior. Phase 3.4/3.5 regressions passed separately (23 unit and 24 PostgreSQL cases). OpenAPI remains at 12 paths and adds only PATCH on the existing patient-detail path, truthfully documenting `400`, `401`, concealed `404`, `422`, and `500`; `200` and `409` are intentionally absent until a real patient mutation/version model is approved. Six migration tests passed and EF reported no pending model changes, so no migration was added. Phase 3.6 cannot be marked functionally complete until product-approved patient fields and their concurrency ownership are defined. Relationship revocation and Phase 3.7 remain unimplemented.
**Phase 3.7 status:** COMPLETE (2026-08-21)
**Phase 3.7 implementation:** Added `RevokeCareRelationship` and authenticated `DELETE /api/v1/care-relationships/{id}`. The application resolves the active Account and its single primary manager profile, then uses a targeted manager-scoped PostgreSQL row lock on the relationship before invoking the existing irreversible domain `Active → Revoked` transition. The first transition persists the server timestamp, revoker Account ID, status, and update timestamp atomically; an already-Revoked relationship owned by the same manager returns idempotent `204` without changing metadata or emitting a duplicate transition audit. Absent and foreign-manager relationship IDs map to identical concealed `404` responses, creator identity and UUID knowledge confer no authority, and malformed UUIDs retain routing-level `404`. Revocation never deletes the subject or relationship history. Existing Active-only authorization/listing queries immediately remove the former manager's patient access while retaining the Revoked relationship in history; another manager's independent relationship remains Active. Privacy-safe audit records only the first successful transition using technical IDs, relationship type, and timestamp. OpenAPI adds only the authenticated DELETE operation. Phase 3.6 remains product-data blocked; no demographics, patient mutation, reactivation, deletion, hardening, or Phase 4 behavior was added.
**Phase 3.7 verification:** Restore and formatting succeeded; Debug build completed with 0 warnings and 0 errors; 436 tests passed (244 unit, 192 PostgreSQL integration, 0 failed/skipped). Focused Phase 3.7 coverage comprises 19 cases (9 unit, including safe exception mapping, and 10 endpoint/PostgreSQL). The matrix verifies normal and repeated revocation, stable metadata and single transition audit, missing/invalid bearer, malformed UUID routing, disabled Account and primary-profile invariant behavior, indistinguishable absent/foreign `404`, manager authority independent of creator identity, multiple-manager isolation, subject/Beeexy-ID/history preservation, two concurrent DELETE requests returning `204`, and the mandatory create→list→read→revoke→denied-read/denied-PATCH→repeat flow. Phase 3.4–3.6 regressions passed separately (40 unit and 36 PostgreSQL cases), including revoked-PATCH authorization precedence (`404`, not `422`). OpenAPI contains 13 paths and the relationship-detail path exposes only DELETE with `204`, `401`, concealed `404`, and `500`. Six migration tests passed and EF reported no pending model changes, so no migration was added. Phase 3.8 has not started.
**Phase 3.6 approved-demographics completion status:** COMPLETE (2026-08-21)
**Phase 3.6 approved-demographics implementation:** Product approval now limits `PatientProfile` demographics to FirstName, LastName, DateOfBirth, SexAssignedAtBirth (`Male`/`Female`), and one of the 50 two-letter U.S. state codes. Names are trimmed Unicode text with a 100-character limit; DOB is an ISO date and cannot be future; state input is trimmed and uppercased. Migration `Phase36ApprovedPatientDemographics` adds nullable demographic columns so existing profiles survive without invented values, plus a positive `version` initialized to 1 and configured as the dedicated EF concurrency token. Newly provisioned primary profiles may remain incomplete, while new managed-patient creation requires all five fields atomically with the relationship. Patient detail exposes all fields and version; patient and relationship collections expose names only. Authorized patient PATCH supports partial updates to exactly those fields, delegates access solely to `AuthorizePatientAccess`, returns concealed `404` before body validation for revoked/unrelated/missing targets, returns stale `409`, increments once per effective update, and preserves version/timestamp for same-value updates. Privacy-safe audit records technical IDs, access reason, changed field categories, and time but no demographic values. `/patients/me` additively exposes demographics and `profileVersion`; its existing `version` remains exclusively the independent `UserPreference` timezone token, and demographic mutation uses `/patients/{patientId}`. Phase 3.7 revocation behavior remains intact. No additional demographics, Phase 3.8, or Phase 4 behavior was introduced.
**Phase 3.6 approved-demographics verification:** Formatting and `git diff --check` succeeded; the full Debug build completed with 0 warnings and 0 errors. The complete backend suite passed 469 tests: 261 unit and 208 PostgreSQL integration, with 0 failed and 0 skipped. Focused coverage includes name/state value objects, managed creation validation and atomicity, legacy nullable profile migration, detail/list projections, primary and managed PATCH, invalid/unknown fields, same-value no-op, sequential stale `409`, real concurrent multiple-manager one-winner behavior, revoked authorization precedence, independent `/patients/me` preference concurrency, Phase 3.7 revocation regression, and OpenAPI demographic schemas/status/security. Migration `20260821065021_Phase36ApprovedPatientDemographics` applies on the full chain, rolls back/reapplies over Phase 3.1, preserves a pre-demographics row with null fields and version 1, and EF reports no pending model changes. OpenAPI remains at 13 paths; no Phase 3.8/Phase 4 endpoint was added.
**Phase 3.8 status:** COMPLETE (2026-08-21). Phase 3 technical acceptance is closed; final human-readable product/legal attestation wording remains an external product-content dependency and is not represented as legal or identity verification.
**Phase 3.8 implementation:** Audited all six Phase 3 endpoints, the shared `Primary`/`Managed`/`Denied` authorization service, database constraints/FKs/indexes, transaction boundaries, error mapping, privacy logging, OpenAPI, and frontend documentation. Managed-patient creation now resolves and rejects an inactive Account before any domain or unsupported-field validation, while still validating before persistence and creating patient plus relationship atomically. Managed demographic PATCH retains its initial concealed authorization-before-validation decision, then reauthorizes the exact Active manager/subject relationship under a PostgreSQL `FOR SHARE` row lock inside the write transaction; relationship revocation uses its existing `FOR UPDATE` lock, so whichever operation wins serializes safely and a completed revocation cannot be followed by a stale authorized mutation. OpenAPI now explicitly enumerates all seven relationship types and both relationship statuses. No endpoint, demographic, sharing behavior, dependent-specific model, Phase 4 behavior, or migration was added.
**Phase 3.8 verification:** Restore was already current; formatting and `git diff --check` succeeded; the complete Debug build succeeded with 0 warnings and 0 errors. The full suite passed 475 tests: 261 unit and 214 PostgreSQL integration, 0 failed and 0 skipped. Five dedicated Phase 3.8 PostgreSQL tests cover all six endpoints against missing/malformed/wrong-signature/wrong-issuer/wrong-audience/expired/Beeexy-ID credentials, disabled-account precedence, the exact Account A/Account B/Patient X journey, demographic-log privacy, revocation-versus-PATCH locking, and exact OpenAPI scope/enums. One additional PostgreSQL test proves two concurrent inserts for the same manager/subject persist exactly one Active relationship. The affected security/creation/constraint/OpenAPI set passed 35/35; all 469 pre-Phase-3.8 regression cases passed inside the full suite. Eight focused migration/FK tests passed, including clean apply and Phase 2/3.1/3.6 rollback/reapply behavior; EF reported no pending model changes. OpenAPI remains at 13 paths with exactly the six approved Phase 3 operations, and `docs/frontend-api-integration.md` now contains their exact contracts.
**Phase 3 acceptance criteria:** PASS — primary and managed patients are rows in the same `patients.patient_profiles` table, distinguished only by non-null versus null `AccountId`. PASS — My Circle management authorizes only implemented patient list/read/demographic-update behavior and is not record sharing. PASS — revocation preserves the patient, Beeexy ID, demographics, relationship history, and other managers. PASS — authorization, IDOR, concealed `404`, multiple-manager, concurrency, migration, Phase 1/2 regression, and complete test suites pass.
**Phase 3 attestation/deferred boundary:** Technical attestation support is complete. Final product/legal wording is an external product-content dependency. Legal verification, adult consent workflows, minor-specific workflows, invitations, profile claiming, granular manager permissions, record sharing, relationship reactivation, and FHIR relationship/consent mapping remain intentionally deferred.

## 1. Objective

Model dependents as independent patients and grant/revoke legitimate management relationships without transferring or deleting their health data.

## 2. Scope

- Create a managed patient profile with relationship attestation.
- List accessible profiles and relationships.
- Read/update permitted managed-profile demographics.
- Revoke management access.
- Support multiple caregivers structurally.

## 3. Explicitly Out of Scope

- Minor-specific law/workflows, external consent verification, invitations, profile claiming, arbitrary friend management, and record sharing.

## 4. Domain Model

- Entity: `CareRelationship` between manager and subject `PatientProfile`.
- Enum: `Parent`, `LegalGuardian`, `Caregiver`, `Spouse`, `Child`, `Sibling`, `Other`.
- Status: `Active`, `Revoked`.
- Value object: authorization attestation version/timestamp.
- Invariants: manager and subject differ; relationship type is allowed; revocation never deletes subject/records; management and sharing are separate; multiple managers are supported.

## 5. Database Changes

- Table `patients.care_relationships` with UUID PK, manager/subject FKs, type/status, creator, attestation version/time, revocation metadata, timestamps.
- Unique partial index for active manager/subject relationship.
- Manager/status and subject/status indexes.
- Check against self-relationship.
- No cascade from relationship removal to patient or health records.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `GET /api/v1/patients` | Bearer | Current account | List primary + actively managed profiles | `200` | `401` |
| `POST /api/v1/care-relationships` | Bearer | Current primary patient as manager | Create managed profile + relationship | `201` | Missing attestation/type `422`; duplicate `409` |
| `GET /api/v1/care-relationships` | Bearer | Current manager | List active/revoked relationships | `200` | `401` |
| `GET /api/v1/patients/{patientId}` | Bearer | Owner or active manager | Read profile | `200` | Concealed `404` when absent/unauthorized |
| `PATCH /api/v1/patients/{patientId}` | Bearer | Owner or active manager | Update permitted demographics | `200` | `404`, `409`, `422` |
| `DELETE /api/v1/care-relationships/{id}` | Bearer | Relationship manager | Revoke relationship | `204` | `404`; repeat is idempotent |

## 7. Application / Use Cases

- `CreateManagedPatient`, `ListAccessiblePatients`, `ListCareRelationships`, `GetPatientProfile`, `UpdateManagedPatient`, `RevokeCareRelationship`.
- Shared `AuthorizePatientAccess` service with explicit access reason.

## 8. Authentication and Authorization

- All endpoints require bearer authentication.
- Active relationship grants only approved management capability.
- Revocation immediately removes access.
- Patient UUID/Beeexy ID alone never grants authority.

## 9. Security and Privacy

- Unauthorized patient resources return concealed `404`.
- Relationship creation records attestation but does not claim production legal verification.
- Audit relationship creation/revocation and denied access.

## 10. External Integrations

- **IMPLEMENT NOW:** none.
- **POST-MVP:** legal/identity/consent verification and invitations.

## 11. FHIR Impact

None until Andrea defines any relationship/consent mapping.

## 12. Tests

- Managed patient uses the same entity structure as primary patient.
- Multiple-manager persistence and authorization.
- Duplicate/self relationship rejection.
- Revocation preserves patient and records and removes access immediately.
- Cross-account IDOR tests using UUID and Beeexy ID.
- Transaction rollback if profile/relationship creation fails.
- Mandatory endpoint test matrix for all six endpoints.

## 13. Acceptance Criteria

- Primary and dependent patients share one model.
- My Circle management is distinct from sharing.
- Revocation never deletes health information.
- Authorization and all tests pass.

## 14. Dependencies

- Phases 1-2.
- Product-approved attestation wording for demo use.

## 15. Deferred / TBD Items

- Legal verification, adult consent, minor workflows, invitations, profile claiming, and granular manager permissions.

---

# Phase 4 — Anonymous and Authenticated Pre-Triage

**Priority:** MVP CORE

## 1. Objective

Deliver an AI-assisted conversational Pre-Triage experience for anonymous and authenticated users while retaining deterministic, versioned clinical authority. Natural-language interpretation may make intake easier, but only validated clinical facts, controlled questionnaire branching, and deterministic rules produce urgency and disposition.

## 2. Scope

- Temporary active sessions for anonymous/authenticated flows.
- Provider-independent AI-assisted intent classification and structured interpretation of natural-language symptom input.
- Application-level clinical-AI safety policies and schema/output validation before extracted data can affect workflow state.
- Multiple symptoms and free text with optional terminology normalization metadata.
- Provisional, immutable, versioned clinical-definition packages with explicit source, review, approval, and activation provenance.
- A supported-pathway registry: `ABDOMINAL_PAIN` is the only current detailed pathway; `HEADACHE`, `CHEST_PAIN`, `FEVER`, `RESPIRATORY_SYMPTOMS`, `BACK_PAIN`, and `OTHER_SYMPTOMS` are recognized but unsupported until dedicated packages exist.
- Versioned symptom-dependent questionnaire definitions and deterministic branch resolution.
- Versioned deterministic clinical rules, stable urgency vocabulary, separate disposition definitions, and red-flag precedence.
- Completed episode persistence/result retrieval.
- Optional guarded AI-assisted patient-friendly phrasing after the canonical result exists.
- Secure, idempotent anonymous claim and 24-hour expiry.

## 3. Explicitly Out of Scope

- Resume after abandonment.
- Autonomous AI clinical decision-making, LLM-determined urgency/disposition, authoritative AI diagnoses, numeric disease probabilities, prescription generation, autonomous agents, and AI changes to completed clinical records.
- Python, Google ADK, a separate AI microservice, or vendor-specific domain design without a later demonstrated requirement.
- Invented or unapproved clinical pathways, thresholds, red flags, rules, or emergency wording.
- Detailed execution for any symptom pathway other than `ABDOMINAL_PAIN`.
- Dynamic AI questioning outside the controlled, versioned questionnaire and deterministic branch graph.

## 4. Domain Model

- Entities: `PreTriageSession`, `PreTriageEpisode`, `QuestionnaireDefinitionVersion`, `TriageQuestion`, `TriageAnswer`, `ReportedSymptom`, `ClinicalRuleSetVersion`, `ClinicalAssessment`, `ClinicalFinding`.
- Reuse the Phase 4.1 aggregates and versioning model. Phase 4.2 may add only the metadata needed to distinguish `ClinicalContentSource`, `ClinicalReviewStatus`, and `ClinicalApprovalStatus`, or equivalent concepts, when existing provenance cannot express them. Provider/model fields do not belong in Domain.
- Package/registry concepts: `ClinicalDefinitionPackage`, `ClinicalPathwayCode`, `IClinicalDefinitionProvider`, and `IClinicalPathwayRegistry`, or equivalent boundaries that map a pathway to exact questionnaire, rule-set, disposition, and message versions.
- AI boundary concepts live in Application/Infrastructure rather than controlling Domain: `ClinicalIntent`, `StructuredSymptomExtraction`, validated fact candidates, and temporary extraction provenance. Concrete provider/model configuration remains outside Domain.
- `PreTriageSession` and its in-progress answers are temporary workflow state. They are not part of Clinical History and are not permanent clinical records.
- Lifecycle: Start Pre-Triage -> temporary `PreTriageSession` -> temporary answers -> Complete -> create permanent `PreTriageEpisode` + `ClinicalAssessment` -> project the completed episode into Clinical History.
- Abandonment lifecycle: `PreTriageSession` -> expires/is discarded -> no `PreTriageEpisode`, `ClinicalAssessment`, or Clinical History record is created.
- Session states: `Active -> Completed`; an anonymous completed episode may become `Claimed` or expire unclaimed.
- Value objects: anonymous token hash, question code, symptom text/code, rule/questionnaire version, pathway code, urgency code, disposition code, and clinical-content status.
- Stable urgency codes are `CRITICAL`, `HIGH`, `MEDIUM`, `LOW`, and `VERY_LOW`, ordered `VERY_LOW < LOW < MEDIUM < HIGH < CRITICAL`. Urgency and disposition are separate concepts. A lower-priority rule can never downgrade a higher urgency or red-flag result.
- `CRITICAL` is supported in the vocabulary, but Phase 4 does not invent an exhaustive set of `CRITICAL` rules absent from the provisional abdominal package.
- Invariants: only completed `PreTriageEpisode` records represent permanent clinical assessments and enter history; completion atomically creates the episode/assessment; Clinical History projection consumes only completed episodes and is idempotent; completed records are immutable; red flags have required precedence; result records exact definition versions and provenance; no numeric disease probabilities exist.

### Deterministic clinical authority

```text
User natural-language input
        ↓
AI interpretation / structured extraction
        ↓
Application validation / safety guardrails
        ↓
Versioned questionnaire + deterministic branching
        ↓
Validated clinical facts
        ↓
Versioned deterministic clinical rule engine
        ↓
Urgency + disposition
        ↓
Optional AI-assisted patient-friendly phrasing
```

> The AI layer assists interpretation and conversation; it does not own clinical authority. Urgency and disposition are produced only by the deterministic, versioned clinical rule engine from validated clinical facts.

## 5. Database Changes

- `triage.pre_triage_sessions`, `pre_triage_episodes`, `questionnaire_versions`, `questions`, `answers`, `reported_symptoms`, `clinical_rule_set_versions`, `clinical_assessments`, `clinical_findings`.
- UUID PKs; nullable patient FK before anonymous claim; unique session-to-episode; token hash unique; claim idempotency constraint.
- Index token hash/expiry, patient/completed time, question/rule versions.
- Add only additive persistence needed for package/pathway references, disposition definitions, and explicit clinical-content source/review/approval status. Provisional and later approved versions must coexist; historical episodes retain their exact referenced versions and statuses.
- If temporary AI-extraction provenance is persisted, keep it in temporary workflow/application-owned storage, exclude provider-specific fields from core Domain, apply minimum retention, and never treat raw or unvalidated extraction as a clinical fact.
- Session and in-progress answer rows are temporary workflow storage, including when stored server-side for anonymous execution. Completion materializes the permanent episode/assessment records; abandoned sessions never do.
- Unclaimed anonymous temporary data and completed episodes expire after 24 hours; a completed anonymous episode may be claimed by an authenticated primary patient within that period.
- Abandoned authenticated sessions are expired/discarded by cleanup, cannot be resumed in the MVP, and never create permanent clinical or history records.
- Clinical definitions are immutable versioned import artifacts, never derived from prototype values. The initial abdominal versions preserve semantics equivalent to `REFERENCE_PLATFORM_DERIVED`, `PROVISIONAL`, and `PENDING_FORMAL_REVIEW`; they are never automatically presented as approved.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/pre-triage/sessions` | Optional Bearer | Anonymous or owner/active manager for patient | Start current assessment and resolve a supported pathway | `201`; anonymous token returned once | Unauthorized patient `404`; recognized-but-unsupported pathway `422`; ambiguous input requests clarification |
| `POST /api/v1/pre-triage/sessions/{id}/answers` | Bearer owner/manager or anonymous token header | Matching session capability | Submit explicit or natural-language answer(s), validate facts, and get next question | `200` progress | Invalid extraction/branch/answer `422` or clarification; completed/expired `409`; invalid capability `401` |
| `POST /api/v1/pre-triage/sessions/{id}/complete` | Same | Matching session capability | Execute deterministic rules and persist episode | `201` assessment/result | Incomplete/no eligible versioned rule set `422`; concurrent/repeat completion `409` or idempotent result |
| `GET /api/v1/pre-triage/sessions/{id}/result` | Bearer owner/manager or anonymous token | Matching completed session | Retrieve result | `200` | Incomplete `409`; absent/expired `404`; bad capability `401` |
| `POST /api/v1/pre-triage/sessions/{id}/claim` | Bearer + anonymous token | Primary patient of authenticated account | Attach anonymous episode | `200` claimed episode | Expired/invalid `401/404`; claimed by another patient `409`; repeat by same patient idempotent |

## 7. Application / Use Cases

- `StartPreTriage`, `InterpretClinicalInput`, `ClassifyClinicalIntent`, `ValidateClinicalAiOutput`, `ExtractStructuredSymptoms`, `SubmitTriageAnswers`, `ResolveNextQuestion`, `CompletePreTriage`, `GetPreTriageResult`, `ClaimAnonymousPreTriage`, `ExpireAnonymousPreTriage`, and `ProjectCompletedPreTriageEpisode`.
- Provider-neutral boundaries include `IClinicalAiProvider`, `ISymptomExtractor`, `IClinicalIntentClassifier`, `IClinicalAiOutputValidator`, and `IClinicalSafetyPolicy`, or a smaller equivalent separation preserving the same authority boundaries.
- Prefer schema-constrained structured AI output. Low-confidence, ambiguous, invalid, conflicting, or unsupported extraction produces clarification and cannot silently create facts or advance questionnaire state.
- `IClinicalRuleEngine` consumes validated facts and executes only the exact selected versioned rules. It is the sole urgency/disposition authority.
- `ISymptomNormalizer` supports uncoded free text and future SNOMED service.

## 8. Authentication and Authorization

- Anonymous flow uses a cryptographically random capability returned once and stored hashed.
- IDs without the capability do not grant anonymous access.
- Authenticated patient selection uses owner/active-manager authorization.
- Claim requires both bearer authentication and anonymous capability.
- Every start, answer, completion, result, and claim operation rechecks the relevant capability or patient authorization; AI-derived identifiers never confer access.

## 9. Security and Privacy

- Capability is never in logs and preferably sent in a dedicated header.
- Anonymous workflow data contains only the minimum assessment data required for server-side execution; if unclaimed, temporary data and any completed anonymous episode expire after 24 hours.
- Authenticated abandonment creates no permanent clinical record, and resume after abandonment is not supported in the MVP.
- Clinical History projection accepts only completed `PreTriageEpisode` records, never a `PreTriageSession` or its temporary answers.
- Application-enforced intent/safety outcomes include at least `PRE_TRIAGE_INPUT`, `OUT_OF_SCOPE`, `PRESCRIPTION_REQUEST`, `UNSUPPORTED_CLINICAL_REQUEST`, `POTENTIAL_PROMPT_INJECTION`, and `AMBIGUOUS`. Critical restrictions cannot rely only on an LLM system prompt.
- AI output is schema-, enum-, confidence-, pathway-, conflict-, and safety-validated before use. AI-supplied urgency, disposition, diagnosis, thresholds, red flags, prescriptions, or probabilities are rejected or ignored.
- Prompt injection cannot disable application safety or deterministic red-flag precedence. Optional rendering cannot remove emergency wording or alter the canonical result.
- Provider requests, responses, errors, and logs exclude capability/bearer tokens, unnecessary demographics, raw health payloads, and prompts containing more clinical data than needed. Provider failure does not leak secrets or internals.
- Emergency wording is configuration tied to the exact clinical-definition version and its review/approval status.
- Completed records cannot be overwritten.

## 10. External Integrations

- **IMPLEMENT NOW:** deterministic internal clinical-definition/rule provider and provider-independent clinical AI abstraction, validation, and safety boundary.
- **IMPLEMENT IF CONFIGURED / OPTIONAL:** a concrete AI provider adapter selected through Infrastructure configuration; the plan is not bound to NVIDIA NIM, Ollama, Gemini, OpenAI, or another vendor.
- **INTERFACE/PLACEHOLDER:** `ISnomedTerminologyService`.
- **POST-MVP:** autonomous agents and dynamic AI questioning beyond the controlled questionnaire.

## 11. FHIR Impact

Internal answers/assessment retain stable identifiers and version provenance needed later for `QuestionnaireResponse`, `RiskAssessment`, `Device`, and `Provenance`. No FHIR is generated here.

## 12. Tests

- Anonymous/authenticated successful flows for the provisional abdominal question branches.
- Multiple symptoms, free text, optional coding/provenance, multi-fact extraction, and avoidance of questions already answered by reliably validated facts.
- Structured-output schema/enum/confidence validation; malformed, ambiguous, conflicting, unsupported, diagnosis/urgency/probability-bearing, and adversarial outputs cannot become authoritative facts.
- Intent/safety fixtures for out-of-scope input, prescription requests, unsupported clinical requests, prompt injection, and ambiguous input.
- Provider-unavailable behavior preserves explicit deterministic intake/assessment and falls back to canonical result wording; no unsafe guessed extraction is accepted.
- Supported/unsupported pathway registry tests and refusal to apply abdominal rules to any other category.
- Deterministic rule repeatability, urgency ordering, no-downgrade behavior, and red-flag precedence using only provisional-package fixtures.
- No numeric probability output.
- Temporary sessions/answers never appear in Clinical History before completion; abandoned anonymous and authenticated sessions create no permanent episode or history record.
- Authenticated abandonment cannot resume in the MVP.
- Token entropy/hash/access tests; anonymous completed-episode claim within 24 hours; unclaimed temporary/completed data expiry/deletion at 24 hours.
- Concurrent completion and claim idempotency; cross-account claim conflict.
- Adversarial patient-friendly rendering cannot alter urgency/disposition, remove required emergency text, prescribe, diagnose, or introduce probabilities.
- AI/provider absence has no effect on deterministic clinical authority.
- Mandatory endpoint test matrix for all five endpoints.

## 13. Acceptance Criteria

- Anonymous users complete/view results without an account and may securely claim within 24 hours.
- Authenticated users assess only authorized patients.
- `PreTriageSession` remains temporary workflow state; only successful completion creates a permanent `PreTriageEpisode` + `ClinicalAssessment` and projects it into Clinical History.
- Abandonment creates no Clinical History record; authenticated abandoned flows cannot resume in the MVP; unclaimed anonymous data expires after 24 hours.
- AI never determines or overrides urgency or disposition; deterministic versioned rules operating on validated facts remain authoritative.
- `ABDOMINAL_PAIN` passes the complete vertical slice against the provisional package; recognized-but-unsupported pathways are handled safely without borrowing abdominal rules.
- Definition versions derived from the provisional abdominal package preserve source/review/approval semantics equivalent to `REFERENCE_PLATFORM_DERIVED`, `PROVISIONAL`, and `PENDING_FORMAL_REVIEW`.
- Later reviewed/approved versions can be imported and activated without mutating episodes produced by earlier versions.
- AI provider outage cannot compromise deterministic assessment or canonical result delivery.
- No prototype clinical values become rules.
- Migrations and all tests pass.

## 14. Dependencies

- Phases 1-3 (Phase 3 only for dependent assessments).
- Phase 4.1 persistence foundation.
- `beeexy-phase4-provisional-clinical-definitions.md` for the provisional abdominal questionnaire, branching, red flags, urgency rules, dispositions, recommendations, and fixtures.

## 15. Deferred / TBD Items

- Dedicated versioned packages for `HEADACHE`, `CHEST_PAIN`, `FEVER`, `RESPIRATORY_SYMPTOMS`, `BACK_PAIN`, and `OTHER_SYMPTOMS`.
- Exhaustive `CRITICAL` rules, formal clinical review/approval and subsequent approved/activated versions, production emergency-message localization, SNOMED provider, possible-condition policy, and future dynamic AI questioning beyond controlled questionnaire behavior.

## Implementation Readiness

### Can proceed now for the abdominal vertical slice

Phase 4.2 through Phase 4.13 may proceed, provided every subphase stays within `beeexy-phase4-provisional-clinical-definitions.md` and does not invent missing content.

### Directly dependent on the provisional abdominal package

Phase 4.2, Phase 4.5, Phase 4.6, Phase 4.7, Phase 4.9, and Phase 4.13.

### Must remain unavailable for detailed clinical execution

`HEADACHE`, `CHEST_PAIN`, `FEVER`, `RESPIRATORY_SYMPTOMS`, `BACK_PAIN`, and `OTHER_SYMPTOMS` remain recognized but unsupported until their own versioned clinical packages exist.

## Phase 4.1 — Pre-Triage Domain + Persistence Foundation

**Phase 4.1 status:** COMPLETE (2026-08-21)
**Phase 4.1 implementation:** Added the clinically content-neutral domain and PostgreSQL persistence foundation for anonymous and authenticated Pre-Triage. `PreTriageSession` is an `Active -> Completed` temporary-workflow aggregate with nullable patient ownership, required expiry, hashed anonymous capability metadata, and temporary answers/symptoms. Completion transfers those child rows to an immutable `PreTriageEpisode`, which records exact questionnaire/rule-set versions and owns one immutable `ClinicalAssessment` result plus findings. Anonymous episodes retain nullable patient ownership and unclaimed expiry metadata; the only permanent-record mutation is a one-time claim that is idempotent for the same patient and conflicts for another. Approved questionnaire/rule packages have stable code/version identities, content hashes, source/import/approval/activation provenance, and no seeded content. The `triage` schema contains the nine planned tables with UUID keys, exact-version composite FKs, unique session-to-episode/assessment/capability/code-version constraints, lifecycle/ownership checks, expiry and patient retrieval indexes, temporary-child cascades only from sessions, and `RESTRICT` on patient and permanent clinical relationships. Migration `20260821203135_Phase41PreTriagePersistenceFoundation` adds the complete schema without changing Phase 1-3 data. No clinical questionnaire, answer option, urgency vocabulary, red flag, score, threshold, probability, SNOMED call, FHIR resource, application use case, cleanup worker, Phase 4 HTTP endpoint, or Phase 4.2 behavior was introduced.
**Phase 4.1 verification:** Restore succeeded; the Debug solution build completed with 0 warnings and 0 errors. The full suite passed 501 tests: 276 unit and 225 real-PostgreSQL integration, with 0 failed and 0 skipped. Focused Phase 4.1 coverage passed 15 domain and 10 persistence cases; nine focused migration/FK cases passed, including clean full-chain apply and Phase 4.1 rollback/reapply. PostgreSQL enforces nullable anonymous ownership, unique capability hashes, one episode per session, exact version provenance, safe patient/permanent-record delete behavior, temporary-row cleanup boundaries, and claim preservation. EF reported no pending model changes; formatting verification and `git diff --check` passed. Medical-team-approved questionnaire content, urgency codes, red flags, deterministic rules, thresholds, and messages remain the explicit dependency for Phase 4.2.

Phase 4.1 remains the authoritative, clinically content-neutral technical foundation and is not redesigned for AI. If its current provenance fields cannot represent provisional review state, Phase 4.2 may add narrowly scoped source/review/approval metadata. Temporary AI-extraction metadata may remain outside the core Domain.

## Phase 4.2 — Clinical Definition Packages + Supported Pathway Registry

**Phase 4.2 status:** COMPLETE (2026-08-21)
**Phase 4.2 implementation:** Added an immutable, versioned `ABDOMINAL_PAIN` definition package (`2026.08.21-provisional.1`) derived only from `beeexy-phase4-provisional-clinical-definitions.md`: 41 typed questions, 14 deterministic branch definitions, 13 red flags, 10 explicit urgency-rule artifacts, the ordered five-level urgency vocabulary, five separate disposition/recommendation definitions, and source limitations. `ABDOMINAL_PAIN` is supported; `HEADACHE`, `CHEST_PAIN`, `FEVER`, `RESPIRATORY_SYMPTOMS`, `BACK_PAIN`, and `OTHER_SYMPTOMS` are recognized but unsupported, with unknown pathways remaining distinct. Package validation rejects broken question/rule references, invalid branch values, incompatible provenance, incorrect urgency ordering, and cross-pathway import. Canonical JSON hashes, deterministic identifiers, immutable same-version semantics, atomic/idempotent import, active/exact-version retrieval, and future-version coexistence are implemented. Both definition versions retain `REFERENCE_PLATFORM_DERIVED`, `PROVISIONAL`, and `PENDING_FORMAL_REVIEW`; provisional content has no approval timestamp and is never promoted in place. Migration `20260822035009_Phase42ClinicalDefinitionPackages` adds the narrowly required pathway, provenance/status, nullable approval, rule-package JSON, indexes, and database checks. No session execution, branch execution, rule evaluation, AI, endpoint, detailed non-abdominal package, probability, diagnosis, prescription, or inferred `CRITICAL` trigger was introduced.
**Phase 4.2 verification:** Restore succeeded; the final Debug solution build completed with 0 warnings and 0 errors. The full suite passed 518 tests: 290 unit and 228 real-PostgreSQL integration, with 0 failed and 0 skipped. Four focused persistence/migration cases passed, including clean full-chain application and Phase 4.2 rollback/reapply; the 14 focused package/registry unit cases also pass. PostgreSQL retains content status and immutable versions, the importer rejects same-version hash changes, and the provider verifies stored hashes before returning definitions. EF reported no pending model changes; formatting verification and `git diff --check` passed. The source package intentionally defines the `CRITICAL` vocabulary/disposition but no complete `CRITICAL` trigger set, so exhaustive critical rules and formal clinical approval remain deferred to a new reviewed version.

**Objective:** Create versioned clinical-definition infrastructure and materialize the provisional `ABDOMINAL_PAIN` package.

**Exact scope:** Import an immutable package containing the core/minimum questionnaire, abdominal branch definitions, exact rule-set reference, urgency vocabulary, separate disposition/recommendation definitions, message references, clinical-content provenance/status, and acceptance fixtures. Register `ABDOMINAL_PAIN` as supported and the six other known categories as recognized-but-unsupported. Preserve `REFERENCE_PLATFORM_DERIVED`, `PROVISIONAL`, and `PENDING_FORMAL_REVIEW`; a later reviewed version is a new version, never a mutation.

**Main components:** Application interfaces `IClinicalDefinitionProvider` and `IClinicalPathwayRegistry`; Domain/package concepts `ClinicalDefinitionPackage`, `ClinicalPathwayCode`, `ClinicalContentSource`, `ClinicalReviewStatus`, and `ClinicalApprovalStatus`, or equivalents; Infrastructure importer/validator and deterministic in-process provider. Reuse Phase 4.1 version entities and add only genuinely necessary provenance/status fields and package references.

**Endpoints involved:** None.

**AI involvement:** None.

**Clinical-definition dependencies:** `beeexy-phase4-provisional-clinical-definitions.md`; no prototype content or cross-pathway inference.

**Security/safety requirements:** Never auto-promote provisional content to approved, never activate a package under the wrong pathway, validate hashes/references/statuses during import, and never reuse abdominal rules for another pathway.

**Tests and acceptance criteria:** Verify exact abdominal import, stable urgency ordering, package integrity, provenance/status retention, immutable versions, coexistence of future versions, deterministic active-version lookup, supported versus recognized-but-unsupported behavior, and rejection of missing/cross-pathway references. No detailed non-abdominal package is created.

**Explicitly out of scope:** Session execution, AI, branch execution, urgency evaluation, endpoints, and detailed definitions for other symptoms.

**Dependencies on previous subphases:** Phase 4.1.

## Phase 4.3 — Clinical AI Boundary + Safety Guardrails Foundation

**Objective:** Establish provider-independent clinical-AI boundaries and application-enforced safety before any AI output can influence Pre-Triage.

**Exact scope:** Define schema-constrained intent classification and structured symptom/fact extraction contracts, output validation, safety-policy evaluation, provider timeout/unavailability behavior, and a deterministic test stub. Required intents are `PRE_TRIAGE_INPUT`, `OUT_OF_SCOPE`, `PRESCRIPTION_REQUEST`, `UNSUPPORTED_CLINICAL_REQUEST`, `POTENTIAL_PROMPT_INJECTION`, and `AMBIGUOUS`.

**Main components:** Application boundaries `IClinicalAiProvider`, `ISymptomExtractor`, `IClinicalIntentClassifier`, `IClinicalAiOutputValidator`, and `IClinicalSafetyPolicy`, or a smaller equivalent architecture; contracts such as `ClinicalIntent`, `StructuredSymptomExtraction`, and `ClinicalExtractionResult`; Infrastructure provider configuration/adapters and a test provider. Domain remains vendor-neutral.

**Endpoints involved:** None.

**AI involvement:** Yes, foundation only; it interprets input and proposes structured values but has no clinical authority.

**Clinical-definition dependencies:** Minimal schema/vocabulary references only; no urgency or pathway rule execution.

**Security/safety requirements:** Enforce prescription and out-of-scope restrictions in application code; treat prompt-like content as untrusted data; reject malformed schemas, unknown enums, unsupported concepts, low-confidence facts, and any AI-authored urgency, disposition, diagnosis, red flag, threshold, prescription, or numeric probability. Provider failure must fail safely without exposing prompts, health data, tokens, configuration, or internals.

**Tests and acceptance criteria:** Accept a valid structured extraction; reject malformed output and invalid enums; convert low confidence/ambiguity to clarification; block prescription/out-of-scope/unsupported requests; prove prompt injection cannot disable restrictions; safely handle timeout/unavailability; prove AI urgency/disposition/probability fields cannot reach authoritative state.

**Explicitly out of scope:** A mandatory production provider, autonomous agents, a separate AI service, conversation persistence, questionnaire execution, and clinical-rule execution.

**Dependencies on previous subphases:** Phase 4.1.

## Phase 4.4 — Start Pre-Triage Session + Anonymous Capability

**Objective:** Securely start anonymous, authenticated-primary, or authenticated-managed-patient Pre-Triage sessions against a supported pathway.

**Exact scope:** Implement `StartPreTriage`; resolve an explicit pathway or optionally classify initial natural-language input; select the exact active questionnaire package; create an expiring Phase 4.1 session; generate a cryptographically random anonymous capability returned once and persisted only as a hash. Reject recognized-but-unsupported pathways without creating a clinically executable session.

**Main components:** Start command/validator/handler, session repository/unit of work, `IClinicalPathwayRegistry`, definition provider, capability generator/hasher/verifier, current-account/patient authorization, and optional Phase 4.3 classifier.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions`.

**AI involvement:** Optional initial intent/symptom classification. Explicit supported-pathway starts do not require AI; if classification is required and unavailable or ambiguous, return a safe retry/clarification response rather than guessing.

**Clinical-definition dependencies:** Phase 4.2 supported registry and active provisional abdominal questionnaire version.

**Security/safety requirements:** UUID alone grants no access; capability entropy and constant-time hash verification meet the repository security standard; tokens never enter logs; authenticated selection requires owner/active-manager authorization; unsupported input cannot be silently mapped to abdominal pain.

**Tests and acceptance criteria:** Cover anonymous, primary-patient, and managed-patient starts; one-time capability return/hash persistence/entropy; IDOR and inactive-manager denial; exact definition selection; recognized-but-unsupported rejection; ambiguous input; and provider outage with safe behavior. Apply the mandatory endpoint matrix.

**Explicitly out of scope:** Answer submission, branch execution, urgency, completion, claim, and resume.

**Dependencies on previous subphases:** Phase 4.2 and Phase 4.3; Phase 3 only for managed-patient authorization.

## Phase 4.5 — Guarded Conversational Intake + Structured Fact Extraction

**Objective:** Convert natural-language input into validated clinical fact candidates without granting AI clinical authority.

**Exact scope:** Implement `InterpretClinicalInput`, `ClassifyClinicalIntent`, `ExtractStructuredSymptoms`, and `ValidateExtractedFacts`. Support only package-known facts such as symptom, location, duration, intensity, character, onset, progression, and associated symptoms. Accept multiple fact candidates from one utterance and mark reliable answers so Phase 4.6 can avoid unnecessary repetition. Invalid, unsupported, low-confidence, ambiguous, or conflicting candidates produce clarification rather than state changes.

**Main components:** Phase 4.3 interfaces, schema-constrained request/response DTOs, fact vocabulary mapper, confidence/conflict validator, clarification result, safety-policy orchestrator, and optional temporary provider-independent extraction provenance.

**Endpoints involved:** No separate endpoint; this capability is consumed by the answer flow.

**AI involvement:** Yes, for interpretation, intent classification, and extraction only.

**Clinical-definition dependencies:** Core fact/question schemas and controlled values from the provisional abdominal package.

**Security/safety requirements:** Do not map unsupported concepts by invention; do not persist rejected candidates; never accept AI authority for urgency, disposition, diagnosis, prescription, thresholds, red flags, or probabilities; safely handle prescription, out-of-scope, unsupported, injection-like, and ambiguous input; minimize provider payloads/logs.

**Tests and acceptance criteria:** Cover multi-fact extraction, normalized duration/intensity/location values, already-supplied fact detection, ambiguity/conflict clarification, malformed/unknown output, low confidence, prompt injection, prescription/out-of-scope requests, unsupported concepts, and provider outage. Prove no rejected or authoritative AI field becomes workflow state.

**Explicitly out of scope:** Persisting answers, selecting questionnaire branches, urgency/disposition evaluation, diagnosis, and free-form autonomous follow-up questions.

**Dependencies on previous subphases:** Phase 4.2, Phase 4.3, and Phase 4.4.

## Phase 4.6 — Questionnaire Execution + Deterministic Branch Resolution

**Objective:** Persist temporary validated answers and advance the provisional abdominal questionnaire through deterministic branches.

**Exact scope:** Implement `SubmitTriageAnswers` and `ResolveNextQuestion`. Accept explicit structured answers and Phase 4.5 validated fact candidates, validate them against the exact session questionnaire version, store them as temporary `TriageAnswer`/symptom state, and deterministically select the next required abdominal question. Skip reliably answered questions while retaining red-flag-question priority. Create no permanent episode yet.

**Main components:** Answer command/validator/handler, session repository/unit of work, answer-schema validator, validated-fact-to-answer mapper, deterministic branch resolver, next-question response mapper, and concurrency control.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions/{id}/answers`.

**AI involvement:** May interpret natural language through Phase 4.5; it never defines, edits, or selects branches outside deterministic package logic. Explicit answers remain usable without an AI provider.

**Clinical-definition dependencies:** The exact provisional abdominal questionnaire, answer schemas, and branch graph selected in Phase 4.4.

**Security/safety requirements:** Reauthorize bearer/capability on every request; reject cross-version questions, invalid options, impossible branches, duplicate/conflicting facts, and completed/expired sessions; preserve temporary-only state; prevent stale/concurrent submissions from corrupting sequence or skipping required red-flag questions.

**Tests and acceptance criteria:** Cover every supplied abdominal branch fixture, explicit and multi-fact natural-language answers, skip-already-answered behavior, red-flag-question priority, invalid/cross-version answers, ambiguity clarification, stale/concurrent writes, completed/expired sessions, anonymous/authenticated access, IDOR, and provider outage. Apply the mandatory endpoint matrix.

**Explicitly out of scope:** Urgency/disposition determination, permanent completion, non-abdominal branch graphs, and history projection.

**Dependencies on previous subphases:** Phase 4.4 and Phase 4.5, using Phase 4.2 definitions.

## Phase 4.7 — Deterministic Clinical Rule Engine + Abdominal-Pain Rules

**Objective:** Implement the sole authoritative deterministic engine for urgency and disposition.

**Exact scope:** Evaluate validated facts against only the explicit provisional abdominal red flags and rules; return matched rule/red-flag references, the highest applicable urgency, and a separately resolved disposition/message reference. Enforce `VERY_LOW < LOW < MEDIUM < HIGH < CRITICAL`, red-flag precedence, and no downgrade. Support the `CRITICAL` vocabulary without inventing absent `CRITICAL` rules. Emit no numeric disease probability.

**Main components:** `IClinicalRuleEngine`, deterministic evaluator, `RuleMatch`, `RedFlagMatch`, `UrgencySeverity`, `DispositionResolver`, package rule compiler/validator, and stable canonical result contracts, or equivalents.

**Endpoints involved:** None.

**AI involvement:** None as clinical authority; provider presence or absence cannot affect identical validated facts and rule versions.

**Clinical-definition dependencies:** Only the provisional abdominal red flags, urgency rules, dispositions, recommendations, and fixtures from Phase 4.2.

**Security/safety requirements:** No AI-generated urgency, hidden fallback score, prototype-derived value, cross-pathway evaluation, lower-priority downgrade, invented threshold/red flag, diagnosis, or probability. Refuse unsupported pathways and incomplete/invalid fact sets.

**Tests and acceptance criteria:** Prove deterministic repeatability; cover every explicit abdominal rule/red-flag fixture; test urgency ordering, overlapping matches, red-flag precedence, and no downgrade; prove unsupported pathways are refused, no probability field exists, no missing `CRITICAL` rule was added, and AI absence has no effect.

**Explicitly out of scope:** Other symptom rule sets, invented/exhaustive `CRITICAL` rules, probabilistic scoring, diagnosis, and endpoints.

**Dependencies on previous subphases:** Phase 4.2 and Phase 4.6.

## Phase 4.8 — Atomic Completion + Immutable Clinical Episode

**Objective:** Atomically convert a complete temporary workflow into immutable permanent clinical records.

**Exact scope:** Implement `CompletePreTriage`; validate session completeness and state; use only validated stored facts; execute the exact deterministic rule-set version; create `PreTriageEpisode`, `ClinicalAssessment`, and `ClinicalFinding` records; transfer temporary child rows; freeze questionnaire/rule versions plus source/review/approval provenance; and commit once. Only successful completion creates permanent/history-eligible state.

**Main components:** Completion command/validator/handler, completeness checker, Phase 4.7 engine, session/episode/assessment repositories, transaction/unit of work, concurrency/idempotency policy, and canonical result mapper.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions/{id}/complete`.

**AI involvement:** None in urgency/disposition. Completion consumes validated workflow state, never raw conversation or unvalidated model output.

**Clinical-definition dependencies:** Exact questionnaire, rule-set, disposition, message, and provisional provenance versions selected for the abdominal session.

**Security/safety requirements:** Reauthorize every request; reject incomplete, expired, unsupported, or version-inconsistent sessions; guarantee rollback on any failure; prevent partial/duplicate episodes and assessments; make concurrent completion safe; preserve immutable clinical records; include no probability fields.

**Tests and acceptance criteria:** Cover successful anonymous/authenticated completion, completeness validation, deterministic result, exact version/status provenance, transaction rollback, concurrent calls, repeat behavior according to the documented idempotency contract, one session-to-episode/assessment constraint, immutability, no raw conversation authority, no temporary answer leakage into history, and the mandatory endpoint matrix.

**Explicitly out of scope:** Result retrieval, AI phrasing, claim, cleanup, history projection, amendments, and FHIR generation.

**Dependencies on previous subphases:** Phase 4.6 and Phase 4.7.

## Phase 4.9 — Secure Result Retrieval + Guarded Patient-Friendly Rendering

**Objective:** Securely return a canonical completed result and optionally improve its phrasing without changing clinical meaning.

**Exact scope:** Implement `GetPreTriageResult`. The canonical response includes urgency, separate disposition, approved findings/red flags, recommendation/message, exact questionnaire/rule versions, and clinical-content source/review/approval provenance. An optional renderer may propose patient-friendly wording only after the canonical deterministic result exists; validate it against immutable result fields and required wording, and fall back to canonical messaging on rejection or provider failure.

**Main components:** Result query/handler/repository, authorization service, canonical DTO mapper, optional patient-friendly renderer through Phase 4.3, semantic/field-preservation validator, and deterministic fallback renderer.

**Endpoints involved:** `GET /api/v1/pre-triage/sessions/{id}/result`.

**AI involvement:** Optional phrasing only. It cannot change urgency/disposition, remove emergency wording, add a diagnosis/prescription/probability, or write back to the completed record.

**Clinical-definition dependencies:** Provisional abdominal dispositions, recommendations, canonical messages, red-flag/finding labels, and provenance.

**Security/safety requirements:** Require matching anonymous capability or authorized patient owner/active manager; return results only for completed, unexpired resources; conceal inaccessible resources appropriately; minimize provider payload/logging; reject adversarial rendering and always retain a deterministic canonical fallback.

**Tests and acceptance criteria:** Cover anonymous/authenticated/managed access, bad capability, IDOR, incomplete/expired/absent resources, exact canonical result/provenance, provider timeout/unavailability, and adversarial renderer attempts to alter urgency/disposition, weaken emergency text, prescribe, diagnose, or add probability. Apply the mandatory endpoint matrix.

**Explicitly out of scope:** Claim, history projection, result mutation/amendment, new clinical findings, and FHIR generation.

**Dependencies on previous subphases:** Phase 4.8; Phase 4.3 only when optional AI rendering is configured.

## Phase 4.10 — Anonymous Episode Claim

**Objective:** Allow an authenticated primary patient to securely claim a completed anonymous episode within its retention window.

**Exact scope:** Implement `ClaimAnonymousPreTriage`; require both bearer authentication and the original anonymous capability; attach the episode to the current account's primary `PatientProfile`; preserve every clinical record and definition reference unchanged; make a same-patient repeat idempotent and a different-patient claim conflict.

**Main components:** Claim command/validator/handler, current primary-patient resolver, capability verifier, session/episode repositories, transaction/concurrency handling, and audit event without sensitive payloads.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions/{id}/claim`.

**AI involvement:** None.

**Clinical-definition dependencies:** None beyond preserving the completed episode's frozen references.

**Security/safety requirements:** Bearer alone and capability alone are each insufficient; never log capability material; prevent cross-account/cross-patient claim; enforce expiration; preserve immutable clinical content; do not allow managed-patient claim without later explicit approval.

**Tests and acceptance criteria:** Cover claim before the exact 24-hour boundary, same-patient idempotent repeat, different-patient conflict, bearer/capability absence or mismatch, expired/absent resource, cross-account attempt, concurrent claim, unchanged clinical content/provenance, and the mandatory endpoint matrix.

**Explicitly out of scope:** Managed-patient claim, episode edits, post-expiry recovery, and history UI.

**Dependencies on previous subphases:** Phase 4.8 and Phase 4.9.

## Phase 4.11 — Expiry + Abandonment Cleanup

**Objective:** Enforce temporary workflow retention and the 24-hour anonymous lifecycle without creating permanent records for abandonment.

**Exact scope:** Implement `ExpireAnonymousPreTriage` and `PreTriageCleanupService`, or equivalents. At the defined 24-hour boundary, discard active anonymous temporary workflow data; expire/remove completed unclaimed anonymous episodes according to the finalized lifecycle; discard abandoned authenticated temporary workflows; retain completed authenticated and successfully claimed records. Resume remains unsupported.

**Main components:** Cleanup application service, scheduled/background worker, clock abstraction, candidate queries, batched idempotent deletion/expiration, repository/unit of work, and privacy-safe operational metrics.

**Endpoints involved:** None.

**AI involvement:** None.

**Clinical-definition dependencies:** None; cleanup must not mutate shared definition versions.

**Security/safety requirements:** Use minimum retention, never expose capability hashes, delete only exact eligible temporary/unclaimed targets, preserve claimed and completed authenticated records, prevent abandoned sessions from producing episode/assessment/history state, and make repeated/concurrent cleanup safe.

**Tests and acceptance criteria:** Test immediately before/at/after 24 hours, anonymous active cleanup, completed-unclaimed cleanup, claimed preservation, authenticated abandonment, completed authenticated preservation, temporary answer/symptom removal, idempotent batches, and concurrency with completion/claim.

**Explicitly out of scope:** Resume, archival recovery, deleting claimed/permanent records, and clinical-history rendering.

**Dependencies on previous subphases:** Phase 4.8 and Phase 4.10.

## Phase 4.12 — Clinical History Projection Boundary

**Objective:** Ensure only a completed permanent `PreTriageEpisode` is eligible to enter Clinical History.

**Exact scope:** Add the minimal idempotent event/outbox/projection boundary, such as `ProjectCompletedPreTriageEpisode`, required for Phase 5. Enforce: `PreTriageSession` never projects directly; temporary `TriageAnswer` never projects directly; completed `PreTriageEpisode` is eligible for exactly one projection. Carry stable identifiers and exact definition/provenance references without generating FHIR.

**Main components:** Completion integration event/outbox record or equivalent durable boundary, projector interface/handler, idempotency key/constraint, and minimal Phase 5-facing contract.

**Endpoints involved:** None.

**AI involvement:** None.

**Clinical-definition dependencies:** Frozen references on the completed episode only; no active-version lookup during projection.

**Security/safety requirements:** Never project incomplete, abandoned, expired, or temporary session state; prevent duplicate projection; preserve patient ownership and immutable source references; exclude raw conversation, capabilities, and provider metadata.

**Tests and acceptance criteria:** Prove no projection before completion, exactly one after completion, repeated/concurrent delivery is idempotent, abandoned/expired sessions produce none, and projection uses the frozen episode rather than current definitions.

**Explicitly out of scope:** Full Clinical History endpoints/UI, amendments, FHIR resource generation, and AI Conversation History.

**Dependencies on previous subphases:** Phase 4.8 and Phase 4.11.

## Phase 4.13 — AI + Clinical Security Hardening and Acceptance Closure

**Objective:** Close Phase 4 with end-to-end clinical-safety, security, concurrency, lifecycle, and abdominal-slice acceptance evidence.

**Exact scope:** Verify all five endpoints and every Phase 4 invariant across anonymous, primary-patient, and authorized managed-patient flows. Audit capability entropy/hash use, IDOR defenses, 24-hour lifecycle, temporary/permanent separation, completion/claim/cleanup races, immutability, exact definition provenance, unsupported pathways, deterministic authority, AI safety/failure behavior, history projection, and privacy-safe logging.

**Main components:** End-to-end acceptance fixtures/harness, provisional abdominal conformance fixtures, adversarial AI provider/renderer stub, concurrency and clock-controlled lifecycle tests, authorization/security tests, database constraint verification, and release-readiness checklist.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions`, `POST /api/v1/pre-triage/sessions/{id}/answers`, `POST /api/v1/pre-triage/sessions/{id}/complete`, `GET /api/v1/pre-triage/sessions/{id}/result`, and `POST /api/v1/pre-triage/sessions/{id}/claim`.

**AI involvement:** Yes for adversarial and availability testing, never as clinical authority.

**Clinical-definition dependencies:** The provisional `ABDOMINAL_PAIN` package and all supplied conformance fixtures. Clinical acceptance is limited to this one detailed pathway.

**Security/safety requirements:** Mandatory fixtures include football question -> `OUT_OF_SCOPE`; medication request -> `PRESCRIPTION_REQUEST`; prompt injection -> restrictions remain enforced; AI-generated urgency -> ignored/rejected; AI-generated numeric probability -> rejected; invalid structured extraction -> clarification; provider unavailable -> safe deterministic fallback. Verify no secrets/raw clinical prompts leak to logs and no abdominal rule executes for another pathway.

**Tests and acceptance criteria:** Pass the mandatory endpoint matrix plus end-to-end anonymous/authenticated/managed flows, IDOR, capability, claim, exact 24-hour expiry, abandonment, atomic/concurrent/idempotent completion and claim, cleanup races, immutable records, exact provisional provenance, no numeric probabilities, rule repeatability/red-flag precedence/no downgrade, provider outage, adversarial extraction/rendering, unsupported pathways, and one idempotent history projection. Build, migrations, and the full suite pass.

**Explicitly out of scope:** Additional symptom packages, exhaustive invented `CRITICAL` rules, Phase 5 feature implementation, FHIR generation, production provider mandate, and autonomous agents.

**Dependencies on previous subphases:** Phase 4.2 through Phase 4.12.

---

# Phase 5 — Clinical History and Amendments

**Priority:** MVP CORE

## 1. Objective

Expose an unlimited patient-owned clinical timeline while preserving immutable originals and traceable corrections.

## 2. Scope

- Project completed Pre-Triage episodes into history.
- Cursor pagination and event detail.
- Traceable Pre-Triage amendments.

## 3. Explicitly Out of Scope

- AI Conversation History, arbitrary clinical deletion, ten-item limits, and unapproved new clinical event types.

## 4. Domain Model

- Entities: `ClinicalHistoryEvent`, `ClinicalAmendment`.
- Relationships: event references patient and authoritative source record; amendment references event/source and author.
- Value objects: event type, source reference, amendment reason, provenance.
- Invariants: history is an index, not duplicated arbitrary JSON; original remains immutable; display may compose amendments without losing provenance.

## 5. Database Changes

- `history.clinical_history_events` and `history.clinical_amendments`.
- UUID PKs; patient/source/author FKs; occurred/recorded timestamps; source version.
- Unique source-event projection constraint.
- Index patient + occurred time + ID for cursor pagination; patient + type.
- No artificial count limit and no cascading source deletion.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `GET /api/v1/patients/{patientId}/clinical-history` | Bearer | Owner/active manager | Cursor-paginated history | `200` page | Bad cursor/filter `422`; unauthorized concealed `404` |
| `GET /api/v1/patients/{patientId}/clinical-history/{eventId}` | Bearer | Owner/active manager | Event, source, provenance, amendments | `200` | `404` absent/wrong patient/unauthorized |
| `POST /api/v1/pre-triage/episodes/{episodeId}/amendments` | Bearer | Owner/active manager | Add correction without overwrite | `201` | Invalid correction `422`; duplicate idempotency `409`; `404` |

## 7. Application / Use Cases

- `ProjectCompletedEpisode`, `ListClinicalHistory`, `GetClinicalHistoryEvent`, `AmendPreTriageEpisode`.
- Transactional/idempotent history projection.

## 8. Authentication and Authorization

Bearer authentication and patient ownership/active management on every query/command. Source IDs never bypass patient authorization.

## 9. Security and Privacy

- Concealed `404` prevents enumeration.
- Amendment author/time/reason are immutable audit data.
- Long-term deletion policy remains undefined; no destructive delete endpoint.

## 10. External Integrations

None.

## 11. FHIR Impact

Original and amendment provenance is preserved for later mapping according to Andrea's materials; no mapping is invented.

## 12. Tests

- Completion creates exactly one history event.
- Stable unlimited cursor pagination and filter validation.
- Amendment leaves source unchanged and renders traceably.
- Concurrent/idempotent projection and amendment requests.
- Cross-patient IDOR and relationship-revocation tests.
- Mandatory endpoint test matrix for all three endpoints.

## 13. Acceptance Criteria

- Completed Pre-Triage appears in authorized history.
- No ten-record limit exists.
- Corrections are traceable and originals immutable.
- All tests pass.

## 14. Dependencies

- Phase 4 and Phase 3 for managed-patient access.

## 15. Deferred / TBD Items

- Long-term retention/deletion rights, additional clinical event types, and exact FHIR amendment representation.

---

# Phase 6 — FHIR Generation, Validation, and Export

**Priority:** MVP CORE

## 1. Objective

Map Beeexy's internal records into validated immutable FHIR export snapshots without making FHIR the internal domain/database model.

## 2. Scope

- Generate, validate, store metadata/checksum, and download FHIR JSON.
- Required conceptual resources: `QuestionnaireResponse`, `RiskAssessment`, `Device`, `Provenance`.
- Use the Markdown files under `Backend/docs/fhir/` as the mandatory source of truth for exact mappings and requirements; do not replace or extend Andrea's mappings by inference.

## 3. Explicitly Out of Scope

- External FHIR-server transmission, invented profiles/extensions/codes, and forcing domain entities to mirror FHIR.

## 4. Domain Model

- Internal entities: `FhirExport`, `FhirValidationResult`; resource objects remain interoperability DTOs.
- Statuses: pending/generated/validation-failed/validated.
- Invariants: export is immutable; exact mapping/profile/FHIR version recorded; invalid artifact cannot be represented as validated.

## 5. Database Changes

- `interoperability.fhir_exports`, `fhir_validation_results`.
- UUID PK; patient/source FKs; FHIR/mapping/profile versions; status; checksum; private artifact URI; timestamps.
- Unique idempotency key per patient/request; indexes patient/time/status.
- Migration does not create FHIR-shaped clinical tables.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/patients/{patientId}/fhir-exports` | Bearer | Owner/active manager | Generate and validate snapshot | `201` metadata | Missing mapping/input or validation failure `422`; duplicate `409`; unauthorized `404` |
| `GET /api/v1/fhir-exports/{id}` | Bearer | Authorized source patient | Export/validation status | `200` | `404` |
| `GET /api/v1/fhir-exports/{id}/content` | Bearer | Authorized source patient | Download validated FHIR JSON | `200 application/fhir+json` | `404`; not validated `409` |

## 7. Application / Use Cases

- `GenerateFhirExport`, `ValidateFhirExport`, `GetFhirExport`, `DownloadFhirExport`.
- `IFhirMapper` implementations live in interoperability infrastructure and use the Markdown source artifacts in `Backend/docs/fhir/`.

## 8. Authentication and Authorization

All endpoints require bearer authentication plus source-patient authorization. Export IDs and Beeexy IDs grant no authority.

## 9. Security and Privacy

- FHIR artifacts are private health data; no public storage URLs.
- Export creation/download is audited.
- Validation diagnostics returned to patients are sanitized.

## 10. External Integrations

- **IMPLEMENT NOW:** FHIR SDK/validator compatible with the release and profiles specified by Andrea's Markdown materials; any release/profile detail those files do not specify remains TBD rather than inferred.
- **POST-MVP:** external FHIR servers.

## 11. FHIR Impact

- `PreTriageEpisode` answers -> `QuestionnaireResponse`.
- `ClinicalAssessment` -> `RiskAssessment`.
- Beeexy processing/software identity -> `Device`.
- Generation/source traceability -> `Provenance`.
- `Backend/docs/fhir/beeexy-coleccion-recursos.md`, `beeexy-provenance-device-ejemplo.md`, and `beeexy-riskassessment-ejemplo.md` are the only source for Andrea's exact mappings, identifiers, terminology, cardinalities, examples, and stated requirements.

## 12. Tests

- Golden-file mapping tests derived directly from the examples in `Backend/docs/fhir/`.
- Resource-reference and identifier integrity tests.
- Validator success and intentional failure tests.
- No arbitrary probabilities or prototype coding.
- Snapshot immutability/checksum/idempotency.
- Cross-patient authorization and private download tests.
- Mandatory endpoint test matrix for all three endpoints.

## 13. Acceptance Criteria

- All required resources conform to the Markdown materials in `Backend/docs/fhir/` and validate.
- Invalid FHIR is not marked/exported as validated.
- Internal domain has no FHIR dependency.
- All tests pass.

## 14. Dependencies

- Phases 4-5.
- Andrea's available mapping/example sources: `Backend/docs/fhir/beeexy-coleccion-recursos.md`, `Backend/docs/fhir/beeexy-provenance-device-ejemplo.md`, and `Backend/docs/fhir/beeexy-riskassessment-ejemplo.md`.

## 15. Deferred / TBD Items

- Exact FHIR release, canonical profile URLs/versions, validator configuration, validated fixture status, and any requirement not specified in `Backend/docs/fhir/` remain TBD and must not be invented.
- External FHIR servers, additional resources beyond Andrea's stated mappings, amendment representation, and long-term export retention.

---

# Phase 7 — Clinic, Doctor Directory, and Deterministic Matching

**Priority:** MVP CORE

## 1. Objective

Provide a public internal doctor directory with first-class clinics and an explainable deterministic matching algorithm.

## 2. Scope

- Published clinics/locations/doctors/affiliations.
- Credential verification state and verified public claims.
- Specialty, language, location, and stored insurance filters.
- Versioned deterministic matching with factor explanations.

## 3. Explicitly Out of Scope

- Doctor/clinic onboarding portals, reviews/ratings, real-time eligibility, inferred credentials, AI scoring, and full tenant/branding configuration.

## 4. Domain Model

- Entities: `Clinic`, `ClinicLocation`, `Doctor`, `DoctorAffiliation`, `DoctorCredential`, `Specialty`, `Language`, `InsurancePlan`, `DoctorInsuranceParticipation`, `DoctorMatchRuleVersion`.
- Credential status: `Submitted`, `PendingVerification`, `Verified`, `Rejected`.
- Invariants: only published records/verified claims are public; match factors/version are explainable/auditable; stored insurance data is not represented as real-time verification.

## 5. Database Changes

- Normalized `directory` tables with UUID PKs and clinic/doctor/credential/specialty/insurance FKs.
- Unique clinic/doctor identifiers and appropriate affiliation constraints.
- Search indexes for publication, specialty, language, location, and insurance.
- Clinic location stores required IANA timezone.
- Match rules/version stored separately from doctor rows.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `GET /api/v1/clinics` | None | Public published data | List/filter clinics | `200` page | Invalid cursor/filter `422` |
| `GET /api/v1/clinics/{id}` | None | Public published data | Clinic profile/locations | `200` | Unpublished/absent `404` |
| `GET /api/v1/doctors` | None | Public published data | Search/filter/rank doctors | `200` page + match explanation | Unsupported filter `422` |
| `GET /api/v1/doctors/{id}` | None | Public published data | Verified doctor profile | `200` | Unpublished/absent `404` |

## 7. Application / Use Cases

- `ListClinics`, `GetClinic`, `SearchDoctors`, `GetDoctor`, `CalculateDoctorMatch`.
- Import/seed approved demo directory through deployment tooling, not patient APIs.

## 8. Authentication and Authorization

Anonymous read is allowed only for approved public fields. No patient-specific result is exposed unless future matching inputs require authenticated context.

## 9. Security and Privacy

- Submitted/rejected credential evidence is never returned publicly.
- No fabricated ratings or credentials.
- Match audit records contain factors/version, not unnecessary health details.

## 10. External Integrations

- **IMPLEMENT NOW:** none.
- **INTERFACE/PLACEHOLDER:** future directory import/geocoding.
- **POST-MVP:** onboarding, real-time insurance, reviews.

## 11. FHIR Impact

None for MVP; no Practitioner/Organization mapping is invented.

## 12. Tests

- Publication and credential-state visibility.
- Deterministic score repeatability, factor weights, tie ordering, and explanations.
- Specialty/language/location/insurance filters.
- Explicit absence of reviews/ratings and real-time eligibility claims.
- Pagination/index-backed query tests.
- Mandatory endpoint test matrix for all four endpoints.

## 13. Acceptance Criteria

- Anonymous users find/view only published verified data.
- Clinic is first-class.
- Matching is deterministic, explainable, versioned, and contains no LLM decision.
- All tests pass.

## 14. Dependencies

- Phase 1.
- Authoritative doctor/clinic demo data and approved matching factors/weights.

## 15. Deferred / TBD Items

- Matching weights, distance/geocoding source, onboarding/verification workflows, credential-document retention, real-time network verification, reviews, and white-label configuration.

---

# Phase 8 — Availability and Appointment Requests

**Priority:** MVP CORE

## 1. Objective

Allow authenticated patients to request Beeexy-managed appointment slots and minimally authorized clinic schedulers to confirm or reject requests, while making double booking database-impossible and retaining complete status history.

## 2. Scope

- Stored availability slots.
- Patient booking as `REQUESTED`.
- Minimal clinic-side backend confirmation/rejection for the MVP/demo, without a clinic portal or full clinic onboarding.
- Appointment listing/detail, cancellation, and rescheduling.
- Official status model and immutable transition history.
- Clinic timezone handling and HTTP 409 conflict behavior.

## 3. Explicitly Out of Scope

- Payments/copays, automatic Pre-Triage sharing, Google Meet implementation, clinic portal, clinic onboarding, intake-form replacement claims, full future Doctor/Clinic roles and permissions, and clinic transitions beyond the minimum confirm/reject mechanism.

## 4. Domain Model

- Entities: `AvailabilitySlot`, `Appointment`, `AppointmentStatusHistory`.
- Statuses: `Requested`, `Confirmed`, `Cancelled`, `Completed`, `NoShow`, `Rejected`.
- Value objects: appointment modality, clinic timezone, reason, idempotency key.
- MVP transitions include `Requested -> Confirmed`, `Requested -> Rejected`, and `Confirmed -> Cancelled`; confirm/reject accept only `Requested` appointments, while a retry of the already-applied same transition is idempotent and any opposite/otherwise invalid transition returns `409`.
- Invariants: new appointment is Requested; appointments are never deleted; cancelled/rejected rows remain; every transition records previous status, new status, actor, and timestamp in `AppointmentStatusHistory`; reschedule is transactional; booking shares no clinical data.

## 5. Database Changes

- `scheduling.availability_slots`, `appointments`, `appointment_status_history`.
- UUID PKs; doctor/clinic/location/patient/slot FKs.
- Unique partial index permits at most one reserving appointment per slot; cancelled/rejected records remain but release the slot.
- Unique account/idempotency key.
- Index patient/time/status, doctor/time, clinic/time.
- Transactions and constraint-to-409 translation; future range overlap constraints deferred.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `GET /api/v1/doctors/{doctorId}/slots` | None | Public published inventory | List available future slots | `200` | Doctor `404`; invalid range `422` |
| `POST /api/v1/appointments` | Bearer | Owner/active manager for patient | Request a slot | `201` Requested appointment | Slot conflict `409`; unauthorized patient `404`; expired/modality mismatch `422` |
| `GET /api/v1/appointments` | Bearer | Accessible patients only | List appointments | `200` page | Invalid filter/cursor `422` |
| `GET /api/v1/appointments/{id}` | Bearer | Appointment patient authority | Detail + status history | `200` | Concealed `404` |
| `POST /api/v1/appointments/{id}/confirm` | Bearer | `AppointmentScheduler` permission for appointment clinic | Confirm a requested appointment | `200` Confirmed appointment | Repeat confirm idempotent; absent `404`; unauthorized `403`; invalid transition/concurrency `409` |
| `POST /api/v1/appointments/{id}/reject` | Bearer | `AppointmentScheduler` permission for appointment clinic | Reject and retain a requested appointment | `200` Rejected appointment | Repeat reject idempotent; absent `404`; unauthorized `403`; invalid transition/concurrency `409` |
| `POST /api/v1/appointments/{id}/cancel` | Bearer | Patient authority under current MVP rules | Cancel and retain | `200` | Invalid transition/concurrency `409`; `404` |
| `POST /api/v1/appointments/{id}/reschedule` | Bearer | Patient authority under current MVP rules | Move request transactionally | `200` | Target slot conflict `409`; invalid state `409`; `404/422` |

## 7. Application / Use Cases

- `ListAvailableSlots`, `RequestAppointment`, `ListAppointments`, `GetAppointment`, `ConfirmAppointment`, `RejectAppointment`, `CancelAppointment`, `RescheduleAppointment`.
- State machine, transition history, idempotency, and database-conflict mapping.
- Future `IVideoMeetingProvider` contract is defined only if needed by the domain boundary.

## 8. Authentication and Authorization

- Slot discovery is anonymous.
- Booking/history and patient cancellation/rescheduling require bearer authentication and patient authority.
- Confirm/reject require bearer authentication and a narrow `AppointmentScheduler` permission scoped to the appointment's clinic. For the MVP/demo, this permission is assigned only to explicitly approved authenticated demo identities through deployment configuration/seed data and grants no patient-clinical-data access.
- Full Doctor/Clinic role modeling, onboarding, permission administration, and portals remain POST-MVP/TBD.

## 9. Security and Privacy

- Appointment reason is sensitive and excluded from logs.
- Booking never grants doctor access to Pre-Triage or profile.
- Confirmation, rejection, and cancellation are audited status transitions, never deletion.

## 10. External Integrations

- **IMPLEMENT NOW:** none.
- **INTERFACE/PLACEHOLDER:** `IVideoMeetingProvider` only if appointment model requires meeting metadata.
- **POST-MVP:** Google Meet, payments, external calendars.

## 11. FHIR Impact

No FHIR is generated in Phase 8. Any later Appointment export in Phase 6 must follow `Backend/docs/fhir/beeexy-coleccion-recursos.md`; no appointment mapping is invented here.

## 12. Tests

- Two concurrent booking requests for one slot: exactly one success, one `409`.
- Idempotent booking retry returns original appointment.
- Initial status always Requested.
- Authenticated/authorized `Requested -> Confirmed` and `Requested -> Rejected` API/integration tests, including same-action idempotent retries and exactly one status-history entry per applied transition.
- Missing scheduling permission is rejected; cross-clinic permission cannot confirm/reject; opposite and other invalid transitions return `409` without changing history.
- `Requested -> Confirmed -> Cancelled` and `Requested -> Rejected` retain the complete ordered status history; rejected appointments release the slot without deletion.
- Allowed/invalid cancellation and reschedule transitions; transaction rollback.
- Cancelled records/history retained and slot release behavior.
- Clinic timezone and DST boundaries.
- No implicit clinical sharing.
- Mandatory endpoint test matrix for all eight endpoints, including API/integration coverage for confirm and reject.

## 13. Acceptance Criteria

- Database constraint prevents duplicate reservation under concurrency.
- Appointments start Requested and support at least `Requested -> Confirmed`, `Requested -> Rejected`, and `Confirmed -> Cancelled`, retaining complete status history without deletion.
- Only an authenticated identity with the clinic-scoped MVP/demo scheduling permission can confirm/reject, and invalid transitions return `409`.
- Patient authorization and timezone behavior are verified.
- All tests pass.

## 14. Dependencies

- Phases 2 and 7 are required. Phase 3 is required only when appointment operations involve managed/dependent PatientProfiles.
- Approved patient cancel/reschedule rules and seed availability.
- Explicitly approved demo scheduler identities and their clinic assignments for the narrow `AppointmentScheduler` permission.

## 15. Deferred / TBD Items

- Completion/no-show and other clinic transition APIs, full Doctor/Clinic authorization and permission administration, onboarding/portal workflows, exact production permission windows, arbitrary range overlap, Google Meet, intake integration, payments, and billing.

---

# Phase 9 — Longitudinal Symptom Follow-Up and Care Guide

**Priority:** MVP SHOULD-HAVE; CLINICAL INPUT BLOCKED

## 1. Objective

Retain repeated check-ins and the exact recommendation shown at each moment, using only approved clinical rules and reviewed Care Guide templates.

## 2. Scope

- Multiple check-ins per Pre-Triage episode.
- Deterministic worsening/red-flag evaluation.
- Recommendation snapshots and reminder intent.
- Reviewed Care Guide template selection/display.

## 3. Explicitly Out of Scope

- Invented reminder/escalation rules, AI-generated care instructions, and routine/task completion tracking.

## 4. Domain Model

- Entities: `SymptomCheckIn`, `FollowUpAssessment`, `CareGuideTemplateVersion`, `CareGuideSnapshot`, `ReminderIntent`.
- Relationships: check-in and guide link to patient/Pre-Triage/rule/template version.
- Invariants: all check-ins/recommendations retained; only approved rule/template versions display; routines are visual-only.

## 5. Database Changes

- `care.symptom_check_ins`, `follow_up_assessments`, `care_guide_template_versions`, `care_guide_snapshots`, `reminder_intents`.
- UUID PKs; patient/episode/rule/template FKs; structured answers; recommendation snapshot; scheduled instant and user timezone.
- Index episode/time and pending reminder due time.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/pre-triage/episodes/{id}/check-ins` | Bearer | Owner/active manager | Record check-in and approved response | `201` | No applicable approved rules `422`; `404` |
| `GET /api/v1/pre-triage/episodes/{id}/check-ins` | Bearer | Owner/active manager | Longitudinal check-in history | `200` | `404` |
| `GET /api/v1/pre-triage/episodes/{id}/care-guide` | Bearer | Owner/active manager | Reviewed guide + provenance | `200` | No approved template `422`; `404` |

## 7. Application / Use Cases

- `RecordSymptomCheckIn`, `EvaluateFollowUp`, `ListCheckIns`, `GetCareGuide`, `CreateReminderIntent`.
- Uses approved deterministic rule/template providers.

## 8. Authentication and Authorization

All capabilities require bearer authentication and patient authority. Anonymous users cannot access Follow-Up or Care Guide.

## 9. Security and Privacy

- Answers/recommendations are sensitive clinical data.
- Preserve shown snapshot and author/version provenance.
- No clinical payload in notification intent/logs beyond necessary references.

## 10. External Integrations

- **IMPLEMENT NOW:** approved rule/template import.
- **INTERFACE/PLACEHOLDER:** notification dispatch consumed in Phase 12.
- **POST-MVP:** AI-authored clinical care.

## 11. FHIR Impact

No mapping is invented. Future resources depend on Andrea's materials.

## 12. Tests

- Multiple ordered check-ins and immutable recommendation snapshots.
- Approved red-flag/worsening fixtures and rule versioning.
- Missing/unapproved rule/template fails closed.
- No completion-state persistence.
- Reminder calculation in user timezone using approved intervals and DST boundaries.
- Authorization/relationship revocation tests.
- Mandatory endpoint test matrix for all three endpoints.

## 13. Acceptance Criteria

- Longitudinal records and displayed recommendations are retained.
- Only medically approved content/rules reach the patient.
- No task-completion state exists.
- All tests pass.

## 14. Dependencies

- Phases 4-5; Phase 12 for actual reminder delivery.
- Medical-team follow-up rules, thresholds, intervals, actions, and reviewed templates.

## 15. Deferred / TBD Items

- All unresolved clinical reminder/escalation behavior, Care Guide catalog, content governance workflow, and task tracking.

---

# Phase 10 — AI Platform, Second Opinion, and AI Conversation History

**Priority:** MVP SHOULD-HAVE

## 1. Objective

Add one replaceable AI provider with Beeexy safety validation, immutable result snapshots, temporary documents, full execution traceability, and history separate from Clinical History.

## 2. Scope

- Free AI conversations.
- Second Opinion from supported non-OCR inputs.
- Temporary uploads with 24-hour deletion.
- Provider/prompt/model/timing/status/failure/safety metadata.
- Safe failure and immutable regeneration.

## 3. Explicitly Out of Scope

- Three-model execution, AI authority over clinical rules, AI-generated urgency/questions/care instructions, OCR, automatic clinical-history promotion, and editing a snapshot in place.

## 4. Domain Model

- Entities: `AiConversation`, `AiMessage`, `AiAnalysisRequest`, `AiResultSnapshot`, `AiExecution`, `AiUploadedDocument`, `AiSafetyValidation`.
- Statuses: `Pending`, `Running`, `Succeeded`, `Failed`, `Rejected`.
- Invariants: one configured provider call per execution; safety validation before display; regeneration creates a new snapshot; AI failure never invalidates deterministic assessment; AI history is non-clinical.

## 5. Database Changes

- `ai.ai_conversations`, `ai_messages`, `ai_analysis_requests`, `ai_result_snapshots`, `ai_executions`, `ai_uploaded_documents`, `ai_safety_validations`.
- UUID PKs/FKs; provider/model/prompt version; timestamps/latency/status; sanitized failure category; safety result; artifact URI/expiry/deletion state.
- Index account/patient/time/status and document expiry.
- Raw health content is not duplicated in technical execution rows.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/ai/conversations` | Bearer | Current account | Start free consultation | `201` | Invalid purpose `422` |
| `GET /api/v1/ai/conversations` | Bearer | Owner only | List separate AI history | `200` | `401` |
| `GET /api/v1/ai/conversations/{id}` | Bearer | Conversation owner | Get messages/snapshots | `200` | `404` |
| `POST /api/v1/ai/conversations/{id}/messages` | Bearer | Conversation owner | Submit message | `202` execution | Concurrent execution `409`; unsafe input `422`; `404` |
| `POST /api/v1/ai/documents` | Bearer | Uploader | Store temporary upload | `201` metadata | Type/size/malware `413/415/422` |
| `DELETE /api/v1/ai/documents/{id}` | Bearer | Uploader | Delete early | `204` | Concealed `404`; repeat idempotent |
| `POST /api/v1/ai/second-opinions` | Bearer | Owner/active manager for patient | Start analysis | `202` | Unsupported/missing input `422`; patient `404` |
| `GET /api/v1/ai/second-opinions/{id}` | Bearer | Patient authority | Safe result/status | `200` | `404`; rejected raw output never returned |
| `POST /api/v1/ai/second-opinions/{id}/regenerate` | Bearer | Patient authority | Create new execution/snapshot | `202` | Already running `409`; `404` |

## 7. Application / Use Cases

- Conversation create/list/get/send.
- `UploadAiDocument`, `DeleteAiDocument`, `ExpireAiDocuments`.
- `RequestSecondOpinion`, `ExecuteAiAnalysis`, `ValidateAiSafety`, `GetSecondOpinion`, `RegenerateSecondOpinion`.
- Provider-neutral prompt builder and result-schema validator.

## 8. Authentication and Authorization

All AI capabilities require bearer authentication. Conversations are account-owned; patient analyses require patient authority. Anonymous access is prohibited.

## 9. Security and Privacy

- Private object storage, short-lived access, type/size/malware validation.
- Documents deleted no later than 24 hours.
- Prompts/provider payloads not logged.
- Unsafe/incomplete output never displayed; generic fallback returned.

## 10. External Integrations

- **IMPLEMENT NOW:** one selected `IAiProvider`, `IAiSafetyValidator`, private `IBlobStore`.
- **INTERFACE/PLACEHOLDER:** OCR/multimodal extraction.
- **POST-MVP:** additional providers/OCR.

## 11. FHIR Impact

AI conversations do not automatically produce FHIR or Clinical History. Execution provenance remains available if Andrea later defines a mapping.

## 12. Tests

- Exactly one provider call per execution.
- Success, timeout, malformed response, unsafe rejection, transient/permanent failure.
- Generic failure and deterministic-assessment independence.
- Execution metadata completeness without PHI log duplication.
- Immutable regeneration and conversation/clinical-history separation.
- Upload ownership/type/size checks and manual/automatic 24-hour deletion.
- Concurrent message/regeneration conflicts and idempotency.
- Mandatory endpoint test matrix for all nine endpoints.

## 13. Acceptance Criteria

- Only safety-approved AI output is displayed.
- AI is provider-replaceable and never authoritative over deterministic rules.
- AI history remains separate and snapshots immutable.
- Document retention is enforced and tested.
- All tests pass.

## 14. Dependencies

- Phases 2-5.
- Selected AI provider/credentials; approved prompt versions, safety policy, disclaimers, input formats, and limits.

## 15. Deferred / TBD Items

- Provider selection, file formats/limits, text-native PDF handling, rejected-output retention, OCR/multimodal processing, and clinical promotion workflow.

---

# Phase 11 — Secure Sharing, QR Access, and Exports

**Priority:** MVP CORE

## 1. Objective

Provide secure, expiring, revocable, unauthenticated read-only sharing plus human-readable PDF, Beeexy JSON, and validated FHIR JSON exports.

## 2. Scope

- Initial `FullProfile` QR share.
- Extensible granular scopes/items.
- Token exchange, shared read-only projection, expiry/revocation, access events.
- PDF, Beeexy JSON, and FHIR JSON export.

## 3. Explicitly Out of Scope

- Recipient editing, Beeexy-ID access, authenticated provider portal, guarantees about copies downloaded outside Beeexy, and granular frontend UI beyond initial FullProfile.

## 4. Domain Model

- Entities: `ShareGrant`, `ShareGrantItem`, `ShareAccessEvent`, `ExportArtifact`.
- Scopes: `FullProfile`, `Case`, `PreTriage`, `Visit`, `SpecificRecords`.
- Events: `ShareCreated`, `ShareAccessed`, `ShareRevoked`, `ShareExpired`.
- Invariants: token random/hashed; read-only; scope filters every projection; revocation/expiry blocks future access; QR contains no clinical data.

## 5. Database Changes

- `sharing.share_grants`, `share_grant_items`, `share_access_events`, `export_artifacts`.
- UUID PKs; patient/creator/source FKs; scope; token hash; expiry/revocation; access timestamps/outcome; export format/checksum/private URI.
- Unique active token hash; indexes patient, expiry, event time.
- No plaintext capability storage.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/shares` | Bearer | Patient owner/authorized manager with sharing capability | Create grant | `201` token returned once + frontend URL | Invalid scope/expiry `422`; duplicate idempotency `409` |
| `GET /api/v1/shares` | Bearer | Patient authority | List grants/status | `200` | `401/404` |
| `POST /api/v1/shares/{id}/revoke` | Bearer | Grant creator/patient authority | Revoke | `204` | `404`; repeat idempotent |
| `GET /api/v1/shares/{id}/activity` | Bearer | Patient authority | User-facing access events | `200` | `404` |
| `POST /api/v1/shared-access/exchange` | None; rate limited | Possession of valid capability | Exchange QR token for short read-only token | `200` | Invalid/expired/revoked `401`; throttle `429` |
| `GET /api/v1/shared-access/profile` | Share-access token | Grant scope | Read shared projection | `200` | Expired/revoked `401`; out-of-scope `403` |
| `POST /api/v1/patients/{id}/exports` | Bearer | Patient authority | Generate PDF/Beeexy JSON/FHIR JSON | `201` metadata | Format/mapping unavailable `422`; duplicate `409`; `404` |
| `GET /api/v1/exports/{id}/content` | Bearer patient authority or permitted share token | Artifact scope | Download | `200` correct media type | `403/404`; incomplete `409` |

## 7. Application / Use Cases

- `CreateShare`, `ListShares`, `RevokeShare`, `ExpireShares`, `ListShareActivity`, `ExchangeShareCapability`, `BuildSharedProfile`, `GenerateExport`, `DownloadExport`.
- Scope evaluator reusable for future granular UI.

## 8. Authentication and Authorization

- Creator operations require bearer and patient authority.
- Recipient needs no account; capability exchange grants temporary read-only scope.
- Beeexy ID is never accepted.
- Management authority and sharing recipient status remain distinct.

## 9. Security and Privacy

- Prefer frontend QR URL fragment so token is exchanged in POST body and avoided in server/referrer logs.
- Store token hash; rate-limit exchange; audit access/download/revoke/expire.
- Patient-facing activity contains approved metadata only.
- Warn that revocation cannot remove external downloaded copies.

## 10. External Integrations

- **IMPLEMENT NOW:** PDF renderer and private artifact storage.
- **INTERFACE/PLACEHOLDER:** frontend QR rendering (not backend concern).
- **POST-MVP:** authenticated provider access.

## 11. FHIR Impact

FHIR export delegates to Phase 6 and returns only validated Andrea-compliant snapshots.

## 12. Tests

- Token entropy/hash/no-log tests.
- Exchange, expiry, revocation, rate limiting, and concurrent revoke/access behavior.
- Beeexy ID access rejection.
- FullProfile projection and granular-scope isolation.
- Read-only enforcement and access-event idempotency.
- PDF, Beeexy JSON, and validated FHIR JSON content/media type.
- Cross-patient share/export IDOR tests.
- Mandatory endpoint test matrix for all eight endpoints.

## 13. Acceptance Criteria

- QR capability opens a read-only view without recipient login.
- Expiry/revocation immediately prevents future Beeexy access.
- Patient sees sharing activity.
- All three formats export with correct authorization.
- All tests pass.

## 14. Dependencies

- Phases 5-6; Phase 10 to include AI history; Phase 13 for Visit scopes.
- Product decision for default/max share duration and public frontend share URL.

## 15. Deferred / TBD Items

- Share-duration defaults, granular UI, recipient identity, long-term export retention, and downloaded-copy governance beyond disclosure.

---

# Phase 12 — In-App and PWA Push Notifications

**Priority:** MVP CORE

## 1. Objective

Deliver traceable in-app and Web Push notifications with correct clinic/user timezone behavior and no invented clinical reminder schedule.

## 2. Scope

- Notification inbox/read state.
- PWA push subscription management.
- Database outbox/background delivery.
- Delivery attempt/failure tracking.
- Appointment notifications and approved reminder intents.

## 3. Explicitly Out of Scope

- Email/SMS product notifications and undefined clinical reminders/escalations.

## 4. Domain Model

- Entities: `Notification`, `PushSubscription`, `NotificationDelivery`, `NotificationPreference`, `OutboxMessage`.
- Statuses only as observable: subset of `Pending`, `Sent`, `Delivered`, `Failed` supported by provider.
- Invariants: appointment uses clinic timezone; personal reminder uses user timezone; source transaction/outbox atomic; push payload contains minimal data.

## 5. Database Changes

- `notifications.notifications`, `push_subscriptions`, `notification_deliveries`, `notification_preferences`, `outbox_messages`.
- UUID PKs; account/patient/source FKs; channel/status; scheduled/attempted/delivered timestamps; failure category; retry count; timezone; read timestamp.
- Index pending due delivery/outbox, account/read/time, subscription endpoint hash.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `GET /api/v1/notifications` | Bearer | Account owner | List inbox | `200` page | Invalid cursor `422` |
| `POST /api/v1/notifications/{id}/read` | Bearer | Notification owner | Mark read | `204` | `404`; repeat idempotent |
| `POST /api/v1/push-subscriptions` | Bearer | Account owner | Register/update subscription | `200/201` | Invalid endpoint/key `422` |
| `DELETE /api/v1/push-subscriptions/{id}` | Bearer | Subscription owner | Revoke | `204` | `404`; repeat idempotent |
| `GET /api/v1/notification-preferences` | Bearer | Account owner | Read preferences/timezone | `200` | `401` |
| `PATCH /api/v1/notification-preferences` | Bearer | Account owner | Update supported preferences/timezone | `200` | Invalid IANA zone/setting `422`; concurrency `409` |

## 7. Application / Use Cases

- Inbox list/read; subscription register/revoke; preference get/update.
- `EnqueueNotification`, `DispatchDueNotifications`, `RecordDeliveryAttempt`, `RetryDelivery`.
- Source modules create notification intents/outbox atomically.

## 8. Authentication and Authorization

All endpoints require bearer and account ownership. Push subscription is secret account-bound data.

## 9. Security and Privacy

- Push payload avoids detailed health information and deep-links to authenticated content.
- Subscription endpoints/keys are not logged.
- Attempt/failure metadata retained for troubleshooting; retention TBD.

## 10. External Integrations

- **IMPLEMENT NOW:** Web Push/VAPID adapter.
- **POST-MVP:** email and SMS delivery.

## 11. FHIR Impact

None.

## 12. Tests

- Inbox ownership/read idempotency.
- Subscription replacement/revocation and cross-account denial.
- Outbox processing/retry/idempotency and provider failure.
- Stale subscription handling.
- Persisted channel/status/timestamps/failure reason.
- Clinic/user timezone and DST tests.
- Verify no undefined clinical reminder is scheduled.
- Mandatory endpoint test matrix for all six endpoints.

## 13. Acceptance Criteria

- In-app and Web Push work with traceable attempts.
- Correct timezone source is used.
- Provider failure does not corrupt source workflow.
- All tests pass.

## 14. Dependencies

- Phase 2; Phase 8 for appointment notifications; Phase 9 for approved clinical reminders.
- VAPID/deployment keys and approved notification copy.

## 15. Deferred / TBD Items

- Clinical reminder triggers/intervals/escalation, delivery receipt semantics, retention, email, and SMS.

---

# Phase 13 — Minimal Visit Recording Pipeline

**Priority:** CONDITIONAL MVP / HIGH RISK

## 1. Objective

Implement the minimum consented microphone-upload-to-transcript-to-summary flow without allowing it to block the core MVP.

## 2. Scope

- Explicit patient consent and formal doctor attestation.
- Private audio upload, transcription/diarization, summary, structured extraction.
- Original + corrected transcript/summary versions.
- Flexible Visit links and granular share items.
- Manual deletion and automatic maximum seven-day retention.

## 3. Explicitly Out of Scope

- Jurisdiction-specific compliance claims, live streaming, OCR, direct Deepgram coupling, and automatic representation of extracted facts as clinically confirmed.

## 4. Domain Model

- Entities: `Visit`, `VisitCaseLink`, `RecordingConsent`, `VisitRecording`, `TranscriptionExecution`, `TranscriptVersion`, `VisitSummaryVersion`, `VisitClinicalExtraction`, `VisitArtifactDeletion`.
- Extraction types: medication, diagnosis, test, action/recommendation.
- Statuses: upload/processing/completed/failed/deleted plus extraction `AutomaticallyExtracted` vs `ClinicallyConfirmed`.
- Invariants: consent/attestation precede upload; originals preserved; corrections add versions; audio expires <=7 days; deleting audio deletes transcript/summary; structured extraction remains separately identifiable with provenance.

## 5. Database Changes

- `visits.visits`, `visit_case_links`, `recording_consents`, `visit_recordings`, `transcription_executions`, `transcript_versions`, `visit_summary_versions`, `visit_clinical_extractions`, `visit_artifact_deletions`.
- UUID PKs; patient/appointment/Pre-Triage/case FKs; many-to-many case links; consent/attestation versions/times; artifact URI/checksum/expiry/deletion; edit author/time; extraction provenance/confirmation status.
- Index patient/time/status, expiry, related entity, processing status.
- Audio stored privately outside PostgreSQL.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/visits` | Bearer | Patient owner/active manager | Create Visit with declarations/links | `201` | Missing/invalid consent/attestation `422`; unauthorized link `404` |
| `POST /api/v1/visits/{id}/recording-upload` | Bearer | Patient authority | Obtain private upload authorization | `200` | Type/size/state `413/415/409`; `404` |
| `POST /api/v1/visits/{id}/recording-complete` | Bearer | Patient authority | Verify upload and queue processing | `202` | Checksum/input `422`; duplicate/state `409` |
| `GET /api/v1/visits` | Bearer | Accessible patients | List visits | `200` page | Invalid filter `422` |
| `GET /api/v1/visits/{id}` | Bearer | Patient authority | Detail/status/versions/extractions | `200` | `404` |
| `POST /api/v1/visits/{id}/transcript-versions` | Bearer | Patient authority | Add correction | `201` | Deleted/state `409`; invalid text `422`; `404` |
| `POST /api/v1/visits/{id}/summary-versions` | Bearer | Patient authority | Add correction | `201` | Deleted/state `409`; invalid text `422`; `404` |
| `DELETE /api/v1/visits/{id}/recording` | Bearer | Patient authority | Delete audio/transcript/summary early | `204` | `404`; repeat idempotent |

## 7. Application / Use Cases

- `CreateVisit`, `AuthorizeRecordingUpload`, `CompleteRecordingUpload`, `ProcessRecording`, `TranscribeVisit`, `SummarizeVisit`, `ExtractStructuredVisitData`, `CorrectTranscript`, `CorrectSummary`, `DeleteVisitRecording`, `ExpireVisitRecordings`, `Get/ListVisit`.

## 8. Authentication and Authorization

All Visit capabilities require bearer and patient authority. Links to appointment/Pre-Triage/cases are independently authorized. Share access is granted only through Phase 11 item scopes.

## 9. Security and Privacy

- Dedicated explicit consent and versioned formal doctor attestation.
- Private storage, short-lived upload authorization, media validation, no public URLs.
- Audio/transcript/summary excluded from logs.
- Delete audio/transcript/summary together manually or by seven-day job; audit deletion.

## 10. External Integrations

- **IMPLEMENT NOW IF PHASE AUTHORIZED:** replaceable `ISpeechTranscriptionProvider` with diarization, private `IBlobStore`, and provider-neutral summarization/extraction.
- **CANDIDATE ONLY:** Deepgram adapter.
- **POST-MVP:** live transcription and advanced multimodal/OCR.

## 11. FHIR Impact

Structured extracted data is FHIR-ready and retains provenance/confirmation status, but no resource mapping is implemented without Andrea's materials.

## 12. Tests

- Missing patient consent/doctor attestation rejection.
- Flexible patient/appointment/Pre-Triage/multiple-case links and authorization.
- Upload ownership/type/size/checksum/state tests.
- Provider success, timeout, failure, retry, and diarization payload mapping.
- Original preservation and corrected-version history/edit metadata.
- Automatically extracted vs clinically confirmed distinction.
- Manual/automatic deletion of audio/transcript/summary and inaccessible deleted artifacts.
- Seven-day boundary and cleanup idempotency/concurrency.
- Granular Visit sharing isolation with Phase 11.
- Mandatory endpoint test matrix for all eight endpoints.

## 13. Acceptance Criteria

- Consent precedes recording.
- Functional upload -> transcription -> summary pipeline works with a replaceable provider.
- Corrections preserve originals.
- Structured extractions retain source/confirmation provenance.
- Retention/deletion is verified.
- All tests pass; otherwise this phase remains incomplete and must not block core MVP release.

## 14. Dependencies

- Phases 2-5, 10-11.
- Selected speech provider/credentials, storage, approved consent/attestation wording, media formats/limits, and summarization/safety rules.

## 15. Deferred / TBD Items

- Final speech provider, jurisdiction/legal requirements, max recording length/formats, structured-extraction retention after artifact deletion, clinical confirmation workflow, and exact FHIR mappings.

---

# Phase 14 — MVP Security, Retention, and Operational Hardening

**Priority:** MVP CORE BEFORE EXTERNAL DEMO/DEPLOYMENT

## 1. Objective

Verify the assembled MVP as an authorization-safe, retention-aware, observable system and exercise its critical journeys end to end.

## 2. Scope

- End-to-end workflows and regression suite.
- Cleanup-job resilience for every defined retention category.
- Authorization/threat review, rate limiting, audit completeness, dependency health, migration upgrade tests, and deployment runbooks.

## 3. Explicitly Out of Scope

- HIPAA/compliance claims, undefined long-term retention enforcement, admin portals, and production organizational/legal controls.

## 4. Domain Model

- Standardized `SecurityAuditEvent` and `RetentionJobExecution` where needed.
- No new patient product domain.
- Invariants: retries/idempotency; user-visible audit limited to relevant sharing events; technical audit access has no patient portal endpoint.

## 5. Database Changes

- Only audit/job metadata and indexes proven necessary by tests.
- Verify every FK/cascade behavior.
- Migration-upgrade path from every prior phase and rollback/runbook where feasible.

## 6. API Endpoints

No new product endpoint. Existing readiness may include enabled critical dependencies without exposing secrets.

## 7. Application / Use Cases

- Reconcile expired anonymous sessions/documents/shares/recordings.
- Recover interrupted outbox/cleanup work.
- Validate startup/deployment configuration.
- Correlate security/audit events.

## 8. Authentication and Authorization

- Full policy matrix for anonymous, account owner, manager, unrelated account, and share recipient.
- Future roles are reserved but not granted patient portal access.

## 9. Security and Privacy

- Threat tests for IDOR, token leakage/replay, upload attacks, prompt/provider leakage, appointment concurrency, log/error leakage, and expired capability access.
- Document that application controls alone do not establish healthcare compliance.

## 10. External Integrations

- **IMPLEMENT NOW:** deployment smoke checks for enabled providers.
- Optional providers fail safely when disabled/unavailable.
- Production vendor/hosting decisions remain external prerequisites.

## 11. FHIR Impact

Run all approved golden exports through the selected validator; no new mapping.

## 12. Tests

- End-to-end: authenticate -> Pre-Triage -> persist -> history -> FHIR/export.
- Anonymous: complete -> view -> claim; separate unclaimed expiry.
- Doctor search -> slot -> concurrent appointment booking.
- Follow-up/Care Guide with approved fixtures.
- AI safe failure while deterministic result survives.
- Share create -> exchange -> access -> audit -> revoke/expire.
- Notification success/failure/timezone.
- Visit pipeline when Phase 13 is included.
- Full cross-account/managed-patient authorization matrix.
- Cleanup restart/idempotency and dependency outage/recovery.
- Fresh and upgrade migration tests.
- OpenAPI compatibility snapshot and sensitive-log scan.

## 13. Acceptance Criteria

- All prior/new tests pass.
- Clean and upgrade migrations apply.
- Defined data-specific retention jobs are observable and idempotent.
- No cross-patient access or token leakage is found.
- Optional provider failure degrades safely.
- Runbooks contain no unsupported compliance promise.

## 14. Dependencies

- Every MVP-selected previous phase and deployment configuration.
- Security/privacy/product approval for external demo scope.

## 15. Deferred / TBD Items

- Production hosting, backup/restore objectives, DR, penetration test, compliance program, and retention schedules for long-lived categories.

---

# Phase 15 — Post-MVP Extension Gates

**Priority:** POST-MVP; NOT AUTHORIZED FOR IMPLEMENTATION BY THIS PLAN

## 1. Objective

Record future extension boundaries so deferred concepts are not accidentally pulled into MVP phases.

## 2. Scope

Potential separately approved future plans for caregiver-only accounts, dependent claiming, minors, provider/clinic onboarding and portals, tenant branding/configuration, admin/support/reviewer/operations portals, external FHIR servers, OCR/multimodal processing, real-time insurance, video provider, reviews, payments, email/SMS notifications, and confirmed structured Visit records.

## 3. Explicitly Out of Scope

Implementation of every item listed above.

## 4. Domain Model

No changes. Future phases must extend current boundaries without changing established ownership, provenance, clinical authority, or FHIR separation rules.

## 5. Database Changes

None.

## 6. API Endpoints

None.

## 7. Application / Use Cases

None until a separate product-approved plan exists.

## 8. Authentication and Authorization

Future Doctor, Clinic Admin, Beeexy Admin, Clinical Reviewer, and Support roles require separately reviewed policies; no implicit access is granted now.

## 9. Security and Privacy

Each future plan requires its own threat, privacy, retention, consent, and audit analysis.

## 10. External Integrations

- **POST-MVP:** all items listed in Scope.

## 11. FHIR Impact

Any new mapping must be added only through Andrea-approved interoperability materials.

## 12. Tests

No code tests. Before any deferred item is implemented, its new phase must define unit, integration, endpoint, authorization, database, concurrency, security, and FHIR tests as applicable and preserve the mandatory endpoint matrix.

## 13. Acceptance Criteria

This phase is documentation-only and must never be interpreted as implementation authorization.

## 14. Dependencies

Explicit user authorization plus applicable product, clinical, legal, privacy, credential, and provider decisions.

## 15. Deferred / TBD Items

- Complex minor/caregiver workflows.
- Dependent profile claiming.
- Apple authentication.
- Doctor/clinic onboarding and B2B portal.
- Admin/support/clinical-review/operations portals.
- External FHIR server integration.
- OCR/multimodal processing.
- Real-time insurance eligibility.
- Reviews, payments/copays/billing.
- Advanced video-provider integration.
- Email/SMS product notifications.
- All unresolved long-term retention, legal, clinical, and compliance rules.

---

## Phase execution protocol

When a phase is explicitly authorized:

1. Implement only that phase and its stated prerequisites already approved.
2. Add its migration and all mandatory tests alongside the implementation.
3. Run build, migration, all existing tests, and all new tests.
4. Do not mark complete while any required test fails.
5. Update this file with phase status and verification evidence.
6. Stop; do not start the next phase without explicit authorization.

## Critical blockers before their phases

- **Phase 2:** Final demographic requirements beyond the fields explicitly documented in `Backend/docs/fhir/` remain TBD.
- **Phase 4:** medically approved questionnaire, urgency model, red flags, rules, and messages.
- **Phase 7:** authoritative directory data and approved deterministic matching factors/weights.
- **Phase 8:** final patient cancel/reschedule rules and approved demo scheduler identity/clinic assignments; advanced Doctor/Clinic authorization is POST-MVP and does not block the minimum confirm/reject mechanism.
- **Phase 9:** approved follow-up rules, intervals, escalation actions, and Care Guide templates.
- **Phase 10:** AI provider, prompt/safety policy, supported inputs, limits, and credentials.
- **Phase 11:** share duration defaults and frontend public share URL.
- **Phase 12:** VAPID keys and approved notification copy/rules.
- **Phase 13:** recording consent/attestation text, speech provider, media constraints, and structured-extraction retention decision.
- **Production:** long-term retention/deletion, legal/privacy/compliance controls, deployment, backup, and disaster-recovery requirements.

## Recommended first phase

Implement **Phase 1 — Backend and Database Foundation** first. It establishes the compilation, migration, API, error-handling, security configuration, and test infrastructure required by every subsequent phase without implementing product behavior.
