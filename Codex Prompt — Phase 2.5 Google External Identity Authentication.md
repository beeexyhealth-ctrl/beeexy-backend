# Phase 2.5 — Google External Identity Authentication

You are continuing the backend implementation defined in:

`Backend/IMPLEMENTATION_PLAN.md`

Already completed:

- Phase 1 — Technical Foundation
- Phase 2.1 — Identity Domain and Persistence Foundation
- Phase 2.2 — Email Authentication Challenge Request Flow
- Phase 2.3 — Email OTP Verification and Atomic Account Provisioning
- Phase 2.4 — Access Tokens, Rotating Refresh Sessions, Reuse Detection, and Logout

Your task is to implement **only Phase 2.5 — Google External Identity Authentication**.

Do NOT implement the rest of Phase 2.

---

# 1. Read and inspect first

Before modifying any code:

1. Read `Backend/IMPLEMENTATION_PLAN.md`.
2. Read the complete **Phase 2 — Identity, Authentication, and Primary Patient Profile** section.
3. Inspect the complete Phase 2.1–2.4 implementation.
4. Review at minimum:
   - `Account`
   - `ExternalIdentity`
   - `PatientProfile`
   - `UserPreference`
   - `NormalizedEmail`
   - `ProvisionAccountAndPrimaryProfile`
   - `IssueAuthenticationTokens`
   - `RefreshSession`
   - JWT/access-token implementation
   - refresh-token implementation
   - authentication transaction boundaries
   - existing email authentication endpoints
   - API exception handling
   - configuration/startup validation
   - dependency injection
   - EF Core mappings for `ExternalIdentity`
   - current authentication integration tests
5. Run or inspect the existing test suite before changing behavior.

Treat `IMPLEMENTATION_PLAN.md` as the source of truth.

Preserve all existing Phase 2 behavior and architectural boundaries.

If a material issue in Phase 2.1–2.4 blocks secure Google authentication, make only the smallest necessary correction and document it.

Do not perform unrelated refactors.

---

# 2. Objective

Implement the optional Google identity authentication flow required by Phase 2:

`POST /api/v1/auth/google`

The flow must allow a user with a valid Google identity to:

- authenticate using Google
- resolve an existing Beeexy account safely
- provision a new Beeexy account when appropriate
- associate the Google identity with the Beeexy account
- receive the same Beeexy access/refresh token pair used by email authentication

Google must remain an **external identity provider**, not become Beeexy's authorization authority.

Beeexy must continue issuing and controlling its own:

- Account identity
- access tokens
- refresh sessions
- PatientProfile ownership
- authorization

---

# 3. Phase 2 requirement

The implementation plan requires:

- optional Google identity adapter/configuration
- `AuthenticateWithGoogle`
- Google validation through `IExternalIdentityProvider`
- Google enabled only with valid configuration
- invalid identity → `401`
- provider disabled/unavailable → `503`
- Google can be enabled without domain/application changes

Implement exactly that scope.

Do not implement Apple authentication.

---

# 4. Architecture

Maintain the existing dependency direction.

The Application layer must not depend directly on:

- Google SDK implementations
- Google HTTP clients
- Google-specific infrastructure classes

Introduce/use:

`IExternalIdentityProvider`

at the appropriate application boundary.

Infrastructure should provide the Google implementation.

The API endpoint should invoke the application use case rather than contain Google authentication logic.

Conceptually:

```text
POST /api/v1/auth/google
            ↓
AuthenticateWithGoogle
            ↓
IExternalIdentityProvider
            ↓
GoogleExternalIdentityProvider
            ↓
Google validation
            ↓
Beeexy account resolution/provisioning
            ↓
Beeexy RefreshSession
            ↓
Beeexy token pair
```

Do not bypass the existing account/session infrastructure.

---

# 5. Application use case

Implement:

`AuthenticateWithGoogle`

It should orchestrate:

1. verify Google provider availability/configuration
2. validate the submitted Google identity credential
3. obtain trusted identity information from the validated credential
4. resolve an existing `ExternalIdentity` by provider + subject
5. resolve/link the appropriate Beeexy account when safe
6. provision Account + PatientProfile + UserPreference when appropriate
7. persist/link the `ExternalIdentity`
8. issue a Beeexy refresh session
9. issue Beeexy access/refresh tokens
10. return the standard authentication result

Do not duplicate the Phase 2.3 provisioning logic.

Reuse `ProvisionAccountAndPrimaryProfile` or refactor only the minimum necessary shared application boundary.

Do not duplicate Phase 2.4 token/session issuance logic.

Reuse the existing authentication token/session infrastructure.

---

# 6. Google endpoint

Implement:

`POST /api/v1/auth/google`

Authentication:

- none initially

Authorization:

- possession of a valid Google identity credential accepted by the configured provider

Expected success:

`200 OK`

Return the same general token-pair/account-summary contract used by:

`POST /api/v1/auth/email/verify`

Avoid maintaining two incompatible authentication success models.

Conceptually:

```json
{
  "credential": "<google-identity-credential>"
}
```

Use a request field name appropriate for the actual validated Google credential type.

Do not accept arbitrary Google profile data from the client as proof of identity.

---

# 7. Trusted Google identity

The backend must validate the Google credential independently.

Do NOT trust client-provided values such as:

```json
{
  "email": "user@gmail.com",
  "googleUserId": "...",
  "name": "..."
}
```

without cryptographic/provider validation.

The trusted identity must come from successful provider validation.

At minimum, obtain:

- provider identifier
- provider subject (`sub`)
- verified email when available/required
- email verification state where provided by Google

Use the provider subject as the stable external identity identifier.

Do not use Google email as the external provider subject.

---

# 8. Google credential validation

Implement a production-quality Google identity adapter behind:

`IExternalIdentityProvider`

Validate all security-relevant properties required for the chosen Google identity credential mechanism.

For Google ID tokens, this should include appropriate validation of:

- signature
- issuer
- audience/client ID
- expiration
- subject
- required email/email verification semantics

Use a maintained Google-supported or standards-compliant validation mechanism where appropriate.

Do not manually implement JWT cryptography if a reliable supported library already provides secure Google ID-token validation.

Do not accept expired or incorrectly targeted Google credentials.

---

# 9. Provider abstraction

`IExternalIdentityProvider` should expose only application-relevant identity information.

Do not leak Google SDK-specific types into Application or Domain.

A successful validation result should conceptually provide:

- provider
- subject
- normalized/verified email where appropriate

Potential display/profile information should only be included if Phase 2 actually requires it.

Do not expand the domain model with Google-specific fields unnecessarily.

---

# 10. Optional provider behavior

Google authentication is optional.

It must be possible to run Beeexy with Google disabled.

When Google is disabled:

`POST /api/v1/auth/google`

must return:

`503 Service Unavailable`

according to the Phase 2 plan.

Do not fail the entire Beeexy API startup merely because Google is intentionally disabled.

Email OTP authentication must continue functioning normally.

---

# 11. Configuration

Introduce strongly typed Google authentication configuration containing only concrete requirements, such as:

- enabled flag
- Google client ID / accepted audience
- any other values genuinely required for secure validation

Do not commit production Google credentials/secrets.

Use environment/configuration conventions established in Phase 1 and Phase 2.4.

If:

```text
Google.Enabled = false
```

Google-specific credentials may be absent.

If:

```text
Google.Enabled = true
```

required configuration must be validated.

Invalid enabled configuration should fail safely at startup or produce the explicitly established unavailable behavior, depending on existing startup-validation conventions.

Document the behavior.

---

# 12. ExternalIdentity resolution

Phase 2.1 already established:

`ExternalIdentity`

with unique:

`provider + subject`

Use that as the primary external-identity lookup.

Conceptually:

```text
Google credential
      ↓
provider = google
subject = Google sub
      ↓
ExternalIdentity
      ↓
Account
```

If an `ExternalIdentity` already exists:

- resolve its Account
- ensure the Account is active
- do not create another Account
- do not create another PatientProfile
- issue a new independent Beeexy authentication session

---

# 13. New Google identity

If no matching `ExternalIdentity` exists, determine whether the verified Google email corresponds to an existing Beeexy Account.

This is security-sensitive.

Use only a Google email that has been validated and is considered verified according to the provider's trusted response.

Never link based on an unverified client-provided email.

---

# 14. Existing account linking

If:

- no `ExternalIdentity(provider=google, subject=...)` exists
- Google provides a trusted verified email
- an active Beeexy Account already exists with the same normalized email

then link the Google `ExternalIdentity` to that existing Account, provided the Phase 2 identity model allows this safely.

Do NOT:

- create a duplicate Account
- create a duplicate PatientProfile
- create a second UserPreference

This allows a user who previously authenticated through email OTP to later use Google with the same verified email.

The linkage must be atomic and concurrency-safe.

---

# 15. New account provisioning

If:

- Google identity is valid
- Google email is trusted/verified
- no matching ExternalIdentity exists
- no Beeexy Account exists for that normalized email

then use the existing provisioning flow to create:

- Account
- primary PatientProfile
- UserPreference
- Beeexy ID

and associate:

- Google ExternalIdentity

with the new Account.

Reuse the same domain invariants established by Phase 2.3.

Do not create a second account-provisioning implementation specifically for Google.

---

# 16. Atomicity

For a new Google user, the following must be transactionally safe:

```text
Account
+
PatientProfile
+
UserPreference
+
ExternalIdentity
+
RefreshSession
```

A failure must not leave partially linked identity state.

For an existing account receiving a new Google identity, the following should also be safely coordinated:

```text
ExternalIdentity linkage
+
authentication session issuance
```

Use existing transaction infrastructure where possible.

Do not create unnecessary distributed transaction abstractions.

---

# 17. Concurrency

Google authentication must remain correct under concurrency.

Important scenario:

Two simultaneous requests authenticate for the first time with the same valid Google identity.

They must not create:

- two ExternalIdentity records
- two Accounts
- two primary PatientProfiles
- duplicate preferences
- inconsistent external-identity linkage

Use existing database uniqueness:

`provider + subject`

and normalized-email uniqueness as final concurrency authorities.

Reuse the account-provisioning concurrency strategy already implemented in Phase 2.3 where applicable.

Do not rely only on check-then-insert.

Expected concurrency conflicts should be handled intentionally rather than surfacing as unhandled `500` responses.

---

# 18. Cross-method identity convergence

Test this important scenario:

```text
Email OTP login
      ↓
Account A created
      ↓
Later Google login
with same trusted verified email
      ↓
ExternalIdentity → Account A
```

Expected:

```text
1 Account
1 PatientProfile
1 UserPreference
1 Google ExternalIdentity
multiple independent RefreshSessions allowed
```

NOT:

```text
Account A → email
Account B → Google
```

The normalized verified email should allow safe convergence when the Google identity has not already been linked elsewhere.

---

# 19. Existing ExternalIdentity conflict

If the Google provider/subject is already linked to an Account, that association is authoritative.

Do not silently move an existing ExternalIdentity to another Account because of a changed or conflicting email.

If provider identity and email-derived account resolution disagree, fail safely rather than silently relinking identities.

Do not expose internal account-linkage details publicly.

Document the chosen failure behavior.

---

# 20. Disabled accounts

If Google identity resolves to a disabled Beeexy Account:

- do not authenticate
- do not issue access token
- do not issue refresh token
- do not reactivate Account
- do not relink identity

Return a safe authentication failure.

Do not expose that the account is specifically disabled.

---

# 21. Email verification semantics

Google identity linking/provisioning based on email requires trusted email verification.

If the Google identity does not contain an acceptable verified email:

- do not automatically provision/link by email
- fail authentication safely unless the ExternalIdentity is already known and can resolve an existing account by provider/subject alone

This distinction is important:

### Existing known ExternalIdentity

Provider + subject may identify the existing Beeexy Account.

### New ExternalIdentity

Do not establish a new Beeexy account/email linkage from an unverified email.

---

# 22. Session issuance

Successful Google authentication must use the same Phase 2.4 session infrastructure as email authentication.

Create a new refresh-session family.

Issue:

- short-lived Beeexy access token
- opaque Beeexy refresh token

Do not use the Google credential as the Beeexy access token.

Do not return Google access tokens as Beeexy credentials.

Google proves identity; Beeexy controls the application session.

Conceptually:

```text
Google credential
      ↓
Google validation
      ↓
Beeexy Account
      ↓
Beeexy RefreshSession
      ↓
Beeexy JWT + Beeexy refresh token
```

---

# 23. Authentication response

Return the same authentication result contract used by email verification.

Conceptually:

```json
{
  "accessToken": "...",
  "refreshToken": "...",
  "accessTokenExpiresAt": "...",
  "refreshTokenExpiresAt": "...",
  "account": {
    "accountId": "...",
    "profileId": "...",
    "beeexyId": "..."
  }
}
```

Reuse existing DTOs/contracts if appropriate.

Do not create Google-specific token response types without a concrete need.

---

# 24. Error semantics

Follow the Phase 2 endpoint specification.

## Invalid Google identity

Return:

`401 Unauthorized`

Examples:

- invalid signature
- expired credential
- wrong issuer
- wrong audience
- malformed identity
- unacceptable new identity without verified email

Do not expose detailed Google validation internals.

## Provider disabled

Return:

`503 Service Unavailable`

## Provider unavailable

Return:

`503 Service Unavailable`

Do not convert temporary provider/infrastructure unavailability into `401` if the identity itself could not actually be validated because the provider infrastructure is unavailable.

## Disabled Beeexy Account

Return a safe authentication failure:

`401 Unauthorized`

## Invalid request shape

Use existing API conventions, including `422` where appropriate for validation failures.

---

# 25. Provider availability failures

Differentiate internally between:

```text
invalid credential
```

and:

```text
provider unavailable
```

but keep public details safe.

Examples of provider-unavailable conditions may include legitimate external validation infrastructure failures where the selected Google validation mechanism requires network/provider metadata.

Do not expose stack traces, URLs, SDK exceptions, or configuration secrets.

---

# 26. Security and privacy

Mandatory:

- never log Google identity credentials
- never log Google ID tokens
- never log Beeexy access tokens
- never log Beeexy refresh tokens
- never persist raw Google credentials
- never persist raw Beeexy refresh tokens
- do not trust client-provided email/profile information without provider validation
- do not use Beeexy ID for authentication
- do not use Google email as authorization
- do not put sensitive profile data into JWT claims

Use stable internal IDs for authorization.

---

# 27. Google data minimization

Persist only identity information required by Phase 2.

At minimum, the ExternalIdentity should need:

- provider
- subject
- Account relationship

Do not automatically persist:

- Google profile photo
- locale
- full Google profile
- OAuth access token
- OAuth refresh token
- unrelated Google claims

unless explicitly required by `IMPLEMENTATION_PLAN.md`.

This increment is authentication, not Google account synchronization.

---

# 28. Database changes

Prefer the existing Phase 2.1 `ExternalIdentity` schema.

Do not create a migration unless secure Google authentication genuinely requires a schema adjustment.

The existing unique:

`provider + subject`

must remain enforced.

If no schema changes are required:

- do not create an empty migration

Do not add Google-specific columns merely for convenience if the generic ExternalIdentity model already supports the requirement.

---

# 29. Tests — provider adapter

Add focused tests for the Google external identity adapter.

At minimum cover:

- valid identity
- invalid signature/credential
- expired identity
- wrong audience
- wrong issuer where applicable
- verified email
- unverified email
- provider disabled
- provider unavailable
- malformed credential

Do not require real production Google credentials for automated tests.

Abstract provider validation appropriately so application/integration tests remain deterministic.

---

# 30. Tests — existing Google user

Scenario:

1. ExternalIdentity already exists.
2. Submit valid Google identity with same provider + subject.
3. Resolve existing Account.
4. Reuse existing PatientProfile.
5. Reuse existing UserPreference.
6. Create a new Beeexy refresh session.
7. Return Beeexy token pair.

Assert no duplicate identity/account/profile records are created.

---

# 31. Tests — new Google user

Scenario:

1. valid Google identity
2. trusted verified email
3. no ExternalIdentity
4. no Account with normalized email

Expected:

- one Account
- one PatientProfile
- one UserPreference
- one ExternalIdentity
- one refresh-session family
- Beeexy token pair returned

Verify atomic persistence.

---

# 32. Tests — email account then Google

Mandatory scenario:

1. authenticate through email OTP
2. Account A exists
3. authenticate through Google using same trusted verified normalized email
4. create/link Google ExternalIdentity
5. resolve Account A
6. issue new Beeexy session

Assert:

```text
Account count = 1
Owned PatientProfile count = 1
UserPreference count = 1
Google ExternalIdentity count = 1
```

Confirm Google did not create Account B.

---

# 33. Tests — Google then email

Also verify the reverse convergence:

1. first authenticate with Google
2. Account A is provisioned
3. later authenticate using Beeexy email OTP for the same normalized email
4. email authentication resolves Account A

Expected:

- still one Account
- still one primary PatientProfile
- still one UserPreference
- Google ExternalIdentity remains linked
- email authentication creates only a new independent session

---

# 34. Tests — identity conflict

Test situations such as:

- provider/subject already linked to Account A
- trusted Google email corresponds to Account B

Do not silently transfer the ExternalIdentity.

Expected:

- safe authentication failure
- no identity reassignment
- no duplicate account
- no secret/account relationship disclosure

---

# 35. Tests — concurrency

Use PostgreSQL/Testcontainers for concurrency-sensitive scenarios.

Mandatory test:

multiple concurrent first Google authentications for the same:

- provider
- subject
- verified normalized email

Assert:

- exactly one ExternalIdentity
- exactly one Account
- exactly one owned PatientProfile
- exactly one UserPreference
- all successful resolutions point to the same Beeexy identity
- no unexpected `500`
- no duplicate account branch

Use the database uniqueness constraints as final authority.

---

# 36. Tests — session integration

Verify successful Google authentication integrates correctly with Phase 2.4.

After Google authentication:

1. receive Beeexy access token
2. receive Beeexy refresh token
3. rotate refresh token successfully
4. old token becomes unusable
5. logout works
6. refresh after logout fails

Do not implement separate Google session semantics.

---

# 37. API endpoint matrix

Add tests for:

`POST /api/v1/auth/google`

At minimum:

### Valid new Google user

`200`

### Valid existing Google user

`200`

### Existing email account + new Google identity

`200`

and linked to existing Account.

### Invalid credential

`401`

### Expired credential

`401`

### Wrong audience

`401`

### Unverified email for new identity

safe `401`

### Disabled Beeexy Account

safe `401`

### Google disabled

`503`

### Provider unavailable

`503`

### Invalid request

existing validation semantics (`422` where appropriate)

---

# 38. Regression requirements

All existing functionality must remain healthy:

```text
POST /api/v1/auth/email/challenges
POST /api/v1/auth/email/verify
POST /api/v1/auth/refresh
POST /api/v1/auth/logout
```

Confirm:

- OTP flow still works
- account provisioning still works
- account concurrency protection still works
- refresh rotation still works
- reuse detection still revokes family
- logout still works
- JWT validation still works
- migrations remain valid
- health endpoints remain healthy

Do not weaken email authentication to accommodate Google.

---

# 39. OpenAPI

Add/update OpenAPI documentation for:

`POST /api/v1/auth/google`

Document:

- request contract
- `200`
- `401`
- `422` if applicable
- `503`

Do not expose implementation-specific Google security details unnecessarily.

Existing bearer documentation must remain intact.

---

# 40. Local/development behavior

Google authentication should be optional during local development.

A developer must be able to run Beeexy with:

```text
Google authentication disabled
```

while continuing to use:

- email OTP challenge tests
- email verification
- refresh
- logout

Do not require Google credentials merely to start the backend when the provider is disabled.

If a deterministic fake/test external identity provider is useful for automated tests, implement it only through test/development dependency injection.

Never enable a fake Google identity provider in Production.

---

# 41. Production behavior

When Google authentication is enabled in Production:

- required configuration must be present
- credentials/client identifiers must come from secure configuration
- test adapters must not be active
- invalid Google credentials must not authenticate
- Beeexy must still issue its own session tokens

Do not commit actual production Google credentials.

---

# 42. Explicitly out of scope

Do NOT implement:

- Apple authentication
- passwords
- password reset
- caregiver-only accounts
- dependent claiming
- legal identity verification
- complex account recovery
- administrative identity UI
- `/api/v1/auth/me`
- `GET /api/v1/patients/me`
- `PATCH /api/v1/patients/me`
- patient demographic editing
- optimistic profile concurrency
- production transactional email provider
- Mailpit/local email UI unless already present
- FHIR
- clinical data
- Phase 3

Do not begin Phase 2.6.

---

# 43. Verification before completion

Before finishing:

1. Restore dependencies.
2. Build the complete backend.
3. Run Google-provider unit tests.
4. Run Google application/use-case tests.
5. Run Google API integration tests.
6. Run PostgreSQL Google concurrency tests.
7. Run email → Google convergence tests.
8. Run Google → email convergence tests.
9. Run Google → refresh → logout integration tests.
10. Run Phase 2.2–2.4 authentication regression tests.
11. Run the complete backend test suite.
12. Apply migrations to a clean PostgreSQL database.
13. Verify EF Core reports no unintended pending model changes.
14. Start the API with Google disabled.
15. Confirm email authentication remains functional.
16. Verify Google-disabled endpoint returns the expected `503`.
17. If test configuration supports enabled Google validation, verify the enabled flow.
18. Confirm no Google credential/token appears in persistence or logs.
19. Confirm no duplicate accounts/profiles can be produced through cross-method authentication.
20. Confirm no `/me`, profile update, production email provider, Apple, or Phase 3 behavior was introduced.

The repository must remain fully working.

---

# 44. Completion report

When Phase 2.5 is complete, report:

## Implemented

Describe exactly what Google authentication functionality was added.

## Google validation

Explain:

- credential type accepted
- validation mechanism/library
- issuer validation
- audience validation
- expiration validation
- verified-email handling

Do not print credentials or tokens.

## Provider abstraction

Explain:

- `IExternalIdentityProvider`
- infrastructure implementation
- provider enabled/disabled behavior

## Identity resolution

Explain behavior for:

- existing ExternalIdentity
- existing email Account without ExternalIdentity
- completely new Google user
- identity conflict
- disabled Account

## Account convergence

Report tests proving:

- email → Google produces one Account
- Google → email produces one Account

## Provisioning

Explain how existing Phase 2.3 provisioning logic was reused.

## Session issuance

Explain how Google authentication reuses Phase 2.4:

- RefreshSession
- Beeexy JWT
- Beeexy refresh token
- rotation
- logout

## Database

Report:

- whether a migration was required
- whether existing ExternalIdentity schema was sufficient
- constraints involved

## Configuration

Explain:

- how Google is enabled
- required configuration when enabled
- disabled behavior
- Production safety

## Security

Confirm:

- client-supplied identity data is not trusted without validation
- raw Google credentials are never persisted
- Google tokens are never logged
- Beeexy issues its own credentials
- Beeexy ID grants no authority
- unverified email cannot establish a new account linkage

## Concurrency

Report the concurrent first-Google-authentication behavior and test results.

## Files changed

List every created/modified file and purpose.

## Tests

Report:

- tests added
- commands executed
- pass/fail counts
- provider-validation tests
- API endpoint matrix
- convergence tests
- concurrency tests
- refresh/logout regression tests
- complete suite results

## Decisions

Document technical decisions made where Phase 2 left implementation details open.

Do not invent product requirements.

## Deferred

Explicitly confirm that account/profile `/me` endpoints and production transactional email delivery remain unimplemented.

The next increment will be:

**Phase 2.6 — Current Account and Primary Patient Profile Read/Update**

Do not implement it yet.

---

# STOP CONDITION

After implementing and verifying **Phase 2.5**, STOP.

Do not proceed automatically to Phase 2.6.

Wait for explicit approval before continuing.