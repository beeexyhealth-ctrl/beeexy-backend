# Render deployment configuration

## Service

Create a **Web Service** with **Docker** as the runtime. Use the repository root as the root directory and `./Dockerfile` as the Dockerfile path. Do not set a Docker Command; the image command starts `Beeexy.Api`.

The image sets `ASPNETCORE_ENVIRONMENT=Production` and enables ASP.NET Core forwarded-header handling for Render's TLS-terminating proxy. Render web services are reached only through that proxy, so the application can recognize the original HTTPS scheme before its existing HTTPS-redirection middleware runs.

## Environment variables

Configure these variables in Render. Store secret values as Render secrets and do not put values in the image or repository:

| Name | Purpose |
| --- | --- |
| `ConnectionStrings__BeeexyDatabase` | Npgsql connection string for the Neon PostgreSQL database. A Neon pooled runtime connection string is supported. |
| `Cors__AllowedOrigins__0` | Exact Vercel frontend origin, including `https://` and with no trailing slash. Use `Cors__AllowedOrigins__1`, `__2`, and so on only for additional trusted origins. |
| `Authentication__EmailChallenge__OtpHashingKey` | Independent high-entropy OTP HMAC secret of at least 32 characters. |
| `Authentication__EmailSender__Provider` | Set to the production provider name `Resend`. |
| `Authentication__EmailSender__Resend__ApiKey` | Resend sending API key. |
| `Authentication__EmailSender__Resend__SenderEmail` | Sender address on a Resend-verified domain. |
| `Authentication__EmailSender__Resend__SenderDisplayName` | Sender display name. |
| `Authentication__Tokens__Issuer` | Issuer identifier for this production API deployment. |
| `Authentication__Tokens__Audience` | Intended production client audience. |
| `Authentication__Tokens__SigningKey` | Independent high-entropy JWT signing secret. |

Render supplies `PORT`; do not give it a fixed value. The image requires it and binds the API to `http://0.0.0.0:$PORT`.

These variables are conditional:

| Name | When needed |
| --- | --- |
| `Authentication__Google__Enabled` | Set to `true` only when Google sign-in is enabled. |
| `Authentication__Google__ClientId` | Required when Google sign-in is enabled. |
| `ClinicalAi__Provider` | Required to enable the configured clinical AI provider; the current implemented value is `Nvidia`. |
| `ClinicalAi__ApiKey` | Required when the NVIDIA clinical AI provider is enabled. |
| `ClinicalAi__Model` | Override only when selecting a different deployed model. |
| `ClinicalAi__BaseUrl` | Override only when the provider endpoint differs from the configured default. |
| `ClinicalAi__TimeoutSeconds` | Override only when changing the provider timeout. |

The checked-in policy and lifetime settings have non-secret defaults. Override their corresponding double-underscore environment names only if the production policy is intentionally changed.

## Health check

Set Render's **Health Check Path** to exactly:

```text
/health/ready
```

This endpoint checks PostgreSQL connectivity and returns `503` when PostgreSQL is unavailable. `/health/live` checks only process liveness and should not be used as Render's readiness gate.

## Neon and EF Core migrations

Production startup intentionally does not create or migrate the database. Only Development startup calls `MigrateAsync`; no `EnsureCreated` path exists. Apply the committed migrations before directing production traffic to a new application version.

From a trusted workstation or CI environment with the .NET 8 SDK, export the same required production configuration variables listed above. For the migration run, set `ConnectionStrings__BeeexyDatabase` to Neon's **direct (non-pooled)** connection string, then run:

```bash
dotnet tool restore
ASPNETCORE_ENVIRONMENT=Production dotnet ef database update \
  --project src/Beeexy.Infrastructure \
  --startup-project src/Beeexy.Api \
  --configuration Release
```

After the migration succeeds, keep the Render runtime variable set to the intended Neon runtime connection string. Do not add migration execution to the Docker command or application startup. The runtime image intentionally contains neither the SDK nor `dotnet-ef`.

## CORS

Set `Cors__AllowedOrigins__0` to the final Vercel production origin. Each additional preview or custom-domain origin must be a separate indexed entry. Beeexy rejects wildcards, credentials, paths, queries, fragments, and trailing slashes; it does not use unrestricted CORS.

## FHIR artifact persistence

FHIR exports are currently stored on the local filesystem at:

```text
/app/private-fhir-artifacts
```

Render's container filesystem is ephemeral. If FHIR exports must remain downloadable after a restart or deploy, attach a Render persistent disk with `/app/private-fhir-artifacts` as its exact mount path. A paid service is required for a Render persistent disk. Without it, the API can start, but database export records can outlive their artifact files and downloads will fail after filesystem replacement.

## Remaining blockers

- Apply all EF Core migrations to Neon before production traffic is enabled.
- Configure and verify the Resend sending domain and all required secrets.
- Attach the persistent disk before enabling durable FHIR export usage.
- Production does not run the Development-only demo clinical-definition importer. Provision the intended approved clinical-definition packages through deployment tooling before relying on pre-triage flows.
- The OTP rate limiter is in-process. Keep the service at one instance until a shared limiter is implemented; multiple instances weaken per-client rate-limit enforcement.
