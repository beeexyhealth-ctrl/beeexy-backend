# Frontend API Integration — Phase 8

## 1. Purpose

This document is the frontend integration contract for the accepted Phase 8
availability and appointment API. It is written for the Beeexy Next.js
frontend and is based on the current endpoint handlers, application use cases,
DTO mappings, authorization code, Problem Details mappings, OpenAPI assertions,
and Phase 8 acceptance tests.

Statements marked **backend contract** describe required server behavior.
Statements marked **frontend recommendation** describe a safe integration
approach and may be adapted to the frontend's existing API and state libraries.

The frontend must use returned identifiers and scheduling projections. It must
not reproduce server-side publication, directory-eligibility, authorization,
reservation, idempotency-fingerprint, transition, or concurrency logic.

## 2. Phase 8 frontend scope

Phase 8 exposes exactly eight scheduling operations across seven paths:

| Method | Path | Authentication | Purpose | Success |
|---|---|---|---|---|
| `GET` | `/api/v1/doctors/{doctorId}/slots` | Anonymous | Discover available slots | `200` |
| `POST` | `/api/v1/appointments` | Bearer + PatientProfile authority | Book a slot | `201`; identical replay `200` |
| `GET` | `/api/v1/appointments` | Bearer + PatientProfile authority | List accessible appointments | `200` |
| `GET` | `/api/v1/appointments/{id}` | Bearer + PatientProfile authority | Read detail and histories | `200` |
| `POST` | `/api/v1/appointments/{id}/confirm` | Bearer + clinic-scoped scheduler | Confirm a request | `200` |
| `POST` | `/api/v1/appointments/{id}/reject` | Bearer + clinic-scoped scheduler | Reject a request | `200` |
| `POST` | `/api/v1/appointments/{id}/cancel` | Bearer + PatientProfile authority | Cancel an appointment | `200` |
| `POST` | `/api/v1/appointments/{id}/reschedule` | Bearer + PatientProfile authority | Move to another slot | `200` |

OpenAPI contains 43 total API paths. Phase 8 does not expose availability
administration, scheduler administration, `Completed`/`NoShow` transition
operations, payments, calendar synchronization, video meetings, or FHIR
Appointment operations.

## 3. Authentication and authorization model

### Patient operations

Booking, appointment list/detail, cancellation, and rescheduling require the
normal Bearer access token. The authenticated account must currently have
authority over the appointment's `patientId` through either:

- ownership of that PatientProfile; or
- an active authorized manager/dependent relationship.

Authority is evaluated by the backend on every operation. Revocation removes
access immediately. The account that originally booked an appointment does
not retain access merely because it is stored as the requesting account.

Missing or invalid authentication returns `401`. Missing and inaccessible
patient-scoped resources intentionally share concealed `404` behavior, usually
with `scheduling.appointment_target_not_found`. The frontend must not try to
distinguish “missing” from “not authorized.”

PatientProfile switching rules:

- use the currently selected accessible `patientId` for booking and filtered
  lists;
- discard patient-bound cursors and cached pages when the selected patient
  changes;
- do not infer access from a previously cached relationship;
- treat a new concealed `404` as possible revocation and refresh the accessible
  PatientProfile context.

### Scheduler operations

Confirm and reject use a different authority boundary. The account must be an
explicitly configured `AppointmentScheduler` for the appointment's current
clinic. The permission is authenticated, fail-closed, and clinic-scoped.

- non-scheduler: `403`;
- scheduler assigned to another clinic: `403`;
- scheduler assigned to this clinic: operation may proceed;
- nonexistent appointment: `404`;
- invalid state: `409`.

Scheduler permission grants no patient-operation or clinical-data access. If
the current frontend has no scheduler-facing surface, document and type these
operations but do not expose them as patient actions. Phase 8 does not provide
a clinic portal or scheduler-assignment API.

### Public availability

Slot discovery is anonymous. It must not be gated behind authentication. A
shared API client may attach its normal session header, but no token is needed.

## 4. Shared TypeScript contracts

These contracts match the serialized JSON property names. UUID and timestamp
aliases improve readability but remain strings at runtime.

```ts
export type Uuid = string;
export type IsoInstant = string;
export type IanaTimeZone = string;

export type AppointmentStatus =
  | "Requested"
  | "Confirmed"
  | "Cancelled"
  | "Completed"
  | "NoShow"
  | "Rejected";

export type AppointmentModality = "inPerson" | "virtual";

export interface AvailabilitySlot {
  slotId: Uuid;
  doctorId: Uuid;
  clinicId: Uuid;
  locationId: Uuid;
  startsAt: IsoInstant;
  endsAt: IsoInstant;
  clinicTimeZone: IanaTimeZone;
  modality: AppointmentModality;
}

export interface RequestAppointmentRequest {
  patientId: Uuid;
  slotId: Uuid;
  modality: AppointmentModality;
  // JSON null is accepted, but omission is the recommended representation.
  reason?: string | null;
  idempotencyKey: Uuid;
}

export interface AppointmentSummary {
  appointmentId: Uuid;
  patientId: Uuid;
  slotId: Uuid;
  doctorId: Uuid;
  clinicId: Uuid;
  locationId: Uuid;
  status: AppointmentStatus;
  modality: AppointmentModality;
  startsAt: IsoInstant;
  endsAt: IsoInstant;
  clinicTimeZone: IanaTimeZone;
  createdAt: IsoInstant;
}

export interface RequestAppointmentResponse extends AppointmentSummary {
  // Omitted when no reason was supplied; the backend does not emit reason: null.
  reason?: string;
}

export type AppointmentHistoryActorType =
  | "patientAuthority"
  | "appointmentScheduler";

export type AppointmentStatusAction =
  | "creation"
  | "confirmation"
  | "rejection"
  | "cancellation"
  | "completion"
  | "noShow";

export interface AppointmentStatusHistoryEntry {
  sequence: number;
  previousStatus: AppointmentStatus | null;
  newStatus: AppointmentStatus;
  actorType: AppointmentHistoryActorType;
  action: AppointmentStatusAction;
  occurredAt: IsoInstant;
}

export interface AppointmentRescheduleHistoryEntry {
  previousSlotId: Uuid;
  newSlotId: Uuid;
  occurredAt: IsoInstant;
}

export interface AppointmentDetail extends AppointmentSummary {
  // Omitted when absent.
  reason?: string;
  statusHistory: AppointmentStatusHistoryEntry[];
  rescheduleHistory: AppointmentRescheduleHistoryEntry[];
}

export interface AppointmentPage {
  items: AppointmentSummary[];
  nextCursor: string | null;
}

export interface RescheduleAppointmentRequest {
  slotId: Uuid;
}

export interface AvailabilityQuery {
  from?: IsoInstant;
  to?: IsoInstant;
}

export interface AppointmentListQuery {
  patientId?: Uuid;
  status?: AppointmentStatus;
  from?: IsoInstant;
  to?: IsoInstant;
  cursor?: string;
  pageSize?: number;
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

Do not add backend-only `version`, requesting-account, actor-account,
idempotency-fingerprint, database, constraint, or publication fields to these
contracts.

## 5. Appointment statuses and modalities

Status values are Pascal-cased and case-sensitive. Modality values are
camel-cased and case-sensitive.

| Value | Meaning in Phase 8 | Reserves slot |
|---|---|---:|
| `Requested` | Waiting for clinic scheduler decision | Yes |
| `Confirmed` | Clinic scheduler confirmed | Yes |
| `Cancelled` | Patient authority cancelled | No |
| `Rejected` | Clinic scheduler rejected | No |
| `Completed` | Historical domain status; no Phase 8 action endpoint | No |
| `NoShow` | Historical domain status; no Phase 8 action endpoint | No |

Modalities are `inPerson` and `virtual`. `virtual` does not imply that Beeexy
creates a video meeting. Booking and rescheduling require the appointment
modality to equal the selected slot modality.

## 6. Availability API

### Request

```http
GET /api/v1/doctors/71030000-0000-4000-8000-000000000001/slots?from=2026-09-01T00%3A00%3A00%2B00%3A00&to=2026-09-15T00%3A00%3A00%2B00%3A00
Accept: application/json
```

**Backend contract:** `GET /api/v1/doctors/{doctorId}/slots` is anonymous.
`doctorId` is a Phase 7 doctor UUID.

Declared response statuses: `200`, `404`, `422`, `500`.

| Query | Type | Required | Behavior |
|---|---|---:|---|
| `from` | ISO-8601 instant | No | Inclusive scheduled-start boundary |
| `to` | ISO-8601 instant | No | Exclusive scheduled-start boundary |

Range behavior:

- both omitted: `[backend now, backend now + 30 days)`;
- `from` supplied and `to` omitted: `[from, from + 30 days)`;
- `to` supplied and `from` omitted: `[backend now, to)`;
- maximum requested range: 90 days;
- `from >= to`: `422`;
- repeated `from` or `to`: `422`;
- unsupported query names: `422`;
- valid range entirely in the past: `200` with `[]`;
- returned slots always satisfy `startsAt > backend now`, even when `from` is
  earlier.

The endpoint returns only published slots whose doctor, clinic, location, and
affiliation remain publicly eligible. Slots reserved by `Requested` or
`Confirmed` appointments are excluded. `Cancelled` and `Rejected`
appointments release their slots.

Ordering is `startsAt` ascending, then `slotId` ascending for ties. Do not sort
by localized display strings.

### `200 OK`

The response is a bare array, not a page envelope:

```json
[
  {
    "slotId": "82000000-0000-4000-8000-000000000001",
    "doctorId": "71030000-0000-4000-8000-000000000001",
    "clinicId": "71020000-0000-4000-8000-000000000001",
    "locationId": "71021000-0000-4000-8000-000000000001",
    "startsAt": "2026-09-10T14:00:00+00:00",
    "endsAt": "2026-09-10T14:30:00+00:00",
    "clinicTimeZone": "America/Lima",
    "modality": "inPerson"
  }
]
```

No availability is `200` with `[]`. Missing, empty-UUID, or unpublished doctor
is `404` with the generic safe doctor-not-found Problem Details. Relevant
validation codes are:

- `availability.range_invalid` — invalid, repeated, or over-90-day range;
- `availability.filter_unsupported` — unknown query parameter.

### Frontend behavior

Render clinic-local date/time from `startsAt`/`endsAt` using
`clinicTimeZone`. Use `slotId` for booking; never reconstruct a slot identity
from its date. A `409` during later booking means the discovery result became
stale: refetch slots and ask the user to select again.

Cache slots by doctor and normalized range. Invalidate/refetch affected slot
queries after booking, rejection, cancellation, or rescheduling.

## 7. Create appointment

### Request

```http
POST /api/v1/appointments
Authorization: Bearer <access-token>
Content-Type: application/json
```

```json
{
  "patientId": "31000000-0000-4000-8000-000000000001",
  "slotId": "82000000-0000-4000-8000-000000000001",
  "modality": "inPerson",
  "reason": "Follow-up visit",
  "idempotencyKey": "93000000-0000-4000-8000-000000000001"
}
```

`patientId`, `slotId`, `modality`, and `idempotencyKey` are required.
`reason` is optional. When present it is trimmed, must contain non-whitespace
text, and may contain at most 500 characters. Unknown body properties are
rejected with `422 scheduling.unsupported_field`.

Declared response statuses: `200`, `201`, `400`, `401`, `404`, `409`, `422`,
`500`.

The server loads all doctor, clinic, location, time, timezone, and publication
data from `slotId`. The request's modality must equal that slot's modality.

### Idempotency

For each new logical booking attempt, generate a fresh UUID, for example with
`crypto.randomUUID()`. Retain it with the pending mutation and reuse the same
key and semantic request when retrying after a timeout, connection loss, or
other uncertainty about whether the server committed.

Do not create a new key merely because the first response was lost. Do not
reuse a key for a different patient, slot, modality, or reason. The backend
computes and stores its own canonical SHA-256 fingerprint; the frontend neither
sends nor reproduces it.

Idempotency is scoped to authenticated account plus key and does not expire in
Phase 8. An exact replay returns the original logical appointment with `200`.
First creation returns `201` and a `Location` header pointing to the detail
route. Incompatible reuse returns `409 scheduling.idempotency_key_reused`.

### Success response

```json
{
  "appointmentId": "94000000-0000-4000-8000-000000000001",
  "patientId": "31000000-0000-4000-8000-000000000001",
  "slotId": "82000000-0000-4000-8000-000000000001",
  "doctorId": "71030000-0000-4000-8000-000000000001",
  "clinicId": "71020000-0000-4000-8000-000000000001",
  "locationId": "71021000-0000-4000-8000-000000000001",
  "status": "Requested",
  "modality": "inPerson",
  "startsAt": "2026-09-10T14:00:00+00:00",
  "endsAt": "2026-09-10T14:30:00+00:00",
  "clinicTimeZone": "America/Lima",
  "reason": "Follow-up visit",
  "createdAt": "2026-09-01T18:00:00+00:00"
}
```

`reason` is omitted when absent. Every new appointment starts `Requested` and
immediately reserves its slot.

### Expected errors and refresh

- inaccessible/revoked `patientId`, missing/ineligible slot: concealed `404`
  with `scheduling.appointment_target_not_found`;
- occupied slot/race: `409 scheduling.slot_reserved`;
- incompatible key reuse: `409 scheduling.idempotency_key_reused`;
- expired slot: `422 scheduling.slot_expired`;
- unpublished slot: `422 scheduling.slot_unbookable`;
- modality mismatch: `422 scheduling.modality_mismatch`;
- invalid modality: `422 scheduling.modality_invalid`;
- blank/overlong reason: `422 scheduling.reason_invalid`;
- empty required UUID: `422 scheduling.identifiers_required`;
- malformed JSON/type binding: generic `400`.

After success, refresh the selected patient's appointment list and the
selected doctor's availability. On `scheduling.slot_reserved`, refresh slots
before showing alternatives. Preserve the idempotency key while retrying the
same uncertain logical mutation. Clear it after definitive success or a
deterministic client/conflict response; retain it across a transport failure or
other outcome where commit status remains unknown.

## 8. List appointments

### Request and filters

```http
GET /api/v1/appointments?patientId=31000000-0000-4000-8000-000000000001&status=Confirmed&from=2026-09-01T00%3A00%3A00%2B00%3A00&pageSize=20
Authorization: Bearer <access-token>
```

| Query | Type | Required | Semantics |
|---|---|---:|---|
| `patientId` | UUID | No | Restrict to one currently accessible PatientProfile |
| `status` | `AppointmentStatus` | No | Exact case-sensitive status |
| `from` | ISO-8601 instant | No | `startsAt >= from` |
| `to` | ISO-8601 instant | No | `startsAt < to` |
| `cursor` | string | No | Opaque keyset cursor |
| `pageSize` | integer | No | Default `20`; range `1..100` |

Declared response statuses: `200`, `400`, `401`, `404`, `422`, `500`.

Without `patientId`, results include appointments owned by the account's
primary PatientProfile and appointments for profiles it actively manages.
Without time filters, the backend does not impose an upcoming-only window;
historical `Cancelled` and `Rejected` records may be returned.

Filters use AND semantics. The temporal range applies to scheduled start, not
interval overlap. `from` and `to` are independent optional filters, except
that when both are supplied `from` must be less than `to`.

Unknown query names and repeated parameters are rejected. Ordering is
`startsAt` ascending, then `appointmentId` ascending. The frontend must retain
backend ordering.

### `200 OK`

```json
{
  "items": [
    {
      "appointmentId": "94000000-0000-4000-8000-000000000001",
      "patientId": "31000000-0000-4000-8000-000000000001",
      "slotId": "82000000-0000-4000-8000-000000000001",
      "doctorId": "71030000-0000-4000-8000-000000000001",
      "clinicId": "71020000-0000-4000-8000-000000000001",
      "locationId": "71021000-0000-4000-8000-000000000001",
      "status": "Confirmed",
      "modality": "inPerson",
      "startsAt": "2026-09-10T14:00:00+00:00",
      "endsAt": "2026-09-10T14:30:00+00:00",
      "clinicTimeZone": "America/Lima",
      "createdAt": "2026-09-01T18:00:00+00:00"
    }
  ],
  "nextCursor": null
}
```

List items never expose `reason` or history.

Relevant errors:

- inaccessible `patientId`: concealed `404` with
  `scheduling.appointment_target_not_found`;
- unsupported query: `422 scheduling.appointment_filter_unsupported`;
- repeated query: `422 scheduling.appointment_filter_invalid`;
- invalid status/casing: `422 scheduling.appointment_status_invalid`;
- invalid `from`/`to`: `422 scheduling.appointment_range_invalid`;
- invalid page size: `422 scheduling.appointment_page_size_invalid`;
- malformed, stale, filter-mismatched, or tampered cursor: `422` with
  `scheduling.appointment_cursor_invalid`.

## 9. Appointment detail

### Request

```http
GET /api/v1/appointments/94000000-0000-4000-8000-000000000001
Authorization: Bearer <access-token>
```

The authenticated account must currently own or actively manage the
appointment's PatientProfile. Missing, inaccessible, revoked, and empty IDs
use concealed `404 scheduling.appointment_target_not_found` semantics.

`id` is the Appointment UUID. Declared response statuses: `200`, `401`, `404`,
`500`.

### `200 OK`

```json
{
  "appointmentId": "94000000-0000-4000-8000-000000000001",
  "patientId": "31000000-0000-4000-8000-000000000001",
  "slotId": "82000000-0000-4000-8000-000000000001",
  "doctorId": "71030000-0000-4000-8000-000000000001",
  "clinicId": "71020000-0000-4000-8000-000000000001",
  "locationId": "71021000-0000-4000-8000-000000000001",
  "status": "Confirmed",
  "modality": "inPerson",
  "startsAt": "2026-09-10T14:00:00+00:00",
  "endsAt": "2026-09-10T14:30:00+00:00",
  "clinicTimeZone": "America/Lima",
  "reason": "Follow-up visit",
  "createdAt": "2026-09-01T18:00:00+00:00",
  "statusHistory": [
    {
      "sequence": 1,
      "previousStatus": null,
      "newStatus": "Requested",
      "actorType": "patientAuthority",
      "action": "creation",
      "occurredAt": "2026-09-01T18:00:00+00:00"
    },
    {
      "sequence": 2,
      "previousStatus": "Requested",
      "newStatus": "Confirmed",
      "actorType": "appointmentScheduler",
      "action": "confirmation",
      "occurredAt": "2026-09-01T18:05:00+00:00"
    }
  ],
  "rescheduleHistory": [
    {
      "previousSlotId": "82000000-0000-4000-8000-000000000002",
      "newSlotId": "82000000-0000-4000-8000-000000000001",
      "occurredAt": "2026-09-01T18:03:00+00:00"
    }
  ]
}
```

`reason` is omitted when absent. Histories are always arrays. Raw actor account
IDs are never returned.

The detail scheduling fields are authoritative for current rendering. A
reschedule changes `slotId`, doctor/clinic/location projection, time, and
possibly clinic timezone while preserving `appointmentId` and status.

Historical detail remains readable to an authorized patient if related
directory entities are later unpublished. Do not hide an existing appointment
only because a fresh Phase 7 public-directory lookup no longer returns its
doctor or clinic; render the scheduling projection returned by this endpoint.

## 10. Confirm appointment

```http
POST /api/v1/appointments/94000000-0000-4000-8000-000000000001/confirm
Authorization: Bearer <scheduler-access-token>
```

There is no request body. An assigned scheduler applies
`Requested -> Confirmed`. Confirmation keeps the slot reserved. An authorized
repeat against an already `Confirmed` appointment is idempotent and returns
`200` without a duplicate history entry.

`id` is the Appointment UUID. Declared response statuses: `200`, `401`, `403`,
`404`, `409`, `500`.

The response is `AppointmentSummary`, with status `Confirmed`; it does not
include reason or histories. Refetch detail to show the new status-history
entry.

Errors:

- non-scheduler or wrong clinic: `403` with
  `scheduling.appointment_scheduler_forbidden`;
- missing appointment: `404 scheduling.appointment_target_not_found`;
- `Rejected`, `Cancelled`, `Completed`, `NoShow`, or incompatible concurrent
  result: `409 scheduling.appointment_transition_conflict`.

After success, refresh appointment detail and relevant appointment lists. The
slot remains unavailable, so an availability refetch is optional rather than
required for correctness.

Do not show this operation in a normal patient interface.

## 11. Reject appointment

```http
POST /api/v1/appointments/94000000-0000-4000-8000-000000000001/reject
Authorization: Bearer <scheduler-access-token>
```

There is no request body. An assigned scheduler applies
`Requested -> Rejected`. Rejection retains the Appointment and releases the
slot. An authorized repeat against an already `Rejected` appointment is
idempotent and returns `200` without duplicate history.

`id` is the Appointment UUID. Declared response statuses: `200`, `401`, `403`,
`404`, `409`, `500`.

The response is `AppointmentSummary`, with status `Rejected`. Authorization
and conflict codes are the same as confirmation:

- `403 scheduling.appointment_scheduler_forbidden`;
- `404 scheduling.appointment_target_not_found`;
- `409 scheduling.appointment_transition_conflict`.

After success, refresh detail, relevant appointment lists, and availability
for the returned `doctorId`. Do not show this operation in a normal patient
interface.

## 12. Cancel appointment

```http
POST /api/v1/appointments/94000000-0000-4000-8000-000000000001/cancel
Authorization: Bearer <access-token>
```

There is no request body. Current owner or active-manager authority is
required. Allowed transitions are:

`id` is the Appointment UUID. Declared response statuses: `200`, `401`, `404`,
`409`, `500`.

- `Requested -> Cancelled`;
- `Confirmed -> Cancelled`.

An authorized repeat when already `Cancelled` is idempotent and returns `200`.
`Rejected`, `Completed`, and `NoShow` return `409` with
`scheduling.appointment_transition_conflict`. Cancellation retains the
Appointment and history and releases the slot.

The response is `AppointmentSummary` with `status: "Cancelled"`. Missing or
inaccessible appointments return concealed `404` with
`scheduling.appointment_target_not_found`.

Phase 8 has no minimum cancellation window, cancellation fee, payment effect,
or deletion behavior. Do not add frontend policy that contradicts this
contract unless a later approved backend phase changes it.

After success, refresh:

- appointment detail;
- all relevant appointment-list pages;
- availability for the response's `doctorId` and relevant range.

## 13. Reschedule appointment

### Request

```http
POST /api/v1/appointments/94000000-0000-4000-8000-000000000001/reschedule
Authorization: Bearer <access-token>
Content-Type: application/json
```

```json
{
  "slotId": "82000000-0000-4000-8000-000000000009"
}
```

`slotId` is the only accepted body field. Do not send doctor, clinic,
location, timestamps, timezone, modality, status, appointment ID, version, or
history actor. An empty ID produces `422 scheduling.identifiers_required`;
unknown body fields produce `422 scheduling.unsupported_field`.

Path `id` is the Appointment UUID. Declared response statuses: `200`, `400`,
`401`, `404`, `409`, `422`, `500`.

Current owner or active-manager authority is required. Rescheduling is allowed
only while status is `Requested` or `Confirmed`:

- `Requested` remains `Requested`;
- `Confirmed` remains `Confirmed`;
- `appointmentId` is unchanged.

The target slot is authoritative for doctor, clinic, location, starts/ends,
and clinic timezone. Cross-doctor and cross-clinic movement is allowed when
the target remains publicly eligible and modality-compatible. The existing
appointment modality does not change.

Targeting the current slot is a safe `200` no-op: no reservation churn,
version increment, status history, or reschedule history is created.

Successful rescheduling atomically reserves the new slot, releases the old
slot, and appends one separate reschedule-history record. It does not append a
status-history entry. If the target cannot be reserved, the original slot and
appointment remain unchanged.

The response is the updated `AppointmentSummary`; reason and histories are not
included.

Errors:

- missing/inaccessible appointment or missing/ineligible target: concealed
  `404 scheduling.appointment_target_not_found`;
- occupied target/race: `409 scheduling.slot_reserved`;
- invalid source status or incompatible appointment race: `409` with
  `scheduling.appointment_reschedule_conflict`;
- expired target: `422 scheduling.slot_expired`;
- unpublished target: `422 scheduling.slot_unbookable`;
- modality mismatch: `422 scheduling.modality_mismatch`;
- malformed JSON/type binding: generic `400`.

Recommended frontend flow:

1. Retain the current summary so its old `doctorId` and range are available for
   invalidation.
2. Load eligible slots for the desired doctor through the public slot API.
3. Submit only the selected `slotId`.
4. On success, replace/refetch detail and appointment-list data.
5. Refetch availability for both the old and returned new doctor/range when
   they differ.
6. On `scheduling.slot_reserved`, refresh slots and ask the user to choose
   again.
7. On `scheduling.appointment_reschedule_conflict`, reload appointment detail
   before deciding whether the action is still available.

There is no Phase 8 reschedule count limit, minimum lead time, fee, payment
effect, or second clinic approval step.

## 14. Pagination and filtering

Appointment cursors are backend-owned opaque Base64URL strings bound to
patient, status, and time filters plus the boundary appointment. Never decode,
parse, modify, reconstruct, display, or use them as business identifiers.

Rules:

1. Start without `cursor`.
2. Append `items` in returned order.
3. If `nextCursor` is non-null, send it unchanged with the same filters.
4. Stop when `nextCursor === null`.
5. Discard the cursor and accumulated pages when patient/status/time filters
   change.
6. `pageSize` is not part of cursor filter identity, but keeping it stable is
   recommended.
7. On `scheduling.appointment_cursor_invalid`, discard traversal state and
   reload the first page rather than trying to repair the cursor.

Example “Load more” logic:

```ts
async function loadAllAppointments(
  baseQuery: Omit<AppointmentListQuery, "cursor">,
  signal?: AbortSignal,
): Promise<AppointmentSummary[]> {
  const items: AppointmentSummary[] = [];
  let cursor: string | undefined;

  do {
    const page = await listAppointments({ ...baseQuery, cursor }, signal);
    items.push(...page.items);
    cursor = page.nextCursor ?? undefined;
  } while (cursor);

  return items;
}
```

For an infinite-query UI, use the query's complete normalized filter object as
part of the cache key and `nextCursor` as the next-page parameter.

## 15. Status history

`statusHistory` appears only in appointment detail. It is ordered by ascending
`sequence` and is immutable.

- creation is sequence `1`, `previousStatus: null`, `newStatus: "Requested"`,
  `actorType: "patientAuthority"`, and `action: "creation"`;
- each applied confirmation, rejection, or cancellation adds one entry;
- same-action retries add none;
- invalid/conflicting transitions add none;
- rescheduling adds none.

Render in returned order. Use `actorType` for safe role wording such as
“Patient/manager” or “Clinic scheduler”; no actor account ID is available.
Do not infer missing history entries from status changes cached elsewhere.

`completion` and `noShow` are representable historical action strings, but
Phase 8 provides no operations to create those transitions.

## 16. Reschedule history

`rescheduleHistory` also appears only in appointment detail. It is ordered by
`occurredAt`, then a stable backend record-ID tie-breaker that is not exposed.
Each entry contains only `previousSlotId`, `newSlotId`, and `occurredAt`.

It is a separate audit stream because a reschedule does not change status.
The frontend may present entries as scheduling changes, but should not invent
old/new time or provider snapshots that the API does not return. If richer old
slot presentation is needed later, it requires an approved contract change;
do not assume old slots remain publicly discoverable.

## 17. Error handling and Problem Details

Errors use `application/problem+json`. `instance` is the request path and
`correlationId` is safe to show in a support/debug UI. `errorCode` is present
for mapped scheduling errors but is optional for framework-generated `400`,
authentication `401`, generic doctor `404`, and unexpected `500` responses.

```json
{
  "title": "Availability slot conflict.",
  "status": 409,
  "detail": "The selected availability slot is already reserved.",
  "instance": "/api/v1/appointments",
  "errorCode": "scheduling.slot_reserved",
  "correlationId": "01H..."
}
```

Use `status` and `errorCode` for behavior. Do not show raw `detail` without the
frontend's normal safe/localized-message policy, and never expose exception or
SQL text.

| HTTP / code | Meaning | Recommended frontend behavior |
|---|---|---|
| `400` | Malformed JSON or binding | Keep form state; show generic malformed-request feedback |
| `401` | Missing/expired/invalid authentication | Run existing reauthentication/session flow |
| `403 scheduling.appointment_scheduler_forbidden` | Not assigned to appointment clinic | Hide scheduler action; show permission error; do not retry as patient |
| `404` doctor not found | Missing/unpublished doctor | Show directory not-found state |
| `404 scheduling.appointment_target_not_found` | Missing/concealed patient, appointment, or scheduling target | Show not-found/unavailable; refresh PatientProfile authority if appropriate |
| `409 scheduling.slot_reserved` | Slot taken, including a booking/reschedule race | Refetch slots and require a new selection |
| `409 scheduling.idempotency_key_reused` | Key reused with different booking semantics | Stop retry; create a new logical attempt/key only after user reconfirms |
| `409 scheduling.appointment_transition_conflict` | Invalid/opposite/concurrent confirm/reject/cancel | Reload detail/list and recompute visible actions |
| `409 scheduling.appointment_reschedule_conflict` | Invalid source state or incompatible reschedule race | Reload detail and slots before retrying |
| `422 availability.range_invalid` | Invalid/repeated/overlong slot range | Correct date range locally |
| `422 availability.filter_unsupported` | Unknown availability query | Treat as client integration bug |
| `422 scheduling.unsupported_field` | Unknown booking/reschedule body field | Treat as contract/client-version bug |
| `422 scheduling.identifiers_required` | Empty required UUID | Block submit and correct client state |
| `422 scheduling.modality_invalid` | Unknown modality string | Correct client enum mapping |
| `422 scheduling.modality_mismatch` | Requested modality differs from slot/appointment | Refresh slot and use returned modality |
| `422 scheduling.reason_invalid` | Blank or over-500-character reason | Show field validation |
| `422 scheduling.slot_expired` | Slot is no longer future | Refetch availability |
| `422 scheduling.slot_unbookable` | Slot is unpublished/unbookable | Refetch availability |
| `422 scheduling.appointment_filter_unsupported` | Unknown list query | Treat as client integration bug |
| `422 scheduling.appointment_filter_invalid` | Repeated list query | Correct query serialization |
| `422 scheduling.appointment_status_invalid` | Invalid status/casing | Correct enum mapping |
| `422 scheduling.appointment_range_invalid` | Invalid appointment range | Correct filters locally |
| `422 scheduling.appointment_page_size_invalid` | Page size outside `1..100` | Clamp/control page size |
| `422 scheduling.appointment_cursor_invalid` | Malformed/stale/mismatched cursor | Discard pages/cursor and restart |
| `500` | Safe unexpected failure | Show retry/support state with correlation ID |

## 18. Recommended frontend API client

No frontend source tree is present in this backend repository. Adapt these
signatures to the existing Beeexy fetch wrapper, authentication injection,
Problem Details parser, and `AbortSignal` conventions. Do not add a new HTTP or
state library solely for Phase 8.

```ts
export interface Phase8SchedulingApi {
  listDoctorSlots(
    doctorId: Uuid,
    query?: AvailabilityQuery,
    signal?: AbortSignal,
  ): Promise<AvailabilitySlot[]>;

  createAppointment(
    request: RequestAppointmentRequest,
    signal?: AbortSignal,
  ): Promise<RequestAppointmentResponse>;

  listAppointments(
    query?: AppointmentListQuery,
    signal?: AbortSignal,
  ): Promise<AppointmentPage>;

  getAppointment(
    appointmentId: Uuid,
    signal?: AbortSignal,
  ): Promise<AppointmentDetail>;

  confirmAppointment(
    appointmentId: Uuid,
    signal?: AbortSignal,
  ): Promise<AppointmentSummary>;

  rejectAppointment(
    appointmentId: Uuid,
    signal?: AbortSignal,
  ): Promise<AppointmentSummary>;

  cancelAppointment(
    appointmentId: Uuid,
    signal?: AbortSignal,
  ): Promise<AppointmentSummary>;

  rescheduleAppointment(
    appointmentId: Uuid,
    request: RescheduleAppointmentRequest,
    signal?: AbortSignal,
  ): Promise<AppointmentSummary>;
}
```

Serialization rules:

- omit optional query/body properties rather than sending `undefined`, empty
  strings, or `reason: null`;
- use `URLSearchParams` or the existing equivalent;
- serialize each query name at most once;
- serialize `Date` values as complete ISO instants, but prefer keeping API
  timestamp values as strings until display formatting;
- send Bearer authentication for every operation except slot discovery;
- send no request body for confirm, reject, or cancel;
- propagate `AbortSignal` so route/filter changes cancel obsolete reads;
- distinguish booking `201` from replay `200` only if the UX needs to know
  whether creation was new; both bodies use the same contract.

## 19. React/Next.js state and cache behavior

Use the frontend's existing cache/state library. Example logical cache keys:

- `doctorSlots(doctorId, from, to)`;
- `appointments(patientId?, status?, from?, to?)`;
- `appointment(appointmentId)`.

Mutation refresh matrix:

| Mutation | Detail | Lists | Old slots | New/current slots |
|---|---:|---:|---:|---:|
| Booking | Seed summary and fetch detail when opened | Refresh | N/A | Refresh selected doctor |
| Confirm | Refresh | Refresh | N/A | Slot stays hidden; optional refresh |
| Reject | Refresh | Refresh | N/A | Refresh returned doctor |
| Cancel | Refresh | Refresh | N/A | Refresh returned doctor |
| Reschedule | Refresh | Refresh | Refresh captured old doctor/range | Refresh returned new doctor/range |

Avoid optimistic reservation changes unless the existing frontend can roll
them back reliably. Slot races are normal: server success is authoritative.
For cancellation or rescheduling, disable duplicate submit controls while the
request is in flight, while still accepting backend idempotent results after a
network retry.

On PatientProfile switch:

1. cancel old patient-bound requests;
2. discard their appointment cursors and accumulated pages;
3. select a new patient-specific cache key;
4. refetch rather than relabeling cached records.

## 20. UI state-transition rules

Action visibility is a UX aid only. The backend remains authoritative for
state and authorization.

| Status | Patient cancel | Patient reschedule | Scheduler confirm | Scheduler reject |
|---|---|---|---|---|
| `Requested` | Yes | Yes | Yes | Yes |
| `Confirmed` | Yes | Yes | Idempotent retry only; hide as new action | No; `409` |
| `Cancelled` | Idempotent retry only; hide as new action | No; `409` | No; `409` | No; `409` |
| `Rejected` | No; `409` | No; `409` | No; `409` | Idempotent retry only; hide as new action |
| `Completed` | No Phase 8 action | No; `409` | No; `409` | No; `409` |
| `NoShow` | No Phase 8 action | No; `409` | No; `409` | No; `409` |

Do not render scheduler actions merely because an appointment is `Requested`;
the UI must also be in an explicitly scheduler-facing context. There is no
Phase 8 endpoint for the frontend to administer or infer scheduler assignment.

After a transition `409`, refetch detail before showing actions again because
another caller may have changed the appointment.

## 21. Timezone handling

`startsAt`, `endsAt`, `createdAt`, and history timestamps are authoritative
instants. `clinicTimeZone` is an IANA timezone such as `America/Lima` or
`America/New_York`.

The browser timezone is not necessarily the clinic timezone. When describing
the booked clinic time, format the returned instant in `clinicTimeZone`:

```ts
export function formatClinicTime(
  instant: IsoInstant,
  clinicTimeZone: IanaTimeZone,
  locale: string,
): string {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
    timeZone: clinicTimeZone,
    timeZoneName: "short",
  }).format(new Date(instant));
}
```

Optionally show the user's device-local time as a secondary aid only if the
existing product UX already supports it. Label both zones clearly.

Never strip offsets, reinterpret timestamps as server/browser-local time, or
send reconstructed times to book/reschedule. Daylight-saving transitions can
skip or repeat local times; the returned instant plus IANA zone is the complete
meaning. Slot selection and mutations always use `slotId`.

## 22. Security and privacy requirements

Scheduling does not grant access to or share:

- Pre-Triage;
- Clinical History;
- diagnosis or urgency;
- clinical notes;
- FHIR exports/resources;
- clinical consent;
- unrelated patient medical data.

Booking does not imply that symptoms or history were shared with a doctor or
clinic. Scheduler permission does not grant patient-detail or clinical access.

Appointment `reason` is sensitive:

- never put it in analytics, telemetry, breadcrumbs, correlation IDs, URLs, or
  client logs;
- do not include it in list-card analytics events;
- do not persist appointment request/detail payloads unnecessarily in
  `localStorage`, `sessionStorage`, IndexedDB, or service-worker caches;
- clear abandoned booking draft data according to existing sensitive-data
  conventions;
- show it only within an authorized patient detail/booking context.

Do not expose or attempt to derive backend request fingerprints or concurrency
versions. An idempotency key is not an authorization token, but it should still
remain within mutation state rather than analytics or URLs. Never log Bearer
tokens or full request/response bodies.

## 23. Integration with Phase 7 directory

Phase 8 references Phase 7 directory identities rather than duplicating them:

```text
Doctor directory
  -> Doctor detail
  -> GET /doctors/{doctorId}/slots
  -> select authoritative slotId
  -> POST /appointments
  -> appointment list/detail
  -> cancel or reschedule
```

Use the Phase 7 `doctorId` to discover slots. Each slot supplies authoritative
`clinicId`, `locationId`, times, timezone, and modality. Appointment responses
repeat the current scheduling identifiers so existing Phase 7 doctor/clinic
navigation can be reused.

Do not merge slot metadata into or overwrite Phase 7 directory models. Do not
require N+1 doctor/clinic detail calls merely to render appointment time and
timezone; the scheduling projection already contains those fields. Fetch a
Phase 7 detail only when the UI needs additional public directory content.

An authorized historical appointment remains renderable when its doctor or
clinic is no longer public. Treat appointment detail as the authority for that
historical scheduling record rather than turning Phase 7 lookup failure into
appointment-not-found.

## 24. Suggested frontend implementation sequence

1. Add the Phase 8 TypeScript contracts and runtime response validation if the
   frontend already uses it.
2. Add the eight API-client functions using the existing fetch/auth/error
   conventions.
3. Integrate anonymous slot discovery into Phase 7 doctor detail.
4. Implement booking with UUID idempotency state and definitive-versus-unknown
   response handling.
5. Implement patient appointment list filters and opaque cursor pagination.
6. Implement appointment detail with separate status and reschedule histories.
7. Implement patient cancellation and its cache invalidation.
8. Implement slot-based rescheduling and old/new availability refresh.
9. Add scheduler confirm/reject only if an explicitly scheduler-facing UI is
   already in product scope; do not invent a clinic portal.
10. Add frontend contract, error, action-visibility, timezone/DST, cache, and
    network-uncertainty tests.
11. Validate generated requests and parsed responses against backend OpenAPI
    and a running accepted Phase 8 backend.

## 25. Acceptance checklist

- [ ] All eight Phase 8 operations are represented in the frontend API layer.
- [ ] Slot discovery works anonymously and supports exact `from`/`to` rules.
- [ ] Availability is rendered using returned instants and `clinicTimeZone`.
- [ ] Booking sends exactly `patientId`, `slotId`, `modality`, optional
      `reason`, and `idempotencyKey`.
- [ ] A new logical booking uses a new UUID; uncertain retries reuse the same
      UUID and semantic request.
- [ ] Initial appointment status is handled as exact `Requested` casing.
- [ ] PatientProfile ownership/active-manager authority and concealed `404`
      behavior are understood.
- [ ] Scheduler authority is distinguished from patient authority and remains
      clinic-scoped.
- [ ] Appointment list uses exact filters, server ordering, and opaque cursors.
- [ ] Patient switching resets patient-bound pages and cursors.
- [ ] Appointment summary/detail fields match the backend exactly.
- [ ] Optional `reason` omission is handled without requiring `null`.
- [ ] Status and reschedule histories are rendered as separate ordered streams.
- [ ] Confirm/reject are not exposed as patient actions.
- [ ] Cancellation rules and idempotent retry behavior are implemented.
- [ ] Rescheduling preserves appointment ID/status and sends only `slotId`.
- [ ] Booking/reschedule slot conflicts trigger availability refresh.
- [ ] Mutation cache/refetch behavior covers detail, lists, and affected slots.
- [ ] Stable Problem Details codes map to safe UX behavior.
- [ ] DST and clinic-versus-device timezone behavior are tested.
- [ ] Appointment reason, tokens, and payloads are excluded from logs/analytics.
- [ ] No clinical/FHIR sharing is inferred from scheduling.
- [ ] No payments, fees, calendar, video-meeting, clinic-portal, or Phase 9
      behavior is assumed.
- [ ] No backend-only fingerprint, account, actor ID, version, constraint, or
      database field was added to frontend contracts.
