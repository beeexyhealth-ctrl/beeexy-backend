# Read-Only Investigation — Phase 9 Symptom Diary `content_unavailable`

## Context

The frontend is correctly reaching the Phase 9 endpoint for a real persisted episode:

```http
GET /api/v1/pre-triage/episodes/b5762a03-3e78-4cdd-adb3-8cea0ac05318/symptom-diary-content
````

The backend returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "Symptom diary content unavailable.",
  "status": 422,
  "detail": "No eligible reviewed symptom diary content is available for this episode.",
  "instance": "/api/v1/pre-triage/episodes/b5762a03-3e78-4cdd-adb3-8cea0ac05318/symptom-diary-content",
  "errorCode": "symptom_diary.content_unavailable",
  "correlationId": "2a9607e655574fb0a9bb888e44d5515e"
}
```

The Pre-Triage UI used **Stomach pain**, which should correspond to the Phase 4 pathway:

```text
ABDOMINAL_PAIN
```

Phase 9 was previously completed with approved active Andrea packages for:

```text
HEADACHE
ABDOMINAL_PAIN
FEVER
CHEST_PAIN
```

including release:

```text
andrea-symptoms-v1
```

## Task

Perform a **READ-ONLY investigation**.

Do not modify production code.
Do not modify migrations.
Do not import data.
Do not change package status.
Do not change the episode.
Do not change Phase 4 pathway mappings.
Do not update the implementation plan.

I need the exact reason why this specific episode returns:

```text
422 symptom_diary.content_unavailable
```

## Investigate the episode

Inspect the persisted episode:

```text
b5762a03-3e78-4cdd-adb3-8cea0ac05318
```

Determine and report:

1. Does the episode exist?
2. Is it completed?
3. Does it have a persisted patient owner?
4. Which questionnaire-definition version is frozen on the episode?
5. What exact pathway does that frozen questionnaire resolve to?
6. Is it exactly `ABDOMINAL_PAIN`?
7. If not, what pathway is actually stored and why?

Do not infer the pathway from frontend text. Read the persisted backend state.

## Investigate Phase 9 content resolution

Trace the exact runtime path used by:

```text
GET /api/v1/pre-triage/episodes/{episodeId}/symptom-diary-content
```

through:

* episode read repository;
* `GetSymptomDiaryContent`;
* active package provider;
* Phase 9 package validation;
* authorization/eligibility checks.

Identify the exact branch that produces:

```text
symptom_diary.content_unavailable
```

for this episode.

## Investigate Andrea package availability

For the episode's actual persisted pathway, inspect the database/package state.

Report whether an exact active Phase 9 package exists.

For the relevant package, report:

```text
package code
package version
packageVersionId
pathway
source
review status
approval status
approvedAt
activatedAt
content hash
```

Verify whether it is:

```text
MEDICAL_TEAM_PROVIDED
REVIEWED
APPROVED
ACTIVE/eligible
```

according to the actual runtime rules.

## Investigate whether Phase 9 content was imported into the current database

This is especially important.

Determine whether the current database/environment used by this API instance actually contains the Phase 9 Andrea packages.

Do not assume that passing tests means the current local database has imported data.

Report:

* current ASP.NET environment;
* current configured database target, without exposing secrets;
* whether `care.symptom_diary_package_versions` contains the Andrea packages;
* how many relevant rows exist;
* whether `andrea-abdominal-pain-symptom-diary` exists;
* whether it is active/eligible.

If the packages are missing from this database, say so explicitly.

## Investigate import command behavior

Inspect the existing command:

```text
import-phase9-andrea-symptom-content
```

Report:

1. whether it is still Production-only;
2. which environment(s) it accepts;
3. whether it targets the currently configured database;
4. whether running it in the current local development environment is intentionally blocked;
5. whether there is already an approved Development/local import mechanism.

Do not run the import command during this investigation.

## Check pathway mismatch possibilities

Explicitly verify these possibilities:

* frontend `Stomach pain` maps to a different persisted pathway than `ABDOMINAL_PAIN`;
* frozen questionnaire pathway is wrong/stale;
* Phase 9 active package is missing;
* package exists but is inactive;
* package exists but provenance/review/approval makes it ineligible;
* package is in another database/environment;
* exact-pathway lookup is failing due to code mismatch;
* package graph is corrupt and fail-closed logic is rejecting it.

## Required output

Return:

### A. Exact episode state

Include completion, ownership, frozen questionnaire and exact persisted pathway.

### B. Exact package state

Show whether the expected Andrea package exists and whether it is eligible.

### C. Exact failing condition

Quote the exact logical condition / branch that causes `content_unavailable`.

### D. Environment/database finding

Explain whether the API is using a database that actually contains Phase 9 packages.

### E. Import command finding

Explain current environment restrictions of `import-phase9-andrea-symptom-content`.

### F. Root cause

Give one concise root-cause statement.

Example format:

```text
ROOT CAUSE:
The episode is ABDOMINAL_PAIN, but the current Development database contains no active Phase 9 Andrea package because the Phase 9 import command was only executed/allowed in Production.
```

or whatever repository evidence actually proves.

### G. Safest fix options

List the minimum safe options, but **do not implement them**.

For each option, explain whether it requires:

* data import only;
* environment/CLI change;
* code change;
* migration;
* frontend change.

### H. Recommendation

Recommend the smallest correct fix based on repository truth.

## STOP CONDITION

This investigation is READ-ONLY.

Do not change anything.

Do not run imports.

Do not edit code.

Do not edit data.

Do not modify backend or frontend behavior.

Return only the investigation findings and recommended next step.