# Beeexy Frontend API Integration — Phase 5

## 1. Purpose and Phase 5 overview

This document is the frontend integration contract for **Phase 5 — Clinical History and Amendments**. It covers the three implemented Clinical History operations, their Bearer authentication and patient authorization rules, opaque cursor pagination, immutable source provenance, and traceable free-text amendments.

The backend endpoint mappings, DTOs, OpenAPI document, application behavior, and PostgreSQL integration tests are authoritative. This guide does not define frontend styling or any planned Phase 6 behavior.

Clinical History is:

- a patient-owned timeline;
- backed by immutable clinical source records;
- currently populated only by completed Pre-Triage episodes;
- readable by the primary patient or an active authorized manager;
- amendable without overwriting the original event or Pre-Triage source.

The normal authenticated flow is:

```text
Pre-Triage completed
        ↓
ClinicalHistoryEvent created by the backend
        ↓
Clinical History list
        ↓
Select event
        ↓
Clinical History detail
        ↓
Optional amendment
        ↓
Refresh detail
```

The anonymous flow is:

```text
Anonymous Pre-Triage completion
        ↓
No patient Clinical History yet
        ↓
User signs in and claims the episode
        ↓
ClinicalHistoryEvent created by the backend
        ↓
Normal authenticated Phase 5 flow
```

Phase 5 exposes exactly these endpoints:

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/patients/{patientId}/clinical-history` | Read a cursor-paginated patient timeline |
| `GET` | `/api/v1/patients/{patientId}/clinical-history/{eventId}` | Read one patient-scoped event and its amendments |
| `POST` | `/api/v1/pre-triage/episodes/{episodeId}/amendments` | Add an immutable, traceable amendment |

There is no frontend operation for creating a history event. Projection happens transactionally inside authenticated Pre-Triage completion or anonymous claim.

## 2. Existing frontend integration prerequisites

All routes in this guide use the exact `/api/v1` prefix. Obtain the API origin from the existing environment/configuration layer. Local development normally uses `http://localhost:5105`, but it must not be embedded in components.

Reuse the existing centralized Beeexy API client described in:

- [`frontend-api-integration.md`](frontend-api-integration.md) for token storage, coordinated refresh, logout, Problem Details, correlation IDs, CORS, and environment configuration;
- [`frontend-api-phase-3.md`](frontend-api-phase-3.md) for the primary/managed `activePatient` model and relationship revocation;
- [`frontend-api-phase-4.md`](frontend-api-phase-4.md) for completion, canonical result retrieval, and anonymous claim.

Do not create Phase 5-specific access-token storage, refresh logic, or a second HTTP client. The repository does not prescribe React Query, SWR, Axios, or another frontend data library, so the examples below use standards-based TypeScript and can be adapted to the existing centralized client.

Use technical UUIDs as follows:

- `patientId`: the selected PatientProfile UUID from the Phase 3 accessible-patient response;
- `eventId`: the Clinical History event UUID returned by the list endpoint;
- `episodeId`: `source.id` from a history list item or detail response.

A Beeexy ID is not a route substitute for `patientId`.

## 3. Authentication and authorization

Every Phase 5 request requires:

```http
Authorization: Bearer <accessToken>
```

The backend validates the token and authorizes the target patient on every request.

| Caller state | Result |
|---|---|
| Primary patient requesting their own history | Allowed |
| Manager with an Active relationship to the patient | Allowed |
| Manager whose relationship was revoked | Concealed `404` |
| Unrelated authenticated Account | Concealed `404` |
| Missing, invalid, or expired Bearer token | `401` |

For amendment creation, the route contains only `episodeId`; the backend resolves the episode's patient and applies the same primary/Active-manager authorization. Knowing an episode or source UUID does not grant access.

The frontend must never use a Beeexy ID, patient UUID, event UUID, episode UUID, source UUID, Account UUID, or relationship UUID as proof of authorization. Identifiers identify resources; only the backend decides authority.

If manager access is revoked while a history screen is open, the next list, detail, or amendment request returns the same concealed `404` used for an absent resource. Remove the inaccessible patient view from the manager UI, refresh the Phase 3 accessible-patient state, and do not reveal whether the record still exists.

## 4. List Clinical History

### `GET /api/v1/patients/{patientId}/clinical-history`

Use this endpoint to fetch the selected patient's timeline. It is read-only and does not create, update, or project records.

### Path parameter

| Parameter | Type | Meaning |
|---|---|---|
| `patientId` | UUID | Technical PatientProfile ID for the primary or actively managed patient |

### Query parameters

| Parameter | Type | Required | Behavior |
|---|---|---:|---|
| `cursor` | string | No | Opaque cursor returned in the previous page's `nextCursor` |
| `pageSize` | integer | No | Defaults to `20`; valid range is `1` through `100` |
| `eventType` | string | No | The only supported value is `COMPLETED_PRE_TRIAGE` |

Omitting `eventType` means all currently supported history event types. There is currently only one. Values are exact and case-sensitive; do not send a display label or lowercase variant.

There is no `offset`, page number, total count, or artificial total-history limit. A patient may have more than ten records.

### Ordering

The backend returns events in deterministic keyset order:

1. `occurredAt DESC`;
2. `eventId DESC` as the tie-breaker.

`occurredAt` is the source episode's completion time. `recordedAt` is when that event entered Clinical History. For an authenticated completion these are currently the same; for an anonymous episode, `recordedAt` is the later successful claim time.

### Example request

```http
GET /api/v1/patients/10000000-0000-0000-0000-000000000001/clinical-history?pageSize=20&eventType=COMPLETED_PRE_TRIAGE
Authorization: Bearer <accessToken>
Accept: application/json
```

### `200 OK`

```json
{
  "items": [
    {
      "eventId": "60000000-0000-0000-0000-000000000006",
      "eventType": "COMPLETED_PRE_TRIAGE",
      "occurredAt": "2026-08-24T14:30:00Z",
      "recordedAt": "2026-08-24T14:30:00Z",
      "source": {
        "type": "PRE_TRIAGE_EPISODE",
        "id": "50000000-0000-0000-0000-000000000005",
        "questionnaireVersionId": "30000000-0000-0000-0000-000000000003",
        "clinicalRuleSetVersionId": "40000000-0000-0000-0000-000000000004"
      }
    }
  ],
  "nextCursor": "eyJ2IjoxLCJwYXRpZW50SWQiOiIuLi4ifQ"
}
```

On the last page, or when the timeline is empty, `nextCursor` is `null`:

```json
{
  "items": [],
  "nextCursor": null
}
```

The list DTO contains source identity and frozen version references, not the full original Pre-Triage answers or neutral result.

## 5. Cursor pagination in the frontend

Treat `nextCursor` as an opaque continuation token. Never decode, edit, concatenate, or construct one in the frontend.

Recommended lifecycle:

```text
Initial load:
    cursor = null
    GET history without cursor
    replace items

If nextCursor is not null:
    enable Load more / infinite scroll

Next load:
    GET history with cursor = previous nextCursor
    append items
    replace nextCursor

When nextCursor is null:
    no more pages
```

Discard existing items and cursor when:

- `patientId` changes;
- `eventType` changes;
- the user explicitly refreshes from the newest page;
- authentication/authorization state changes.

A cursor is bound to the patient, the optional event-type filter, and its boundary event. Reusing it with another patient or filter, sending a malformed cursor, or sending a cursor whose boundary is unavailable returns `422` with `clinical_history.cursor_invalid`.

Events inserted after the first page follow keyset semantics:

- events newer than the current boundary do not suddenly appear in later pages;
- older events remain eligible for later pages;
- refreshing from the first page is how the UI discovers newly inserted newer events.

### TypeScript pagination example

```ts
async function loadFirstHistoryPage(
  patientId: Uuid,
  signal?: AbortSignal,
): Promise<ClinicalHistoryListState> {
  const page = await getClinicalHistory(
    patientId,
    { pageSize: 20, eventType: "COMPLETED_PRE_TRIAGE" },
    signal,
  );

  return {
    items: page.items,
    nextCursor: page.nextCursor,
  };
}

async function loadNextHistoryPage(
  patientId: Uuid,
  current: ClinicalHistoryListState,
  signal?: AbortSignal,
): Promise<ClinicalHistoryListState> {
  if (current.nextCursor === null) return current;

  const page = await getClinicalHistory(
    patientId,
    {
      cursor: current.nextCursor,
      pageSize: 20,
      eventType: "COMPLETED_PRE_TRIAGE",
    },
    signal,
  );

  // A request/UI retry should not duplicate an already rendered event.
  const known = new Set(current.items.map((item) => item.eventId));
  const appended = page.items.filter((item) => !known.has(item.eventId));

  return {
    items: [...current.items, ...appended],
    nextCursor: page.nextCursor,
  };
}
```

Prevent two simultaneous “load more” requests from consuming the same cursor. Use `isLoadingMore`, request cancellation, or the equivalent deduplication mechanism already present in the frontend.

## 6. Read a Clinical History event

### `GET /api/v1/patients/{patientId}/clinical-history/{eventId}`

Call this endpoint when the user selects an item from the timeline.

### Path parameters

| Parameter | Type | Source |
|---|---|---|
| `patientId` | UUID | Current Phase 3 `activePatient.profileId` |
| `eventId` | UUID | `eventId` from the selected history item |

The event is scoped to the patient in the URL. A real event UUID paired with the wrong patient UUID returns the same `404` as an absent event.

### Example request

```http
GET /api/v1/patients/10000000-0000-0000-0000-000000000001/clinical-history/60000000-0000-0000-0000-000000000006
Authorization: Bearer <accessToken>
Accept: application/json
```

### `200 OK`

```json
{
  "eventId": "60000000-0000-0000-0000-000000000006",
  "eventType": "COMPLETED_PRE_TRIAGE",
  "occurredAt": "2026-08-24T14:30:00Z",
  "recordedAt": "2026-08-24T14:30:00Z",
  "source": {
    "type": "PRE_TRIAGE_EPISODE",
    "id": "50000000-0000-0000-0000-000000000005",
    "questionnaireVersionId": "30000000-0000-0000-0000-000000000003",
    "clinicalRuleSetVersionId": "40000000-0000-0000-0000-000000000004"
  },
  "provenance": {
    "sourceType": "PRE_TRIAGE_EPISODE",
    "sourceId": "50000000-0000-0000-0000-000000000005",
    "questionnaireVersionId": "30000000-0000-0000-0000-000000000003",
    "clinicalRuleSetVersionId": "40000000-0000-0000-0000-000000000004"
  },
  "amendments": [
    {
      "amendmentId": "70000000-0000-0000-0000-000000000007",
      "reason": "Correct reported duration",
      "author": {
        "type": "BEEEXY_ACCOUNT",
        "beeexyId": "BXY-EXAMPLE"
      },
      "createdAt": "2026-08-24T15:00:00Z",
      "provenance": {
        "sourceType": "PRE_TRIAGE_EPISODE",
        "sourceId": "50000000-0000-0000-0000-000000000005",
        "questionnaireVersionId": "30000000-0000-0000-0000-000000000003",
        "clinicalRuleSetVersionId": "40000000-0000-0000-0000-000000000004"
      }
    }
  ]
}
```

The `source` and `provenance` objects deliberately repeat the authoritative episode and frozen questionnaire/rule-set version identities in their public shapes. The frontend should display provenance when useful, but must not reinterpret or recompute the original result from these IDs.

Important current limitation: this detail response exposes event metadata, source identity, version provenance, and amendments. It does **not** expose the original symptoms, answers, duration, intensity, neutral result, questionnaire content, or rule-set content. There is also no Phase 5 endpoint that accepts `source.id` to retrieve the full source episode. Do not invent those fields or attempt to treat version UUIDs as clinical content.

## 7. Rendering amendments in the detail view

`amendments` is always an array and may be empty. Existing amendments are ordered deterministically from oldest to newest by `createdAt`, then by `amendmentId` for equal timestamps.

Each amendment contains:

- its server-generated `amendmentId`;
- the trimmed free-text `reason` submitted by the user;
- public author data (`type: "BEEEXY_ACCOUNT"` and a nullable `beeexyId`);
- server-controlled `createdAt`;
- provenance matching the original event's authoritative source and frozen versions.

An amendment does **not** overwrite or replace the event, episode, answers, symptoms, assessment, source versions, or earlier amendments. The list representation also remains unchanged after amendment creation.

Recommended presentation:

```text
Original event metadata
-----------------------
Event type
Occurred / recorded timestamps
Source and provenance

Amendments / Corrections
------------------------
- reason, public author, timestamp, provenance
- reason, public author, timestamp, provenance
```

Do not merge the amendment reason into an “effective” clinical result. The current backend stores traceability metadata and a free-text reason only; it does not define a corrected field/value or replacement semantics.

## 8. Create a Pre-Triage amendment

### `POST /api/v1/pre-triage/episodes/{episodeId}/amendments`

Use the selected event's source ID as the path value:

```text
history item or detail
        ↓
source.id
        ↓
episodeId
```

Do not use `eventId`, patient ID, session ID, or a version ID in this route.

### Request

```http
POST /api/v1/pre-triage/episodes/50000000-0000-0000-0000-000000000005/amendments
Authorization: Bearer <accessToken>
Content-Type: application/json
Accept: application/json
```

```json
{
  "idempotencyKey": "80000000-0000-0000-0000-000000000008",
  "reason": "Correct reported duration"
}
```

The request accepts exactly these JSON fields:

| Field | Type | Required | Rules |
|---|---|---:|---|
| `idempotencyKey` | UUID string | Yes | Must be a non-empty UUID |
| `reason` | string | Yes | Must contain non-whitespace text; surrounding whitespace is trimmed |

Unknown fields are rejected with `422`, including attempted author, patient, timestamp, provenance, correction, urgency, or audit fields. The frontend must not send an amendment ID, author ID, Account ID, Beeexy ID, patient ID, creation timestamp, or source provenance. The backend derives or generates all of them.

### Idempotency-key lifecycle

Generate the UUID in the frontend before the logical submission. `crypto.randomUUID()` is suitable in supported secure browser contexts.

- Generate one key for one logical “add correction” action.
- Retain and reuse that key while retrying the same action after an uncertain network outcome.
- Do not generate a new key merely because a retry returned `409`.
- Generate a new key only when the user intentionally starts a genuinely new amendment.
- The uniqueness scope is the Clinical History event, enforced by PostgreSQL.

The key is not returned in the success response or detail DTO. Keep it only as transient submission state; do not use it as a displayed identifier.

### `201 Created`

```json
{
  "amendmentId": "70000000-0000-0000-0000-000000000007",
  "reason": "Correct reported duration",
  "author": {
    "type": "BEEEXY_ACCOUNT",
    "beeexyId": "BXY-EXAMPLE"
  },
  "createdAt": "2026-08-24T15:00:00Z",
  "provenance": {
    "sourceType": "PRE_TRIAGE_EPISODE",
    "sourceId": "50000000-0000-0000-0000-000000000005",
    "questionnaireVersionId": "30000000-0000-0000-0000-000000000003",
    "clinicalRuleSetVersionId": "40000000-0000-0000-0000-000000000004"
  }
}
```

The response also includes a `Location` header ending in `/amendments/{amendmentId}`. No GET endpoint currently exists at that location, so do not navigate to or fetch it. Refetch the Clinical History event detail instead.

## 9. Amendment form UX and retry behavior

Suggested integration flow:

```text
User opens Clinical History event
        ↓
Selects Add correction
        ↓
Enters a non-empty reason
        ↓
Frontend generates one UUID
        ↓
POST amendment
        ↓
201 Created
        ↓
Refetch event detail
        ↓
New amendment appears oldest → newest
```

Recommended local behavior:

- trim only for client validation/display convenience; the backend remains authoritative and trims the persisted reason;
- disable duplicate clicks while one request is in flight;
- keep `reason` and `idempotencyKey` until the submission is conclusively reconciled;
- after `201` and a successful detail refresh, clear the form and key;
- after a correctable `422`, keep the form, let the user correct it, and retain the same key for that logical action;
- after an uncertain transport/`500` result, retain the key and reconcile before retrying;
- after `404`, close the inaccessible editor and show the generic unavailable state.

For `409`, refetch detail first. The backend does not return the existing amendment in the conflict response, and detail does not expose idempotency keys. If the intended reason is visible in the refreshed amendments, treat the user action as reconciled. If it cannot be safely identified, show a neutral “submission already processed or conflicted” state and retain support diagnostics; do not force another insert with a new key.

## 10. Error handling and Problem Details

Expected failures use `application/problem+json`. The shared Beeexy shape is:

```json
{
  "title": "Request validation failed.",
  "status": 422,
  "detail": "Page size must be between 1 and 100.",
  "instance": "/api/v1/patients/10000000-0000-0000-0000-000000000001/clinical-history",
  "errorCode": "clinical_history.page_size_invalid",
  "correlationId": "<request-correlation-id>"
}
```

`type`, `detail`, and `errorCode` depend on the failure path and must be treated as optional. `instance` is the request path. `correlationId` is also returned in `X-Correlation-ID`; retain it for support without attaching tokens or clinical content.

Branch first on HTTP status and then on `errorCode` when present. Do not parse human-readable `title` or `detail` to drive logic.

| Status | Meaning | Frontend behavior |
|---|---|---|
| `200` | Successful list or detail read | Replace/append state as appropriate |
| `201` | Amendment created | Refetch detail, then clear the reconciled form |
| `400` | Malformed JSON, invalid query-value type, or malformed HTTP request | Fix request construction; do not retry unchanged |
| `401` | Missing, invalid, or expired Bearer authentication | Apply the existing single coordinated refresh; if unrecoverable, require login |
| `404` | Resource absent or concealed: unauthorized patient, revoked manager, wrong-patient event, absent event, or inaccessible episode | Never distinguish causes; show `This record is no longer available.` |
| `409` | The amendment idempotency key already exists for this event | Refetch detail; do not generate a new key just to force success |
| `422` | Cursor, page-size, filter, idempotency key, reason, or request-shape validation failed | Use `errorCode` for safe feedback where appropriate |
| `500` | Safe unexpected failure | Do not assume a write committed; retain key, correlation ID, and reconcile before retrying |

Current Phase 5 validation codes are:

| Code | Endpoint | Meaning |
|---|---|---|
| `clinical_history.page_size_invalid` | List | `pageSize` is outside `1–100` |
| `clinical_history.event_type_invalid` | List | `eventType` is not exactly `COMPLETED_PRE_TRIAGE` |
| `clinical_history.cursor_invalid` | List | Cursor is malformed, unavailable, or belongs to another patient/filter |
| `clinical_amendment.invalid_idempotency_key` | Amendment | Missing, malformed, or empty UUID key |
| `clinical_amendment.invalid_reason` | Amendment | Missing, blank, or otherwise invalid reason |
| `clinical_amendment.unsupported_fields` | Amendment | Request contains any field beyond `idempotencyKey` and `reason` |

The exact duplicate response currently has title `Clinical amendment conflict.` and detail `An amendment with this idempotency key already exists.` It has no stable `errorCode`; branch on `409`, not that text.

The concealed history/detail/amendment `404` currently maps to the generic patient-profile-not-found Problem Details. This is intentional privacy behavior. Do not change the message to “permission denied” or infer which concealed case occurred.

## 11. TypeScript data contracts

These interfaces mirror the current camelCase JSON. UUIDs and ISO timestamps are strings at transport level.

```ts
export type Uuid = string;
export type IsoTimestamp = string;

export type ClinicalHistoryEventType = "COMPLETED_PRE_TRIAGE";
export type ClinicalHistorySourceType = "PRE_TRIAGE_EPISODE";
export type ClinicalHistoryAuthorType = "BEEEXY_ACCOUNT";

export interface ClinicalHistorySource {
  type: ClinicalHistorySourceType;
  id: Uuid;
  questionnaireVersionId: Uuid;
  clinicalRuleSetVersionId: Uuid;
}

export interface ClinicalHistoryProvenance {
  sourceType: ClinicalHistorySourceType;
  sourceId: Uuid;
  questionnaireVersionId: Uuid;
  clinicalRuleSetVersionId: Uuid;
}

export interface ClinicalHistoryItem {
  eventId: Uuid;
  eventType: ClinicalHistoryEventType;
  occurredAt: IsoTimestamp;
  recordedAt: IsoTimestamp;
  source: ClinicalHistorySource;
}

export interface ClinicalHistoryPage {
  items: ClinicalHistoryItem[];
  nextCursor: string | null;
}

export interface ClinicalHistoryAmendmentAuthor {
  type: ClinicalHistoryAuthorType;
  beeexyId: string | null;
}

export interface ClinicalHistoryAmendment {
  amendmentId: Uuid;
  reason: string;
  author: ClinicalHistoryAmendmentAuthor;
  createdAt: IsoTimestamp;
  provenance: ClinicalHistoryProvenance;
}

export interface ClinicalHistoryEventDetail extends ClinicalHistoryItem {
  provenance: ClinicalHistoryProvenance;
  amendments: ClinicalHistoryAmendment[];
}

export interface CreatePreTriageAmendmentRequest {
  idempotencyKey: Uuid;
  reason: string;
}

export type CreatePreTriageAmendmentResponse = ClinicalHistoryAmendment;

export interface BeeexyProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  correlationId?: string;
}

export interface ClinicalHistoryQuery {
  cursor?: string;
  pageSize?: number;
  eventType?: ClinicalHistoryEventType;
}

export interface ClinicalHistoryListState {
  items: ClinicalHistoryItem[];
  nextCursor: string | null;
}
```

Do not add urgency, disposition, diagnosis, red flags, recommendations, source result, corrected value, or idempotency key to these response types; the backend does not return them.

## 12. Suggested frontend service functions

The following code is an adapter example for the existing centralized authenticated client. `authenticatedFetch` must apply the current Beeexy access token and coordinated one-time refresh policy. It must not read a token from component-local storage.

```ts
type AuthenticatedFetch = (
  path: string,
  init?: RequestInit,
) => Promise<Response>;

// Supplied by the application's existing centralized Beeexy API client.
declare const authenticatedFetch: AuthenticatedFetch;

export class BeeexyApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly problem: BeeexyProblemDetails | null,
  ) {
    super(problem?.title ?? `Beeexy API request failed with ${status}`);
  }
}

async function readJsonOrProblem<T>(response: Response): Promise<T> {
  if (response.ok) return (await response.json()) as T;

  const problem = response.headers
    .get("content-type")
    ?.includes("application/problem+json")
    ? ((await response.json()) as BeeexyProblemDetails)
    : null;

  throw new BeeexyApiError(response.status, problem);
}

export async function getClinicalHistory(
  patientId: Uuid,
  query: ClinicalHistoryQuery = {},
  signal?: AbortSignal,
): Promise<ClinicalHistoryPage> {
  const search = new URLSearchParams();
  if (query.cursor !== undefined) search.set("cursor", query.cursor);
  if (query.pageSize !== undefined) {
    search.set("pageSize", String(query.pageSize));
  }
  if (query.eventType !== undefined) {
    search.set("eventType", query.eventType);
  }

  const suffix = search.size === 0 ? "" : `?${search.toString()}`;
  const response = await authenticatedFetch(
    `/api/v1/patients/${encodeURIComponent(patientId)}/clinical-history${suffix}`,
    {
      method: "GET",
      headers: { Accept: "application/json" },
      signal,
    },
  );

  return readJsonOrProblem<ClinicalHistoryPage>(response);
}

export async function getClinicalHistoryEvent(
  patientId: Uuid,
  eventId: Uuid,
  signal?: AbortSignal,
): Promise<ClinicalHistoryEventDetail> {
  const response = await authenticatedFetch(
    `/api/v1/patients/${encodeURIComponent(patientId)}` +
      `/clinical-history/${encodeURIComponent(eventId)}`,
    {
      method: "GET",
      headers: { Accept: "application/json" },
      signal,
    },
  );

  return readJsonOrProblem<ClinicalHistoryEventDetail>(response);
}

export async function createPreTriageAmendment(
  episodeId: Uuid,
  request: CreatePreTriageAmendmentRequest,
  signal?: AbortSignal,
): Promise<CreatePreTriageAmendmentResponse> {
  const response = await authenticatedFetch(
    `/api/v1/pre-triage/episodes/${encodeURIComponent(episodeId)}/amendments`,
    {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
      },
      body: JSON.stringify(request),
      signal,
    },
  );

  return readJsonOrProblem<CreatePreTriageAmendmentResponse>(response);
}
```

The centralized client must send the actual request with:

```ts
headers.set("Authorization", `Bearer ${accessToken}`);
```

It should also join the relative path to the configured API origin, parse Problem Details, preserve `X-Correlation-ID`, and coordinate refresh so multiple `401` responses do not trigger concurrent refresh-token rotation. Do not blindly retry the amendment POST with a newly generated key.

If the existing client already exposes typed `get`/`post` helpers, keep those conventions and use the path/query/body behavior shown above rather than copying this wrapper literally.

## 13. Recommended frontend state flow

### List state

```ts
interface ClinicalHistoryScreenState {
  patientId: Uuid;
  eventType?: ClinicalHistoryEventType;
  items: ClinicalHistoryItem[];
  nextCursor: string | null;
  isLoading: boolean;
  isLoadingMore: boolean;
  error: BeeexyApiError | null;
}
```

- Replace `items` on initial load, patient change, filter change, or refresh.
- Append by unique `eventId` only when loading the current `nextCursor`.
- Ignore/cancel responses for a previously selected patient.
- `nextCursor === null` is the only “no more pages” signal; there is no total count.

### Detail state

```ts
interface ClinicalHistoryDetailState {
  patientId: Uuid;
  eventId: Uuid;
  event: ClinicalHistoryEventDetail | null;
  isLoading: boolean;
  error: BeeexyApiError | null;
}
```

Replace the entire `event` with each successful detail response. Do not calculate an “effective” event by mutating the original with amendments.

### Amendment state

```ts
interface AddAmendmentState {
  reason: string;
  idempotencyKey: Uuid | null;
  isSubmitting: boolean;
  error: BeeexyApiError | null;
}
```

Create the key when the user initiates the logical submission, not on every HTTP attempt. Retain it across ambiguous network/`500` outcomes and `409` reconciliation. Clear it after confirmed success or when the user cancels and later starts a genuinely new amendment.

## 14. Cache and refresh behavior

After amendment creation, do not locally rewrite the original event or fabricate a new corrected event.

Preferred sequence:

1. POST the amendment.
2. Receive `201`.
3. Refetch/revalidate `GET /patients/{patientId}/clinical-history/{eventId}`.
4. Replace cached detail with the backend response.

Conceptually:

```ts
await createPreTriageAmendment(detail.source.id, {
  idempotencyKey,
  reason,
});

const refreshed = await getClinicalHistoryEvent(patientId, detail.eventId);
setDetail(refreshed);
```

The list response does not include amendment counts and the event itself does not change, so list invalidation is not required solely to show the new amendment. Refetch it only if the product UI has separate derived indicators outside the current API contract.

No frontend cache library is present in this backend repository. If the consuming application uses one, invalidate/revalidate the detail key equivalent to:

```text
["clinical-history-event", patientId, eventId]
```

Do not introduce a new state library solely for Phase 5.

## 15. Integration with Phase 4 Pre-Triage

### Authenticated primary or managed flow

```text
Complete authenticated Pre-Triage
        ↓
backend atomically creates the patient-owned history event
        ↓
frontend does not create or project history
        ↓
refresh/fetch Clinical History for the selected patient
```

For a primary patient, Phase 4 session start normally omits `patientId`. For a managed patient, it uses the selected authorized PatientProfile UUID. In both cases, successful first completion creates exactly one history event for that patient. Repeated completion does not create duplicates.

The completion response's `episodeId` is the same authoritative episode identity later exposed as history `source.id`, but the frontend still obtains `eventId` through the history list.

### Anonymous flow

```text
Complete anonymous Pre-Triage
        ↓
no PatientProfile owner and no Clinical History event
        ↓
authenticate using the existing Phase 2 flow
        ↓
POST /api/v1/pre-triage/sessions/{sessionId}/claim
with Bearer + original anonymous capability
        ↓
backend assigns the primary patient and creates one history event
        ↓
refresh the primary patient's Clinical History
```

Claim supports only the authenticated Account's server-derived primary patient; it does not claim into a managed patient. Repeating a successful same-patient claim remains idempotent and does not duplicate history.

The frontend must never call a “create history event” endpoint. No such endpoint exists.

## 16. Suggested screens and components

The current contracts map naturally to:

```text
ClinicalHistoryPage
 ├── ClinicalHistoryList
 │    └── ClinicalHistoryItem
 └── LoadMore / InfiniteScroll

ClinicalHistoryEventPage
 ├── OriginalEventMetadataSection
 ├── ProvenanceSection
 ├── AmendmentsList
 └── AddAmendmentForm
```

Suggested responsibilities:

- `ClinicalHistoryPage`: owns patient/filter selection, first-page loading, cursor reset, and generic concealed-`404` handling.
- `ClinicalHistoryList`: renders the backend order without client-side resorting.
- `ClinicalHistoryItem`: routes with both current `patientId` and returned `eventId`.
- `OriginalEventMetadataSection`: displays only event/source fields actually returned.
- `ProvenanceSection`: displays or makes available the frozen source/version references without interpreting them.
- `AmendmentsList`: renders the returned oldest-to-newest order.
- `AddAmendmentForm`: owns transient reason/idempotency state and never accepts author/audit fields.

These are conceptual names, not a requirement to adopt a particular routing or component framework.

## 17. Frontend security requirements

The frontend must:

- send the Beeexy access token only through the established Bearer-authenticated client;
- treat `patientId`, `eventId`, and `episodeId` as identifiers, never authorization;
- use the selected technical PatientProfile UUID, not a Beeexy ID, in patient routes;
- treat every concealed `404` generically;
- discard cursors when patient/filter context changes;
- keep cursor and idempotency values out of analytics unless explicitly approved;
- avoid logging tokens, history bodies, amendment reasons, source data, or other clinical content;
- retain the response correlation ID for privacy-safe support diagnostics;
- use server timestamps and provenance as authoritative;
- rely on backend ordering instead of reordering amendments into a different semantic sequence.

The frontend must not:

- decode or modify cursors;
- expose internal UUIDs unnecessarily in visible UI or analytics;
- send author, Account, patient, timestamp, or provenance fields in amendment bodies;
- overwrite the original event locally;
- infer hidden resource existence from `404`;
- assume an Active relationship will remain active between requests;
- create urgency, diagnosis, disposition, red flags, disease probability, treatment, prescription, or recommendation from Phase 5 data;
- treat questionnaire/rule-set UUIDs as evidence of an authoritative clinical rule execution.

Follow the existing frontend authentication architecture for safe token storage. Phase 5 does not change that architecture.

## 18. What Phase 5 does not provide

Do not implement frontend controls or API calls for:

- creating a Clinical History event directly;
- deleting a Clinical History event;
- editing or overwriting the original Pre-Triage episode;
- updating or deleting an amendment;
- arbitrary JSON Patch or structured correction fields;
- retrieving an amendment by amendment ID;
- retrieving the full source episode by `source.id`;
- additional Clinical History event types;
- AI Conversation History;
- FHIR generation, export, or amendment representation;
- urgency, disposition, diagnosis, red flags, prescriptions, treatment recommendations, or disease probability;
- retention/deletion-right workflows.

Long-term retention/deletion rights, additional event types, exact FHIR amendment representation, and AI Conversation History remain explicitly deferred.

## 19. End-to-end integration examples

### Example A — Load Clinical History

```text
Select active patient
→ GET first page without cursor
→ render returned order
→ nextCursor exists
→ user scrolls or selects Load more
→ GET with the exact returned cursor
→ append unique items
→ repeat until nextCursor is null
```

### Example B — Open detail

```text
User selects a history item
→ retain current patientId
→ read item.eventId
→ GET /patients/{patientId}/clinical-history/{eventId}
→ render event metadata + provenance + amendments
```

### Example C — Create amendment

```text
Open detail
→ read detail.source.id as episodeId
→ user enters reason
→ generate one idempotency UUID
→ POST /pre-triage/episodes/{episodeId}/amendments
→ 201
→ refetch detail
→ replace detail state
→ amendment appears in returned order
```

### Example D — Manager revoked

```text
Manager previously had access
→ relationship is revoked
→ next list/detail/amendment request returns concealed 404
→ close inaccessible view
→ refresh accessible patients
→ show generic “This record is no longer available.”
```

Do not preserve a stale editable copy or tell the manager that the record still exists.

### Example E — Anonymous claim

```text
Complete anonymous Pre-Triage
→ no history event yet
→ authenticate
→ claim with Bearer + original capability through Phase 4
→ receive 200
→ clear anonymous claim state
→ refresh primary-patient Clinical History
→ newly claimed episode appears once
```

## 20. Integration checklist and verified limitations

- [ ] Reuse centralized Bearer injection and coordinated refresh.
- [ ] Use the Phase 3 selected PatientProfile UUID as `patientId`.
- [ ] Fetch the first page with no cursor.
- [ ] Send exact `cursor`, `pageSize`, and `eventType` query names.
- [ ] Restrict `pageSize` to `1–100` and default to `20` when omitted.
- [ ] Support only `COMPLETED_PRE_TRIAGE`.
- [ ] Preserve backend `occurredAt DESC`, `eventId DESC` ordering.
- [ ] Treat `nextCursor` as opaque and discard it across patient/filter changes.
- [ ] Use `eventId` only for the patient-scoped detail route.
- [ ] Use `source.id`, not `eventId`, as amendment `episodeId`.
- [ ] Generate one UUID per logical amendment submission.
- [ ] Retain that UUID across ambiguous retries and `409` reconciliation.
- [ ] Send exactly `idempotencyKey` and `reason` in amendment JSON.
- [ ] Refetch detail after amendment creation.
- [ ] Render amendments oldest to newest without overwriting original metadata.
- [ ] Handle unrelated/revoked/absent resources with one generic `404` UX.
- [ ] Refresh history after authenticated completion or anonymous claim; never create history manually.
- [ ] Render no deferred clinical or FHIR behavior.

All request/response examples and TypeScript fields above were checked against the implemented endpoint DTOs, application validation, authorization repository, cursor codec/query, OpenAPI contract tests, and real PostgreSQL endpoint tests.

Frontend-relevant current limitations are:

1. Phase 5 detail exposes metadata and provenance, not the full original Pre-Triage result.
2. Amendment detail does not expose its idempotency key, so a `409` retry is reconciled by refreshing detail rather than matching a returned key.
3. The amendment `201` response has a `Location` header, but no amendment-detail GET route exists.
4. Only free-text amendment reasons are supported; there is no structured corrected-field/value meaning.

## 21. API reference summary

| Endpoint | Purpose | Auth | Success | Common errors |
|---|---|---|---|---|
| `GET /api/v1/patients/{patientId}/clinical-history` | Cursor-paginated patient timeline | Bearer; primary or Active manager | `200` `ClinicalHistoryPage` | `401`, concealed `404`, validation `422`, `500` |
| `GET /api/v1/patients/{patientId}/clinical-history/{eventId}` | Patient-scoped event, provenance, amendments | Bearer; primary or Active manager | `200` `ClinicalHistoryEventDetail` | `401`, concealed `404`, `500` |
| `POST /api/v1/pre-triage/episodes/{episodeId}/amendments` | Add immutable free-text amendment | Bearer; primary or Active manager | `201` `ClinicalHistoryAmendment` | `401`, concealed `404`, duplicate `409`, validation `422`, `500` |
