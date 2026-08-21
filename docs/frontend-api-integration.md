# Beeexy Backend API — Frontend Integration Guide

## 1. Purpose

This document is the implementation-oriented handoff for a frontend integrating the current Beeexy backend. It describes the routes, JSON contracts, authentication lifecycle, errors, and configuration that exist now. The backend implementation and integration tests are the source of truth; future functionality must not be inferred from this document as if it were already implemented.

## 2. Current Backend Scope

The current backend provides Phase 1 health checks, Phase 2 authentication/profile preferences, and the Phase 3 My Circle patient-management APIs through relationship revocation and approved patient demographics.

Current public/business endpoints:

| Method | Route | Authentication | Purpose |
|---|---|---|---|
| `POST` | `/api/v1/auth/email/challenges` | None | Request an email OTP |
| `POST` | `/api/v1/auth/email/verify` | None | Verify an OTP and issue a Beeexy session |
| `POST` | `/api/v1/auth/google` | None | Exchange a Google ID token for a Beeexy session |
| `POST` | `/api/v1/auth/refresh` | None (body contains refresh token) | Rotate a refresh session |
| `POST` | `/api/v1/auth/logout` | Bearer access token | Revoke the refresh-session family |
| `GET` | `/api/v1/auth/me` | Bearer access token | Read the authenticated account |
| `GET` | `/api/v1/patients/me` | Bearer access token | Read the current primary profile |
| `PATCH` | `/api/v1/patients/me` | Bearer access token | Update the current primary profile |
| `GET` | `/api/v1/patients` | Bearer access token | List primary and actively managed patients |
| `GET` | `/api/v1/patients/{patientId}` | Bearer access token | Read authorized patient demographics |
| `PATCH` | `/api/v1/patients/{patientId}` | Bearer access token | Partially update authorized patient demographics |
| `GET` | `/api/v1/care-relationships` | Bearer access token | List relationship history |
| `POST` | `/api/v1/care-relationships` | Bearer access token | Create a managed patient and active relationship |
| `DELETE` | `/api/v1/care-relationships/{id}` | Bearer access token | Irreversibly revoke a manager-owned relationship |

Health/deployment endpoints:

| Method | Route | Authentication | Purpose |
|---|---|---|---|
| `GET` | `/health/live` | None | Process liveness; does not require PostgreSQL |
| `GET` | `/health/ready` | None | PostgreSQL/application readiness |

There are no password, Apple, clinical, FHIR, invitation, profile-claiming, or granular relationship-permission APIs in the current backend.

## 3. Base URL and Environments

The frontend should obtain the API origin from its environment/configuration layer rather than embedding it in components.

- Development launch profile `http`: `http://localhost:5105`.
- Production: use the deployed backend origin configured for that environment; the backend does not prescribe a frontend variable name.

Therefore, local development normally uses `http://localhost:5105/api/v1/...`, but the frontend should treat that as an environment value, not a permanent constant. The API launch profile is defined in `src/Beeexy.Api/Properties/launchSettings.json`.

## 4. Authentication Architecture

Email authentication:

```text
request challenge → user receives OTP → verify OTP → Beeexy access/refresh token pair
```

Google authentication:

```text
Google Identity Services → Google ID token → POST /api/v1/auth/google → Beeexy token pair
```

Google only proves the external identity. Google ID tokens are not Beeexy bearer tokens. The frontend must use the `accessToken` returned by Beeexy in the `Authorization` header. The Beeexy `beeexyId` is an informational patient identifier, not an authentication credential and not an authorization selector.

## 5. Token Lifecycle

### Access token

The access token is a signed JWT (HS256 on the backend) with the configured issuer and audience. The current Development/production configuration defaults are:

- Lifetime: 15 minutes (`Authentication:Tokens:AccessTokenLifetimeMinutes`).
- Important claims: `sub` (account UUID), `sid` (refresh-session UUID), `jti`, and `iat`.
- It is validated for signature, issuer, audience, lifetime, and required expiration.

Send it as:

```http
Authorization: Bearer <accessToken>
```

The frontend should use the returned `accessTokenExpiresAt` rather than decoding claims as its only expiry source. Token timestamps are ISO-8601 UTC values, for example `2026-08-20T23:15:00+00:00`.

### Refresh token

The refresh token is an opaque random token. It is returned in JSON, not an HttpOnly cookie, and is stored hashed by the backend. The current default lifetime is 30 days (`Authentication:Tokens:RefreshTokenLifetimeDays`).

Every successful refresh rotates the session and returns a new access token and a new refresh token. Replace both values atomically and discard the previous refresh token immediately.

If an old rotated refresh token is submitted again, Beeexy treats this as reuse and revokes the affected refresh-token family, including the current descendant. The refresh request then returns `401`. This makes concurrent refresh requests especially dangerous.

### Recommended client lifecycle

1. On email or Google login, receive and store the current token pair using the frontend's chosen secure session strategy.
2. Send the access token as a Bearer token for authenticated requests.
3. Refresh before or after access-token expiry using `/api/v1/auth/refresh`.
4. Atomically replace both tokens with the returned pair.
5. Never submit the old refresh token again after a successful rotation.
6. If refresh returns `401`, clear local authentication state and require sign-in.
7. On logout, call `/api/v1/auth/logout` when possible, then clear local tokens/session state regardless of the result.

The backend does not use cookies or a JWT blacklist. Logout revokes refresh capability, but an already-issued access JWT remains cryptographically valid until its short expiration under the current design.

## 6. API Endpoints

All JSON property names below are the actual camel-case JSON representation of the backend records. Requests should send `Content-Type: application/json`.

### 6.1 `POST /api/v1/auth/email/challenges`

Purpose: request a one-time sign-in code for an email address.

Authentication: none.

Request body:

```json
{
  "email": "person@example.com"
}
```

Success: `202 Accepted`, with an empty response body. The OTP is delivered by the configured authentication email sender; it is never returned by this endpoint.

Important statuses:

- `400 Bad Request`: malformed JSON/request syntax.
- `422 Unprocessable Entity`: invalid email or other request/domain validation failure.
- `429 Too Many Requests`: email/IP rate limit exceeded. The response may include `Retry-After`.
- `500 Internal Server Error`: unexpected persistence or email-delivery failure.

The response is deliberately generic for registered and unregistered addresses. Do not use this endpoint's response to infer whether an account exists. Show the OTP entry UI after `202`, without expecting an OTP in JSON.

Related next step: submit the email and user-entered code to `/api/v1/auth/email/verify`.

### 6.2 `POST /api/v1/auth/email/verify`

Purpose: consume a valid OTP, provision/reuse the account and primary profile, and issue a Beeexy session.

Authentication: none.

Request body:

```json
{
  "email": "person@example.com",
  "code": "123456"
}
```

Success: `200 OK` with the authentication response:

```json
{
  "accessToken": "<jwt>",
  "refreshToken": "<opaque-refresh-token>",
  "accessTokenExpiresAt": "2026-08-20T23:15:00+00:00",
  "refreshTokenExpiresAt": "2026-09-19T23:00:00+00:00",
  "account": {
    "accountId": "00000000-0000-0000-0000-000000000000",
    "profileId": "00000000-0000-0000-0000-000000000000",
    "beeexyId": "BXY-..."
  }
}
```

Important statuses:

- `400`: malformed JSON/request syntax.
- `401`: wrong, expired, or otherwise unverifiable code; show a safe verification failure and allow a new challenge.
- `409`: the challenge was already consumed; request a new challenge.
- `422`: invalid email/code shape or domain validation failure.
- `429`: verification-attempt or request rate limit exceeded.
- `500`: unexpected server/infrastructure failure.

Store the current token pair and account summary on success. `accountId`, `profileId`, and `beeexyId` are identifiers returned for display/state correlation; none is a bearer credential.

Related next step: call `/api/v1/auth/me` and `/api/v1/patients/me` to bootstrap account/profile state.

### 6.3 `POST /api/v1/auth/google`

Purpose: authenticate with Google and issue the same Beeexy session shape as email verification.

Authentication: none.

Request body:

```json
{
  "credential": "<google-id-token-jwt>"
}
```

`credential` must be the Google ID token JWT returned in the `credential` field by Google Identity Services. Do not send an email, Google access token, or Beeexy ID. The backend validates the token against the configured Google Web Client ID, issuer/signature/expiry, and Google subject. A new Google user must have a verified email so the backend can provision the account.

Success: `200 OK` with the exact same authentication response as email verification (`accessToken`, `refreshToken`, both expirations, and `account`).

Important statuses:

- `400`: malformed JSON/request syntax.
- `401`: invalid/rejected Google credential, unverified required identity, disabled account, or identity conflict.
- `422`: missing/invalid credential request shape.
- `503`: Google authentication is disabled in backend configuration or the Google provider is unavailable.
- `500`: unexpected server/infrastructure failure.

Related next step: continue with the same Beeexy token/bootstrap flow as email login. The Google credential itself must not be stored as the Beeexy session.

### 6.4 `POST /api/v1/auth/refresh`

Purpose: rotate the current refresh session and obtain a new token pair.

Authentication: no Bearer header is required; the refresh token is in the JSON body.

Request body:

```json
{
  "refreshToken": "<current-opaque-refresh-token>"
}
```

Success: `200 OK` with the same authentication response shape as login, including a new `accessToken`, `refreshToken`, expiration timestamps, and account summary.

Important statuses:

- `400`: malformed JSON/request syntax.
- `401`: missing, invalid, expired, revoked, or reused refresh token.
- `500`: unexpected server/infrastructure failure.

**Never submit the same refresh token twice after a successful rotation.** Use one single-flight/mutex refresh operation when multiple API requests discover an expired access token. Reuse of an old rotated token can revoke the whole affected refresh family.

Related next step: atomically replace both locally held tokens, then retry waiting requests with the new access token.

### 6.5 `POST /api/v1/auth/logout`

Purpose: revoke the refresh-session family belonging to the current authenticated session.

Authentication: required:

```http
Authorization: Bearer <accessToken>
```

Request body: none. Do not send the refresh token to this endpoint.

Success: `204 No Content` with an empty body.

Important statuses:

- `401`: missing/invalid access token or session identity.
- `500`: unexpected server/infrastructure failure.

Logout revokes refresh sessions, not already-issued JWTs. Clear local authentication state immediately after a successful logout. If the access token is already invalid and logout returns `401`, clear local state anyway; there is no valid session to preserve locally.

Related next step: navigate to the signed-out state and require a new email or Google sign-in.

### 6.6 `GET /api/v1/auth/me`

Purpose: read the authenticated account, its primary profile reference, and timezone preference.

Authentication: required Bearer access token.

Request headers:

```http
Authorization: Bearer <accessToken>
```

Success: `200 OK`:

```json
{
  "accountId": "00000000-0000-0000-0000-000000000000",
  "status": "active",
  "primaryProfile": {
    "profileId": "00000000-0000-0000-0000-000000000000",
    "beeexyId": "BXY-..."
  },
  "preferences": {
    "timezone": "Etc/UTC"
  }
}
```

The account is resolved from the JWT `sub` claim. The primary profile reference is returned for information and state bootstrap; it is not a client-selected authorization target. `beeexyId` is informational.

Important statuses:

- `200`: current account found.
- `401`: missing/expired/invalid access token.
- `500`: an unexpected/invariant failure resolving the account/profile; treat as a server error and use the correlation ID for support.

Related next step: load `/api/v1/patients/me` for the current profile data.

### 6.7 `GET /api/v1/patients/me`

Purpose: read the authenticated account's current primary patient profile.

Authentication: required Bearer access token.

Request headers:

```http
Authorization: Bearer <accessToken>
```

No patient ID, account ID, or Beeexy ID is supplied in the URL. The backend resolves the primary profile from the authenticated account.

Success: `200 OK`:

```json
{
  "profileId": "00000000-0000-0000-0000-000000000000",
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

`profileVersion` belongs to `PatientProfile` demographics. `version` belongs only to the account-scoped timezone preference. Provisioned or historical primary profiles can return `null` demographics until profile completion.

Important statuses:

- `401`: missing/invalid access token.
- `404`: no current primary profile is available.
- `500`: unexpected server/invariant failure.

Related next step: use the returned `version` in `/api/v1/patients/me` PATCH requests.

### 6.8 `PATCH /api/v1/patients/me`

Purpose: partially update the current primary profile. The only currently supported mutable field is `timezone`.

Authentication: required Bearer access token.

Request headers:

```http
Authorization: Bearer <accessToken>
Content-Type: application/json
```

Request body:

```json
{
  "timezone": "America/Lima",
  "version": 1
}
```

`timezone` may be `null` to leave it unchanged, but `version` must be a positive integer from the latest profile response. Unknown JSON fields are rejected as unsupported; do not send future fields until the backend implements them.

Success: `200 OK` with the updated profile response and its new version:

```json
{
  "profileId": "00000000-0000-0000-0000-000000000000",
  "beeexyId": "BXY-...",
  "firstName": null,
  "lastName": null,
  "dateOfBirth": null,
  "sexAssignedAtBirth": null,
  "state": null,
  "profileVersion": 1,
  "preferences": {
    "timezone": "America/Lima"
  },
  "version": 2
}
```

Important statuses:

- `400`: malformed JSON/request syntax.
- `401`: missing/invalid access token.
- `404`: no current primary profile is available.
- `409`: stale version; the profile changed after it was read. Re-fetch and reconcile rather than blindly retrying the old body.
- `422`: non-positive version, unrecognized IANA timezone, or unsupported field.
- `500`: unexpected persistence/invariant failure.

Concurrency example:

```text
GET → version 3
PATCH version 3 → success, version 4
another PATCH version 3 → 409
```

Related next step: update local profile state from the returned response; after `409`, call GET again before attempting another update.

### 6.9 My Circle and approved demographics

The only approved demographic fields are `firstName`, `lastName`, `dateOfBirth`, `sexAssignedAtBirth` (`Male` or `Female`), and `state` (a valid two-letter code for one of the 50 U.S. states). Dates use `YYYY-MM-DD`. State input is trimmed and normalized to uppercase. Unknown fields are rejected with `422`. Relationship types are exactly `Parent`, `LegalGuardian`, `Caregiver`, `Spouse`, `Child`, `Sibling`, and `Other`; relationship statuses are `Active` and `Revoked`.

Create a managed patient and relationship with `POST /api/v1/care-relationships`:

```json
{
  "relationshipType": "Child",
  "attestationVersion": "phase-3.6-approved",
  "attestationAccepted": true,
  "patient": {
    "firstName": "Maria",
    "lastName": "Arias",
    "dateOfBirth": "2012-05-12",
    "sexAssignedAtBirth": "Female",
    "state": "NY"
  }
}
```

Success is `201 Created`:

```json
{
  "relationship": {
    "id": "00000000-0000-0000-0000-000000000000",
    "type": "Child",
    "status": "Active",
    "attestationVersion": "phase-3.6-approved",
    "attestedAt": "2026-08-21T12:00:00+00:00"
  },
  "patient": {
    "profileId": "00000000-0000-0000-0000-000000000000",
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

`GET /api/v1/patients` returns the primary patient first and active managed patients after it. Each summary contains only `profileId`, `beeexyId`, nullable `firstName`/`lastName`, `accessType`, and the nullable relationship summary; DOB, sex, state, and version are intentionally omitted:

```json
{
  "patients": [
    {
      "profileId": "10000000-0000-0000-0000-000000000001",
      "beeexyId": "BXY-PRIMARY",
      "firstName": null,
      "lastName": null,
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

`GET /api/v1/care-relationships` returns only relationships where the current primary patient is the manager. It retains both Active and Revoked history and exposes only nullable `firstName`/`lastName` in each subject summary:

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
      "attestationVersion": "phase-3.6-approved",
      "attestedAt": "2026-08-21T12:00:00+00:00",
      "createdAt": "2026-08-21T12:00:00+00:00",
      "revokedAt": null
    }
  ]
}
```

`GET /api/v1/patients/{patientId}` returns the complete authorized detail:

```json
{
  "profileId": "00000000-0000-0000-0000-000000000000",
  "beeexyId": "BXY-...",
  "firstName": "Maria",
  "lastName": "Arias",
  "dateOfBirth": "2012-05-12",
  "sexAssignedAtBirth": "Female",
  "state": "NY",
  "version": 1
}
```

Partially update any subset of the five fields with `PATCH /api/v1/patients/{patientId}` and the latest patient `version`:

```json
{
  "firstName": "Maria Fernanda",
  "state": "FL",
  "version": 1
}
```

Success is `200` with the same full detail shape and version 2. An effective update increments the patient version once. A same-value update returns `200` without incrementing it. A stale version returns `409`; refetch detail before reconciling. Missing/invalid version, invalid demographics, an empty demographic patch, or unknown fields return `422`. Absent, unrelated, or revoked access returns the same concealed `404`, including when the body is invalid.

All My Circle routes require Bearer authentication. A UUID is the route selector; neither a Beeexy ID nor knowledge of a relationship/creator identifier grants access. A primary patient is the `PatientProfile` owned by the authenticated account (`accountId` is populated); a managed patient uses that same model/table but has no account. A relationship grants only the implemented My Circle list/read/demographic-update capability. It is not record sharing, provider access, external disclosure, or consent delegation.

Revocation uses `DELETE /api/v1/care-relationships/{id}` with no request body. An owning manager receives `204 No Content` for both the first revocation and repeats. Foreign or nonexistent relationship IDs receive concealed `404`, never an existence-revealing `403`. The first revocation preserves a stable server timestamp and revoker metadata; repeats do not overwrite them. The subject immediately disappears from that manager's patient list and detail/PATCH access returns concealed `404`, while the Revoked relationship remains in relationship history. The patient, Beeexy ID, demographics, and any other manager's independent access remain persisted.

The request's `attestationAccepted` and version provide technical recording only. They do not establish legal or identity verification, and final human-readable product/legal wording is an external product-content dependency.

### 6.10 `GET /health/live`

Purpose: deployment/process liveness. Public and unauthenticated. PostgreSQL is not required for this check.

Success: `200 OK`:

```json
{
  "status": "Healthy",
  "correlationId": "<request-correlation-id>"
}
```

`503` indicates process-level health failure. This is generally for deployment monitoring, not application session bootstrap.

### 6.11 `GET /health/ready`

Purpose: deployment readiness, including PostgreSQL availability. Public and unauthenticated.

Success: `200 OK` with the same health response shape. `503 Service Unavailable` returns `status: "Unhealthy"` and a correlation ID when PostgreSQL is unavailable. It intentionally does not expose connection details.

## 7. Error Handling

Expected API failures use `application/problem+json`. The backend's common Problem Details fields are:

```json
{
  "status": 401,
  "title": "Authentication failed.",
  "detail": "The authentication session is invalid.",
  "instance": "/api/v1/auth/refresh",
  "errorCode": "<present for domain/request validation errors>",
  "correlationId": "<request-correlation-id>"
}
```

`detail`, `errorCode`, and `instance` are conditional according to the mapped exception. `correlationId` is added by the API error pipeline. The same ID is also returned in the `X-Correlation-ID` response header; clients should include it in support diagnostics without logging tokens, OTPs, or Google credentials.

Frontend handling categories:

- `400`: request serialization/malformed JSON problem; fix the client request.
- `401`: access token/session/credential is not accepted. For an authenticated API request, attempt one coordinated refresh; if refresh also returns `401`, clear the session. For login verification, allow a new challenge/code.
- `404`: resource is absent or concealed by authorization; do not infer whether a patient or relationship exists.
- `409`: consumed OTP replay or stale profile update; request a new OTP or re-fetch profile state respectively.
- `422`: show validation feedback where appropriate; honor the returned `errorCode` when present.
- `429`: show a retry message and respect `Retry-After` when present.
- `500`: generic unexpected failure; do not display internal exception details.
- `503`: temporary/unconfigured provider or readiness failure; show a service-unavailable state and retry according to product policy.

The backend does not expose internal exception class names as its public error contract.

### Status-code quick reference

| Status | Current Beeexy use |
|---|---|
| `200` | Successful token issuance/rotation, account/profile reads, or profile update |
| `201` | Managed patient and care relationship created atomically |
| `202` | Email challenge accepted; response body is empty |
| `204` | Logout or relationship revocation succeeded; response body is empty |
| `400` | Malformed JSON or malformed HTTP request |
| `401` | Invalid/expired access or refresh session, invalid OTP, or rejected Google identity |
| `404` | Missing route/resource or concealed patient/relationship authorization denial |
| `409` | Consumed OTP replay or stale profile version |
| `422` | Request/domain validation failure, including invalid email, timezone, or unsupported profile field |
| `429` | Email/IP throttling or exhausted OTP verification attempts |
| `500` | Unexpected server, persistence, or delivery failure |
| `503` | Google authentication disabled/unavailable, or readiness dependency unavailable |

## 8. Optimistic Concurrency

There are two deliberately separate compare-and-swap tokens. `/patients/me` timezone PATCH uses the preference `version`; patient-demographic PATCH uses the detail `version` (shown as `profileVersion` only in `/patients/me`). A stale token produces `409` without overwriting current state. Re-fetch the owning resource, reconcile the UI, and send its latest token.

## 9. Frontend Authentication Flows

### Email

```text
1. User enters email.
2. POST /api/v1/auth/email/challenges.
3. On 202, show OTP input; never expect an OTP in the response.
4. User enters code.
5. POST /api/v1/auth/email/verify.
6. Store the current Beeexy access/refresh pair.
7. GET /api/v1/auth/me.
8. GET /api/v1/patients/me.
```

On `422`, show email/code validation. On `401`, the code is not accepted or has expired. On `409`, request a new challenge. On `429`, wait and retry according to `Retry-After`.

### Google

```text
1. Initialize Google Identity Services with the frontend Web Client ID.
2. User signs in with Google.
3. Receive the Google ID-token JWT in the callback's credential field.
4. POST { credential } to /api/v1/auth/google.
5. Store the Beeexy access/refresh pair returned by the API.
6. Continue exactly like an email-authenticated user.
```

Do not include a Google Client Secret in frontend or backend browser configuration. The Google client ID must match the client ID configured in the backend.

### Refresh

Use a single-flight/mutex-style refresh operation. If several API calls receive `401` at once, only one should submit the current refresh token; other calls should wait:

```text
multiple API requests receive 401
        ↓
one refresh request only
        ↓
other requests wait
        ↓
replace both tokens
        ↓
retry requests
```

If refresh fails with `401`, clear the session and show sign-in. Do not retry the same refresh token repeatedly.

### Logout

```text
POST /api/v1/auth/logout with current access token
→ clear frontend authentication state
→ navigate to signed-out state
```

If logout returns `401`, the safest behavior is still to clear local state, because the backend no longer accepts the current access token/session identity.

### Session bootstrap

At application startup, if no local session exists, remain signed out. If a session exists, use the access token while valid; refresh through the single-flight mechanism when needed; then call `/api/v1/auth/me` and `/api/v1/patients/me`. Any failed refresh `401` or unrecoverable authenticated `401` clears the local session. Do not invent a cookie or server-side session mechanism: the current backend returns both tokens in JSON.

## 10. TypeScript Contract Reference

These are documentation-only interfaces matching the current JSON contracts. Dates are ISO-8601 strings at the transport boundary; UUIDs are strings.

```ts
interface EmailChallengeRequest {
  email: string | null;
}

interface EmailVerificationRequest {
  email: string | null;
  code: string | null;
}

interface GoogleAuthenticationRequest {
  credential: string | null;
}

interface RefreshRequest {
  refreshToken: string | null;
}

interface AccountSummary {
  accountId: string;
  profileId: string;
  beeexyId: string;
}

interface AuthenticationResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
  account: AccountSummary;
}

interface CurrentAccountResponse {
  accountId: string;
  status: string; // currently "active" for an active account
  primaryProfile: {
    profileId: string;
    beeexyId: string;
  };
  preferences: {
    timezone: string;
  };
}

interface CurrentPatientResponse {
  profileId: string;
  beeexyId: string;
  firstName: string | null;
  lastName: string | null;
  dateOfBirth: string | null; // YYYY-MM-DD
  sexAssignedAtBirth: "Male" | "Female" | null;
  state: string | null;
  profileVersion: number;
  preferences: {
    timezone: string;
  };
  version: number;
}

interface UpdatePatientRequest {
  timezone: string | null;
  version: number;
}

interface PatientDetailResponse {
  profileId: string;
  beeexyId: string;
  firstName: string | null;
  lastName: string | null;
  dateOfBirth: string | null;
  sexAssignedAtBirth: "Male" | "Female" | null;
  state: string | null;
  version: number;
}

interface UpdatePatientDemographicsRequest {
  version: number;
  firstName?: string;
  lastName?: string;
  dateOfBirth?: string;
  sexAssignedAtBirth?: "Male" | "Female";
  state?: string;
}

type RelationshipType =
  | "Parent"
  | "LegalGuardian"
  | "Caregiver"
  | "Spouse"
  | "Child"
  | "Sibling"
  | "Other";

type RelationshipStatus = "Active" | "Revoked";

interface AccessiblePatientsResponse {
  patients: Array<{
    profileId: string;
    beeexyId: string;
    firstName: string | null;
    lastName: string | null;
    accessType: "Primary" | "Managed";
    relationship: {
      relationshipId: string;
      type: RelationshipType;
    } | null;
  }>;
}

interface CreateManagedPatientRequest {
  relationshipType: RelationshipType;
  attestationVersion: string;
  attestationAccepted: true;
  patient: {
    firstName: string;
    lastName: string;
    dateOfBirth: string;
    sexAssignedAtBirth: "Male" | "Female";
    state: string;
  };
}

interface CreateManagedPatientResponse {
  relationship: {
    id: string;
    type: RelationshipType;
    status: "Active";
    attestationVersion: string;
    attestedAt: string;
  };
  patient: {
    profileId: string;
    beeexyId: string;
    firstName: string;
    lastName: string;
    dateOfBirth: string;
    sexAssignedAtBirth: "Male" | "Female";
    state: string;
    version: number;
  };
}

interface CareRelationshipsResponse {
  relationships: Array<{
    id: string;
    subject: {
      profileId: string;
      beeexyId: string;
      firstName: string | null;
      lastName: string | null;
    };
    type: RelationshipType;
    status: RelationshipStatus;
    attestationVersion: string;
    attestedAt: string;
    createdAt: string;
    revokedAt: string | null;
  }>;
}
```

The backend records are nullable at request binding time, but valid requests must satisfy the endpoint/use-case validation. `UpdatePatientRequest` currently supports only `timezone` and `version`; unknown fields are rejected.

## 11. Security Requirements for Frontend

- Never use `beeexyId` as authentication or as permission to select a profile.
- Never submit or retain an old refresh token after successful rotation.
- Never log access tokens, refresh tokens, OTPs, or Google ID credentials.
- Never place tokens in query parameters or URLs.
- Send access tokens only in the `Authorization: Bearer` header.
- On refresh `401`, clear the local authentication state.
- Serialize refresh operations; do not run concurrent refreshes with one refresh token.
- Treat the Google ID token as a one-time identity proof for the exchange, not as a Beeexy API token.
- Do not assume logout immediately invalidates an already-issued access JWT.
- Do not expose OTPs or development in-memory sender internals in product UI/API behavior.

## 12. CORS and Environment Configuration

The backend allows an explicit list of HTTP(S) origins from `Cors:AllowedOrigins` (environment form: `Cors__AllowedOrigins__0`, etc.). The checked-in default is `http://localhost:3000`; production must configure explicit frontend origins. Wildcards, paths, credentials, queries, fragments, and trailing slashes are rejected by startup validation.

The API allows headers and methods but does not call `AllowCredentials`; authentication is sent in JSON/Bearer headers, not cookies. The frontend therefore does not need credentialed cross-origin cookies for the current token architecture.

Frontend-relevant configuration:

- API base URL: frontend environment/configuration.
- Google Web Client ID: frontend Google Identity Services configuration and matching backend `Authentication:Google:ClientId` when Google is enabled.
- Frontend origin: backend `Cors:AllowedOrigins`.

Never expose or place these in frontend configuration:

- PostgreSQL connection credentials.
- JWT signing key.
- OTP HMAC key.
- Resend API key.
- Any other backend secret.

## 13. Time Handling

API instants, including token expiration fields, are serialized as ISO-8601 UTC/offset timestamps. Treat them as instants rather than local wall-clock strings. User profile timezone preference is separate and uses an IANA identifier such as `America/Lima` or the default `Etc/UTC`. Use that preference for user-facing display; do not reinterpret token expiration using the profile timezone.

## 14. Current Limitations / Deferred APIs

The current backend does not provide APIs for:

- Apple authentication.
- Password authentication.
- Caregiver-only accounts, invitations, or dependent profile claiming.
- Additional demographics not approved/implemented in the current profile contract.
- Clinical records, FHIR resources, triage, care plans, visits, AI, sharing, scheduling, notifications, or other later phases.

**If the frontend needs a feature not documented here, inspect the backend and implementation plan before inventing a request contract.**

## 15. Integration Checklist

- [ ] API base URL configured outside components.
- [ ] Email challenge integrated.
- [ ] OTP verification integrated.
- [ ] Authentication response handled.
- [ ] Access-token Bearer handling added.
- [ ] Refresh-token single-flight rotation implemented.
- [ ] Both tokens replaced atomically after refresh.
- [ ] Logout integrated.
- [ ] `/auth/me` bootstrap integrated.
- [ ] `/patients/me` integrated.
- [ ] Separate preference `version` and patient `profileVersion` handling integrated.
- [ ] My Circle patient/relationship lists integrated with name-only summaries.
- [ ] Managed-patient creation and demographic validation integrated.
- [ ] Patient detail/PATCH stale-`409` reconciliation integrated.
- [ ] Relationship revocation and concealed-`404` handling integrated.
- [ ] Google Identity Services integrated.
- [ ] Google credential sent to Beeexy as an ID token.
- [ ] `401` handling implemented.
- [ ] `409` profile conflict handling implemented.
- [ ] `422` validation handling implemented.
- [ ] `429` throttle UX handled.
- [ ] Tokens, OTPs, and Google credentials excluded from logs.

## 16. Verification Notes

The Phase 1 health, Phase 2 authentication/preference, and Phase 3 My Circle routes were verified against the endpoint mappings, DTO records, authorization services, migrations, exception handler, OpenAPI declarations, and PostgreSQL integration tests. The complete backend suite passes 475 tests (261 unit and 214 PostgreSQL integration) with no failures or skips. The five dedicated Phase 3.8 acceptance tests cover the complete six-endpoint Bearer matrix, disabled-account precedence, the Account A/Account B/Patient X journey, revocation-versus-PATCH locking, demographic-log privacy, and exact OpenAPI scope/enums; a separate concurrent duplicate-relationship test verifies the database uniqueness boundary.

No discrepancy was found between the current implementation plan, endpoint mappings, OpenAPI declarations, and tested request/response contracts for these routes. Technical attestation version/acceptance recording is complete; final human-readable product/legal wording remains an external product-content dependency and the API does not claim legal verification. Broader future phases are not current frontend APIs. The frontend still needs its deployment-specific API origin, its Google Web Client ID when Google is enabled, and a CORS origin configured to match the frontend host.
