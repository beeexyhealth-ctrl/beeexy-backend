# Beeexy — Chat Pre-Triage Part 4: Conversation Projection Contract

## Role
Work as a senior .NET backend engineer on the existing Beeexy backend. Implement **Part 4 — Conversation Projection Contract** only. This is the last major backend contract layer before frontend Parts 5–8. Do not implement frontend code or a second chat workflow engine.

## Existing verified foundation
Parts 1–3.1 are complete:
- Exactly five pathways: `HEADACHE`, `ABDOMINAL_PAIN`, `CHEST_PAIN`, `FEVER`, `OTHER_SYMPTOMS`.
- `POST /api/v1/pre-triage/intake/interpret`: side-effect-free natural-language interpretation (`RESOLVED`, `AMBIGUOUS`, `UNRESOLVED`) using deterministic aliases/Nemotron.
- `POST /api/v1/pre-triage/intake`: interpretation → normal session → pinned package → candidate revalidation → accepted initial answers. It never auto-completes.
- Durable intake idempotency via required `Idempotency-Key`, PostgreSQL-backed locking/mapping, safe authenticated/anonymous scopes, atomic session+answers+mapping, and replay without another AI call.
- Part 3.1 verification is complete: 21/21 idempotency integration tests, 656/656 unit tests, 423/423 PostgreSQL integration tests, migration apply→rollback→reapply all passed.

Do not redesign Parts 1–3.1.

## Product objective
Francisco wants Pre-Triage to render as a chat:

```text
Beeexy: What are you experiencing today?
[Headache] [Stomach pain] [Chest pain] [Fever] [Other]

User: My stomach has hurt for two days.

Beeexy: How intense is the pain?
[1 ───────── 10]

User: 6

Beeexy: Are you experiencing any of these?
[Nausea] [Diarrhea] [Fever] [None]
```

The frontend must **not** reproduce questionnaire/progression rules with conditions such as `if (!duration) askDuration()`. The backend must project the existing deterministic session state into a frontend-friendly conversation contract.

## Core architectural rule
**Projection, not a second workflow engine.**

Source of truth remains:

```text
PreTriageSession
+ pinned questionnaire/rule-set
+ accepted answers
+ existing deterministic progression
```

Part 4 adds only:

```text
source of truth → ConversationProjection
```

Do not add `ChatSession`, `Conversation`, `ChatMessage`, `ConversationTurn`, a second progression engine, or AI-selected next questions.

## Inspect first
Before editing inspect:
- `PreTriageSession` and lifecycle/status;
- questionnaire/rule-set pinning;
- `StartPreTriage`, `StartPreTriageFromIntake`;
- `SubmitTriageAnswers`;
- accepted-values DTOs;
- current progression/current-question logic;
- definition packages/question metadata;
- answer types/validation;
- Review/completion;
- anonymous capability authorization;
- authenticated ownership/concealment;
- History/FHIR;
- OpenAPI conventions;
- `docs/frontend-api-phase-4.md`;
- Parts 1–3.1 tests.

Reuse/refactor existing progression logic. Do not duplicate it.

## Canonical projection
Create one application-layer projection/use case, repository-named appropriately, conceptually:

```text
GetPreTriageConversationState
PreTriageConversationProjection
```

It must deterministically expose at minimum:
- session identity/status;
- authoritative pathway;
- conversation state;
- progress;
- authoritative accepted values where useful;
- exactly one `nextInteraction` when more input is required;
- clear readiness for Review.

Use this one projection builder everywhere.

## Conversation states
Expose a small stable machine contract derived from real lifecycle semantics, preferably:

```text
IN_PROGRESS
READY_FOR_REVIEW
COMPLETED
EXPIRED
```

Only include states that map cleanly to existing domain behavior.

Rules:
- unanswered required data → `IN_PROGRESS`;
- all required data accepted but explicit completion not performed → `READY_FOR_REVIEW`;
- normal completed session → `COMPLETED`;
- expired → existing repository-consistent expired projection/error behavior.

Do not invent parallel lifecycle state.

## Progress
Expose backend-calculated progress, conceptually:

```json
{
  "completed": 2,
  "total": 3,
  "percentage": 67
}
```

Derive it from the **session's pinned questionnaire definition** and accepted required values. Do not hardcode percentages or calculate from the currently active package.

Define optional-field contribution explicitly based on current questionnaire semantics.

Requirements:
- deterministic;
- percentage 0–100;
- stable for the pinned questionnaire version;
- frontend does not calculate clinical progress.

## Next interaction
For `IN_PROGRESS`, expose exactly what the frontend needs to render the next valid input, conceptually:

```json
{
  "field": "intensity",
  "prompt": "How intense is the pain?",
  "inputType": "SCALE",
  "required": true,
  "constraints": {
    "min": 1,
    "max": 10,
    "step": 1
  }
}
```

or:

```json
{
  "field": "additionalSymptoms",
  "prompt": "Are you experiencing any of these symptoms?",
  "inputType": "MULTI_SELECT",
  "required": true,
  "options": [
    { "value": "NAUSEA", "label": "Nausea" }
  ]
}
```

Derive field/question ID, prompt, input type, required semantics, constraints and controlled options from the **pinned definition**.

Do not make the frontend maintain authoritative option lists.

## Input types
Expose only types genuinely required by the current five packages. Inspect actual definitions first. Likely concepts may include structured duration, scale, single-select and multi-select.

Only expose `TEXT` if an approved existing questionnaire field supports it. Do not invent unrestricted primary-symptom storage for `OTHER_SYMPTOMS`.

For structured duration, expose sufficient constraints/units for the frontend to construct the existing valid answer contract without duplicating clinical validation rules.

## Display text
Question/prompt labels must be deterministic, not AI-generated. Prefer existing versioned definition display metadata.

If definitions lack necessary display metadata, add the smallest version-safe metadata extension to the existing definition packages without changing clinical semantics.

Nemotron must never phrase or choose the next question.

## Options and labels
Return canonical machine values separately from display labels:

```json
{
  "value": "NAUSEA",
  "label": "Nausea"
}
```

Only return options allowed by the pinned package. Respect the existing canonical representation of `NONE` rather than inventing a conflicting answer value.

## Accepted values
Where useful expose only values already accepted/persisted by the backend, preferably reusing the existing accepted-values DTO:

```json
{
  "duration": { "value": 2, "unit": "DAYS" },
  "intensity": 6
}
```

Never expose unvalidated AI candidates as authoritative accepted values.

## Pathway metadata
Expose stable code + frontend display label, conceptually:

```json
{
  "code": "ABDOMINAL_PAIN",
  "label": "Stomach pain"
}
```

Use the five authoritative pathway definitions. Add no new pathways.

## Read-only endpoint
Add a repository-consistent endpoint, preferably:

```http
GET /api/v1/pre-triage/sessions/{sessionId}/conversation
```

It must be read-only, deterministic and authorization-protected using existing anonymous/authenticated semantics.

Do not overload `/intake/interpret`.

## Mutation response integration
Inspect current successful responses for:
- `POST /api/v1/pre-triage/intake`;
- existing `/answers`.

Prefer additively returning the canonical conversation projection in successful mutation responses when this can be done without breaking contracts. This lets the frontend immediately render the next state without an extra GET.

Example:

```text
POST intake → existing result + conversation
POST answers → existing result + conversation
```

Requirements:
- additive only;
- one canonical projection builder;
- no duplicated endpoint-specific progression logic;
- dedicated GET remains useful for refresh/recovery.

If embedding it would materially complicate existing contracts, keep the GET canonical and explain why.

## Intake-prefilled progression
Part 3 may accept values from the first free-text message.

Example:

```text
"My stomach has hurt for two days and it's a 6 out of 10."
→ ABDOMINAL_PAIN
→ duration = 2 DAYS
→ intensity = 6
```

Projection must naturally skip accepted fields:

```text
nextInteraction = additionalSymptoms
```

If only duration was accepted, projection should return intensity next. The frontend must not infer this.

## Quick replies
Future quick-reply pathway buttons must continue to use the existing deterministic session-start flow:

```text
[Chest pain] → existing session start → conversation projection
```

Free text uses:

```text
"My chest hurts" → /intake → conversation projection
```

Both converge on the same normal session/projection architecture. Never force exact quick replies through Nemotron.

## Answer progression
Reuse the existing `/answers` semantics:

```text
structured answer
→ existing validation/persistence
→ deterministic progression
→ updated ConversationProjection
```

Do not create a parallel chat-answer API unless existing architecture makes it absolutely necessary.

## Ready for Review
When all required answers exist:

```json
{
  "state": "READY_FOR_REVIEW",
  "nextInteraction": null,
  "progress": {
    "completed": 3,
    "total": 3,
    "percentage": 100
  }
}
```

Session must remain uncompleted. Do not automatically complete or bypass Review.

Future frontend Part 8 will perform:

```text
chat → READY_FOR_REVIEW → existing Review → existing Complete
```

## Completed / expired
Completed:
- `state = COMPLETED`;
- `nextInteraction = null`;
- read-only;
- do not reopen immutable completion.

Expired:
- preserve current expiration semantics;
- no next clinical question;
- do not revive;
- use repository-consistent error/projection behavior.

## Version pinning — CRITICAL
Projection must always use the **session's pinned questionnaire/rule package**, never the current active package.

Explicitly test:

```text
create session with v1
activate v2
retrieve old session projection
→ still v1 prompt/options/constraints/order/progress
```

This is a mandatory acceptance criterion.

## No AI for progression
Projection must make zero Nemotron/clinical-AI calls.

AI must not:
- choose next question;
- calculate progress;
- generate options;
- decide Review readiness;
- alter question order;
- decide completion.

Add focused verification for zero provider calls.

## Authorization
Anonymous:
- preserve existing capability/cookie/session access semantics;
- projection cannot weaken Part 3.1 security;
- unauthorized callers cannot read another anonymous session.

Authenticated:
- preserve ownership/current patient rules and concealed 404 behavior where applicable;
- no cross-account leakage.

Do not special-case Demo Guest; it is a normal authenticated Beeexy patient.

## History / FHIR
Projection itself creates no History or FHIR state.

Preserve:

```text
Active session
→ answers
→ Review
→ Complete
→ Clinical History
→ FHIR
```

Regression-test this.

## Compact response contract
A conceptual example only:

```json
{
  "sessionId": "uuid",
  "state": "IN_PROGRESS",
  "pathway": {
    "code": "ABDOMINAL_PAIN",
    "label": "Stomach pain"
  },
  "progress": {
    "completed": 2,
    "total": 3,
    "percentage": 67
  },
  "acceptedValues": {
    "duration": {
      "value": 2,
      "unit": "DAYS"
    },
    "intensity": 6
  },
  "nextInteraction": {
    "field": "additionalSymptoms",
    "prompt": "Are you experiencing any of these symptoms?",
    "inputType": "MULTI_SELECT",
    "required": true,
    "options": [
      { "value": "NAUSEA", "label": "Nausea" }
    ]
  }
}
```

Follow existing DTO conventions rather than copying this blindly.

Do not leak internal rule-engine details, AI prompts/model configuration, irrelevant DB IDs, secrets, or unapproved diagnosis/urgency data.

## Errors
Use existing Problem Details conventions for invalid session ID, invalid bearer, invalid/missing anonymous authorization/capability, concealed/not found, expiration, unavailable pinned definition and unexpected failures.

Never expose stack traces/provider payloads.

## Performance
Projection must be cheap and deterministic. No AI. Avoid unnecessary DB round trips. Do not add caching unless an existing pattern clearly justifies it.

## OpenAPI
Document:
- endpoint;
- auth/capability requirements;
- state values;
- progress semantics;
- pathway;
- accepted values;
- `nextInteraction`;
- supported input types;
- options/constraints;
- `READY_FOR_REVIEW`;
- completed/expired behavior;
- pinned-version guarantee.

If projection is embedded in intake/answer responses, update those contracts too.

## REQUIRED frontend handoff document
Create:

```text
frontend-api-chat-pretriage.md
```

in the appropriate repository documentation location.

It must be a complete frontend contract for Parts 5–8 and reflect **actual implemented code**, not hypothetical APIs.

Document:

### Entry: quick replies
Exactly:
```text
Headache
Stomach pain
Chest pain
Fever
Other
```
Explain the existing deterministic session-start endpoint and request/response.

### Entry: free text
Document:
```http
POST /api/v1/pre-triage/intake
Idempotency-Key: ...
```
including key generation/retry semantics from Part 3.1.

### Intake outcomes
Explain `RESOLVED`, `AMBIGUOUS`, `UNRESOLVED` and expected frontend behavior.

### Conversation projection
Document the exact DTO, states, progress, pathway, accepted values, `nextInteraction`, input types, constraints and options.

### Answer submission
Document the exact existing `/answers` contract required for **every currently projected input type**, with JSON examples.

### Projection refresh
Document the dedicated GET endpoint.

### Review/Complete
Document:
```text
READY_FOR_REVIEW → existing Review → existing Complete
```

### Session access
Document authenticated and anonymous/capability behavior.

### Idempotency
Explain:
```text
new logical intake → new random key
retry same logical intake → reuse same key
new intake → new key
never derive from symptom text
```

### Errors
Document relevant Problem Details/error codes, including interpretation unavailable, idempotency key reused, expiration and authorization/capability errors.

This file is important: the frontend implementation prompt for Parts 5–8 will explicitly read it.

## Tests

### Five pathways
Create/project all:
```text
HEADACHE
ABDOMINAL_PAIN
CHEST_PAIN
FEVER
OTHER_SYMPTOMS
```

### Initial projection
Verify valid initial projection for each.

### Progress
Verify completed/total/percentage at multiple stages and bounds 0–100.

### Next interaction
Verify it derives from actual accepted answers.

### Intake-prefilled
Resolved free-text intake containing duration/intensity must skip those accepted fields.

### Partial intake
If duration accepted but intensity rejected, next interaction must be intensity.

### Answer progression
Submit via existing `/answers`; verify projection advances.

### Ready for Review
All required answers:
```text
READY_FOR_REVIEW
nextInteraction = null
percentage = 100
```
while session remains uncompleted.

### Completed
After existing explicit completion:
```text
COMPLETED
nextInteraction = null
```

### Version pinning
Create under v1, activate v2, verify old projection remains entirely v1 for prompt/options/constraints/order/progress denominator.

### No AI
Projection generation invokes zero Nemotron/provider calls.

### Anonymous
Authorized anonymous access succeeds; unauthorized access fails safely.

### Authenticated
Owner succeeds; another account cannot retrieve it.

### Expiration
Match existing expiration semantics.

### History/FHIR
Projection and active answering create neither History nor FHIR. Normal completion/downstream flows still work.

### Regression
Keep Parts 1–3.1 and Phase 4–6 tests green. Do not depend on persistent Demo Guest in tests.

## Database
Part 4 should normally require **no database migration**. Do not create chat/conversation/message tables.

If deterministic display metadata is missing, prefer extending existing code/config definition packages rather than persistence.

If no migration is needed, explicitly report:
```text
No database migration added.
```

## Out of scope
Do NOT implement:
- Next.js/React frontend;
- chat bubbles/layout/animations;
- composer;
- microphone/speech-to-text;
- Part 5–9;
- persistent messages/conversations;
- AI-generated next questions;
- diagnosis;
- urgency/disposition;
- treatment;
- new pathways;
- Demo Guest special behavior;
- automatic completion;
- new History/FHIR semantics;
- global API redesign.

## Expected architecture

```text
                    USER ENTRY
                       │
          ┌────────────┴────────────┐
          │                         │
     QUICK REPLY                 FREE TEXT
          │                         │
 existing session start         POST /intake
          │                         │
          └────────────┬────────────┘
                       ↓
               normal PreTriageSession
               pinned questionnaire
               accepted answers
                       │
                       ↓
             ConversationProjection
                       │
          ┌────────────┴─────────────┐
          │                          │
     IN_PROGRESS              READY_FOR_REVIEW
          │                          │
 nextInteraction              existing Review
          │                          │
 existing /answers            existing Complete
          │                          │
          └──── progression ─────────┘
                                     ↓
                              Clinical History
                                     ↓
                                   FHIR
```

## Verification checklist
1. Inspect/reuse existing progression logic.
2. Implement one canonical projection service/use case.
3. Add read-only conversation projection endpoint.
4. Derive from pinned definitions only.
5. Expose stable state/progress/pathway.
6. Expose authoritative accepted values.
7. Expose exactly one next interaction while in progress.
8. Separate machine codes from labels.
9. Project real constraints/options/input types.
10. Use zero AI for projection.
11. Add no ChatSession/message persistence.
12. Skip Part 3 prefilled values naturally.
13. Handle partial accepted intake correctly.
14. Reuse existing `/answers`.
15. Consider additive projection in intake/answer responses.
16. Keep one projection builder.
17. `READY_FOR_REVIEW` has no next interaction.
18. Never auto-complete.
19. Completed remains immutable.
20. Preserve expiration.
21. Preserve anonymous security.
22. Preserve authenticated ownership/concealment.
23. No Demo Guest special case.
24. No History/FHIR side effects.
25. Explicitly test pinned-version behavior.
26. Explicitly test zero AI calls.
27. Cover all five pathways.
28. Update OpenAPI.
29. Create `frontend-api-chat-pretriage.md`.
30. Document quick replies and free-text entry.
31. Document Part 3.1 `Idempotency-Key`.
32. Document AMBIGUOUS/UNRESOLVED.
33. Document answer examples for every projected input type.
34. Document Review/Complete handoff.
35. Document errors.
36. Avoid migration unless truly necessary.
37. Run Release build.
38. Run unit tests.
39. Run full PostgreSQL integration suite.
40. Run focused projection tests.
41. Run formatting verification.
42. Run `git diff --check`.
43. Do not implement Part 5+.

## Final report
Report concisely:
1. Files created/modified.
2. Projection service/use case.
3. Exact endpoint.
4. Exact projection DTO.
5. State semantics.
6. Progress calculation.
7. `nextInteraction` contract/input types.
8. Prompt/options/constraint derivation.
9. Pinned-version guarantee.
10. Accepted-values representation.
11. Intake integration.
12. `/answers` integration.
13. Whether projection is embedded additively in mutation responses.
14. READY_FOR_REVIEW behavior.
15. Completed/expired behavior.
16. Anonymous/authenticated authorization.
17. Confirmation of zero AI calls for projection.
18. Confirmation no conversation persistence was added.
19. History/FHIR regression behavior.
20. `frontend-api-chat-pretriage.md` location/content.
21. Whether migration was required.
22. Focused/unit/full integration results.
23. Release build.
24. Formatting/static checks.
25. `git diff --check`.
26. Any limitation frontend Parts 5–8 must account for.

Do not implement unrelated features.
