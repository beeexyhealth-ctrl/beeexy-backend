# Beeexy Phase 3 API — Frontend Integration Guide

## 1. Purpose

This guide is the frontend contract for **Phase 3 — My Circle and Managed Patient Profiles**. It covers only the current Phase 3 API surface: completing primary-patient demographics, creating and listing managed patients, reading and editing authorized patients, viewing relationship history, revoking access, and selecting an active patient in the frontend.

The backend implementation, endpoint DTOs, authorization service, OpenAPI document, and Phase 3.8 acceptance/security tests are the source of truth for this guide. Phase 3 management access is not clinical-record sharing, legal verification, or delegated authentication.

## 2. Authentication Prerequisite

Phase 3 assumes the frontend already has:

- an authenticated Beeexy Account;
- a valid access token;
- working refresh and session handling;
- integration with `GET /api/v1/auth/me`;
- integration with `GET /api/v1/patients/me`.

Every Phase 3 request sends:

```http
Authorization: Bearer <accessToken>
```

See [`docs/frontend-api-integration.md`](frontend-api-integration.md) for email/Google sign-in, token rotation, logout, and the complete Phase 2 session lifecycle. Those details are intentionally not repeated here.

Missing, malformed, incorrectly signed, wrong-issuer, wrong-audience, expired, or non-token credentials return `401`. An otherwise valid token for a disabled Account also returns a generic `401`.

## 3. Phase 3 Frontend Journey

### First registration

```text
Onboarding
→ Login
→ Complete your primary PatientProfile
→ Who are you caring for?
   ├── Just me
   │     → activePatient = primary PatientProfile
   │     → App
   └── Someone else
         → Create managed PatientProfile
         → activePatient = new managed PatientProfile
         → App
```

### Returning session

```text
Session restore
→ load account/profile
→ load accessible patients
→ restore/select activePatient
→ App
```

A practical bootstrap is:

1. Restore/refresh the authenticated session using the Phase 2 flow.
2. Load `/api/v1/auth/me` and `/api/v1/patients/me`.
3. Load `GET /api/v1/patients` for the current choices.
4. Restore the locally selected `profileId` only if it is still present in that response.
5. Otherwise select the primary patient, which is the first accessible-patient entry.

Inside the app, expose a switcher such as `Caring for: Maria Arias ▼`.

`activePatient` is frontend/session state. The current backend has no field or endpoint that persists this selection.

## 4. Approved Patient Demographics

Phase 3 supports exactly these fields:

| Field | Transport value | Current rules |
|---|---|---|
| `firstName` | string | Unicode text; trimmed by the server; nonblank when supplied; maximum 100 characters. |
| `lastName` | string | Unicode text; trimmed by the server; nonblank when supplied; maximum 100 characters. |
| `dateOfBirth` | string | Strict ISO date `YYYY-MM-DD`; no time or timezone; cannot be later than the current UTC date. |
| `sexAssignedAtBirth` | string | Exactly `Male` or `Female`; casing is significant. Surrounding whitespace is trimmed by application validation. |
| `state` | string | One of the 50 two-letter U.S. postal codes. Input is trimmed and uppercased; responses are canonical uppercase. |

Accepted state codes are:

```text
AL AK AZ AR CA CO CT DE FL GA HI ID IL IN IA KS KY LA ME MD MA MI MN
MS MO MT NE NV NH NJ NM NY NC ND OH OK OR PA RI SC SD TN TX UT VT VA
WA WV WI WY
```

For example, `" ca "` is stored and returned as `"CA"`. Full state names, territories, districts, and unknown codes are rejected.

New managed patients require all five demographics. Newly provisioned and historical primary profiles may contain `null` for any of them. PATCH fields are optional because the operation is partial, but a field that is included cannot be `null` or blank; the current API does not clear a demographic back to `null`.

Do not send additional demographics or aliases such as `name`, `gender`, `address`, `timezone`, or `accountId`. Unknown fields are rejected.

## 5. Phase 3 Endpoint Summary

These are the **six** Phase 3 operations:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/patients` | List the primary patient and actively managed patients. |
| `POST` | `/api/v1/care-relationships` | Atomically create a managed patient and its active relationship. |
| `GET` | `/api/v1/care-relationships` | List the current manager's Active and Revoked relationship history. |
| `GET` | `/api/v1/patients/{patientId}` | Read an authorized PatientProfile. |
| `PATCH` | `/api/v1/patients/{patientId}` | Partially update approved patient demographics. |
| `DELETE` | `/api/v1/care-relationships/{id}` | Irreversibly revoke a manager-owned relationship. |

`patientId` and relationship `id` are UUIDs. A Beeexy ID is not accepted as a substitute route identifier and never grants authority.

## 6. Complete the Primary Patient Profile

After first login, call `GET /api/v1/patients/me`. It supplies both the primary `profileId` and the current PatientProfile `profileVersion`:

```json
{
  "profileId": "10000000-0000-0000-0000-000000000001",
  "beeexyId": "BXY-...",
  "firstName": null,
  "lastName": null,
  "dateOfBirth": null,
  "sexAssignedAtBirth": null,
  "state": null,
  "profileVersion": 1,
  "preferences": {
    "timezone": "Etc/UTC"
  },
  "version": 1
}
```

The two versions have different owners and must never be interchanged:

- `profileVersion` is the optimistic-concurrency token for `PatientProfile` demographics. Send its value as `version` to `PATCH /api/v1/patients/{profileId}`.
- `version` in `/patients/me` is the independent Phase 2 `UserPreference`/timezone token. It is used only by `PATCH /api/v1/patients/me`.

To complete all approved primary demographics, use the primary `profileId` in the Phase 3 patient PATCH route:

```http
PATCH /api/v1/patients/10000000-0000-0000-0000-000000000001
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "firstName": "Jesus",
  "lastName": "Arias",
  "dateOfBirth": "1990-04-18",
  "sexAssignedAtBirth": "Male",
  "state": "FL",
  "version": 1
}
```

Success is `200 OK` with the full patient-detail contract and its current version:

```json
{
  "profileId": "10000000-0000-0000-0000-000000000001",
  "beeexyId": "BXY-...",
  "firstName": "Jesus",
  "lastName": "Arias",
  "dateOfBirth": "1990-04-18",
  "sexAssignedAtBirth": "Male",
  "state": "FL",
  "version": 2
}
```

An effective update increments the PatientProfile version once. A stale version returns `409`; refetch before reconciling. Invalid/missing demographics, a missing/non-positive version, an empty demographic patch, or unknown fields return `422`. Malformed JSON returns `400`.

There is no `profileComplete` property. Derive completion as:

```ts
const profileComplete =
  patient.firstName !== null &&
  patient.lastName !== null &&
  patient.dateOfBirth !== null &&
  patient.sexAssignedAtBirth !== null &&
  patient.state !== null;
```

Non-null values returned by the backend already satisfy its normalization and validation rules.

## 7. List Accessible Patients

### `GET /api/v1/patients`

This endpoint answers: **Who can I manage right now?** It should drive the My Circle list, patient switcher, and valid `activePatient` choices.

Success is `200 OK`:

```json
{
  "patients": [
    {
      "profileId": "10000000-0000-0000-0000-000000000001",
      "beeexyId": "BXY-PRIMARY",
      "firstName": "Jesus",
      "lastName": "Arias",
      "accessType": "Primary",
      "relationship": null
    },
    {
      "profileId": "20000000-0000-0000-0000-000000000002",
      "beeexyId": "BXY-MANAGED",
      "firstName": "Maria",
      "lastName": "Arias",
      "accessType": "Managed",
      "relationship": {
        "relationshipId": "30000000-0000-0000-0000-000000000003",
        "type": "Child"
      }
    }
  ]
}
```

Behavior:

- The authenticated Account's actual primary PatientProfile is always first and has `accessType: "Primary"` with `relationship: null`.
- Only patients reached through the current primary profile's Active manager relationships follow it. They have `accessType: "Managed"` and relationship context.
- Revoked and unrelated patients are excluded.
- Managed patients are ordered by relationship `createdAt`, then relationship UUID. Duplicate subject rows are defensively removed.
- There is no pagination in the current Phase 3 contract.
- The response is data-minimized: it includes names but omits date of birth, sex assigned at birth, state, patient version, Account IDs, and persistence metadata.
- `firstName` and `lastName` are nullable because historical profiles can be incomplete.

`Primary` means the PatientProfile owned by the authenticated Account. `Managed` means the current primary patient has an Active relationship to the subject. These are access labels, not separate patient entity types.

Expected statuses are `200`, `401`, and safe `500`.

## 8. Create a Managed Patient

### `POST /api/v1/care-relationships`

Send only relationship intent, technical attestation, and the new patient's approved demographics:

```http
POST /api/v1/care-relationships
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "relationshipType": "Child",
  "attestationVersion": "phase-3.8-approved",
  "attestationAccepted": true,
  "patient": {
    "firstName": "Maria",
    "lastName": "Arias",
    "dateOfBirth": "2012-05-12",
    "sexAssignedAtBirth": "Female",
    "state": "ny"
  }
}
```

The frontend must send a canonical relationship type:

- `Parent`
- `LegalGuardian`
- `Caregiver`
- `Spouse`
- `Child`
- `Sibling`
- `Other`

Friendly labels are a presentation concern; API values should remain exactly these strings.

The backend resolves the manager Account and primary profile from the Bearer token. It atomically creates:

```text
PatientProfile + Beeexy ID + Active CareRelationship
```

It does **not** create an Account, external identity, refresh session, or authentication capability for the managed patient. Do not send manager Account/Profile IDs, a subject ID, a Beeexy ID, or other identity fields.

Success is `201 Created`; the `Location` header points to `/api/v1/patients/{newProfileId}`:

```json
{
  "relationship": {
    "id": "30000000-0000-0000-0000-000000000003",
    "type": "Child",
    "status": "Active",
    "attestationVersion": "phase-3.8-approved",
    "attestedAt": "2026-08-21T12:00:00+00:00"
  },
  "patient": {
    "profileId": "20000000-0000-0000-0000-000000000002",
    "beeexyId": "BXY-...",
    "firstName": "Maria",
    "lastName": "Arias",
    "dateOfBirth": "2012-05-12",
    "sexAssignedAtBirth": "Female",
    "state": "NY",
    "version": 1
  }
}
```

### Attestation UX

- `attestationVersion` is required, trimmed by the server, nonblank, and limited to 64 characters.
- `attestationAccepted` must be explicitly `true`; omission binds as `false` and is rejected.
- `attestedAt` is generated by the server. Never send or calculate the acceptance timestamp.
- The version should identify the exact product-approved text presented to the user.

Technical recording is implemented. Final human-readable product/legal attestation wording is still an external product-content dependency. Do not invent wording or present this mechanism as legal or identity verification.

### Status behavior

| Status | Meaning for this endpoint |
|---|---|
| `201` | Patient and relationship were created atomically. |
| `400` | Malformed JSON or request syntax. |
| `401` | Bearer authentication failed or the Account is inactive. |
| `409` | A protected uniqueness/persistence conflict occurred; no orphan managed profile is retained. |
| `422` | Invalid relationship type/attestation/demographics or unsupported field. |
| `500` | Safe generic unexpected/invariant failure; internal database/exception details are not exposed. |

This endpoint has no idempotency key. Do not use `409` as a general duplicate-click detector, and do not blindly replay an ambiguous successful POST; refetch the two lists first.

## 9. List Care Relationships

### `GET /api/v1/care-relationships`

This endpoint answers: **What management relationships do I have or have I had?** It is scoped to relationships where the authenticated Account's primary PatientProfile is the manager.

Success is `200 OK`:

```json
{
  "relationships": [
    {
      "id": "30000000-0000-0000-0000-000000000003",
      "subject": {
        "profileId": "20000000-0000-0000-0000-000000000002",
        "beeexyId": "BXY-MANAGED",
        "firstName": "Maria",
        "lastName": "Arias"
      },
      "type": "Child",
      "status": "Active",
      "attestationVersion": "phase-3.8-approved",
      "attestedAt": "2026-08-21T12:00:00+00:00",
      "createdAt": "2026-08-21T12:00:00+00:00",
      "revokedAt": null
    }
  ]
}
```

Behavior:

- Both `Active` and `Revoked` history is returned.
- A Revoked row is historical information and grants no current patient access.
- Relationships are ordered by `createdAt`, then relationship UUID.
- Relationships where the current primary profile is only the subject are excluded.
- Subject names are nullable. Full demographics, patient version, manager Account IDs, creator/revoker IDs, and persistence metadata are omitted.
- There is no pagination in the current Phase 3 contract.

Expected statuses are `200`, `401`, and safe `500`.

### `/patients` versus `/care-relationships`

```text
GET /patients              = Who can I manage right now?
GET /care-relationships   = What management relationships do I have/had?
```

For example, after Maria's relationship is revoked, Maria is absent from `/patients`, while the relationship remains in `/care-relationships` with `status: "Revoked"` and a non-null `revokedAt`.

## 10. Read Patient Detail

### `GET /api/v1/patients/{patientId}`

Use the UUID `profileId` from an accessible-patient entry. Success is `200 OK` with exactly the approved patient detail:

```json
{
  "profileId": "20000000-0000-0000-0000-000000000002",
  "beeexyId": "BXY-MANAGED",
  "firstName": "Maria",
  "lastName": "Arias",
  "dateOfBirth": "2012-05-12",
  "sexAssignedAtBirth": "Female",
  "state": "NY",
  "version": 3
}
```

All five demographics are nullable in the detail contract because primary/historical profiles may be incomplete. The response does not expose preferences or the internal authorization reason.

| Target/access | Result |
|---|---|
| Current Account's primary PatientProfile | `200` |
| Patient with an exact Active manager-to-subject relationship | `200` |
| Nonexistent UUID | concealed `404` |
| Unrelated real patient | concealed `404` |
| Patient whose relationship for this manager is Revoked | concealed `404` |
| Invalid/missing Bearer token | `401` |

A malformed UUID or Beeexy ID in the route fails the UUID route constraint with `404`.

## 11. Update Patient Demographics

### `PATCH /api/v1/patients/{patientId}`

The same endpoint updates either the authenticated user's primary profile or an actively managed profile. Send the current detail `version` and at least one of these fields:

- `firstName`
- `lastName`
- `dateOfBirth`
- `sexAssignedAtBirth`
- `state`

Example partial update:

```json
{
  "firstName": "Maria Fernanda",
  "state": "fl",
  "version": 3
}
```

Success is `200 OK` with the complete `PatientDetail` response. In this example the normalized state is `FL` and, if either value changed, `version` becomes `4`.

Exact patch semantics:

- Omitted demographic fields remain unchanged.
- A supplied field is validated as a required value; `null` cannot be used to clear it.
- At least one approved demographic must be present.
- Unknown/immutable fields, including `profileId`, `beeexyId`, `accountId`, relationship fields, and `timezone`, are rejected.
- All values are validated before mutation; a failed request does not partially apply fields.
- Any effective one-field or multi-field patch increments `version` exactly once.
- If all supplied normalized values equal the stored values, the response is `200` and neither version nor update timestamp changes.
- Authorization occurs before body validation. Missing, unrelated, or Revoked targets therefore return the same concealed `404` even when the request body is invalid.

| Status | Meaning for this endpoint |
|---|---|
| `200` | Patch accepted; use the returned full detail and version. |
| `400` | Malformed JSON/request. |
| `401` | Bearer authentication failed or the Account is inactive. |
| `404` | Patient is unavailable, including concealed absent/unrelated/Revoked cases. |
| `409` | The supplied PatientProfile version is stale. |
| `422` | Missing/non-positive version, empty demographic patch, invalid value, or unsupported field. |
| `500` | Safe generic unexpected/invariant failure. |

## 12. Revoke a Care Relationship

### `DELETE /api/v1/care-relationships/{id}`

Use the relationship `id`, not the patient's `profileId`. Send no request body.

The current owning manager receives `204 No Content` for both the first DELETE and subsequent DELETEs of the already-Revoked relationship. The first successful transition is irreversible and records server-controlled revocation metadata. Repeats do not replace that metadata.

An absent or foreign-manager relationship ID returns the same concealed `404`. A malformed UUID also routes to `404`. Invalid authentication returns `401`; unexpected/invariant failures use safe `500` Problem Details.

Revocation:

- does not delete the PatientProfile;
- does not delete its Beeexy ID or demographics;
- does not delete relationship history;
- immediately removes this manager's patient list/read/update access;
- does not affect another manager's independent Active relationship.

Recommended UI flow:

1. Show a confirmation dialog that describes removing this Account's management access, not deleting the person.
2. On `204`, refetch both `/patients` and `/care-relationships`.
3. Invalidate cached detail for the subject.
4. If that subject is `activePatient`, switch to the primary patient or another currently accessible patient.

## 13. Optimistic Concurrency

Patient demographic writes use compare-and-swap behavior:

```text
GET patient → version 3
PATCH with version 3 → 200, version 4
another PATCH using stale version 3 → 409
```

On `409`:

1. Do not retry the old body blindly.
2. Refetch `GET /api/v1/patients/{patientId}`.
3. Refresh the local form/model with the latest patient and version.
4. Tell the user the profile changed elsewhere when appropriate.
5. Let the user reconcile and reapply the intended edit.

Two caregivers can edit the same patient concurrently; the PatientProfile version ensures only one write using a given version wins. A same-value request still must carry the current version, but it does not increment that version.

Remember the naming difference:

```text
/patients/me profileVersion → send as /patients/{id} PATCH body.version
/patients/me version        → use only for /patients/me timezone PATCH
```

## 14. Concealed `404` Behavior

For patient detail and update, the backend deliberately does not reveal whether the target:

- does not exist;
- is a real but unauthorized patient; or
- was previously accessible through a now-Revoked relationship.

The public `404` result is equivalent for these cases. Relationship DELETE similarly conceals missing and foreign-manager relationship IDs.

The frontend must:

- treat the resource as unavailable;
- refetch My Circle state if local access may be stale;
- navigate back to My Circle or another safe screen;
- invalidate stale patient data;
- never guess or display an authorization cause.

Do not build different UX from knowledge of a UUID or Beeexy ID.

## 15. Multiple Managers

The data and authorization model supports independent caregivers:

```text
Manager A ──Active──> Patient X
Manager B ──Active──> Patient X
```

Both managers can independently list, read, and update Patient X. They share the same PatientProfile version, so concurrent demographic edits are protected by optimistic concurrency. Revoking Manager A's relationship removes only A's access; Manager B remains authorized through B's own Active relationship.

The current frontend API cannot invite a second manager or attach an existing profile. `POST /care-relationships` always creates a new managed PatientProfile and its initial relationship. Invitations and linking/claiming flows remain deferred; do not fabricate a way to create the diagram above from the current frontend surface.

## 16. Active Patient and Patient Switcher

The authenticated Account and the selected patient are separate concepts:

```text
Account owner = Jesus
activePatient = Maria
```

The access token always represents Jesus's Account. `activePatient` identifies which currently accessible PatientProfile the UI is operating on. Future clinical modules should use the selected PatientProfile according to each endpoint's contract and must not assume that the current Account owner is always the current patient.

Recommended state shape:

```ts
type ActivePatientState = {
  profileId: string;
  accessType: "Primary" | "Managed";
};
```

Build the switcher only from the latest `/patients` response. Persisting its `profileId` in frontend/session storage is a client decision; the backend does not persist it. On bootstrap, list refresh, `404`, or revocation, validate the stored selection against the latest accessible set and fall back safely to the primary entry.

## 17. TypeScript Contract Reference

These documentation-only interfaces match the current camel-case JSON. UUIDs, date-only values, and timestamps are strings at the transport boundary.

```ts
type RelationshipType =
  | "Parent"
  | "LegalGuardian"
  | "Caregiver"
  | "Spouse"
  | "Child"
  | "Sibling"
  | "Other";

type RelationshipStatus = "Active" | "Revoked";
type SexAssignedAtBirth = "Male" | "Female";

interface AccessiblePatient {
  profileId: string; // UUID
  beeexyId: string;
  firstName: string | null;
  lastName: string | null;
  accessType: "Primary" | "Managed";
  relationship: {
    relationshipId: string; // UUID
    type: RelationshipType;
  } | null;
}

interface AccessiblePatientsResponse {
  patients: AccessiblePatient[];
}

interface CareRelationship {
  id: string; // UUID
  subject: {
    profileId: string; // UUID
    beeexyId: string;
    firstName: string | null;
    lastName: string | null;
  };
  type: RelationshipType;
  status: RelationshipStatus;
  attestationVersion: string;
  attestedAt: string; // ISO-8601 instant
  createdAt: string; // ISO-8601 instant
  revokedAt: string | null; // ISO-8601 instant
}

interface CareRelationshipListResponse {
  relationships: CareRelationship[];
}

interface PatientDetail {
  profileId: string; // UUID
  beeexyId: string;
  firstName: string | null;
  lastName: string | null;
  dateOfBirth: string | null; // YYYY-MM-DD
  sexAssignedAtBirth: SexAssignedAtBirth | null;
  state: string | null; // canonical two-letter code
  version: number;
}

interface CreateManagedPatientRequest {
  relationshipType: RelationshipType;
  attestationVersion: string;
  attestationAccepted: true;
  patient: {
    firstName: string;
    lastName: string;
    dateOfBirth: string; // YYYY-MM-DD
    sexAssignedAtBirth: SexAssignedAtBirth;
    state: string;
  };
}

interface CreateManagedPatientResponse {
  relationship: {
    id: string; // UUID
    type: RelationshipType;
    status: "Active";
    attestationVersion: string;
    attestedAt: string; // ISO-8601 instant
  };
  patient: {
    profileId: string; // UUID
    beeexyId: string;
    firstName: string;
    lastName: string;
    dateOfBirth: string; // YYYY-MM-DD
    sexAssignedAtBirth: SexAssignedAtBirth;
    state: string;
    version: number;
  };
}

interface UpdatePatientRequest {
  version: number;
  firstName?: string;
  lastName?: string;
  dateOfBirth?: string; // YYYY-MM-DD
  sexAssignedAtBirth?: SexAssignedAtBirth;
  state?: string;
}
```

Although response demographics can be nullable, valid managed-patient creation requires non-null values. An update must include at least one optional demographic field in addition to `version`.

### Suggested frontend service methods

```ts
listAccessiblePatients(): Promise<AccessiblePatientsResponse>
createManagedPatient(request: CreateManagedPatientRequest): Promise<CreateManagedPatientResponse>
listCareRelationships(): Promise<CareRelationshipListResponse>
getPatient(patientId: string): Promise<PatientDetail>
updatePatient(patientId: string, patch: UpdatePatientRequest): Promise<PatientDetail>
revokeCareRelationship(relationshipId: string): Promise<void>
```

These are conceptual boundaries only. Phase 3 does not require a particular HTTP client, cache, or state-management library.

## 18. Frontend Screens and Refresh Rules

### Suggested screens

- **Complete your profile:** first name, last name, date of birth, sex assigned at birth, and state; submit to `/patients/{primaryProfileId}` with `profileVersion` as the body `version`.
- **Who are you caring for?:** `Just me` selects the primary patient; `Someone else` opens managed-patient creation.
- **My Circle:** show the primary patient, active managed patients, and an `Add person` action.
- **Patient detail:** load the full approved demographics and show relationship context from the list/history model.
- **Edit patient:** PATCH approved fields with the latest PatientProfile version.
- **Remove access:** confirm and revoke the relationship without describing it as patient deletion.
- **Patient switcher:** switch only among profiles from the current accessible-patient list.

### State refresh rules

After create:

- refetch or add the returned patient to the accessible list;
- refetch relationship history;
- optionally select the new managed patient as `activePatient`;
- retain the returned patient version if detail is cached.

After update:

- replace cached detail with the response;
- update the name in list/switcher state if it changed;
- retain the returned version for the next edit.

After revoke:

- refetch both lists;
- invalidate the revoked patient's detail for this manager;
- if it was active, select the primary or another accessible patient.

## 19. Error Handling

Expected application failures use `application/problem+json`. A typical validation response is:

```json
{
  "status": 422,
  "title": "Request validation failed.",
  "detail": "State must be a valid two-letter U.S. state code.",
  "instance": "/api/v1/patients/20000000-0000-0000-0000-000000000002",
  "errorCode": "patient.invalid_state",
  "correlationId": "<request-correlation-id>"
}
```

`detail`, `errorCode`, and other fields depend on the failure path. Do not require an `errorCode` on every Problem Details response. Preserve `correlationId` for support diagnostics without attaching tokens or demographics.

| Status | Phase 3 frontend handling |
|---|---|
| `200` | List/detail/update success. Replace local state with the returned representation. |
| `201` | Managed patient and relationship created atomically. |
| `204` | Relationship revocation succeeded; there is no response body. |
| `400` | Malformed JSON/HTTP request; correct the client serialization. |
| `401` | Authentication/session is not accepted. Use the established coordinated Phase 2 refresh flow, then clear the session if unrecoverable. |
| `404` | Resource unavailable, including concealed authorization/revocation. Do not infer the cause. |
| `409` | PATCH concurrency conflict or POST uniqueness conflict, depending on endpoint. Refetch relevant state; do not blindly retry. |
| `422` | Request/domain validation failure. Show suitable field feedback when `errorCode` permits. |
| `500` | Safe generic unexpected failure. Do not show or expect internal exception/database details; use `correlationId` for support. |

Common Phase 3 validation codes include `care_relationship.invalid_type`, `care_relationship.attestation_required`, `care_relationship.invalid_attestation_version`, `care_relationship.unsupported_field`, `patient.demographics_required`, `patient.invalid_first_name`, `patient.invalid_last_name`, `patient.invalid_date_of_birth`, `patient.invalid_sex_assigned_at_birth`, `patient.invalid_state`, `patient.invalid_version`, `patient.no_demographic_fields`, and `patient.unsupported_field`.

## 20. Security Requirements

- Never authorize locally from a Beeexy ID.
- Knowledge of a UUID, Beeexy ID, relationship ID, creator ID, or subject identity does not imply access.
- Treat the backend's latest accessible-patient response and per-request authorization result as authoritative.
- Never try to distinguish the reason behind a concealed `404`.
- Do not log demographics unnecessarily; avoid request/response body logging for these routes.
- Never log access or refresh tokens.
- Do not send manager Account/Profile IDs or subject identity fields during creation; the backend derives the manager from the token and creates the subject.
- Do not add unsupported request fields.
- Always send the latest PatientProfile version on PATCH.
- Do not treat an Active management relationship as permission for record sharing, provider access, or external disclosure.

## 21. Current Limitations

The frontend must not invent APIs or behavior for:

- caregiver invitations;
- profile claiming;
- linking arbitrary existing patients;
- legal or identity verification;
- minor-specific workflows;
- adult consent workflows;
- granular manager permissions;
- record sharing;
- relationship reactivation;
- FHIR relationship or consent mapping.

These capabilities remain deferred. The current DELETE transition is irreversible through the public API.

## 22. Frontend Integration Checklist

- [ ] Load primary and actively managed profiles from `/patients`.
- [ ] Detect incomplete primary demographics from the five nullable fields.
- [ ] Complete the primary PatientProfile through `/patients/{primaryProfileId}`.
- [ ] Keep PatientProfile `profileVersion` separate from preference `version`.
- [ ] Implement the `Who are you caring for?` choice.
- [ ] Implement frontend/session `activePatient` state.
- [ ] Implement a patient switcher from currently accessible profiles.
- [ ] Implement `Add to My Circle` with managed-patient creation.
- [ ] Send only the five approved demographics.
- [ ] Offer exactly the seven relationship types.
- [ ] Use product-approved attestation content and a matching version.
- [ ] Implement full patient detail.
- [ ] Edit demographics with the current patient version.
- [ ] Handle `409` by refetching and reconciling.
- [ ] Show relationship-revocation confirmation.
- [ ] Refetch both lists and invalidate detail after revoke.
- [ ] Switch `activePatient` safely after revoked access.
- [ ] Treat concealed `404` as unavailable without guessing why.
- [ ] Never authorize from a Beeexy ID or known UUID.
- [ ] Do not invent deferred APIs.

## 23. Verification Notes

This guide documents exactly six Phase 3 operations and no Phase 4 endpoint. Route mappings, request and response DTO property names/nullability, relationship enums/statuses, demographic value objects, authorization decisions, optimistic concurrency, revocation behavior, Problem Details mappings, OpenAPI declarations, Phase 3 endpoint tests, and the Phase 3.8 security acceptance suite were checked against the current repository.

Phase 2 content is intentionally limited to its authentication/session prerequisites and the `/auth/me` plus `/patients/me` bootstrap contracts. The remaining external product-content dependency is the final human-readable attestation wording and the product-controlled version that identifies it.
