# Private demo access

## Purpose and boundary

Private demo access is a deployment gate in front of the Beeexy API. It answers only whether a visitor may enter the private demo. It is independent of Beeexy accounts, patient profiles, Google and email authentication, bearer tokens, and refresh sessions. Passing this gate does not identify or authorize a Beeexy user; the normal authentication flow still follows it.

No database table or migration is used. The API issues a short-lived, signed, stateless cookie after validating one configured username and the hashes of a password and keyword.

## API

| Method and path | Behavior |
| --- | --- |
| `POST /api/v1/private-access/login` | Validates `username`, `password`, and `keyword`; returns `204` and sets the cookie, or a generic `401`. |
| `GET /api/v1/private-access/session` | Returns only `authenticated` and nullable `expiresAt`. When the gate is disabled it returns authenticated with no expiry. |
| `POST /api/v1/private-access/logout` | Deletes the cookie and returns `204`; repeated calls are safe. |

When enabled, centralized middleware rejects every other `/api` request without a valid cookie before Beeexy bearer authentication or product handlers run. The three routes above, `/health/live`, `/health/ready`, non-API deployment resources, and all CORS `OPTIONS` requests are exempt. Google and email authentication are intentionally not exempt.

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

The API fails at startup whenever the gate is enabled and a required value, hash, key, or policy is invalid. Store the username, both hashes, and signing key as Render secrets. Do not put generated credentials or these values in appsettings, source control, deployment logs, or build arguments. Use a signing key independent from the Beeexy JWT and OTP keys.

To disable the gate, set `PrivateAccess__Enabled=false`. The remaining private-access values are then ignored and ordinary Beeexy API behavior is unchanged.

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

## Vercel and cross-origin cookies

The production cookie is host-only, HTTP-only, `Secure`, `SameSite=None`, explicitly expiring, and scoped to `/`. This is required when a Vercel frontend and Render API are different sites. The configured CORS policy permits credentials only for the exact origins under `Cors__AllowedOrigins`; wildcards remain prohibited.

Frontend requests to the login, session, logout, and all subsequent Beeexy API routes must use `credentials: "include"` (or the client-library equivalent). Do not read or copy the cookie into JavaScript, and do not store a private-access value in `localStorage`. If the frontend and API are later placed behind the same site, reassess whether a stricter SameSite policy can be used.

## Security limitations

This is a temporary shared-secret deployment barrier, not per-user authentication or authorization. Anyone who learns the shared combination can enter until it is rotated, audit events cannot attribute access to a person, and stateless sessions remain valid until expiry after a credential rotation unless the session signing key is also rotated. Use a short session lifetime, distribute the combination narrowly, rotate it after exposure, keep the Render service at one instance while relying on the in-process limiter, and retain normal Beeexy authentication and authorization behind the gate.
