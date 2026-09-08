# Phase 9 — Frontend API Contract

## 1. Purpose

This document is the authoritative frontend integration contract for the completed
Beeexy Backend Phase 9 API. It is based on the runtime endpoint handlers,
application validation, persistence adapters, OpenAPI assertions, and Phase
9.1–9.7 tests.

Statements labeled **backend contract** describe behavior the frontend can rely
on. Statements labeled **frontend guidance** describe a safe integration approach
and may be adapted to the frontend's existing API and state libraries.

Phase 9 is a **voluntary longitudinal symptom diary** attached to an eligible,
completed, patient-owned Pre-Triage episode. An authorized user can:

- retrieve the active, reviewed diary questions and static information for the
  episode's frozen pathway;
- voluntarily submit one immutable symptom check-in; and
- retrieve immutable check-in history in deterministic pages.

Phase 9 does **not** classify severity, determine whether the patient is better or
worse, calculate trends or risk, automatically detect red flags, generate
personalized recommendations, schedule reminders, invoke AI, produce Phase 9
FHIR output, or project diary entries into Clinical History.

The frontend must render backend-provided content and preserve submitted values.
It must not recreate clinical logic that the backend intentionally omits.

## 2. Authentication / Authorization

All three Phase 9 operations require the normal Beeexy Bearer access token:

```http
Authorization: Bearer <access-token>
```

**Backend contract:** authorization is evaluated on every request against the
patient who owns the completed Pre-Triage episode.

- A primary patient can access an eligible episode owned by their PatientProfile.
- A manager can access an eligible episode for a managed patient only while the
  care relationship is active and authorizes that access.
- A relationship does not grant reverse access to the manager's own patient data.
- Revocation takes effect on the next request.
- A UUID or previously cached response never grants authority by itself.
- Anonymous or invalid authentication returns `401`.
- Missing, inaccessible, unrelated, reverse-relationship, revoked, incomplete,
  and unclaimed-anonymous episodes use the same concealed `404` application
  response when the route matched.
- A syntactically malformed `episodeId` does not match the UUID route and returns
  routing `404`; a Problem Details `errorCode` is not guaranteed for that routing
  failure.

The concealed application response is:

```json
{
  "title": "Symptom diary content not found.",
  "status": 404,
  "detail": "The requested symptom diary content could not be found.",
  "instance": "/api/v1/pre-triage/episodes/00000000-0000-0000-0000-000000000000/check-ins",
  "errorCode": "symptom_diary.episode_not_found",
  "correlationId": "<server-correlation-id>"
}
```

The `instance` reflects the requested path. The frontend must not use this
response to infer whether an episode exists.

**Frontend guidance:** after a patient-context switch, relationship refresh,
revocation, logout, or concealed `404`, clear episode-specific content, draft
answers, the current idempotency key, history pages, and cursors. Re-resolve the
current accessible patient/episode context instead of retrying from stale state.

## 3. Supported Phase 9 Pathways

The episode's frozen Phase 4 questionnaire determines the pathway. There is no
pathway request parameter and no client-controlled package selection.

| Frozen episode pathway | Phase 9 content | Behavior |
|---|---|---|
| `HEADACHE` | Approved `andrea-symptoms-v1` package | Available when the episode and caller are eligible |
| `ABDOMINAL_PAIN` | Approved `andrea-symptoms-v1` package | Available when the episode and caller are eligible |
| `FEVER` | Approved `andrea-symptoms-v1` package | Available when the episode and caller are eligible |
| `CHEST_PAIN` | Approved `andrea-symptoms-v1` package | Available when the episode and caller are eligible |
| `OTHER_SYMPTOMS` | No Andrea Phase 9 package | Content GET returns `422 symptom_diary.content_unavailable` |

There is no default, nearest-match, or cross-pathway fallback. In particular,
the frontend must not show HEADACHE, ABDOMINAL_PAIN, FEVER, or CHEST_PAIN diary
content for an `OTHER_SYMPTOMS` episode. Submitting another pathway's
`packageVersionId` also fails closed with
`422 symptom_diary.content_unavailable`.

## 4. Endpoint Summary

Phase 9 exposes exactly three operations:

| Method | Path | Authentication | Purpose | Success | Main failures |
|---|---|---|---|---|---|
| `GET` | `/api/v1/pre-triage/episodes/{episodeId}/symptom-diary-content` | Bearer | Load active reviewed content for the episode's frozen pathway | `200` | `401`, concealed `404`, `422`, sanitized `500` |
| `POST` | `/api/v1/pre-triage/episodes/{episodeId}/check-ins` | Bearer | Create or idempotently replay one voluntary immutable entry | first `201`; exact replay `200` | `400`, `401`, concealed `404`, `409`, `422`, sanitized `500` |
| `GET` | `/api/v1/pre-triage/episodes/{episodeId}/check-ins` | Bearer | List exact frozen historical entries | `200` | `401`, concealed `404`, `422`, sanitized `500` |

There is no check-in detail, update, delete, trend, reminder, or `/care-guide`
operation. The current complete OpenAPI document contains 53 paths; that global
count is informational and may change in a later approved backend phase, while
the Phase 9 operation set must remain contract-driven.

## 5. GET Symptom Diary Content

```http
GET /api/v1/pre-triage/episodes/{episodeId}/symptom-diary-content
Authorization: Bearer <access-token>
```

### Request contract

- `episodeId` is a required UUID route parameter.
- No query parameter is accepted. Any query parameter returns
  `422 symptom_diary.unsupported_query`.
- A request body is not accepted. A body returns
  `422 symptom_diary.unsupported_body`.
- The backend obtains the pathway only from persisted episode/questionnaire
  state and selects the active eligible package server-side.

### Exact success shape

The following is a complete HEADACHE response. UUIDs are illustrative; package
codes, version, hash, provenance, schemas, option values, order, and reviewed
content are the current `andrea-symptoms-v1` contract.

```json
{
  "episodeId": "11111111-1111-4111-8111-111111111111",
  "pathway": "HEADACHE",
  "packageVersionId": "22222222-2222-4222-8222-222222222222",
  "packageCode": "andrea-headache-symptom-diary",
  "packageVersion": "andrea-symptoms-v1",
  "contentHash": "acdd3489902a53e411723097fd14844c91600484cd88a0ca0f2f06c2e18267ed",
  "provenance": {
    "source": "MEDICAL_TEAM_PROVIDED",
    "reviewStatus": "REVIEWED",
    "approvalStatus": "APPROVED",
    "approvedAt": "2026-09-07T00:00:00+00:00"
  },
  "questionSet": {
    "code": "andrea-headache-questions",
    "version": "andrea-symptoms-v1",
    "questions": [
      {
        "code": "onset",
        "prompt": "When did it start?",
        "sourceOrder": 1,
        "isRequired": false,
        "answerSchema": {
          "type": "string"
        },
        "options": []
      },
      {
        "code": "maximum-intensity-onset",
        "prompt": "How quickly did it reach maximum intensity?",
        "sourceOrder": 2,
        "isRequired": false,
        "answerSchema": {
          "type": "string",
          "enum": [
            "Immediately / within seconds–minutes",
            "Within several hours",
            "Gradually over >1 day",
            "Not sure"
          ]
        },
        "options": [
          {
            "code": "immediately-seconds-minutes",
            "value": "Immediately / within seconds–minutes",
            "displayText": "Immediately / within seconds–minutes",
            "sourceOrder": 1
          },
          {
            "code": "within-several-hours",
            "value": "Within several hours",
            "displayText": "Within several hours",
            "sourceOrder": 2
          },
          {
            "code": "gradually-over-one-day",
            "value": "Gradually over >1 day",
            "displayText": "Gradually over >1 day",
            "sourceOrder": 3
          },
          {
            "code": "not-sure",
            "value": "Not sure",
            "displayText": "Not sure",
            "sourceOrder": 4
          }
        ]
      },
      {
        "code": "severity",
        "prompt": "How severe is it?",
        "sourceOrder": 3,
        "isRequired": false,
        "answerSchema": {
          "type": "string"
        },
        "options": []
      },
      {
        "code": "associated-symptoms",
        "prompt": "Do you have any of these symptoms?",
        "sourceOrder": 4,
        "isRequired": false,
        "answerSchema": {
          "type": "array",
          "items": {
            "type": "string",
            "enum": [
              "Fever/neck stiffness",
              "Weakness or numbness",
              "Difficulty speaking",
              "Visual loss/new visual disturbance",
              "Confusion/fainting/seizure",
              "Repeated vomiting",
              "Recent significant head trauma",
              "None of these"
            ]
          },
          "uniqueItems": true
        },
        "options": [
          {
            "code": "fever-neck-stiffness",
            "value": "Fever/neck stiffness",
            "displayText": "Fever/neck stiffness",
            "sourceOrder": 1
          },
          {
            "code": "weakness-numbness",
            "value": "Weakness or numbness",
            "displayText": "Weakness or numbness",
            "sourceOrder": 2
          },
          {
            "code": "difficulty-speaking",
            "value": "Difficulty speaking",
            "displayText": "Difficulty speaking",
            "sourceOrder": 3
          },
          {
            "code": "visual-loss-disturbance",
            "value": "Visual loss/new visual disturbance",
            "displayText": "Visual loss/new visual disturbance",
            "sourceOrder": 4
          },
          {
            "code": "confusion-fainting-seizure",
            "value": "Confusion/fainting/seizure",
            "displayText": "Confusion/fainting/seizure",
            "sourceOrder": 5
          },
          {
            "code": "repeated-vomiting",
            "value": "Repeated vomiting",
            "displayText": "Repeated vomiting",
            "sourceOrder": 6
          },
          {
            "code": "recent-significant-head-trauma",
            "value": "Recent significant head trauma",
            "displayText": "Recent significant head trauma",
            "sourceOrder": 7
          },
          {
            "code": "none",
            "value": "None of these",
            "displayText": "None of these",
            "sourceOrder": 8
          }
        ]
      }
    ]
  },
  "information": {
    "code": "andrea-headache-warning-information",
    "version": "andrea-symptoms-v1",
    "heading": "Red flags:",
    "warningSigns": [
      { "code": "warning-01", "displayText": "thunderclap onset", "sourceOrder": 1 },
      { "code": "warning-02", "displayText": "neurological deficit", "sourceOrder": 2 },
      { "code": "warning-03", "displayText": "altered consciousness", "sourceOrder": 3 },
      { "code": "warning-04", "displayText": "seizure", "sourceOrder": 4 },
      { "code": "warning-05", "displayText": "fever + neck stiffness", "sourceOrder": 5 },
      { "code": "warning-06", "displayText": "acute visual loss", "sourceOrder": 6 },
      { "code": "warning-07", "displayText": "significant head trauma", "sourceOrder": 7 },
      { "code": "warning-08", "displayText": "pregnancy/postpartum", "sourceOrder": 8 },
      { "code": "warning-09", "displayText": "new/progressive headache substantially different from usual.", "sourceOrder": 9 }
    ]
  }
}
```

`information.heading` and `information.body` are independently omitted when
their backend value is `null`. Current Andrea packages return `heading` and omit
`body`. Internal source paths and import timestamps are not exposed.

### Frontend rendering

- Sort and render questions by `sourceOrder`; the backend already returns this
  order, but using the explicit field avoids relying on array accidents.
- Render option labels from `displayText` and POST the corresponding exact
  `value`. Never POST the option `code` in place of its value.
- Use `prompt`, `answerSchema`, `options`, and `isRequired` from the response.
  Do not hard-code symptom-specific question text.
- Preserve Unicode, punctuation, capitalization, and choice values exactly.

### Critical `packageVersionId` rule

> The frontend must retain the exact `packageVersionId` returned by this GET and
> submit that same immutable package version in the subsequent POST.

Never replace it with a cached ID, infer it from `packageCode`, choose a newer
version client-side, or substitute a package from another pathway. If content is
reloaded and its `packageVersionId` changes before the user starts a new logical
submission, render and submit the newly returned package as one coherent unit.

### Warning signs

`information.warningSigns` is immutable, reviewed, static display information.
It is not a list of triggered conditions. The frontend may render the returned
heading and ordered text, but must not compare it to answers, mark a warning as
triggered, derive urgency/severity, generate recommendations, initiate an alert,
or choose follow-up actions from it.

## 6. POST Symptom Check-In

```http
POST /api/v1/pre-triage/episodes/{episodeId}/check-ins
Authorization: Bearer <access-token>
Content-Type: application/json
```

### Exact request

```json
{
  "packageVersionId": "22222222-2222-4222-8222-222222222222",
  "idempotencyKey": "33333333-3333-4333-8333-333333333333",
  "answers": [
    {
      "questionCode": "onset",
      "value": "Since yesterday evening"
    },
    {
      "questionCode": "maximum-intensity-onset",
      "value": "Within several hours"
    },
    {
      "questionCode": "associated-symptoms",
      "value": [
        "Repeated vomiting"
      ]
    }
  ]
}
```

**Backend contract:**

- `episodeId`, `packageVersionId`, and `idempotencyKey` must be non-empty UUIDs.
- `answers` must be present and must be a JSON array. It may be empty because all
  questions in the current Andrea packages have `isRequired: false`.
- Each answer contains exactly `questionCode` and `value`.
- Unknown top-level fields return
  `422 symptom_diary.unsupported_fields`.
- Unknown fields within an answer, null answer entries, missing/blank/unknown
  question codes, duplicate question codes, absent values, or invalid value
  shapes return `422 symptom_diary.answers_invalid`.
- An answer must refer to a question in the exact pinned package.
- Answers omitted for optional questions create no default or answer row.
- Answers in the request are normalized to question `sourceOrder` for response
  and logical-request hashing. For reliable retries, resend the exact same JSON
  body rather than depending on this normalization.
- Each serialized answer value is limited to 65,536 characters of JSON.
- Validation is structural only. There are no medical thresholds, combinations,
  warning matches, severity classifications, or inferred actions.

Current accepted value shapes are:

- free text: a JSON string, including an empty string if the user submits it;
- single choice: one exact string from `answerSchema.enum` and the matching
  option `value`;
- multiple choice: a JSON array of unique exact strings from
  `answerSchema.items.enum`.

The current array schema has no minimum item count, so an empty array is
structurally valid. The backend does not give special clinical meaning to the
`"None of these"` option and does not enforce mutual exclusion between it and
other selections. The frontend must not describe backend acceptance as a
clinical evaluation.

### Idempotency

The idempotency identity is `(episodeId, idempotencyKey)`. The backend includes
the exact package and logical answers in the request fingerprint.

- First accepted creation: `201 Created`.
- Exact logical replay: `200 OK`, returning the original immutable entry.
- Same episode/key with a different package or materially different answer
  payload: `409 symptom_diary.idempotency_conflict`.
- Concurrent identical requests converge on one stored entry; callers may
  observe one `201` and one `200`.

**Frontend guidance:** generate one UUID idempotency key for each logical Submit
action. Retain that key and the exact body while the outcome is unknown. After a
network timeout, retry the same body with the same key. Use a new key for a
genuinely new entry. Never reuse a key after editing the package, question set,
selected values, or free text.

### Exact success response

The response contains only the questions that were actually answered, enriched
with their frozen definitions. This complete example corresponds to a check-in
with one submitted free-text answer. UUIDs and `createdAt` are illustrative.

```json
{
  "checkInId": "44444444-4444-4444-8444-444444444444",
  "episodeId": "11111111-1111-4111-8111-111111111111",
  "createdAt": "2026-09-08T03:15:00+00:00",
  "pathway": "HEADACHE",
  "packageVersionId": "22222222-2222-4222-8222-222222222222",
  "packageCode": "andrea-headache-symptom-diary",
  "packageVersion": "andrea-symptoms-v1",
  "contentHash": "acdd3489902a53e411723097fd14844c91600484cd88a0ca0f2f06c2e18267ed",
  "provenance": {
    "source": "MEDICAL_TEAM_PROVIDED",
    "reviewStatus": "REVIEWED",
    "approvalStatus": "APPROVED",
    "approvedAt": "2026-09-07T00:00:00+00:00"
  },
  "questionSet": {
    "code": "andrea-headache-questions",
    "version": "andrea-symptoms-v1"
  },
  "answers": [
    {
      "questionCode": "onset",
      "prompt": "When did it start?",
      "sourceOrder": 1,
      "isRequired": false,
      "answerSchema": {
        "type": "string"
      },
      "options": [],
      "value": "Since yesterday evening"
    }
  ],
  "information": {
    "code": "andrea-headache-warning-information",
    "version": "andrea-symptoms-v1",
    "heading": "Red flags:",
    "warningSigns": [
      { "code": "warning-01", "displayText": "thunderclap onset", "sourceOrder": 1 },
      { "code": "warning-02", "displayText": "neurological deficit", "sourceOrder": 2 },
      { "code": "warning-03", "displayText": "altered consciousness", "sourceOrder": 3 },
      { "code": "warning-04", "displayText": "seizure", "sourceOrder": 4 },
      { "code": "warning-05", "displayText": "fever + neck stiffness", "sourceOrder": 5 },
      { "code": "warning-06", "displayText": "acute visual loss", "sourceOrder": 6 },
      { "code": "warning-07", "displayText": "significant head trauma", "sourceOrder": 7 },
      { "code": "warning-08", "displayText": "pregnancy/postpartum", "sourceOrder": 8 },
      { "code": "warning-09", "displayText": "new/progressive headache substantially different from usual.", "sourceOrder": 9 }
    ]
  }
}
```

The server controls `checkInId`, `createdAt`, account provenance, patient
ownership, and pathway. None is accepted from the request.

### Immutability

There is no update or delete endpoint. A later patient report is a new check-in
with a new idempotency key. The frontend must not present an edit/save workflow
that implies an existing entry can be changed on the server.

## 7. GET Symptom Diary History

```http
GET /api/v1/pre-triage/episodes/{episodeId}/check-ins?cursor=<opaque>&pageSize=20
Authorization: Bearer <access-token>
```

### Query parameters

| Parameter | Required | Contract |
|---|---|---|
| `cursor` | No | Opaque continuation cursor returned by the immediately preceding compatible page |
| `pageSize` | No | Integer from `1` through `100`; defaults to `20` |

No other query parameter is accepted. Repeating `cursor`, repeating `pageSize`,
or including an unsupported query key returns
`422 symptom_diary.unsupported_query`. A non-integer, zero, negative, or greater
than 100 page size returns `422 symptom_diary.page_size_invalid`. A GET body
returns `422 symptom_diary.unsupported_body`.

Authorization/episode eligibility is checked before paging validation. An
inaccessible episode remains concealed as `404` even when the supplied cursor or
page size is also invalid.

### Cursor rules

`nextCursor` is integrity-protected, URL-safe, opaque, and bound to:

- cursor format version;
- the exact episode;
- the normalized page size; and
- the final `(createdAt, checkInId)` boundary of the returned page.

The frontend must store the string unchanged, URL-encode it as a query value,
resend it unchanged, and never decode, edit, concatenate, or derive meaning from
it. If the first request omitted `pageSize`, its normalized size is 20; later
requests using that cursor must also omit `pageSize` or explicitly use 20.

Malformed, tampered, stale, cross-episode, or page-size-incompatible cursors
return `422 symptom_diary.cursor_invalid`. Reset pagination intentionally and
refetch from the first page; do not repeatedly retry the rejected cursor.

### Ordering

The backend order is exactly:

```text
createdAt ASC, checkInId ASC
```

The UUID is the deterministic tie-breaker when two entries have the same server
creation instant. A complete cursor traversal returns each entry once without
duplicates or omissions.

### Exact response

An empty history is:

```json
{
  "items": [],
  "nextCursor": null
}
```

This is a complete page containing one valid entry created with `answers: []`.
The identifiers and timestamp are illustrative.

```json
{
  "items": [
    {
      "checkInId": "44444444-4444-4444-8444-444444444444",
      "episodeId": "11111111-1111-4111-8111-111111111111",
      "createdAt": "2026-09-08T03:15:00+00:00",
      "pathway": "HEADACHE",
      "packageVersionId": "22222222-2222-4222-8222-222222222222",
      "packageCode": "andrea-headache-symptom-diary",
      "packageVersion": "andrea-symptoms-v1",
      "contentHash": "acdd3489902a53e411723097fd14844c91600484cd88a0ca0f2f06c2e18267ed",
      "provenance": {
        "source": "MEDICAL_TEAM_PROVIDED",
        "reviewStatus": "REVIEWED",
        "approvalStatus": "APPROVED",
        "approvedAt": "2026-09-07T00:00:00+00:00"
      },
      "questionSet": {
        "code": "andrea-headache-questions",
        "version": "andrea-symptoms-v1"
      },
      "answers": [],
      "information": {
        "code": "andrea-headache-warning-information",
        "version": "andrea-symptoms-v1",
        "heading": "Red flags:",
        "warningSigns": [
          { "code": "warning-01", "displayText": "thunderclap onset", "sourceOrder": 1 },
          { "code": "warning-02", "displayText": "neurological deficit", "sourceOrder": 2 },
          { "code": "warning-03", "displayText": "altered consciousness", "sourceOrder": 3 },
          { "code": "warning-04", "displayText": "seizure", "sourceOrder": 4 },
          { "code": "warning-05", "displayText": "fever + neck stiffness", "sourceOrder": 5 },
          { "code": "warning-06", "displayText": "acute visual loss", "sourceOrder": 6 },
          { "code": "warning-07", "displayText": "significant head trauma", "sourceOrder": 7 },
          { "code": "warning-08", "displayText": "pregnancy/postpartum", "sourceOrder": 8 },
          { "code": "warning-09", "displayText": "new/progressive headache substantially different from usual.", "sourceOrder": 9 }
        ]
      }
    }
  ],
  "nextCursor": null
}
```

When more entries exist, `nextCursor` is a non-null opaque string.

### Frozen historical content rule

> Every history item is reconstructed from that entry's own exact immutable
> `packageVersionId`.

The frontend must render the prompt, schema, options, submitted value,
information, warnings, hash, and provenance returned inside that history item.
It must **not** fetch today's active content and use current questions or options
to render an old entry. After package B becomes active, an entry recorded against
package A continues to return package A exactly.

## 8. Question Rendering Contract

Current `andrea-symptoms-v1` content uses exactly these structural schemas:

| Detection | Suggested control | POST `value` | Rules |
|---|---|---|---|
| `answerSchema.type === "string"` and `options.length === 0` | Text input or textarea appropriate to the frontend layout | JSON string | Preserve exact user string; serialized value limit is 65,536 JSON characters |
| `answerSchema.type === "string"` and `answerSchema.enum` exists | Radio/select single choice | One exact option `value` string | Do not POST option `code`, label index, or translated text |
| `answerSchema.type === "array"`, item type is `string`, and `uniqueItems === true` | Checkbox/multi-select | Array of unique exact option `value` strings | No duplicates; every string must be in `items.enum`; array order is preserved |

For all current packages, `isRequired` is `false`. In future reviewed content,
the frontend must honor the returned field rather than assuming optionality.
Omit an unanswered optional question from `answers`; do not invent a default
answer. A required question must have one structurally valid answer.

Render questions and options from backend data, ordered by `sourceOrder`. Do not
hard-code prompts, option values, warning text, package UUIDs, or pathway-to-form
definitions into frontend code.

If a future response contains a schema type the frontend does not support, fail
closed in the UI: preserve no fabricated value, disable submission for that
package, and show a neutral compatibility/unavailable state. Do not coerce the
schema or guess a control.

## 9. TypeScript Contracts

These interfaces use the exact serialized API property names and current
nullability/omission behavior. UUIDs and ISO timestamps remain strings at
runtime.

```ts
export type Uuid = string;
export type IsoInstant = string;

export type SymptomDiaryPathway =
  | "HEADACHE"
  | "ABDOMINAL_PAIN"
  | "FEVER"
  | "CHEST_PAIN";

export type SymptomDiaryAnswerValue = string | string[];

export interface SymptomDiaryFreeTextSchema {
  type: "string";
}

export interface SymptomDiarySingleChoiceSchema {
  type: "string";
  enum: string[];
}

export interface SymptomDiaryMultipleChoiceSchema {
  type: "array";
  items: {
    type: "string";
    enum: string[];
  };
  uniqueItems: true;
}

export type SymptomDiaryAnswerSchema =
  | SymptomDiaryFreeTextSchema
  | SymptomDiarySingleChoiceSchema
  | SymptomDiaryMultipleChoiceSchema;

export interface SymptomDiaryContentProvenance {
  source: "MEDICAL_TEAM_PROVIDED";
  reviewStatus: "REVIEWED";
  approvalStatus: "APPROVED";
  approvedAt: IsoInstant;
}

export interface SymptomDiaryQuestionOption {
  code: string;
  value: string;
  displayText: string;
  sourceOrder: number;
}

export interface SymptomDiaryQuestion {
  code: string;
  prompt: string;
  sourceOrder: number;
  isRequired: boolean;
  answerSchema: SymptomDiaryAnswerSchema;
  options: SymptomDiaryQuestionOption[];
}

export interface SymptomWarningSign {
  code: string;
  displayText: string;
  sourceOrder: number;
}

export interface SymptomDiaryInformation {
  code: string;
  version: string;
  // Each property is omitted when its backend value is null.
  heading?: string;
  body?: string;
  warningSigns: SymptomWarningSign[];
}

export interface SymptomDiaryQuestionSet {
  code: string;
  version: string;
  questions: SymptomDiaryQuestion[];
}

export interface SymptomDiaryContentResponse {
  episodeId: Uuid;
  pathway: SymptomDiaryPathway;
  packageVersionId: Uuid;
  packageCode: string;
  packageVersion: string;
  contentHash: string;
  provenance: SymptomDiaryContentProvenance;
  questionSet: SymptomDiaryQuestionSet;
  information: SymptomDiaryInformation;
}

export interface SymptomDiarySubmittedAnswer {
  questionCode: string;
  value: SymptomDiaryAnswerValue;
}

export interface CreateSymptomCheckInRequest {
  packageVersionId: Uuid;
  idempotencyKey: Uuid;
  answers: SymptomDiarySubmittedAnswer[];
}

export interface SymptomDiaryCheckInQuestionSet {
  code: string;
  version: string;
}

export interface SymptomDiaryAcceptedAnswer {
  questionCode: string;
  prompt: string;
  sourceOrder: number;
  isRequired: boolean;
  answerSchema: SymptomDiaryAnswerSchema;
  options: SymptomDiaryQuestionOption[];
  value: SymptomDiaryAnswerValue;
}

export interface SymptomCheckInResponse {
  checkInId: Uuid;
  episodeId: Uuid;
  createdAt: IsoInstant;
  pathway: SymptomDiaryPathway;
  packageVersionId: Uuid;
  packageCode: string;
  packageVersion: string;
  contentHash: string;
  provenance: SymptomDiaryContentProvenance;
  questionSet: SymptomDiaryCheckInQuestionSet;
  answers: SymptomDiaryAcceptedAnswer[];
  information: SymptomDiaryInformation;
}

export interface SymptomCheckInHistoryPage {
  items: SymptomCheckInResponse[];
  nextCursor: string | null;
}

export interface BeeexyProblemDetails {
  type?: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  correlationId?: string;
}
```

`SymptomDiaryAnswerSchema` and `SymptomDiaryAnswerValue` describe the currently
approved packages. Runtime schema discrimination is still required; do not cast
unknown future schemas into this union without validation.

## 10. Suggested API Client Methods

These signatures describe the transport contract; they do not prescribe a
frontend library or error-wrapper architecture.

```ts
declare function getSymptomDiaryContent(
  episodeId: Uuid,
  signal?: AbortSignal,
): Promise<SymptomDiaryContentResponse>;

declare function createSymptomCheckIn(
  episodeId: Uuid,
  request: CreateSymptomCheckInRequest,
  signal?: AbortSignal,
): Promise<SymptomCheckInResponse>;

declare function listSymptomCheckIns(
  episodeId: Uuid,
  query?: {
    cursor?: string;
    pageSize?: number;
  },
  signal?: AbortSignal,
): Promise<SymptomCheckInHistoryPage>;
```

The client should parse non-success responses as `BeeexyProblemDetails` when the
response content type and body support it. Do not assume routing `404`, generic
`401`, malformed-body `400`, or unexpected `500` always includes an
`errorCode`.

## 11. Frontend State Flow

A minimal integration flow is:

```text
eligible completed episode
  -> GET symptom-diary-content
  -> retain exact packageVersionId and render returned schema/content
  -> user voluntarily enters answers
  -> create one idempotencyKey for this logical submission
  -> POST exact packageVersionId + key + answers
  -> render neutral success
  -> optionally refresh or paginate history
```

Useful episode-scoped state includes:

- `episodeId`;
- the complete content response and its exact `packageVersionId`;
- draft answer values keyed by `questionCode`;
- the current logical-submission `idempotencyKey` and frozen request body while
  submission outcome is unknown;
- history items, chosen page size, and `nextCursor`.

Discard this state when its episode or patient context is no longer current.
Do not merge answer drafts across packages or episodes.

## 12. Error Contract

Application validation failures use Beeexy Problem Details with `title`,
`status`, optional `detail`, request `instance`, `correlationId`, and the stable
`errorCode` listed below. Frontend user copy should be controlled by the
frontend; do not display raw technical messages directly.

| HTTP | Stable `errorCode` | Endpoint(s) | Meaning | Recommended frontend behavior |
|---|---|---|---|---|
| `400` | none guaranteed | POST | Malformed JSON, invalid JSON-to-UUID binding, or another malformed request body | Keep safe local draft state, report that the request could not be submitted, and correct serialization before retrying |
| `401` | none guaranteed | all three | Missing, invalid, or expired Bearer authentication | Enter the existing reauthentication/session-refresh flow; do not treat as missing diary content |
| `404` | `symptom_diary.episode_not_found` when the UUID route matched application handling | all three | Episode is missing, incomplete, unclaimed, not owned, unrelated, reverse-only, revoked, or otherwise inaccessible | Treat as invalid/lost patient context, clear episode state, and refresh accessible context; do not reveal existence |
| routing `404` | none guaranteed | all three | `episodeId` is not a UUID and the constrained route did not match | Treat as a client routing/identifier defect; do not retry unchanged |
| `409` | `symptom_diary.idempotency_conflict` | POST | Same episode/idempotency key was already used for a different package or logical answers | Preserve the original entry, stop blind retries, and use a new key only for a deliberate new submission |
| `422` | `symptom_diary.content_unavailable` | all three as applicable | No eligible approved package, OTHER_SYMPTOMS, wrong/unknown/cross-pathway package, or corrupt/unavailable exact historical content | Show a neutral unavailable state; never substitute another pathway or current package |
| `422` | `symptom_diary.unsupported_query` | content GET, POST, history GET | Content GET/POST received any query selector, or history received an unsupported/repeated selector | Correct the client request construction |
| `422` | `symptom_diary.unsupported_body` | content GET, history GET | A GET request included a body | Remove the body and retry if context is still valid |
| `422` | `symptom_diary.unsupported_fields` | POST | The top-level request contains a field outside `packageVersionId`, `idempotencyKey`, and `answers` | Remove client-only fields; never send patient/pathway/time/assessment metadata |
| `422` | `symptom_diary.identifiers_required` | POST | `packageVersionId` or `idempotencyKey` is the all-zero UUID | Restore the exact package ID and generate/retain a non-empty submission UUID |
| `422` | `symptom_diary.answers_invalid` | POST | `answers` is missing/null or an answer is unknown, duplicate, incomplete, has extra fields, or violates the exact returned structural schema/options | Retain the user's draft, reconcile it against the pinned package, and show structural validation state |
| `422` | `symptom_diary.page_size_invalid` | history GET | `pageSize` is not an integer from 1 through 100 | Correct or omit it; omitted means 20 |
| `422` | `symptom_diary.cursor_invalid` | history GET | Cursor is malformed, tampered, stale, cross-episode, or incompatible with the requested page size | Intentionally reset pagination and fetch the first page |
| `500` | none guaranteed | all three | Unexpected sanitized backend failure | Show a generic retryable failure with the correlation ID available for support; never expose raw response internals |

Representative stable validation response:

```json
{
  "title": "Request validation failed.",
  "status": 422,
  "detail": "Page size must be between 1 and 100.",
  "instance": "/api/v1/pre-triage/episodes/11111111-1111-4111-8111-111111111111/check-ins",
  "errorCode": "symptom_diary.page_size_invalid",
  "correlationId": "<server-correlation-id>"
}
```

Do not branch primarily on English `title` or `detail`; for known application
errors, use HTTP status plus stable `errorCode`.

## 13. Managed Patient Behavior

### Self

Use the completed episode selected for the authenticated user's own
PatientProfile. The server still verifies ownership on each request.

### Active managed patient

The same three routes are used; there is no manager-specific Phase 9 API. Use an
episode belonging to the currently selected managed patient. The server verifies
the active relationship on every read and locks/revalidates manager authority
during a write.

### Patient context switch

Clear the previous episode's content, answer draft, idempotency state, history,
and cursor before loading the new patient's episode. Never reuse a cursor,
package UUID, or submission key across patient contexts.

### Revoked management access

A previously successful screen may receive concealed
`404 symptom_diary.episode_not_found` on its next request. Clear inaccessible
patient-specific state and refresh the user's accessible PatientProfiles. Do not
continue to display stale sensitive history as if access were current.

## 14. Loading / Empty / Error States

Frontend state should distinguish:

- **content loading:** waiting for content GET; do not render a stale package for
  another episode;
- **content available:** render the complete exact package and retain its UUID;
- **content unavailable:** `422 symptom_diary.content_unavailable`; show neutral
  unavailability and no fallback form;
- **history loading:** preserve already authorized displayed data according to
  the frontend's privacy policy while fetching the requested page;
- **empty history:** `200` with `items: []` and `nextCursor: null`;
- **submission pending:** freeze the exact request body and idempotency key;
- **created:** `201`; treat the returned server record as authoritative;
- **idempotent replay:** `200`; treat it as successful recovery of the original
  entry, not a second entry;
- **access lost:** concealed `404`; clear episode/patient-scoped sensitive state;
- **invalid cursor:** `422`; offer or perform an intentional first-page reset.

## 15. Retry Semantics

- Content GET is read-only and safe to retry for transient transport failures.
- History GET is read-only and safe to retry with the same unchanged cursor and
  compatible page size.
- POST is safe to retry only with the exact same body and the same idempotency
  key for the same logical submission.
- After an ambiguous timeout, do not generate a new key before retrying; doing so
  can create a second legitimate immutable entry.
- Never retry a changed POST payload with the same key.
- Do not automatically retry `401`, concealed `404`, structural `422`, invalid
  cursor `422`, or idempotency `409` without first resolving their cause.

## 16. Clinical Safety Rules for Frontend

> The frontend must not add clinical logic that Phase 9 intentionally omits.

Do not implement:

- “you are getting worse,” “improving,” or similar comparisons;
- severity labels derived from a free-text answer or selected value;
- trend, delta, probability, or risk scores;
- red-flag/warning matching against answers;
- automated urgent-care banners based on selected diary answers;
- personalized medical recommendations, next actions, or escalation;
- medication or treatment advice;
- reminder cadence, overdue/adherence state, or automatic follow-up scheduling;
- AI interpretation or summaries;
- Phase 9 trend charts that imply clinical meaning.

Warning signs may only be rendered as the approved static information returned
by the backend. A neutral chronological list of immutable entries is permitted;
clinical interpretation of that list is not.

## 17. Privacy Guidance

Symptom diary answers and history are sensitive health information.

- Do not write request/response bodies, free text, prompts, options, warnings, or
  cursors to browser console logs.
- Do not include raw answers or diary content in analytics, crash breadcrumbs,
  telemetry, URLs, query strings, or notification payloads.
- Avoid insecure persistent browser storage for raw diary payloads and answer
  drafts. Follow Beeexy's existing secure API/session/cache patterns.
- Keep the Bearer token out of logs and client-visible error reporting.
- Clear patient-specific state on patient switch, access loss, and logout.
- Do not retain stale managed-patient history after revocation.
- Use `correlationId` for support diagnostics without attaching sensitive request
  content.

## 18. Example Flows

### A — Load diary

1. Select an eligible completed Pre-Triage episode.
2. GET its `symptom-diary-content` with the Bearer token.
3. On `200`, render questions/options/information from the response.
4. Preserve the exact `episodeId` and `packageVersionId` as one form context.
5. On `422 content_unavailable`, render no fallback package.

### B — Submit

1. Build `answers` from returned question codes and exact value shapes.
2. Generate one non-empty UUID idempotency key for the logical submission.
3. Freeze `{ packageVersionId, idempotencyKey, answers }` while pending.
4. POST it to the episode's `/check-ins` route.
5. Treat `201` and `200` as success; both return the authoritative entry.
6. Optionally refresh history after success.
7. For an ambiguous network timeout, retry the frozen body and same key.

### C — History pagination

1. GET `/check-ins` with an omitted page size or one value from 1 through 100.
2. Render every item using that item's returned frozen definitions/information.
3. If `nextCursor` is non-null, request the next page with that cursor unchanged
   and the same normalized page size.
4. Append results in returned order.
5. Stop when `nextCursor` is `null`.

### D — OTHER_SYMPTOMS

1. A valid completed `OTHER_SYMPTOMS` episode is selected.
2. Content GET returns `422 symptom_diary.content_unavailable`.
3. Show a neutral unavailable state.
4. Do not substitute another package and do not allow manual package selection.

### E — Managed access revoked

1. An active manager previously loaded content or history.
2. The relationship is revoked.
3. The next Phase 9 request returns concealed
   `404 symptom_diary.episode_not_found`.
4. Clear episode-specific content, drafts, submission keys, history, and cursors.
5. Refresh accessible patient context without revealing whether data still
   exists.

## 19. Explicitly Out of Scope

The Phase 9 frontend integration must not implement:

- editing or deleting check-ins;
- client-selected pathways or manual package selection;
- an `OTHER_SYMPTOMS` fallback;
- trend scoring or clinical trend charts;
- severity/risk/urgency derivation;
- warning matching or triggered-red-flag state;
- recommendations, escalation, or medication advice;
- reminders, adherence tracking, notifications, or scheduling;
- AI summaries or interpretation;
- Phase 9 FHIR generation;
- Clinical History synchronization;
- hidden client defaults for optional unanswered questions;
- rendering old entries with current active package content.

## 20. Verify Against Tests

This contract was cross-checked against:

- the completed Phase 9.1–9.7 implementation-plan section;
- `SymptomDiaryEndpointExtensions` and its exact serialized DTOs;
- `GetSymptomDiaryContent`, `RecordSymptomCheckIn`, and
  `ListSymptomCheckIns`;
- exact-package, episode eligibility, transaction, read-repository, cursor, and
  dependency-injection implementations;
- `ApiExceptionHandler` Phase 9 mappings;
- content, creation, history, Phase 9.7 acceptance, semantic safety, package,
  structural-validation, idempotency/concurrency, OpenAPI, and CORS tests.

Runtime implementation, tests, and OpenAPI agree on the three operations,
success/error statuses, JSON shapes, authorization behavior, package binding,
idempotency, history reconstruction, and safety boundaries documented here.
No material Phase 9 API contract discrepancy was found.
