# Frontend API integration — Phase 6 FHIR R4 export

## 1. Purpose and source of truth

This guide is the frontend contract for the completed Phase 6 FHIR R4 export
surface. It explains how an authenticated Beeexy client creates an export from
one immutable Clinical History event, reads privacy-safe lifecycle metadata,
and downloads the already-generated validated bytes.

The current backend endpoint DTOs, application access layer, domain lifecycle,
Firely R4 serializer and validator, OpenAPI declarations, and Phase 6.7
acceptance tests are the source of truth for the statements labeled **backend
contract**. Suggestions labeled **frontend recommendation** describe a safe UX
but are not server requirements.

This document covers exactly these operations:

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/patients/{patientId}/fhir-exports` | Generate and validate an export |
| `GET` | `/api/v1/fhir-exports/{id}` | Read lifecycle metadata |
| `GET` | `/api/v1/fhir-exports/{id}/content` | Download validated immutable content |

It does not define a frontend application, change the backend, add a FHIR
server integration, or start Phase 7.

The frontend does not construct `QuestionnaireResponse`, `Device`,
`Provenance`, or `Bundle` resources. It also does not serialize, checksum,
validate, store, or regenerate the artifact. Those operations belong entirely
to the backend; the frontend supplies technical selectors, presents safe
metadata, and downloads the result.

### Repository facts that remain undefined

There is no frontend source tree in this repository. Consequently, the current
repository does not define:

- exact Next.js route, component, service, or hook filenames;
- whether the consuming app uses the App Router or Pages Router;
- React Query, SWR, Axios, or another data-fetching/state library;
- an export history/list screen or client-side persistence policy;
- a backend export-list endpoint, cancellation endpoint, or delete endpoint;
- a server polling interval, `Retry-After`, webhook, or push notification;
- cache validators such as `ETag` or `Last-Modified` for these routes.

The TypeScript names and hook shapes below are therefore implementation
recommendations. Adapt them to the consuming frontend's established
centralized authenticated client. Do not introduce a second token store or HTTP
stack solely for Phase 6.

## 2. Prerequisites and identifiers

Before starting an export, the frontend needs:

1. a valid Beeexy access token;
2. the selected Phase 3 `activePatient.profileId` as `patientId`;
3. an accessible immutable Clinical History event UUID as
   `sourceClinicalHistoryEventId`;
4. one non-empty UUID `idempotencyKey`, generated once for the user's export
   intent and retained across retries.

Both primary-patient and active-manager access are supported. The access token
always identifies the Account; selecting `activePatient` does not change the
token. A patient, event, or export UUID is only a selector and never grants
authority.

Use the configured API origin from the existing environment/configuration
layer. The Phase 6 contract defines relative `/api/v1` routes, not a fixed
deployment origin.

## 3. Authentication, authorization, and headers

### Backend contract

All three routes require:

```http
Authorization: Bearer <beeexyAccessToken>
```

The create request also requires a JSON body:

```http
Content-Type: application/json
```

Useful optional headers are:

```http
Accept: application/json
X-Correlation-ID: <privacy-safe-client-correlation-id>
```

for create and metadata, and:

```http
Accept: application/fhir+json
X-Correlation-ID: <privacy-safe-client-correlation-id>
```

for download. `Accept` is not required by these endpoint handlers and the
download response media type is fixed, but sending it expresses client intent.
The global API pipeline supports the optional `X-Correlation-ID` header and
always returns an `X-Correlation-ID`. Do not put a patient identifier, event
identifier, token, or clinical value in a correlation ID.

There is no `Idempotency-Key` HTTP header in this API. The idempotency key is a
required request-body UUID.

Authorization is reevaluated on every operation:

- the patient profile's owning Account is authorized;
- an Account whose primary profile has an exact Active manager relationship to
  the patient is authorized;
- a Revoked manager and an unrelated Account are denied;
- metadata and download resolve the export's source patient and reauthorize
  current access at request time.

Missing and inaccessible patients, source events, and exports are deliberately
concealed behind the same `404` behavior. The frontend must not claim that a
resource exists, reveal prior access, or translate this into a specific
“permission denied” message.

### Frontend recommendation

Route every call through the existing `authenticatedFetch`-style client so it
applies the current access token, coordinates one-time token refresh, preserves
correlation IDs, and joins the relative route to the configured API origin.
Do not read or copy access tokens inside export components.

If patient access is revoked while an export view is open, close the view after
the concealed `404`, refresh the accessible-patient list, and safely fall back
from an invalid `activePatient` selection.

## 4. Shared TypeScript contracts

These types mirror the JSON DTOs exposed by both create and metadata responses.
UUIDs and timestamps are JSON strings; timestamps are ISO-8601 instants.

```ts
export type FhirExportStatus =
  | "Pending"
  | "Generated"
  | "ValidationFailed"
  | "Validated";

export type FhirExportValidationOutcome = "Failed" | "Passed";

export interface FhirExportValidationMetadata {
  outcome: FhirExportValidationOutcome;
  errorCount: number;
  warningCount: number;
  completedAt: string;
}

export interface FhirExportMetadata {
  id: string;
  status: FhirExportStatus;
  fhirVersion: string;
  mappingVersion: string;
  createdAt: string;
  generatedAt: string | null;
  validationCompletedAt: string | null;
  validation: FhirExportValidationMetadata | null;
}

export interface CreateFhirExportRequest {
  sourceClinicalHistoryEventId: string;
  idempotencyKey: string;
}

export interface BeeexyProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  correlationId?: string;
}
```

Do not add fields such as `fhirVersion`, `mappingVersion`, profile, validator,
patient ID, Account ID, or requested resource types to
`CreateFhirExportRequest`. Unknown JSON fields are explicitly rejected.

## 5. Create and validate an export

### Request

```http
POST /api/v1/patients/{patientId}/fhir-exports
Authorization: Bearer <beeexyAccessToken>
Content-Type: application/json
Accept: application/json

{
  "sourceClinicalHistoryEventId": "d108c93c-f6df-4c7b-98ee-fde52fd9260b",
  "idempotencyKey": "5a58ac61-782c-4326-8104-a52a68cf15e7"
}
```

`patientId`, `sourceClinicalHistoryEventId`, and `idempotencyKey` must be
non-empty UUIDs. The source event must belong to the route patient and be
accessible to the caller.

The server—not the client—selects:

- FHIR R4 release `4.0.1`;
- mapping `beeexy-fhir-r4-base-mvp-v1`;
- the base R4 validation specification;
- the serializer, validator, and runtime version.

The operation generates and validates synchronously. A normal successful
create response is already `Validated`; creation is not a request to enqueue a
frontend-visible background job.

```text
immutable Clinical History source
→ FHIR R4 generation and serialization
→ private immutable storage and SHA-256 checksum
→ Firely R4 validation
→ Validated or ValidationFailed
```

### New success: `201 Created`

A newly created and validated export returns metadata and a relative `Location`
header:

```http
HTTP/1.1 201 Created
Location: /api/v1/fhir-exports/ffca5aca-f79d-4331-8496-e9c55a6f70a4
Content-Type: application/json; charset=utf-8

{
  "id": "ffca5aca-f79d-4331-8496-e9c55a6f70a4",
  "status": "Validated",
  "fhirVersion": "4.0.1",
  "mappingVersion": "beeexy-fhir-r4-base-mvp-v1",
  "createdAt": "2026-08-24T20:30:00+00:00",
  "generatedAt": "2026-08-24T20:30:00.0100000+00:00",
  "validationCompletedAt": "2026-08-24T20:30:00.0200000+00:00",
  "validation": {
    "outcome": "Passed",
    "errorCount": 0,
    "warningCount": 0,
    "completedAt": "2026-08-24T20:30:00.0200000+00:00"
  }
}
```

The timestamp values above are illustrative. Treat them as opaque ISO-8601
instants and format them for display in the user's locale/timezone.

### Idempotent replay success: `200 OK`

Replaying the same `patientId`, `sourceClinicalHistoryEventId`, and
`idempotencyKey` returns the same export metadata with `200 OK`. Concurrent
identical requests are also collapsed to one persisted export and one artifact;
one request can receive `201` while the replay receives `200`.

The frontend must accept both `200` and `201` as success. Do not treat `200` as
a failure, and do not require the `Location` header on a replay.

Idempotency is scoped by patient. Reusing the same UUID for another patient is
an independent key, but a frontend should still generate a fresh key for a new
intent to avoid confusing local state.

### Create errors

| Status | Backend meaning | Frontend behavior |
|---|---|---|
| `400` | Malformed JSON or request binding failure | Fix client serialization; do not retry unchanged |
| `401` | Missing, expired, or invalid Beeexy access token | Use the established coordinated refresh/login flow |
| `404` | Patient/source absent, wrong-patient source, or concealed access denial | Show a generic unavailable message; refresh patient access if appropriate |
| `409` | The same patient-scoped idempotency key was used for different export inputs | Stop automatic retry; preserve evidence and start a new intent only after correcting client state |
| `422` | Missing/empty identifiers, unknown fields, unsupported mapping input, or completed FHIR validation rejection | Treat as non-transient; show safe feedback and do not loop |
| `503` | Artifact storage, validator, or supporting export infrastructure is unavailable | Offer retry with the same idempotency key |
| `500` | Safe unexpected or artifact-integrity failure | Do not expose internals; retain correlation ID; a reconciliation retry must reuse the same key |

Two request-validation failures expose stable `errorCode` values:

| `errorCode` | Cause |
|---|---|
| `fhir_export.identifiers_required` | Either required UUID is missing or the empty UUID |
| `fhir_export.unsupported_field` | The request contains any additional JSON property |

Other Phase 6 errors do not guarantee an `errorCode`. Branch first on HTTP
status and use a recognized stable code only when present. Do not branch on the
English `title` or `detail` text.

A standards-invalid artifact is durably stored as `ValidationFailed`, but the
POST returns `422` Problem Details rather than metadata. The current API does
not include the failed export ID in that Problem Details response and provides
no export-list/discovery endpoint. A frontend cannot navigate to that failed
metadata unless it already obtained the export ID through some separately
defined application state; do not invent an ID or parse logs.

## 6. Read export metadata

### Request and success

```http
GET /api/v1/fhir-exports/{id}
Authorization: Bearer <beeexyAccessToken>
Accept: application/json
```

Success is `200 OK` with `FhirExportMetadata`. The response intentionally does
not include:

- artifact bytes or FHIR resources;
- patient, Account, source-event, Beeexy ID, or idempotency key;
- checksum or checksum algorithm;
- private storage URI/path;
- validator identity or raw diagnostics;
- questionnaire questions, answers, or other clinical content.

The response fields mean:

| Field | Meaning |
|---|---|
| `id` | Export UUID used by metadata and content routes |
| `status` | Exact persisted lifecycle value |
| `fhirVersion` | Truthful version recorded for this artifact; current new exports use `4.0.1` |
| `mappingVersion` | Truthful mapping recorded for this artifact; current new exports use `beeexy-fhir-r4-base-mvp-v1` |
| `createdAt` | When the export record was created |
| `generatedAt` | When immutable bytes were stored, otherwise `null` |
| `validationCompletedAt` | When a terminal validation result was stored, otherwise `null` |
| `validation` | Sanitized terminal result and counts, otherwise `null` |

`validation.completedAt` and top-level `validationCompletedAt` describe the
same completed validation event in the current contract. Keep both fields in
the TypeScript type because both are part of the response DTO.

### Exact lifecycle values

| Status | Expected timestamps/validation | Download eligibility |
|---|---|---|
| `Pending` | Only `createdAt`; generation and validation fields are `null` | Not downloadable |
| `Generated` | `generatedAt` set; validation fields are `null` | Not downloadable |
| `ValidationFailed` | generation and validation completion set; `validation.outcome` is `Failed` | Not downloadable |
| `Validated` | generation and validation completion set; `validation.outcome` is `Passed` | Potentially downloadable; content endpoint remains authoritative |

The enum values are case-sensitive. Model all four even though a normal new
POST runs generation and validation synchronously and returns only after a
terminal outcome.

Metadata can describe historical release-neutral records truthfully. Do not
rewrite an old `fhirVersion` or `mappingVersion` to the current R4 values and do
not infer current-download eligibility from a friendly UI label. Historical
artifacts are never upgraded in place.

### Metadata errors

The documented outcomes are:

| Status | Meaning and response |
|---|---|
| `401` | Missing/invalid authentication; run the established auth flow |
| `404` | Missing or currently inaccessible export; conceal the cause |
| `500` | Safe unexpected internal failure; retain correlation ID |

A malformed or empty UUID does not match the `:guid` route constraint, or is
handled as not found, and therefore yields `404`, not a UUID-specific `400`.

For a known export ID, metadata GET is useful for reopening an export detail,
refreshing its authoritative lifecycle state, or supporting a future screen
that already has the ID. It cannot discover exports because no list endpoint
is defined.

### Polling guidance

**Backend contract:** no polling protocol or interval is defined. There is no
`Retry-After`, progress percentage, estimated completion time, webhook, or push
event. The create call performs its normal validation synchronously.

**Frontend recommendation:** do not start a polling loop after an ordinary
successful POST; use the returned metadata immediately. Poll metadata only
when the client has a known export ID in `Pending` or `Generated` state—for
example, after restoring a route created by a future or external workflow.
Use bounded exponential backoff with jitter, stop on either terminal state,
stop when the view unmounts or patient changes, and impose a visible timeout.
Those timings are frontend policy, not backend guarantees.

## 7. Download exact validated content

### Request

```http
GET /api/v1/fhir-exports/{id}/content
Authorization: Bearer <beeexyAccessToken>
Accept: application/fhir+json
```

### Success contract

For an authorized export in `Validated` state whose frozen specification
exactly matches the current R4 base MVP, success is:

```http
HTTP/1.1 200 OK
Content-Type: application/fhir+json
Content-Disposition: attachment; filename=beeexy-fhir-export-<export-id>.json
```

The semantic filename is exactly:

```text
beeexy-fhir-export-{lowercase-hyphenated-export-uuid}.json
```

The framework may quote or add standards-compatible encoding parameters to the
raw `Content-Disposition` header. Prefer its supplied filename and use the
known semantic filename as a fallback.

The body is the exact immutable byte array that was generated, checksummed, and
validated. The backend reads the existing private artifact, verifies its
stored SHA-256 checksum with a fixed-time comparison, and returns those bytes
without regeneration. The API does not expose its checksum or private storage
identity.

Range processing is disabled. Do not build resumable or byte-range download
behavior for this endpoint. `ETag`, `Last-Modified`, and client caching
semantics are not defined by the current contract.

### Download errors

| Status | Backend meaning | Frontend behavior |
|---|---|---|
| `401` | Missing/invalid authentication | Use established refresh/login behavior |
| `404` | Export absent or access now concealed | Close the inaccessible view; never confirm existence |
| `409` | Export is not eligible for download (`Pending`, `Generated`, `ValidationFailed`, or non-current/legacy specification) | Refresh metadata and disable download; do not retry in a tight loop |
| `503` | Artifact storage is temporarily unavailable | Offer delayed retry |
| `500` | Integrity verification or an unexpected internal operation failed | Do not save partial content; retain correlation ID for support |

The content route is the final authority. Even when metadata says `Validated`,
the server can reject a historical artifact whose frozen FHIR/mapping
specification is not the exact current R4 base MVP.

A conservative current-MVP button condition is:

```ts
const isDownloadCandidate =
  metadata.status === "Validated" &&
  metadata.fhirVersion === "4.0.1" &&
  metadata.mappingVersion === "beeexy-fhir-r4-base-mvp-v1";
```

This client check improves presentation but never replaces the authenticated
content request or its server-side authorization and eligibility checks.

### Browser-safe TypeScript download

The following is a client-side Next.js/TypeScript example. The exact module
path is not defined by this repository. It deliberately treats non-success as
Problem Details and success as a `Blob`; it does not JSON-parse the FHIR body.

```ts
type AuthenticatedFetch = (
  input: string,
  init?: RequestInit,
) => Promise<Response>;

declare const authenticatedFetch: AuthenticatedFetch;

export class ApiProblemError extends Error {
  constructor(
    public readonly status: number,
    public readonly problem: BeeexyProblemDetails | null,
    public readonly correlationId: string | null,
  ) {
    super(problem?.title ?? `API request failed with status ${status}`);
  }
}

async function throwProblem(response: Response): Promise<never> {
  const correlationId = response.headers.get("X-Correlation-ID");
  const contentType = response.headers.get("Content-Type") ?? "";
  let problem: BeeexyProblemDetails | null = null;

  if (contentType.includes("application/problem+json")) {
    problem = (await response.json()) as BeeexyProblemDetails;
  }

  throw new ApiProblemError(response.status, problem, correlationId);
}

function filenameFromContentDisposition(
  value: string | null,
  fallback: string,
): string {
  if (!value) return fallback;

  const utf8 = value.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8?.[1]) {
    try {
      return decodeURIComponent(utf8[1].trim());
    } catch {
      return fallback;
    }
  }

  const basic = value.match(/filename="?([^";]+)"?/i);
  return basic?.[1]?.trim() || fallback;
}

export async function downloadFhirExport(exportId: string): Promise<void> {
  const response = await authenticatedFetch(
    `/api/v1/fhir-exports/${encodeURIComponent(exportId)}/content`,
    {
      method: "GET",
      headers: { Accept: "application/fhir+json" },
    },
  );

  if (!response.ok) await throwProblem(response);

  const blob = await response.blob();
  const fallback = `beeexy-fhir-export-${exportId.toLowerCase()}.json`;
  const filename = filenameFromContentDisposition(
    response.headers.get("Content-Disposition"),
    fallback,
  );
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement("a");

  try {
    anchor.href = objectUrl;
    anchor.download = filename;
    anchor.rel = "noopener";
    document.body.appendChild(anchor);
    anchor.click();
  } finally {
    anchor.remove();
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 0);
  }
}
```

Run this helper only in the browser because it uses `document`, `window`, and
object URLs. Do not place the artifact bytes in React state, serialize them to
JSON, write them to `localStorage`, or send them through analytics/error
reporting.

## 8. Recommended client service shape

The following names are **frontend recommendations**, not existing repository
files. A consuming app could expose three small functions from its established
API layer:

```ts
async function readJsonOrProblem<T>(response: Response): Promise<T> {
  if (!response.ok) await throwProblem(response);
  return (await response.json()) as T;
}

export async function createFhirExport(
  patientId: string,
  request: CreateFhirExportRequest,
): Promise<FhirExportMetadata> {
  const response = await authenticatedFetch(
    `/api/v1/patients/${encodeURIComponent(patientId)}/fhir-exports`,
    {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify(request),
    },
  );

  return readJsonOrProblem<FhirExportMetadata>(response);
}

export async function getFhirExport(
  exportId: string,
): Promise<FhirExportMetadata> {
  const response = await authenticatedFetch(
    `/api/v1/fhir-exports/${encodeURIComponent(exportId)}`,
    { method: "GET", headers: { Accept: "application/json" } },
  );

  return readJsonOrProblem<FhirExportMetadata>(response);
}
```

`downloadFhirExport` from the prior section is the third service function.
Keep the response status available in the generic client if analytics or UI
needs to distinguish new `201` from replayed `200`, but both are successful and
have the same JSON type.

If the frontend uses a query library, conceptual hooks might be:

- `useCreateFhirExport()` for the POST mutation;
- `useFhirExport(exportId)` for metadata with key
  `['fhir-export', exportId]`;
- a plain event handler calling `downloadFhirExport(exportId)` for content.

Do not cache a content `Blob` in a general query cache. Cancel metadata work and
clear inaccessible export views on logout, patient switch, or access-revocation
signals. Whether metadata is cached is a consuming-app choice; never confuse a
cached authorization success with current server authority.

## 9. Idempotency strategy

### Backend contract

The effective create identity is patient scope plus idempotency key. For the
current server-owned mapping:

- same patient + same key + same source event returns the same export;
- concurrent identical requests create only one export/artifact;
- same patient + same key + different source event returns `409`;
- same patient + different key creates a new export;
- different patient + same key is an independent scope.

The artifact is immutable. A replay does not regenerate successful content.

### Frontend recommendation

Create one key with `crypto.randomUUID()` at the beginning of a deliberate
export intent:

```ts
interface ExportIntent {
  patientId: string;
  sourceClinicalHistoryEventId: string;
  idempotencyKey: string;
}

function newExportIntent(
  patientId: string,
  sourceClinicalHistoryEventId: string,
): ExportIntent {
  return {
    patientId,
    sourceClinicalHistoryEventId,
    idempotencyKey: crypto.randomUUID(),
  };
}
```

Retain that object while a request is in flight and through transport,
authentication-refresh, `500`, or `503` reconciliation retries. Never generate
a fresh key in a generic retry interceptor. Disable repeated submit while the
same intent is running.

Generate a new key only for a deliberate new export intent. If the user changes
patient or source event, discard the old intent and create a new one. A `409`
means the frontend reused a key with inconsistent input; do not hide that bug
by automatically changing the key and resubmitting.

Long-term persistence of an incomplete intent is not defined by the backend.
If the consuming app chooses to survive a page reload, store only the minimum
technical intent metadata under its established security policy—never tokens,
FHIR bytes, or clinical answers.

## 10. Recommended user flow and UI states

### End-to-end happy path

1. Load the current accessible-patient list and validate `activePatient`.
2. Open an accessible Clinical History event detail.
3. Let the user explicitly request a FHIR export.
4. Create and retain one UUID idempotency key for that intent.
5. Disable duplicate submit and show an indeterminate “Generating and
   validating…” state while POST is pending.
6. Accept either `201` or `200` and retain the returned export ID/metadata.
7. Confirm `status === 'Validated'` and display the version, validation summary,
   and timestamps.
8. Enable download and request `/content` only on an explicit user action.
9. Save the response as the server filename, then release the object URL and
   discard the in-memory Blob.

The Clinical History event detail is the natural recommended entry point
because POST requires its exact UUID. This is a UX suggestion; the backend does
not prescribe a screen.

### Presentation state model

| Client state | Trigger | Recommended presentation |
|---|---|---|
| `idle` | No intent | “Export as FHIR R4” action |
| `submitting` | POST in flight | Indeterminate generation/validation state; disable duplicate action |
| `validated` | Successful metadata status `Validated` | Show success, versions/counts, enable explicit download |
| `pending` | Known metadata status `Pending` | Show queued/not generated; optional bounded metadata refresh |
| `generated` | Known metadata status `Generated` | Show generated/validation incomplete; optional bounded refresh |
| `validationFailed` | Known metadata status `ValidationFailed`, or POST `422` validation rejection | Show safe “could not produce a valid FHIR export”; no download |
| `unavailable` | Concealed `404` | Close sensitive detail and show generic unavailable state |
| `transientError` | Network/`503`, selected safe `500` reconciliation | Offer controlled retry with the same idempotency key |
| `downloadError` | Content `409`/`503`/`500` | Do not save bytes; refresh metadata or offer safe retry as appropriate |

Do not show a fabricated percentage, queue position, validation diagnostics,
or ETA. None exists in the contract.

### Retry matrix

| Operation/failure | Automatic retry? | Required key/state behavior |
|---|---|---|
| POST network failure before a response | At most controlled retry | Reuse the same idempotency key because commit outcome is unknown |
| POST `401` followed by successful coordinated refresh | One normal authenticated replay | Reuse the same request and key |
| POST `503` | Bounded/manual retry is reasonable | Reuse the same key; generation may already exist |
| POST `500` | Prefer user-controlled reconciliation | Reuse the same key; retain correlation ID |
| POST `400`, `404`, `409`, `422` | No unchanged retry loop | Correct input/access or end the intent |
| metadata GET network/`500` | Bounded retry with backoff | GET is safe; stop on unmount/patient change |
| metadata GET `404` | No | Conceal and close inaccessible state |
| content GET `503` | Delayed/manual retry | Same export ID |
| content GET `409` | No immediate retry | Refresh metadata; endpoint says artifact is ineligible |
| content GET `500` | No blind loop | Save no bytes; retain correlation ID |

## 11. Problem Details and safe messages

Errors use `application/problem+json`. A representative shape is:

```json
{
  "title": "FHIR export state conflict.",
  "status": 409,
  "detail": "The FHIR export is not available for this operation.",
  "instance": "/api/v1/fhir-exports/ffca5aca-f79d-4331-8496-e9c55a6f70a4/content",
  "correlationId": "01f4a38747ff4da58feca135ca7a68c8"
}
```

`type`, `detail`, and `errorCode` are optional depending on the failure path.
`instance` is the request path. Prefer the body `correlationId`, falling back to
the `X-Correlation-ID` response header, for privacy-safe support workflows.

Current backend titles/details include:

| Situation | Safe title | Safe detail |
|---|---|---|
| Missing/concealed export or source | `FHIR export not found.` | `The requested FHIR export could not be found.` |
| Idempotency mismatch | `FHIR export conflict.` | `The idempotency key belongs to different export inputs.` |
| Ineligible download state | `FHIR export state conflict.` | `The FHIR export is not available for this operation.` |
| Completed validation rejected | `FHIR validation failed.` | `The generated artifact did not pass FHIR validation.` |
| Mapping/input cannot export | `FHIR export mapping failed.` | `The source cannot be exported with the current FHIR mapping.` |
| Expected infrastructure outage | `FHIR export service unavailable.` | `FHIR export infrastructure is currently unavailable.` |
| Artifact integrity failure | `FHIR artifact integrity failure.` | `The immutable artifact could not be safely processed.` |

These strings are informative, not stable programmatic codes. Suggested UI
copy should remain generic:

- `404`: “This export is no longer available.”
- validation/mapping `422`: “Beeexy could not create a valid FHIR R4 export
  from this record.”
- `503`: “FHIR export is temporarily unavailable. Try again later.”
- `500`: “The export could not be processed safely. Contact support with the
  correlation ID if the problem continues.”

Never display raw server exceptions, raw validator diagnostics, storage paths,
or inferred hidden-resource ownership.

## 12. FHIR scope the frontend may communicate

### Backend contract

New successful exports are official FHIR JSON for R4 `4.0.1`, media type
`application/fhir+json`, mapping version
`beeexy-fhir-r4-base-mvp-v1`. The document is a `Bundle` with
`type = collection` and exactly one of each:

1. `QuestionnaireResponse`;
2. software `Device`;
3. `Provenance`.

Internal resource identities are deterministic UUID URNs and references resolve
inside the Bundle. The `QuestionnaireResponse` uses frozen question codes as
`linkId` and frozen answer schemas to choose truthful R4 `value[x]` types.

The validation pipeline performs strict R4 parsing, Firely POCO
structural/model validation, and Beeexy's closed Bundle/reference checks. It
does not use an external terminology server and does not claim conformance to a
separate implementation guide/profile. In particular, the current export makes
no US Core claim and defines no custom Beeexy FHIR profile.

`RiskAssessment` is intentionally deferred because the current source lacks an
authoritative prediction outcome, probability, or mitigation.
`Composition`, `Patient`, `Organization`, and `Practitioner` are also outside
this closed MVP. The export does not fabricate urgency, disposition, diagnosis,
treatment, probability, or other clinical facts.

### Frontend wording recommendation

It is accurate to say “FHIR R4 export” or “FHIR R4 4.0.1 collection Bundle.” It
is not accurate to advertise:

- a full patient chart;
- an EHR submission or synchronization;
- terminology-server validation;
- implementation-guide/profile certification;
- a diagnosis, clinical recommendation, or RiskAssessment;
- an export containing Patient/provider/organization resources.

Do not inspect the downloaded Bundle to decide whether the backend succeeded.
The server already validated the exact bytes. Client-side parsing is optional
for a separate product feature and is not needed for download.

## 13. Legacy and non-current artifacts

Phase 6 had an earlier release-neutral snapshot state. Those historical
artifacts retain their recorded version, mapping, media type, and lifecycle;
the backend never mutates or relabels them as current R4 content.

Metadata may therefore expose values other than `4.0.1` and
`beeexy-fhir-r4-base-mvp-v1`. Display them truthfully if a known legacy export
is opened. The content endpoint rejects non-current specifications with `409`
even if historical state otherwise appears terminal.

The frontend must not:

- replace a legacy version label with `4.0.1`;
- offer a force-download or construct a private artifact URL;
- treat `Validated` alone as a permanent guarantee for every historical
  specification;
- regenerate a legacy object in the browser;
- claim historical release-neutral JSON is FHIR JSON.

There is currently no endpoint for listing or discovering historical exports.
GET metadata requires a known export UUID, and metadata does not return the
source-event or patient UUID. An export history UI therefore needs a future
explicit backend contract; it must not scrape logs or infer associations.

## 14. Security and privacy checklist

- Use only the established Bearer-authenticated client and configured API
  origin.
- Never put access tokens in a query string; send them only in the
  `Authorization` header.
- Always download through the authenticated `/content` endpoint. Never
  construct a direct storage URL or attempt to use a private storage path.
- Never log access/refresh tokens, request Authorization headers, FHIR bytes,
  questionnaire answers, or downloaded Blob contents.
- Do not send clinical content, export bodies, or patient/event/export IDs to
  analytics, session replay, or generic error-reporting breadcrumbs.
- Treat patient, Clinical History event, and export UUIDs as sensitive technical
  identifiers even though they are not authorization secrets.
- Treat every concealed `404` as “unavailable,” not proof of existence or
  access revocation.
- Revalidate current patient access through the backend; cached metadata is not
  authority.
- Keep content in memory only long enough to initiate the user-requested file
  save, then revoke the object URL and drop references.
- Do not store FHIR bytes in `localStorage`, `sessionStorage`, IndexedDB,
  service-worker caches, a query cache, logs, or crash reports without a future
  reviewed product/security requirement.
- Do not expose checksums, validator diagnostics, or private artifact paths;
  they are intentionally absent from the DTO.
- Do not use Beeexy ID, export ID, source event ID, or a prior successful
  download as an authorization credential.
- Cancel stale requests and clear patient-scoped export UI on logout, patient
  switch, or known access revocation.
- Preserve only the privacy-safe correlation ID when escalating a failure to
  support.

## 15. End-to-end implementation example

This orchestration is a **frontend recommendation**. It shows the important
key-reuse and success checks without prescribing a component framework:

```ts
export async function createThenDownloadFhirExport(
  patientId: string,
  sourceClinicalHistoryEventId: string,
): Promise<FhirExportMetadata> {
  const intent = newExportIntent(patientId, sourceClinicalHistoryEventId);

  // Keep `intent` stable if the established client performs an auth refresh or
  // if the UI offers a controlled reconciliation retry.
  const metadata = await createFhirExport(intent.patientId, {
    sourceClinicalHistoryEventId: intent.sourceClinicalHistoryEventId,
    idempotencyKey: intent.idempotencyKey,
  });

  if (metadata.status !== "Validated") {
    // Defensive for the full lifecycle contract. A normal successful current
    // POST is already Validated.
    return metadata;
  }

  await downloadFhirExport(metadata.id);
  return metadata;
}
```

Prefer separate “Create export” and “Download” user actions so the user can
review the result and browsers do not block an asynchronous automatic file
save. If the consuming product intentionally combines them, keep clear loading
and error boundaries for the POST and content GET because creation can succeed
while the later download encounters revoked access or temporary storage
unavailability.

## 16. Contract summary

The Phase 6 frontend integration is intentionally small: create from one
authorized immutable source event using a stable body idempotency UUID, consume
the returned lifecycle metadata, and download only through the authenticated
content route. The server owns R4 mapping and validation, protects access on
every request, conceals inaccessible resources, and releases only exact
checksum-verified immutable bytes. The client owns safe intent state, honest UI
wording, controlled retries, short-lived Blob handling, and privacy-preserving
error presentation.

## 17. Frontend delivery checklist

- [ ] Reuse the existing centralized authenticated HTTP client and configured
  API origin.
- [ ] Use `activePatient.profileId`, never Beeexy ID, as POST `patientId`.
- [ ] Source `sourceClinicalHistoryEventId` from an accessible immutable
  Clinical History event.
- [ ] Use Bearer authentication for create, metadata, and content requests;
  never put the token in a query string.
- [ ] Send only `sourceClinicalHistoryEventId` and `idempotencyKey` in the POST
  body.
- [ ] Generate the idempotency UUID once per deliberate intent and reuse it for
  every reconciliation retry.
- [ ] Accept both `201 Created` and idempotent `200 OK` as create success.
- [ ] Model all four exact status values and both exact validation outcomes.
- [ ] Preserve timestamp nullability and both validation completion fields.
- [ ] Do not poll after an ordinary synchronous successful POST.
- [ ] If a known nonterminal ID is polled, use bounded backoff and stop on
  terminal state, timeout, unmount, or patient switch.
- [ ] Enable ordinary download only for current validated metadata, but still
  treat the content endpoint as authoritative and handle `409`.
- [ ] Fetch content as a `Blob`; never call `response.json()` on a successful
  content response.
- [ ] Download only through the authenticated `/content` route and preserve the
  backend `application/fhir+json` response as exact bytes.
- [ ] Use the `Content-Disposition` filename with the exact documented fallback.
- [ ] Revoke the object URL and avoid persistent/query caching of FHIR bytes.
- [ ] Do not construct FHIR resources or run export mapping/validation in the
  browser.
- [ ] Do not expose or construct private artifact storage references.
- [ ] Do not persist FHIR JSON in `localStorage`, `sessionStorage`, or IndexedDB
  by default.
- [ ] Do not log FHIR bodies, raw clinical JSON, tokens, or download content.
- [ ] Parse non-success responses as optional-field Problem Details.
- [ ] Handle concealed `404` without revealing whether the resource exists.
- [ ] Preserve correlation ID for support without attaching clinical data.
- [ ] Retry transient/ambiguous POST outcomes only with the original
  idempotency key.
- [ ] Do not claim an external FHIR server, terminology validation, profile
  certification, full chart, or RiskAssessment.
- [ ] Do not invent export listing, cancellation, deletion, progress, cache, or
  range-download capabilities.
- [ ] Test primary-owner access, active-manager access, revoked access,
  idempotent replay, input conflict, validation rejection, legacy/ineligible
  content, transient download failure, and Blob cleanup.
