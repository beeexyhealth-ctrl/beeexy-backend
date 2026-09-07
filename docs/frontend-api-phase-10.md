# Phase 10 Frontend API Integration

## 1. Overview

This is the frontend integration contract for the completed Beeexy Phase 10
backend. It is based on the current endpoint handlers, HTTP DTOs, application
use cases, authorization boundaries, persistence queries, Problem Details
mappings, OpenAPI assertions, integration tests, and the accepted Phase
10.1–10.8 implementation evidence.

Phase 10 exposes four frontend-facing capabilities:

1. authenticated AI Conversations and persistent AI History;
2. private Temporary Document upload and deletion;
3. structured informational Second Opinions; and
4. immutable Second Opinion regeneration.

Every Phase 10 endpoint requires Bearer authentication. The backend performs
provider execution, structural validation, and safety validation. The frontend
must not reproduce those rules or call an AI provider directly. It renders
only the approved assistant content, fixed fallback, result, metadata, and
status returned by Beeexy.

AI History is not Clinical History. Conversation messages and Second Opinion
results do not create Clinical History events, amendments, FHIR resources,
appointments, or deterministic Pre-Triage results. Rejected provider output is
restricted backend audit material and is never present in these API responses.

Temporary Document blobs expire exactly 24 hours after upload and may be
deleted earlier. Their expiry is independent of durable Second Opinion results.
A result remains readable after its source blob is deleted, and regeneration
reuses the frozen original input rather than the blob.

### Current implementation notes

- The public surface contains exactly 10 operations across eight paths.
- The conversation soft-delete route exists and is part of the accepted API.
- The three `202` operations execute the provider inside the HTTP request and
  return only after a terminal `succeeded`/`completed`, `rejected`, or `failed`
  outcome. They are not queued background jobs.
- `POST` Second Opinion and regeneration return both a `Location` header and a
  `statusUrl` field. Read that URL to obtain the result projection.
- No endpoint exposes raw provider output, restricted audit output, prompts,
  private blob keys, or provider credentials.

## 2. Authentication

Send the access token through the existing Beeexy session client:

```http
Authorization: Bearer <access-token>
Accept: application/json
```

Missing, invalid, or expired authentication returns `401` before resource
lookup, request validation, persistence, or provider execution. The global
pipeline emits safe Problem Details and `X-Correlation-ID`; the frontend must
not depend on a Phase 10-specific `errorCode` for `401`. Apply the existing
refresh/sign-in flow and do not retry indefinitely.

Phase 10 does not use `403`. Resource authorization failures are deliberately
concealed as `404`:

- a missing, foreign, or logically deleted conversation;
- a patient the account does not currently control;
- a missing or foreign Temporary Document;
- a missing or foreign Second Opinion; or
- a Second Opinion whose patient authority was revoked.

The frontend must not distinguish “missing” from “not allowed.” Remove or
invalidate stale patient/resource state and show one neutral unavailable
message. Knowledge of a UUID never grants access.

For patient-associated conversations, current patient authority is checked
when the conversation is created and again when patient context is assembled
for a message. Second Opinion create/read/regenerate operations also require
current patient authority. A previously authorized cached object is not proof
of current access.

## 3. Base route summary

| Method | Path | Auth | Success | Frontend purpose |
|---|---|---|---|---|
| `POST` | `/api/v1/ai/conversations` | Bearer | `201` | Create an optionally patient-associated conversation |
| `GET` | `/api/v1/ai/conversations` | Bearer | `200` | List non-deleted AI History |
| `GET` | `/api/v1/ai/conversations/{id}` | Bearer | `200` | Read conversation messages and disclaimer |
| `POST` | `/api/v1/ai/conversations/{id}/messages` | Bearer | `202` | Execute one message and receive its terminal outcome |
| `DELETE` | `/api/v1/ai/conversations/{id}` | Bearer | `204` | Logically delete/hide a conversation |
| `POST` | `/api/v1/ai/documents` | Bearer | `201` | Upload one private temporary PDF or TXT |
| `DELETE` | `/api/v1/ai/documents/{id}` | Bearer | `204` | Physically delete a temporary blob early |
| `POST` | `/api/v1/ai/second-opinions` | Bearer | `202` | Execute a Second Opinion from selected inputs |
| `GET` | `/api/v1/ai/second-opinions/{id}` | Bearer | `200` | Read safe status/latest approved result |
| `POST` | `/api/v1/ai/second-opinions/{id}/regenerate` | Bearer | `202` | Execute again from the immutable original input |

UUID route parameters use the canonical UUID string representation. A value
which does not satisfy the route's GUID constraint does not enter the handler
and normally receives generic `404` handling.

JSON property names are camelCase. UUIDs and `DateTimeOffset` values serialize
as strings; timestamps are ISO 8601 instants with an offset. All status and
role values documented below are lowercase strings produced explicitly by the
endpoint mapping, not numeric .NET enums.

## 4. AI Conversations

### 4.1 Create conversation

`POST /api/v1/ai/conversations`

```json
{
  "purpose": "GENERAL_HEALTH",
  "patientId": "9fb45f87-f9dd-4dbf-a2d5-b2be4cd41624"
}
```

| JSON field | Type | Required | Contract |
|---|---|---:|---|
| `purpose` | string | Yes | One of `GENERAL_HEALTH`, `MEDICAL_TERMS`, `SYMPTOM_DISCUSSION`, `CLINICIAN_QUESTIONS` |
| `patientId` | UUID or `null` | No | Optional patient association; current patient authority is required |

The backend trims `purpose`, replaces `-` with `_`, and compares its uppercase
form. The frontend should still send the canonical values above. Purpose is
validated at creation but is not returned by the API and is not exposed in
list/detail. Unknown JSON fields are rejected with `422` and
`ai.conversation.unsupported_field`.

Success is `201 Created`, with a `Location` header pointing to the conversation
detail and this body:

```json
{
  "conversationId": "bf981c53-bf8e-41e2-ad47-1c6cf8574d85",
  "patientId": "9fb45f87-f9dd-4dbf-a2d5-b2be4cd41624",
  "createdAt": "2026-09-02T15:10:00+00:00",
  "disclaimer": {
    "version": "ai-general-disclaimer-v1",
    "content": "Esta respuesta ha sido generada por inteligencia artificial y no sustituye una evaluación médica. Consulta siempre con un profesional de salud certificado."
  }
}
```

`patientId` is `null` when no patient is associated. Creation does not call the
AI provider.

Errors:

- `400`: malformed JSON/body binding;
- `401`: missing or invalid Bearer session;
- concealed `404`: non-empty patient UUID is missing or unauthorized; an empty
  patient UUID is handled the same way;
- `422 ai.conversation.purpose_invalid`: missing/blank/unsupported purpose;
- `422 ai.conversation.unsupported_field`: an extra JSON property; and
- safe `500`: unexpected backend failure.

### 4.2 List conversations

`GET /api/v1/ai/conversations?cursor=<opaque>&pageSize=20`

| Query parameter | Type | Required | Contract |
|---|---|---:|---|
| `cursor` | string | No | Opaque account-bound keyset cursor |
| `pageSize` | integer | No | Default `20`; minimum `1`; maximum `100` |

The response is paginated. Conversations are ordered by `createdAt`
descending, then conversation UUID descending. The frontend must preserve that
order and treat the cursor as opaque.

```json
{
  "items": [
    {
      "conversationId": "bf981c53-bf8e-41e2-ad47-1c6cf8574d85",
      "patientId": null,
      "createdAt": "2026-09-02T15:10:00+00:00"
    }
  ],
  "nextCursor": null
}
```

`nextCursor` is a string only when another page exists; `null` means stop.
Discard cursors on account/session changes. A malformed, other-account,
unknown, or deleted-boundary cursor returns `422
ai.conversation.cursor_invalid`. An out-of-range page size returns `422
ai.conversation.page_size_invalid`; a syntactically non-integer page size is a
framework `400`.

Logically deleted conversations never appear. There are no filters, search,
message previews, purpose, title, update timestamp, or unread count in this
response.

### 4.3 Conversation detail

`GET /api/v1/ai/conversations/{id}` returns:

```json
{
  "conversation": {
    "conversationId": "bf981c53-bf8e-41e2-ad47-1c6cf8574d85",
    "patientId": null,
    "createdAt": "2026-09-02T15:10:00+00:00"
  },
  "messages": [
    {
      "messageId": "4a4f5b9d-54cc-426a-a600-ece49a1c88e7",
      "role": "user",
      "content": "What does hydration mean for general health?",
      "sequence": 1,
      "createdAt": "2026-09-02T15:11:00+00:00"
    },
    {
      "messageId": "7c07cc20-a45e-49fd-b569-d9153f0587ee",
      "role": "assistant",
      "content": "Approved informational content or a fixed Beeexy fallback.",
      "sequence": 2,
      "createdAt": "2026-09-02T15:11:01+00:00"
    }
  ],
  "disclaimer": {
    "version": "ai-general-disclaimer-v1",
    "content": "Esta respuesta ha sido generada por inteligencia artificial y no sustituye una evaluación médica. Consulta siempre con un profesional de salud certificado."
  }
}
```

Messages are in ascending `sequence` order. Roles are exactly `user` and
`assistant`. Assistant content is either safety-approved provider content or a
fixed backend fallback and is safe to render as escaped text. Never render any
message as raw HTML. User content is echoed from the user and must receive the
frontend's normal untrusted-text treatment.

No execution list, result snapshots, provider/model/prompt identifiers,
safety reason, `aiGenerated` flag, or deleted timestamp is returned.

Missing, foreign, and logically deleted conversations all return the same
concealed `404 ai.conversation.not_found`.

### 4.4 Send message

`POST /api/v1/ai/conversations/{id}/messages`

```json
{
  "content": "What does hydration mean for general health?"
}
```

`content` is required, trimmed by the backend, and limited to 4,000
characters. Unknown JSON fields return `422
ai.conversation.unsupported_field`.

The endpoint returns `202 Accepted` only after the provider, structural
validation, and safety pipeline has finished. The `Location` header points to
`/api/v1/ai/conversations/{id}`. The response is already terminal:

```json
{
  "conversationId": "bf981c53-bf8e-41e2-ad47-1c6cf8574d85",
  "userMessageId": "4a4f5b9d-54cc-426a-a600-ece49a1c88e7",
  "executionId": "11cf606a-fadf-48cb-a174-4170666b9f55",
  "status": "completed",
  "assistantMessage": {
    "messageId": "7c07cc20-a45e-49fd-b569-d9153f0587ee",
    "role": "assistant",
    "content": "Approved informational content or a fixed Beeexy fallback.",
    "sequence": 2,
    "createdAt": "2026-09-02T15:11:01+00:00"
  },
  "disclaimer": {
    "version": "ai-general-disclaimer-v1",
    "content": "Esta respuesta ha sido generada por inteligencia artificial y no sustituye una evaluación médica. Consulta siempre con un profesional de salud certificado."
  }
}
```

Status behavior:

| `status` | `assistantMessage` | Meaning |
|---|---|---|
| `completed` | Present | Structurally valid execution. Content is either approved AI text or a fixed Beeexy safety fallback. |
| `rejected` | Omitted | Provider output failed structural/schema validation; raw output is unavailable. |
| `failed` | Omitted | Provider timeout, cancellation, unavailable provider, or normalized provider failure. |

`assistantMessage` is omitted, not `null`, in `rejected` and `failed`
responses. The submitted user message remains in history even when no
assistant message is created.

A medical-safety rejection after structurally valid output deliberately maps
to `completed` with a fixed generic or critical fallback as the assistant
message. The frontend must render that backend content and must not try to
infer whether it was a fallback by parsing the text. There is no public safety
reason/category field.

Only one execution may run for a conversation. A competing submission returns
`409 ai.conversation.execution_conflict`; keep the composer disabled while the
request is active, then let the user deliberately retry. The conversation
supports at most 50 persisted user and assistant messages. Exhaustion returns
`422 ai.conversation.message_limit_reached` without calling the provider.

There is no conversation execution-status endpoint and no polling. Reconcile
the local message list from the response or refetch conversation detail.

### 4.5 Request-policy errors

The backend rejects off-topic requests, jailbreak/role overrides, serious-harm
instructions, and illicit-substance manufacturing instructions before calling
the provider. All four categories intentionally use the same frontend-visible
response:

```json
{
  "title": "Request validation failed.",
  "status": 422,
  "detail": "The AI conversation request cannot be processed.",
  "instance": "/api/v1/ai/conversations/{id}/messages",
  "errorCode": "ai.conversation.request_not_supported",
  "correlationId": "..."
}
```

Blank or over-4,000-character content instead uses
`ai.conversation.message_invalid`. The 50-message limit uses
`ai.conversation.message_limit_reached`. Do not classify the user's input in
the browser or reveal policy regexes; display safe product copy based on the
backend code.

### 4.6 Conversation disclaimer

Create, detail, and message responses expose:

```ts
{
  version: "ai-general-disclaimer-v1";
  content: "Esta respuesta ha sido generada por inteligencia artificial y no sustituye una evaluación médica. Consulta siempre con un profesional de salud certificado.";
}
```

The list response does not contain a disclaimer. Render the exact returned
content rather than hard-coding a translated replacement. Conversation
responses do not expose a separate AI-generated flag.

### 4.7 Logical delete/hide

`DELETE /api/v1/ai/conversations/{id}` returns `204 No Content` with no body.
It is owner-only and logically deletes the conversation. A repeated delete by
the same owner remains `204`; internal history/audit records are retained.

After deletion, the conversation disappears from list results, detail and
message submission return concealed `404`, and the frontend should evict the
detail and all cached list entries. A foreign or genuinely missing UUID also
returns the same `404`.

## 5. Temporary Documents

### 5.1 Upload document

`POST /api/v1/ai/documents` accepts `multipart/form-data` with exactly one file
part named `file` and no text form fields:

```ts
const form = new FormData();
form.append("file", file);
```

Do not manually set the multipart `Content-Type`; `fetch` must add its boundary.

Accepted files:

- `.pdf` with declared `application/pdf`, a real PDF signature, and useful
  embedded text;
- `.txt` with declared `text/plain`, strict UTF-8 text, and useful content;
- exactly one file per request; and
- at most 25 MiB, exactly 26,214,400 bytes in the current configuration.

The extension, declared media type, and content signature must agree. An empty
file, binary/control-character TXT, malformed PDF, scanned/image-only PDF,
unsafe file, or file whose text cannot be extracted is rejected.

Success is `201 Created`. The `Location` header identifies the document, but
there is no public document GET/download route.

```json
{
  "documentId": "e8371933-f732-42d7-a0b2-a17d2c6b3825",
  "contentType": "text/plain",
  "sizeBytes": 4172,
  "uploadedAt": "2026-09-02T15:20:00+00:00",
  "expiresAt": "2026-09-03T15:20:00+00:00",
  "status": "active"
}
```

The response does not return filename, extracted text, blob key/URI, patient
ID, analysis ID, or a download URL.

Upload errors:

| Status | Code/condition | Frontend handling |
|---|---|---|
| `400` | malformed multipart or missing required `file` binding | Ask the user to select the file again |
| `413` | `ai.document.too_large` or server multipart ceiling | Enforce/display 25 MiB and do not retry unchanged |
| `415` | `ai.document.unsupported_media` | Request matching text-native PDF or UTF-8 TXT |
| `422` | `ai.document.single_file_required` | Send exactly one file and no other form field |
| `422` | `ai.document.empty`, `ai.document.size_mismatch` | Ask for another file |
| `422` | `ai.document.file_unsafe` | Show neutral unsafe/unverifiable-file copy |
| `422` | `ai.document.unusable_text` | Request another text-based file |

Client-side size/type checks improve UX but do not replace backend validation.

### 5.2 Scanned PDFs / OCR

OCR is unsupported. Scanned or image-only PDFs are rejected with `422
ai.document.unusable_text`. Ask the user for a text-based PDF or UTF-8 TXT.
Do not send JPG/PNG/DOCX, run browser OCR as a Phase 10 workaround, or treat a
`.pdf` extension as proof that the file is usable.

### 5.3 Expiry

`expiresAt` is exactly `uploadedAt + 24 hours`. Backend cleanup physically
deletes the private blob no later than that lifecycle boundary and retains only
minimal lifecycle metadata. Access does not extend expiry; Phase 10 exposes no
document-read operation. Starting a Second Opinion does not extend expiry.

The frontend should derive any countdown/warning only from `expiresAt`, while
treating the server as authoritative. Because no list/get document endpoint
exists, keep uploaded metadata in the active form/session state according to
the frontend's privacy policy. Do not promise recovery after a page reload.

A document selected for one Second Opinion is associated with that analysis
and cannot start another new analysis. The selected document must still be
active, unexpired, owner-visible, and unassociated when the request starts.
Extracted document text used by Second Opinion is limited to 64,000 characters;
an otherwise accepted upload that exceeds that usable analysis bound receives
`422 ai.second_opinion.document_text_unavailable` when selected.

The normalized original analysis input is frozen before execution. A durable
result survives source deletion/expiry, and regeneration never reads or
requires the original blob.

### 5.4 Delete document

`DELETE /api/v1/ai/documents/{id}` is owner-only and returns `204 No Content`.
It physically removes the private blob first and marks retained lifecycle
metadata deleted. Repeating deletion as the owner is idempotent `204`, as is
deleting retained metadata after automatic expiry. A foreign or unknown ID is
concealed as `404 ai.document.not_found`.

On `204`, remove the metadata from active selection state. If it was already
used for a completed Second Opinion, do not remove or invalidate that result.

## 6. Second Opinion

### 6.1 Request Second Opinion

`POST /api/v1/ai/second-opinions`

```json
{
  "patientId": "9fb45f87-f9dd-4dbf-a2d5-b2be4cd41624",
  "text": "Please help me understand the clinician's observations.",
  "documentIds": ["e8371933-f732-42d7-a0b2-a17d2c6b3825"],
  "preTriageSessionId": "a63dd8e7-4d0b-4d98-ad47-7dcc215afdea",
  "clinicalHistoryEventIds": [
    "672502d7-2f87-4496-980f-d58132975075"
  ]
}
```

| JSON field | Type | Required | Contract |
|---|---|---:|---|
| `patientId` | UUID | Yes | Current primary/active-manager patient authority required |
| `text` | string or `null` | No | Meaningful trimmed text, maximum 8,000 characters |
| `documentIds` | UUID array or `null` | No | Zero or one unique, active, unexpired, unassociated document owned by the account |
| `preTriageSessionId` | UUID or `null` | No | One authorized completed Pre-Triage session for the same patient |
| `clinicalHistoryEventIds` | UUID array or `null` | No | Zero to three unique authorized events for the same patient |

At least one non-demographic source must be meaningful: `text`, one document,
one Pre-Triage session, or at least one Clinical History event. Optional fields
may be omitted; sending `null` is accepted by the DTO, but omission is
recommended. All selected UUIDs must be non-empty and arrays must not contain
duplicates. Unknown JSON fields are rejected.

Authorization/mismatch of patient, document, Pre-Triage, or Clinical History
sources is concealed as `404`. Deleted/expired/already-used documents return
`422 ai.second_opinion.document_unavailable`; missing/unextractable/over-64,000
character document text returns `422
ai.second_opinion.document_text_unavailable`. More than one document returns
`422 ai.second_opinion.document_limit`; more than three history events returns
`422 ai.second_opinion.history_limit`.

An authorized but incomplete Pre-Triage selection can surface the shared
Pre-Triage `409` state-conflict behavior, although `409` is not currently
declared for this operation in Phase 10 OpenAPI. The frontend should offer only
completed sessions.

### 6.2 Accepted response

The endpoint executes before responding, then returns `202 Accepted`, a
`Location` header equal to `statusUrl`, and:

```json
{
  "analysisId": "97c61f6e-acf7-4e99-9ea0-cb672904c81e",
  "executionId": "e12565e6-7201-47e2-adf1-ebaf7891eaae",
  "status": "succeeded",
  "statusUrl": "/api/v1/ai/second-opinions/97c61f6e-acf7-4e99-9ea0-cb672904c81e"
}
```

The receipt status is terminal and is exactly `succeeded`, `rejected`, or
`failed`. It does not contain the structured result, safe fallback, generation
timestamp, or metadata. Call `GET statusUrl` after the response to read the
public projection. This is normally one follow-up read, not periodic polling.

### 6.3 Get Second Opinion

`GET /api/v1/ai/second-opinions/{id}` can serialize these status values:

| Status | Result | Metadata | Safe message | UI behavior |
|---|---|---|---|---|
| `pending` | Omitted | Omitted | Omitted | Show pending; a later GET may be used |
| `running` | Omitted | Omitted | Omitted | Show processing; a later GET may be used |
| `succeeded` | Present | Present | Omitted | Render structured result and metadata |
| `failed` | Omitted | Omitted | Omitted | Show generic retry-later/product failure UI; never expose provider detail |
| `rejected` | Omitted | Omitted | Present | Render only `safeMessage` |

Nullable fields are omitted rather than emitted as `null`. The current public
POST path returns only after terminal completion, so `pending` and `running`
are representable read states but are not normal receipt statuses.

Missing, foreign, or no-longer-patient-authorized analyses return concealed
`404 ai.second_opinion.not_found`.

### 6.4 Structured result

A successful read has this shape:

```json
{
  "analysisId": "97c61f6e-acf7-4e99-9ea0-cb672904c81e",
  "patientId": "9fb45f87-f9dd-4dbf-a2d5-b2be4cd41624",
  "executionId": "e12565e6-7201-47e2-adf1-ebaf7891eaae",
  "status": "succeeded",
  "result": {
    "summary": "A safety-approved educational summary.",
    "importantPoints": ["A relevant point to discuss with the doctor."],
    "possibleQuestionsForDoctor": ["What context would help clarify this?"],
    "missingInformation": [],
    "disclaimer": "This is not a medical diagnosis. Beeexy AI offers educational insights based on clinical literature, not a substitute for a licensed physician. Always discuss results with your doctor."
  },
  "metadata": {
    "aiGenerated": true,
    "generatedAt": "2026-09-02T15:30:00+00:00",
    "resultVersion": "ai-second-opinion-result@v1",
    "provider": "opaque-backend-provider-id",
    "modelVersion": "opaque-backend-model-id",
    "promptVersion": "ai-second-opinion@v1",
    "disclaimerVersion": "ai-second-opinion-disclaimer-v1"
  }
}
```

`summary` is a string. The three semantic sections are arrays of strings; each
array has at most 20 entries in a structurally approved result. The provider
and model fields are omitted if backend provenance is unavailable. Treat their
values as display metadata, not as model-selection inputs.

The disclaimer and version are distinct from the conversation disclaimer.
Render the returned result disclaimer exactly. The API does not expose a
standalone generation date outside `metadata.generatedAt`, confidence score,
citations, diagnosis field, disease probabilities, specialty field, urgency,
source list, snapshot history, or raw provider response.

### 6.5 Safety rendering

Render only `result` when `status === "succeeded"`, or only `safeMessage` when
it is present for a rejected result. Never parse provider output—the provider
payload is not public. Never infer urgency, diagnosis, rejection category, or
recommended treatment from the text.

For `failed`, no safe message is currently exposed. Use frontend-owned generic
failure chrome without fabricating medical guidance. For `rejected`, the fixed
safe message can be either the generic Beeexy rejection copy or the critical
Beeexy-controlled fallback. It is not the rejected model output.

### 6.6 Possible causes / specialty

Possible causes can appear only as qualified possibilities inside the
structured text sections; they are not diagnoses. A specialty suggestion may
appear only as an informational topic to discuss with the user's doctor. It is
not a referral, appointment order, or confirmed care recommendation.

There are no dedicated `possibleCauses`, `diagnoses`, or `specialty` fields.

> Not currently exposed by the Phase 10 backend API.

### 6.7 Missing information

Insufficient information is represented as a normal successful result. The
backend-approved explanation appears in `summary` and/or entries in
`result.missingInformation`; there is no separate `insufficient` status. An
empty `missingInformation` array is valid.

## 7. Regeneration

`POST /api/v1/ai/second-opinions/{id}/regenerate` is Bearer-secured and accepts
no request body. Send `null`/no content, not `{}` and not replacement fields.
A non-empty body returns `422
ai.second_opinion.regeneration_body_not_allowed`.

The response is `202 Accepted` with the same terminal receipt shape as initial
creation. `analysisId` and `statusUrl` remain the original analysis; each
attempt has a new `executionId`.

```json
{
  "analysisId": "97c61f6e-acf7-4e99-9ea0-cb672904c81e",
  "executionId": "8ec464c6-9c90-4cb7-9c3f-13c197e95dd6",
  "status": "succeeded",
  "statusUrl": "/api/v1/ai/second-opinions/97c61f6e-acf7-4e99-9ea0-cb672904c81e"
}
```

Frontend flow:

1. Disable Regenerate while the request is active.
2. Send a bodyless POST.
3. On `202`, inspect the attempt's terminal `status`.
4. Read `statusUrl` to get the latest approved public result.
5. On `409 ai.second_opinion.execution_conflict`, keep the existing result and
   allow a deliberate later retry after the active attempt completes.

Missing, foreign, or revoked-patient access is concealed as `404
ai.second_opinion.not_found`. An invalid/corrupt frozen input returns `422
ai.second_opinion.immutable_input_invalid` before provider execution.

**Regeneration reuses the original immutable input and does not automatically
incorporate later patient/context changes.** It does not read or extend the
Temporary Document blob and never requires re-upload. Later demographics,
Pre-Triage, Clinical History, document state, and conversation state are not
silently included.

An approved regeneration appends a new immutable snapshot. Failed or rejected
attempts do not replace prior approved snapshots. When a prior success exists,
`GET statusUrl` deliberately returns the latest approved snapshot and its own
execution/metadata even if the just-finished regeneration receipt says
`failed` or `rejected`. Preserve and continue displaying that prior result;
the receipt describes the attempted regeneration, while GET describes the
current displayable result.

## 8. Async/polling behavior

| `202` operation | When response is sent | How completion/result is observed | Polling required? |
|---|---|---|---|
| Send conversation message | After execution is terminal | Response contains status and optional assistant message; detail may be refetched | No |
| Request Second Opinion | After execution is terminal | Receipt status, then GET `statusUrl` for result/fallback | No periodic polling; one read normally |
| Regenerate Second Opinion | After execution is terminal | Receipt status, then GET `statusUrl` for latest approved result | No periodic polling; one read normally |

`GET /api/v1/ai/second-opinions/{id}` is the only Phase 10 status/read endpoint
and can represent `pending` and `running`. If a GET actually returns either,
the frontend may re-query using its existing cancellable status-query policy
and must stop on `succeeded`, `failed`, `rejected`, `404`, or navigation/account
change. The backend publishes no polling interval, `Retry-After` value, SSE,
WebSocket, webhook, or execution-status URL for conversation messages. Do not
invent one.

Network errors are ambiguous transport outcomes. Do not automatically submit a
new message, Second Opinion, or regeneration in a hidden retry, because a
provider execution may already have been recorded. For a known Second Opinion
ID, a safe read of `statusUrl` can reconcile state. On `409`, do not launch a
parallel attempt.

## 9. Error handling / ProblemDetails

Application and status-code errors use `application/problem+json`. The shared
shape is:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
  "title": "Request validation failed.",
  "status": 422,
  "detail": "The AI conversation request cannot be processed.",
  "instance": "/api/v1/ai/conversations/{id}/messages",
  "errorCode": "ai.conversation.request_not_supported",
  "correlationId": "f07127f4d8b04e7b965cc67fbd5b0d67"
}
```

`instance` is the request path. `correlationId` is also returned in the
`X-Correlation-ID` header. `errorCode` is present for mapped application errors
but not every framework/auth/routing error. Phase 10 semantic validation uses
one safe `detail` and `errorCode`; it does not currently expose a field-level
`errors` dictionary or a separate `traceId`. Do not display raw `detail` for
unexpected failures without the existing frontend's approved error-copy rules.

| Status | Current Phase 10 use | Recommended handling |
|---|---|---|
| `400` | Malformed JSON, query conversion, multipart, or required-body binding on endpoints which declare it | Fix request construction; do not retry unchanged |
| `401` | Missing/invalid/expired Bearer on every operation | Refresh or sign in; clear patient-bound AI cache when session changes |
| `403` | Not used by Phase 10 | Keep generic client support, but do not expect it for Phase 10 authority failures |
| `404` | Concealed missing/foreign/deleted/revoked resource or route mismatch | Treat as one neutral unavailable state |
| `409` | Active conversation execution or regeneration; shared incomplete Pre-Triage state can also surface during Second Opinion input assembly | Disable duplicate action, preserve current state, retry deliberately |
| `413` | Temporary Document too large/request ceiling | Select a file no larger than 26,214,400 bytes |
| `415` | Unsupported/spoofed document media | Select matching text-native PDF or UTF-8 TXT |
| `422` | Semantic request, policy, limits, document usability, or immutable-input validation | Branch on known `errorCode`; backend remains authoritative |
| `429` | Not currently used by Phase 10 endpoints | If introduced by shared infrastructure, honor `Retry-After`; do not assume Phase 10 semantics |
| `500` | Unexpected server failure | Show generic error and retain `correlationId` for support |
| `502` / `503` | Not currently exposed as Phase 10 provider outcomes | Provider failures normally return `202` with terminal `failed`; keep generic infrastructure handling |

Important mapped codes include:

- Conversations: `ai.conversation.purpose_invalid`,
  `ai.conversation.message_invalid`,
  `ai.conversation.request_not_supported`,
  `ai.conversation.message_limit_reached`,
  `ai.conversation.page_size_invalid`, `ai.conversation.cursor_invalid`,
  `ai.conversation.unsupported_field`, `ai.conversation.not_found`, and
  `ai.conversation.execution_conflict`.
- Documents: `ai.document.single_file_required`, `ai.document.too_large`,
  `ai.document.unsupported_media`, `ai.document.empty`,
  `ai.document.size_mismatch`, `ai.document.file_unsafe`,
  `ai.document.unusable_text`, and `ai.document.not_found`.
- Second Opinion: `ai.second_opinion.unsupported_field`,
  `ai.second_opinion.source_ids_invalid`, `ai.second_opinion.input_required`,
  `ai.second_opinion.text_invalid`, `ai.second_opinion.document_limit`,
  `ai.second_opinion.history_limit`, `ai.second_opinion.document_unavailable`,
  `ai.second_opinion.document_text_unavailable`,
  `ai.second_opinion.not_found`,
  `ai.second_opinion.execution_conflict`,
  `ai.second_opinion.regeneration_body_not_allowed`, and
  `ai.second_opinion.immutable_input_invalid`.

Patient authorization failures may use the concealed patient `404` without an
`errorCode`; never make access logic depend solely on the extension.

## 10. Consolidated TypeScript contracts

These types match the actual camelCase public JSON. `Uuid` and `IsoInstant` are
strings at runtime.

```ts
export type Uuid = string;
export type IsoInstant = string;

export type AiConversationPurpose =
  | "GENERAL_HEALTH"
  | "MEDICAL_TERMS"
  | "SYMPTOM_DISCUSSION"
  | "CLINICIAN_QUESTIONS";

export type AiMessageRole = "user" | "assistant";
export type AiConversationExecutionStatus =
  | "completed"
  | "rejected"
  | "failed";

export interface AiDisclaimer {
  version: string;
  content: string;
}

export interface CreateAiConversationRequest {
  purpose: AiConversationPurpose;
  patientId?: Uuid | null;
}

export interface AiConversation {
  conversationId: Uuid;
  patientId: Uuid | null;
  createdAt: IsoInstant;
  disclaimer: AiDisclaimer;
}

export interface AiConversationSummary {
  conversationId: Uuid;
  patientId: Uuid | null;
  createdAt: IsoInstant;
}

export interface AiConversationPage {
  items: AiConversationSummary[];
  nextCursor: string | null;
}

export interface ListAiConversationsQuery {
  cursor?: string;
  pageSize?: number;
}

export interface AiConversationMessage {
  messageId: Uuid;
  role: AiMessageRole;
  content: string;
  sequence: number;
  createdAt: IsoInstant;
}

export interface AiConversationDetail {
  conversation: AiConversationSummary;
  messages: AiConversationMessage[];
  disclaimer: AiDisclaimer;
}

export interface SendAiConversationMessageRequest {
  content: string;
}

export interface AiConversationExecution {
  conversationId: Uuid;
  userMessageId: Uuid;
  executionId: Uuid;
  status: AiConversationExecutionStatus;
  // Omitted for failed or structurally rejected output.
  assistantMessage?: AiConversationMessage;
  disclaimer: AiDisclaimer;
}

export type AiDocumentStatus = "active" | "deleted" | "expired";

export interface AiDocument {
  documentId: Uuid;
  contentType: "application/pdf" | "text/plain";
  sizeBytes: number;
  uploadedAt: IsoInstant;
  expiresAt: IsoInstant;
  status: AiDocumentStatus;
}

export interface RequestSecondOpinionRequest {
  patientId: Uuid;
  text?: string | null;
  documentIds?: Uuid[] | null;
  preTriageSessionId?: Uuid | null;
  clinicalHistoryEventIds?: Uuid[] | null;
}

export type SecondOpinionTerminalStatus =
  | "succeeded"
  | "failed"
  | "rejected";

export type SecondOpinionStatus =
  | "pending"
  | "running"
  | SecondOpinionTerminalStatus;

export interface SecondOpinionAccepted {
  analysisId: Uuid;
  executionId: Uuid;
  status: SecondOpinionTerminalStatus;
  statusUrl: string;
}

export interface SecondOpinionResult {
  summary: string;
  importantPoints: string[];
  possibleQuestionsForDoctor: string[];
  missingInformation: string[];
  disclaimer: string;
}

export interface SecondOpinionMetadata {
  aiGenerated: boolean;
  generatedAt: IsoInstant;
  resultVersion: string;
  // Omitted only when backend provenance is unavailable.
  provider?: string;
  modelVersion?: string;
  promptVersion?: string;
  disclaimerVersion: string;
}

export interface SecondOpinion {
  analysisId: Uuid;
  patientId: Uuid;
  executionId?: Uuid;
  status: SecondOpinionStatus;
  result?: SecondOpinionResult;
  metadata?: SecondOpinionMetadata;
  safeMessage?: string;
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

There is no request body type for regeneration and no JSON request type for
upload. Upload uses the browser's `FormData`; delete operations return no body.

## 11. Recommended frontend API-client functions

Adapt these signatures to the existing Beeexy API wrapper rather than adding a
new HTTP/state library:

```ts
createAiConversation(
  request: CreateAiConversationRequest,
  signal?: AbortSignal,
): Promise<AiConversation>;

listAiConversations(
  query?: ListAiConversationsQuery,
  signal?: AbortSignal,
): Promise<AiConversationPage>;

getAiConversation(
  conversationId: Uuid,
  signal?: AbortSignal,
): Promise<AiConversationDetail>;

sendAiConversationMessage(
  conversationId: Uuid,
  request: SendAiConversationMessageRequest,
  signal?: AbortSignal,
): Promise<AiConversationExecution>;

deleteAiConversation(
  conversationId: Uuid,
  signal?: AbortSignal,
): Promise<void>;

uploadAiDocument(
  file: File,
  signal?: AbortSignal,
): Promise<AiDocument>;

deleteAiDocument(
  documentId: Uuid,
  signal?: AbortSignal,
): Promise<void>;

requestSecondOpinion(
  request: RequestSecondOpinionRequest,
  signal?: AbortSignal,
): Promise<SecondOpinionAccepted>;

getSecondOpinion(
  analysisId: Uuid,
  signal?: AbortSignal,
): Promise<SecondOpinion>;

regenerateSecondOpinion(
  analysisId: Uuid,
  signal?: AbortSignal,
): Promise<SecondOpinionAccepted>;
```

Implementation matrix:

| Function | Method/path | Special handling |
|---|---|---|
| `createAiConversation` | `POST /api/v1/ai/conversations` | Capture body; `Location` is detail URL |
| `listAiConversations` | `GET /api/v1/ai/conversations` | Omit undefined query values; cursor is opaque |
| `getAiConversation` | `GET /api/v1/ai/conversations/{id}` | Concealed `404` evicts stale detail |
| `sendAiConversationMessage` | `POST .../{id}/messages` | `202` body is terminal; handle `409`; no polling |
| `deleteAiConversation` | `DELETE .../{id}` | Accept idempotent `204`; evict list/detail |
| `uploadAiDocument` | `POST /api/v1/ai/documents` | `FormData` field `file`; browser sets boundary; handle `413/415/422` |
| `deleteAiDocument` | `DELETE .../{id}` | Accept idempotent owner `204`; clear selection |
| `requestSecondOpinion` | `POST /api/v1/ai/second-opinions` | Read terminal receipt, then GET `statusUrl` |
| `getSecondOpinion` | `GET .../{id}` | Discriminated rendering by `status` |
| `regenerateSecondOpinion` | `POST .../{id}/regenerate` | No body; handle `409`; GET latest approved result |

Every function should reuse the existing Bearer-token, API-origin,
ProblemDetails, and correlation-ID behavior. Pass `AbortSignal` through to
`fetch`. A client cancellation does not prove the backend execution was
cancelled before persistence; do not silently repeat mutations.

## 12. Frontend state recommendations

### Conversations

Keep server data and transient UI state separate:

- server: paginated summaries, opaque cursor, selected detail/messages;
- UI: list/detail loading, draft, submitting message, deleting;
- execution: request active, terminal response, `409` banner/action;
- safety: render returned assistant content and disclaimer without local
  classification; and
- deletion: optimistic hiding is reasonable only if rollback occurs on failure;
  always evict on `204`.

Do not insert an assistant placeholder as durable server data. A failed or
structurally rejected execution creates no assistant message.

### Documents

Track the local `File`, upload progress/loading, returned `AiDocument`, expiry
warning, selected/deleting state, and mapped validation error separately. Once
uploaded, use `documentId`; never retain or fabricate private blob URLs.

Use 26,214,400 bytes for an early UX check, while preserving backend authority.
Clear expired/deleted selections and require another upload for a new initial
Second Opinion. Do not re-upload for regeneration.

### Second Opinion

Track selected patient, typed text, at most one returned document ID, one
completed Pre-Triage session ID, up to three Clinical History event IDs,
submitting state, receipt, server result, and regeneration state.

Model `pending`, `running`, `succeeded`, `failed`, and `rejected` exhaustively.
Only `succeeded` has `result`/`metadata`; only a rejection without a prior
approved snapshot exposes `safeMessage`. Keep a prior successful view while a
regeneration request is active and retain it if the receipt fails/rejects.

## 13. Flow diagrams

### AI Conversation

```text
Create conversation (201 + disclaimer)
  -> send allowed health message
  -> backend runs provider + structure + safety before responding
  -> 202 terminal response
  -> render approved assistant text/fixed fallback, or failed/rejected state
  -> optionally refetch detail and continue AI History
```

### Temporary Document

```text
Select one PDF/TXT
  -> client checks basic type/<=25 MiB
  -> upload field "file"
  -> backend validates type/signature/safety/useful text
  -> 201 temporary metadata with expiresAt
  -> use once in initial Second Opinion or delete
  -> physical deletion no later than upload + 24h
```

### Second Opinion

```text
Select authorized patient + at least one input source
  -> POST Second Opinion
  -> backend completes execution
  -> 202 terminal receipt with statusUrl
  -> GET statusUrl
  -> render structured safe result or backend-safe state
```

### Regenerate

```text
Regenerate with no body
  -> backend reuses original immutable input and completes execution
  -> 202 terminal attempt receipt
  -> GET original statusUrl
  -> show newest approved result
  -> prior successful result remains preserved on failed/rejected attempt
```

## 14. UX rules from backend constraints

- Never present conversation or Second Opinion output as a diagnosis.
- Display the exact backend-provided disclaimer where its response exposes it.
- Never calculate urgency or emergency classification from AI text.
- Allow at most one Temporary Document in an initial Second Opinion.
- OCR and image-only PDFs are unsupported.
- Treat `expiresAt` as authoritative; document blobs do not survive beyond the
  backend retention lifecycle.
- Never automatically re-upload a document for regeneration.
- Treat specialty language as informational discussion, not a referral.
- Do not request or display raw audit, provider, prompt, safety-rule, or
  rejected-output data.
- Do not recreate backend safety or request-policy logic in the browser.
- Preserve returned ordering and opaque cursor semantics.

## 15. Frontend security/privacy

- Reuse the existing Beeexy auth-token storage and refresh behavior.
- Do not log request/response bodies, health text, conversation messages,
  document metadata, or Second Opinion content to analytics/error tools.
- Do not add localStorage/IndexedDB persistence of raw health or AI content
  unless the existing approved architecture explicitly requires it.
- Redact Bearer tokens and avoid patient/health data in correlation IDs.
- Do not expose raw API errors; map known safe codes to product copy and retain
  only the correlation ID needed for support.
- Respect concealed `404`; never probe to differentiate ownership from absence.
- Never call the AI provider from the browser or expose prompts, provider
  payloads, private blob paths, API keys, or model credentials.
- Do not build direct database or object-storage access. All access goes through
  the public authenticated API.
- Render all returned strings as text, not unsanitized HTML.

## 16. Endpoint-to-screen mapping

This is integration guidance, not a final UI design.

| Likely UI area | Backend operations |
|---|---|
| AI entry/home | Create conversation; open conversation list; open Second Opinion form |
| Conversation list | List conversations; soft-delete conversation |
| Conversation detail/chat | Get detail; send message; show returned disclaimer/fallback |
| Temporary Document uploader | Upload one document; show expiry; delete document |
| Second Opinion form | Select patient/text/document/Pre-Triage/Clinical History; request analysis |
| Second Opinion status/result | Get by `analysisId`; render status/result/metadata/safe message |
| Regenerate action | Bodyless regeneration; handle conflict; refetch latest approved result |

## 17. What frontend must NOT implement

- OCR or scanned-image extraction;
- direct AI-provider calls, provider credentials, or prompt construction;
- browser-side authoritative AI safety, diagnosis, or urgency logic;
- direct private blob, object-storage, or database access;
- more than one document per initial Second Opinion;
- automatic Clinical History promotion/amendments;
- automatic FHIR generation or mapping;
- multi-provider/model selection or consensus;
- provider-specific controls based on opaque metadata;
- public snapshot/audit history not exposed by the API;
- regeneration from newer patient, Pre-Triage, Clinical History, document, or
  conversation state; or
- hidden automatic retries of mutation/provider-execution endpoints.

## 18. Frontend integration acceptance checklist

- [ ] All 10 actual Phase 10 operations are represented in the API client
- [ ] Existing Bearer authentication and session-change invalidation are reused
- [ ] Request/response types use exact camelCase backend fields
- [ ] Conversation ownership and concealed `404` are handled
- [ ] Message `409` is handled without a parallel retry
- [ ] The 50-message limit is handled
- [ ] Conversation soft-delete/hide and idempotent `204` are supported
- [ ] One PDF/TXT is uploaded with form field `file`
- [ ] 25 MiB / 26,214,400-byte UX validation is present
- [ ] Backend remains authoritative for file and semantic validation
- [ ] `expiresAt` and absent document list/get recovery are handled
- [ ] Manual document delete clears frontend selection
- [ ] Actual Second Opinion input fields and limits are used
- [ ] At most one document and three Clinical History events are selected
- [ ] Terminal receipt and all five GET statuses are handled
- [ ] Structured result fields are rendered without invented fields
- [ ] Disclaimer and AI metadata are rendered from the response
- [ ] Failed and rejected states never expose/expect raw output
- [ ] Bodyless regeneration is implemented
- [ ] Regeneration `409` preserves the current result
- [ ] Regeneration never requires/re-uploads the source document
- [ ] No OCR or rejected-output handling is added
- [ ] No Clinical History/FHIR promotion is added
- [ ] No provider secret/browser provider call is added
- [ ] Requests accept `AbortSignal` and ambiguous cancellations are not hidden-retried
- [ ] OpenAPI/backend contracts are checked during frontend implementation

## 19. Source-of-truth appendix

The following backend files were inspected for this contract:

- `IMPLEMENTATION_PLAN_01-09-2026-1345.md` — Phase 10.1–10.8 scope,
  completion evidence, final matrix, and deferred capabilities.
- `src/Beeexy.Api/Ai/AiConversationEndpointExtensions.cs` — conversation
  routes, request/response DTOs, status and disclaimer mapping.
- `src/Beeexy.Api/Ai/AiDocumentEndpointExtensions.cs` — multipart field,
  upload/delete routes, size ceiling, and response DTO.
- `src/Beeexy.Api/Ai/SecondOpinionEndpointExtensions.cs` — request,
  read/regenerate routes, DTOs, omission behavior, statuses, and status URL.
- `src/Beeexy.Application/Ai/AiConversationContracts.cs`,
  `AiConversationUseCases.cs`, `AiConversationRequestPolicy.cs`, and
  `AiConversationPrompt.cs` — limits, cursor, ordering, policy, terminal
  execution behavior, and safe assistant projection.
- `src/Beeexy.Application/Ai/AiDocumentContracts.cs` and
  `AiDocumentUseCases.cs`, plus
  `src/Beeexy.Infrastructure/Ai/AiDocumentValidation.cs` — 25 MiB, types,
  extraction, safety, metadata, deletion, and retention semantics.
- `src/Beeexy.Application/Ai/SecondOpinionContracts.cs`,
  `SecondOpinionInputAssembler.cs`, `SecondOpinionPrompt.cs`, and
  `SecondOpinionUseCases.cs` — inputs, limits, immutable replay, structured
  result, statuses, fallback, disclaimer, and metadata.
- `src/Beeexy.Application/Ai/AiSafetyContracts.cs` and
  `ExecuteSafeAiAnalysis.cs` — approved/fallback behavior and product content.
- `src/Beeexy.Domain/Ai/AiEnums.cs`, `AiConversation.cs`, and
  `AiUploadedDocument.cs` — state values, logical deletion, and document
  lifecycle invariants.
- `src/Beeexy.Infrastructure/Ai/AiConversationRepository.cs` and
  `SecondOpinionRepository.cs` — ordering, ownership, current displayable
  result selection, and regeneration preservation.
- `src/Beeexy.Api/Errors/ApiExceptionHandler.cs`,
  `src/Beeexy.Api/Middleware/CorrelationIdMiddleware.cs`, and
  `src/Beeexy.Api/Program.cs` — Bearer, Problem Details, correlation, JSON, and
  status-code behavior.
- `tests/Beeexy.Tests.Integration/Api/AiConversationEndpointTests.cs`,
  `AiDocumentEndpointTests.cs`, `SecondOpinionEndpointTests.cs`,
  `Phase10AcceptanceTests.cs`, `OpenApiAndCorsTests.cs`, and
  `ProblemDetailsAndLoggingTests.cs` — accepted public behavior and OpenAPI
  verification.

### Implementation-plan versus actual API

The accepted implementation resolves or narrows several plan-level statements:

1. The original baseline route list omitted a way to satisfy conversation
   soft-delete. The accepted additive `DELETE
   /api/v1/ai/conversations/{id}` route exists, making 10 operations total.
2. The plan describes `202` AI execution semantics, while the current handlers
   await provider completion and return terminal receipts. There is no queued
   job or required polling loop.
3. Conversation detail exposes ordered messages, not result snapshots or
   execution/audit history.
4. The implemented conversation disclaimer is the centralized
   `ai-general-disclaimer-v1` Spanish text documented above; it is not the
   shorter conversation-start wording originally written in the Phase 10.4
   planning prose.
5. Initial Second Opinion and regeneration actually expose `executionId` and
   `statusUrl`, and set the HTTP `Location` header.
6. An incomplete selected Pre-Triage session can produce a shared `409` from
   the reused Pre-Triage read boundary, although the Phase 10 OpenAPI operation
   currently lists `400/401/404/422/500` rather than `409` for initial request.

## 20. Accuracy requirements

This guide intentionally documents only the current public Phase 10 API:

- 10 Bearer-secured operations across eight OpenAPI paths;
- exact camelCase fields and explicitly mapped lowercase statuses/roles;
- conversation keyset pagination and no other Phase 10 pagination;
- form field `file`, text-native PDF/TXT, and 26,214,400 bytes;
- immediate terminal `202` receipts with no required periodic polling;
- concealed ownership/patient `404`, execution `409`, and semantic `422`;
- immutable regeneration from original input; and
- only backend-approved result/fallback/disclaimer content.

OCR, download/list document APIs, conversation execution polling, result
snapshot history, safety-reason/audit output, confidence/citations, diagnosis,
urgency, dedicated possible-cause/specialty fields, multi-provider selection,
Clinical History promotion, and AI-to-FHIR mapping are not currently exposed
by the Phase 10 backend API.
