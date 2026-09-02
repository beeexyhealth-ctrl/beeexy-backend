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

**Phase 4 status:** COMPLETE (2026-08-22)

**Priority:** MVP CORE

## 1. Objective

Deliver a controlled AI-assisted symptom-intake demo for anonymous and authenticated users. The demo collects a minimum structured dataset for the three confirmed pathways `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`, completes it into an immutable episode, and returns a neutral symptom summary. `ABDOMINAL_PAIN` is displayed as "Stomach pain" in the frontend. Natural-language interpretation may make intake easier, but validated package-defined answers, deterministic questionnaire progression, and a deterministic completeness check remain authoritative. The current demo does not classify clinical urgency, calculate disposition, diagnose, prescribe, or approximate a production triage protocol.

## 2. Scope

- Temporary active sessions for anonymous/authenticated flows.
- Provider-independent AI-assisted intent classification and structured interpretation of natural-language symptom input.
- Application-level clinical-AI safety policies and schema/output validation before extracted data can affect workflow state.
- Extraction of a selected primary symptom, duration, intensity from 1 through 10, and controlled additional-symptom selections from exactly `NAUSEA`, `DIARRHEA`, and `FEVER`; one message may populate multiple fields and no fourth additional-symptom option exists.
- Immutable, versioned definition packages with explicit source, review, approval, activation, and detailed-clinical-versus-simplified-demo profile metadata.
- A demo-supported-pathway registry containing exactly `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`. `CHEST_PAIN` and `OTHER_SYMPTOMS` remain recognized but unsupported for the frontend demo and receive no clinical protocol. The already recognized backend-only `RESPIRATORY_SYMPTOMS` and `BACK_PAIN` also remain unsupported and unchanged.
- Simplified immutable questionnaire-package versions for the three supported demo pathways. Each package contains only the controlled demo fields and progression needed for symptom, duration, intensity, and selected additional symptoms. Applicability is deterministic: the `FEVER` primary pathway excludes `FEVER` from its additional-symptom choices, leaving only `NAUSEA` and `DIARRHEA` applicable for that pathway.
- Deterministic questionnaire progression, validation, already-answered-question skipping, and minimum-completeness evaluation.
- Immutable completed episode/assessment persistence and secure neutral result retrieval.
- Optional guarded AI-assisted neutral phrasing after the canonical structured summary exists.
- Secure, idempotent anonymous claim and 24-hour expiry.
- Abandonment cleanup and the completed-episode-only Clinical History projection boundary.

## 3. Explicitly Out of Scope

- Resume after abandonment.
- Clinical urgency classification, including user-facing `CRITICAL`, `HIGH`, `MEDIUM`, `LOW`, or `VERY_LOW` outcomes.
- Clinical disposition calculation, red-flag-based escalation, deterministic urgency-rule execution, emergency-level recommendations, diagnostic probabilities, treatment recommendations, or detailed symptom protocols intended to approximate production clinical triage.
- Autonomous AI clinical decision-making, AI diagnoses, prescriptions, treatment invention, authoritative recommendations, autonomous agents, and AI changes to completed records.
- Python, Google ADK, a separate AI microservice, or vendor-specific domain design without a later demonstrated requirement.
- Supporting every frontend symptom option. Only `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` receive simplified packages; `CHEST_PAIN` and `OTHER_SYMPTOMS` remain recognized but unsupported.
- Invented or unapproved pathways, clinical thresholds, red flags, urgency rules, dispositions, emergency wording, or full protocols.
- Dynamic AI questioning outside the controlled, versioned demo questionnaire.
- FHIR generation, SNOMED production integration, and full Clinical History feature implementation.

## 4. Domain Model

- Entities: `PreTriageSession`, `PreTriageEpisode`, `QuestionnaireDefinitionVersion`, `TriageQuestion`, `TriageAnswer`, `ReportedSymptom`, `ClinicalRuleSetVersion`, `ClinicalAssessment`, `ClinicalFinding`.
- Reuse the completed Phase 4.1 aggregates and versioning model. Do not delete or redesign `ClinicalRuleSetVersion`, `ClinicalFinding`, urgency fields, or other future-clinical structures merely because the demo does not execute them.
- `ClinicalAssessment` represents a structured symptom-intake summary for the demo. The current model requires a non-null urgency code, so Phase 4.7 must add the smallest backward-compatible domain/persistence adjustment that permits a neutral assessment with no urgency rather than storing a fake urgency sentinel. Existing future-clinical assessment creation may retain its stricter invariant. This is the only currently identified Phase 4.1 compatibility adjustment.
- Package/registry concepts remain `ClinicalDefinitionPackage`, `ClinicalPathwayCode`, `IClinicalDefinitionProvider`, and `IClinicalPathwayRegistry`, or equivalent boundaries that map a pathway to an exact immutable questionnaire and package provenance. An existing rule-set version reference may remain for provenance/FK compatibility but is not executed by the demo and must contain no demo urgency/disposition authority.
- AI boundary concepts live in Application/Infrastructure rather than controlling Domain: `ClinicalIntent`, `StructuredSymptomExtraction`, validated fact candidates, and temporary extraction provenance. Concrete provider/model configuration remains outside Domain.
- `PreTriageSession` and its in-progress answers are temporary workflow state. They are not part of Clinical History and are not permanent clinical records.
- Lifecycle: Start Pre-Triage -> temporary `PreTriageSession` -> validated temporary demo answers -> minimum-completeness check -> create permanent immutable `PreTriageEpisode` + neutral `ClinicalAssessment` summary -> project only the completed episode into Clinical History.
- Abandonment lifecycle: `PreTriageSession` -> expires/is discarded -> no `PreTriageEpisode`, `ClinicalAssessment`, or Clinical History record is created.
- Session states: `Active -> Completed`; an anonymous completed episode may become `Claimed` or expire unclaimed.
- Current demo value objects include anonymous capability hash, question code, pathway code, duration value/unit, intensity, controlled option codes, questionnaire/package version, and content provenance. Existing urgency/disposition value objects remain dormant for future compatibility.
- Invariants: only completed `PreTriageEpisode` records are permanent and history-eligible; completion atomically creates exactly one episode and neutral assessment after minimum completeness; projection is idempotent; completed records are immutable; results preserve exact definition versions/provenance; and no urgency, disposition, diagnosis, prescription, treatment recommendation, or numeric disease probability is generated.

### Deterministic demo workflow authority

```text
User natural-language input
        ↓
AI interpretation / structured extraction
        ↓
Application validation / safety guardrails
        ↓
Versioned simplified questionnaire
        ↓
Deterministic progression + completeness check
        ↓
Validated symptom-intake summary
        ↓
Immutable episode + neutral assessment
        ↓
Secure canonical result / optional neutral phrasing
```

> AI assists interpretation and neutral conversation only. Application code owns validation, questionnaire state, completeness, and persistence. Neither AI nor a deterministic urgency engine produces clinical urgency or disposition in the current demo.

## 5. Database Changes

- `triage.pre_triage_sessions`, `pre_triage_episodes`, `questionnaire_versions`, `questions`, `answers`, `reported_symptoms`, `clinical_rule_set_versions`, `clinical_assessments`, `clinical_findings`.
- UUID PKs; nullable patient FK before anonymous claim; unique session-to-episode; token hash unique; claim idempotency constraint.
- Index token hash/expiry, patient/completed time, question/rule versions.
- Preserve the completed Phase 4.1 and Phase 4.2 migrations and all existing clinical-rule/finding structures for future compatibility. Prefer additive definition imports and registry configuration; do not remove columns, tables, constraints, or the detailed abdominal package.
- Add immutable simplified questionnaire/package versions for only `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`. Do not mutate the existing abdominal package in place.
- Migration `20260822061610_Phase45ConfirmedDemoPackages` additively extends the constrained provenance vocabulary with `PRODUCT_DEMO_DEFINED`, `NOT_APPLICABLE`, and `NOT_CLINICALLY_APPROVED`. The migration changes no table or column shape, preserves preexisting rows, and supports rollback/reapply of imported demo definitions without misrepresenting them after reapplication.
- Phase 4.7 must make the current required `clinical_assessments.urgency_code` nullable, or implement an equally small truthful representation, so a neutral symptom summary does not persist a fabricated urgency. Prefer a nullable field with a dedicated neutral-assessment factory over a sentinel urgency code. No destructive migration is allowed.
- If temporary AI-extraction provenance is persisted, keep it in temporary workflow/application-owned storage, exclude provider-specific fields from core Domain, apply minimum retention, and never treat raw or unvalidated extraction as a clinical fact.
- Session and in-progress answer rows are temporary workflow storage, including when stored server-side for anonymous execution. Completion materializes the permanent episode/assessment records; abandoned sessions never do.
- Unclaimed anonymous temporary data and completed episodes expire after 24 hours; a completed anonymous episode may be claimed by an authenticated primary patient within that period.
- Abandoned authenticated sessions are expired/discarded by cleanup, cannot be resumed in the MVP, and never create permanent clinical or history records.
- Clinical definitions are immutable versioned import artifacts. The existing abdominal versions retain `REFERENCE_PLATFORM_DERIVED`, `PROVISIONAL`, and `PENDING_FORMAL_REVIEW`; new simplified demo versions retain truthful demo provenance and are never presented as approved clinical protocols.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/pre-triage/sessions` | Optional Bearer | Anonymous or owner/active manager for patient | Start against an explicit demo-supported pathway | `201`; anonymous capability returned once | Unauthorized patient concealed `404`; unsupported/unknown/unavailable pathway `422`; invalid supplied Bearer `401` |
| `POST /api/v1/pre-triage/sessions/{id}/answers` | Bearer owner/manager or anonymous capability header | Matching session access | Submit explicit structured values or natural language, persist validated answers, and return progress/next question | `200` progress | Invalid/ambiguous extraction or answer produces safe clarification/`422`; completed/expired/stale state `409`; invalid capability `401` |
| `POST /api/v1/pre-triage/sessions/{id}/complete` | Same | Matching session access | Validate minimum demo completeness and atomically persist a neutral episode/assessment summary | `201` completed summary reference | Incomplete `422`; expired `404/409`; concurrent/repeat completion follows documented idempotency contract |
| `GET /api/v1/pre-triage/sessions/{id}/result` | Bearer owner/manager or anonymous capability | Matching completed session | Retrieve the neutral structured symptom summary | `200` | Incomplete `409`; absent/expired concealed `404`; bad capability `401` |
| `POST /api/v1/pre-triage/sessions/{id}/claim` | Bearer + anonymous token | Primary patient of authenticated account | Attach anonymous episode | `200` claimed episode | Expired/invalid `401/404`; claimed by another patient `409`; repeat by same patient idempotent |

The result contract contains primary symptom, duration, intensity, controlled additional symptoms, completion timestamp, exact definition/questionnaire version, and content provenance. It must not contain urgency, disposition, red-flag output, emergency recommendation, diagnosis, prescription, treatment recommendation, or disease probability. A neutral continuation message may point to a non-clinical next Beeexy experience such as finding a doctor.

## 7. Application / Use Cases

- `StartPreTriage`, `InterpretClinicalInput`, `ClassifyClinicalIntent`, `ValidateClinicalAiOutput`, `ExtractStructuredSymptoms`, `SubmitTriageAnswers`, `ResolveNextQuestion`, `CheckDemoQuestionnaireCompleteness`, `CompletePreTriage`, `GetPreTriageResult`, `ClaimAnonymousPreTriage`, `ExpireAnonymousPreTriage`, and `ProjectCompletedPreTriageEpisode`.
- Provider-neutral boundaries include `IClinicalAiProvider`, `ISymptomExtractor`, `IClinicalIntentClassifier`, `IClinicalAiOutputValidator`, and `IClinicalSafetyPolicy`, or a smaller equivalent separation preserving the same authority boundaries.
- Prefer schema-constrained structured AI output. Low-confidence, ambiguous, invalid, conflicting, or unsupported extraction produces clarification and cannot silently create answers or advance questionnaire state.
- The answer workflow accepts explicit structured input without an AI provider. Valid extraction may populate duration, intensity, and controlled additional symptoms together; deterministic progression skips fields already validly answered.
- `IDemoQuestionnaireCompletenessPolicy` and a canonical summary builder, or equivalents, operate only on the exact pinned simplified questionnaire version. No `IClinicalRuleEngine` is required for the demo.
- `ISymptomNormalizer` remains a future-facing interface for uncoded text and eventual SNOMED integration; it cannot make an unsupported pathway supported.

## 8. Authentication and Authorization

- Anonymous flow uses a cryptographically random capability returned once and stored hashed.
- IDs without the capability do not grant anonymous access.
- Authenticated patient selection uses owner/active-manager authorization.
- Claim requires both bearer authentication and anonymous capability.
- Every start, answer, completion, result, and claim operation rechecks the relevant capability or patient authorization; AI-derived identifiers never confer access.
- Existing Phase 4.4 invalid-authentication non-downgrade, primary-patient defaulting, managed-patient authorization, exact-version pinning, and concealed-IDOR behavior remain unchanged when additional demo pathways are registered.

## 9. Security and Privacy

- Capability is never in logs and preferably sent in a dedicated header.
- Anonymous workflow data contains only the minimum assessment data required for server-side execution; if unclaimed, temporary data and any completed anonymous episode expire after 24 hours.
- Authenticated abandonment creates no permanent clinical record, and resume after abandonment is not supported in the MVP.
- Clinical History projection accepts only completed `PreTriageEpisode` records, never a `PreTriageSession` or its temporary answers.
- Application-enforced intent/safety outcomes include at least `PRE_TRIAGE_INPUT`, `OUT_OF_SCOPE`, `PRESCRIPTION_REQUEST`, `UNSUPPORTED_CLINICAL_REQUEST`, `POTENTIAL_PROMPT_INJECTION`, and `AMBIGUOUS`. Critical restrictions cannot rely only on an LLM system prompt.
- AI output is schema-, enum-, confidence-, pathway-, conflict-, and safety-validated before use. AI-supplied urgency, disposition, diagnosis, thresholds, red flags, prescriptions, treatment recommendations, or probabilities are rejected or ignored.
- Prompt injection cannot disable application safety or deterministic questionnaire state. Optional neutral rendering cannot add clinical conclusions or alter canonical structured fields.
- Provider requests, responses, errors, and logs exclude capability/bearer tokens, unnecessary demographics, raw health payloads, and prompts containing more clinical data than needed. Provider failure does not leak secrets or internals.
- Unknown or unsupported symptoms are never silently mapped to a supported demo pathway. `OTHER_SYMPTOMS` remains unsupported and is not a clinical catch-all.
- Completed records cannot be overwritten.

## 10. External Integrations

- **IMPLEMENT FOR DEMO:** deterministic internal simplified-definition provider, questionnaire progression/completeness, and the existing provider-independent clinical-AI abstraction, validation, and safety boundary.
- **IMPLEMENT IF CONFIGURED / OPTIONAL:** a concrete AI provider adapter selected through Infrastructure configuration; the plan is not bound to NVIDIA NIM, Ollama, Gemini, OpenAI, or another vendor.
- **INTERFACE/PLACEHOLDER:** `ISnomedTerminologyService`.
- **POST-DEMO:** clinical urgency/rule providers, full symptom protocols, autonomous agents, and dynamic AI questioning beyond the controlled questionnaire.

## 11. FHIR Impact

Internal answers and the completed neutral assessment retain stable identifiers and version provenance that may later support `QuestionnaireResponse` and `Provenance` mapping. Do not emit a `RiskAssessment` or imply risk/urgency from this demo summary. No FHIR is generated in Phase 4; exact Phase 6 mapping remains deferred.

## 12. Tests

- End-to-end anonymous, authenticated-primary, and authorized-managed flows for `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`.
- Explicit structured flow remains usable during AI provider outage.
- Duration extraction and unit validation; intensity integer/range validation from 1 through 10; controlled additional-symptom selection restricted to `NAUSEA`, `DIARRHEA`, and `FEVER`; deterministic exclusion of `FEVER` as an additional option when the primary pathway is `FEVER`; multi-field extraction from one message; and avoidance of questions already answered by reliably validated values.
- Structured-output schema/enum/confidence validation; malformed, ambiguous, conflicting, unsupported, diagnosis/urgency/disposition/probability-bearing, and adversarial outputs cannot become authoritative answers.
- Intent/safety fixtures for out-of-scope input, prescription requests, unsupported clinical requests, prompt injection, and ambiguous input.
- Provider-unavailable behavior preserves explicit deterministic intake/completion and canonical result wording; no unsafe guessed extraction is accepted.
- Registry tests prove exactly `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` are demo-supported; `CHEST_PAIN`, `OTHER_SYMPTOMS`, and every other preexisting recognized-but-unsupported pathway remain safe; unknown pathways remain distinct; and no package is borrowed across pathways.
- Deterministic questionnaire progression and completeness tests cover each simplified package, already-answered skipping, stale/concurrent submissions, and completion refusal until the minimum fields are present.
- Negative contract/reflection tests prove no current demo response or completion path generates urgency, disposition, red-flag output, emergency recommendation, diagnosis, prescription, treatment recommendation, or numeric disease probability.
- Temporary sessions/answers never appear in Clinical History before completion; abandoned anonymous and authenticated sessions create no permanent episode or history record.
- Authenticated abandonment cannot resume in the MVP.
- Token entropy/hash/access tests; anonymous completed-episode claim within 24 hours; unclaimed temporary/completed data expiry/deletion at 24 hours.
- Concurrent completion and claim idempotency; cross-account claim conflict.
- Adversarial neutral rendering cannot alter canonical fields or add urgency, disposition, diagnosis, prescription, treatment, emergency advice, or probability.
- Clinical History projection consumes only completed immutable episodes and is exactly-once/idempotent.
- Capability security, bearer non-downgrade, IDOR, owner/manager authorization, migration, database constraints, OpenAPI, concurrency, and privacy-safe logging regressions remain green.
- Mandatory endpoint test matrix for all five endpoints.

## 13. Acceptance Criteria

- Anonymous users complete/view results without an account and may securely claim within 24 hours.
- Authenticated users assess only authorized patients.
- `PreTriageSession` remains temporary workflow state; only successful completion creates a permanent `PreTriageEpisode` + `ClinicalAssessment` and projects it into Clinical History.
- Abandonment creates no Clinical History record; authenticated abandoned flows cannot resume in the MVP; unclaimed anonymous data expires after 24 hours.
- `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` pass the simplified vertical slice; `ABDOMINAL_PAIN` is presented as "Stomach pain" without changing its stable pathway code.
- Each supported pathway collects primary symptom, duration, intensity 1-10, and controlled additional symptoms through an exact immutable simplified questionnaire version. The complete controlled option catalog is exactly `NAUSEA`, `DIARRHEA`, and `FEVER`, with no fourth option.
- The `FEVER` package deterministically excludes `FEVER` from applicable additional-symptom choices when `FEVER` is already the primary symptom; it must not ask or persist redundant fever-as-additional-symptom data.
- Natural language may populate multiple valid fields and already answered questions are skipped; explicit structured entry works without AI.
- Completion requires only the minimum demo questionnaire and creates an immutable neutral structured summary, not a clinical risk assessment.
- No current demo execution or response produces urgency, disposition, red-flag escalation, emergency recommendation, diagnosis, prescription, treatment recommendation, or disease probability.
- Existing and new definition versions coexist without mutation; provenance truthfully distinguishes the detailed provisional abdominal artifact from simplified non-clinical demo content.
- AI provider outage cannot compromise deterministic questionnaire use, completion, or canonical neutral result delivery.
- Recognized-but-unselected and unknown pathways fail safely without borrowing definitions or being mapped to abdominal pain.
- Migrations and all tests pass.

## 14. Dependencies

- Phases 1-3 (Phase 3 only for dependent assessments).
- Completed Phase 4.1-4.4 foundations.
- Andrea's demo direction in this plan is authoritative for current Phase 4 execution.
- Andrea-confirmed demo configuration: supported pathways are `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`; the controlled additional-symptom catalog is exactly `NAUSEA`, `DIARRHEA`, and `FEVER`; `FEVER` is excluded when it duplicates the primary pathway.
- The frontend flow is a UX reference only; it is not authority to support all five choices or to create clinical protocols.
- `beeexy-phase4-provisional-clinical-definitions.md` remains provenance for the stored detailed abdominal package and future clinical work, but its red flags, urgency rules, dispositions, and emergency recommendations are not dependencies of current demo execution.

## 15. Deferred / Technical TBD Items

- The demo pathway and additional-symptom product decisions are complete: `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` are supported; `NAUSEA`, `DIARRHEA`, and `FEVER` are the complete additional-symptom catalog; no fourth option may be invented.
- Truthful demo provenance is resolved: simplified packages persist `PRODUCT_DEMO_DEFINED`, `NOT_APPLICABLE`, and `NOT_CLINICALLY_APPROVED` and remain distinct from reference-platform-derived clinical content.
- **TBD — neutral continuation wording:** optional product-approved message directing the user to the next Beeexy experience without clinical recommendation.
- Concrete AI provider/vendor selection, production terminology normalization, localization, and future dynamic questioning.
- All urgency, disposition, red-flag execution, emergency recommendations, detailed protocols, formal clinical approval, and clinical FHIR risk mapping are deferred to **POST-DEMO / FUTURE CLINICAL PRE-TRIAGE**.

## Implementation Readiness

### Completed and still valid

Phase 4.1 through Phase 4.4 remain complete. Their persistence foundations, immutable versioning, provider-independent AI guardrails, anonymous capability security, 24-hour expiry metadata, authenticated authorization, IDOR protection, and exact-definition pinning are retained.

### Ready for Phase 4.6 intake implementation

Phase 4.5 is complete. The active demo-definition boundary supports `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` using the exact `NAUSEA`, `DIARRHEA`, and `FEVER` option catalog and deterministic primary-symptom exclusion. No remaining product decision blocks Phase 4.6. Its intake implementation must consume the exact pinned simplified package schemas rather than inventing content or applying the stored detailed abdominal protocol.

### Small additive compatibility work

Phase 4.5 added profile-aware provider/registry resolution and one narrow provenance check-constraint migration; Phase 4.4's handler, capability, authorization, lifecycle, and persistence design did not change. Phase 4.7 must allow a neutral `ClinicalAssessment` without a fabricated urgency, which currently requires a small backward-compatible domain/persistence adjustment and likely a narrow migration. All other existing Phase 4.1 clinical structures remain intact and dormant.

### Must remain unavailable

`CHEST_PAIN` and `OTHER_SYMPTOMS` remain recognized but unsupported for the demo and receive no protocol. Existing `RESPIRATORY_SYMPTOMS` and `BACK_PAIN` recognition remains unchanged and unsupported. Detailed clinical execution is unavailable for every pathway, including all three supported demo pathways: stored detailed abdominal rules remain versioned but are not invoked.

## Phase 4.1 — Pre-Triage Domain + Persistence Foundation

**Phase 4.1 status:** COMPLETE (2026-08-21)
**Phase 4.1 implementation:** Added the clinically content-neutral domain and PostgreSQL persistence foundation for anonymous and authenticated Pre-Triage. `PreTriageSession` is an `Active -> Completed` temporary-workflow aggregate with nullable patient ownership, required expiry, hashed anonymous capability metadata, and temporary answers/symptoms. Completion transfers those child rows to an immutable `PreTriageEpisode`, which records exact questionnaire/rule-set versions and owns one immutable `ClinicalAssessment` result plus findings. Anonymous episodes retain nullable patient ownership and unclaimed expiry metadata; the only permanent-record mutation is a one-time claim that is idempotent for the same patient and conflicts for another. Approved questionnaire/rule packages have stable code/version identities, content hashes, source/import/approval/activation provenance, and no seeded content. The `triage` schema contains the nine planned tables with UUID keys, exact-version composite FKs, unique session-to-episode/assessment/capability/code-version constraints, lifecycle/ownership checks, expiry and patient retrieval indexes, temporary-child cascades only from sessions, and `RESTRICT` on patient and permanent clinical relationships. Migration `20260821203135_Phase41PreTriagePersistenceFoundation` adds the complete schema without changing Phase 1-3 data. No clinical questionnaire, answer option, urgency vocabulary, red flag, score, threshold, probability, SNOMED call, FHIR resource, application use case, cleanup worker, Phase 4 HTTP endpoint, or Phase 4.2 behavior was introduced.
**Phase 4.1 verification:** Restore succeeded; the Debug solution build completed with 0 warnings and 0 errors. The full suite passed 501 tests: 276 unit and 225 real-PostgreSQL integration, with 0 failed and 0 skipped. Focused Phase 4.1 coverage passed 15 domain and 10 persistence cases; nine focused migration/FK cases passed, including clean full-chain apply and Phase 4.1 rollback/reapply. PostgreSQL enforces nullable anonymous ownership, unique capability hashes, one episode per session, exact version provenance, safe patient/permanent-record delete behavior, temporary-row cleanup boundaries, and claim preservation. EF reported no pending model changes; formatting verification and `git diff --check` passed. Medical-team-approved questionnaire content, urgency codes, red flags, deterministic rules, thresholds, and messages remain the explicit dependency for Phase 4.2.

Phase 4.1 remains the authoritative, clinically content-neutral technical foundation and is not redesigned for AI. If its current provenance fields cannot represent provisional review state, Phase 4.2 may add narrowly scoped source/review/approval metadata. Temporary AI-extraction metadata may remain outside the core Domain.

**Demo-scope addendum:** Preserve all Phase 4.1 entities and migration history. During neutral demo completion, add only the smallest backward-compatible assessment adjustment required to avoid fabricating a required urgency value; do not remove future-clinical fields or weaken existing lifecycle, immutability, ownership, or versioning constraints.

## Phase 4.2 — Clinical Definition Packages + Supported Pathway Registry

**Phase 4.2 status:** COMPLETE (2026-08-21)
**Phase 4.2 implementation:** Added an immutable, versioned `ABDOMINAL_PAIN` definition package (`2026.08.21-provisional.1`) derived only from `beeexy-phase4-provisional-clinical-definitions.md`: 41 typed questions, 14 deterministic branch definitions, 13 red flags, 10 explicit urgency-rule artifacts, the ordered five-level urgency vocabulary, five separate disposition/recommendation definitions, and source limitations. `ABDOMINAL_PAIN` is supported; `HEADACHE`, `CHEST_PAIN`, `FEVER`, `RESPIRATORY_SYMPTOMS`, `BACK_PAIN`, and `OTHER_SYMPTOMS` are recognized but unsupported, with unknown pathways remaining distinct. Package validation rejects broken question/rule references, invalid branch values, incompatible provenance, incorrect urgency ordering, and cross-pathway import. Canonical JSON hashes, deterministic identifiers, immutable same-version semantics, atomic/idempotent import, active/exact-version retrieval, and future-version coexistence are implemented. Both definition versions retain `REFERENCE_PLATFORM_DERIVED`, `PROVISIONAL`, and `PENDING_FORMAL_REVIEW`; provisional content has no approval timestamp and is never promoted in place. Migration `20260822035009_Phase42ClinicalDefinitionPackages` adds the narrowly required pathway, provenance/status, nullable approval, rule-package JSON, indexes, and database checks. No session execution, branch execution, rule evaluation, AI, endpoint, detailed non-abdominal package, probability, diagnosis, prescription, or inferred `CRITICAL` trigger was introduced.
**Phase 4.2 verification:** Restore succeeded; the final Debug solution build completed with 0 warnings and 0 errors. The full suite passed 518 tests: 290 unit and 228 real-PostgreSQL integration, with 0 failed and 0 skipped. Four focused persistence/migration cases passed, including clean full-chain application and Phase 4.2 rollback/reapply; the 14 focused package/registry unit cases also pass. PostgreSQL retains content status and immutable versions, the importer rejects same-version hash changes, and the provider verifies stored hashes before returning definitions. EF reported no pending model changes; formatting verification and `git diff --check` passed. The source package intentionally defines the `CRITICAL` vocabulary/disposition but no complete `CRITICAL` trigger set, so exhaustive critical rules and formal clinical approval remain deferred to a new reviewed version.

**Demo-scope addendum:** Phase 4.2 remains complete and its detailed abdominal package/migration are retained unchanged, but current demo execution does not consume that package's red flags, urgency rules, dispositions, or emergency recommendations. The completed Phase 4.5 additive follow-up supplies simplified immutable questionnaire versions for exactly `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`; includes only primary symptom, duration, intensity, and the controlled `NAUSEA`/`DIARRHEA`/`FEVER` additional-symptom catalog; deterministically excludes `FEVER` as an additional choice for the `FEVER` primary pathway; truthfully labels the packages non-clinical/demo content; and registers only those three pathways as demo-supported. No protocol exists for `CHEST_PAIN`, `OTHER_SYMPTOMS`, or any other unsupported pathway.

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

**Phase 4.3 status:** COMPLETE (2026-08-21)
**Phase 4.3 implementation:** Added a provider-independent, schema-versioned clinical interpretation boundary in Application with typed pathway, symptom, fact, known-answer, ambiguity, confidence, validation, safety, and provider-failure contracts. The stable safety categories are `PRE_TRIAGE_INPUT`, `OUT_OF_SCOPE`, `PRESCRIPTION_REQUEST`, `PROHIBITED_MEDICAL_ADVICE`, `POTENTIAL_PROMPT_INJECTION`, `UNSUPPORTED_CLINICAL_REQUEST`, and `AMBIGUOUS`. Deterministic input policy executes before any provider call and provider-reported safety classifications are re-enforced by Application. Candidate confidence uses explicit `SUFFICIENT`, `UNCERTAIN`, `LOW`, and `UNSPECIFIED` signals rather than an invented numeric threshold; Application alone assigns `ACCEPTED_CANDIDATE`, `NEEDS_CLARIFICATION`, `REJECTED`, or `UNSUPPORTED`. The validator resolves pathways and active packages exclusively through the Phase 4.2 registry/provider, validates all eight answer shapes and package-controlled choices/ranges/units, enforces request-scoped vocabulary and known-answer conflict detection, supports multiple symptom candidates, and rejects malformed schemas, invalid enums, unknown facts/pathways, cross-pathway candidates, and detected forbidden-authority output. `InterpretClinicalInput` returns safe categorical outcomes for unavailable, timeout, invalid, rejected, and unconfigured providers without fabricating facts. Infrastructure registers only a credential-free `UnavailableClinicalAiProvider` fallback; no production provider, vendor SDK/model/configuration, Domain dependency, prompt/response persistence, database change, migration, AI log payload, session mutation, questionnaire/branch execution, clinical-rule execution, urgency/disposition selection, diagnosis/probability/prescription authority, or Phase 4 endpoint was added.
**Phase 4.3 verification:** Restore succeeded and the final Debug solution build completed with 0 warnings and 0 errors. The full suite passed 559 tests: 328 unit and 231 real-PostgreSQL integration, with 0 failed and 0 skipped. Focused Phase 4.3 coverage passed 38 unit cases and 3 PostgreSQL cases. It verifies the required safety fixtures, provider-bypass resistance, reported-medication versus recommendation separation, all structured answer kinds, multiple symptoms, known/conflicting facts, confidence clarification, malformed/unknown/forbidden output, safe provider failures, and reflection-based exclusion of tokens, capabilities, urgency, disposition, diagnosis, probability, prescription, and treatment-plan fields. Real Phase 4.2 definitions accept valid abdominal candidates, reject unknown facts, and distinguish supported, recognized-but-unsupported, and unknown pathways without borrowing abdominal content; all cases preserve unchanged session, episode, and assessment counts. Existing OpenAPI regressions confirm no new endpoint. EF reported no pending model changes and no migration was created; no project dependency changed. Formatting verification and `git diff --check` passed. Phase 4.2 provenance remains `REFERENCE_PLATFORM_DERIVED`, `PROVISIONAL`, and `PENDING_FORMAL_REVIEW`.

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

**Phase 4.4 status:** COMPLETE (2026-08-22)
**Phase 4.4 implementation:** Added `POST /api/v1/pre-triage/sessions` and the `StartPreTriage` application use case for anonymous, authenticated-primary, and authorized managed-patient starts. The request accepts only an explicit pathway and optional authenticated patient UUID; missing, unknown, recognized-but-unsupported, unavailable-definition, extra-field, natural-language, and anonymous-patient requests fail safely without creating a session. Supplied invalid bearer credentials cannot downgrade to anonymous access. Authenticated patient resolution reuses the shared patient-authorization boundary and conceals inaccessible, revoked, reverse-direction, and missing patient records behind `404`. Successful starts pin the exact active Phase 4.2 questionnaire and rule-set identities and return their provisional provenance. Anonymous starts generate a 256-bit cryptographic capability, persist only its SHA-256 representation, provide constant-time verification, return the raw capability exactly once after successful persistence, and expire after 24 hours; logs omit both raw capability and hash. The endpoint creates only the temporary Phase 4.1 session: no episode, clinical assessment, finding, answer, rule evaluation, urgency, disposition, claim, resume, or AI-provider call occurs. Optional bearer OpenAPI metadata documents both anonymous and bearer modes. No schema change, migration, project dependency, production clinical package seeding, or endpoint beyond session start was introduced.
**Phase 4.4 verification:** Restore succeeded and the final Debug solution build completed with 0 warnings and 0 errors. The full suite passed 599 tests: 353 unit and 246 real-PostgreSQL integration, with 0 failed and 0 skipped. Focused Phase 4.4 coverage passed 40 cases: 25 application/capability unit cases and 15 API/persistence cases, including independent capability entropy, hash-only persistence, constant-time verification, exact 24-hour expiry, exact/future active-version pinning, response provenance, strict input handling, invalid-auth non-downgrade, primary/managed authorization, IDOR concealment, no permanent clinical records, and no sensitive capability logging or headers. The 82 focused Phase 4.1-4.3 regression cases passed, as did all 9 migration/rollback cases. EF reported no pending model changes and no migration was created; formatting and `git diff --check` passed. Answer submission, deterministic questionnaire progression, neutral completion/result retrieval, claiming, cleanup, and history projection remain deferred to the revised demo phases; clinical rule execution is now post-demo.

**Demo-scope addendum:** Phase 4.4's endpoint, authorization, lifecycle, capability, expiry, and exact-version pinning remain unchanged. Phase 4.5 extended the registry/provider configuration and acceptance tests, so the existing generic start use case now creates sessions pinned to simplified packages for `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`. `CHEST_PAIN`, `OTHER_SYMPTOMS`, and every other recognized-but-unsupported pathway continue to return `422`.

**Objective:** Securely start anonymous, authenticated-primary, or authenticated-managed-patient Pre-Triage sessions against a supported pathway.

**Exact scope:** Implement `StartPreTriage`; resolve an explicit pathway; select the exact active questionnaire package; create an expiring Phase 4.1 session; generate a cryptographically random anonymous capability returned once and persisted only as a hash. Reject recognized-but-unsupported pathways without creating an executable session.

**Main components:** Start command/validator/handler, session repository/unit of work, `IClinicalPathwayRegistry`, definition provider, capability generator/hasher/verifier, and current-account/patient authorization.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions`.

**AI involvement:** None. Phase 4.4 accepts an explicit supported pathway and does not require a provider.

**Clinical-definition dependencies:** Phase 4.2 provider/import/versioning foundations plus the completed Phase 4.5 simplified `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` demo versions. The lookup boundary is unchanged and session start selects only the simplified demo package profile.

**Security/safety requirements:** UUID alone grants no access; capability entropy and constant-time hash verification meet the repository security standard; tokens never enter logs; authenticated selection requires owner/active-manager authorization; unsupported input cannot be silently mapped to abdominal pain.

**Tests and acceptance criteria:** Cover anonymous, primary-patient, and managed-patient starts; one-time capability return/hash persistence/entropy; IDOR and inactive-manager denial; exact definition selection; strict rejection of natural-language/unsupported request fields; recognized-but-unsupported/unknown/unavailable-definition rejection; and invalid supplied authentication without anonymous downgrade. Apply the mandatory endpoint matrix.

**Explicitly out of scope:** Answer submission, branch execution, urgency, completion, claim, and resume.

**Dependencies on previous subphases:** Phase 4.2 and Phase 4.3; Phase 3 only for managed-patient authorization.

## Phase 4.5 — Confirmed Demo Pathways + Simplified Definition Packages

**Phase 4.5 status:** COMPLETE (2026-08-22)
**Phase 4.5 implementation:** Added immutable `2026.08.22-demo.1` simplified definition packages for exactly `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`, each with four controlled fields: pinned primary symptom, duration, intensity integer 1-10, and additional symptoms. A typed global catalog contains exactly `NAUSEA`, `DIARRHEA`, and `FEVER`; package applicability retains all three for headache and abdominal pain and deterministically limits fever to `NAUSEA` and `DIARRHEA`. `ABDOMINAL_PAIN` retains its stable code and stores the display label "Stomach pain." Package metadata explicitly defines required-answer/progression order and permits an answered empty additional-symptom selection. Added `SIMPLIFIED_DEMO_INTAKE` versus `DETAILED_CLINICAL` package profiles, validation that rejects clinical branches/rules/red flags/urgencies/dispositions from demo packages, canonical profile/metadata serialization with backward-compatible omission for existing detailed content, deterministic IDs/hashes, idempotent import, and profile-aware active-definition retrieval. Demo provenance persists as `PRODUCT_DEMO_DEFINED`, `NOT_APPLICABLE`, and `NOT_CLINICALLY_APPROVED`. Migration `20260822061610_Phase45ConfirmedDemoPackages` only widens the existing provenance check constraints and preserves rollback/reapply semantics. The supported registry is now exactly the confirmed three pathways; `CHEST_PAIN`, `OTHER_SYMPTOMS`, `RESPIRATORY_SYMPTOMS`, and `BACK_PAIN` remain recognized but unsupported. The existing detailed abdominal version and Phase 4.1-4.4 persistence/security behavior remain intact and coexist with the simplified abdominal version. No answer endpoint, AI runtime extraction, progression execution, completion/result, urgency, disposition, red-flag execution, diagnosis, prescription, probability, treatment, or unsupported-pathway package was added.
**Phase 4.5 verification:** The final Debug solution test run passed 616 tests: 367 unit and 249 real-PostgreSQL integration, with 0 failed and 0 skipped. Sixteen focused package cases verify exact pathways, fields, display labels, typed three-value catalog, no fourth option, FEVER exclusion, completeness metadata, deterministic identity/hash behavior, detailed-package compatibility, unsupported-package rejection, and absence/rejection of clinical authority artifacts. Thirty-two targeted definition, AI-boundary, session-start, migration, and rollback regression cases passed before the full suite. PostgreSQL verification covers truthful provenance persistence, three-package idempotent import, coexistence and exact retrieval of the detailed abdominal package, profile-aware active resolution, clean full-chain migration, and Phase 4.5 rollback/reapply. Existing anonymous/authenticated capability, authorization, IDOR, expiry, hash-only capability persistence, version pinning, failure, and logging tests remain green.

**Objective:** Materialize the confirmed versioned, non-clinical questionnaire definitions that the controlled demo will execute before intake code is built.

**Exact scope:** Import new immutable simplified packages for exactly `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`. `ABDOMINAL_PAIN` retains its stable code and uses the frontend display label "Stomach pain." Each package contains only primary symptom identity, duration value/unit, intensity integer 1-10, controlled additional-symptom options from the complete catalog `NAUSEA`, `DIARRHEA`, and `FEVER`, deterministic applicability/display/progression metadata, and minimum-completeness metadata. The `FEVER` package excludes `FEVER` from applicable additional choices because it duplicates the primary symptom. Register only the three confirmed pathways as demo-supported. Retain the detailed abdominal package and all other recognized pathway codes unchanged, but do not execute their detailed clinical artifacts.

**Main components:** Confirmed demo-pathway configuration, simplified package schema/profile, the exact three-code additional-symptom catalog, deterministic primary-symptom applicability filtering, canonical hash/version import, truthful demo/non-clinical provenance status, registry updates, and package validation that distinguishes a demo intake package from a future clinical rule package.

**Endpoints involved:** No new endpoint. `POST /api/v1/pre-triage/sessions` gains `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` through the existing registry/provider boundary; its request, security, and lifecycle contract remain unchanged.

**AI involvement:** None.

**Clinical-definition dependencies:** Andrea's confirmed pathway and option decisions recorded above. The existing provisional abdominal package may inform identifiers but is not the executed demo questionnaire and must not lend urgency/red-flag semantics to the simplified version.

**Security/safety requirements:** Never support a pathway without its own exact simplified package; never borrow abdominal definitions; never treat `OTHER_SYMPTOMS` as an automatic catch-all; never mark demo definitions clinically approved; preserve immutable same-version/hash behavior and exact-version activation.

**Tests and acceptance criteria:** Prove exactly `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` resolve as demo-supported; `CHEST_PAIN`, `OTHER_SYMPTOMS`, and every other recognized-but-unsupported pathway remain unsupported; unknown remains distinct; the option catalog is exactly `NAUSEA`, `DIARRHEA`, and `FEVER` with no fourth value; `FEVER` is not applicable as an additional symptom for the `FEVER` primary pathway; each simplified package contains only allowed demo fields/options and no executable urgency, disposition, red-flag, diagnosis, probability, prescription, or treatment content; imports are immutable/idempotent; versions coexist; Phase 4.4 starts and pins each supported package; all existing capability/authentication/authorization behavior remains green.

**Explicitly out of scope:** Any fourth additional-symptom option, packages for `CHEST_PAIN` or `OTHER_SYMPTOMS`, detailed symptom protocols, answer submission, AI extraction, completion, clinical rules, and deleting/mutating the detailed abdominal package.

**Dependencies on previous subphases:** Phase 4.2 and Phase 4.4. Completed; no remaining product decision blocks Phase 4.6.

## Phase 4.6 — Guarded AI-Assisted Intake + Deterministic Questionnaire Progression

**Phase 4.6 status:** COMPLETE (2026-08-22)
**Phase 4.6 implementation:** Added `POST /api/v1/pre-triage/sessions/{id}/answers` with mutually exclusive structured and natural-language request modes and a provider-neutral response containing accepted answer categories, deterministic progression, the next package-defined question/options, ready-to-complete state, and safe clarification/provider-unavailable categories. Structured submissions validate positive duration values with only `MINUTES`, `HOURS`, `DAYS`, `WEEKS`, or `MONTHS`, integer intensity 1-10, and exact pinned-package additional choices. Natural-language submissions reuse the Phase 4.3 safety/provider boundary, validate typed candidates against the session's exact immutable Phase 4.5 package, accept several sufficient facts atomically, retain confidently accepted facts when another candidate needs clarification, and never let an extracted pathway change the pinned pathway. Package metadata alone determines `DURATION -> INTENSITY -> ADDITIONAL_SYMPTOMS -> READY_TO_COMPLETE`, skips existing answers, treats an empty controlled additional selection as answered, and excludes/rejects redundant `FEVER` for a primary `FEVER` session while retaining `FEVER` for `HEADACHE` and `ABDOMINAL_PAIN`. Every request reauthorizes the anonymous capability or authenticated primary/active-manager relationship; an invalid Bearer cannot downgrade, UUID alone is insufficient, and inaccessible/revoked/cross-account sessions remain concealed. PostgreSQL row locking plus structural JSON comparison makes exact repeats idempotent and rejects concurrent/stale differing values without overwrite. Provider timeout/unavailability/configuration failure writes no fabricated facts, while structured intake remains available. Audit logs contain only session/category/count/progression metadata, not capabilities, tokens, narratives, prompts, provider output, or clinical values. Phase 4.6 writes only temporary `TriageAnswer` rows and creates no episode, assessment, finding, clinical rule result, urgency, disposition, red flag, emergency advice, diagnosis, prescription, treatment, or probability. Existing Phase 4.1-4.5 schema was sufficient, so no migration or package change was required.
**Phase 4.6 verification:** Restore succeeded; the final Debug solution build completed with 0 warnings and 0 errors. The full suite passed 673 tests: 412 unit and 261 real-PostgreSQL integration, with 0 failed and 0 skipped. Focused Phase 4.6 coverage passed 45 unit and 12 endpoint/PostgreSQL cases. It covers all three demo pathways, exact duration/intensity/additional validation, no fourth option, FEVER applicability, structured provider-independent fallback, multi-field and individual natural-language extraction, pinned-pathway confirmation/mismatch, confidence/ambiguity/malformed/forbidden output, deterministic safety classifications, provider failure categories, already-answered skipping, ready-to-complete without completion, completed/expired/cross-version rejection, structural-JSON repeat idempotency, concurrent same/different submissions, no duplicate answers, anonymous capability matrices, primary/managed authorization, revoked/IDOR denial, invalid-auth non-downgrade, privacy-safe logging, absence of permanent records/clinical authority, and the documented OpenAPI contract/status/security matrix. The clean full migration chain and rollback/reapply regressions passed, EF reported no pending model changes, formatting completed, and `git diff --check` passed. Phase 4.7 episode completion, neutral assessment, and result retrieval remain unimplemented and are the next increment.

**Objective:** Collect the minimum demo symptom dataset through explicit structured answers or validated natural-language extraction while Application code retains questionnaire authority.

**Exact scope:** Implement `InterpretClinicalInput`, `ClassifyClinicalIntent`, `ExtractStructuredSymptoms`, `ValidateExtractedFacts`, `SubmitTriageAnswers`, and `ResolveNextQuestion` as one cohesive answer workflow. Against the session's exact Phase 4.5 questionnaire version, accept duration value/unit, intensity 1-10, and additional-symptom selections restricted to `NAUSEA`, `DIARRHEA`, and `FEVER`. A message such as "I've had a stomachache since yesterday, about 6 out of 10" may propose pathway, duration, and intensity together; the pathway must match the already pinned session and only valid package-known values persist. Deterministically skip questions already answered, exclude an additional-symptom choice equal to the primary pathway (`FEVER` for a `FEVER` session), and return the next missing required field or a ready-to-complete state.

**Main components:** Phase 4.3 safety/validation interfaces, schema-constrained extraction DTOs, answer command/validator/handler, validated-candidate-to-answer mapper, package-specific duration/intensity/option validators, deterministic primary/additional-symptom applicability filter, deterministic progression/completeness preview, clarification result, optimistic concurrency/idempotency handling, and privacy-minimized temporary extraction provenance if needed.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions/{id}/answers`. Natural-language pathway-first session creation is not required by the confirmed demo and remains optional future scope; the current demo selects the symptom explicitly before using natural language for remaining fields.

**AI involvement:** Optional interpretation, equivalent-wording classification, and multi-field extraction only. Explicit structured input must work when no provider is configured or the provider is unavailable. AI cannot edit the pinned pathway, questionnaire state, controlled options, progression, or completeness rules.

**Clinical-definition dependencies:** Only the exact simplified package selected in Phase 4.5; no detailed abdominal branches, red flags, urgency rules, or dispositions.

**Security/safety requirements:** Reauthorize bearer/capability on every request; validate schema, confidence, pathway, ranges, units, controlled values, known-answer conflicts, and session version before persistence; reject unsupported symptom remapping and forbidden-authority fields; return clarification for malformed, ambiguous, low-confidence, or conflicting extraction; prevent stale/concurrent writes; minimize provider payloads and logs.

**Tests and acceptance criteria:** Cover explicit and natural-language duration, intensity boundaries, exact acceptance of `NAUSEA`, `DIARRHEA`, and `FEVER`, rejection of every unlisted/fourth option, deterministic omission/rejection of redundant `FEVER` additional-symptom data for a `FEVER` primary session, multi-field extraction, equivalent wording, already-answered skipping, next-missing-field progression, pathway mismatch, unknown/unsupported symptoms, invalid/cross-version options, ambiguity/conflict clarification, out-of-scope/prescription/injection fixtures, provider timeout/unavailability, stale/concurrent submissions, completed/expired state, anonymous/authenticated/managed access, capability security, and IDOR. Prove no urgency, disposition, red flag, diagnosis, prescription, treatment, or probability reaches workflow state.

**Explicitly out of scope:** Permanent completion, clinical urgency/rules, dynamic model-authored questions, unsupported pathway protocols, result retrieval, and history projection.

**Dependencies on previous subphases:** Phase 4.3, Phase 4.4, and completed Phase 4.5 simplified definitions.

## Phase 4.7 — Neutral Completion + Secure Result Retrieval

**Phase 4.7 status:** COMPLETE (2026-08-22)
**Phase 4.7 implementation:** Added `POST /api/v1/pre-triage/sessions/{id}/complete` and `GET /api/v1/pre-triage/sessions/{id}/result` for anonymous capability, authenticated-primary, and currently authorized managed-patient access. Completion reauthorizes inside a PostgreSQL transaction, locks the session row, and for managed patients takes the established Phase 3 shared active-relationship lock. It reloads only the session-pinned `SIMPLIFIED_DEMO_INTAKE` package, strictly revalidates the exact duration, intensity, and answered additional-symptom JSON representations against package metadata, rejects extra/missing/corrupt/cross-version state, and materializes controlled primary/additional symptoms before promoting all validated temporary answers into exactly one immutable episode. `FEVER` as the primary symptom excludes and rejects redundant additional `FEVER`; `HEADACHE + FEVER` and `ABDOMINAL_PAIN + FEVER` remain valid. A dedicated neutral-assessment factory creates exactly one assessment with null urgency, no result-message authority, and zero findings. Migration `20260822163355_Phase47NeutralClinicalAssessment` narrowly makes `triage.clinical_assessments.urgency_code` nullable and adjusts only its nonblank check while preserving urgency-bearing creation and historical values. Existing one-session/one-episode and one-episode/one-assessment constraints plus the session row lock make completion concurrency-safe; the first success returns `201`, authorized repeats return the identical stable result with `200`, and transaction failure rolls back the session transition, episode, assessment, promoted answers, and controlled symptoms. Result retrieval is no-tracking/read-only and returns session/episode IDs, primary symptom code/display, duration, intensity, controlled additional symptoms, stable completion time, exact questionnaire/package code and version, and truthful demo source/review/approval provenance. It omits urgency, disposition, red flags, emergency advice, diagnosis, prescription, treatment, probability, and AI/provider/model/confidence/raw fields. No clinical rules or detailed abdominal artifacts execute, no optional AI renderer was added, and no Phase 4.8 claim or later behavior was implemented.
**Phase 4.7 verification:** The final Debug solution build completed with 0 warnings and 0 errors. Six focused unit cases and ten focused real-PostgreSQL/OpenAPI/migration cases passed, covering all three pathways, strict completeness/corrupt JSON, neutral construction, FEVER exclusion, first/repeat/GET identity, concurrent completion, capability and invalid-bearer handling, primary/managed/revoked/IDOR authorization, post-completion answer immutability, forced assessment-insert rollback, exact neutral OpenAPI schema, clean full-chain migration, historical urgency preservation, and Phase 4.7 rollback/reapply. The complete suites passed 688 tests: 418 unit and 270 real-PostgreSQL integration, with 0 failed and 0 skipped. EF reported no pending model changes; formatting and `git diff --check` passed. Phase 4.8 anonymous claim, Phase 4.9 cleanup, history projection, FHIR, and every clinical-rule/authority feature remain deferred.

**Objective:** Atomically convert a complete temporary demo workflow into an immutable structured symptom-intake episode and securely return a neutral summary.

**Exact scope:** Implement `CheckDemoQuestionnaireCompleteness`, `CompletePreTriage`, and `GetPreTriageResult`. Completion validates only the exact pinned simplified questionnaire's required symptom, duration, intensity, and controlled additional-symptom fields; applies the primary/additional-symptom applicability rule so `FEVER` is neither required nor retained as an additional symptom for a `FEVER` primary episode; creates one immutable `PreTriageEpisode`; creates a neutral `ClinicalAssessment` marker/summary without executing a clinical rule engine; transfers validated answers/symptoms; freezes definition/provenance references; and commits atomically. Result retrieval returns only the canonical structured summary, completion timestamp, exact versions, and provenance. An optional guarded renderer may add neutral patient-friendly wording or a non-clinical next-step message without changing canonical fields.

**Main components:** Completeness policy, completion command/handler, neutral assessment factory, smallest backward-compatible nullable-urgency persistence adjustment, session/episode/assessment repositories, transaction/unit of work, concurrency/idempotency policy, canonical neutral result mapper/query, authorization, and optional Phase 4.3 renderer/validator with deterministic fallback.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions/{id}/complete` and `GET /api/v1/pre-triage/sessions/{id}/result`.

**AI involvement:** None for completeness, persistence, or canonical summary. Optional wording only after the immutable result exists; provider failure returns deterministic neutral wording.

**Clinical-definition dependencies:** Exact simplified questionnaire/package version and truthful demo provenance from Phase 4.5. Existing detailed rule-set references may be preserved for schema/version compatibility but no rule, red flag, urgency, disposition, or emergency message executes.

**Security/safety requirements:** Reauthorize every request; reject incomplete, expired, inaccessible, unsupported, or version-inconsistent sessions; guarantee rollback on failure; prevent partial/duplicate episodes and assessments; make repeat/concurrent completion safe; conceal IDOR; preserve immutability; never use a fake urgency sentinel; never expose urgency, disposition, red flags, diagnosis, prescription, treatment, probability, provider metadata, or raw narrative.

**Tests and acceptance criteria:** Cover successful anonymous/authenticated/managed completion and retrieval for every demo pathway; minimum-completeness failures; exact answers/version/provenance; neutral nullable urgency representation; no clinical findings or rule execution; rollback; concurrent/repeat completion; one session-to-episode/assessment; immutability; bad capability; IDOR; incomplete/expired/absent result; provider/rendering failure; adversarial renderer; and mandatory endpoint matrices. Assert response/schema/domain state contain no generated urgency, disposition, red-flag escalation, emergency recommendation, diagnosis, prescription, treatment, or probability.

**Explicitly out of scope:** Clinical rule evaluation, clinical recommendation, claim, cleanup, history projection, amendments, and FHIR generation.

**Dependencies on previous subphases:** Phase 4.6 and the Phase 4.1 neutral-assessment compatibility adjustment described above.

## Phase 4.8 — Anonymous Episode Claim

**Phase 4.8 status:** COMPLETE (2026-08-22)
**Phase 4.8 implementation:** Added authenticated `POST /api/v1/pre-triage/sessions/{id}/claim` with no request body, query parameters, or patient selector. The endpoint requires both a valid Bearer identity and the original `X-Pre-Triage-Capability`; the existing JWT middleware and current-account resolver enforce active-account and exactly-one-primary-profile invariants, while the established constant-time capability verifier checks the hash-only anonymous credential. The target is derived exclusively as the authenticated account's primary `PatientProfile`; managed/dependent and arbitrary-patient claim are unavailable. A dedicated PostgreSQL repository opens one transaction, locks the anonymous session row with `FOR UPDATE`, loads the existing episode and neutral assessment graph, validates completed anonymous lifecycle, exact session/episode expiry and frozen version relationships, null urgency, and zero findings, then calls the existing Phase 4.1 `PreTriageEpisode.Claim` transition and commits only the ownership and server-generated `ClaimedAt` mutation. It never creates or rewrites the episode, assessment, answers, symptoms, completion time, questionnaire/rule-set references, or provenance. First claim and same-primary repeats return the same minimal `200` response (`sessionId`, `episodeId`, `patientId`, `claimedAt`); repeats preserve the original timestamp and emit no duplicate transition audit, while another patient receives a privacy-safe `409` without ownership disclosure or transfer. The persisted anonymous expiry is enforced exactly for first claim (`now < expiry`), is never reset, and a successfully claimed episode remains permanent after that boundary. Targeted row locking serializes same-patient and competing-patient claims, and transaction failure rolls back both ownership and timestamp. Privacy-safe claim auditing contains only technical IDs and the transition timestamp and never capability/token/intake/result data. Phase 4.7 result authorization now recognizes claimed episode ownership through the existing patient-access model: authenticated authorized access remains available permanently, while anonymous capability result access retains its existing behavior only until the original expiry. No clinical package, questionnaire progression, completeness, AI provider, clinical rule, detailed abdominal artifact, urgency, disposition, red flag, finding, diagnosis, prescription, treatment, recommendation, or probability executes. The Phase 4.1 ownership, `claimed_at`, expiry index, checks, and foreign keys already fully supported the operation, so no migration or model change was added.
**Phase 4.8 verification:** Eight focused unit cases and eight focused real-PostgreSQL/OpenAPI cases passed. They cover existing-graph ownership attachment and neutral preservation, exact before/at/after expiry behavior, same-patient idempotency after expiry, sequential and concurrent same/different-patient outcomes, stable `ClaimedAt`, capability missing/wrong/random/cross-session matrices, UUID-only and bearer-only denial, invalid-bearer non-downgrade, malformed/signature/issuer/audience/expired JWT rejection, disabled account, missing-primary invariant, absent/incomplete/corrupt lifecycle states, selector rejection, immutable canonical result and all record/version/provenance identities, permanent authenticated result access, capability expiry, hash-only/log privacy, zero AI calls, zero clinical authority, and forced PostgreSQL update-trigger rollback. Thirteen migration regressions passed the clean full chain and rollback/reapply coverage; no Phase 4.8 migration exists because the Phase 4.1 schema is sufficient. The complete suites passed 703 tests: 426 unit and 277 real-PostgreSQL integration, with 0 failed and 0 skipped. The final Debug build completed with 0 warnings and 0 errors, EF reported no pending model changes, formatting and `git diff --check` passed, and no live AI credentials were required. Phase 4.9 anonymous expiry/abandonment cleanup, Clinical History projection, FHIR, managed-patient claim, transfer/unclaim, post-expiry recovery, and every clinical-rule/authority feature remain deferred.

**Objective:** Allow an authenticated primary patient to securely claim a completed anonymous demo episode within its retention window.

**Exact scope:** Implement `ClaimAnonymousPreTriage`; require both bearer authentication and the original anonymous capability; attach the episode to the current account's primary `PatientProfile`; preserve every answer, neutral assessment field, definition reference, and provenance value unchanged; make a same-patient repeat idempotent and a different-patient claim conflict.

**Main components:** Claim command/validator/handler, current primary-patient resolver, capability verifier, session/episode repositories, transaction/concurrency handling, and privacy-safe audit event.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions/{id}/claim`.

**AI involvement:** None.

**Clinical-definition dependencies:** None beyond preserving the completed episode's frozen references.

**Security/safety requirements:** Bearer alone and capability alone are each insufficient; never log capability material; prevent cross-account/cross-patient claim; enforce expiration; preserve immutable summary content; do not allow managed-patient claim without later explicit approval.

**Tests and acceptance criteria:** Cover claim before the exact 24-hour boundary, same-patient idempotent repeat, different-patient conflict, bearer/capability absence or mismatch, expired/absent resource, cross-account attempt, concurrent claim, unchanged answers/summary/provenance, and the mandatory endpoint matrix.

**Explicitly out of scope:** Managed-patient claim, episode edits, post-expiry recovery, and history UI.

**Dependencies on previous subphases:** Phase 4.7.

## Phase 4.9 — Expiry + Abandonment Cleanup

**Phase 4.9 status:** COMPLETE (2026-08-22)
**Phase 4.9 implementation:** Added the non-HTTP `ExpireAnonymousPreTriage` boundary, a directly callable `PreTriageCleanupService`, targeted PostgreSQL cleanup repository, aggregate privacy-safe telemetry, validated cleanup configuration, and a cancellation-aware non-overlapping `BackgroundService`. Every run freezes the injected server clock at PostgreSQL precision and applies the existing persisted boundary exactly: `now < expiresAt` remains ineligible and `now >= expiresAt` is eligible. Active anonymous sessions and expired authenticated abandoned sessions are physically deleted through the existing session cascades, removing only their temporary answers/symptoms and creating no episode, assessment, finding, history state, or replacement session. Completed anonymous episodes are physically removed only when the completed source session remains anonymous and the episode still has both null patient ownership and null `ClaimedAt`; cleanup explicitly deletes findings, assessment, episode-owned answers/symptoms, episode, then source session inside one transaction because the permanent graph intentionally uses `RESTRICT`. Successfully claimed episodes and completed authenticated primary/managed-patient episodes are positively excluded and preserved indefinitely, including far beyond the original anonymous expiry. Candidate discovery uses the existing session status/expiry and unclaimed-episode expiry indexes, deterministic expiry/UUID keyset ordering, configurable batches, and a bounded maximum number of batches per run; defaults are a 15-minute cadence, 100 candidates per batch, and 10 batches per run. Each candidate mutation locks the source session first with `FOR UPDATE`, matching Phase 4.7 completion and Phase 4.8 claim order, then locks/reloads the episode where present and revalidates status, ownership, claim metadata, graph provenance, and persisted expiry before deletion. Missing/already-cleaned and stale candidates are safe no-ops; concurrent cleaners serialize without double-delete failure; a failed graph deletion rolls back completely and remains retryable. Completion winning the session lock preserves valid authenticated permanent state (or causes a stale anonymous-active candidate to skip); cleanup winning removes only still-active abandoned state so completion cannot create an orphan. Claim winning attaches ownership before cleanup revalidation and is preserved; cleanup winning removes the expired unclaimed graph so claim cannot resurrect it. Routine telemetry contains only aggregate batch/category/count/duration/failure-category data, never capabilities or hashes, tokens, demographics, narratives, answers, or result content. The Phase 4.1 expiry/index/FK schema was sufficient, so no migration or EF model change was required. No endpoint, readiness dependency, AI/clinical execution, definition mutation, Clinical History projection, outbox, FHIR, resume, recovery, or archive behavior was added.
**Phase 4.9 verification:** Ten focused unit cases and sixteen focused integration cases passed; the integration focus comprises twelve real-PostgreSQL cleanup cases and four startup/configuration cases. Coverage includes exact before/at/after expiry, anonymous-active and authenticated-abandonment cascades, completed-unclaimed restricted-graph deletion, claimed and authenticated-completed preservation, deterministic multi-batch processing, repeat and two-worker idempotency, candidate revalidation, cancellation, worker failure containment, aggregate-log privacy, forced mid-delete rollback/retry, and three real PostgreSQL concurrency cases for two cleaners, completion-versus-cleanup, and claim-versus-cleanup. The complete suites passed 729 tests: 436 unit and 293 real-PostgreSQL integration, with 0 failed and 0 skipped. All 13 migration regressions passed the clean full chain and rollback/reapply coverage; no Phase 4.9 migration exists. The final Debug solution build completed with 0 warnings and 0 errors, EF reported no pending model changes, the OpenAPI regression retained exactly 18 paths with no cleanup route, formatting verification and `git diff --check` passed, and no live AI credentials were required. Phase 4.10 Clinical History Projection Boundary, full history, FHIR, resume/recovery, archival recovery, clinical rules, and all clinical-authority features remain deferred.

**Objective:** Enforce temporary workflow retention and the 24-hour anonymous lifecycle without creating permanent records for abandonment.

**Exact scope:** Implement `ExpireAnonymousPreTriage` and `PreTriageCleanupService`, or equivalents. At the defined 24-hour boundary, discard active anonymous temporary workflow data; expire/remove completed unclaimed anonymous episodes according to the finalized lifecycle; discard abandoned authenticated temporary workflows; retain completed authenticated and successfully claimed records. Resume remains unsupported.

**Main components:** Cleanup application service, scheduled/background worker, clock abstraction, candidate queries, batched idempotent deletion/expiration, repository/unit of work, and privacy-safe operational metrics.

**Endpoints involved:** None.

**AI involvement:** None.

**Clinical-definition dependencies:** None; cleanup must not mutate shared definition versions.

**Security/safety requirements:** Use minimum retention, never expose capability hashes, delete only exact eligible temporary/unclaimed targets, preserve claimed and completed authenticated records, prevent abandoned sessions from producing episode/assessment/history state, and make repeated/concurrent cleanup safe.

**Tests and acceptance criteria:** Test immediately before/at/after 24 hours, anonymous active cleanup, completed-unclaimed cleanup, claimed preservation, authenticated abandonment, completed authenticated preservation, temporary answer/symptom removal, no abandoned permanent/history records, idempotent batches, and concurrency with completion/claim.

**Explicitly out of scope:** Resume, archival recovery, deleting claimed/permanent records, and Clinical History rendering.

**Dependencies on previous subphases:** Phase 4.7 and Phase 4.8.

## Phase 4.10 — Clinical History Projection Boundary

**Phase 4.10 status:** COMPLETE (2026-08-22)
**Phase 4.10 implementation:** Added the internal `ProjectCompletedPreTriageEpisode` application handler and `IPreTriageHistoryProjector`/repository boundaries over a minimal immutable `PreTriageHistoryProjection` contract. Eligibility is represented durably by exactly one `PreTriageHistoryProjectionRecord` whose source-episode identifier is also its idempotency key and primary key. Authenticated primary- and managed-patient completion creates that record beside the episode and neutral assessment in the existing locked completion transaction; the record carries the episode's actual subject patient, completion time, and creation time. Anonymous completion creates no record and cannot project. A successful Phase 4.8 claim creates the record beside ownership and `ClaimedAt` in the existing locked claim transaction; same-patient repeats reuse the one record, and expired/unclaimed cleanup still creates none. The projector returns null without a durable record, then positively validates the completed session/immutable episode/neutral assessment/ownership/record graph and rebuilds only the structured neutral symptom summary from episode-owned answers and controlled symptoms. Its Phase 5-facing contract contains source type and episode ID, patient ID, completion time, primary symptom code/display, duration value/unit, intensity, controlled additional symptoms, exact questionnaire code/version, exact package/rule-set code/version, and truthful clinical content source/review/approval. It loads definitions exclusively by the episode's frozen questionnaire-version ID, verifies the frozen rule-set ID, and never consults an active version. PostgreSQL migration `20260822182341_Phase410ClinicalHistoryProjectionBoundary` adds `history.pre_triage_projection_records` with source-episode primary-key uniqueness, patient/completion index, timestamp check, and `RESTRICT` episode/patient foreign keys; it deterministically backfills only existing patient-owned completed neutral graphs that satisfy the authenticated-completion or claimed-anonymous lifecycle shape, while excluding unclaimed or inconsistent graphs. Session-row serialization plus the source primary key make concurrent completion, claim, and projection delivery one logical result without leaking uniqueness failures. Projection-record insert failure rolls back the entire completion or claim mutation. Phase 4.9 cleanup preserves eligible records and their permanent source graphs, while abandoned active sessions and expired completed-unclaimed anonymous graphs remain projection-free and deletable. No public route, history UI, full Clinical History event, amendment, FHIR resource, AI call, active-definition reinterpretation, urgency, disposition, red flag, diagnosis, prescription, treatment, recommendation, or probability was added.
**Phase 4.10 verification:** Six focused unit cases and sixteen focused real-PostgreSQL cases passed, including five concurrency/race cases. Coverage proves no projection before completion or anonymous claim; authenticated primary/managed eligibility and ownership stability after relationship revocation; anonymous unclaimed exclusion, cleanup deletion, first/same-patient claim idempotency, and permanent claimed preservation; repeated and concurrent completion/projector/claim delivery with one database record; frozen version A despite a separate active version B; corrupt urgency-bearing graph rejection; forbidden-field and no-FHIR contract shape; forced completion-marker and claim-marker rollback; deterministic valid-row backfill; clean migration rollback/reapply; and unchanged cleanup completion/claim races. The complete suites passed 740 tests: 442 unit and 298 real-PostgreSQL integration, with 0 failed and 0 skipped. All 14 migration regressions passed, including the clean full chain through Phase 4.10 and projection-boundary rollback/reapply. The final Debug solution build completed with 0 warnings and 0 errors; EF reported no pending model changes; the OpenAPI regression retained exactly 18 paths with no history/projection route; formatting verification and `git diff --check` passed; and no live AI credentials were required. Phase 4.11 demo security/acceptance closure, full Phase 5 Clinical History events/endpoints/UI/amendments, FHIR, AI Conversation History, and every clinical-rule/authority feature remain deferred.

**Objective:** Ensure only a completed immutable demo episode is eligible to enter Clinical History.

**Exact scope:** Add the minimal idempotent event/outbox/projection boundary, such as `ProjectCompletedPreTriageEpisode`, required for Phase 5. Enforce that `PreTriageSession` and temporary `TriageAnswer` records never project directly, while one completed `PreTriageEpisode` is eligible for exactly one neutral symptom-summary projection. Carry stable identifiers, structured answers, completion time, and exact definition/provenance references without generating FHIR or clinical urgency.

**Main components:** Completion integration event/outbox record or equivalent durable boundary, projector interface/handler, idempotency key/constraint, and minimal Phase 5-facing neutral summary contract.

**Endpoints involved:** None.

**AI involvement:** None.

**Clinical-definition dependencies:** Frozen references on the completed episode only; no active-version lookup during projection.

**Security/safety requirements:** Never project incomplete, abandoned, expired, or temporary session state; prevent duplicate projection; preserve patient ownership and immutable source references; exclude raw conversation, capabilities, provider metadata, urgency, disposition, diagnosis, and recommendations.

**Tests and acceptance criteria:** Prove no projection before completion, exactly one after completion, repeated/concurrent delivery is idempotent, abandoned/expired sessions produce none, and projection uses the frozen neutral episode rather than current definitions.

**Explicitly out of scope:** Full Clinical History endpoints/UI, amendments, FHIR resource generation, and AI Conversation History.

**Dependencies on previous subphases:** Phase 4.7 and Phase 4.9.

## Phase 4.11 — Demo Security + Acceptance Closure

**Phase 4.11 status:** COMPLETE (2026-08-22)
**Phase 4.11 implementation:** Audited the retained Phase 4.1 through Phase 4.10 implementation, tests, migrations, five-route OpenAPI surface, Phase 3 identity/managed-patient authorization reuse, capability and expiry boundaries, row-lock ordering, cleanup worker/startup configuration, clinical-definition artifacts, AI safety boundary, neutral completion/result/claim/projection contracts, and privacy-safe telemetry. Existing coverage already proved the endpoint credential matrices, invalid-Bearer non-downgrade, capability isolation/hash-only persistence, IDOR concealment, managed authorization and revocation, structured and natural-language intake validation, adversarial/provider-unavailable behavior, anonymous completion/claim/cleanup journeys, lifecycle boundaries, immutability/version isolation, rollback, and PostgreSQL races. Closure adds an explicit six-case authenticated-primary/managed-patient by three-supported-pathway journey matrix that verifies canonical retrieval, non-clinical provenance, neutral assessment persistence, exact patient ownership, no anonymous retention marker, and exactly one projection record. It also adds a compact allow-list regression for the public neutral result and internal projection shapes plus a guard that executable triage type surfaces remain free of vendor-specific and FHIR/HL7 coupling. No endpoint, clinical rule, migration, provider mandate, FHIR artifact, history route/UI, or Phase 5 authority was added.
**Phase 4.11 verification:** Two focused closure unit tests and six focused real-PostgreSQL patient-owned journey cases passed. The complete suites passed 748 tests: 444 unit and 304 real-PostgreSQL integration, with 0 failed and 0 skipped. The explicit concurrency/race filter passed 17 cases, including all nine Phase 4 PostgreSQL answer, completion, projection, claim, and cleanup concurrency/race tests. All 14 migration regressions passed, including clean full-chain application through Phase 4.10 and the Phase 4 rollback/reapply matrix; EF reported no pending model changes. The focused development OpenAPI regression passed with exactly 18 paths and exactly the five documented Phase 4 endpoints. The final Debug solution build completed with 0 warnings and 0 errors; formatting verification changed 0 of 334 files; `git diff --check` passed; and no live AI credentials were required. Phase 4 is closed as a neutral, non-clinically-authoritative demo increment. Phase 5 Clinical History endpoints/events/UI/amendments, FHIR, AI Conversation History, production AI-provider selection, additional pathways, and all clinical-rule/urgency/disposition/diagnostic authority remain deferred.

**Objective:** Close the demo increment with end-to-end evidence for controlled intake, safety, authorization, lifecycle, neutral completion, and explicit absence of clinical conclusions.

**Exact scope:** Verify all five endpoints and every retained Phase 4 invariant across anonymous, primary-patient, and authorized managed-patient flows for `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`. Audit capability entropy/hash use, invalid-auth non-downgrade, IDOR defenses, 24-hour lifecycle, temporary/permanent separation, completion/claim/cleanup races, immutability, exact package provenance, unsupported pathways, deterministic questionnaire authority, AI safety/failure behavior, neutral result contracts, history projection, migrations, and privacy-safe logging.

**Main components:** End-to-end acceptance fixtures/harness, simplified-package fixtures for each of the three supported pathways, adversarial AI provider/renderer stub, concurrency and clock-controlled lifecycle tests, authorization/security tests, database constraint verification, negative clinical-output contract tests, OpenAPI verification, and release-readiness checklist.

**Endpoints involved:** `POST /api/v1/pre-triage/sessions`, `POST /api/v1/pre-triage/sessions/{id}/answers`, `POST /api/v1/pre-triage/sessions/{id}/complete`, `GET /api/v1/pre-triage/sessions/{id}/result`, and `POST /api/v1/pre-triage/sessions/{id}/claim`.

**AI involvement:** Yes for interpretation, guarded neutral rendering, adversarial testing, and availability testing; never as questionnaire or clinical authority.

**Clinical-definition dependencies:** The exact simplified demo packages selected in Phase 4.5. The detailed abdominal clinical package remains a non-executed regression artifact.

**Security/safety requirements:** Mandatory fixtures include football question -> `OUT_OF_SCOPE`; medication request -> `PRESCRIPTION_REQUEST`; prompt injection -> restrictions remain enforced; invalid/ambiguous output -> clarification; unsupported symptom -> no remapping; provider unavailable -> explicit structured flow remains usable. Assert no response, persisted neutral assessment, projection, renderer, or log introduces urgency, disposition, red-flag escalation, emergency recommendation, diagnosis, prescription, treatment advice, or probability.

**Tests and acceptance criteria:** Pass the mandatory endpoint matrix plus end-to-end anonymous/authenticated/managed flows for `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER`; the exact `NAUSEA`/`DIARRHEA`/`FEVER` additional-symptom catalog with no fourth option; nonredundant `FEVER` applicability; controlled duration/intensity data; multi-field extraction and already-answered skipping; `CHEST_PAIN`/`OTHER_SYMPTOMS` and other recognized-but-unsupported/unknown behavior; IDOR/capability/authentication; exact 24-hour expiry; abandonment; atomic/concurrent/idempotent completion and claim; cleanup races; immutable records; exact provenance; AI outage/adversarial behavior; neutral result schema; one idempotent history projection; migration rollback/pending-model checks; OpenAPI; formatting; and the full backend suite.

**Explicitly out of scope:** Future clinical rule execution, full protocols, additional unselected packages, Phase 5 features, FHIR generation, production provider mandate, and autonomous agents.

**Dependencies on previous subphases:** Phase 4.1 through Phase 4.10. Andrea's pathway and option confirmation is recorded in Phase 4.5; no further product selection is required for demo acceptance.

## POST-DEMO / FUTURE CLINICAL PRE-TRIAGE

The previously planned deterministic clinical rule-engine increment is removed from the current Phase 4 execution order. It is not implemented merely because Phase 4.1 already contains `ClinicalRuleSetVersion`, `ClinicalFinding`, and urgency-compatible persistence or because Phase 4.2 stores detailed provisional abdominal artifacts.

Future clinical Pre-Triage may, only after formal product, medical, legal, and regulatory approval, introduce dedicated reviewed packages and an authoritative deterministic engine for:

- pathway-specific detailed questionnaires and protocols;
- red-flag evaluation and precedence;
- clinically reviewed urgency vocabularies such as `CRITICAL`, `HIGH`, `MEDIUM`, `LOW`, and `VERY_LOW`;
- disposition calculation and emergency recommendations;
- approved patient-facing clinical messages;
- deterministic rule conformance fixtures and no-downgrade behavior;
- clinically appropriate `RiskAssessment`/FHIR mapping;
- additional symptom packages, formal approvals, localization, and production terminology integration.

Future implementation must use new immutable reviewed definition versions, never reinterpret a completed demo episode, never mutate the stored provisional abdominal package, never use AI as clinical authority, and undergo a new regulatory/security/clinical acceptance plan. None of these capabilities is a dependency or acceptance criterion for the current demo.

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

**Phase 5.5 implementation:** Added authenticated `POST /api/v1/pre-triage/episodes/{episodeId}/amendments` and the `AmendPreTriageEpisode` command. The endpoint accepts exactly a required non-empty UUID `idempotencyKey` and the existing Phase 5.1 free-text `AmendmentReason`; unknown correction, author, timestamp, provenance, or audit fields are rejected. It resolves and share-locks the authoritative completed patient-owned episode and its exact Clinical History event, then applies the shared Phase 3 authorization decision inside the same transaction. Active manager relationships are share-locked so revocation cannot race past an authorized insert. The caller's active Account is the immutable author, timestamps and amendment IDs are server-controlled, and source/event/version provenance is copied through the Phase 5.1 aggregate without changing the episode, assessment, answers, symptoms, versions, history event, or earlier amendments. The public `201` body reuses the Phase 5.4 amendment representation with `BEEEXY_ACCOUNT` plus the author's Beeexy ID; concealed missing/ineligible/unauthorized sources return the existing indistinguishable `404`, invalid requests return safe `422`, and duplicates return safe `409`. Because Phase 5.1 had no durable request discriminator, additive migration `Phase55TraceablePreTriageAmendments` adds a nullable legacy-compatible `idempotency_key` and a filtered unique `(clinical_history_event_id, idempotency_key)` index. Every API-created amendment requires a key; PostgreSQL uniqueness makes sequential and simultaneous retries deterministic while distinct keys remain independent. Privacy-safe audit logs contain only technical identifiers, authorization category, and time. The existing domain supports traceability metadata and reason only, so no arbitrary patch, new clinical correction payload, or clinical interpretation was invented.

**Phase 5.5 verification:** Seventeen focused domain/application tests and four real authenticated PostgreSQL endpoint journeys passed, covering primary completion, active-manager authorship, revocation, cross-patient and identifier-only IDOR concealment, validation/audit-field rejection, immutable source snapshots, list stability, claimed-anonymous eligibility, sequential duplicates, simultaneous duplicate races, distinct amendments, and Phase 5.4 detail rendering. All 14 migration regressions passed, including Phase 5.5 rollback/reapply, and EF reported no pending model changes. OpenAPI now has exactly 21 paths and documents Bearer security plus `201`/`401`/`404`/`409`/`422` without amendment update/delete operations. The complete unit suite passed 480 tests. The complete PostgreSQL integration suite ran 327 tests: 321 passed and exactly the six previously established development-bootstrap/unavailable-database fixture cases failed; no Phase 5.5 test or regression failed. The Debug solution build completed with 0 warnings and 0 errors, formatting completed, and `git diff --check` passed. No amendment update/delete, source overwrite, arbitrary clinical JSON patch, FHIR, AI, history deletion, or Phase 5.6 functionality was introduced.

**Phase 5.6 implementation:** Closed Phase 5 through focused acceptance coverage without changing production contracts, persistence, or migrations. Two real authenticated PostgreSQL journeys now prove that revoking one of two independent managers immediately conceals list, detail, and amendment access from only that manager while preserving the other manager's authority and every stored source/history/amendment record; and that anonymous completion creates no patient history until the legitimate claim, after which one projection is listable, readable, amendable, and source-immutable. Two contract/architecture tests freeze the minimal public list/detail/amendment shapes, retain the intentionally non-OpenAPI extension-data validation boundary, and reject destructive history/amendment operations plus Phase 6 FHIR, AI Conversation History, and unapproved diagnostic/treatment concepts. Review of the existing Phase 5.1-5.5 implementation and acceptance matrix found no product defect, so no production-code change or speculative refactor was made.

**Phase 5.6 verification:** All four dedicated closure tests passed, as did 44 focused Clinical History/projection unit tests and 77 focused real-PostgreSQL Phase 5, migration, and OpenAPI integration tests. The focused matrix covers primary and active-manager journeys, independent-manager revocation, anonymous completion/claim, thirteen-event cursor traversal, equal-time ordering and concurrent insertion stability, cursor/filter/IDOR rejection, frozen provenance, source immutability, database-backed sequential and simultaneous amendment idempotency, distinct amendment keys, and exactly-once completion/claim/projection concurrency. All 14 migration regressions passed clean apply plus supported rollback/reapply checks, EF reported no pending model changes, and OpenAPI remains exactly 21 paths with only the three approved Phase 5 routes and no destructive methods. The complete unit suite passed 482 tests. The complete PostgreSQL integration suite ran 329 tests: 323 passed and exactly the same six previously documented unrelated fixture cases failed (three development demo bootstrap database-teardown cases and three deliberately unavailable-database host-start cases); no Phase 5 test or regression failed. The final Debug solution build completed with 0 warnings and 0 errors, solution-wide formatting verification and `git diff --check` passed, and no Phase 6 FHIR behavior, AI Conversation History, deletion, arbitrary JSON patch, urgency/disposition/diagnostic authority, or new Clinical History event type was introduced. Phase 5 is functionally complete, with the six unchanged infrastructure/test-fixture failures explicitly outstanding outside its scope.

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

**Phase 6.1 status: COMPLETE**

**Phase 6.1 implementation:** Added the FHIR export domain and PostgreSQL persistence foundation without adding a FHIR SDK or FHIR resource types to the Domain. `FhirExport` records its source patient and immutable Clinical History event, exact FHIR/mapping/profile versions, per-patient UUID idempotency key, lifecycle status, checksum algorithm/value, private storage URI metadata, and creation/update/generation/validation timestamps. `FhirValidationResult` records the outcome, exact validator identity/version, artifact checksum, error/warning counts, and validation time. One-way domain transitions and database lifecycle/outcome checks prevent invalid artifacts from being represented as validated; a composite PostgreSQL validation-proof foreign key binds each result to the export's exact outcome, checksum, and validation timestamp. The `interoperability.fhir_exports` and `interoperability.fhir_validation_results` tables use UUID primary keys, restricted patient/source/export foreign keys, patient/source integrity, one result per export, per-patient idempotency uniqueness, and focused patient/time/status/source/outcome indexes. Migration `20260824202650_Phase61FhirExportPersistenceFoundation` is additive to the Phase 1-5 chain and creates no FHIR-shaped clinical table.

**Phase 6.1 verification:** Locked restore succeeded and the final Debug solution build completed with 0 warnings and 0 errors. All 490 unit tests passed, including 8 focused Phase 6.1 domain tests. All 5 focused real-PostgreSQL persistence tests passed, as did the fresh full-chain migration, Phase 6.1 rollback/reapply with existing patient-data preservation, focused migration regressions, and the OpenAPI regression. The complete integration suite ran 335 tests: 329 passed and exactly the same six pre-existing Phase 5 fixture/startup failures documented above remained; no Phase 6.1 test or regression failed. EF reported no pending model changes, formatting verification and `git diff --check` passed, and OpenAPI remains exactly 21 paths with no FHIR route. FHIR generation, serialization, runtime validator integration, artifact storage/download, FHIR SDK integration, and Phase 6 API endpoints remain explicitly unimplemented.

**Phase 6.2 status: COMPLETE**

**Phase 6.2 implementation:** Read `Backend/docs/fhir/beeexy-coleccion-recursos.md`, `Backend/docs/fhir/beeexy-provenance-device-ejemplo.md`, and `Backend/docs/fhir/beeexy-riskassessment-ejemplo.md` completely and recorded their supported facts and unresolved requirements in the Andrea mapping inventory. Added a provider-independent Application-layer `IFhirMapper<TInput, TRepresentation>` boundary plus typed inputs for the four required conceptual mappings: frozen patient-owned episode/questionnaire answers and terminology to `QuestionnaireResponse`; neutral authoritative assessment identity to `RiskAssessment`; explicitly supplied Beeexy runtime software identity to `Device`; and typed target/agent/source relationships plus internal source IDs to `Provenance`. Added logical outbound resource identities, generation trace, and an explicit mapping-specification identity that cannot create the Phase 6.1 export-version snapshot until FHIR release and profile applicability are resolved. The contracts reject mismatched or incomplete source graphs and prohibit the current neutral assessment from acquiring urgency, disposition, diagnosis, probability, mitigation, treatment, red flags, or other invented clinical authority. FHIR identifiers remain outbound references only and confer no authorization.

**Phase 6.2 unresolved/TBD:** Andrea's documents do not specify the FHIR release, canonical profile URLs or profile versions, Patient resource identity strategy, Questionnaire identity/version encoding, Questionnaire item `linkId` strategy, or translation from Beeexy answer JSON schemas to typed FHIR answers. The current neutral `ClinicalAssessment` also truthfully lacks the required RiskAssessment prediction outcome, probability, and mitigation, so RiskAssessment generation remains explicitly blocked instead of reusing Andrea's vertigo example values. The future generation component must supply the actual Device runtime version; the example version is not a default.

**Phase 6.2 verification:** Locked solution restore succeeded; the complete Debug solution build completed with 0 warnings and 0 errors; all 10 focused Phase 6.2 tests and all 500 unit tests passed. The 8 Phase 6.1 domain tests, 5 real-PostgreSQL Phase 6.1 persistence tests, and OpenAPI regression passed. The complete integration suite ran 335 tests: 329 passed and exactly the same six pre-existing Phase 5 fixture/startup failures remained (three development demo bootstrap database-teardown cases and three deliberately unavailable-database host-start cases); no Phase 6.1 or Phase 6.2 test failed. EF reported no pending model changes, so Phase 6.2 required no migration. Solution-wide formatting verification and `git diff --check` passed, the Domain remains free of FHIR SDK dependencies, and OpenAPI remains exactly 21 paths with no Phase 6 endpoint. Actual FHIR resource generation, Bundle generation, JSON serialization, validation, artifact/checksum creation, storage, download, transmission, and API endpoints remained unimplemented in Phase 6.2.

**Phase 6.3 status: COMPLETE**

**Phase 6.3 implementation:** Implemented the concrete `QuestionnaireResponseMapper` behind the Phase 6.2 `IFhirMapper` contract as a deterministic, side-effect-free Application-layer mapping. From a validated completed-episode mapping input it produces a release-neutral `QuestionnaireResponseRepresentation` with Andrea's supported `completed` status, patient/source identities, authored time, and only the actually submitted items in frozen questionnaire display order. The Phase 6.2 input now also snapshots the exact questionnaire content hash and each answered question's frozen answer-schema JSON. The representation preserves the historical questionnaire UUID/code/version/hash, question UUID/code/text/order, answer UUID/schema/exact JSON/JSON value kind/recorded time, and mapping-specification identity. Explicit Boolean false remains distinct from an unanswered question, free text is unchanged, later questionnaire versions cannot change historical output, source records are not mutated, and no AI, normalization, clinical inference, persistence, HTTP, or validation dependency participates. Missing schema, null/unsupported answer content, mismatched versions, and inconsistent source relationships fail explicitly.

**Phase 6.3 SDK/serialization decision and TBDs:** Completely reread `Backend/docs/fhir/beeexy-coleccion-recursos.md`, `Backend/docs/fhir/beeexy-provenance-device-ejemplo.md`, `Backend/docs/fhir/beeexy-riskassessment-ejemplo.md`, and `docs/fhir/phase-6.2-andrea-mapping-inventory.md`. They still establish no FHIR release, canonical profile URLs/versions, QuestionnaireResponse logical-ID strategy, Patient reference strategy, Questionnaire reference/version encoding, item `linkId` strategy, or Beeexy answer-schema/JSON to FHIR `answer.value[x]` translation. Consequently no FHIR SDK could legitimately be selected and no FHIR JSON serialization was implemented. The concrete release-neutral representation exposes every unresolved requirement, leaves the affected FHIR fields unset, and cannot claim standards validation. No R4/R4B/R5 release, canonical, profile, example identifier, terminology, link ID, or value translation was invented.

**Phase 6.3 verification:** Locked restore succeeded and the complete Debug solution build completed with 0 warnings and 0 errors. All 11 focused Phase 6.3 tests passed; the combined Phase 6.1-6.3 focused unit regression ran 29/29, and the complete unit suite passed 511/511. All 5 real-PostgreSQL Phase 6.1 persistence tests and the exact OpenAPI regression passed. The complete integration suite ran 335 tests: 329 passed and exactly the same six pre-existing Phase 5 fixture/startup failures remained (three development demo bootstrap database-teardown cases and three deliberately unavailable-database host-start cases); no Phase 6.3 test or regression failed. EF reported no pending model changes, so no migration was required. Solution-wide formatting verification and `git diff --check` passed, OpenAPI remains exactly 21 paths with no FHIR endpoint, and `Beeexy.Domain` remains free of FHIR SDK dependencies. `RiskAssessment` remains blocked because the neutral assessment lacks truthful prediction, probability, and mitigation. Device, Provenance, Bundle generation, complete export orchestration, FHIR serialization/validation, artifact/checksum storage, download/transmission, and Phase 6 APIs remain unimplemented. Phase 6.4 has not started.

**Phase 6.4 status: COMPLETE**

**Phase 6.4 implementation:** Completely reread the three Andrea sources actually checked in at `docs/fhir/beeexy-coleccion-recursos.md`, `docs/fhir/beeexy-provenance-device-ejemplo.md`, and `docs/fhir/beeexy-riskassessment-ejemplo.md`, plus the Phase 6.2 inventory and Phase 6.3 generation document, and inspected the current ClinicalAssessment, completed Pre-Triage, Clinical History provenance, and Phase 6.1 export models. Added deterministic Application-layer release-neutral mapping boundaries for the remaining concepts. `DeviceMapper` preserves only Andrea's Beeexy processing-software name/name-type, model-number concept, explicitly supplied runtime version, manufacturer concept, and software type text; it adds no patient hardware, UDI, serial number, regulatory status, identifier, owner, organization, canonical, or example product version. `ProvenanceMapper` preserves the planned export UUID, internal generation identities for Provenance/target RiskAssessment/author Device/source QuestionnaireResponse, authoritative patient/history-event/episode/assessment source UUIDs, generation UTC timestamp, explicit mapping-specification version, and Andrea's CREATE/author/source concepts. Internal generation identities remain separate from unset final FHIR references, carry no authorization, and expose no Account/authentication/capability/manager/secret/storage metadata. A shared requirement resolver keeps release/profile states explicit without changing Phase 6.3 semantics.

**Phase 6.4 RiskAssessment blocker:** Concrete RiskAssessment generation remains prominently **BLOCKED**. The neutral authoritative ClinicalAssessment contains only assessment/episode/rule-set identity and occurrence time; the validated source graph adds patient and Clinical History event identity. It contains no prediction outcome, probability, qualitative risk, mitigation, urgency, disposition, diagnosis, recommendation, treatment, red flag, or finding. `RiskAssessmentMapper.Inspect` exposes only those truthful source facts, Andrea's supported final/disclaimer concepts, and the exact unresolved requirements. `RiskAssessmentMapper.Map` raises the typed `RiskAssessmentGenerationBlockedException` instead of returning a misleading partial resource. The missing authoritative clinical inputs are prediction outcome, prediction probability, and mitigation; Patient/resource identity and final reference construction also remain unresolved. Questionnaire answers, symptom intensity, old clinical definitions, AI, heuristics, and generic medical knowledge cannot supply those fields. No vertigo outcome, `0.72`, `moderate`, referral wording, arbitrary probability, risk band, or other clinical value was fabricated.

**Phase 6.4 release/serialization decision and remaining TBDs:** Andrea's materials still do not select an exact FHIR release, canonical profile URLs/versions, or final resource identity/reference strategy. No FHIR SDK dependency, R4/R4B/R5 choice, profile, canonical, official FHIR serializer, or validator was introduced. Every Phase 6.4 representation leaves final reference strings unset and reports that it cannot be serialized as FHIR. Bundle/snapshot assembly (Phase 6.5), formal validation (Phase 6.6), complete export orchestration, artifact/checksum generation, persistence/storage, download/transmission, validation status transitions, and all Phase 6 APIs remain unimplemented.

**Phase 6.4 verification:** Locked restore succeeded; the complete Debug solution build completed with 0 warnings and 0 errors. All 18 focused Phase 6.4 tests passed; the combined Phase 6.1-6.4 unit regression ran 47/47, and the complete unit suite passed 529/529. The 5 real-PostgreSQL Phase 6.1 persistence tests and full OpenAPI/CORS regression passed in an 11/11 focused integration run. The complete integration suite ran 335 tests: 329 passed and exactly the same six pre-existing Phase 5 fixture/startup failures remained—the three `FreshDevelopmentDatabase_StartsSessionsForEveryDemoPathway` cases for HEADACHE, ABDOMINAL_PAIN, and FEVER, plus `Live_WhenPostgreSqlIsUnavailable_RemainsHealthy`, `Ready_WhenPostgreSqlIsUnavailable_ReturnsSafeServiceUnavailable`, and `UnavailableDatabase_ConnectionSecretIsNotLoggedOrReturned`; no Phase 6.4 integration test or regression failed. EF reported no pending model changes, so Phase 6.4 required no migration. OpenAPI remains exactly 21 paths with no FHIR route, `Beeexy.Domain` remains free of FHIR SDK dependencies, and no Bundle/export/validator/storage/download/API component was added. Solution-wide formatting verification and `git diff --check` passed.

**Phase 6.5 status: COMPLETE**

**Phase 6.5 implementation:** Added a deterministic `FhirSnapshotAssembler`, compact fixed-order UTF-8 serializer, exact-byte SHA-256 calculator, and internal `GenerateFhirExport` orchestrator over the Phase 6.1-6.4 contracts. The artifact is explicitly `beeexy-release-neutral-interoperability-snapshot` version `1` with private media type `application/vnd.beeexy.interoperability-snapshot+json`; it is not official FHIR JSON, a Bundle, complete FHIR, or validatable. It freezes authoritative source and historical questionnaire identities/content, supported QuestionnaireResponse/Device/Provenance representations in deterministic order, generation/mapping/runtime facts, and unresolved requirements. Mandatory RiskAssessment is not silently omitted: it is recorded as a blocked required resource because prediction outcome, probability, and mitigation remain unavailable, so final standards-compliant FHIR export remains blocked without preventing the truthful Phase 6.5 snapshot. No AI or clinical inference participates.

**Phase 6.5 storage/lifecycle/idempotency:** Added replaceable private artifact storage and an atomic local filesystem adapter using cryptographically random 256-bit opaque keys, a non-public `beeexy-private-artifact` URI, temporary-file plus no-overwrite move semantics, internal exact-byte reads, and cleanup. Generation reloads the authoritative PostgreSQL graph inside a transaction, uses a transaction-scoped advisory lock per patient/idempotency key, persists `Pending`, stores bytes, then records `SHA-256` and transitions to `Generated` only after successful storage. Same patient/key returns one export and artifact under sequential or concurrent requests; different keys and patients remain isolated. Storage failure rolls back the export; database/commit failure after storage deletes the artifact, with a typed reconciliation exception if cleanup itself fails. The generated artifact and checksum cannot be replaced in place, and no validation state/result is created.

**Phase 6.5 unresolved/out of scope:** The exact FHIR release, canonical profile applicability/URLs/versions, final Patient/Questionnaire/resource identity and reference strategy, Questionnaire version/`linkId` encoding, answer-schema JSON to FHIR `value[x]` translation, and authoritative RiskAssessment prediction/probability/mitigation remain TBD. The Phase 6.1 schema was sufficient, so Phase 6.5 adds no migration or EF model change. No FHIR SDK, official FHIR serializer, validator, validation transition, download/content endpoint, public URL, external transmission, amendment mapping, or Phase 6 HTTP API was added by Phase 6.5; the validation boundary is addressed by Phase 6.6 below.

**Phase 6.5 verification:** The final Debug solution build completed with 0 warnings and 0 errors. All 16 focused Phase 6.5 unit tests and 4 focused real-PostgreSQL tests passed; the Phase 6.1-6.4 unit regression passed 47/47; the complete unit suite passed 545/545; and all 14 migration-behavior regressions passed. The complete integration suite ran 339 tests: 333 passed and exactly the same six pre-existing Phase 5 failures remained—the three `FreshDevelopmentDatabase_StartsSessionsForEveryDemoPathway` cases for HEADACHE, ABDOMINAL_PAIN, and FEVER, plus `Live_WhenPostgreSqlIsUnavailable_RemainsHealthy`, `Ready_WhenPostgreSqlIsUnavailable_ReturnsSafeServiceUnavailable`, and `UnavailableDatabase_ConnectionSecretIsNotLoggedOrReturned`; no Phase 6.5 test failed. EF reported no pending model changes; OpenAPI remains exactly 21 paths with no FHIR route; `Beeexy.Domain` remains free of FHIR SDK dependencies; and formatting/static plus `git diff --check` passed.

**Phase 6.6 status: COMPLETE — STATE B**

**Phase 6.6 implementation:** **Validation pipeline implemented; concrete standards validation blocked by unresolved authoritative FHIR/clinical requirements.** Added a typed prerequisite evaluator that distinguishes release-neutral representation, unresolved specification, and unavailable required-resource content. The production evaluator explicitly blocks the Phase 6.5 snapshot for its non-FHIR format, unresolved release/profiles/resource identities/references/Questionnaire `linkId` and `answer.value[x]`, missing mandatory RiskAssessment prediction/probability/mitigation and incomplete required resource set, and lack of an approved validation specification. An eligible decision must instead carry an exact release, mapping version, and resolved profile applicability that match the immutable export. No FHIR SDK, release, profile, canonical, default validation package, or clinical value was invented.

**Phase 6.6 checksum/validator/lifecycle:** Added internal `ValidateFhirExport` orchestration and an `IFhirValidator` boundary for valid, invalid, unavailable, and unsupported-specification results. The use case locks and loads the patient-scoped export, reads the exact private bytes, verifies Phase 6.5 SHA-256 with a fixed-time comparison before eligibility or validator invocation, and only records `Generated -> Validated` or `Generated -> ValidationFailed` after an eligible validator reports completed success or invalid content. Blocked, checksum-failed, artifact-unavailable, unsupported, and validator-infrastructure outcomes preserve `Generated` and create no false validation evidence. Completed evidence atomically freezes validator identity/version, checksum association, counts, and repository-clock timestamp; final results are idempotent. A PostgreSQL advisory lock serializes concurrent attempts so only one validator execution and one result can win. Validation never regenerates or mutates the artifact, and retry after infrastructure failure is safe. Raw validator details, provider codes, exception text, storage paths, and artifact bodies are not persisted or returned as diagnostics; only generic counts/summaries/category codes cross the application boundary.

**Phase 6.6 persistence/out of scope:** The Phase 6.1 export/result schema and one-to-one result constraint fully represent the required evidence, so no migration or EF model change was added. The production validator registration is intentionally unavailable and unreachable for blocked Phase 6.5 snapshots; controlled test validators prove success/invalid semantics without making a compliance claim. No HTTP endpoint, OpenAPI operation, download/content route, public URL, external transmission, new resource, amendment representation, artifact regeneration, AI, or clinical inference was introduced. Concrete validation can be unlocked only by authoritative resolution of the listed FHIR and clinical blockers.

**Phase 6.6 verification:** The final Debug solution build completed with 0 warnings and 0 errors. All 13 focused Phase 6.6 unit tests and all 6 focused real-PostgreSQL Phase 6.6 tests passed, including blocker, patient-scope, tampered, retry, success, invalid, idempotent, and concurrent behavior. The Phase 6.1-6.5 focused unit regression passed 63/63, the complete unit suite passed 558/558, and all 15 migration-behavior tests passed. The complete integration suite ran 345 tests: 339 passed and exactly the same six pre-existing Phase 5 fixture/startup failures remained—the three `FreshDevelopmentDatabase_StartsSessionsForEveryDemoPathway` cases for HEADACHE, ABDOMINAL_PAIN, and FEVER, plus `Live_WhenPostgreSqlIsUnavailable_RemainsHealthy`, `Ready_WhenPostgreSqlIsUnavailable_ReturnsSafeServiceUnavailable`, and `UnavailableDatabase_ConnectionSecretIsNotLoggedOrReturned`; no Phase 6.6 test failed. EF reported no pending model changes. All 6 OpenAPI/CORS regressions passed, retaining exactly 21 paths and no FHIR route. Static inspection confirmed no Domain FHIR SDK dependency and no Phase 6 API/content/download endpoint. Solution-wide formatting verification and `git diff --check` passed.

**Phase 6 R4 standards-validation unblocking status: COMPLETE — STATE A**

**Resolved MVP decisions:** Andrea selected FHIR R4 4.0.1 and deferred `RiskAssessment` because the current demo has no authoritative risk prediction/probability/mitigation inputs. The concrete mapping `beeexy-fhir-r4-base-mvp-v1` targets base R4 only, with no profile claims. It emits a `collection` Bundle containing `QuestionnaireResponse`, software `Device`, and `Provenance`; deterministic per-export UUID URNs provide every entry identity and all internal references. Frozen Beeexy question codes provide `item.linkId`. Frozen answer-schema types deterministically map supported answers to truthful `valueString`, `valueInteger`, `valueBoolean`, or `valueQuantity` values; choice values remain strings because no authoritative coding system is stored. Patient subject and Questionnaire canonical references are omitted under their optional base cardinalities rather than fabricated. `RiskAssessment`, `Composition`, and `Patient` are not generated.

**Concrete generation and validation:** Infrastructure pins Firely `Hl7.Fhir.R4` 6.4.0; Domain and Application retain SDK-neutral contracts and representations. The Firely POCO adapter produces official UTF-8 FHIR JSON. The same byte array is SHA-256 checksummed, stored immutably, reloaded, checksum-verified, strictly deserialized, recursively validated against the R4 POCO base model, and checked for the closed Bundle resource/reference contract. Valid content can transition to `Validated`; invalid content can transition to `ValidationFailed`. External terminology-server expansion is not executed and is distinguished by a sanitized warning. No migration, endpoint, public storage, external transmission, or Phase 6.7 work was added. See `docs/fhir/phase-6-r4-standards-validation-unblocking.md`.

**Historical note:** Phase 6.2–6.6 were correctly implemented as release-neutral State B foundations because release, profile applicability, identity/reference, `linkId`, typed answer, and RiskAssessment-scope decisions were unresolved at that time. Existing release-neutral artifacts keep their original bytes, unresolved release marker, and blocked eligibility; this unblocking applies only to newly generated exports using the new mapping identity.

**R4 unblocking verification:** The final Debug solution build completed with 0 warnings and 0 errors. All 77 Phase 6 interoperability unit tests passed, including all 8 direct concrete R4 generation, typed-answer, fixture, structural/reference-invalid, and real validation-lifecycle tests; the complete unit suite passed 567/567. All 36 focused FHIR persistence/lifecycle, migration, and OpenAPI/CORS integration tests passed, retaining exactly 21 OpenAPI paths and no FHIR route. The full integration suite ran 345 tests: 339 passed and exactly the same six pre-existing Phase 5 failures recorded in Phase 6.6 remained; no FHIR test failed. EF reported no pending model changes, no migration was added, Infrastructure is the only layer with the Firely package, and formatting plus `git diff --check` passed.

**Phase 6.7 status: COMPLETE**

**Phase 6.7 implementation:** Added exactly the three planned bearer-authenticated operations: `POST /api/v1/patients/{patientId}/fhir-exports`, `GET /api/v1/fhir-exports/{id}`, and `GET /api/v1/fhir-exports/{id}/content`. The POST accepts only a Clinical History event UUID and per-patient UUID idempotency key; the server fixes R4 4.0.1, `beeexy-fhir-r4-base-mvp-v1`, runtime identity, serialization, and validation. It composes the existing generation and validation use cases, returns `201` for a new validated export and `200` for replay, preserves safe `422` semantics for actual invalid FHIR, and distinguishes validator/storage outages with `503`. The shared owner/active-manager policy is reevaluated for every operation, while revoked managers, unrelated accounts, and absent patients/exports receive indistinguishable concealed `404` responses. Database advisory locks remain authoritative for generation idempotency and validation concurrency; the validation transaction reloads the committed state after a concurrent generation winner so one export cannot acquire contradictory results.

**Phase 6.7 metadata/download/privacy:** Metadata exposes only lifecycle status, truthful release/mapping versions, timestamps, and sanitized validation outcome/counts. Download permits only `Validated` exports matching the current R4 specification, reads the immutable artifact without regeneration, verifies SHA-256 before returning anything, and responds with the exact stored bytes as `application/fhir+json` and a technical export-ID filename. Pending, Generated, ValidationFailed, and historical release-neutral artifacts return `409`; historical metadata is not rewritten. Privacy-safe creation, validation-completion, successful-download, and integrity-rejection audit events contain no artifact JSON, raw answers/free text, token, storage path, or raw validator diagnostics. The existing Phase 6.1 schema fully supports these operations, so no migration or EF model change was added. See `docs/fhir/phase-6.7-export-api-and-acceptance.md`.

**Phase 6 final acceptance status: COMPLETE — STATE A.** New exports are genuinely serialized and validated as base FHIR R4 4.0.1 collection Bundles containing exactly QuestionnaireResponse, Device, and Provenance. Invalid content cannot be reported or downloaded as Validated, source clinical records remain immutable, and RiskAssessment remains explicitly deferred because prediction/probability/mitigation inputs are unavailable. The earlier Phase 6.2–6.6 State B history and all immutable release-neutral artifacts remain truthful. Phase 7 had not started at the time of this Phase 6 acceptance.

**Phase 6.7 verification:** The final Debug solution build completed with 0 warnings and 0 errors. All 13 focused Phase 6.7 access/error unit cases passed, all 90 Phase 6 unit regressions passed, and the complete unit suite passed 578/578. All 5 real authenticated Phase 6.7 API/PostgreSQL journeys passed, including owner/manager/revocation/IDOR concealment, sequential and concurrent idempotency, real R4 validation, exact-byte download, state/legacy gating, tamper rejection, safe validation failure, source immutability, and audit/privacy. All 40 focused FHIR, migration-behavior, and OpenAPI integration regressions, all 19 dedicated migration regressions, and all 13 repository-wide OpenAPI regressions passed. OpenAPI contains exactly 24 paths and adds only the three approved Phase 6 operations with Bearer security and `application/fhir+json` content. The complete integration suite ran 350 tests: 344 passed and exactly the same six pre-existing Phase 5 fixture/startup failures remained—the three `FreshDevelopmentDatabase_StartsSessionsForEveryDemoPathway` cases plus the three deliberately unavailable-database health/logging cases; no Phase 6.7 or FHIR test failed. EF reported no pending model changes, so no migration was added. Solution-wide formatting verification, static Domain/Application SDK inspection, and `git diff --check` passed.

---

# Phase 7 — Clinic, Doctor Directory, and Deterministic Matching

**Priority:** MVP CORE

**Phase 7.1 status: COMPLETE (2026-08-28).** The directory domain and persistence foundation is implemented. Phase 7.2 and later Phase 7 work remain intentionally unimplemented.

**Phase 7.1 implementation:** Added the neutral `Clinic`, `ClinicLocation`, `Doctor`, `DoctorAffiliation`, `DoctorCredential`, `Specialty`, `Language`, `InsurancePlan`, `DoctorInsuranceParticipation`, and standalone `DoctorMatchRuleVersion` foundations, plus normalized doctor-specialty and doctor-language relationships. Clinic locations require a domain-validated IANA timezone. Credential state is restricted to exactly `Submitted`, `PendingVerification`, `Verified`, and `Rejected`; the model stores claim/state metadata only and does not imply external credentialing. Migration `20260829012832_Phase71DirectoryFoundation` adds twelve normalized `directory` tables with UUID keys, unique clinic/doctor/catalog/version codes, restrictive foreign keys, a clinic/location-consistent affiliation key, normalized relationship uniqueness, status/timezone checks, and publication/location/specialty/language/insurance/credential indexes. No records are seeded. Match-rule versions are stored separately from doctors and contain no factor, weight, score, or configuration payload.

**Phase 7.1 verification:** Restore completed with all projects up to date; solution formatting verification formatted 0 of 524 files; the Debug solution build succeeded with 0 warnings and 0 errors. Focused Phase 7.1 unit tests passed 16/16, focused real-PostgreSQL directory and migration tests passed 8/8, and the relevant migration/FK regression set passed 19/19. The complete unit suite passed 688/688. The complete integration suite was run twice and each run completed 450 tests with 449 passed, 1 failed, and 0 skipped; no Phase 7.1 test failed. The observed non-directory failure was order-dependent in an existing current-account audit assertion and passed 1/1 when rerun alone, so it is recorded rather than hidden or reclassified as a Phase 7.1 failure. The fresh migration chain, Phase 7.1 rollback/reapply, Phase 1--6 preservation, zero directory seed rows, UUID/FK/index/check inspection, and restrictive delete behavior passed against PostgreSQL. EF reported no pending model changes. OpenAPI remained at the pre-7.1 count of 32 paths and contains no clinic or doctor route. Static scope inspection found no directory FHIR `Practitioner`/`Organization`, endpoint, geocoding/distance, or scoring additions, and `git diff --check` passed.

**Phase 7.2 status: COMPLETE (2026-08-28).** The product-approved synthetic demo directory import and internal publication/credential visibility boundary are implemented. Phase 7.3 and later directory API, filtering, search, and matching work remain intentionally unimplemented.

**Phase 7.2 implementation:** Added the source-controlled package `beeexy-synthetic-demo-directory@2026.08.29-demo.1` with expected SHA-256 content hash `82da23f40c8f92f135fb2413ccfc8e794f8bb7eb56e3a77bfe19a0d1d850601a`, stable UUIDs, and clearly synthetic clinics, locations, doctors, affiliations, credentials, specialties, languages, insurance plans, and stored doctor-insurance participation. The package includes published/unpublished records and every credential state. A package validator rejects duplicate identifiers/codes and invalid references. The PostgreSQL importer validates before writing, acquires a package-scoped transaction advisory lock, persists the complete graph and import ledger atomically, treats the same code/version/hash as an idempotent no-op, and rejects changed content under an existing version. The existing Development-only hosted demo bootstrap invokes the importer after migrations; Production startup does not import it. `PublicDirectoryVisibilityPolicy` and `PublicDirectoryQueryBoundary` admit only published clinics/doctors, require published parents/locations/affiliations, and admit only `Verified` credentials belonging to published doctors. Here `Published` and `Verified` are explicitly demo-dataset states, not real-world or external verification. Insurance participation remains stored directory data only, and the match-rule-version table remains empty.

**Phase 7.2 database and migration:** Migration `20260829040757_Phase72SyntheticDemoDirectoryImport` adds only `directory.demo_directory_imports`, a UUID-keyed immutable ledger with required package code, version, 64-character lowercase SHA-256 content hash, import timestamp, checks, and a unique package-code/version index. This minimal additive table is required to prevent changed content from silently reusing an imported version. Phase 7.1 tables and relationships are unchanged; imports remain separate from EF migrations and no directory records are seeded by migration execution.

**Phase 7.2 verification:** Restore completed with all projects up to date; solution formatting verification formatted 0 of 537 files; the Debug solution build succeeded with 0 warnings and 0 errors. All 8 focused Phase 7.2 unit cases passed; the combined Phase 7.1/7.2 unit set passed 24/24; all 7 focused real-PostgreSQL Phase 7.2 import/bootstrap/visibility tests passed; and the combined directory, fresh-chain, Phase 7.1/7.2 rollback/reapply, and FK regression set passed 18/18. The complete unit suite passed 696/696. The complete integration suite ran 458 tests with 457 passed, 1 failed, and 0 skipped; no Phase 7.2 test failed. A filtered rerun identified the unrelated failure as `DatabasePrivateAccessEndpointTests.SeparateCredentials_IssueSeparateNormalIdentitiesAndEnforcePatientIsolation`, and that test passed 1/1 when rerun alone, confirming order dependence without reclassifying it as a Phase 7.2 failure. EF reported no pending model changes. OpenAPI remained at 32 paths with no clinic, doctor, admin/import, or matching route. Static scope checks found no matching factors/weights/scores, geocoding/distance, ratings/reviews, real-time insurance, or FHIR `Practitioner`/`Organization` additions, and `git diff --check` passed.

**Phase 7.3 status: COMPLETE (2026-08-29).** The anonymous public clinic directory is implemented. Phase 7.4 doctor APIs and all search, ranking, and matching work remain intentionally unimplemented.

**Phase 7.3 implementation:** Added `ListClinics` and `GetClinic` with exactly `GET /api/v1/clinics` and `GET /api/v1/clinics/{id}`. The list projection contains only the stored clinic UUID, code, and name. Detail adds only eligible stored locations with UUID, name, locality, administrative area, country, and IANA timezone. Both endpoints are anonymous and explicitly describe the records as product-approved synthetic demo data rather than authoritative healthcare-provider information. Missing and unpublished clinic detail use the same safe `404`; publication flags, import metadata, ratings/reviews, coordinates/distance/maps, opening hours, availability, insurance claims, external verification, and fabricated counts are not exposed.

**Phase 7.3 filtering, pagination, and visibility:** The list supports exact stored-value `code`, `locality`, `administrativeArea`, and `country` filters with AND semantics. Location filters require one eligible location matching all supplied location fields. Unsupported, blank/invalid, or repeated filters return `422`. Pagination defaults to 20 and accepts 1--100, orders published clinics by UUID ascending, and uses an opaque canonical Base64URL cursor bound to the normalized filter set and an existing visible boundary row; malformed, filter-mismatched, or stale/hidden cursors return `422`. The repository composes all filters, visibility predicates, ordering, and lookahead limit in PostgreSQL before materialization. It reuses `PublicDirectoryQueryBoundary` for every clinic and location read; the boundary now also supplies the PostgreSQL UUID keyset query while retaining its single published-clinic predicate. Reads are no-tracking, list execution is one bounded query, and detail uses one clinic query plus one location query without N+1 loading. The immutable Phase 7.2 package/version/hash was not changed, and the existing schema and indexes were sufficient, so no migration or EF model change was added.

**Phase 7.3 verification:** Locked restore and solution formatting succeeded; the final Debug solution build completed with 0 warnings and 0 errors. All 8 focused Phase 7.3 unit cases passed, and all 12 focused real-PostgreSQL API/query cases passed, covering anonymous access, published/hidden records and locations, concealed detail, exact filters and empty pages, UUID keyset traversal, invalid cursor/filter handling, truthful contract shape, one-query list/two-query detail behavior, and exact OpenAPI scope. The Phase 7.1/7.2 directory, import, migration, and FK regression set passed 34/34. The complete unit suite passed 704/704. The complete integration suite ran 469 tests with 468 passed, 1 failed, and 0 skipped; no Phase 7.3 or directory test failed. The exact failure was the previously documented order-dependent `DatabasePrivateAccessEndpointTests.SeparateCredentials_IssueSeparateNormalIdentitiesAndEnforcePatientIsolation` row-count assertion (expected 2, observed 3), and it passed 1/1 when rerun alone. EF reported no pending model changes. OpenAPI contains exactly 34 paths, adding only the two anonymous clinic GET paths with truthful synthetic-data descriptions; no doctor, matching, ranking, search, importer, admin, or mutation endpoint was added. Formatting/static inspection and `git diff --check` passed.

**Phase 7.4 status: COMPLETE (2026-08-29).** The anonymous public doctor directory and deterministic stored-data filters are implemented. Phase 7.5 matching and all later Phase 7 work remain intentionally unimplemented.

**Phase 7.4 implementation and contract:** Added `SearchDoctors` and `GetDoctor` with exactly `GET /api/v1/doctors` and `GET /api/v1/doctors/{id}`. Both routes are anonymous. List and detail expose the same public-safe stored profile: doctor UUID, code, display name, specialty code/name values, language code/name values, eligible clinic affiliations with clinic UUID/code/name and optional eligible location UUID/name/locality/administrative area/country/IANA timezone, stored insurance-plan code/name participation, and eligible credential names. Missing and unpublished doctor detail use the same safe `404`. Internal publication/status/import metadata, credential evidence and non-public states, ratings/reviews, availability, coordinates/distance/maps, live insurance claims, external verification, matching fields, and recommendations are not exposed. The OpenAPI descriptions explicitly identify the records and `Verified` credentials as synthetic demo-dataset data and stored insurance participation as neither live coverage nor payer/network confirmation.

**Phase 7.4 filtering, pagination, and visibility:** The list accepts exactly `cursor`, `pageSize`, `specialtyCode`, `languageCode`, `locality`, `administrativeArea`, `country`, and `insurancePlanCode`. Codes use canonical exact stored-value matching; location parts use exact stored values and, when combined, must all match one eligible published affiliation location. All supplied dimensions narrow by intersection, never by score or partial-match count. Unsupported, repeated, blank/invalid, malformed-cursor, filter-mismatched-cursor, and stale/hidden-cursor requests return safe `422`; valid no-match queries return `200` with an empty page. Pagination defaults to 20, accepts 1--100, orders only by the stable doctor UUID ascending, and uses the shared canonical opaque Base64URL cursor mechanism bound to the normalized filters and an existing eligible boundary doctor. `PublicDirectoryQueryBoundary` remains the sole visibility boundary: doctors must be published; affiliations require published doctors, clinics, locations, and relationships; credential projection admits only `Verified`; and specialty, language, and stored-insurance relationships cannot bypass the published-doctor boundary. Filtering, visibility, UUID keyset ordering, and lookahead limiting run in PostgreSQL before materialization. List relationship loading uses five fixed bulk queries after the bounded doctor query, detail uses one doctor plus the same five fixed relationship queries, all reads are no-tracking, and there is no N+1 or full-directory/client-side filtering. Existing indexes were sufficient. The immutable Phase 7.2 dataset, package version/hash, schema, and migrations were not changed.

**Phase 7.4 verification:** Locked restore and solution formatting succeeded; the final Debug solution build completed with 0 warnings and 0 errors. All 10 focused Phase 7.4 unit tests passed; all 3 focused real-PostgreSQL query tests and all 18 focused API/OpenAPI tests passed. The Phase 7.1--7.3 unit regression set passed 32/32, the Phase 7.1/7.2 persistence/import/migration integration regression set passed 15/15, and the Phase 7.3 clinic API/query regression set passed 11/11. The complete unit suite passed 714/714. The complete integration suite ran 490 tests with 489 passed, 1 failed, and 0 skipped; no Phase 7.4 or directory test failed. The single failure was the previously documented order-dependent `DatabasePrivateAccessEndpointTests.SeparateCredentials_IssueSeparateNormalIdentitiesAndEnforcePatientIsolation`, and it passed 1/1 when rerun alone. EF reported no pending model changes. OpenAPI contains exactly 36 paths, adding only the two anonymous doctor GET paths; it contains no matching, scoring, ranking, recommendation, importer/admin, or doctor-mutation surface. Formatting/static scope inspection and `git diff --check` passed. `CalculateDoctorMatch`, factors, weights, scores, explanations, match auditing, AI/LLM, semantic/vector search, geocoding/distance, real-time insurance, and all Phase 7.5+ behavior remain unimplemented.

**Phase 7.5 status: COMPLETE (2026-08-29).** The internal versioned deterministic demo doctor-matching engine is implemented. Phase 7.6 public doctor-search integration and all later Phase 7 work remain intentionally unimplemented.

**Phase 7.5 rule, calculation, and auditability:** Added the immutable product-approved demo rule package `beeexy-demo-doctor-match-rules@2026.08.29-demo.1` with expected canonical SHA-256 content hash `2aefb8bfb21fadef1ad4bede0d4545988ddfc7c66dc5f79332555773756fd926`. It contains exactly four explicit 25-point factors: `specialty_exact` using `exact_canonical_doctor_specialty_relationship_v1`, `language_exact` using `exact_canonical_doctor_language_relationship_v1`, `location_exact` using `exact_same_eligible_affiliation_location_fields_v1`, and `stored_insurance_participation_exact` using `exact_stored_doctor_insurance_participation_v1`. `CalculateDoctorMatch` requires an exact rule version and accepts optional normalized specialty, language, locality, administrative-area, country, and stored-insurance-plan criteria. Only data admitted by `PublicDirectoryQueryBoundary` participates. The score is a deterministic integer from 0 through 100 using `sum_matched_weight_points_no_reweight_v1`: a matched factor contributes its configured weight, a non-match contributes zero, and missing input is `not_applicable_zero_contribution_v1` with no weight redistribution. Location parts must match the same eligible stored affiliation location. Every candidate result includes the rule package/version/hash, formula and semantics identifiers, maximum score, normalized nonclinical criteria, doctor UUID, total demo-match points, and all four canonically ordered factor records with semantics code, weight, `Matched`/`NotMatched`/`NotApplicable` state, contribution, explanation code, and ordered explanation data. Results order by score descending and then canonical UUID text ascending (`score_desc_uuid_text_asc_v1`). The immutable configuration plus complete deterministic structured output is the audit boundary; no per-request audit row, patient identity, diagnosis, urgency, timestamp, or other unnecessary health data is persisted. Repeated serialization is byte-identical for identical version, input, and eligible data.

**Phase 7.5 persistence, bootstrap, scope, and verification:** Migration `20260829070507_Phase75VersionedDoctorMatching` adds only `directory.doctor_match_rule_configurations`, a one-to-one restrictive extension of the existing rule-version table with package code, 64-character lowercase content hash, and four explicit integer weight columns constrained to positive values totaling 100. The independently versioned configuration importer validates before writing, uses a transaction and advisory lock, is idempotent for identical content, rejects changed or incomplete content under an existing version, and runs only from the Development demo bootstrap; Production imports nothing. The Phase 7.2 synthetic directory package and doctor rows remain unchanged. Locked restore and solution formatting succeeded; the final Debug solution build completed with 0 warnings and 0 errors. Focused Phase 7.5 unit tests passed 23/23, focused matching configuration/import/query tests passed 7/7 against PostgreSQL, and focused fresh-migration/schema/bootstrap tests passed 4/4. Phase 7.1--7.4 regression sets passed 42/42 unit, 15/15 directory persistence/import/migration, 11/11 clinic API/query, and 21/21 doctor API/query/OpenAPI. The complete unit suite passed 737/737. The complete integration suite ran 498 tests with 497 passed, 1 failed, and 0 skipped; no Phase 7.5 or directory test failed. The sole failure was the previously documented order-dependent `DatabasePrivateAccessEndpointTests.SeparateCredentials_IssueSeparateNormalIdentitiesAndEnforcePatientIsolation`, which passed 1/1 when rerun alone. EF reported no pending model changes. OpenAPI remains exactly 36 paths: `/api/v1/doctors` retains its neutral UUID ordering and unchanged response, no matching field or public matching endpoint was added, and `CalculateDoctorMatch` is internal only. Static inspection confirms the matching path makes no network, AI/LLM/ML, semantic/vector, geocoding/distance, ratings/reviews, live-insurance, external-verification, FHIR `Practitioner`/`Organization`, scheduling, or Phase 8 call. The demo weights are neither clinically validated nor production recommendation logic. Formatting/static scope inspection and `git diff --check` passed. Phase 7.6+ remains outstanding.

**Phase 7.6 status: COMPLETE (2026-08-29).** Matching is integrated into the existing anonymous `GET /api/v1/doctors` operation; no matching route or public rule-version selector was added. Supplying any normalized `specialtyCode`, `languageCode`, `locality`, `administrativeArea`, `country`, or `insurancePlanCode` activates the single product-approved demo rule `beeexy-demo-doctor-match-rules@2026.08.29-demo.1`. Every supplied dimension remains an exact Phase 7.4 hard filter with intersection semantics, including the same-eligible-location rule. The repository first materializes the complete publicly eligible filtered doctor-ID set, and an overload of the existing `CalculateDoctorMatch` use case restricts its Phase 7.5 repository snapshots to exactly those IDs before delegating unchanged to `DeterministicDoctorMatchEngine`; no score, weight, factor, explanation, or formula was copied into search, SQL, endpoint code, or DTO mapping. The engine's complete global result is paginated only after its exact `score_desc_uuid_text_asc_v1` ordering. Only the selected page's profiles are then loaded through the existing fixed bulk, no-tracking public projection. Because the public criteria are also hard filters, every survivor naturally receives the configured points for every supplied applicable factor; true ties remain canonical UUID-text ascending. A valid criteria set with no survivors returns `200` with an empty page. With no criteria, Phase 7.4 neutral UUID-ascending pagination remains active, the matching rule is not read, and the response omits `match`, avoiding an all-zero recommendation representation.

**Phase 7.6 public contract, cursor, and boundary:** A match-active search item adds only `match.ruleVersion`, integer `match.matchScore`, and four canonically ordered factors containing `factorCode`, `semanticsVersion`, `configuredWeightPoints`, `state`, `contributionPoints`, `explanationCode`, and ordered key/value `explanationData`. Configuration hashes, package codes, formula/audit internals, publication state, hidden relationships, and patient or clinical data remain private. The exact four 25-point factors, 0--100 maximum, missing-input `NotApplicable`/zero/no-reweighting behavior, formula, and explanations remain Phase 7.5-owned and unchanged. The versioned ranked cursor payload is opaque canonical Base64URL and binds the complete normalized filter/matching criteria, exact approved rule version, last score, and canonical doctor UUID tie key. Resume recalculates the same exact-version filtered traversal, requires the boundary doctor and score still to exist, and then continues score-descending/UUID-text-ascending; malformed, tampered/noncanonical, criteria-mismatched, stale/hidden-boundary, score-mismatched, or incompatible-version cursors return the existing safe `422`. Neutral no-criteria traversal retains the Phase 7.4 cursor. `PublicDirectoryQueryBoundary` continues to exclude unpublished doctors, clinics, locations and affiliations from candidates and contributions, while credentials remain limited to the demo-dataset `Verified` state. Stored insurance means exact synthetic stored participation only, never current eligibility, coverage, payer confirmation, or real-time in-network status. Location means exact eligible stored fields only, with no geocoding or distance. The OpenAPI operation explicitly states that the demo score is not probability, confidence, provider quality, medical suitability, clinical validation, or production recommendation logic. Doctor detail is unchanged.

**Phase 7.6 persistence, verification, and remaining scope:** No schema, migration, dataset, factor, weight, formula, rule package/version/hash, or dependency changed; EF reports no pending model changes. Locked restore succeeded, solution formatting verification completed, and the Debug solution build succeeded with 0 warnings and 0 errors. Focused Phase 7.4--7.6/Phase 7.5 unit coverage passed 35/35. Focused real-PostgreSQL doctor API, ranked pagination/cursor/version/explanation, directory-query, matching-query, and match-import coverage passed 38/38, including exact 25/75/100 scores, exact factors/states/contributions, hard-filter combinations, global ties across page boundaries, repeat stability, all six criteria-mismatch classes, incompatible rule versions, neutral omission, visibility, detail regression, and the public contract. The complete unit suite passed 739/739. The complete integration suite ran 508 tests with 507 passed, 1 failed, and 0 skipped; all ten new Phase 7.6 integration cases and all Phase 7.1--7.5 regressions passed. The exact failure remains the pre-existing order-dependent `DatabasePrivateAccessEndpointTests.SeparateCredentials_IssueSeparateNormalIdentitiesAndEnforcePatientIsolation` assertion (expected 2, observed 3): it reproduced as the sole failure in its three-test class and passed 1/1 alone. OpenAPI remains exactly 36 paths and adds no matching path. Static inspection confirms the public matching path performs no network, AI/LLM/ML/vector, geocoding/distance, ratings/reviews, live-insurance, external-verification, FHIR `Practitioner`/`Organization`, scheduling, or Phase 8 call. `git diff --check` passed. Phase 7.7 final security, query-performance, acceptance-matrix, and comprehensive Phase 7 hardening remain outstanding; Phase 7 as a whole is not yet marked complete.

**Phase 7.7 status: COMPLETE (2026-08-29). Phase 7 status: COMPLETE. Phase 8 status: NOT STARTED.** Final acceptance preserved exactly four anonymous Phase 7 operations (`GET /api/v1/clinics`, `GET /api/v1/clinics/{id}`, `GET /api/v1/doctors`, and `GET /api/v1/doctors/{id}`) and the existing 36-path OpenAPI surface. Invalid bearer credentials do not alter those anonymous responses. Publication and eligibility remain centralized in `PublicDirectoryQueryBoundary`: unpublished clinics, doctors, locations, and affiliations cannot become candidates or public relationships, and only `Verified` credentials can be projected. Security acceptance also rejects malformed, unknown-version, overlong, blank, extreme-page-size, malformed-identifier, and empty-identifier inputs with safe existing `404`/`422` contracts; response checks exclude persistence/provider details, stack traces, cursor internals, package/configuration metadata, credential workflow state/evidence, and hidden synthetic records. Valid unknown casing and Unicode inputs retain exact-match semantics and return empty successful pages. Public wording remains explicit that the directory, affiliations, stored insurance participation, and matching rule are synthetic/demo data; insurance is not live eligibility or coverage, and matching is not clinical suitability, provider quality, probability, confidence, or production recommendation logic.

**Phase 7.7 matching and pagination acceptance:** The existing engine remains the only score authority and retains exactly four 25-point factors, a 0--100 additive maximum, no reweighting, missing-input `NotApplicable`/zero-contribution behavior, and canonical factor/explanation ordering. Search still applies every supplied criterion as an exact hard-filter intersection before ranking and never relaxes criteria. Ranked traversal evaluates the complete eligible filtered candidate set, orders globally by score descending then canonical UUID text ascending, and paginates only afterward; neutral no-criteria traversal stays UUID ascending and omits `match`. Existing and new coverage verifies empty results, deterministic repeats, cross-page ties, cursor/filter/rule/score/boundary binding, stale or hidden boundaries, all filter combinations, and exhaustive ranked and neutral pagination without duplicates or omissions.

**Phase 7.7 query, index, and materialization acceptance:** Real-PostgreSQL acceptance captures and safely runs `EXPLAIN (FORMAT JSON)` for eleven representative shapes: clinic unfiltered, clinic location-filtered, doctor neutral, specialty, language, location, insurance, combined filters, neutral continuation, matching-active first page, and ranked continuation. It asserts publication predicates, relevant joins/`EXISTS`, stable ordering/limits where applicable, UUID continuation binding, and parameterization of all user-supplied values without imposing environment-sensitive planner-node or timing thresholds. Existing indexes cover clinic/doctor publication, clinic locality/area/country and clinic publication, affiliation doctor/clinic/location joins and publication, reverse specialty/language/insurance lookup, and credential doctor/status lookup; there is no evidence-based need for another index or migration. Clinic listing is one bounded query and detail is two; neutral doctor profile loading uses one bounded base query plus five fixed bulk projections. Active ranked search intentionally uses a fixed 13-query traversal and materializes the whole already-filtered eligible candidate set so the Phase 7.5 engine can preserve exact global ordering. That bounded-query design avoids per-row N+1 behavior and is accepted for the synthetic demo MVP; database-side ranking and production-volume benchmarking remain later scale work and are not claimed here.

**Phase 7.7 persistence and final verification:** No production code, schema, migration, seed/package content, factor, weight, formula, rule identity, dependency, endpoint, or public DTO changed; only acceptance coverage and this plan record were added. Locked restore succeeded; formatting verification and `git diff --check` passed; the Debug solution build completed with 0 warnings and 0 errors; EF reported no pending model changes. All three Phase 7 migration rollback/reapply checks passed 3/3. New Phase 7.7 acceptance passed 5/5, focused directory/matching unit coverage passed 44/44, and the focused Phase 7 integration regression set passed 73/73. The complete unit suite passed 739/739. The complete integration suite now runs 513 tests with 512 passed, 1 failed, and 0 skipped; every Phase 7 test passes. The sole failure is the already documented order-dependent private-access assertion `DatabasePrivateAccessEndpointTests.SeparateCredentials_IssueSeparateNormalIdentitiesAndEnforcePatientIsolation` (expected 2, observed 3), which also reproduces within its own class on a fresh PostgreSQL container and passes 1/1 alone. Phase 7 tests perform read-only directory requests or directory-only migration/import/query setup and do not write the identity or patient-profile state involved, so the failure is isolated from Phase 7. All Phase 7 acceptance criteria are satisfied. Phase 8 has not been implemented or started.

## 1. Objective

Provide a public internal doctor directory with first-class clinics and an explainable deterministic matching algorithm.

## 2. Scope

- Published clinics/locations/doctors/affiliations.
- Credential verification state and verified public claims.
- Specialty, language, location, and stored insurance filters.
- Versioned deterministic matching with factor explanations.
- A product-approved synthetic/demo directory dataset; doctors, clinics, locations, specialties, languages, insurance, affiliations, and credentials may be generated for the demo and must not be presented as real, externally verified, or authoritative data.

## 3. Explicitly Out of Scope

- Doctor/clinic onboarding portals, reviews/ratings, real-time eligibility, inferred credentials, AI/LLM scoring, unapproved geocoding/distance logic, invented FHIR `Practitioner`/`Organization`, and full tenant/branding configuration.
- Any claim that makes synthetic demo doctors, clinics, or their data appear real, externally verified, or authoritative.

## 4. Domain Model

- Entities: `Clinic`, `ClinicLocation`, `Doctor`, `DoctorAffiliation`, `DoctorCredential`, `Specialty`, `Language`, `InsurancePlan`, `DoctorInsuranceParticipation`, `DoctorMatchRuleVersion`.
- Credential status: `Submitted`, `PendingVerification`, `Verified`, `Rejected`.
- Invariants: only published records/verified claims are public; match factors/version are explainable/auditable; stored insurance data is not represented as real-time verification. For the approved demo dataset, `Published` means approved for visibility within the demo experience, and `Verified` claims/credentials means verified within that dataset only; neither represents real credentialing, external verification, or production professional validation. Submitted, `PendingVerification`, rejected, and other unauthorized evidence remain non-public.

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
- Import/seed the product-approved synthetic/demo directory dataset through deployment tooling, not patient-facing APIs.

## 8. Authentication and Authorization

Anonymous read is allowed only for approved public fields. No patient-specific result is exposed unless future matching inputs require authenticated context.

## 9. Security and Privacy

- Submitted/rejected credential evidence is never returned publicly.
- No fabricated ratings or credentials represented as real or externally verified.
- Match audit records contain factors/version, not unnecessary health details.
- Public demo data and labels must not imply that synthetic professionals, clinics, claims, or credentials are real or externally verified.

## 10. External Integrations

- **IMPLEMENT NOW:** none.
- **INTERFACE/PLACEHOLDER:** future authoritative directory sources/import and approved geocoding.
- **POST-MVP:** onboarding, real-time insurance, reviews.

## 11. FHIR Impact

None for MVP; no Practitioner/Organization mapping is invented.

## 12. Tests

- Publication and credential-state visibility.
- Deterministic score repeatability, versioned demo factor weights, tie ordering, and explanations; the same inputs and version produce the same result.
- Specialty/language/location/insurance filters.
- Explicit absence of reviews/ratings and real-time eligibility claims.
- Pagination/index-backed query tests.
- Mandatory endpoint test matrix for all four endpoints.

## 13. Acceptance Criteria

- Anonymous users find/view only published verified data; for the demo dataset, `Published` and `Verified` retain their demo-only meanings and do not imply real credentialing or external verification.
- Clinic is first-class.
- Matching uses product-approved deterministic demo factors and weights, is explainable, auditable, versioned, repeatable for the same inputs/version, and contains no LLM decision; it is not clinically validated or production recommendation logic.
- All tests pass.

## 14. Dependencies

- Phase 1.
- Product approval of the synthetic/demo directory dataset before import and of deterministic demo matching factors and weights before use. The absence of authoritative real directory data or production matching factors/weights does not block the MVP/demo.

## 15. Deferred / TBD Items

- Authoritative real directory data and sources, real credentialing/external verification, production matching rules/weights and clinical/product validation, approved distance/geocoding source and logic, onboarding/verification workflows, credential-document retention, real-time network verification, reviews, and white-label configuration.

---

# Phase 8 — Availability and Appointment Requests

**Priority:** MVP CORE

## 1. Objective

Allow authenticated identities with authority over a PatientProfile to request and manage Beeexy-held appointment slots and minimally authorized clinic schedulers to confirm or reject requests, while making double booking database-impossible, preserving clinic-local scheduling semantics, and retaining complete immutable audit history.

## 2. Scope

- Explicitly stored `AvailabilitySlot` records loaded through an idempotent seed/import mechanism that references the Doctor, Clinic, and Location directory entities from Phase 7.
- Public discovery of published, future, currently unreserved slots, with explicit query ranges and a bounded default window.
- Authenticated appointment booking that always creates the Appointment as `Requested`, immediately reserves its slot, and is idempotent within the authenticated-account boundary.
- Appointment listing/detail, patient cancellation from `Requested` or `Confirmed`, and transactional rescheduling from `Requested` or `Confirmed` without changing Appointment identity or status.
- Minimal clinic-side confirmation/rejection through the clinic-scoped `AppointmentScheduler` permission, without a clinic portal, clinic accounts, or full Doctor/Clinic RBAC.
- Official status model, immutable append-only status history, separate auditable reschedule records, concurrency-safe mutations, and stable `ProblemDetails` errors.
- Explicit clinic timezone and UTC-instant handling, `InPerson` and `Virtual` modalities, and database-enforced prevention of duplicate slot reservations.

## 3. Explicitly Out of Scope

- Payments, copays, Stripe/payment-provider integration, Google Meet or other video-meeting implementation, Google Calendar, Outlook Calendar, external calendar synchronization, and automatic recurring slot generation.
- Recurring availability management, availability-management APIs, clinic scheduling administration UI, clinic portal, clinic onboarding, clinic accounts, full Doctor/Clinic roles, general permission administration, and production permission-policy administration.
- FHIR Appointment generation/export, automatic Pre-Triage sharing, Clinical History sharing, intake-form replacement, AI scheduling, and any scheduling-triggered clinical-data sharing or consent change.
- Arbitrary time-range overlap scheduling, `Completed` transition API, `NoShow` transition API, and clinic transitions beyond the minimum confirm/reject mechanism.
- Speculative infrastructure for deferred capabilities, including a video-meeting provider abstraction when no current domain boundary needs meeting metadata.

## 4. Domain Model

- Entities: `AvailabilitySlot`, `Appointment`, immutable `AppointmentStatusHistory`, and an immutable append-only Appointment reschedule audit record. A reschedule audit record captures the Appointment, old and new slots, actor, and UTC timestamp; it does not manufacture a status transition.
- Official statuses remain `Requested`, `Confirmed`, `Cancelled`, `Completed`, `NoShow`, and `Rejected`. `Completed` and `NoShow` belong to the domain model, but their transition APIs are deferred beyond Phase 8.
- A new Appointment always starts as `Requested`. Allowed Phase 8 status transitions are `Requested -> Confirmed`, `Requested -> Rejected`, `Requested -> Cancelled`, and `Confirmed -> Cancelled`. Confirm/reject accept only `Requested`; cancellation accepts only `Requested` or `Confirmed`.
- `Requested` and `Confirmed` reserve the selected slot. `Rejected` and `Cancelled` release it. Appointments and their audit/history records are never physically deleted.
- An already successfully applied identical action is idempotent where appropriate, including same-action confirm/reject retries. An opposite or otherwise invalid transition returns `409 Conflict` and changes neither Appointment state nor history.
- `Requested` and `Confirmed` appointments may be rescheduled without a maximum count or minimum time-before-appointment restriction. Rescheduling preserves Appointment identity and current status, and atomically reserves the target before releasing the old slot. Failure to reserve the target rolls the complete operation back, retains the old slot association, and returns `409 Conflict` for a reservation conflict.
- `AvailabilitySlot` identifies or references its doctor, clinic, location, start instant, end instant, clinic timezone, supported modality, and publication/availability state. Only published, future, currently unreserved slots are discoverable or bookable; past and unpublished slots cannot be booked.
- Demo seed slots use 30-minute durations unless existing Phase 7/demo data explicitly requires otherwise. Duration is represented by start/end instants, and the domain does not assume all future slots are 30 minutes. Interval calculations use half-open `[start, end)` semantics.
- Clinic timezone is an explicit IANA identifier and is never inferred from server timezone, deployment region, patient device, or browser. Slot and Appointment instants use an unambiguous UTC representation while retaining clinic timezone for clinic-local interpretation/display. New York demo clinics use `America/New_York` unless their imported directory configuration supplies another correct timezone.
- Appointment modality is an extensible value/enum with Phase 8 values `InPerson` and `Virtual`. The requested modality must equal the slot modality; mismatch is `422 Unprocessable Entity`. `Virtual` does not mean that Beeexy creates a video meeting.
- Appointment reason is optional sensitive free text with a maximum length of 500 characters. It is not interpreted by AI, converted to a clinical category, or supplied to Pre-Triage.
- Appointment request idempotency key is a client-supplied UUID scoped to authenticated account + idempotency key. Keys do not expire in Phase 8; an identical retry returns the original Appointment, while incompatible payload reuse returns `409 Conflict`.
- Appointment creation appends an initial `AppointmentStatusHistory` entry with null/not-applicable previous status and new status `Requested`. Every applied status transition appends exactly one entry containing Appointment, previous/new status, actor, UTC timestamp, and an action/transition type where needed. Entries are ordered, immutable, and never updated or deleted.
- The aggregate permits future stricter cancellation and rescheduling policies without redesign; Phase 8 has no minimum cancellation/rescheduling lead time, cancellation penalty, payment consequence, or reschedule limit.

## 5. Database Changes

- Add `scheduling.availability_slots`, `appointments`, `appointment_status_history`, and durable append-only reschedule-audit storage, using UUID primary keys and Doctor/Clinic/Location/PatientProfile/slot foreign keys as applicable.
- Store appointment/slot instants as UTC instants and retain the slot's explicit IANA clinic timezone. API representations are unambiguous ISO-8601 values.
- Enforce a PostgreSQL unique partial index/constraint (or equivalent database invariant) allowing at most one Appointment whose status is `Requested` or `Confirmed` for a slot. `Rejected` and `Cancelled` rows remain but do not reserve the slot; future arbitrary time-range overlap detection is deferred.
- Enforce unique authenticated account + idempotency key, persist the UUID key and sufficient canonical request identity/fingerprint to distinguish identical retries from incompatible reuse, and retain these records without Phase 8 expiry.
- Index patient/time/status, doctor/time, and clinic/time for the required availability and Appointment queries.
- Use database transactions plus existing Beeexy/EF Core concurrency patterns for creation, status transitions, history append, cancellation, and rescheduling. Concurrent incompatible mutations such as confirm versus reject allow at most one valid transition; the loser returns `409` without duplicate history, corrupt slot ownership, or partial updates.
- Translate the expected reservation/idempotency/concurrency constraint violations into stable scheduling `ProblemDetails` conflict responses. Two concurrent requests for one slot must yield exactly one successful Appointment and one `409`.
- Preserve historical Appointment integrity when referenced directory records change or become unpublished: unpublishing never cascades to delete an Appointment. Do not add full Doctor/Clinic/Location snapshots unless an existing architectural requirement already mandates them.

## 6. API Endpoints

| Method / route | Authentication | Authorization | Purpose | Response | Validation and errors |
|---|---|---|---|---|---|
| `GET /api/v1/doctors/{doctorId}/slots` | None | Public published inventory | List only future, published, unreserved slots in an optional explicit time range; default next 30 days, maximum 90-day window | `200` | Unknown doctor `404`; invalid/over-limit range `422` |
| `POST /api/v1/appointments` | Bearer | Existing authority over `patientId` PatientProfile | Request `{ patientId, slotId, modality, idempotencyKey, reason? }`; `reason` max 500; key is UUID | `201` Requested appointment; identical retry returns original Appointment | Concealed patient `404`; duplicate reservation or incompatible key reuse `409`; past/unpublished slot, modality mismatch, or domain validation `422` |
| `GET /api/v1/appointments` | Bearer | Existing authority over returned PatientProfiles | Cursor-paginated list with patient, status, and relevant time-range/upcoming-versus-historical filters | `200` page, including cancelled/rejected records | Invalid filter/cursor `422`; inaccessible patient resources follow concealed `404` rules where applicable |
| `GET /api/v1/appointments/{id}` | Bearer | Existing authority over Appointment PatientProfile | Scheduling detail plus complete ordered status history | `200` | Nonexistent or inaccessible Appointment `404`; no automatic Pre-Triage/Clinical History data |
| `POST /api/v1/appointments/{id}/confirm` | Bearer | `AppointmentScheduler` for Appointment clinic | Apply `Requested -> Confirmed` | `200` Confirmed appointment | Repeat confirm idempotent; nonexistent `404`; missing/cross-clinic permission `403`; invalid transition/concurrency `409` |
| `POST /api/v1/appointments/{id}/reject` | Bearer | `AppointmentScheduler` for Appointment clinic | Apply `Requested -> Rejected`, retain Appointment, release slot | `200` Rejected appointment | Repeat reject idempotent; nonexistent `404`; missing/cross-clinic permission `403`; invalid transition/concurrency `409` |
| `POST /api/v1/appointments/{id}/cancel` | Bearer | Existing authority over Appointment PatientProfile | Apply `Requested/Confirmed -> Cancelled`, retain Appointment, release slot | `200` Cancelled appointment | Nonexistent/inaccessible `404`; repeat cancellation idempotent where appropriate; invalid transition/concurrency `409` |
| `POST /api/v1/appointments/{id}/reschedule` | Bearer | Existing authority over Appointment PatientProfile | Transactionally move a `Requested` or `Confirmed` Appointment to a compatible target slot while preserving identity/status | `200` rescheduled appointment | Nonexistent/inaccessible `404`; unavailable target `409`; invalid state/concurrency `409`; past/unpublished target or modality/domain validation `422` |

All eight endpoints use existing Beeexy `ProblemDetails` and stable machine-readable error codes: `401` for missing/invalid authentication; `403` for an authenticated scheduler without the required clinic scope; `404` for nonexistent or deliberately concealed inaccessible resources; `409` for double booking, concurrency conflict, incompatible idempotency reuse, or invalid transition; and `422` for a syntactically valid request that violates scheduling/domain validation. No second error envelope is introduced.

## 7. Application / Use Cases

- `ListAvailableSlots`, `RequestAppointment`, `ListAppointments`, `GetAppointment`, `ConfirmAppointment`, `RejectAppointment`, `CancelAppointment`, `RescheduleAppointment`.
- Provide an idempotent availability seed/import path for demo inventory; do not add availability-management APIs. Seeded slots reference Phase 7 directory entities and use their configured clinic timezone.
- `ListAvailableSlots` applies the future/published/unreserved rules, optional range, 30-day default, 90-day maximum, and half-open interval semantics.
- `RequestAppointment` validates PatientProfile authority, required request fields, UUID idempotency key, modality equality, slot publication/future state, and optional reason length before atomically creating the `Requested` Appointment, initial history entry, and reservation. Booking invokes neither Pre-Triage nor any clinical-sharing flow.
- State transitions validate the official state machine and actor, update state/reservation, and append exactly one immutable history entry in one transaction. Same-action success retries are idempotent; incompatible or losing concurrent transitions are `409` and do not mutate state/history.
- Cancellation has no Phase 8 minimum lead time, penalty, or payment consequence and applies from `Requested` or `Confirmed`. Its policy boundary remains replaceable by stricter future rules.
- Rescheduling has no Phase 8 minimum lead time or count limit, preserves identity/status, creates an immutable reschedule audit record, and reserves the target before releasing the old slot within one transaction. Any failure rolls back all changes.
- Map expected database reservation, idempotency, and concurrency failures to stable `409` `ProblemDetails`; map scheduling validation failures to stable `422` codes.
- Define `IVideoMeetingProvider` only if a genuine current domain boundary requires meeting metadata; otherwise introduce no placeholder or speculative infrastructure.

## 8. Authentication and Authorization

- Slot discovery is anonymous.
- Booking, Appointment listing/detail, cancellation, and rescheduling require bearer authentication and reuse the existing PatientProfile authority model; no scheduling-specific ownership model is created.
- Patient authority includes the patient/profile owner and an active authorized manager/dependent relationship supported by the existing system. Revocation immediately removes the corresponding scheduling authority; booking never grants authority.
- Use the existing concealed `404` behavior where required to hide inaccessible patient resources. Missing/invalid authentication is `401`.
- Confirm/reject require bearer authentication and only the narrow `AppointmentScheduler` permission scoped by `clinicId`. A scheduler may act for multiple clinics only when explicitly assigned to each and may confirm/reject only Appointments for an assigned clinic.
- Approved scheduler identities and clinic assignments are explicit deployment/demo seed configuration, not hard-coded domain rules or an architectural blocker. Authenticated identities missing the permission, including a scheduler for another clinic, receive `403`; nonexistent Appointment is `404`; invalid transition is `409`.
- `AppointmentScheduler` grants no implicit access to Pre-Triage, Clinical History, FHIR exports, clinical profile information, or other patient clinical data. Full Doctor/Clinic RBAC, onboarding, permission administration, clinic accounts, and portals remain deferred.

## 9. Security and Privacy

- Treat Appointment reason as sensitive: never log it, clinical text, or complete Appointment request payloads, and exclude reason from telemetry payloads. Non-sensitive operational identifiers and transition metadata may be logged only as required for audit/diagnostics.
- Booking and confirmation do not share or expose Pre-Triage; booking does not expose Clinical History; scheduling does not alter clinical consent; and no Appointment operation implicitly shares Pre-Triage, Clinical History, FHIR exports, or other clinical information.
- Booking never grants additional patient authority, and `AppointmentScheduler` never grants clinical-data access.
- Creation, confirmation, rejection, cancellation, and rescheduling are durably audited. Status history and reschedule audit records are append-only and immutable; Appointments are never deleted.

## 10. External Integrations

- **IMPLEMENT NOW:** none.
- **INTERFACE/PLACEHOLDER:** none by default. Define `IVideoMeetingProvider` only if a genuine Phase 8 domain requirement for meeting metadata emerges; `Virtual` alone is not such a requirement.
- **POST-MVP:** Google Meet or other video meetings, payments/copays, Google/Outlook Calendar, and other external-calendar synchronization.

## 11. FHIR Impact

No FHIR Appointment is generated or exported in Phase 8, and scheduling never implicitly exposes existing FHIR exports. Any later Appointment mapping/export must follow `docs/fhir/beeexy-coleccion-recursos.md`; no mapping is invented here.

## 12. Tests

- Domain tests cover initial `Requested`, all allowed Phase 8 transitions, rejected invalid/opposite transitions, same-action idempotency, reservation/release rules, reason length, modality matching, and the lack of Phase 8 lead-time/reschedule-count restrictions.
- Persistence/integration tests prove the partial unique reservation invariant, retained cancelled/rejected records, non-expiring account-scoped idempotency, identical retry response, incompatible-key reuse `409`, ordered immutable status history, exactly one history entry per applied transition, and separately auditable reschedules.
- Concurrency integration tests issue two simultaneous bookings for one slot and require exactly one success and one `409`; concurrent confirm versus reject (and equivalent incompatible mutations) permit one winner, one `409`, one applied history entry, and consistent slot ownership.
- Rescheduling tests cover both `Requested` and `Confirmed`, preserved identity/status, successful atomic target reservation/old-slot release, and complete rollback to the previous slot when target reservation fails.
- Authorization tests cover patient owner, active manager/dependent authority, revoked authority, concealed `404`, unauthenticated `401`, missing scheduler permission `403`, cross-clinic scheduler `403`, multiple explicitly assigned clinic scopes, and absence of clinical-data access from scheduling permission.
- Availability/API tests cover only future/published/unreserved results, past/unpublished booking rejection, unknown doctor `404`, default 30-day and maximum 90-day ranges, invalid range `422`, half-open boundaries, explicit IANA timezone round-trips, and DST boundary behavior.
- Security tests or test-safe telemetry assertions prove reason and complete request payloads are absent from application logs/telemetry and that booking/confirmation do not invoke Pre-Triage, expose Clinical History/FHIR data, or alter consent.
- Apply the mandatory endpoint matrix to all eight Phase 8 endpoints: success, validation, authentication, authorization/ownership, missing-resource, conflict/idempotency where applicable, persistence side effects, response contract, and regression coverage.
- Run the complete existing test suite in addition to the Phase 8 domain, application, persistence, concurrency, security, and API/integration tests.

## 13. Acceptance Criteria

1. All eight Phase 8 endpoints are implemented.
2. Appointment creation always starts as `Requested` and creates its initial history entry.
3. `Requested` immediately reserves the selected slot.
4. Database constraints prevent duplicate reservations.
5. Two concurrent booking attempts for one slot produce exactly one success and one `409`.
6. An identical account-scoped booking retry returns the originally created Appointment without another reservation.
7. Incompatible reuse of the same idempotency key returns `409`.
8. `Requested -> Confirmed` works.
9. `Requested -> Rejected` works.
10. Patient cancellation works from both `Requested` and `Confirmed` without a Phase 8 lead-time rule, penalty, or deletion.
11. Rejected and cancelled Appointments release their slots.
12. Appointment records remain queryable and are never physically deleted.
13. Complete ordered status history is immutable and contains the creation entry plus exactly one entry per applied transition.
14. Same-action confirm/reject retries are idempotent.
15. Opposite/invalid or losing concurrent transitions return `409` without changing Appointment state/history, duplicating history, or corrupting slot ownership.
16. Rescheduling from `Requested` and `Confirmed` preserves Appointment identity/status and is transactional.
17. A failed target-slot reservation returns the appropriate error, rolls back completely, and leaves the Appointment associated with its previous slot.
18. Every successful reschedule is separately and immutably auditable without a fake status transition.
19. Patient/profile-owner and active manager/dependent authority are enforced consistently for booking, list, detail, cancellation, and rescheduling.
20. Revoked PatientProfile management authority is respected, and booking grants no new authority.
21. `AppointmentScheduler` is clinic-scoped, with explicitly multi-clinic assignments limited to their configured clinics.
22. Missing and cross-clinic scheduler permissions fail with `403`.
23. Scheduler permission grants no clinical-data access.
24. Clinic IANA timezone, UTC persistence/API representation, clinic-local display interpretation, and DST boundary behavior are verified without dependence on server/device/browser timezone.
25. Appointment reason validation enforces optional free text up to 500 characters, and reason/complete request payload content is absent from application logs and telemetry.
26. Booking/confirmation neither invoke nor share Pre-Triage, expose Clinical History/FHIR data, nor alter clinical consent.
27. All eight endpoints pass the mandatory API/integration test matrix, including existing `ProblemDetails` and defined `401`/`403`/`404`/`409`/`422` semantics.
28. The complete existing test suite remains green.

## 14. Dependencies

- Phase 2 identity/authentication and existing PatientProfile authority.
- Phase 7 Doctor, Clinic, and Location directory entities and imported configuration, which scheduling references rather than duplicates.
- Phase 3 only for Appointment operations involving managed/dependent PatientProfiles.
- Explicit deployment/demo selection and seed configuration of authenticated scheduler identities and clinic assignments for `AppointmentScheduler`. The exact email/account values are operational configuration supplied before the clinic-side demo, not an architectural or product-decision blocker.

## 15. Deferred / TBD Items

- Stricter production cancellation/rescheduling windows, penalties, limits, and broader clinic transition policy; `Completed` and `NoShow` transition APIs.
- Recurring availability management/generation, clinic scheduling administration UI, availability-management APIs, arbitrary time-range overlap scheduling, and external calendar synchronization.
- Clinic portal/accounts/onboarding, full Doctor/Clinic RBAC, general permission administration, and production scheduler-assignment workflows.
- Google Meet or other video-meeting integration, Google/Outlook Calendar integration, payments, copays, billing, and payment providers.
- FHIR Appointment generation/export, automatic Pre-Triage or Clinical History sharing, intake-form integration/replacement, and AI scheduling.

**Phase 8.8 status: COMPLETE (2026-08-31). Phase 8 status: COMPLETE. Phase 9 status: NOT STARTED.** Final acceptance preserves exactly eight Phase 8 scheduling operations across seven paths: anonymous availability discovery; authenticated booking, list, detail, cancellation, and rescheduling under current PatientProfile authority; and clinic-scoped scheduler confirmation/rejection. The official lifecycle, reserving statuses, account-scoped idempotency, database-enforced double-booking protection, immutable ordered status history, separate immutable reschedule audit, cancellation release, and transactional reschedule rollback invariants are all covered by deterministic domain, application, real-PostgreSQL API, persistence, race, and stale-write tests. Historical cancelled/rejected appointments remain queryable, public availability reflects every reservation/release outcome, and cross-clinic scheduler and revoked patient authority fail closed.

**Phase 8.8 hardening and final verification:** The acceptance audit found no production-code or schema defect; no production code, migration, API path, product policy, or external integration changed. A focused `Phase8Acceptance` test layer now labels the existing granular coverage, with additional acceptance checks for the exact eight-operation OpenAPI matrix, sensitive Appointment reason/idempotency/token logging exclusion, absence of scheduling-triggered Pre-Triage/Clinical History/FHIR mutations, New York spring-forward and fall-back UTC/IANA behavior, concurrent advisory-locked demo availability import convergence, and all critical scheduling indexes including reservation, idempotency, status-history sequencing, list/availability lookup, and reschedule-history ordering. Focused Phase 8 tests passed 92/92 unit and 84/84 PostgreSQL integration tests. The complete unit suite passed 836/836 and the complete PostgreSQL integration suite passed 599/599. The Phase 8 migration rollback/reapply test passed, OpenAPI remains exactly 43 paths, the final Debug build completed with zero warnings/errors, EF reported no pending model changes, and `git diff --check` passed. No Phase 9 or POST-MVP scheduling functionality was started.

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

**Phase 10.1 status:** COMPLETE (2026-09-01)
**Phase 10.1 implementation:** Added the provider-neutral Phase 10 domain and PostgreSQL persistence foundation for `AiConversation`, `AiMessage`, `AiAnalysisRequest`, immutable `AiResultSnapshot`, lifecycle-controlled `AiExecution`, metadata-only `AiUploadedDocument`, and `AiSafetyValidation`. The new `ai` schema contains exactly `ai_conversations`, `ai_messages`, `ai_analysis_requests`, `ai_result_snapshots`, `ai_executions`, `ai_uploaded_documents`, and `ai_safety_validations`, with UUID ownership/reference keys, optional patient associations, restrictive foreign keys, lifecycle/check and uniqueness constraints, operational indexes, append-only history guards, logical conversation deletion, immutable original input/result artifacts, document expiry/deletion state, and privacy-conscious execution metadata that excludes prompts and raw provider payloads. Migration `20260901223517_Phase101AiPlatformPersistenceFoundation` is additive. No public endpoint, provider call, document binary storage, Clinical History/FHIR projection, Second Opinion execution, regeneration, or Phase 10.2+ behavior was introduced; Phase 4 abstractions required no change.
**Phase 10.1 verification:** Locked dependency restore succeeded; Debug build completed with 0 warnings and 0 errors; 20 focused domain tests and 8 focused PostgreSQL persistence/migration tests passed. The complete backend suite passed 1,493 tests (880 unit and 613 PostgreSQL integration, 0 failed/skipped). The complete migration chain applied to fresh PostgreSQL 16; Phase 10.1 rolled back to the prior Phase 8 migration and reapplied while preserving prior data; EF reported no pending model changes. Whole-solution formatting verification and `git diff --check` passed, and 26 explicit application-startup/health tests passed.
**Phase 10.2 status:** COMPLETE (2026-09-01)
**Phase 10.2 implementation:** Added the internal `ExecuteAiAnalysis` orchestration pipeline, provider-neutral `IAiProvider` request/response and sanitized failure contracts, stable workload/prompt identities, an exact-version prompt-contract registry, a schema/version-aware structured-result registry, internal non-displayable outcomes, privacy-safe execution telemetry, and EF-backed lifecycle persistence through the existing Phase 10.1 `AiExecution`. Each started execution performs exactly one configured provider call with no retry, fan-out, or fallback; persists provider/model/combined prompt-contract version, timestamps, latency, status, and sanitized failure category; returns structurally valid content only to the future internal safety stage; and maps malformed structure to the existing technical `Rejected` state without performing Phase 10.3 medical-safety validation. The existing `NvidiaClinicalAiProvider` now also implements `IAiProvider` through its same configured `HttpClient`, options, timeout, transport envelope, and credential-free unavailable fallback while preserving the specialized Phase 4 `IClinicalAiProvider` contract and deterministic Pre-Triage behavior. No database model change or migration, raw prompt/provider-payload persistence or logging, public endpoint, Clinical History/FHIR mutation, concrete-provider dependency outside Infrastructure, or Phase 10.3+ behavior was added.
**Phase 10.2 verification:** Locked restore and whole-solution formatting succeeded; Debug build completed with 0 warnings and 0 errors. Focused coverage passed 24 pipeline/prompt/schema unit cases, 11 NVIDIA adapter cases, and 7 PostgreSQL lifecycle cases; a separate 80-case Phase 10.2/Phase 4 unit matrix, all 20 Phase 10.1 domain regressions, and a 30-case Phase 10.1/10.2/Phase 4 PostgreSQL matrix passed. The complete backend suite passed 1,535 tests (915 unit and 620 PostgreSQL integration, 0 failed/skipped). EF reported no pending model changes, so no Phase 10.2 migration was created; the existing full migration-chain tests remained green. Thirty-two explicit startup/health/OpenAPI tests passed with the API surface unchanged at 43 paths and no `/api/v1/ai/*` route. `git diff --check` passed, and provider-leakage scans found no NVIDIA/OpenAI/Anthropic/configuration types in Domain or the Phase 10 Application contracts.
**Phase 10.3 status:** COMPLETE (2026-09-01)
**Phase 10.3 implementation:** Added the provider-neutral `IAiSafetyValidator` boundary, deterministic Beeexy validator, typed safety decisions/reason codes, versioned Spanish disclaimer and fixed generic/critical Beeexy fallbacks, `ExecuteSafeAiAnalysis` composition around the single-provider Phase 10.2 pipeline, write-only safety persistence, and privacy-safe decision telemetry. Structurally valid provider output now remains non-displayable until Beeexy safety approval; approved output atomically creates the first immutable `AiResultSnapshot` and an approved `AiSafetyValidation`, while rejected diagnosis, prescription/medication/dosage advice, unsafe or authoritative urgency/emergency instructions, disease probabilities, and unsupported content create no snapshot, retain raw output only in `RestrictedAuditOutput`, and return only Beeexy-controlled copy. Provider execution, structural validation, safety category, and display eligibility remain distinct decisions. Malformed technical output continues to stop at the Phase 10.2 schema boundary without duplicating raw output into a safety record. No provider call occurs in safety validation, no public endpoint or Clinical History/FHIR/Pre-Triage mutation was added, and no Phase 10.4+ behavior was introduced.
**Phase 10.3 verification:** Locked restore and whole-solution formatting succeeded; Debug build completed with 0 warnings and 0 errors. Focused coverage passed 49 safety/orchestration/privacy unit cases and 6 PostgreSQL safety-pipeline cases; adjacent regressions passed 163 Phase 10.1/10.2, Phase 4 AI, deterministic Pre-Triage, and Clinical History unit cases plus 44 PostgreSQL AI/Pre-Triage/Clinical History cases. The complete backend suite passed 1,590 tests (964 unit and 626 PostgreSQL integration, 0 failed/skipped), including the complete migration chain. EF reported no pending model changes, so no Phase 10.3 migration was created. Thirty-two explicit startup/health/OpenAPI/CORS tests passed with the API surface unchanged at 43 paths and no `/api/v1/ai/*` route. `git diff --check` passed, and static scans found no concrete-provider/configuration leakage in Domain or Phase 10 Application contracts and no raw rejected output, prompts, provider payloads, or secrets in technical execution metadata or safety telemetry.
**Phase 10.4 status:** COMPLETE (2026-09-01)
**Phase 10.4 implementation:** Added the five authenticated AI Conversation endpoints: create (`201`), owner-only cursor list (`200`), concealed owner detail (`200`/`404`), message execution (`202` with `422` policy/limit and `409` concurrent-execution behavior), and idempotent logical deletion (`204`). Conversations are current-account owned, may be immutably associated at creation with a primary or actively managed patient, and revalidate current patient authority before assembling context for each associated message. The dedicated provider-neutral context assembler reuses existing patient-profile and Clinical History read boundaries, supplies minimized age/sex plus at most three recent Clinical History/Pre-Triage source summaries, persists only source references in the immutable analysis-input provenance, never logs context, and never mutates Pre-Triage, Clinical History, or FHIR. A deterministic request policy rejects off-topic, jailbreak/prompt-override, serious-harm, and illicit-substance-manufacturing requests before persistence/provider execution while allowing medical terminology, non-diagnostic symptom discussion, general health education, and clinician-question preparation. The versioned `ai-conversation@v1` prompt and `ai-conversation-result@v1` schema run through the existing single-call Phase 10.2 pipeline and Phase 10.3 safety boundary; only approved answer text or a Beeexy-controlled fallback becomes an assistant message, while rejected raw output remains restricted audit-only and malformed/provider-failed output creates no assistant message. Provider context is deterministically recent-first bounded by a configurable 8,000–64,000 character budget (16,000 default) without summarization calls; the exact maximum is 50 total user/assistant messages, with submission rejected before execution unless capacity remains for the user/assistant exchange. Cross-process same-conversation execution exclusion uses a PostgreSQL advisory lease, not process-local state. Message ordering uses the Phase 10.1 sequence constraint, soft deletion hides normal AI History while retaining messages/execution/safety audit artifacts, and the shared versioned Phase 10.3 disclaimer is returned without contradictory copies. No Temporary Documents, Second Opinion, regeneration, automatic Clinical History/FHIR promotion, or Phase 10.5–10.8 behavior was introduced.
**Phase 10.4 verification:** Locked restore and whole-solution formatting succeeded; the final Debug build completed with 0 warnings and 0 errors. Focused coverage passed 28 Phase 10.4 application/policy/prompt/context unit cases and 11 PostgreSQL API/privacy/concurrency cases; cross-phase regressions passed 409 unit cases and 108 PostgreSQL cases spanning Phase 10.1–10.4, authentication/patient authority, Phase 4 deterministic/AI behavior, Clinical History, FHIR, Phase 7, and Phase 8. The complete backend suite passed 1,629 tests (992 unit and 637 PostgreSQL integration, 0 failed/skipped), including the complete migration chain. EF reported no pending model changes, so no Phase 10.4 migration was created. Thirty-two explicit startup/health/OpenAPI/CORS tests passed; the API surface is 46 paths with exactly the five planned Phase 10.4 operations, bearer security, documented `201`/`200`/`202`/`204` and safe error contracts, and public DTOs free of provider/prompt/safety-audit fields. Tests prove one provider call for accepted execution, zero calls for creation/input rejection/limit/conflict, database-backed conflict across two application instances, current patient-authority revocation, bounded/minimized patient context, sequence ordering, cross-account concealment, raw-rejection isolation, privacy-safe logging, logical-deletion audit retention, and no Pre-Triage/Clinical History/FHIR mutation. `git diff --check` and provider/privacy/deferred-scope static scans passed.

**Phase 10.5 status:** COMPLETE (2026-09-01)
**Phase 10.5 implementation:** Added authenticated `POST /api/v1/ai/documents` (`201`) and owner-only `DELETE /api/v1/ai/documents/{id}` (`204`) with concealed foreign/missing `404` and idempotent repeated owner deletion while lifecycle metadata remains. Upload accepts exactly one text-native PDF or strict UTF-8 TXT, enforces the configurable exact ceiling of 26,214,400 bytes with bounded reads, validates MIME/extension/signature agreement, rejects unsupported/spoofed types with `415`, and maps malformed, scanned/image-only, empty/meaningless, binary, suspicious/malicious, or indeterminately scanned content to safe `422` responses. Provider-neutral `IAiDocumentSafetyScanner`, `IAiDocumentTextExtractor`, and `IAiDocumentBlobStore` boundaries isolate the deterministic EICAR/active-PDF file-safety baseline, PdfPig embedded-text-only extraction, and configurable private filesystem adapter. Opaque 256-bit server-generated blob keys are never public or authorization-bearing; filenames and extracted text are not retained; only the Phase 10.1 metadata record is persisted. Accepted artifacts expire exactly at upload plus 24 hours. Manual deletion and `ExpireAiDocuments` physically delete first and then retain deleted/expired lifecycle metadata, tolerate already-missing blobs, remain retry-safe after persistence failure, and never make a deleted artifact usable. The deadline-aware hosted worker runs immediately, wakes at the earliest durable expiry rather than merely on a post-deadline periodic tick, falls back safely on scheduling failure, and performs an age-based private-store sweep so compensated-write failures/orphans cannot persist beyond the same retention window. Upload, deletion, and expiry make zero AI calls and create no Pre-Triage, Clinical History, FHIR, analysis, execution, result, or safety artifact. The repository has no configured production antivirus vendor; the replaceable scanner boundary and deterministic local safety baseline are complete for this subphase, while deployment may supply a stronger scanner adapter without changing Application or Domain. No raw-download endpoint, OCR, Second Opinion, regeneration, automatic clinical promotion, or Phase 10.6–10.8 behavior was added.
**Phase 10.5 verification:** Dependency restore and whole-solution formatting/verification succeeded; the final Debug build completed with 0 warnings and 0 errors. Focused coverage passed 39 application/storage/extraction/file-safety/retention unit cases and 10 authenticated API/PostgreSQL/privacy/expiry cases. The complete backend suite passed 1,682 tests (1,031 unit and 651 PostgreSQL integration, 0 failed/skipped), including authentication, patient authority, Phase 4 deterministic Pre-Triage, Clinical History, FHIR, Phase 7, Phase 8, Phase 10.1–10.4, and the complete migration chain. OpenAPI verification passed with 48 paths and exactly the two planned Phase 10.5 operations, bearer security, multipart upload, and documented `201`/`204`/`401`/`404`/`413`/`415`/`422` safe contracts. Tests cover valid PDF/TXT, exact/over-limit sizes, spoofing, fake/malformed/scanned/no-text documents, strict TXT decoding, scanner unsafe/failure behavior, private opaque storage, persistence compensation, owner concealment, manual/repeated deletion, before/at/after-expiry behavior, missing blobs, orphan sweep, metadata retention, and absence of AI/Clinical History/FHIR mutation. EF reported no pending model changes, so no Phase 10.5 migration was created. `git diff --check`, privacy/provider/deferred-scope scans, startup/configuration validation, and the existing health/CORS regressions passed; Phase 10.6–10.8 remain unimplemented.

**Phase 10.6 status:** COMPLETE (2026-09-02)
**Phase 10.6 implementation:** Added authenticated `POST /api/v1/ai/second-opinions` (`202`) and owner/current-patient-authorized `GET /api/v1/ai/second-opinions/{id}` (`200`) with concealed patient, source, owner, and foreign-result `404` behavior and semantic input `422`. The explicit request selects a required patient, optional meaningful typed text, zero-or-one current-account Temporary Document, optional authorized completed Pre-Triage session, and up to three authorized Clinical History events; at least one meaningful non-demographic source is required. The input assembler reuses existing patient authority, profile, Pre-Triage result, Clinical History read, Temporary Document ownership/blob/extraction, and 24-hour lifecycle boundaries. It freezes minimized demographics, normalized selected content, and exact source UUID provenance in `AiAnalysisRequest.OriginalInputSnapshotJson` before execution, never stores raw document binary, never extends expiry, and never rereads a source document during result retrieval. The dedicated `ai-second-opinion@v1` provider-neutral prompt and exact `ai-second-opinion-result@v1` schema require Summary, Important Points, Possible Questions for the Doctor, Missing Information, and the centralized `ai-second-opinion-disclaimer-v1` disclaimer. Execution reuses the Phase 10.2 exactly-one-provider pipeline and Phase 10.3 structural/safety boundary. Approved output creates the existing immutable append-only result snapshot and exposes AI-generated, generation-time, result, provider/model, prompt, and disclaimer version metadata; failed/malformed/unsafe output creates no display snapshot, and GET returns only a fixed Beeexy generic/critical fallback where applicable, never restricted raw output. The safety validator now rejects new test/exam/study recommendations only for the Second Opinion workload while retaining qualified possible-cause, specialty-discussion, supplied-study explanation, physician-opinion discussion, and insufficient-information language. The result remains available after its document is deleted or expires. No Clinical History event/amendment, FHIR resource, directory/matching action, appointment/scheduling record, OCR, regeneration endpoint, hidden retry, second provider call, or Phase 10.7/10.8 behavior was added.
**Phase 10.6 verification:** Locked dependencies were already current; whole-solution formatting verification and `git diff --check` passed; the final Debug solution build completed with 0 warnings and 0 errors. The implementation adds 48 unit cases and 7 authenticated API/real-PostgreSQL cases. Coverage includes text-only, document-only, combined completed Pre-Triage and Clinical History, immutable source/provenance snapshots, one-document and three-history limits, empty/unsupported/foreign/deleted/expired/unavailable input rejection before provider execution, exactly one provider call, prompt/schema/disclaimer/version metadata, possible causes versus diagnosis/probability, qualified specialty language, existing physician opinion/study discussion, insufficiency, prescription/medication/test/exam/urgency rejection, critical fallback, restricted-output isolation, provider/malformed failures, document expiry preservation and result survival after deletion, current owner/patient authority, and zero Clinical History/FHIR/directory/scheduling side effects. The complete backend suite passed 1,737 tests (1,079 unit and 658 PostgreSQL integration, 0 failed/skipped), including the complete migration chain. OpenAPI contains exactly 50 paths, adding only the two planned bearer-secured Phase 10.6 operations with documented `202`/`200`/`400`/`401`/concealed `404`/`422`/`500` contracts and no regeneration route. EF reported no pending model changes, so no Phase 10.6 migration was created. Static deferred-scope scans found no regeneration, Clinical History/FHIR/directory/scheduling write path, and Phase 10.7–10.8 remain unimplemented.

**Phase 10.7 status:** COMPLETE (2026-09-02)
**Phase 10.7 implementation:** Added bearer-secured `POST /api/v1/ai/second-opinions/{id}/regenerate` with owner/current-patient authority, concealed `404`, bodyless original-input-only semantics, invalid immutable-state `422`, same-analysis active-execution `409`, and `202` receipts pointing to the existing status URL. Regeneration parses and validates only the immutable `ai-second-opinion-input@v1` artifact already stored on the original `AiAnalysisRequest`; it never queries current demographics, Pre-Triage, Clinical History, conversation context, or Temporary Document storage, and therefore continues after the source blob is expired or physically deleted without reading, restoring, copying, or extending it. Every accepted attempt creates a distinct `AiExecution`, makes exactly one call through the current provider-neutral Second Opinion prompt/model configuration, persists the exact provider/model/prompt provenance used, and reuses the existing structural and medical-safety boundaries. Safety-approved attempts append a new `AiResultSnapshot` with a database-protected monotonic sequence; prior snapshots and their execution provenance remain immutable. Deterministic GET retrieval selects the highest-sequence approved snapshot and its own generation/provider/model/prompt metadata, so timeout, transient/permanent failure, caller cancellation, malformed output, and safety rejection retain their independent trace while never replacing or exposing an earlier success. Cross-process exclusion uses the existing PostgreSQL advisory-lock pattern scoped by analysis plus a durable `Pending`/`Running` row check; competing attempts make zero provider calls, later deliberate attempts are accepted after completion, and unrelated analyses are not serialized. No Clinical History/FHIR/directory/scheduling or document-lifecycle mutation was introduced, no public snapshot-history endpoint was added, and no Phase 10.8 behavior was implemented.
**Phase 10.7 verification:** Locked restore succeeded; the final Debug solution build completed with 0 warnings and 0 errors; whole-solution formatting verification and `git diff --check` passed. Focused coverage passed 24 unit/application/ProblemDetails cases and 13 authenticated API/real-PostgreSQL cases (37 total, 0 failed/skipped), including exact original text/document/Pre-Triage/Clinical History replay, exclusion of later demographics and clinical state, post-deletion document independence, body rejection, concealed authorization, invalid immutable state before execution, exactly-one/zero-call invariants, S1→S2→S3 immutability and linkage, current prompt/model/safety provenance, deterministic latest approved retrieval, complete timeout/transient/permanent/malformed/unsafe/cancellation preservation, raw-output isolation, persisted-active conflict, near-simultaneous cross-instance conflict, later retry acceptance, unrelated-analysis concurrency, and zero clinical/FHIR/scheduling/document side effects. The complete backend suite passed 1,774 tests (1,103 unit and 671 PostgreSQL integration, 0 failed/skipped), including Phase 7/8, Phase 10.1–10.6, authorization, Pre-Triage, Clinical History, FHIR, Temporary Document retention, safety, startup/OpenAPI, and the complete migration chain. OpenAPI contains exactly 51 paths and adds only the planned bodyless regeneration route with bearer security and documented `202`/`401`/concealed `404`/`409`/`422`/`500` responses. EF reported no pending model changes, so no Phase 10.7 migration was created. Phase 10.8 remains unimplemented pending explicit approval.

**Phase 10.8 status:** COMPLETE (2026-09-02)
**Phase 10.8 implementation:** Closed the complete Phase 10 security and retention boundary without adding a product capability. The ten approved bearer-secured operations are covered as one exact OpenAPI/authentication matrix, including the additive logical Conversation deletion route; anonymous requests are rejected before lookup, validation, persistence, or provider execution. Existing ownership, managed-patient authority, concealed `404`, single-provider-call, structural/safety approval, restricted rejected-output, immutable snapshot/regeneration, concurrency, AI History separation, deterministic Pre-Triage independence, and zero Clinical History/FHIR promotion behavior were audited and retained. Temporary Document expiry now keyset-pages through every row due at one frozen cutoff, so repeated deletion failure for an old full batch cannot starve later expired artifacts; successful deletions persist while failures remain durable and retryable. After a partial failure, scheduling excludes already-overdue retry rows when calculating the next future deadline, uses the original run cutoff to avoid a deadline race, and falls back to the bounded cadence without a busy loop. Cleanup telemetry records only removed counts or sanitized exception categories. The private filesystem adapter creates a user-only directory and `0600` artifacts on Unix, and the production image now pre-creates `/app/private-ai-documents` for its unprivileged runtime identity. Deployment documentation defines provider/secret handling, the exact 25 MiB/no-OCR contract, fixed upload-plus-24-hour retention, private mounts, safe failure/logging, and smoke checks. No schema change, new endpoint, download route, OCR, additional provider behavior, consensus, Clinical History promotion, FHIR mapping, or Phase 11+ behavior was introduced.
**Phase 10.8 verification:** Locked restore, the final Debug solution build, whole-solution formatting verification, `git diff --check`, and the production Docker image build succeeded; build output was 0 warnings and 0 errors. The focused Phase 10.8 suite passed 315 tests: 246 unit and 69 real-PostgreSQL integration/security/API/migration cases, 0 failed/skipped. It composes rather than replaces Phase 10.1–10.7 coverage and proves the complete ten-operation anonymous/bearer and OpenAPI matrix, owner/patient IDOR concealment, exactly-one/zero provider calls, success and sanitized provider failure modes, malformed/unsafe non-displayability, prompt/rejected-output/log isolation, immutable regeneration and prior-result preservation, cross-process conflicts, AI/Clinical History/FHIR/Pre-Triage separation, upload/type/signature/size/file-safety behavior, manual/idempotent deletion, exact 24-hour expiry, missing-blob tolerance, multi-page failure progress, retry recovery, cancellation, no-busy-loop deadline scheduling, private opaque storage, configuration validation, clean migration chain, and Phase 10.1 rollback/reapply. The complete backend suite passed 1,781 tests (1,107 unit and 674 PostgreSQL integration, 0 failed/skipped). OpenAPI remains exactly 51 paths with exactly the ten approved Phase 10 operations and no private storage, restricted-audit, prompt, or provider-payload schema. EF reported no pending model changes; no Phase 10.8 migration was created.
**Phase 10 overall status:** COMPLETE (2026-09-02). Phase 10 is closed; this does not close the complete Beeexy implementation plan.

## Phase-wide objective

Add one replaceable AI provider with Beeexy safety validation, immutable result snapshots, temporary documents, full execution traceability, and history separate from Clinical History.

The complete phase covers authenticated free AI conversations, Second Opinion from supported non-OCR inputs, temporary uploads with a maximum 24-hour retention period, provider/prompt/model/timing/status/failure/safety metadata, safe failure, and immutable regeneration. AI is supplemental and informational. Wherever Beeexy's deterministic clinical rules apply, the deterministic clinical assessment remains authoritative.

## Phase-wide boundaries

The following are out of scope across Phase 10: three-model execution; multi-provider orchestration as a product feature; AI authority over deterministic clinical rules; AI-generated authoritative urgency, care instructions, questionnaire definitions, or deterministic Pre-Triage decisions; OCR; unsupported multimodal extraction; automatic AI-to-Clinical-History promotion; automatic FHIR generation from AI conversations or results; and editing an existing AI result snapshot in place. AI must never diagnose definitively, prescribe, or become a clinical authority.

All Phase 10 capabilities require bearer authentication. Account ownership governs conversations and uploads; Phase 3's shared patient-authority decision governs patient-scoped context and analysis. A UUID, Beeexy ID, conversation ID, document ID, analysis ID, or result ID alone grants no authority. Missing and unauthorized patient-scoped resources use concealed `404` behavior where specified.

AI conversations and results remain non-clinical AI History. They do not automatically create Clinical History records or FHIR resources. Execution provenance may be retained for a future mapping only if Andrea later defines and approves it; that mapping is deferred and does not block Phase 10.

## Implementation sequence and dependency graph

Baseline dependency chain: `10.1 -> 10.2 -> 10.3`.

After 10.3, Phase 10.4 (conversations) and Phase 10.5 (temporary documents) are architecturally parallelizable. Phase 10.6 depends on 10.1–10.3 and additionally uses 10.5 when a document is supplied. Phase 10.7 follows 10.6, and Phase 10.8 closes the complete phase. The practical sequential implementation order is `10.1 -> 10.2 -> 10.3 -> 10.4 -> 10.5 -> 10.6 -> 10.7 -> 10.8`.

Phase 10 does not depend on Phase 9. It reuses Phase 2 authentication/account ownership, Phase 3 patient authority, Phase 4's provider-neutral AI and application-owned deterministic safety boundaries, and Phase 5's Clinical History separation and immutable-source concepts.

## Phase 10.1 — AI Platform Domain + Persistence Foundation

### Objective

Establish the provider-neutral Phase 10 domain and persistence foundation required by all later Phase 10 capabilities, without implementing user-facing AI behavior.

### Scope

- Define `AiConversation`, `AiMessage`, `AiAnalysisRequest`, `AiResultSnapshot`, `AiExecution`, `AiUploadedDocument`, and `AiSafetyValidation`, plus the minimum value objects and reference types needed to preserve their invariants.
- Preserve execution statuses `Pending`, `Running`, `Succeeded`, `Failed`, and `Rejected`, with valid, explicit state transitions.
- Represent account ownership; optional patient association where applicable; conversation/message relationships; analysis requests; immutable original analysis-input provenance and result snapshots; executions and immutable regenerated snapshots; provider/model/prompt-version metadata; timestamps and latency; sanitized failure categories; safety category/result and user-display eligibility; restricted-audit retention of rejected output; document metadata and expiry/physical-deletion state; provenance/reference relationships; and logical deletion needed by Phase 10.4.
- Use UUID primary and foreign keys and existing Beeexy creation/update/concurrency conventions. Technical execution rows must reference content-bearing records and must not unnecessarily duplicate raw health content.

### Out of Scope

Actual provider calls, final prompt content, user-facing conversation execution, document binary storage, Second Opinion execution, regeneration, final safety rules, and public Phase 10 endpoints.

### Domain / Persistence / API Impact

- Map `ai.ai_conversations`, `ai.ai_messages`, `ai.ai_analysis_requests`, `ai.ai_result_snapshots`, `ai.ai_executions`, `ai.ai_uploaded_documents`, and `ai.ai_safety_validations` in the `ai` persistence boundary.
- Add required ownership, relationship, status/time, patient/time, execution/status, provenance, and document-expiry indexes; use restrictive foreign-key behavior where deletion could destroy audit or result history.
- Model `AiResultSnapshot` as append-only/immutable. Model logical conversation deletion with a deletion timestamp/state while retaining internal audit data.
- Preserve enough analysis-input provenance for later immutable regeneration while separating content records from technical execution metadata and minimizing PHI duplication.
- No public API surface is added in 10.1.

### Dependencies

Phase 2 authentication/account foundation; Phase 3 patient-authority concepts where reused; Phase 4 AI architecture and safety boundaries where reusable; Phase 5 Clinical History separation; and existing Beeexy persistence conventions. Phase 10.1 does not depend on Phase 9.

### Implementation Deliverables

Phase 10 domain entities, value objects and statuses; persistence mappings; `ai` schema/table mappings; indexes and constraints; repository/persistence boundaries where needed; migration(s); and focused domain/persistence tests.

### Tests

- Entity invariants, relationship integrity, UUID ownership/reference integrity, execution-state validity, and optional patient association.
- Immutable snapshot constraints and logical-deletion representation.
- Persistence round trips, indexes/constraints where testable, restrictive deletion behavior, and migration rollback/reapply/pending-model checks.
- AI History/Clinical History separation and absence of inappropriate raw PHI duplication in technical execution records.

### Definition of Done

10.1 is complete when the Phase 10 domain can persist all later AI workflows without provider-specific concepts or user-facing AI execution.

## Phase 10.2 — Provider-Neutral AI Execution Pipeline

### Objective

Define the reusable provider-neutral execution pipeline through which Phase 10 AI workloads run.

### Scope

- Inspect Phase 4's `IClinicalAiProvider` and related boundaries first; reuse or adapt them where the contract is genuinely shared instead of creating parallel provider abstractions. Phase 4's deterministic questionnaire and safety authority must remain unchanged.
- Define `IAiProvider` (or an appropriately generalized reused Phase 4 boundary), provider-neutral execution request/response contracts, a provider-neutral prompt-building boundary, and structured result-schema validation.
- Reuse or adapt the existing configured `NvidiaClinicalAiProvider` and `ClinicalAiProviderOptions` as the concrete Phase 10 integration behind the provider-neutral boundary. Preserve the credential-free unavailable fallback and do not expose NVIDIA concepts in the Domain or public contracts.
- Define distinct versioned provider-neutral prompt/safety contracts for free AI conversation, Second Opinion, and applicable safety/fallback behavior. Do not embed full production prompts in this plan or technical logs.
- Record provider/model identification, the exact applicable prompt-contract version, execution lifecycle, timestamps, latency, timeout, cancellation, and normalized transient/permanent failure categories.
- Make exactly one configured provider call per execution. The Phase 10 Domain must not depend on NVIDIA, OpenAI, Anthropic, or any other concrete provider.

### Out of Scope

Final free-chat behavior, the final Second Opinion prompt, document extraction, OCR, final safety-policy implementation beyond its pipeline hook, multi-provider orchestration, and three-model execution.

### Domain / Persistence / API Impact

- Add application/infrastructure execution contracts and an orchestrator that writes complete neutral metadata, including the workload/prompt-contract version, to `AiExecution` without logging prompts or provider payloads.
- Validate the provider response against the workload's structured schema before it can enter the safety/display pipeline. A technically successful call can still become `Rejected` due to malformed schema or later safety validation.
- Concrete adapters remain Infrastructure/configuration concerns; no provider-specific field enters public or Domain contracts.
- No public API is added in 10.2.

### Dependencies

Phase 10.1; the existing Phase 4 provider-neutral abstractions; and the repository's configured NVIDIA adapter. Deployment credentials/configuration are required only for live integration acceptance; missing credentials must select the safe unavailable fallback.

### Implementation Deliverables

Provider-neutral contracts; execution orchestrator; separately versioned free-conversation, Second Opinion, and safety/fallback contracts; prompt-version metadata handling; structured result-schema validator boundary; timeout, cancellation, and failure normalization; reuse/adaptation of the existing NVIDIA adapter; and execution metadata persistence.

### Tests

- Exactly one provider call for success, timeout, cancellation, malformed structured output, and transient/permanent failure paths.
- Execution metadata completeness and valid lifecycle transitions.
- The exact conversation or Second Opinion prompt-contract version and applicable safety-policy version are traceable for each execution without logging prompt bodies.
- No provider-specific leakage into Domain/public contracts and no prompt/provider payload logging.
- Deterministic clinical assessment and Phase 4's explicit structured path remain independent of provider availability or output.

### Definition of Done

10.2 is complete when a Phase 10 workload can execute through one replaceable provider and produce validated provider-neutral execution metadata without exposing unsafe output.

## Phase 10.3 — AI Safety Validation + Execution Traceability

### Objective

Introduce Beeexy's application-level AI safety boundary and make every AI execution auditable before output becomes display-eligible.

### Scope

- Implement the pipeline around `IAiSafetyValidator` and `AiSafetyValidation` with baseline categories `Approved`, `UnsafeMedicalAdvice`, `Diagnosis`, `Prescription`, `Unsupported`, and `Malformed`. Later categories may be additive but must not change these baseline meanings; `Malformed` is the only spelling used.
- Reject definitive diagnoses; prescriptions; instructions to start or stop medication; instructions to change medication or dosage; AI-authoritative urgency classifications; unrestricted AI-authored emergency instructions; and numerical/percentage disease probabilities.
- Keep four distinct recorded decisions: (1) provider execution success/failure; (2) output-schema validation; (3) Beeexy safety validation; and (4) user-display eligibility. A technically successful provider call does not imply schema validity, safety approval, or display eligibility.
- Never return rejected output through normal user APIs. Return a generic safe fallback, keep the rejection internally traceable, and retain rejected raw output only behind restricted audit controls under the approved retention/access policy.
- In the Second Opinion contract, possible causes or considerations are allowed only in neutral, non-diagnostic language such as “Possible considerations include...” or “One possibility that could be discussed with a physician is...”. Assertions equivalent to “You have X”, “Your diagnosis is X”, or a numerical disease probability are rejected.
- Treat the general disclaimer as versioned/configurable product content, referenced rather than scattered through business logic, with this required semantic content: “Esta respuesta ha sido generada por inteligencia artificial y no sustituye una evaluación médica. Consulta siempre con un profesional de salud certificado.”
- Route potentially critical content to a fixed Beeexy-controlled fallback with this baseline meaning: “La información proporcionada podría requerir atención médica. Si crees que puedes estar ante una emergencia o tus síntomas son graves, busca atención médica de inmediato.” This is Beeexy safety copy, not model output or a deterministic AI urgency classification.

### Out of Scope

Replacing deterministic Pre-Triage rules, allowing AI to determine Beeexy urgency, full conversation UX, Second Opinion, and document lifecycle.

### Domain / Persistence / API Impact

- Persist the safety category, validator/policy version, applicable disclaimer/fallback content version, timestamps, display-eligibility decision, and linkage to the execution/result candidate.
- Separate restricted rejected-output audit material from normal result retrieval and technical logs; keep ordinary execution metadata non-PHI-heavy.
- Define generic provider/schema/safety failure responses without leaking raw output, prompt content, provider internals, or secrets.
- No public product endpoint is introduced in 10.3; later endpoints must consume this boundary.

### Dependencies

Phases 10.1 and 10.2; Phase 4 clinical-AI boundaries; and approved safety/disclaimer decisions.

### Implementation Deliverables

Safety validator and categories; display-eligibility decision; generic fallback behavior; restricted rejected-output audit handling; execution/safety traceability; disclaimer version/reference handling; and the critical-safety fallback boundary.

### Tests

- Approved output and rejection of definitive diagnosis, prescription, medication advice, disease probability, AI-authoritative urgency, malformed output, and unsupported output.
- Rejected output is never displayed; generic fallback is returned; restricted audit retention and access are enforced; safety metadata is persisted.
- Fixed Beeexy safety copy remains distinguishable from model output, and deterministic Pre-Triage remains independent.
- Logs contain neither raw rejected output nor prompts/provider payloads.

### Definition of Done

10.3 is complete when no Phase 10 result can become user-displayable without schema and safety approval, and every execution has sufficient privacy-conscious traceability.

## Phase 10.4 — AI Conversations + Conversation History

### Objective

Deliver authenticated informational AI conversations with persistent AI History that remains separate from Clinical History.

### Scope

- Allow general health questions, explanations of health/medical terminology, non-diagnostic discussion of symptoms, and preparation of questions for a healthcare professional.
- Reject requests to manufacture illicit substances, facilitate serious harm, bypass Beeexy's role/safety constraints through jailbreak or prompt injection, or pursue topics unrelated to health.
- Make each conversation account-owned and optionally patient-associated. When associated, assemble only authorized patient context, which may include Pre-Triage, Clinical History, and demographics, through Phase 3 patient authority.
- Keep four concepts distinct: source references/context assembled for an execution; AI conversation messages; immutable AI result/execution artifacts; and authoritative Clinical History records. Do not copy patient source records into technical execution rows for convenience, and never project messages or results automatically.
- Enforce a maximum of 50 user/assistant messages per conversation and a configurable provider/context token budget; reject further submission gracefully when the configured limit is reached, never send unbounded history to the provider, and never place provider-specific token limits in the Domain.
- Soft-delete conversations: hide them from normal history while retaining them under internal audit controls using Beeexy's deletion timestamp/state convention.
- Treat the conversation-start disclaimer as configurable/versioned product content with this required semantic content: “Toda la información generada es por IA. Recuerda acudir siempre a un profesional médico certificado.”

### Out of Scope

Automatic Clinical History promotion, anonymous chat, diagnostic chat, unlimited provider context, Second Opinion, and document upload.

### Domain / Persistence / API Impact

| Method / route | Authentication | Authorization | Purpose | Success | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/ai/conversations` | Bearer | Current account; patient authority if associated | Create conversation | `201` | Invalid purpose/context `422`; patient concealed `404` |
| `GET /api/v1/ai/conversations` | Bearer | Owner only | List non-deleted AI History | `200` | `401` |
| `GET /api/v1/ai/conversations/{id}` | Bearer | Conversation owner | Get messages/snapshots | `200` | Concealed `404` |
| `POST /api/v1/ai/conversations/{id}/messages` | Bearer | Conversation owner and current patient authority when patient context is requested | Submit message | `202` execution | Configured message/context limit or invalid/unsafe input `422`; concurrent execution `409`; concealed `404` |
| `DELETE /api/v1/ai/conversations/{id}` | Bearer | Conversation owner | Soft-delete conversation | `204` | Concealed `404`; repeated owner deletion is idempotent `204` |

The original Phase 10 API listed no operation capable of satisfying its soft-deletion requirement. The additive `DELETE /api/v1/ai/conversations/{id}` contract above is the smallest API-contract addition and must be included in 10.8 acceptance; it performs no hard deletion.

### Dependencies

Phases 10.1–10.3; Phase 2 authentication; Phase 3 patient authority; Phase 4 Pre-Triage context when referenced; and Phase 5 Clinical History when referenced.

### Implementation Deliverables

Conversation create/list/get; message submission; account ownership; optional patient association; authorized patient-context assembly; bounded context/history; soft deletion; disclaimer; and execution/safety integration.

### Tests

- Authenticated creation, anonymous rejection, account ownership, optional patient association, patient authority, and authorized patient-context retrieval.
- Off-topic, harmful/illicit, and jailbreak rejection; successful message execution; concurrent execution conflict.
- 50-message limit, graceful limit rejection, and configurable context-budget behavior without provider-specific Domain limits.
- Soft-deleted conversation hidden from user history but retained for restricted audit; repeated owner deletion; concealed cross-account access.
- AI History remains separate from Clinical History and creates neither Clinical History nor FHIR artifacts.

### Definition of Done

10.4 is complete when an authenticated user can safely create and use a bounded AI conversation, optionally with authorized patient context, while AI History remains a separate non-clinical record.

## Phase 10.5 — Temporary Documents + 24h Retention

### Objective

Deliver private temporary document ingestion for Phase 10 analysis with strict validation and physical deletion no later than 24 hours.

### Scope

- Support text-native PDF and TXT only, with a maximum size of exactly 25 MiB (26,214,400 bytes) per document and at most one document per Second Opinion. The byte limit is configurable and validated at byte level; display units are not the enforcement mechanism.
- Validate declared content type, actual signature/type where applicable, size, malware/security status, ownership, and useful extractable text.
- Extract text from supported files; reject scanned/image-only PDFs and nominally valid files with no useful extractable text. Do not perform OCR, do not invoke the provider with empty/meaningless document content, and return a safe validation response asking for another supported document.
- Store the blob privately with only short-lived authorized access, support early manual deletion, and physically delete it automatically no later than 24 hours after upload. Manual deletion is idempotent for an owner when minimal lifecycle metadata remains.
- Retain only the minimum lifecycle/deletion metadata needed for audit. A result or durable normalized analysis-input snapshot must never extend the temporary blob's retention.
- Manual or automatic source-document deletion never mutates an already-created immutable Second Opinion result snapshot.

### Out of Scope

OCR, scanned-PDF extraction, JPG/PNG, DOCX, permanent medical-document storage, automatic Clinical History ingestion, and more than one document per Second Opinion.

### Domain / Persistence / API Impact

| Method / route | Authentication | Authorization | Purpose | Success | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/ai/documents` | Bearer | Current account/uploader | Upload temporary document | `201` metadata | Too large `413`; unsupported/spoofed media `415`; malware, unusable text, or semantic validation `422` |
| `DELETE /api/v1/ai/documents/{id}` | Bearer | Uploader | Delete blob early | `204` | Absent/foreign concealed `404`; repeated owner deletion `204` when lifecycle metadata identifies the prior deletion |

- Integrate a private `IBlobStore`, PDF/TXT extraction, upload validation, `AiUploadedDocument` lifecycle metadata, and an `ExpireAiDocuments` worker/job/use case.
- Mark deletion/expiry atomically and make cleanup retry-safe. The blob URI/key is private and never acts as authorization.

### Dependencies

Phase 10.1; Phase 10.3 security boundaries where applicable; a private `IBlobStore`; and malware/file-validation infrastructure. Phase 10.5 does not require Phase 10.4 and can proceed in parallel after shared prerequisites.

### Implementation Deliverables

Private blob-store abstraction/integration; upload validation; PDF/TXT text extraction; ownership enforcement; metadata persistence; manual deletion; expiry worker/job/use case; and deletion-state traceability.

### Tests

- Valid TXT and text-native PDF; unsupported type; spoofed extension/content type; scanned/image-only PDF; unusable extracted text with a safe request-for-another-document response; and size above 25 MiB.
- Ownership, concealed not-found, manual deletion, repeated deletion, cleanup retries/races, and automatic physical deletion by 24 hours.
- Blob removed while minimal lifecycle metadata remains, and expired/deleted documents cannot start new analysis.
- Manual or automatic document deletion leaves an already-created result snapshot unchanged.

### Definition of Done

10.5 is complete when supported temporary documents can be safely uploaded, validated, text-extracted, privately stored, manually deleted, and automatically removed within the retention window.

## Phase 10.6 — Second Opinion Pipeline

### Objective

Deliver Beeexy's structured informational Second Opinion workflow from approved user/patient inputs without making AI a diagnostic or deterministic clinical authority.

### Scope

Product-facing intent: “Get a second perspective on your case. An independent AI reviews your information and offers an evidence-based second opinion in minutes to help you make confident decisions.” This presentation does not make the feature a physician, licensed medical opinion, or diagnosis.

Backend/domain definition: Second Opinion is an AI-generated informational analysis of user-provided health information intended to provide an additional perspective, identify relevant considerations, and help the user prepare for discussion with a licensed healthcare professional. It does not provide a medical diagnosis.

- Accept user-written text, one supported temporary document, authorized Pre-Triage information, and authorized Clinical History.
- Return a structured result containing: (1) summary; (2) important points/relevant considerations; (3) possible questions to discuss with a physician; (4) missing or insufficient information; and (5) disclaimer.
- Possible diseases, causes, or clinical considerations may be mentioned only as possibilities, never as definitive diagnoses or numerical disease probabilities. A medical-specialty discussion suggestion is allowed.
- Do not recommend specific medical tests, laboratory studies, imaging, or examinations.
- An existing physician opinion/diagnosis may be explained, other possibilities may be identified, and discussion questions may be suggested; the AI must not claim to replace, overturn, confirm, or definitively refute the physician.
- When information is insufficient, return a valid insufficiency result, identify missing information when safe, invite additional information, and never fabricate facts.
- Potentially concerning content uses Phase 10.3's fixed Beeexy fallback, not model-authored urgency.
- Include this result disclaimer as versioned/configurable product content whose semantic meaning survives localization: **This is not a medical diagnosis.** Beeexy AI offers educational insights based on clinical literature, not a substitute for a licensed physician. Always discuss results with your doctor.
- User-facing metadata includes the AI-generated indication, generation date/time, and applicable AI/result/model version identifier.

### Out of Scope

Definitive diagnosis, medical-test recommendations, AI-authoritative urgency, prescriptions or medication changes, OCR, more than one document, automatic Clinical History promotion, and automatic FHIR generation.

### Domain / Persistence / API Impact

| Method / route | Authentication | Authorization | Purpose | Success | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/ai/second-opinions` | Bearer | Current account and owner/active manager for any patient context | Start analysis | `202` | Unsupported/missing/invalid input `422`; patient/document concealed `404` |
| `GET /api/v1/ai/second-opinions/{id}` | Bearer | Analysis owner and current patient authority where patient-scoped | Retrieve safe status/result | `200` | Concealed `404`; rejected raw output never returned |

- Implement `RequestSecondOpinion`, an authorized input-context assembler, a minimized immutable original analysis-input snapshot/provenance record, a distinct versioned provider-neutral Second Opinion prompt contract/result schema, schema and safety validation, immutable result persistence, and retrieval.
- The document blob remains temporary; the original analysis-input snapshot retains only the normalized input required for result provenance and Phase 10.7 regeneration, with minimized PHI duplication.

### Dependencies

Phases 10.1–10.3; Phase 10.5 when a document is supplied; Phase 4 for Pre-Triage context; Phase 5 for Clinical History context; and Phase 3 patient authority. Phase 10.6 does not depend on Phase 9.

### Implementation Deliverables

`RequestSecondOpinion`; input-context assembler; immutable original analysis-input snapshot/provenance; versioned prompt contract; structured provider-neutral result schema; schema and safety validation; result persistence/retrieval; and disclaimer/provenance metadata.

### Tests

- Text-only and document requests; authorized Pre-Triage and Clinical History context; unauthorized patient/document context; one-document maximum.
- Structured schema containing Summary, Important points/relevant considerations, Possible questions for a physician, Missing/insufficient information, and Disclaimer; non-diagnostic possible causes; rejection of definitive diagnosis, numerical probability, and test/exam recommendations; allowed specialty suggestion.
- Non-authoritative analysis of an existing physician diagnosis; valid insufficient-information result; no fabricated missing facts.
- Safety fallback; rejected raw output never returned; and user-facing provenance containing an `AI-generated` indicator, generation date/time, result/model or applicable AI version, and disclaimer version.

### Definition of Done

10.6 is complete when an authorized user can request and retrieve a structured, safety-approved, immutable informational Second Opinion from supported inputs.

## Phase 10.7 — Regeneration + Immutable Snapshots + Failure Handling

### Objective

Complete the result lifecycle with immutable regeneration, concurrency protection, and safe failure.

### Scope

- Preserve immutable `AiResultSnapshot`; create one execution per regeneration attempt; never modify a previous snapshot; keep complete execution traceability; fail safely; and leave deterministic assessments independent.
- Regenerate from the same immutable original analysis-input snapshot. Do not silently incorporate later Clinical History, Pre-Triage, demographics, uploaded documents, or conversation state.
- Each regeneration creates a new execution and, only after schema/safety approval, a new immutable result snapshot linked to the same original input.
- Preserve normalized original document input needed for regeneration after the temporary blob is physically deleted, without retaining the blob beyond 24 hours and while minimizing duplicated PHI.
- Define idempotency/concurrency behavior so simultaneous regeneration of the same analysis returns a conflict rather than creating untraceable competing results. Automatic retries must remain visible as execution attempts and must not create hidden user-visible snapshots.

### Out of Scope

Editing snapshots in place, automatically using newer patient data, retaining temporary files beyond policy, multi-provider comparison, and hidden automatic retries/results without traceability.

### Domain / Persistence / API Impact

| Method / route | Authentication | Authorization | Purpose | Success | Validation and errors |
|---|---|---|---|---|---|
| `POST /api/v1/ai/second-opinions/{id}/regenerate` | Bearer | Analysis owner and current patient authority where patient-scoped | Create execution/new snapshot from original input | `202` | Already running `409`; concealed `404`; invalid immutable-input state `422` |

- Implement `RegenerateSecondOpinion`, immutable original-input linkage, a new execution per attempt, a new snapshot per approved success, database-backed concurrency protection, and sanitized failure mapping.
- A failed, timed-out, malformed, or unsafe regeneration records its execution outcome but never mutates or replaces a prior successful snapshot.

### Dependencies

Phases 10.1–10.3 and 10.6; Phase 10.5 retention semantics when the original input included a document.

### Implementation Deliverables

Regeneration use case; immutable original-input reference/snapshot; new execution per attempt; new result snapshot per successful approved regeneration; concurrency/idempotency rules; sanitized failure behavior; and complete linkage.

### Tests

- New snapshot per successful regeneration; prior snapshot unchanged; same original input reused.
- Later Clinical History, Pre-Triage, demographics, document state, and conversation changes ignored.
- Expired/deleted original blob does not break regeneration, and the blob is not retained as a workaround.
- Concurrent regeneration `409`; timeout; malformed/unsafe result; transient/permanent provider failure; and traceable retry behavior.
- Failed regeneration does not mutate prior results; deterministic assessment remains unaffected.

### Definition of Done

10.7 is complete when every regeneration is independently traceable and immutable, uses the original input semantics, and cannot corrupt or replace earlier successful results.

## Phase 10.8 — Security, Retention + Phase 10 Acceptance

### Objective

Close Phase 10 by validating authorization, privacy, retention, safety, endpoint behavior, observability, and end-to-end acceptance across the complete AI platform.

### Scope

- Require bearer authentication for every AI capability; prohibit anonymous access; enforce account ownership and patient authority; keep object storage private with short-lived authorized access; conceal resource existence where required; and restrict rejected-output audit access.
- Verify no prompt/provider payload logging, no raw unsafe/rejected output through user APIs, minimal PHI duplication, physical document deletion no later than 24 hours, and correct logical conversation deletion.
- Verify AI History remains distinct from Clinical History and that no automatic FHIR or Clinical History promotion occurs.
- Audit observability so provider/model/prompt version, timing, status, failure category, and safety decisions are traceable without leaking secrets or unnecessary health content.

### Out of Scope

Phase 11+ functionality, OCR, additional-provider product behavior, multi-model consensus, AI-to-Clinical-History promotion, and future Andrea-defined FHIR mapping for AI artifacts.

### Domain / Persistence / API Impact

The final authenticated endpoint matrix is:

1. `POST /api/v1/ai/conversations`
2. `GET /api/v1/ai/conversations`
3. `GET /api/v1/ai/conversations/{id}`
4. `POST /api/v1/ai/conversations/{id}/messages`
5. `DELETE /api/v1/ai/conversations/{id}` (additive soft-delete contract identified in 10.4)
6. `POST /api/v1/ai/documents`
7. `DELETE /api/v1/ai/documents/{id}`
8. `POST /api/v1/ai/second-opinions`
9. `GET /api/v1/ai/second-opinions/{id}`
10. `POST /api/v1/ai/second-opinions/{id}/regenerate`

Preserve the owning-subphase contracts and established semantics: `201` for synchronous resource creation, `202` for asynchronous AI execution, `204` for deletion, concealed `404`, `409` for concurrent execution/regeneration, `413` for size, `415` for unsupported/spoofed media, and `422` for semantic/validation/safety-related invalid input where appropriate. Do not redesign these codes during acceptance.

### Dependencies

Phases 10.1–10.7; existing authentication/authorization; the existing NVIDIA adapter with deployment credentials/configuration for live integration acceptance; private blob storage; and approved Phase 10 product/safety contracts.

### Implementation Deliverables

Final authorization hardening; retention verification; privacy/logging review; complete endpoint acceptance matrix; integration and security tests; final regression suite; and required operational documentation.

### Tests

- Exactly one provider call; success; timeout; malformed output; unsafe rejection; transient/permanent failure; generic safe failure; and deterministic-assessment independence.
- Complete provider-neutral execution metadata without inappropriate PHI duplication; prompt/provider payload absence from logs; restricted rejected-output audit; and no rejected raw output in APIs.
- Immutable regeneration; AI History/Clinical History separation; no automatic FHIR generation or Clinical History promotion.
- Upload ownership, type/signature/size validation, manual/idempotent deletion, automatic deletion by 24 hours, and private blob access.
- Concurrent message and regeneration conflicts; complete bearer/ownership/patient-authority endpoint matrix; concealed not-found behavior; and soft-deleted conversations absent from normal history but retained for audit.
- Migrations, rollback/reapply, pending-model checks, OpenAPI, configuration validation, and the complete backend regression suite.

### Definition of Done

Phase 10 is complete only when only safety-approved output can be displayed; the provider remains replaceable; AI never controls deterministic clinical rules; AI History stays separate from Clinical History; snapshots and regeneration are immutable and traceable; temporary-document retention is enforced; authorization/ownership and every endpoint contract are covered; all required unit/integration/security/acceptance tests pass; and no Phase 10 responsibility remains ambiguously assigned.

## Phase 10 deferred / operational dependencies

- Deferred product capabilities: OCR, scanned-image and unsupported multimodal extraction, additional providers as product behavior, multi-provider/model consensus, automatic Clinical History promotion, and any future AI-to-FHIR mapping.
- Concrete provider selection is resolved by the existing configurable NVIDIA integration (`NvidiaClinicalAiProvider` behind `IClinicalAiProvider`). Phase 10 must reuse/adapt it behind `IAiProvider` or an appropriately generalized shared provider-neutral boundary. Deployment credentials/configuration remain the only provider operational dependency for live integration acceptance, are not Domain dependencies, and do not block 10.1. Secrets must never be written into this plan.
- Concrete versioned prompt text must be approved before the subphase that executes it. The safety and disclaimer semantics above are fixed Phase 10 contracts and are no longer TBD.
- Supported formats and limits are resolved for MVP as text-native PDF/TXT, 25 MiB per document, and one document per Second Opinion; they are no longer TBD.
- Rejected-output handling is resolved: rejected raw output is retained internally only for restricted audit under Beeexy's backend-wide security, privacy, access, and retention controls; it is never user-displayable or available to ordinary application/log access. This is a Phase 10 requirement, not a deferred product decision.

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
- **Phase 7:** product approval of a synthetic/demo directory dataset and deterministic demo matching factors/weights is required; authoritative real directory data, real credentialing, and production matching rules/validation do not block the MVP/demo.
- **Phase 9:** approved follow-up rules, intervals, escalation actions, and Care Guide templates.
- **Phase 10:** COMPLETE. Production NVIDIA credentials and a credentialed deployment smoke check remain operational deployment concerns, not implementation or standard acceptance blockers. Provider selection, versioned prompt content, restricted-audit handling, MVP inputs/limits, safety semantics, disclaimers, private storage, and retention behavior are implemented and covered with credential-free fakes in the repository suite.
- **Phase 11:** share duration defaults and frontend public share URL.
- **Phase 12:** VAPID keys and approved notification copy/rules.
- **Phase 13:** recording consent/attestation text, speech provider, media constraints, and structured-extraction retention decision.
- **Production:** long-term retention/deletion, legal/privacy/compliance controls, deployment, backup, and disaster-recovery requirements.

## Recommended first phase

Implement **Phase 1 — Backend and Database Foundation** first. It establishes the compilation, migration, API, error-handling, security configuration, and test infrastructure required by every subsequent phase without implementing product behavior.
