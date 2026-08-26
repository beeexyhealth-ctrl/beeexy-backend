# Private demo access

## Purpose and boundary

Private demo access is a deployment gate in front of the Beeexy API. It answers only whether a visitor may enter the private demo. The optional Demo Guest integration then resolves exactly one configured, persistent Beeexy `Account` and complete owned primary `PatientProfile`, and issues that account a normal Beeexy access/refresh session. These remain separate boundaries: the private cookie admits a visitor to the deployment; the normal Beeexy session supplies identity and authorization.

The private-demo flow is therefore:

```text
Private Access login -> private-access cookie -> Demo Guest session -> Beeexy
```

Google and email authentication remain available behind the gate. Demo Guest mode is additive and does not replace or modify them.

No database table or migration is used. The API issues a short-lived, signed, stateless cookie after validating one configured username and the hashes of a password and keyword.

## API

| Method and path | Behavior |
| --- | --- |
| `POST /api/v1/private-access/login` | Validates `username`, `password`, and `keyword`; returns `204` and sets the cookie, or a generic `401`. |
| `GET /api/v1/private-access/session` | Returns only `authenticated` and nullable `expiresAt`. When the gate is disabled it returns authenticated with no expiry. |
| `POST /api/v1/private-access/logout` | Deletes the cookie and returns `204`; repeated calls are safe. |
| `POST /api/v1/private-access/guest-session` | Requires a valid private-access cookie, accepts no body or query, resolves only the configured Demo Guest, and returns the standard Beeexy authentication-token response. |

When enabled, centralized middleware rejects every other `/api` request without a valid cookie before Beeexy bearer authentication or product handlers run. Login, private-session status, private logout, `/health/live`, `/health/ready`, non-API deployment resources, and all CORS `OPTIONS` requests are exempt. `guest-session` is intentionally not exempt and cannot be reached without the valid private cookie. Google and email authentication are also intentionally not exempt.

`guest-session` returns `200` with the same `accessToken`, `refreshToken`, expiry fields, and account summary used by successful Google/email authentication. It returns the private gate's `401` Problem Details when the cookie is missing or invalid, `400` for any request body or query, and safe `503` Problem Details when Demo Guest mode is disabled or the configured account/profile is absent, inactive, incomplete, or incompatible. It never accepts an email, account ID, patient ID, Beeexy ID, or role from the caller.

Invalid username, password, and keyword attempts have the same response. The focused login limiter is keyed by requester IP, stores no submitted credentials, and uses `PrivateAccess__LoginPermitLimit` attempts per `PrivateAccess__LoginRateLimitWindowMinutes`. It is in-process, so a multi-instance deployment needs a shared limiter to enforce a deployment-wide limit.

Audit logs record successful access, generic failure categories, logout, gate rejection, and throttling. They do not record submitted fields, hashes, signing keys, or cookies.

## Required Render configuration

Set these environment variable names in Render. Values shown here are descriptions, not example secrets.

| Name | Requirement |
| --- | --- |
| `PrivateAccess__Enabled` | `true` to enable the gate; `false` disables it completely. |
| `PrivateAccess__Username` | Chosen shared demo username. |
| `PrivateAccess__PasswordHash` | PBKDF2 hash emitted by the setup command. |
| `PrivateAccess__KeywordHash` | PBKDF2 hash emitted by the setup command. |
| `PrivateAccess__SessionSigningKey` | Base64-encoded random key of at least 32 bytes; the setup command emits one. |
| `PrivateAccess__SessionLifetimeMinutes` | Positive lifetime, no more than 1440 minutes. The checked-in default is 60. |
| `PrivateAccess__LoginPermitLimit` | Positive per-IP permit count. The checked-in default is 5. |
| `PrivateAccess__LoginRateLimitWindowMinutes` | Positive fixed-window length. The checked-in default is 15. |
| `PrivateAccess__DemoGuest__Enabled` | `true` enables the single Demo Guest; requires Private Access itself to be enabled. |
| `PrivateAccess__DemoGuest__Email` | Dedicated internal normalized-email identity for the Demo Account; this is not the Private Access username. |
| `PrivateAccess__DemoGuest__FirstName` | Required approved primary-profile first name, maximum 100 characters. |
| `PrivateAccess__DemoGuest__LastName` | Required approved primary-profile last name, maximum 100 characters. |
| `PrivateAccess__DemoGuest__DateOfBirth` | Required non-future ISO date in `YYYY-MM-DD` format. |
| `PrivateAccess__DemoGuest__SexAssignedAtBirth` | Required existing profile value: exactly `Male` or `Female`. |
| `PrivateAccess__DemoGuest__State` | Required valid two-letter U.S. state code. |
| `PrivateAccess__DemoGuest__Timezone` | Required recognized IANA timezone identifier. |

The API fails at startup whenever the gate is enabled and a required value, hash, key, or policy is invalid. Store the username, both hashes, and signing key as Render secrets. Do not put generated credentials or these values in appsettings, source control, deployment logs, or build arguments. Use a signing key independent from the Beeexy JWT and OTP keys.

When Demo Guest is enabled, incomplete or invalid profile configuration fails startup without echoing configured values. Demo Guest cannot be enabled while Private Access is disabled. To disable only automatic Demo Guest sessions while retaining the gate, set `PrivateAccess__DemoGuest__Enabled=false`. To disable the whole gate, first disable Demo Guest and then set `PrivateAccess__Enabled=false`.

## Generate and hash credentials locally

The generator is a local administrative mode, not an HTTP endpoint. From a trusted workstation, create a batch of brand-themed suggestions:

```bash
dotnet run --project src/Beeexy.Api -- private-access generate 5
```

Choose a set, then run the interactive hashing command:

```bash
dotnet run --project src/Beeexy.Api -- private-access hash
```

When attached to a terminal, the password and keyword are read without echo and are not command-line arguments, which keeps them out of shell history. The command prints configuration-ready password and keyword hashes plus a newly generated session signing key. Copy those values directly into Render secrets, set the separately chosen username, and clear the terminal output when appropriate. The command never reads or rotates current deployment configuration.

## Provision the Demo Guest

Provisioning is an explicit CLI operation, not startup seeding and not an HTTP endpoint. Apply the existing database migrations first, set the complete production configuration and direct database connection string in the trusted operator environment, then run:

```bash
ASPNETCORE_ENVIRONMENT=Production \
dotnet run --project src/Beeexy.Api --configuration Release -- \
  private-access provision-demo-guest
```

The command uses the existing normalized-email PostgreSQL advisory lock and identity transaction. On first execution it creates one normal active `Account`, one owned primary `PatientProfile`, one `UserPreference`, and a normal Beeexy ID, then fills the configured approved demographics and timezone atomically. It creates no OTP challenge and no Google `ExternalIdentity`.

Later executions acquire the same lock and verify exact account/profile/preference compatibility. They do not create duplicates or overwrite an existing account. If the configured email already belongs to an inactive, incomplete, or demographically different identity, provisioning fails safely and leaves it unchanged. The CLI prints only whether it created or verified the Demo Guest; it does not print tokens or configured identity values.

Provisioning is persistent. It must be run once for each production database after migrations and before the frontend relies on `guest-session`.

## Vercel and cross-origin cookies

The production cookie is host-only, HTTP-only, `Secure`, `SameSite=None`, explicitly expiring, and scoped to `/`. This is required when a Vercel frontend and Render API are different sites. The configured CORS policy permits credentials only for the exact origins under `Cors__AllowedOrigins`; wildcards remain prohibited.

Frontend requests to login, private-session status, guest-session, private logout, and all subsequent Beeexy API routes must use `credentials: "include"` (or the client-library equivalent). After Private Access login succeeds, the private-demo frontend calls `guest-session`, hydrates its existing Beeexy token/session state from the normal response, loads `/api/v1/auth/me` and `/api/v1/patients/me`, and enters the application without Google/email login or onboarding. If the private cookie is still valid but the normal Beeexy access/refresh session is absent or expired, the frontend may call `guest-session` again without requesting the shared credentials again.

Do not read or copy the private cookie into JavaScript, and do not store a private-access value in `localStorage`. If the frontend and API are later placed behind the same site, reassess whether a stricter SameSite policy can be used.

## Session, data, and logout semantics

Every successful `guest-session` request creates a new normal refresh-session family. Multiple browsers and repeated calls can coexist; no bearer token is shared globally. Refresh rotation, reuse detection, expiry, account-active checks, claims, issuer/audience, and bearer middleware are the standard Beeexy implementations.

All visitors represent the same primary patient. Their Pre-Triage episodes, Clinical History, amendments, and FHIR exports use the normal patient authorization/storage paths and may be visible to later demo visitors. Data persists across private and Beeexy session expiry, logout, application restarts, and deployments according to the normal database/artifact-storage behavior. No automatic reset or cleanup is performed.

Beeexy logout and Private Access logout are independent. To completely leave the private demo, the frontend should first call `POST /api/v1/auth/logout` with the current bearer access token to revoke that refresh-session family, clear its normal tokens/auth state, and then call `POST /api/v1/private-access/logout` with credentials included to clear the private cookie. Private logout alone does not revoke a Beeexy refresh session; Beeexy logout alone leaves the private cookie valid and permits a new Demo Guest session without re-entering the shared combination.

## Security limitations

This is a temporary shared-secret deployment barrier and shared patient identity, not per-visitor isolation. Anyone who learns the shared combination can enter until it is rotated, audit events cannot attribute shared Demo Guest activity to a person, and every visitor can see or extend data authorized to the shared primary patient. Stateless private sessions remain valid until expiry after credential rotation unless the private session signing key is also rotated.

Use a dedicated internal email that is not an operator's real account, a short private-session lifetime, narrow credential distribution, and rotation after exposure. Keep the Render service at one instance while relying on the in-process private-login limiter. Retain all normal Beeexy authentication and patient authorization behind the gate; the Demo Guest receives no doctor, clinic, scheduler, managed-patient, or administrative privilege.
