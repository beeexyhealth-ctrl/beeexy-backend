# Phase 6.7 FHIR export API and acceptance closure

## Outcome and scope

Phase 6.7 exposes the concrete State A FHIR R4 export pipeline completed by the
standards-validation unblocking work. It adds authenticated creation, safe
lifecycle metadata, and integrity-gated download. It does not add a public
artifact location, an external FHIR server integration, a new FHIR resource, a
clinical inference, or a database migration.

The server owns the FHIR release, mapping, validation specification, serializer,
validator, and runtime version. A caller can select only the immutable Clinical
History source event and a UUID idempotency key.

## HTTP endpoints

All routes require a valid Beeexy bearer token.

### Create and validate

```http
POST /api/v1/patients/{patientId}/fhir-exports
Content-Type: application/json

{
  "sourceClinicalHistoryEventId": "<uuid>",
  "idempotencyKey": "<uuid>"
}
```

A new export returns `201 Created`, a `Location` header pointing to the metadata
route, and safe metadata. A replay of the same patient, idempotency key, and
source returns `200 OK` and the same export. Reusing a key for different inputs
within its patient scope returns `409 Conflict`. Unsupported request fields are
rejected with `422`; clients cannot request R5, override the mapping, or choose a
validator.

The use case first reuses `GenerateFhirExport`, then reuses
`ValidateFhirExport`. Generation stores one immutable byte array and its SHA-256
checksum. Validation reloads and verifies those exact bytes before applying the
real R4 validator. PostgreSQL transaction-scoped advisory locks preserve both
generation idempotency and one consistent validation outcome under concurrent
requests. Because both stages share a scoped EF context at the HTTP boundary,
the validation transaction clears the committed generation snapshot before
acquiring its lock and reloads the winning database state.

A standards-invalid artifact is durably represented as `ValidationFailed` and
the POST returns safe `422` Problem Details. Validator, storage, or supporting
infrastructure unavailability does not create false validation evidence and is
reported separately as safe `503` Problem Details.

### Read metadata

```http
GET /api/v1/fhir-exports/{id}
```

This returns `200 OK` with lifecycle metadata only:

- export ID and lifecycle status;
- truthful FHIR and mapping versions;
- creation, generation, and validation-completion timestamps;
- sanitized validation outcome plus error and warning counts.

It never returns artifact bytes, a checksum, a private URI/path, a patient or
account identifier, raw diagnostics, questionnaire content, or other PHI.

### Download validated content

```http
GET /api/v1/fhir-exports/{id}/content
Accept: application/fhir+json
```

Only an export in `Validated` state whose frozen specification exactly matches
the current R4 base MVP can be downloaded. The use case reads the existing
private artifact once, verifies its stored SHA-256 using the existing
fixed-time comparison, and returns those exact bytes without regeneration as:

```http
Content-Type: application/fhir+json
Content-Disposition: attachment; filename=beeexy-fhir-export-<export-id>.json
```

`Pending`, `Generated`, and `ValidationFailed` exports return `409 Conflict`.
Historical release-neutral artifacts also return `409`; they retain their real
version/state and are never upgraded in place or mislabeled as FHIR. A checksum
mismatch returns a safe internal integrity failure and no bytes.

## Authorization and concealment

Every operation resolves the export's source patient and applies the shared
Phase 3 patient-access model at request time:

- the patient's owner is allowed;
- an active manager is allowed;
- a revoked manager is denied;
- an unrelated account is denied.

Missing and inaccessible patients/exports use the same concealed `404` shape.
An export UUID, FHIR resource UUID, Beeexy ID, relationship creator, or prior
access does not grant authority. Manager access is reevaluated for every
metadata read and download. Missing or invalid bearer credentials return `401`.

## State A FHIR contract

New exports use FHIR R4 4.0.1 and mapping
`beeexy-fhir-r4-base-mvp-v1`. The UTF-8 JSON is a `Bundle` with
`type = collection` and exactly these resources:

1. `QuestionnaireResponse`
2. software `Device`
3. `Provenance`

Entry identities are deterministic UUID URNs and all internal references
resolve within the Bundle. Frozen question codes become `linkId`; frozen answer
schemas control the truthful R4 `value[x]` type. Firely R4 serialization and
validation remain confined to Infrastructure; Domain has no FHIR SDK reference.

Base R4 validation includes strict parsing, POCO structural/model validation,
and Beeexy's closed Bundle/reference contract. It does not execute an external
terminology server and makes no implementation-guide/profile conformance claim.

`RiskAssessment` remains deferred because the current source has no
authoritative prediction outcome, probability, or mitigation. `Composition`,
`Patient`, `Organization`, and `Practitioner` are also outside this closed MVP.
No urgency, disposition, diagnosis, treatment, probability, or other clinical
content is fabricated to obtain validation success.

## Error and privacy behavior

The endpoints use the existing safe Problem Details pipeline:

- `400` for malformed JSON/request binding;
- `401` for missing or invalid bearer authentication;
- concealed `404` for missing or inaccessible resources;
- `409` for idempotency or download-state conflicts;
- `422` for mapping/input or standards-validation rejection;
- `503` for expected validator/storage infrastructure unavailability;
- `500` for an unexpected or integrity-safe internal failure.

Responses and logs do not expose FHIR bodies, raw answers/free text, tokens,
private artifact identities, exception types, or raw validator diagnostics.
Privacy-safe technical audit events record successful export creation,
validation completion, successful validated download, and integrity rejection.
They contain technical identifiers, action/status, access category, and time
only.

## OpenAPI and acceptance status

OpenAPI adds exactly these operations and documents bearer security, request and
idempotency behavior, relevant response codes, and
`application/fhir+json` for validated content. The API-level acceptance journey
authenticates, completes Pre-Triage, creates and validates an export, reads
metadata, downloads the exact stored bytes, parses and inspects the R4 Bundle,
and proves the clinical source is unchanged.

Phase 6.7 is complete and closes Phase 6 in concrete State A. Historical State B
notes remain valid for the earlier release-neutral period and those immutable
artifacts. Phase 7 is not started by this work.

## Verification evidence

- The Debug solution build completed with zero warnings and zero errors.
- All 13 focused Phase 6.7 unit cases, all 90 Phase 6 unit regressions, and all
  578 unit tests passed.
- All five Phase 6.7 API/PostgreSQL journeys, all 40 focused FHIR,
  migration-behavior, and OpenAPI integration regressions, and all 19 dedicated
  migration regressions passed.
- All 13 repository-wide OpenAPI regressions passed. OpenAPI has exactly 24
  paths and adds only the three Phase 6 operations.
- The full integration run executed 350 tests: 344 passed and the same six
  pre-existing development-bootstrap/unavailable-database fixture failures
  remained. No Phase 6.7 or FHIR test failed.
- EF Core reports no pending model changes; no migration was added.
- Solution-wide formatting, static dependency inspection, and
  `git diff --check` passed.
