# Database-backed private tester access

## Boundary

Private Access is a deployment gate and tester authentication mechanism. In database mode,
each credential is linked one-to-one to a normal Beeexy `Account`. Successful login creates:

```text
private credential -> opaque private session -> Account -> PatientProfile/UserPreference
                                           \-> normal JWT and refresh-session family
```

Pre-Triage, Clinical History, FHIR, and other patient-scoped modules receive only the normal
Beeexy Account/Profile identity. They do not contain demo-tester authorization branches.

## Runtime configuration

| Variable | Purpose |
| --- | --- |
| `PrivateAccess__Enabled` | Enables the deployment gate. |
| `PrivateAccess__AuthenticationMode` | Use `Legacy` during migration and `Database` after tester provisioning. |
| `PrivateAccess__SessionLifetimeMinutes` | Private-session lifetime, maximum 1440 minutes. |
| `PrivateAccess__LoginPermitLimit` | Per-IP attempts in each fixed window. |
| `PrivateAccess__LoginRateLimitWindowMinutes` | Fixed-window duration. |

Database mode does not require an individual username, password hash, keyword hash, or private
session signing key in environment configuration. It uses opaque random cookies and stores only
their hashes. Rate-limit windows are shared through PostgreSQL.

The following variables are legacy-only and must be removed after migration:

- `PrivateAccess__Username`
- `PrivateAccess__PasswordHash`
- `PrivateAccess__KeywordHash`
- `PrivateAccess__SessionSigningKey`
- every `PrivateAccess__DemoGuest__*` variable

## Provisioning

Apply EF migrations before running any command. Use the direct production database connection
from a trusted workstation.

Migrate the deployed shared guest while still in `Legacy` mode:

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet run \
  --project src/Beeexy.Api --configuration Release -- \
  private-access migrate-demo-guest
```

Provision a new atomic batch:

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet run \
  --project src/Beeexy.Api --configuration Release -- \
  private-access provision-testers \
  --batch-id external-2026 \
  --count 100 \
  --output ./external-2026-credentials.csv
```

The output file is created with owner-only permissions and is never overwritten. It contains
the only recoverable copy of each password and keyword. Transfer it through an approved encrypted
channel and remove local copies after distribution. A successful rerun verifies the existing
batch and does not re-emit credentials. If the artifact is lost, rotate credentials explicitly.

Generated accounts use visibly synthetic demographics (`Demo Tester NNN`, 1990-01-01, alternating
Female/Male, CA, America/Lima) and synthetic `.invalid` emails. These values can be edited through
the existing profile APIs and must never be treated as real clinical demographics.

## Administration

```text
private-access list [--batch-id <slug>]
private-access deactivate --tester-key <key>
private-access activate --tester-key <key>
private-access revoke --tester-key <key> --confirm
private-access rotate-credentials --tester-key <key> --output <new.csv>
```

Deactivation disables both the credential and linked Account and revokes all private and refresh
sessions. It is reversible. Revocation performs the same invalidation but is permanent. Clinical
data is preserved in both cases.

## API and security behavior

- `POST /api/v1/private-access/login` accepts username/password/keyword. In database mode it returns
  the standard Beeexy authentication response and sets the HTTP-only private cookie.
- `GET /api/v1/private-access/session` retains the `{ authenticated, expiresAt }` response.
- `POST /api/v1/private-access/logout` revokes the current private session and linked refresh family.
- `POST /api/v1/private-access/guest-session` is legacy-only and returns unavailable in database mode.
- Invalid, disabled, revoked, and unknown credentials receive the same generic denial.
- The gate performs an indexed database check on every API request so revocation is immediate.
- Normal JWT claims, rotation, reuse detection, Account-active checks, and patient authorization remain unchanged.

## Safe rollout

1. Back up production and apply the additive migration.
2. Deploy the compatibility backend in `Legacy` mode; existing cookies and frontend continue working.
3. Run `migrate-demo-guest`, then provision and securely distribute the tester batch.
4. Deploy a frontend that accepts either legacy `204` login or database-mode `200` login.
5. Change `PrivateAccess__AuthenticationMode` to `Database`; existing stateless cookies reauthenticate.
6. Verify two independent testers, cross-profile denial, logout, refresh, and deactivation.
7. Remove legacy environment variables and the frontend `guest-session` call.
8. Remove the legacy endpoint/classes in a later cleanup release after production verification.
