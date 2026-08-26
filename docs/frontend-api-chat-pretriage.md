# Chat Pre-Triage frontend API contract

This is the implemented backend contract for frontend Chat Pre-Triage Parts 5–8. The backend projects the existing `PreTriageSession`, pinned questionnaire/rule-set, and accepted answers into a conversation state. It does not persist messages or run a second chat workflow.

Base path: `/api/v1`  
JSON: `Content-Type: application/json`

## Entry choices

The quick replies are exactly:

```text
Headache
Stomach pain
Chest pain
Fever
Other
```

Map them to these authoritative pathway codes:

| Quick reply | `pathway` |
|---|---|
| Headache | `HEADACHE` |
| Stomach pain | `ABDOMINAL_PAIN` |
| Chest pain | `CHEST_PAIN` |
| Fever | `FEVER` |
| Other | `OTHER_SYMPTOMS` |

Quick replies are deterministic and must not go through AI:

```http
POST /api/v1/pre-triage/sessions

{
  "pathway": "ABDOMINAL_PAIN"
}
```

`201 Created` returns the existing session contract plus `conversation`:

```json
{
  "sessionId": "40000000-0000-0000-0000-000000000004",
  "pathway": "ABDOMINAL_PAIN",
  "status": "Active",
  "expiresAt": "2026-08-27T18:00:00Z",
  "questionnaire": {
    "code": "abdominal-pain-demo-questionnaire",
    "version": "2026.08.22-demo.1"
  },
  "ruleSet": {
    "code": "abdominal-pain-demo-neutral-rules",
    "version": "2026.08.22-demo.1"
  },
  "clinicalContent": {
    "source": "PRODUCT_DEMO_DEFINED",
    "reviewStatus": "NOT_APPLICABLE",
    "clinicalApproval": "NOT_CLINICALLY_APPROVED"
  },
  "anonymousCapability": "<one-time-secret>",
  "conversation": {
    "sessionId": "40000000-0000-0000-0000-000000000004",
    "sessionStatus": "ACTIVE",
    "state": "IN_PROGRESS",
    "expiresAt": "2026-08-27T18:00:00Z",
    "pathway": {
      "code": "ABDOMINAL_PAIN",
      "label": "Stomach pain"
    },
    "questionnaire": {
      "code": "abdominal-pain-demo-questionnaire",
      "version": "2026.08.22-demo.1"
    },
    "ruleSet": {
      "code": "abdominal-pain-demo-neutral-rules",
      "version": "2026.08.22-demo.1"
    },
    "progress": {
      "completed": 0,
      "total": 3,
      "percentage": 0
    },
    "acceptedValues": {},
    "nextInteraction": {
      "field": "duration",
      "questionCode": "DURATION",
      "prompt": "How long ago did the stomach pain start?",
      "inputType": "DURATION",
      "required": true,
      "constraints": {
        "minimum": 0,
        "exclusiveMinimum": true,
        "allowedUnits": ["MINUTES", "HOURS", "DAYS", "WEEKS", "MONTHS"]
      },
      "options": []
    }
  }
}
```

Authenticated start sends the existing Bearer token. Omit `patientId` for the caller's primary patient, or send an authorized managed PatientProfile UUID. Authenticated responses include `patientId` and omit `anonymousCapability`.

## Free-text entry

```http
POST /api/v1/pre-triage/intake
Idempotency-Key: 7a0a7680-0ac3-4df0-9959-d7137d514d68
Content-Type: application/json

{
  "text": "My stomach has hurt for two days and it is a 6 out of 10."
}
```

The key is required, URL-safe, opaque, and at most 128 characters. A random UUID is recommended.

- New logical intake: generate a new random key.
- Retry the same logical intake: reuse the same key and identical text.
- New intake: generate a new key.
- Never derive a key from symptom text or patient data.
- Reusing a scoped key with different text returns `409 pre_triage.idempotency_key_reused`.

Anonymous intake also sets the secure, HttpOnly `Beeexy.PreTriage.IntakeScope` cookie. The browser must retain and resend it. A committed anonymous replay additionally requires the originally returned `X-Pre-Triage-Capability`; the raw capability cannot be reconstructed by the backend.

### Intake outcomes

| `resolution` | HTTP | Frontend action |
|---|---:|---|
| `RESOLVED` | `201` | Store the session/capability and render top-level `conversation`. |
| `AMBIGUOUS` | `200` | Show the returned pathway choices and ask the user to select/clarify. No session was created. |
| `UNRESOLVED` | `200` | Ask for a clearer supported symptom or offer the five quick replies. No session was created. |

A resolved response retains the existing `session` and `initialAnswers` fields and additively returns one top-level `conversation`:

```json
{
  "resolution": "RESOLVED",
  "session": {
    "sessionId": "40000000-0000-0000-0000-000000000004",
    "pathway": "ABDOMINAL_PAIN",
    "status": "Active",
    "expiresAt": "2026-08-27T18:00:00Z",
    "questionnaire": {
      "code": "abdominal-pain-demo-questionnaire",
      "version": "2026.08.22-demo.1"
    },
    "ruleSet": {
      "code": "abdominal-pain-demo-neutral-rules",
      "version": "2026.08.22-demo.1"
    },
    "clinicalContent": {
      "source": "PRODUCT_DEMO_DEFINED",
      "reviewStatus": "NOT_APPLICABLE",
      "clinicalApproval": "NOT_CLINICALLY_APPROVED"
    },
    "anonymousCapability": "<one-time-secret>"
  },
  "initialAnswers": {
    "sessionId": "40000000-0000-0000-0000-000000000004",
    "pathway": "ABDOMINAL_PAIN",
    "questionnaireVersion": "2026.08.22-demo.1",
    "outcome": "ACCEPTED",
    "acceptedAnswers": ["DURATION", "INTENSITY"],
    "acceptedValues": {
      "duration": { "value": 2, "unit": "DAYS" },
      "intensity": 6
    },
    "progression": {
      "state": "IN_PROGRESS",
      "answeredRequiredFields": ["DURATION", "INTENSITY"],
      "missingRequiredFields": ["ADDITIONAL_SYMPTOMS"],
      "nextQuestion": {
        "code": "ADDITIONAL_SYMPTOMS",
        "prompt": "Do you have any of these additional symptoms?",
        "answerType": "MULTIPLE_CHOICE",
        "allowedValues": ["NAUSEA", "DIARRHEA", "FEVER"],
        "allowedUnits": []
      },
      "readyToComplete": false
    }
  },
  "conversation": {
    "sessionId": "40000000-0000-0000-0000-000000000004",
    "sessionStatus": "ACTIVE",
    "state": "IN_PROGRESS",
    "expiresAt": "2026-08-27T18:00:00Z",
    "pathway": { "code": "ABDOMINAL_PAIN", "label": "Stomach pain" },
    "questionnaire": {
      "code": "abdominal-pain-demo-questionnaire",
      "version": "2026.08.22-demo.1"
    },
    "ruleSet": {
      "code": "abdominal-pain-demo-neutral-rules",
      "version": "2026.08.22-demo.1"
    },
    "progress": { "completed": 2, "total": 3, "percentage": 67 },
    "acceptedValues": {
      "duration": { "value": 2, "unit": "DAYS" },
      "intensity": 6
    },
    "nextInteraction": {
      "field": "additionalSymptoms",
      "questionCode": "ADDITIONAL_SYMPTOMS",
      "prompt": "Do you have any of these additional symptoms?",
      "inputType": "MULTI_SELECT",
      "required": true,
      "constraints": {
        "minimumSelections": 0,
        "maximumSelections": 3,
        "allowsEmptySelection": true
      },
      "options": [
        { "value": "NAUSEA", "label": "Nausea" },
        { "value": "DIARRHEA", "label": "Diarrhea" },
        { "value": "FEVER", "label": "Fever" }
      ]
    }
  }
}
```

If intake accepts only duration, `conversation.nextInteraction.field` is `intensity`. If duration and intensity are accepted, it is `additionalSymptoms`. The frontend must not repeat or skip fields itself.

`AMBIGUOUS` and `UNRESOLVED` responses omit `session`, `initialAnswers`, and `conversation`.

## Conversation projection

Refresh/recovery endpoint:

```http
GET /api/v1/pre-triage/sessions/{sessionId}/conversation
X-Pre-Triage-Capability: <anonymous-only>
Authorization: Bearer <authenticated-only>
```

The projection is read-only, deterministic, and always loaded through the session's pinned questionnaire and matching rule-set. Activating a newer package does not alter an existing session's prompt, options, constraints, order, denominator, or accepted values. Projection makes zero AI/provider calls and creates no History or FHIR state.

### States

| `state` | `sessionStatus` | Meaning |
|---|---|---|
| `IN_PROGRESS` | `ACTIVE` | At least one required value is missing. Exactly one `nextInteraction` is present. |
| `READY_FOR_REVIEW` | `ACTIVE` | All required values are accepted. `nextInteraction` is absent and progress is 100%. The session is not completed. |
| `COMPLETED` | `COMPLETED` | Existing explicit completion occurred. `nextInteraction` is absent and accepted values are immutable. |

Expired sessions do not expose an `EXPIRED` projection. They preserve the existing concealed `404 Pre-triage session not found` behavior and cannot be revived.

### Progress semantics

The current five pinned questionnaires require exactly duration, intensity, and additional symptoms. Therefore `total` is currently `3`. `completed` counts distinct accepted required values, and `percentage` is backend-calculated and rounded to the nearest integer:

```text
0/3 = 0
1/3 = 33
2/3 = 67
3/3 = 100
```

Optional questionnaire fields, if introduced by a future valid package, do not contribute to this denominator. The frontend must not calculate clinical progress.

### Accepted values

Only persisted, backend-validated values appear:

```json
{
  "duration": { "value": 2, "unit": "DAYS" },
  "intensity": 6,
  "additionalSymptoms": ["NAUSEA"]
}
```

Absent values are omitted. AI candidates that were rejected or require clarification never appear as accepted values.

### Input types

`DURATION`:

```json
{
  "field": "duration",
  "questionCode": "DURATION",
  "prompt": "How long ago did the stomach pain start?",
  "inputType": "DURATION",
  "required": true,
  "constraints": {
    "minimum": 0,
    "exclusiveMinimum": true,
    "allowedUnits": ["MINUTES", "HOURS", "DAYS", "WEEKS", "MONTHS"]
  },
  "options": []
}
```

`SCALE`:

```json
{
  "field": "intensity",
  "questionCode": "INTENSITY",
  "prompt": "How intense is it from 1 to 10?",
  "inputType": "SCALE",
  "required": true,
  "constraints": { "minimum": 1, "maximum": 10, "step": 1 },
  "options": []
}
```

`MULTI_SELECT`:

```json
{
  "field": "additionalSymptoms",
  "questionCode": "ADDITIONAL_SYMPTOMS",
  "prompt": "Do you have any of these additional symptoms?",
  "inputType": "MULTI_SELECT",
  "required": true,
  "constraints": {
    "minimumSelections": 0,
    "maximumSelections": 3,
    "allowsEmptySelection": true
  },
  "options": [
    { "value": "NAUSEA", "label": "Nausea" },
    { "value": "DIARRHEA", "label": "Diarrhea" },
    { "value": "FEVER", "label": "Fever" }
  ]
}
```

For primary pathway `FEVER`, options contain only `NAUSEA` and `DIARRHEA`, and `maximumSelections` is `2`.

There is no `NONE` answer code. “None” is canonically represented by `additionalSymptoms: []`; `allowsEmptySelection: true` tells the frontend it may render a None control. Do not submit `"NONE"`.

No current package projects `TEXT` or `SINGLE_SELECT`.

## Answer submission

Reuse the existing endpoint:

```http
POST /api/v1/pre-triage/sessions/{sessionId}/answers
X-Pre-Triage-Capability: <anonymous-only>
Authorization: Bearer <authenticated-only>
Content-Type: application/json
```

Send the pinned questionnaire version when it is available locally. A mismatch returns `422 pre_triage.questionnaire_version_mismatch`.

Duration:

```json
{
  "questionnaireVersion": "2026.08.22-demo.1",
  "structured": {
    "duration": { "value": 2, "unit": "DAYS" }
  }
}
```

Scale:

```json
{
  "questionnaireVersion": "2026.08.22-demo.1",
  "structured": {
    "intensity": 6
  }
}
```

Multi-select:

```json
{
  "questionnaireVersion": "2026.08.22-demo.1",
  "structured": {
    "additionalSymptoms": ["NAUSEA", "DIARRHEA"]
  }
}
```

None:

```json
{
  "questionnaireVersion": "2026.08.22-demo.1",
  "structured": {
    "additionalSymptoms": []
  }
}
```

Multiple currently valid values may be submitted atomically:

```json
{
  "questionnaireVersion": "2026.08.22-demo.1",
  "structured": {
    "duration": { "value": 2, "unit": "DAYS" },
    "intensity": 6,
    "additionalSymptoms": []
  }
}
```

`200 OK` retains the existing `acceptedAnswers`, `acceptedValues`, `progression`, and optional `clarification`, and additively includes `conversation`. Unlike the existing mutation-local `acceptedValues`, `conversation.acceptedValues` contains all values already accepted for the session. Render the next UI from `conversation`, not by applying frontend progression rules.

The endpoint still supports `naturalLanguage` instead of `structured`, but the structured shapes above are the deterministic frontend contract for every projected input type. Never send both modes.

## Review and completion

When `conversation.state` becomes `READY_FOR_REVIEW`:

1. Render the Review UI from `conversation.pathway` and `conversation.acceptedValues`.
2. Do not submit more fields or infer a next question.
3. Do not mark the session completed locally.
4. On explicit user confirmation, call the existing completion endpoint.

```http
POST /api/v1/pre-triage/sessions/{sessionId}/complete
X-Pre-Triage-Capability: <anonymous-only>
Authorization: Bearer <authenticated-only>
```

There is no separate Review mutation endpoint and no automatic completion. Review is the explicit frontend stage between `READY_FOR_REVIEW` and the existing `/complete` call.

Completion returns `201` the first time and the same immutable result with `200` on an authorized repeat. After completion, refresh the conversation endpoint to obtain `state: "COMPLETED"`; it has no `nextInteraction`.

The preserved downstream flow is:

```text
active session → answers → READY_FOR_REVIEW → Review UI → /complete
→ Clinical History for authenticated/claimed patient workflows → existing FHIR export flow
```

Projection and active answer submission themselves create no episode, Clinical History event, or FHIR export.

## Session access

Anonymous sessions return a one-time `anonymousCapability`. Store it only for the current flow and send it in:

```http
X-Pre-Triage-Capability: <anonymousCapability>
```

The session UUID is not authority. Missing, wrong, or another session's capability returns `401`. Never put the capability in URLs, logs, analytics, telemetry, screenshots, or error reports.

Authenticated sessions use the existing Bearer token. The current primary patient or a currently authorized managed-patient relationship may access the session. Another account, a revoked manager, an anonymous caller attempting to read an authenticated session, or an authenticated caller attempting to read an unclaimed anonymous session receives a concealed `404` where applicable. Demo Guest has no special rules; it is a normal authenticated Beeexy patient.

If an invalid `Authorization` header is supplied, the backend returns `401` and never downgrades the request to anonymous mode.

## Problem Details and frontend handling

All errors use the repository Problem Details shape and include `correlationId`. Important cases:

| HTTP | `errorCode` when present | Meaning/action |
|---:|---|---|
| `400` | — | Malformed JSON or request binding failure. |
| `401` | — | Invalid Bearer or missing/wrong anonymous capability. Reauthenticate or recover the correct capability; do not probe another session. |
| `404` | — | Invalid/absent session route, concealed authorization, expiry, or unavailable ownership. End/recover the local flow. |
| `409` | `pre_triage.idempotency_key_reused` | Intake key was reused with different text. Generate a key only for a genuinely new intake. |
| `409` | `pre_triage.anonymous_replay_capability_required` | A committed anonymous retry needs the originally issued capability. Recover it or start a genuinely new intake with a new key. |
| `409` | — | Completed/expired session cannot accept answers, or an existing answer conflicts. Refresh projection/result and do not overwrite. |
| `422` | `pre_triage.idempotency_key_invalid` | Missing, repeated, empty, too long, or non-URL-safe intake key. |
| `422` | `pre_triage.intake_interpretation_invalid` | Invalid first-message body. |
| `422` | `pre_triage.duration_invalid` | Duration is not positive or uses an unsupported unit. |
| `422` | `pre_triage.intensity_invalid` | Intensity is outside the pinned 1–10 scale. |
| `422` | `pre_triage.additional_symptoms_invalid` | Selection is duplicated or outside the pinned options. |
| `422` | `pre_triage.questionnaire_version_mismatch` | Refresh projection and submit using its pinned version. |
| `503` | `pre_triage.interpretation_unavailable` | AI interpretation is temporarily unavailable. Preserve the text/key for a retry or offer deterministic quick replies. |
| `500` | — | Safe unexpected failure. Retain the correlation ID and show retry/recovery UI. |

Do not display stack traces or treat provider payloads as frontend-authoritative data.

## Frontend implementation rules

- Use only `conversation.nextInteraction` to choose the next control.
- Use `options[].value` for requests and `options[].label` for display.
- Use backend prompts; do not generate or rewrite clinical questions with AI.
- Use `conversation.acceptedValues` as authoritative session state.
- Use backend progress; do not calculate it.
- A missing `nextInteraction` is valid only for `READY_FOR_REVIEW` or `COMPLETED`.
- Preserve `sessionId`, pinned questionnaire version, and anonymous capability where applicable.
- Refresh with `GET .../conversation` after reload, uncertain mutation delivery, or recovery.
- Do not create frontend rules such as `if (!duration) askDuration()`.
- Do not auto-complete at 100%.
- Do not introduce a frontend pathway or option absent from the projection.
