# Frontend integration: database-backed Private Tester Access

## Overview

Private Access is the deployment gate for the private Beeexy experience. It has two
server-selected authentication modes:

- **Legacy** authenticates one configured Private Access credential, then creates a
  normal session for the single configured Demo Guest.
- **Database** authenticates an individual tester credential from the database. Each
  credential is linked to exactly one normal Beeexy `Account`, which has exactly one
  `PatientProfile` and one `UserPreference`.

In Database mode, a successful Private Access login returns ordinary Beeexy access
and refresh tokens for that tester's own Account/Profile. Pre-Triage, Clinical
History, FHIR, and other patient-scoped features therefore continue to use the
existing authenticated API and patient identity. The frontend must not add a
demo-tester flag, special patient selector, or feature-specific tester branch.

The backend selects the mode through server configuration. The frontend does not
receive the mode explicitly; it determines the successful flow from the login HTTP
status.

## Authentication modes and rollout compatibility

| Server mode | Successful `POST /api/v1/private-access/login` | Next frontend action |
| --- | --- | --- |
| `Legacy` | `204 No Content` and an HTTP-only Private Access cookie | Call the Legacy `POST /api/v1/private-access/guest-session` endpoint. |
| `Database` | `200 OK`, an HTTP-only Private Access cookie, and `AuthenticationTokenResponse` JSON | Hydrate ordinary Beeexy authentication state from the response. Do **not** call `guest-session`. |

During rollout, production may use either mode. A compatibility frontend must support
both success statuses. The backend configuration key is
`PrivateAccess__AuthenticationMode` (`Legacy` or `Database`); it is server-only and
must not be exposed to the browser.

## Private Access login

### Request

```http
POST /api/v1/private-access/login
Content-Type: application/json
```

No `Authorization` header is required or expected for this endpoint. Browser requests
must use `credentials: "include"` so the HTTP-only Private Access cookie returned by
the backend is retained and sent on later API requests. `Accept: application/json` is
recommended when the client expects a Database-mode JSON response.

```json
{
  "username": "tester-001",
  "password": "<one-time-issued-password>",
  "keyword": "<one-time-issued-keyword>"
}
```

The three properties are required non-empty strings. Maximum lengths enforced by the
endpoint are 128 characters for `username` and 512 characters each for `password` and
`keyword`.

The response has `Cache-Control: no-store` on successful enabled Private Access login.

### Database-mode success: `200 OK`

The response body is the same normal authentication DTO returned by other Beeexy
authentication flows:

```json
{
  "accessToken": "<JWT access token>",
  "refreshToken": "<opaque refresh token>",
  "accessTokenExpiresAt": "2026-08-28T18:30:00+00:00",
  "refreshTokenExpiresAt": "2026-09-27T18:15:00+00:00",
  "account": {
    "accountId": "11111111-1111-1111-1111-111111111111",
    "profileId": "22222222-2222-2222-2222-222222222222",
    "beeexyId": "BXY-22222222222222222222222222222222"
  }
}
```

- `accountId` is the tester's normal Beeexy Account identifier.
- `profileId` is that Account's owned primary PatientProfile identifier.
- `beeexyId` is that PatientProfile's Beeexy identifier.

The backend also sets the `beeexy-private-access` HTTP-only cookie. Browser code must
not attempt to read it.

### Legacy-mode success: `204 No Content`

The body is empty. The backend sets the `beeexy-private-access` HTTP-only cookie but
does **not** issue normal Beeexy access or refresh tokens at this step. Proceed to the
Legacy guest-session flow below.

> `204` is also returned when Private Access is disabled server-side. In that disabled
> deployment state the gate does not protect the API, so the client must not interpret
> a `204` alone as proof that a credential was authenticated. Private demo production
> deployments should have Private Access enabled.

## Legacy guest-session flow

After a **Legacy-mode** `204` login, send the following request with
`credentials: "include"`:

```http
POST /api/v1/private-access/guest-session
```

This endpoint accepts **no** request body, query parameters, or transfer-encoding.
It requires the valid Private Access cookie and returns `200 OK` with the exact
`AuthenticationTokenResponse` JSON shown above. Hydrate existing auth state from that
response, then continue the ordinary authenticated application flow.

Do not call this endpoint:

- after a Database-mode `200` login;
- to select or impersonate an Account/Profile (it has no identity selector); or
- after Private Access has been switched to Database mode. In Database mode the Demo
  Guest definition is disabled, so a valid gated request to this endpoint returns
  `503 Demo Guest unavailable`.

## Database single-step flow

For a `200` response from `/api/v1/private-access/login`:

1. Parse the `AuthenticationTokenResponse`.
2. Hydrate the existing Beeexy auth store with `accessToken`, `refreshToken`, expiry
   timestamps, and the `account` reference.
3. Use the existing authenticated API client to load `/api/v1/auth/me` and
   `/api/v1/patients/me` as the application normally does.
4. Do **not** call `/api/v1/private-access/guest-session`.

The returned Account/Profile identity is the tester's own normal identity. Existing
patient-scoped endpoints and authorization rules determine accessible data.

## Auth and session lifecycle

### Private Access cookie and gate

The `beeexy-private-access` cookie is HTTP-only, has path `/`, and is marked essential.
In production it is `Secure` with `SameSite=None`; outside production it is
`SameSite=Lax`. Cross-origin browser calls must keep `credentials: "include"`.

Except for Private Access `login`, `session`, and `logout` (and CORS preflight), the
Private Access gate checks the cookie before `/api` requests. A missing, invalid,
expired, disabled, or revoked Database-mode private session causes a gated request to
return `401 Private access required`; an invalid present cookie is cleared.

`GET /api/v1/private-access/session` is available without a valid gate cookie and
returns:

```json
{
  "authenticated": true,
  "expiresAt": "2026-08-28T18:30:00+00:00"
}
```

or:

```json
{
  "authenticated": false,
  "expiresAt": null
}
```

It has `Cache-Control: no-store` and clears an invalid present cookie. When Private
Access is disabled server-side it returns `{ "authenticated": true, "expiresAt": null }`.

### Bearer and refresh tokens

Database-mode login does not change normal Bearer-token or refresh-token contracts:

- Send `Authorization: Bearer <accessToken>` to existing authenticated endpoints.
- Continue using `POST /api/v1/auth/refresh` with its existing JSON request:

  ```json
  { "refreshToken": "<current refresh token>" }
  ```

  It returns the same `AuthenticationTokenResponse` and rotates the refresh session.
- Continue using `POST /api/v1/auth/logout` for the existing normal authenticated
  logout behavior.

The Private Access gate still requires the private cookie for these `/api` calls, so
keep `credentials: "include"` on the normal API client as well.

### Tester logout, deactivation, and revocation

For the Private Tester Access screen's logout action, call:

```http
POST /api/v1/private-access/logout
```

with `credentials: "include"`. It returns `204 No Content`, clears the private cookie,
and, in Database mode, revokes the linked normal refresh-session family. Clear the
frontend's normal auth state after this response.

Tester deactivation/revocation is an administrative backend operation. It disables or
revokes the credential and Account, revokes associated private sessions and active
refresh sessions, and is observed by the database-backed gate on the next API request.
The frontend should clear local auth state and return to the Private Access screen on a
gate `401`; it must not attempt to infer whether the reason was deactivation,
revocation, expiry, or a malformed cookie.

## Error contract

Problem Details responses use JSON fields such as `status`, `title`, `detail`,
`instance`, and `correlationId`. The `correlationId` is an extension added by the API.

| Situation | Status | Implemented response semantics | Frontend action |
| --- | --- | --- | --- |
| Unknown username, incorrect password, incorrect keyword, disabled/revoked credential, disabled Account, missing/invalid identity relationship | `401` | `title: "Private access denied."`; `detail: "The private access credentials are invalid."` | Show one generic credential error. |
| Missing/empty/overlong login properties | `400` | `title: "Invalid request."`; `detail: "The private access request is invalid."` | Show a validation/form error. |
| Malformed login JSON | `400` | Framework bad-request response; do not rely on a parser-specific detail string. | Treat as a form/request error. |
| Per-IP login limit reached | `429` | `title: "Too many requests."`; `detail: "Please try again later."`; includes `Retry-After` response header (seconds). | Disable/retry only after the header duration. |
| Missing/invalid/expired/revoked private cookie on a protected API request | `401` | `title: "Private access required."`; `detail: "A valid private demo access session is required."` | Clear auth state and show the Private Access screen. |
| Invalid normal refresh token/family | `401` | `title: "Authentication failed."`; `detail: "The authentication session is invalid."` | Preserve existing normal refresh-failure handling; return to Private Access if the gate also rejects later calls. |
| `guest-session` without valid private cookie | `401` | Rejected by the Private Access gate. | Only call after Legacy `204` with credentials included. |
| `guest-session` with body/query/transfer encoding | `400` | Request is rejected because the endpoint accepts none. | Do not send a body or query. |
| `guest-session` unavailable, including Database mode | `503` | `title: "Demo Guest unavailable."`; `detail: "The Demo Guest authentication session is not available."` | Do not retry as a Database login; use the `200` single-step flow. |

Credential failures intentionally use the same generic `401` response for unknown,
incorrect, disabled, revoked, and unavailable identities. Do not create UI paths that
try to distinguish them; this avoids account enumeration.

## Frontend state machine

```text
POST /api/v1/private-access/login
  |
  +-- 204 No Content
  |     Legacy flow
  |       -> POST /api/v1/private-access/guest-session (no body/query; credentials: include)
  |       -> 200 AuthenticationTokenResponse
  |       -> hydrate normal Beeexy auth state
  |
  +-- 200 OK + AuthenticationTokenResponse
  |     Database flow
  |       -> hydrate normal Beeexy auth state directly
  |       -> do NOT call /api/v1/private-access/guest-session
  |
  +-- 400
  |     -> show malformed/invalid request error
  |
  +-- 401
  |     -> show one generic Private Access credential error
  |
  +-- 429
        -> show rate-limit error and honor Retry-After
```

After either successful path, use the established application initialization flow,
including `/api/v1/auth/me` and `/api/v1/patients/me` with the normal Bearer token and
private cookie.

## TypeScript contracts

```ts
export interface PrivateAccessLoginRequest {
  username: string;
  password: string;
  keyword: string;
}

export interface AccountSummary {
  accountId: string;
  profileId: string;
  beeexyId: string;
}

export interface AuthenticationTokenResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
  account: AccountSummary;
}

export interface PrivateAccessSessionStatusResponse {
  authenticated: boolean;
  expiresAt: string | null;
}

export interface ProblemDetailsResponse {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  correlationId?: string;
}

export type PrivateAccessLoginOutcome =
  | { kind: "legacy" }
  | { kind: "database"; authentication: AuthenticationTokenResponse };
```

The login parser should branch on HTTP status, not body presence: `204` maps to
`{ kind: "legacy" }`; `200` must parse `AuthenticationTokenResponse`; non-success
responses should parse `ProblemDetailsResponse` when the response content type permits.

## Security requirements for the frontend

- Submit password and keyword only for the login attempt. Do not retain them in state,
  logs, analytics, URLs, local storage, session storage, or telemetry.
- Do not persist tester credentials. The provisioning CSV is server-side administrative
  material, not a frontend asset.
- Do not attempt to read, construct, or store the HTTP-only Private Access cookie.
- Keep existing secure handling for normal access and refresh tokens.
- Do not expose Private Access server configuration or server-only secrets.
- Preserve `credentials: "include"` on login, session checks, logout, the Legacy
  guest-session request, and normal cross-origin API requests while Private Access is
  enabled.

## Existing frontend behavior that remains unchanged

- The normal authenticated API client continues to send the Bearer access token.
- Normal refresh rotation and normal `/api/v1/auth/logout` behavior remain unchanged.
- Existing account/profile loading from `/api/v1/auth/me` and `/api/v1/patients/me`
  remains unchanged.
- Pre-Triage, Clinical History, FHIR, and all other patient-scoped modules continue to
  use the normal Account/Profile identity and existing authorization behavior.

## Frontend migration checklist

1. Add the three-field Private Access login request and always send it with
   `credentials: "include"`.
2. Branch on successful login status: `204` for Legacy and `200` for Database.
3. Retain the bodyless Legacy `guest-session` call only on `204`.
4. On `200`, hydrate the existing normal auth store directly; do not make a
   `guest-session` call.
5. Ensure the normal API client continues to send both the Bearer token and browser
   credentials while Private Access is enabled.
6. Handle `400`, generic `401`, and `429` without credential-specific messaging; honor
   `Retry-After` for `429`.
7. On a gate `401`, clear local auth state and return to the Private Access screen.
8. Deploy this dual-mode frontend before production changes
   `PrivateAccess__AuthenticationMode` to `Database`.
9. Remove the Legacy `204`/`guest-session` branch only after production has been
   verified in Database mode and the backend's Legacy flow is retired.

## Examples

### Login request

```ts
const response = await fetch("/api/v1/private-access/login", {
  method: "POST",
  credentials: "include",
  headers: {
    "Content-Type": "application/json",
    "Accept": "application/json"
  },
  body: JSON.stringify({
    username: "external-2026-tester-001-example",
    password: "<issued-password>",
    keyword: "<issued-keyword>"
  })
});
```

### Legacy continuation

```ts
if (response.status === 204) {
  const guestSession = await fetch("/api/v1/private-access/guest-session", {
    method: "POST",
    credentials: "include"
  });
  // Require guestSession.status === 200, then parse AuthenticationTokenResponse.
}
```

### Database continuation

```ts
if (response.status === 200) {
  const authentication: AuthenticationTokenResponse = await response.json();
  // Hydrate the existing Beeexy auth store. No guest-session request follows.
}
```

### Generic credential failure

```json
{
  "title": "Private access denied.",
  "status": 401,
  "detail": "The private access credentials are invalid.",
  "instance": "/api/v1/private-access/login",
  "correlationId": "<server-generated-correlation-id>"
}
```

## Source references

This document is derived from the current backend implementation and OpenAPI endpoint
definitions, specifically:

- `src/Beeexy.Api/PrivateAccess/PrivateAccessEndpointExtensions.cs` — login/session/
  logout/guest-session handlers; `PrivateAccessLoginRequest`; and
  `PrivateAccessSessionStatusResponse`.
- `src/Beeexy.Api/PrivateAccess/PrivateAccessGateMiddleware.cs` — protected-path gate,
  exemptions, invalid-cookie clearing, and gate `401` Problem Details.
- `src/Beeexy.Api/Identity/AuthenticationEndpointExtensions.cs` —
  `AuthenticationTokenResponse`, `AccountSummaryResponse`, refresh, normal logout,
  `/auth/me`, and token response serialization.
- `src/Beeexy.Application/Identity/AuthenticatePrivateAccess.cs` — Database-mode
  credential validation, normal token issuance, and identity invariants.
- `src/Beeexy.Application/Identity/ResolvePrivateAccessSession.cs` and
  `src/Beeexy.Application/Identity/LogoutPrivateAccessSession.cs` — private-session
  resolution, expiry, logout, and linked refresh-family revocation.
- `src/Beeexy.Application/Identity/RotateRefreshSession.cs` and
  `src/Beeexy.Application/Identity/LogoutSession.cs` — unchanged normal refresh and
  logout behavior.
- `src/Beeexy.Api/Errors/ApiExceptionHandler.cs` — rate-limit, Demo Guest, and normal
  session Problem Details mapping.
- `src/Beeexy.Api/Configuration/StartupConfiguration.cs` and
  `src/Beeexy.Api/PrivateAccess/PrivateAccessSettings.cs` — mode selection and cookie
  configuration.
- `src/Beeexy.Api/Patients/PatientEndpointExtensions.cs` — `/api/v1/patients/me`.
- `tests/Beeexy.Tests.Integration/Api/OpenApiAndCorsTests.cs`,
  `PrivateAccessEndpointTests.cs`, `DatabasePrivateAccessEndpointTests.cs`, and
  `DemoGuestSessionEndpointTests.cs` — OpenAPI response declarations and exercised
  mode, cookie, isolation, logout, and error behavior.

The existing `docs/frontend-api-private-access.md` is consistent with the core
Database-mode flow. This document expands it with the Legacy contract, errors, state
machine, and rollout requirements; no implementation conflict was found.
