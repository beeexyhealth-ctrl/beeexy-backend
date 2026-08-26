# Beeexy frontend integration: Private Access and Demo Guest

This is the complete frontend contract for the implemented Beeexy private-demo flow. It is intended to be copied directly into the frontend repository.

## Architecture and required flow

Private Access and Beeexy identity are separate layers:

```text
Visitor
  -> Private Access Gate (shared username + password + keyword)
  -> private-access HTTP-only cookie
  -> Demo Guest session (one server-configured real Beeexy account)
  -> normal Beeexy bearer/refresh session
  -> complete primary PatientProfile
  -> Beeexy
```

For the private demo, after Private Access succeeds, **do not navigate to normal Google/email Login**. Instead:

```text
POST /api/v1/private-access/login
  -> 204 + private cookie
POST /api/v1/private-access/guest-session
  -> normal Beeexy authentication response
hydrate the existing Beeexy auth state
GET /api/v1/auth/me
GET /api/v1/patients/me
enter Beeexy directly
```

The Demo Guest is a normal, persistent Beeexy `Account` with a complete owned primary `PatientProfile`. The backend does not create a fake guest token or bypass patient authorization. Google/email authentication remains available behind the private gate, although the private-demo UI need not expose it.

All fetches to the API—including Private Access, Demo Guest, authentication, and product calls—must use:

```ts
credentials: "include"
```

The API permits credentials only from exactly configured CORS origins. CORS preflight `OPTIONS` requests are public. An unconfigured preview/custom origin will be blocked by the browser and must be added to backend deployment configuration.

## Shared contracts

```ts
export interface BeeexyProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  correlationId?: string;
}

export interface PrivateAccessSessionStatus {
  authenticated: boolean;
  expiresAt: string | null;
}

export interface AuthenticationTokenResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
  account: {
    accountId: string;
    profileId: string;
    beeexyId: string;
  };
}
```

Errors are `application/problem+json`. `type` is a framework HTTP reference, not an application error code. Private Access/Demo Guest errors do not include `errorCode`. Responses include `X-Correlation-ID`; retain it for diagnostics without logging submitted credentials or tokens.

## Private Access endpoints

### Login

```http
POST /api/v1/private-access/login
Content-Type: application/json
```

No private cookie or Beeexy bearer token is required.

```json
{
  "username": "...",
  "password": "...",
  "keyword": "..."
}
```

All fields are exact, case-sensitive strings. Do not trim or normalize them. `username` must be nonblank and at most 128 UTF-16 code units; `password` and `keyword` must each be nonblank and at most 512.

- `204 No Content`: success; sets the private cookie and returns `Cache-Control: no-store` when the gate is enabled. Do not parse a body.
- `400 Bad Request`: invalid fields produce title `Invalid request.` and detail `The private access request is invalid.` Malformed/missing JSON produces title `The request is malformed.`
- `401 Unauthorized`: title `Private access denied.` and detail `The private access credentials are invalid.` Every wrong field receives the same generic response.
- `429 Too Many Requests`: title `Too many requests.`, detail `Please try again later.`, and `Retry-After: <seconds>`. Every enabled login request consumes the per-IP fixed-window allowance, including malformed, successful, and failed attempts.

The successful production cookie is named `beeexy-private-access` and is host-only, `HttpOnly`, `Secure`, `SameSite=None`, explicitly expiring, and scoped to `/`. JavaScript cannot and must not read or construct it.

### Private session status

```http
GET /api/v1/private-access/session
```

No body or authentication is required. It always returns `200 OK`, `Cache-Control: no-store`, and:

```json
{
  "authenticated": true,
  "expiresAt": "2026-08-26T18:30:00+00:00"
}
```

or:

```json
{
  "authenticated": false,
  "expiresAt": null
}
```

An invalid/expired cookie is cleared and reported as `authenticated: false`; this endpoint does not return `401` for that condition. When the entire private gate is disabled it returns `authenticated: true, expiresAt: null`.

### Private logout

```http
POST /api/v1/private-access/logout
```

No body or authentication is required. It idempotently expires the private cookie and returns `204 No Content` with `Cache-Control: no-store`.

## Demo Guest session endpoint

```http
POST /api/v1/private-access/guest-session
```

Requirements:

- A valid existing `beeexy-private-access` cookie is required.
- No Beeexy bearer token is required.
- The request accepts **no body and no query parameters**.
- The caller cannot select an email, account ID, patient ID, Beeexy ID, or role. Any body/query—including such fields—is rejected.
- The server always chooses its single configured Demo Guest.

Send a bodyless request:

```ts
const response = await fetch(
  `${API_BASE_URL}/api/v1/private-access/guest-session`,
  {
    method: "POST",
    credentials: "include",
    headers: { Accept: "application/json, application/problem+json" },
  },
);
```

Do not add `Content-Type` when there is no body.

### `200 OK`

Returns the exact normal Beeexy authentication DTO:

```json
{
  "accessToken": "...",
  "refreshToken": "...",
  "accessTokenExpiresAt": "2026-08-26T18:15:00+00:00",
  "refreshTokenExpiresAt": "2026-09-25T18:00:00+00:00",
  "account": {
    "accountId": "00000000-0000-0000-0000-000000000000",
    "profileId": "00000000-0000-0000-0000-000000000000",
    "beeexyId": "BXY-..."
  }
}
```

The values above are shape placeholders, not real credentials or IDs. The response includes `Cache-Control: no-store`. Feed this DTO into the frontend's existing Beeexy authentication/session state exactly as a successful Google/email response. Use the access token as the normal `Authorization: Bearer ...` credential and use the existing refresh rotation flow with the returned refresh token.

Every successful call creates a distinct normal refresh-session family. Repeated calls and multiple browsers can coexist; tokens are not shared globally.

### `400 Bad Request`

A request body, transfer-encoded body, or query parameter returns safe Problem Details:

```json
{
  "title": "The request is malformed.",
  "status": 400,
  "instance": "/api/v1/private-access/guest-session",
  "correlationId": "..."
}
```

### `401 Unauthorized`

A missing, expired, malformed, or tampered private cookie is rejected by the global gate before guest-session issuance:

```json
{
  "title": "Private access required.",
  "status": 401,
  "detail": "A valid private demo access session is required.",
  "instance": "/api/v1/private-access/guest-session",
  "correlationId": "..."
}
```

The response includes `Cache-Control: no-store`; an invalid supplied cookie is also expired. Return the visitor to the three-field Private Access screen.

### `503 Service Unavailable`

If Demo Guest mode is disabled or its persistent Account/Profile is missing, inactive, incomplete, or incompatible:

```json
{
  "title": "Demo Guest unavailable.",
  "status": 503,
  "detail": "The Demo Guest authentication session is not available.",
  "instance": "/api/v1/private-access/guest-session",
  "correlationId": "..."
}
```

Show a neutral “demo temporarily unavailable” state. Do not send the user to Google/email login automatically and do not retry in a tight loop; this condition needs backend/operator correction.

An unexpected failure may return the API's safe `500` Problem Details.

## Bootstrap and recovery state machine

Use the backend—not persisted UI flags—as the authority:

```text
Application starts
  -> GET private-access/session
     -> authenticated=false: show Private Access form
     -> authenticated=true:
          -> valid normal Beeexy auth state: load account/profile and enter app
          -> missing/expired normal Beeexy auth state:
               POST private-access/guest-session
               hydrate normal auth state
               load /api/v1/auth/me and /api/v1/patients/me
               enter app
```

Thus a visitor with a valid Private Access cookie but an absent/expired normal Beeexy session does **not** need to re-enter username/password/keyword. Request a new Demo Guest session.

After initial login:

```text
login 204
  -> POST guest-session
     -> 200: hydrate existing auth store, load account/profile, enter Beeexy
     -> private-gate 401: return to Private Access form
     -> 503: show demo unavailable
```

Avoid interceptor loops: exclude all four `/api/v1/private-access/*` operations from any automatic private-gate retry. For other API calls, distinguish the gate-specific `401` by the exact title `Private access required.` Normal Beeexy authentication also uses `401` and should continue through the existing refresh/login handling.

The provisioned profile already contains first name, last name, date of birth, sex assigned at birth, state, and timezone. Existing account/profile responses should naturally treat it as complete. Do not add a frontend `skipOnboarding` override.

## Complete logout

Private Access and Beeexy logout are independent.

To fully exit the private demo:

1. Call `POST /api/v1/auth/logout` with the current access token to revoke that normal refresh-session family.
2. Clear the frontend's existing Beeexy access/refresh/auth state.
3. Call bodyless `POST /api/v1/private-access/logout` with `credentials: "include"` to clear the private cookie.
4. Return to the Private Access screen.

Private logout alone does not revoke a normal Beeexy refresh session. Beeexy logout alone leaves the private cookie active, so the frontend can obtain a new Demo Guest session without asking for the shared credentials again. If the access token has expired, use the existing refresh flow as appropriate before normal logout; never send a refresh token to a Private Access endpoint.

## Shared data implications

All demo visitors represent the same persistent primary patient. Pre-Triage completions project into the existing Clinical History, amendments and FHIR exports use normal patient authorization, and data is not automatically reset on logout or between visitors. The UI should not imply that this is a private, per-visitor patient workspace.

The Demo Guest has only normal primary-account permissions. It has no demo-specific admin, clinician, clinic, scheduler, doctor, or managed-patient privilege. Another patient's resources remain inaccessible, and `beeexyId` is display/reference data—not an authorization credential.

## Security requirements

- Conceal `password` and `keyword` inputs.
- Never place the shared inputs, private cookie, access token, or refresh token in URLs, logs, analytics, error telemetry, or general persisted UI state.
- Never store the Private Access combination or cookie in `localStorage`, `sessionStorage`, IndexedDB, or a frontend-created cookie.
- Preserve the frontend's existing secure handling for normal Beeexy access/refresh tokens; the guest response does not introduce a new token type.
- Do not identify which shared field was incorrect.
- Respect login `Retry-After` and do not repeatedly retry `401`, `429`, or `503`.
- Do not parse, read, copy, or manufacture `beeexy-private-access`.
- Continue sending `credentials: "include"` on every API call, including calls carrying a bearer token.

## Frontend acceptance checklist

- Fresh load checks Private Access session status from the backend.
- A failed private session check shows the three-field gate.
- Login uses exact camel-case fields and handles bodyless `204`.
- Login success immediately calls bodyless `POST /guest-session`; it does not navigate to normal Login.
- Guest `200` hydrates the existing Beeexy auth store using the normal authentication DTO.
- The app then loads current account and primary profile and bypasses onboarding naturally because profile data is complete.
- Valid private cookie + missing/expired normal auth requests a new guest session without shared credentials.
- Gate-specific and normal-auth `401` responses are distinguished.
- `400`, `429`/`Retry-After`, and safe guest `503` states are handled without leaking detail.
- The shared API client always includes browser credentials.
- Full logout performs normal Beeexy logout/state clearing and then Private Access logout.
- No caller-selected identity fields are sent to `guest-session`.
- The UI does not promise per-visitor data isolation or automatic demo-data reset.

