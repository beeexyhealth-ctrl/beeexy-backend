# Phase 2 production configuration

## Transactional authentication email

Beeexy uses the Resend HTTPS API for production passwordless sign-in email. Resend was selected because one authenticated JSON request is sufficient, credentials do not need to be persisted, and the integration fits the existing `IAuthenticationEmailSender` boundary without a provider SDK or a general-purpose email subsystem.

Production requires these environment variables:

```text
Authentication__EmailSender__Provider=Resend
Authentication__EmailSender__Resend__ApiKey=<secret Resend sending API key>
Authentication__EmailSender__Resend__SenderEmail=<address on a verified sending domain>
Authentication__EmailSender__Resend__SenderDisplayName=Beeexy
```

Keep the API key in the deployment secret store. Do not place it in an appsettings file, image, log, or source-control variable. Use the narrowest Resend sending permission available and rotate the key through the secret store. The configured sender domain must be verified with Resend before deployment.

The production adapter sends only a plain-text sign-in message containing the OTP, its UTC expiration, and an instruction to ignore an unrequested message. It sends no tokens, internal identifiers, or profile data. It does not log requests or provider response bodies. A provider rejection, network failure, or timeout becomes a generic delivery failure; the application then deletes the just-created challenge so an undelivered OTP cannot remain usable.

Development deliberately uses `Authentication__EmailSender__Provider=InMemory`. Integration tests use the same deterministic boundary to capture OTPs internally; no public endpoint exposes them. Production startup rejects this provider.

## Other production secrets

Production also requires the PostgreSQL connection string, a unique high-entropy JWT signing key, a separate high-entropy OTP HMAC key, and explicit CORS origins. A Google client ID is required only when Google authentication is enabled. None of these values should be committed to source control.

## Deployment assumptions

The Phase 2 MVP runs one API instance. OTP request throttling is an in-process, thread-safe fixed-window limiter partitioned by normalized email and connection IP. Before running multiple API replicas, replace it with a shared limiter so limits cannot be bypassed across instances.

The API intentionally does not trust arbitrary `X-Forwarded-For` headers. Production should either preserve the real client address as the connection address or add ASP.NET forwarded-header handling restricted to explicitly known proxies at deployment time. If every request arrives with one proxy address, the IP rate-limit partition will be shared and overly restrictive.

TLS terminates at the API or a trusted production reverse proxy. Non-Development environments redirect HTTP to HTTPS and emit HSTS. Authorization runs after CORS and before endpoint execution; configured CORS origins must be exact HTTP(S) origins and cannot use wildcards.
