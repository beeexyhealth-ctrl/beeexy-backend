# Appointment administration CLI

The Phase 8 appointment administration CLI is a temporary internal operations bridge. It
allows a trusted Beeexy operator to list requested appointments and confirm or reject one
appointment without SQL, a scheduler JWT, a Private Access cookie, or a temporary
`AppointmentScheduler` assignment. It is not a clinic portal.

Production commands operate against the database selected by
`ConnectionStrings:BeeexyDatabase` in the normal application configuration. Verify the
active environment and configuration before running a mutation. Never put a database
password or connection string on the command line. Apply all committed EF migrations before
using these commands against production.

## Environment policy

`ASPNETCORE_ENVIRONMENT` must be explicitly set, case-insensitively, to `Development` or
`Production`. Missing, `Staging`, `Test`, and unknown values are rejected. Mutation commands
print the active environment before accessing an appointment.

## Commands

List requested appointments for the clinic recorded on the authoritative availability slot:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/Beeexy.Api -- appointment-list-requested --clinic <clinicId>
```

The optional `--limit` is from 1 through 200. Its default is 50:

```powershell
dotnet run --project src/Beeexy.Api -- appointment-list-requested --clinic <clinicId> --limit 100
```

Results contain only `AppointmentId`, `ClinicId`, `Doctor`, `StartsAt`, `EndsAt`,
`ClinicTimeZone`, `Modality`, `Status`, and `CreatedAt`. They are ordered by `StartsAt`, then
`AppointmentId`. An empty list is a successful result. No patient, reason, Pre-Triage,
Clinical History, diagnosis, urgency, FHIR, idempotency, fingerprint, or concurrency data is
queried or printed.

Confirm an appointment:

```powershell
dotnet run --project src/Beeexy.Api -- appointment-confirm <appointmentId> --actor "local-operator"
```

Reject an appointment interactively:

```powershell
dotnet run --project src/Beeexy.Api -- appointment-reject <appointmentId> --actor "local-operator"
```

The reject command prints a minimal scheduling summary and prompts
`Reject this appointment? [y/N]`. Only `y`, case-insensitively, proceeds. For trusted
non-interactive automation, bypass the prompt explicitly:

```powershell
dotnet run --project src/Beeexy.Api -- appointment-reject <appointmentId> --actor "local-operator" --yes
```

`--actor` is required, trimmed, limited to 128 non-control characters, and stored on the
append-only status-history event as a `BeeexyOperations` actor. Use a stable operator or
automation identity. Do not put notes, clinical details, or secrets in it.

For Production, use normal production configuration and secret injection:

```bash
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Beeexy.Api --configuration Release -- appointment-list-requested --clinic <clinicId>
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Beeexy.Api --configuration Release -- appointment-confirm <appointmentId> --actor "operator@example.com"
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Beeexy.Api --configuration Release -- appointment-reject <appointmentId> --actor "operator@example.com" --yes
```

## Behavior and safety

Confirm and reject use the same application transition engine, domain lifecycle methods,
optimistic concurrency token, transaction boundary, append-only history, and reservation
constraint as the HTTP API. Confirm preserves the slot reservation. Reject releases it.
Repeating the same action succeeds without adding another history row. Missing appointments,
opposite or incompatible transitions, and concurrency conflicts do not overwrite state.

Structured logs contain only command name, appointment ID, clinic ID, result status,
operational actor, and whether a transition was newly applied. They exclude appointment
reason, patient and clinical data, credentials, tokens, and connection strings.

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Success, including empty lists, idempotent retries, and a declined reject prompt |
| 1 | Invalid arguments or validation failure |
| 2 | Appointment not found |
| 3 | Invalid transition, reservation conflict, or concurrency conflict |
| 4 | Missing/unsupported environment or database configuration failure |
| 5 | Unexpected failure; review structured server logs |

## Troubleshooting

- Exit 4: set `ASPNETCORE_ENVIRONMENT` explicitly and ensure
  `ConnectionStrings:BeeexyDatabase` is available through normal configuration.
- Exit 2: verify the complete appointment UUID and list the clinic's current requests.
- Exit 3: another operator or patient may have changed the appointment. List or inspect its
  current state and rerun only if the intended transition is still valid.
- Database or schema errors: confirm production secrets are configured and deploy the latest
  committed migrations. Do not work around the CLI with direct SQL updates.
