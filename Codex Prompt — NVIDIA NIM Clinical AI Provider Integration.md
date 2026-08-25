Implement the minimum real AI provider integration required for the existing Phase 4 Pre-Triage natural-language intake flow.

Use NVIDIA NIM with this model:

`nvidia/nemotron-3.5-lightning-30b-a3b`

NVIDIA's hosted OpenAI-compatible base URL is:

`https://integrate.api.nvidia.com/v1`

The repository already contains the provider-neutral Clinical AI architecture, including `IClinicalAiProvider`, `InterpretClinicalInput`, `ClinicalSafetyPolicy`, `ClinicalAiOutputValidator`, natural-language handling in `SubmitTriageAnswers`, deterministic questionnaire progression, and `UnavailableClinicalAiProvider`.

Do NOT redesign those components. Reuse them.

## Goal

Allow the existing natural-language Pre-Triage request:

```json
{
  "naturalLanguage": "I've had a headache since yesterday, intensity around 7/10, and I also feel nauseous."
}
```

for an already-started `HEADACHE` session to call NVIDIA NIM and extract structured facts equivalent to:

- `DURATION = 1 DAY`
- `INTENSITY = 7`
- `ADDITIONAL_SYMPTOMS = [NAUSEA]`

The existing deterministic application layer must remain responsible for validating, accepting, persisting, and using those facts to skip already-answered questionnaire fields.

## Important safety boundary

The AI is only a structured-information extractor.

It must NOT generate or control:

- clinical urgency
- disposition
- diagnoses
- disease probabilities
- prescriptions
- treatment recommendations
- red flags
- clinical recommendations
- questionnaire progression
- completeness
- persistence decisions

The existing application validators remain authoritative.

## Implementation requirements

### 1. NVIDIA provider adapter

Add an Infrastructure implementation of the existing `IClinicalAiProvider`.

Prefer a small adapter such as:

`NvidiaClinicalAiProvider`

Do not introduce Phase 10 entities, AI conversation history, autonomous agents, document analysis, or unrelated AI infrastructure.

Use `HttpClient` and NVIDIA's OpenAI-compatible Chat Completions endpoint rather than introducing a large vendor SDK unless there is a strong repository-specific reason.

Endpoint:

`POST {BaseUrl}/chat/completions`

Default BaseUrl:

`https://integrate.api.nvidia.com/v1`

Default model:

`nvidia/nemotron-3.5-lightning-30b-a3b`

### 2. Configuration

Introduce typed configuration/options for:

- `ClinicalAi:Provider`
- `ClinicalAi:ApiKey`
- `ClinicalAi:Model`
- `ClinicalAi:BaseUrl`
- `ClinicalAi:TimeoutSeconds`

Support environment-variable configuration such as:

- `ClinicalAi__Provider`
- `ClinicalAi__ApiKey`
- `ClinicalAi__Model`
- `ClinicalAi__BaseUrl`
- `ClinicalAi__TimeoutSeconds`

Never log the API key.

Never return it through errors.

Never commit a real credential.

### 3. Conditional dependency injection

When the configured provider is NVIDIA and all required settings are valid, register `NvidiaClinicalAiProvider` as `IClinicalAiProvider`.

Otherwise retain the existing `UnavailableClinicalAiProvider`.

Do not make the entire API fail to start merely because Clinical AI is disabled unless the existing configuration conventions explicitly require that behavior.

### 4. Prompt

Create a versioned extraction prompt dedicated only to the Phase 4 simplified Pre-Triage intake.

The system/developer prompt must instruct the model to:

- extract only facts explicitly present in the patient's message;
- respect the already-selected/pinned pathway supplied by the backend;
- only use fact/question codes allowed by the backend;
- never guess missing information;
- represent uncertainty or ambiguity explicitly;
- never provide prose outside the structured response;
- never provide urgency, diagnosis, probability, prescription, treatment, red flags, disposition, or medical recommendations.

Do not treat the prompt as the safety boundary.

Existing application validation and safety policies remain authoritative.

### 5. Structured output

Use NVIDIA's structured JSON mode where supported:

```json
"response_format": {
  "type": "json_object"
}
```

For this extraction use case, disable model reasoning/thinking if supported by the hosted NVIDIA endpoint so that the response budget is dedicated to the JSON result.

Use a deterministic/low-randomness configuration appropriate for extraction.

Strictly parse the returned JSON.

Reject malformed responses, unexpected structure, unknown enums, unknown fact codes, or missing required structural members rather than trying to repair them heuristically.

Map the vendor JSON response into the existing internal:

`ClinicalAiProviderOutput`

using the current schema version:

`clinical-interpretation-v1`

Do not change the internal Phase 4 contract unless absolutely necessary.

### 6. Provider failure mapping

Map NVIDIA/network behavior into the existing safe provider failure categories, including as applicable:

- `Unavailable`
- `Timeout`
- `InvalidStructuredResponse`
- `RejectedOutput`
- `ConfigurationUnavailable`

Respect cancellation tokens.

Use a bounded timeout.

Do not expose NVIDIA response bodies, secrets, prompts, or unnecessary clinical content in technical errors/logs.

Avoid unsafe automatic retries for requests when request state or cancellation makes them inappropriate.

### 7. Preserve deterministic validation

Do not bypass or duplicate the existing:

- `ClinicalSafetyPolicy`
- `ClinicalAiOutputValidator`
- `ClinicalAnswerValueValidator`
- pathway registry validation
- known-answer conflict detection
- locked answer persistence
- deterministic progression
- completeness checks

The provider output is only a proposal.

Only existing application code can accept it as a validated answer.

### 8. Current demo scope

Keep the current flow:

Frontend selects one supported pathway first:

- `HEADACHE`
- `ABDOMINAL_PAIN`
- `FEVER`

Then natural-language intake runs inside that pinned session.

Do NOT implement AI-controlled initial pathway/session creation in this change.

For answer extraction, preserve the current accepted natural-language facts:

- `DURATION`
- `INTENSITY`
- `ADDITIONAL_SYMPTOMS`

Do not make `PRIMARY_SYMPTOM` an authoritative AI answer because the session pathway already defines it.

### 9. Testing

Add unit/integration coverage for at least:

- successful NVIDIA structured response;
- duration extraction;
- intensity extraction;
- additional symptom extraction;
- multiple facts extracted from one message;
- malformed JSON;
- unknown fact code;
- unknown enum/value;
- ambiguous output;
- insufficient confidence;
- pathway mismatch;
- forbidden urgency/diagnosis/recommendation content;
- NVIDIA timeout;
- NVIDIA unavailable/error response;
- missing configuration;
- cancellation;
- API key never appearing in logs/errors;
- deterministic questionnaire skipping after accepted extracted values.

Tests must use fake HTTP handlers/stubs.

CI and the normal test suite must not require a live NVIDIA API key or internet access.

### 10. Verification

After implementation:

- run formatting;
- build the full solution;
- run the new focused tests;
- run all relevant Phase 4 regression tests;
- run the complete test suites;
- verify EF has no pending model changes;
- verify no migration was introduced unless genuinely necessary;
- verify OpenAPI changed only if required by an existing endpoint contract change.

Do not implement Phase 10.

Do not add clinical urgency, diagnosis, probability, treatment, disposition, or recommendation behavior.

At the end, report:

1. files created/modified;
2. exact configuration variables introduced;
3. how the NVIDIA request is constructed;
4. the extraction prompt design;
5. the NVIDIA JSON contract;
6. how it maps to `ClinicalAiProviderOutput`;
7. failure behavior;
8. tests added;
9. build/test results;
10. exact commands I should run locally to configure the API key and test the AI-powered Pre-Triage flow.