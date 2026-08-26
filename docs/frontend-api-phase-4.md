# Beeexy Frontend API Integration — Phase 4

## 1. Purpose

This document is the frontend integration contract for **Phase 4 — Anonymous and Authenticated Pre-Triage**. It describes the seven implemented `/api/v1/pre-triage` operations, their security modes, the deterministic intake flow, neutral completion/result behavior, and optional anonymous claim after Beeexy authentication.

The current endpoint mappings, DTOs, application behavior, OpenAPI document, and integration tests are authoritative. This guide does not define backend architecture, Phase 1–3 APIs, Phase 5 Clinical History APIs, frontend styling, or future clinical behavior.

The Phase 4 journey is:

```text
Interpret and start from the first natural-language message, or select a pathway and start
→ submit structured or natural-language answers
→ follow backend progression
→ complete
→ use/retrieve the neutral result
→ optionally authenticate and claim an anonymous result
```

## 2. Current Phase 4 Scope

Phase 4 is a controlled, non-clinically-authoritative symptom-intake demo. It records one selected primary symptom plus duration, intensity, and controlled additional symptoms. Completion returns an immutable neutral symptom summary.

The public Phase 4 endpoint inventory is exactly:

| Method | Route | Anonymous | Authenticated primary | Authorized managed patient |
|---|---|---:|---:|---:|
| `POST` | `/api/v1/pre-triage/intake` | Yes | Yes, Bearer | No patient selector |
| `POST` | `/api/v1/pre-triage/intake/interpret` | Yes | Yes, Bearer | Stateless; no patient selector |
| `POST` | `/api/v1/pre-triage/sessions` | Yes | Yes | Yes |
| `POST` | `/api/v1/pre-triage/sessions/{id}/answers` | Yes, capability | Yes, Bearer | Yes, Bearer |
| `POST` | `/api/v1/pre-triage/sessions/{id}/complete` | Yes, capability | Yes, Bearer | Yes, Bearer |
| `GET` | `/api/v1/pre-triage/sessions/{id}/result` | Yes, capability | Yes, Bearer | Yes, Bearer |
| `POST` | `/api/v1/pre-triage/sessions/{id}/claim` | Bearer + capability | Claims only into the caller's primary patient | No managed-patient selector |

### Local-development setup

When the API runs with `ASPNETCORE_ENVIRONMENT=Development`, startup applies the EF Core migrations and idempotently imports and activates the five non-clinical demo packages: `HEADACHE`, `ABDOMINAL_PAIN`, `CHEST_PAIN`, `FEVER`, and `OTHER_SYMPTOMS`. Therefore a fresh local database needs only the normal API launch command:

```bash
dotnet run --project src/Beeexy.Api
```

This startup bootstrap is deliberately unavailable outside Development. Deployments must apply schema migrations through their normal migration process and must not obtain demo clinical-definition content implicitly.

`{id}` is the technical session UUID. A session UUID, patient UUID, Account ID, or Beeexy ID never grants authority by itself.

## 3. Base API and Authentication Assumptions

All routes below include the exact `/api/v1` prefix. JSON request bodies use:

```http
Content-Type: application/json
```

Authenticated Phase 4 calls reuse the existing centralized Beeexy authentication/session client:

```http
Authorization: Bearer <accessToken>
```

See [`frontend-api-integration.md`](frontend-api-integration.md) for access-token refresh, logout, and session bootstrap. See [`frontend-api-phase-3.md`](frontend-api-phase-3.md) for the primary/managed `activePatient` model. Do not create Phase 4-specific Bearer or refresh-token storage.

Intake, interpretation, start, answer, complete, and result are dual-mode operations:

- no `Authorization` header means anonymous mode;
- a valid Bearer token means authenticated mode;
- if an `Authorization` header is supplied but is invalid, the backend returns `401` and never downgrades the request to anonymous mode;
- an anonymous capability does not rescue an invalid Bearer request.

Claim always requires valid Bearer authentication as well as the original anonymous capability.

## 4. Supported Pathways and Intake Values

Supported primary pathways are exactly:

| API code | Frontend display |
|---|---|
| `HEADACHE` | Headache |
| `ABDOMINAL_PAIN` | Stomach pain |
| `CHEST_PAIN` | Chest pain |
| `FEVER` | Fever |
| `OTHER_SYMPTOMS` | Other symptoms |

Send pathway codes exactly as shown. The matching is ordinal/case-sensitive after surrounding whitespace is trimmed.

Recognized but unsupported codes include `RESPIRATORY_SYMPTOMS` and `BACK_PAIN`. They return `422` with `pre_triage.pathway_unsupported`. An unknown value returns `422` with `pre_triage.pathway_unknown`; it is never remapped to abdominal pain or another supported pathway.

The required intake fields after pathway selection are exactly:

1. `DURATION`
2. `INTENSITY`
3. `ADDITIONAL_SYMPTOMS`

Additional symptom codes are exactly:

```text
NAUSEA
DIARRHEA
FEVER
```

For a primary `FEVER` session, the allowed additional symptoms are exactly `NAUSEA` and `DIARRHEA`. The frontend must hide `FEVER` in that case, and the backend rejects it if submitted. For the other four primary pathways, all three additional symptoms are available.

An empty additional-symptom selection is valid, but it must be submitted as `additionalSymptoms: []` so the field counts as answered.

The current `HEADACHE`, `ABDOMINAL_PAIN`, and `FEVER` simplified packages use `2026.08.22-demo.1`; `CHEST_PAIN` and `OTHER_SYMPTOMS` use `2026.08.26-demo.1`. Treat the version returned by session start as authoritative instead of hard-coding it.

## 5. Anonymous Capability

Anonymous session creation returns a raw, session-specific capability in:

```text
anonymousCapability
```

The backend returns it once and stores only a one-way hash. It cannot be recovered later.

> The raw anonymous capability must be stored client-side for the current anonymous flow. It is returned once and cannot be recovered from the backend.

Send it on later anonymous requests in this exact header:

```http
X-Pre-Triage-Capability: <anonymousCapability>
```

The capability is required for anonymous answer submission, completion, and result retrieval. Claim requires both this header and Bearer authentication.

Frontend handling rules:

- keep the capability only as long as the anonymous Pre-Triage/result/claim flow needs it;
- never place it in a URL, query string, route, or fragment;
- never log it or include it in analytics, telemetry, error reports, or support screenshots;
- never send it to unrelated endpoints;
- do not reuse it for another session;
- clear it after a successful claim, explicit abandonment, or expiry;
- keep it after completion if result retrieval or optional claim is still needed.

The original anonymous access window is not extended by completion or claim. After claim, capability-based result access works only until the original anonymous expiry. The authenticated owner, and any currently authorized patient manager, can retrieve the claimed result through Bearer authorization after that boundary.

## 6. Start a Session

### `POST /api/v1/pre-triage/sessions`

Purpose: create a new temporary Pre-Triage session pinned to the active simplified package for the selected pathway.

Path parameters: none.

Capability header: none.

Authentication and patient targeting:

| Flow | Bearer | `patientId` request field | Result |
|---|---|---|---|
| Anonymous | Omit | Omit | Anonymous session and one-time capability |
| Authenticated primary | Send | Omit, preferably | Backend derives the caller's primary PatientProfile |
| Authenticated primary, explicit | Send | Primary profile UUID | Same primary-patient behavior |
| Authenticated managed patient | Send | Selected managed profile UUID | Allowed only while the caller has active management access |

An anonymous request that supplies `patientId` returns `401`. An authenticated request targeting an absent, unrelated, reverse-direction, or revoked patient returns a concealed `404`.

Request:

```json
{
  "pathway": "ABDOMINAL_PAIN"
}
```

Managed-patient request:

```json
{
  "pathway": "FEVER",
  "patientId": "20000000-0000-0000-0000-000000000002"
}
```

Anonymous success is `201 Created`:

```json
{
  "sessionId": "40000000-0000-0000-0000-000000000004",
  "pathway": "ABDOMINAL_PAIN",
  "status": "Active",
  "expiresAt": "2026-08-23T12:00:00Z",
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
}
```

For authenticated creation, the same response includes `patientId` and omits `anonymousCapability`:

```json
{
  "sessionId": "40000000-0000-0000-0000-000000000004",
  "patientId": "20000000-0000-0000-0000-000000000002",
  "pathway": "FEVER",
  "status": "Active",
  "expiresAt": "2026-08-23T12:00:00Z",
  "questionnaire": {
    "code": "fever-demo-questionnaire",
    "version": "2026.08.22-demo.1"
  },
  "ruleSet": {
    "code": "fever-demo-neutral-rules",
    "version": "2026.08.22-demo.1"
  },
  "clinicalContent": {
    "source": "PRODUCT_DEMO_DEFINED",
    "reviewStatus": "NOT_APPLICABLE",
    "clinicalApproval": "NOT_CLINICALLY_APPROVED"
  }
}
```

Store `sessionId`, `pathway`, `expiresAt`, the returned questionnaire version, and—only for anonymous sessions—the capability.

Important statuses:

| Status | Meaning and frontend action |
|---|---|
| `201` | New session. Replace any abandoned local draft only after deciding that a new session is intended. |
| `400` | Malformed JSON/HTTP request. Fix client serialization. |
| `401` | Invalid supplied Bearer, disabled account, or anonymous attempt to target a patient. Apply the existing auth policy or restart anonymously without an `Authorization` header. |
| `404` | Authenticated patient target is unavailable or not authorized. Refresh accessible patients and choose a valid `activePatient`. |
| `422` | Missing, unknown, unsupported pathway, unusable package, or unknown request field. Use `errorCode` where present. |
| `500` | Safe unexpected failure. Show a retry state and retain the correlation ID. |

Idempotency: **not idempotent**. Every successful call creates a distinct session; every anonymous success returns a distinct capability.

Session start does not return questionnaire progression. A frontend may submit all three structured fields together. For a step-by-step flow, the first fixed Phase 4 field is duration; after the first answer response, use only the backend's returned progression metadata to choose subsequent fields.

### Start from a natural-language intake message

#### `POST /api/v1/pre-triage/intake`

Purpose: interpret the visitor's first message and, only when the pathway is unambiguous, atomically create a normal session and apply any initial answer values that are valid for the newly pinned package. This is the preferred single-call entry point for a conversational frontend. It does not complete the session.

The request body contains exactly one field:

```json
{
  "text": "My stomach has hurt for two days and it is 6 out of 10"
}
```

`text` is required, is trimmed by the backend, must not be blank, and has a maximum length of 4,000 characters. `pathway`, `patientId`, and every other extra field are rejected with `422`; the frontend cannot override the interpreted pathway or select a managed patient through this operation.

Authentication and ownership:

- omit `Authorization` to create an anonymous session when resolution succeeds;
- send a valid Bearer token to create a session for the caller's server-derived primary patient;
- an invalid supplied Bearer returns `401` and is never downgraded to anonymous;
- the request does not accept `X-Pre-Triage-Capability`; a successful anonymous response creates and returns a new capability inside `session`.

A resolved request returns `201 Created`. The response combines the canonical session-start contract with the canonical answer/progression contract:

```json
{
  "resolution": "RESOLVED",
  "session": {
    "sessionId": "40000000-0000-0000-0000-000000000004",
    "pathway": "ABDOMINAL_PAIN",
    "status": "Active",
    "expiresAt": "2026-08-27T12:00:00Z",
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
      "duration": {
        "value": 2,
        "unit": "DAYS"
      },
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
        "allowedUnits": [],
        "minimum": null,
        "maximum": null
      },
      "readyToComplete": false
    }
  }
}
```

An authenticated `201` uses the same shape, except `session.patientId` is present and `session.anonymousCapability` is omitted.

The backend interprets the message once. Exact aliases such as `"Chest pain"` resolve deterministically without an AI-provider call. For other safe input, at most one provider call is made. After starting the session, every candidate value is revalidated against the exact questionnaire package pinned by `session.questionnaire`. Only values that still pass are persisted through the normal answer mutation path and returned in `initialAnswers.acceptedAnswers` and `initialAnswers.acceptedValues`.

Candidate rejection is field-by-field. For example, a valid duration and invalid intensity create the session, persist only duration, and return progression asking for intensity. If no candidate value survives revalidation, `acceptedAnswers` is empty, `acceptedValues` is `{}`, and progression begins at `DURATION`. The frontend must use only `initialAnswers`, never the original text or a local extraction, as the accepted state.

An ambiguous interpretation returns `200 OK`, creates no session or answers, and may provide supported candidate pathways:

```json
{
  "resolution": "AMBIGUOUS",
  "candidatePathways": ["HEADACHE", "CHEST_PAIN"]
}
```

An unresolved interpretation also returns `200 OK` and creates no state:

```json
{
  "resolution": "UNRESOLVED"
}
```

`candidatePathways`, `session`, and `initialAnswers` are omitted when they do not apply. A provider timeout, provider failure, or invalid provider structure returns safe Problem Details instead of an interpretation response and creates no state.

Important statuses:

| Status | Meaning and frontend action |
|---|---|
| `201` | `RESOLVED`; store the session and, for anonymous flow, its one-time capability. Continue from `initialAnswers.progression`. |
| `200` | `AMBIGUOUS` or `UNRESOLVED`; no session exists. Clarify or offer explicit pathway selection. |
| `400` | Malformed JSON/HTTP request. Fix client serialization. |
| `401` | Supplied Bearer is invalid or the authenticated account is unavailable. Apply the existing authentication recovery flow. |
| `404` | The authenticated caller's primary patient is unavailable. Do not fall back to anonymous implicitly. |
| `409` | Safe orchestration/session state conflict. Do not assume a session was created. |
| `422` | Blank/overlong text or an unknown request field. Correct the request. |
| `503` | Interpretation provider is unavailable, timed out, misconfigured, or returned invalid structure. Offer explicit structured/pathway intake. |
| `500` | Safe unexpected failure. Session creation and initial answer persistence are rolled back together. |

This start operation is not idempotent: the backend has no request idempotency key or durable start-deduplication convention. A repeated successful request can create another session. Do not automatically retry after an uncertain network outcome, and suppress duplicate submits in the UI. Never use raw intake text as a deduplication key.

Even when `initialAnswers.progression.readyToComplete` is `true`, the session remains `Active`. The frontend must still show Review and call the existing explicit `/complete` operation. Clinical History projection and FHIR export do not occur during intake; they remain downstream of explicit completion and the existing authenticated workflows.

## 7. Submit Answers

### `POST /api/v1/pre-triage/sessions/{id}/answers`

Purpose: submit one or more structured values, or one natural-language message, and receive backend-authoritative progression.

Path parameter: `{id}` is the session UUID.

Authentication:

- anonymous session: omit Bearer and send `X-Pre-Triage-Capability`;
- primary/managed session: send Bearer and omit the capability;
- authorization is checked on every request, so a revoked manager receives concealed `404`.

The request has two mutually exclusive modes. Sending both, neither, or unknown fields returns `422`.

### Structured mode

Structured fields are optional individually, but each request must contain at least one. A single request may submit all remaining fields atomically:

```json
{
  "questionnaireVersion": "2026.08.22-demo.1",
  "structured": {
    "duration": {
      "value": 1,
      "unit": "DAYS"
    },
    "intensity": 6,
    "additionalSymptoms": ["NAUSEA"]
  }
}
```

`questionnaireVersion` is optional. If supplied, it must exactly match the session-pinned version. Using the version returned by session start helps detect stale local state.

Duration rules:

- `value` is a positive number;
- `unit` is exactly one of `MINUTES`, `HOURS`, `DAYS`, `WEEKS`, or `MONTHS`.

Intensity is an integer from `1` through `10`. A fractional number is malformed for the integer DTO and returns `400`; an integer outside the range returns `422`.

Additional symptoms must use the exact controlled codes. Unknown values, a fourth option, duplicates, or `FEVER` inside a primary `FEVER` session return `422`. Submit `[]` to explicitly answer “none.”

Examples of partial structured submissions:

```json
{
  "structured": {
    "duration": {
      "value": 2,
      "unit": "HOURS"
    }
  }
}
```

```json
{
  "structured": {
    "additionalSymptoms": []
  }
}
```

### Natural-language mode

```json
{
  "naturalLanguage": "It started yesterday, is 6 out of 10, and I feel nauseous."
}
```

The message must be nonblank and no longer than 4,000 characters. The backend may extract several fields from one message, but validates every candidate against the selected pathway's pinned package before writing it. A proposed pathway cannot change the session's selected pathway.

Natural-language safety, clarification, unsupported, and provider-unavailable results still use `200 OK`; inspect `outcome` and `clarification`. They do not fabricate answers. Structured mode remains available regardless of AI-provider status.

### Answer response

Success and safe natural-language outcomes return `200 OK`. For example, after accepting duration, the next question is intensity:

```json
{
  "sessionId": "40000000-0000-0000-0000-000000000004",
  "pathway": "HEADACHE",
  "questionnaireVersion": "2026.08.22-demo.1",
  "outcome": "ACCEPTED",
  "acceptedAnswers": ["DURATION"],
  "progression": {
    "state": "IN_PROGRESS",
    "answeredRequiredFields": ["DURATION"],
    "missingRequiredFields": ["INTENSITY", "ADDITIONAL_SYMPTOMS"],
    "nextQuestion": {
      "code": "INTENSITY",
      "prompt": "How intense is it from 1 to 10?",
      "answerType": "INTEGER_SCALE",
      "allowedValues": [],
      "allowedUnits": [],
      "minimum": 1,
      "maximum": 10
    },
    "readyToComplete": false
  }
}
```

When all required fields are valid:

```json
{
  "sessionId": "40000000-0000-0000-0000-000000000004",
  "pathway": "HEADACHE",
  "questionnaireVersion": "2026.08.22-demo.1",
  "outcome": "ACCEPTED",
  "acceptedAnswers": ["ADDITIONAL_SYMPTOMS"],
  "progression": {
    "state": "READY_TO_COMPLETE",
    "answeredRequiredFields": ["DURATION", "INTENSITY", "ADDITIONAL_SYMPTOMS"],
    "missingRequiredFields": [],
    "readyToComplete": true
  }
}
```

`nextQuestion` is omitted when there is no next question. `clarification` is omitted for a normal accepted response.

Natural-language outcome values are:

| `outcome` | Meaning | Frontend action |
|---|---|---|
| `ACCEPTED` | One or more validated facts were accepted. | Update local progression from the response. |
| `CLARIFICATION_REQUIRED` | The input or validated provider output is ambiguous/incomplete. | Show a neutral clarification and continue with backend progression or structured fields. |
| `SAFETY_RESTRICTED` | The input requested something outside safe intake behavior. | Do not render clinical advice; guide the user back to supported structured intake. |
| `UNSUPPORTED` | The interpreted request/value is unsupported. | Do not remap it; offer supported structured inputs. |
| `PROVIDER_UNAVAILABLE` | AI is unavailable, timed out, or not configured. | Fall back to structured intake. |

When present, `clarification.code` is one of:

- `CLARIFICATION_REQUIRED`
- `SAFETY_RESTRICTED`
- `UNSUPPORTED_INPUT`
- `INTERPRETATION_UNAVAILABLE`
- `INVALID_INTERPRETATION`

`clarification.classification`, when present, is a safety category such as `AMBIGUOUS`, `OUT_OF_SCOPE`, `PRESCRIPTION_REQUEST`, `PROHIBITED_MEDICAL_ADVICE`, `POTENTIAL_PROMPT_INJECTION`, `UNSUPPORTED_CLINICAL_REQUEST`, or `PRE_TRIAGE_INPUT`. Do not display these machine values as medical conclusions.

Important statuses:

| Status | Meaning and frontend action |
|---|---|
| `200` | Request processed. Always inspect `outcome`, accepted fields, and progression. |
| `400` | Malformed JSON/type, such as fractional intensity. Fix serialization/input typing. |
| `401` | Anonymous capability or supplied Bearer is missing/invalid. Apply the access-specific recovery flow. |
| `404` | Session absent or authenticated patient authorization is concealed. Clear stale state or refresh the selected patient. |
| `409` | Session is completed/expired, or an already-recorded field has a different value. Refetch the result when appropriate; do not overwrite locally. |
| `422` | Input mode, field, version, duration, intensity, or additional-symptom validation failed. Preserve valid local state and correct the field. |
| `500` | Safe unexpected failure. Do not assume any answer was accepted; retry only after reconciling progression/state. |

Idempotency: an exact repeat of an already stored answer is retry-safe and returns `200` without duplicating it. A different value for an answered field returns `409` and does not overwrite the original. Do not blindly retry a changed answer.

## 8. Questionnaire Progression and Rendering

Do not independently compute progression, skip order, valid options, or readiness in the frontend. After every answer response, replace local progression with `response.progression`.

The backend is authoritative over:

- `answeredRequiredFields`;
- `missingRequiredFields`;
- `nextQuestion`;
- `nextQuestion.allowedValues`;
- `nextQuestion.allowedUnits`;
- `nextQuestion.minimum` and `maximum`;
- `readyToComplete`.

Current question rendering can map directly from returned metadata:

| `answerType` | Current field | Suggested control behavior |
|---|---|---|
| `DURATION` | Duration | Positive numeric input plus `allowedUnits` selector |
| `INTEGER_SCALE` | Intensity | Integer selector/slider/buttons using `minimum` and `maximum` |
| `MULTIPLE_CHOICE` | Additional symptoms | Chips/checkboxes from `allowedValues`; empty selection is allowed |

Natural language is an optional separate free-text submission mode, not a package-authored `nextQuestion` type.

The current deterministic order is duration, intensity, then additional symptoms, while already answered fields are skipped. This description helps initial rendering only; the returned progression remains authoritative. Completion should be enabled only when `state === "READY_TO_COMPLETE"` and `readyToComplete === true`.

## 9. Complete a Session

### `POST /api/v1/pre-triage/sessions/{id}/complete`

Purpose: atomically promote a complete temporary intake into one immutable episode and return its canonical neutral summary.

Path parameter: `{id}` is the session UUID.

Request body: none. Do not send JSON.

Authentication:

- anonymous session: capability header, no Bearer;
- primary/managed session: Bearer, no capability;
- current managed-patient authorization is rechecked.

Success behavior:

- first successful completion: `201 Created`;
- authorized repeat: `200 OK` with the same episode ID, completion time, and canonical result;
- both statuses return the complete result schema documented in the next section.

The frontend can render the returned completion body immediately. A separate `GET /result` is not required immediately after `201`; use GET for reload/recovery or later retrieval.

Important statuses:

| Status | Meaning and frontend action |
|---|---|
| `201` | First completion. Persist/render the returned neutral result. |
| `200` | Idempotent repeat. Treat the returned result as canonical. |
| `401` | Capability/Bearer invalid. Apply the relevant access recovery flow. |
| `404` | Session absent, inaccessible, revoked, or anonymous completion attempted at/after expiry. Clear stale state or show neutral unavailability. |
| `409` | Session state changed/conflicted. Retrieve the result if completion may already have won. |
| `422` | Intake is incomplete/invalid (`pre_triage.completion_incomplete`) or its pinned definition is inconsistent. Return to backend-driven intake for user-correctable incompleteness. |
| `500` | Atomic completion failed safely. Do not create a local episode; retry/reconcile with GET. |

Idempotency: **yes after successful completion**. The backend owns duplicate prevention; the frontend must not invent its own episode deduplication.

## 10. Retrieve the Neutral Result

### `GET /api/v1/pre-triage/sessions/{id}/result`

Purpose: retrieve the canonical immutable neutral symptom summary for a completed session.

Path parameter: `{id}` is the session UUID.

Request body: none.

Authentication:

- unclaimed anonymous, or capability-based access after claim but before original expiry: capability header and no Bearer;
- authenticated primary/managed patient session: Bearer;
- claimed anonymous episode: Bearer for its patient owner or a currently authorized manager.

Success is `200 OK`:

```json
{
  "sessionId": "40000000-0000-0000-0000-000000000004",
  "episodeId": "50000000-0000-0000-0000-000000000005",
  "primarySymptom": {
    "code": "HEADACHE",
    "display": "Headache"
  },
  "duration": {
    "value": 2,
    "unit": "DAYS"
  },
  "intensity": 5,
  "additionalSymptoms": [],
  "completedAt": "2026-08-22T12:05:00Z",
  "questionnaire": {
    "code": "headache-demo-questionnaire",
    "version": "2026.08.22-demo.1"
  },
  "package": {
    "code": "headache-demo-neutral-rules",
    "version": "2026.08.22-demo.1"
  },
  "clinicalContent": {
    "source": "PRODUCT_DEMO_DEFINED",
    "reviewStatus": "NOT_APPLICABLE",
    "clinicalApproval": "NOT_CLINICALLY_APPROVED"
  }
}
```

This is a neutral symptom summary. Render the structured values and truthful provenance only. The response does not contain, and the frontend must not infer or display:

- urgency or triage priority;
- disposition or where/when to seek care;
- diagnosis or condition;
- recommendation or emergency advice;
- prescription or medication advice;
- treatment;
- disease probability;
- red flags.

Important statuses:

| Status | Meaning and frontend action |
|---|---|
| `200` | Render the returned immutable neutral summary. |
| `401` | Capability/Bearer invalid. Apply the relevant access recovery flow. |
| `404` | Session absent, concealed by authorization, cleaned up, or no longer available through the anonymous capability. Show neutral unavailability and clear stale state. |
| `409` | Session has not completed. Resume intake from known progression or safely restart if state cannot be recovered. |
| `500` | Safe unexpected/invariant failure. Show a generic retry state with correlation ID. |

Idempotency: **yes/read-only**. Repeated authorized GETs return the same canonical episode content.

## 11. Claim an Anonymous Result

### `POST /api/v1/pre-triage/sessions/{id}/claim`

Purpose: attach an existing completed anonymous episode to the authenticated Account's server-derived primary PatientProfile.

Path parameter: `{id}` is the original anonymous session UUID.

Required headers:

```http
Authorization: Bearer <accessToken>
X-Pre-Triage-Capability: <originalAnonymousCapability>
```

Request body: none.

Query parameters: none.

The endpoint rejects a body or any query selector with `400`. There is no `patientId`, `profileId`, Account ID, or Beeexy ID selector. Managed-patient claim is not supported.

Success is `200 OK`:

```json
{
  "sessionId": "40000000-0000-0000-0000-000000000004",
  "episodeId": "50000000-0000-0000-0000-000000000005",
  "patientId": "10000000-0000-0000-0000-000000000001",
  "claimedAt": "2026-08-22T13:00:00Z"
}
```

Claim does not recompute or change the Pre-Triage result. After success, authenticated GET returns the same canonical result that anonymous GET returned.

Important statuses:

| Status | Meaning and frontend action |
|---|---|
| `200` | First claim or same-primary-patient idempotent repeat. Clear anonymous claim state and use authenticated result access. |
| `400` | A body/query selector or malformed request was supplied. Fix the client; claim accepts headers only. |
| `401` | Bearer or original capability missing/invalid. Do not attempt a claim with UUID alone. |
| `404` | Session absent, not an anonymous session, first claim at/after expiry, or otherwise unavailable. Clear stale anonymous state and show neutral unavailability. |
| `409` | Session is not completed, or another patient already owns the episode. Do not disclose or infer the existing owner. |
| `500` | Safe unexpected failure. Ownership is transactional; do not assume claim succeeded. Reconcile with authenticated result retrieval before retrying. |

Idempotency: **yes for the same primary patient**. A repeat returns the original `claimedAt`. A different patient receives privacy-safe `409`. A first claim requires `now < expiresAt`; after a successful claim, same-patient repeats remain idempotent even after the original expiry, but still require both credentials.

## 12. Authenticated Frontend Flow

Phase 4 uses the existing Phase 3 `activePatient` selection. Do not infer access locally; the backend reauthorizes every request.

For a primary `activePatient`, either use `/intake` with natural language or omit `patientId` from explicit start. For a managed `activePatient`, use explicit `/sessions` start and send its technical `profileId` as `patientId`; `/intake` intentionally has no managed-patient selector. Supplying the primary profile ID explicitly to `/sessions` is accepted but unnecessary.

Recommended flow:

1. Restore the centralized Beeexy session and current Phase 3 `activePatient`.
2. For the primary patient, either send the first natural-language message to `POST /api/v1/pre-triage/intake` or let the user select one of the five pathways. For a managed patient, use explicit selection.
3. Call `/intake` with Bearer for the primary conversational path, or call `POST /api/v1/pre-triage/sessions` with Bearer, pathway, and `patientId` only for a managed selection.
4. On intake `201` or session-start `201`, store the returned `sessionId`, pathway, expiry, and questionnaire version. On intake `200`, clarify/select a pathway without creating local session state.
5. Continue from `initialAnswers.progression` after intake; after explicit start, submit all structured fields together or begin with duration.
6. After every answer response, replace local progression with the backend response.
7. When the backend returns `READY_TO_COMPLETE` and `readyToComplete: true`, call `/complete` with no body.
8. Render the neutral result returned by completion.
9. Use `GET /result` for reload/recovery or later display.

If managed access is revoked during the flow, later answer/complete/result calls return concealed `404`. Refresh accessible patients, invalidate that managed patient's draft from the manager's view, and choose an authorized patient.

## 13. Anonymous Frontend Flow

Recommended flow:

1. Start Pre-Triage without an `Authorization` header.
2. Either send the first natural-language message to `POST /api/v1/pre-triage/intake` or let the user select one of the five supported pathways and call `POST /api/v1/pre-triage/sessions`.
3. On intake `200`, clarify/select without storing session state. On either `201`, store `sessionId`, `pathway`, `expiresAt`, questionnaire version, and `anonymousCapability`.
4. Continue from `initialAnswers.progression` after intake, or submit the first answers after explicit start.
5. Submit later answers with `X-Pre-Triage-Capability` and no Bearer.
6. Drive the remaining UI from each backend progression response.
7. Complete with the capability and no request body.
8. Render the completion response or retrieve it with capability-based GET.
9. Offer an explicit sign-in/create-account action if the user wants to save the result.
10. Complete the existing Beeexy authentication flow without resetting its centralized session.
11. Call `/claim` with both Bearer and the original capability.
12. On `200`, clear the anonymous capability/draft state and use authenticated result access.

Do not claim automatically if the user never authenticates or has not chosen to save the result. Do not create a second Pre-Triage episode after login.

## 14. Local State and API Client Integration

A minimal conceptual state shape is:

```ts
type ActivePreTriage = {
  sessionId: string
  pathway: PreTriagePathway
  patientId?: string
  questionnaireVersion: string
  expiresAt: string
  anonymousCapability?: string
  progression?: QuestionnaireProgress
}
```

Recommended lifecycle:

- keep the active object in the existing app/session state;
- keep the capability in memory by default;
- if reload survival is a product requirement, only use approved session-scoped storage and treat the capability as a credential; do not place it in long-lived, synced, analytics-visible, or URL-derived storage;
- `sessionId`, pathway, patient ID, version, and expiry may survive route navigation/reload without granting access by themselves;
- keep anonymous state after completion until result display/optional claim is finished;
- clear the anonymous state after successful claim, explicit abandonment, or server expiry;
- clear stale state when a concealed `404` indicates the temporary flow is no longer available;
- never store Bearer or refresh tokens in this Phase 4 object.

Reuse the centralized Beeexy API client for:

- Bearer injection;
- coordinated refresh/retry behavior;
- Problem Details parsing;
- correlation IDs;
- authenticated session bootstrap.

The client must also support anonymous requests with a capability and no Bearer, plus claim requests carrying both. Avoid ad-hoc `fetch` calls that bypass centralized security/error behavior.

## 15. Problem Details and Error Handling

Expected application failures use `application/problem+json`. The common shape is:

```json
{
  "status": 422,
  "title": "Request validation failed.",
  "detail": "The structured answer is invalid for the pinned questionnaire.",
  "instance": "/api/v1/pre-triage/sessions/40000000-0000-0000-0000-000000000004/answers",
  "errorCode": "pre_triage.intensity_invalid",
  "correlationId": "<request-correlation-id>"
}
```

`detail`, `errorCode`, `type`, and `instance` are conditional. `correlationId` is also returned in `X-Correlation-ID`. Preserve it for support without attaching credentials, capability, intake text, or result content.

Branch first on HTTP status, then on `errorCode` when present. Do not parse human-readable `detail`. Authentication, concealed-not-found, conflict, malformed-request, and unexpected failures do not necessarily have `errorCode`.

Stable frontend-useful Phase 4 validation codes include:

| Code | Meaning |
|---|---|
| `pre_triage.pathway_required` | Start pathway missing/blank |
| `pre_triage.pathway_unknown` | Unknown pathway code |
| `pre_triage.pathway_unsupported` | Recognized but unsupported pathway |
| `pre_triage.definition_unavailable` | No usable active simplified package |
| `pre_triage.unsupported_field` | Unknown session-start field |
| `pre_triage.intake_interpretation_invalid` | Intake text is blank/too long or contains an unknown field |
| `pre_triage.interpretation_unavailable` | Provider unavailable/timeout/configuration/invalid structured response |
| `pre_triage.answer_input_invalid` | Both/neither answer modes or unknown structured field |
| `pre_triage.answer_required` | Structured object contains no answer |
| `pre_triage.natural_language_invalid` | Natural-language input invalid/too long |
| `pre_triage.questionnaire_version_mismatch` | Submitted version differs from pinned version |
| `pre_triage.duration_invalid` | Duration value/unit invalid |
| `pre_triage.intensity_invalid` | Intensity outside integer 1–10 |
| `pre_triage.additional_symptoms_invalid` | Additional selection invalid for package/pathway |
| `pre_triage.completion_incomplete` | Required intake is incomplete or invalid |

General Phase 4 status handling:

| Status | Frontend policy |
|---|---|
| `400` | Malformed JSON/type/HTTP request, or claim body/query supplied. Fix request construction. |
| `401` | Bearer or capability not accepted. For authenticated calls, apply the existing one-time coordinated refresh policy; if unrecoverable, clear auth. For anonymous calls, clear/restart the inaccessible draft. |
| `404` | Absent, expired/cleaned, revoked, or authorization-concealed resource. Show “This Pre-Triage is no longer available,” without implying another patient's resource exists. |
| `409` | Operation-specific state conflict: changed existing answer, completed/expired answer flow, result before completion, incomplete claim, or competing claim owner. Reconcile state; do not blindly retry. |
| `422` | Correctable request/package validation. Keep the user in the flow and render safe field feedback when `errorCode` allows. |
| `503` | Pre-session interpretation unavailable. Offer deterministic pathway/structured intake and do not assume a session exists. |
| `500` | Safe generic failure. Do not display internal details or assume a mutation committed; use correlation ID and reconcile before retry. |

None of the seven Phase 4 endpoints currently declares `429`; do not build Phase 4-specific rate-limit behavior beyond the centralized client's general policy.

## 16. Retry and Idempotency Rules

| Operation | Retry semantics |
|---|---|
| Start from intake | Not idempotent after a `201`. A retry that reached the server can create another session/capability. `200` non-resolved and failure responses create no state. |
| Start | Not idempotent. A retry that reached the server can create another session/capability. |
| Submit exact same answer | Idempotent: `200`, no duplicate answer. |
| Submit different value for answered field | Not an overwrite: `409`, original remains. |
| Complete after first success | Idempotent: `200`, same canonical result. |
| Get result | Read-only/idempotent. |
| Claim by same primary patient | Idempotent: `200`, original `claimedAt`. |
| Claim by another patient | `409`, no transfer. |

Do not implement frontend-generated duplicate episodes or ownership-transfer logic. For ambiguous network failures, reconcile through result retrieval when access allows before issuing another mutation.

## 17. Expiry and Cleanup

Every session start returns a server-generated `expiresAt`, currently 24 hours after creation. The server clock and persisted expiry are authoritative; `now < expiresAt` is inside the active window and `now >= expiresAt` is expired.

Frontend expectations:

- active anonymous and abandoned authenticated drafts may be removed after expiry;
- completed unclaimed anonymous data may be removed at expiry;
- answer submission on an expired active session currently returns `409`;
- anonymous completion/result and first claim at/after expiry return concealed `404`;
- cleanup may cause an expired/abandoned resource to return concealed `404`;
- a successfully claimed episode remains permanent for its patient;
- a completed authenticated primary/managed episode remains permanent;
- claim does not extend original capability-based access;
- authenticated access to a claimed result remains available after the original anonymous expiry.

On an expired/cleaned `404`, clear the stale local Pre-Triage state and return the user to a safe restart screen. Do not expose cleanup implementation details.

## 18. Clinical History Projection

Completed authenticated episodes and successfully claimed anonymous episodes are internally prepared for future Clinical History projection.

There is currently **no Phase 4 frontend API for listing or reading Clinical History**. Do not invent a history route, timeline, event DTO, amendment flow, polling behavior, or FHIR read model. Rendering the immediate neutral result uses only `/pre-triage/sessions/{id}/result`.

## 19. AI Integration Status

The backend supports a provider-neutral, AI-assisted natural-language intake boundary with deterministic application safety and package validation.

Current runtime status:

- natural-language request transport and safety/validation behavior are implemented;
- AI is optional and never controls the questionnaire, progression, completeness, urgency, diagnosis, or treatment;
- NVIDIA-backed interpretation is registered only when valid NVIDIA provider configuration is present; otherwise `UnavailableClinicalAiProvider` is registered and deliberately reports configuration-unavailable behavior;
- exact supported pathway aliases resolve without invoking the provider;
- without provider configuration, other safe symptom-like `/answers` requests return `outcome: "PROVIDER_UNAVAILABLE"`, while `/intake` and `/intake/interpret` return `503` Problem Details with `errorCode: "pre_triage.interpretation_unavailable"`;
- application safety can return `SAFETY_RESTRICTED` or `CLARIFICATION_REQUIRED` before any provider call;
- structured intake works without AI and is the dependable fallback.

The frontend may expose natural language, but it must gracefully transition to structured intake on clarification, restriction, unsupported input, invalid interpretation, or provider unavailability. Never trust or render raw model output; the public response contains only backend-validated categories and progression.

## 20. Current Phase 4 Limitations

The frontend must not invent UI, fields, or claims for:

- clinical urgency or priority;
- disposition or emergency recommendation;
- diagnosis or condition;
- prescription or medication selection;
- treatment advice;
- numeric disease probability/confidence;
- clinical red-flag execution/escalation;
- supported pathways beyond `HEADACHE`, `ABDOMINAL_PAIN`, `CHEST_PAIN`, `FEVER`, and `OTHER_SYMPTOMS`;
- a fourth additional symptom;
- detailed abdominal clinical rules;
- managed-patient anonymous claim;
- claim transfer, unclaim, or post-expiry recovery;
- Clinical History read/list endpoints;
- FHIR generation/export;
- AI-authored questions or AI clinical authority.

The `ruleSet`/`package` reference is provenance for a non-clinical demo package. It is not evidence that a clinical rule engine ran.

## 21. TypeScript Contracts

These practical types mirror the current JSON contracts. UUID and ISO timestamp aliases document intent but remain strings at transport level.

```ts
export type Uuid = string;
export type IsoTimestamp = string;

export type PreTriagePathway =
  | "HEADACHE"
  | "ABDOMINAL_PAIN"
  | "CHEST_PAIN"
  | "FEVER"
  | "OTHER_SYMPTOMS";

export type AdditionalSymptom =
  | "NAUSEA"
  | "DIARRHEA"
  | "FEVER";

export type DurationUnit =
  | "MINUTES"
  | "HOURS"
  | "DAYS"
  | "WEEKS"
  | "MONTHS";

export type RequiredAnswerCode =
  | "DURATION"
  | "INTENSITY"
  | "ADDITIONAL_SYMPTOMS";

export type NextQuestionAnswerType =
  | "DURATION"
  | "INTEGER_SCALE"
  | "MULTIPLE_CHOICE";

export interface ClinicalDefinitionReference {
  code: string;
  version: string;
}

export interface ClinicalContentStatus {
  source: "PRODUCT_DEMO_DEFINED";
  reviewStatus: "NOT_APPLICABLE";
  clinicalApproval: "NOT_CLINICALLY_APPROVED";
}

export interface StartPreTriageRequest {
  pathway: PreTriagePathway;
  /** Omit for anonymous and authenticated-primary flows. */
  patientId?: Uuid;
}

interface PreTriageSessionStartCommon {
  sessionId: Uuid;
  pathway: PreTriagePathway;
  status: "Active";
  expiresAt: IsoTimestamp;
  questionnaire: ClinicalDefinitionReference;
  ruleSet: ClinicalDefinitionReference;
  clinicalContent: ClinicalContentStatus;
}

export type PreTriageSessionStartResponse =
  | (PreTriageSessionStartCommon & {
      patientId?: never;
      anonymousCapability: string;
    })
  | (PreTriageSessionStartCommon & {
      patientId: Uuid;
      anonymousCapability?: never;
    });

export interface StartPreTriageFromIntakeRequest {
  text: string;
}

export type PreTriageIntakeResolution =
  | "RESOLVED"
  | "AMBIGUOUS"
  | "UNRESOLVED";

export interface DurationAnswer {
  value: number;
  unit: DurationUnit;
}

export interface StructuredPreTriageAnswers {
  duration?: DurationAnswer;
  intensity?: number;
  additionalSymptoms?: AdditionalSymptom[];
}

export type SubmitPreTriageAnswersRequest =
  | {
      questionnaireVersion?: string;
      structured: StructuredPreTriageAnswers;
      naturalLanguage?: never;
    }
  | {
      questionnaireVersion?: string;
      structured?: never;
      naturalLanguage: string;
    };

export interface NextQuestion {
  code: RequiredAnswerCode;
  prompt: string;
  answerType: NextQuestionAnswerType;
  allowedValues: string[];
  allowedUnits: DurationUnit[];
  minimum: number | null;
  maximum: number | null;
}

export type QuestionnaireProgressState =
  | "IN_PROGRESS"
  | "READY_TO_COMPLETE";

export interface QuestionnaireProgress {
  state: QuestionnaireProgressState;
  answeredRequiredFields: RequiredAnswerCode[];
  missingRequiredFields: RequiredAnswerCode[];
  /** Omitted in READY_TO_COMPLETE state. */
  nextQuestion?: NextQuestion;
  readyToComplete: boolean;
}

export type TriageIntakeOutcome =
  | "ACCEPTED"
  | "CLARIFICATION_REQUIRED"
  | "SAFETY_RESTRICTED"
  | "UNSUPPORTED"
  | "PROVIDER_UNAVAILABLE";

export type IntakeClarificationCode =
  | "CLARIFICATION_REQUIRED"
  | "SAFETY_RESTRICTED"
  | "UNSUPPORTED_INPUT"
  | "INTERPRETATION_UNAVAILABLE"
  | "INVALID_INTERPRETATION";

export type ClinicalIntentClassification =
  | "PRE_TRIAGE_INPUT"
  | "OUT_OF_SCOPE"
  | "PRESCRIPTION_REQUEST"
  | "PROHIBITED_MEDICAL_ADVICE"
  | "POTENTIAL_PROMPT_INJECTION"
  | "UNSUPPORTED_CLINICAL_REQUEST"
  | "AMBIGUOUS";

export interface IntakeClarification {
  code: IntakeClarificationCode;
  classification?: ClinicalIntentClassification;
}

export interface PreTriageAnswerResponse {
  sessionId: Uuid;
  pathway: PreTriagePathway;
  questionnaireVersion: string;
  outcome: TriageIntakeOutcome;
  acceptedAnswers: RequiredAnswerCode[];
  acceptedValues: PreTriageAcceptedValues;
  progression: QuestionnaireProgress;
  clarification?: IntakeClarification;
}

export interface PreTriageAcceptedValues {
  duration?: DurationAnswer;
  intensity?: number;
  additionalSymptoms?: AdditionalSymptom[];
}

export type StartPreTriageFromIntakeResponse =
  | {
      resolution: "RESOLVED";
      candidatePathways?: never;
      session: PreTriageSessionStartResponse;
      initialAnswers: PreTriageAnswerResponse;
    }
  | {
      resolution: "AMBIGUOUS" | "UNRESOLVED";
      candidatePathways?: PreTriagePathway[];
      session?: never;
      initialAnswers?: never;
    };

export interface NeutralPreTriageResult {
  sessionId: Uuid;
  episodeId: Uuid;
  primarySymptom: {
    code: PreTriagePathway;
    display: "Headache" | "Stomach pain" | "Chest pain" | "Fever" | "Other symptoms";
  };
  duration: DurationAnswer;
  intensity: number;
  additionalSymptoms: AdditionalSymptom[];
  completedAt: IsoTimestamp;
  questionnaire: ClinicalDefinitionReference;
  package: ClinicalDefinitionReference;
  clinicalContent: ClinicalContentStatus;
}

/** POST /complete returns this schema for both 201 and idempotent 200. */
export type CompletePreTriageResponse = NeutralPreTriageResult;

export interface ClaimAnonymousPreTriageResponse {
  sessionId: Uuid;
  episodeId: Uuid;
  patientId: Uuid;
  claimedAt: IsoTimestamp;
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

Runtime checks must still enforce that `StructuredPreTriageAnswers` contains at least one field, intensity is an integer 1–10, duration is positive, and `FEVER` is excluded from additional symptoms for a primary `FEVER` session.

## 22. Suggested Frontend API Functions

A later frontend implementation can expose a small Phase 4 service surface through the centralized API client:

```ts
startPreTriageFromIntake(request)
interpretPreTriageIntake(request)
startPreTriage(request, activePatient?)
submitPreTriageAnswers(sessionId, request, access)
completePreTriage(sessionId, access)
getPreTriageResult(sessionId, access)
claimAnonymousPreTriage(sessionId, anonymousCapability)

setAnonymousPreTriageCapability(sessionId, capability, expiresAt)
clearAnonymousPreTriage()
```

Here, `access` conceptually selects either existing Bearer handling or one anonymous capability header. Claim is the only service call that deliberately uses both.

Service behavior should:

- omit Bearer entirely for anonymous calls;
- omit capability for normal authenticated primary/managed calls;
- never serialize capability into a URL;
- send no body for complete, result, or claim;
- return both HTTP status and parsed response for completion so callers can distinguish first `201` from repeat `200` if needed;
- normalize Problem Details through the existing client;
- avoid automatically retrying non-idempotent intake/start or changed answer submissions.

## 23. Frontend Security Checklist

- Never log, display, analyze, or report the anonymous capability.
- Never place capability in a URL, query parameter, fragment, or route state that is serialized to URLs.
- Never send capability to unrelated endpoints.
- Never use a Beeexy ID, patient UUID, session UUID, Account ID, or relationship ID as authorization.
- Never trust `patientId` without backend authorization.
- Never infer managed access locally; handle concealed `404` neutrally.
- Never retain a revoked managed patient's draft as accessible to that manager.
- Never locally decide clinical urgency, diagnosis, disposition, treatment, or red flags.
- Never trust natural-language/provider output unless the backend response accepted it.
- Never invent or submit unsupported pathways or additional symptoms.
- Never include capability, Bearer tokens, natural-language input, or result content in analytics/error-reporting payloads.
- Clear expired/abandoned anonymous state.
- Continue using the established centralized auth/session system.

## 24. Frontend Integration Checklist

- [ ] Render exactly Headache, Stomach pain, Chest pain, Fever, and Other symptoms.
- [ ] Map Stomach pain to `ABDOMINAL_PAIN`.
- [ ] Offer exactly `NAUSEA`, `DIARRHEA`, and `FEVER` as the global additional catalog.
- [ ] Hide and reject additional `FEVER` when primary is `FEVER`.
- [ ] Submit an empty `additionalSymptoms` array when the user selects none.
- [ ] Start primary sessions with omitted `patientId`.
- [ ] Start managed sessions with the selected authorized profile UUID.
- [ ] Never allow an invalid Bearer request to fall back to anonymous handling.
- [ ] Send only `text` to `/pre-triage/intake`; never send a locally selected pathway or patient.
- [ ] On intake `201`, store the returned session/capability and continue from `initialAnswers.progression`.
- [ ] On intake `200`, create no local session and handle `AMBIGUOUS`/`UNRESOLVED` explicitly.
- [ ] Suppress duplicate intake submits and do not automatically retry an uncertain `201` request.
- [ ] Store the one-time anonymous capability safely.
- [ ] Attach `X-Pre-Triage-Capability` only to the correct anonymous session calls.
- [ ] Support structured intake without AI.
- [ ] Treat structured and natural-language modes as mutually exclusive.
- [ ] Handle natural-language clarification, restriction, unsupported, and provider-unavailable outcomes.
- [ ] Drive subsequent questions/options/readiness from backend progression.
- [ ] Enable completion only for backend `READY_TO_COMPLETE`.
- [ ] Send no request body to completion.
- [ ] Render the completion response directly as the neutral result.
- [ ] Support canonical result GET for recovery/reload.
- [ ] Render no urgency, diagnosis, disposition, recommendation, treatment, prescription, probability, or red-flag UI.
- [ ] Offer explicit optional claim after successful Beeexy authentication.
- [ ] Send both Bearer and original capability on claim.
- [ ] Send no body, query, or patient selector on claim.
- [ ] Clear anonymous claim state after claim success.
- [ ] Treat server `expiresAt` as authoritative and clear stale state on expiry/cleanup.
- [ ] Do not trigger completion, Clinical History, or FHIR work from intake; explicit Review/completion remains required.
