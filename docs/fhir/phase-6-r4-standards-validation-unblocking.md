# Phase 6 FHIR R4 standards-validation unblocking

## Outcome and scope

Phase 6 now has a concrete, validation-eligible mapping identified by
`beeexy-fhir-r4-base-mvp-v1`. It generates UTF-8 FHIR JSON for FHIR R4
4.0.1 and validates the exact immutable bytes that were checksummed and stored.
The historical Phase 6.2–6.6 release-neutral artifacts and their validation
blockers remain unchanged; they are not retroactively reinterpreted as FHIR.

This work adds no HTTP endpoint, public artifact location, external FHIR server,
or database migration. Phase 6.7 remains unauthorized.

## Clinical and product decisions supplied by Andrea

- The selected release is FHIR R4 4.0.1.
- The current demo does not produce authoritative inputs for a truthful
  `RiskAssessment`; that resource is deferred from this MVP rather than filled
  with inferred prediction, probability, outcome, or mitigation values.
- `QuestionnaireResponse` represents the completed pre-triage answers.
- The established Device fields are Beeexy Triage Engine,
  `manufacturer-name`, `triage-core`, Beeexy Inc., the actual runtime version,
  and Clinical decision support software.
- The established Provenance activity and agent concepts are CREATE and author,
  with the Beeexy Device as the software agent and the response as source data.

Andrea's example documents remain the source for those product mappings. The
technical choices below unblock a closed MVP artifact but are not described as
Andrea-authored or profile-approved requirements.

The focused fixture
`tests/Beeexy.Tests.Unit/Fixtures/beeexy-r4-base-mvp-reference.json` uses Andrea's
QuestionnaireResponse example text and the documented Device/Provenance
constants. Its stable question-code `linkId`, UUID identities, collection
container, omission decisions, and retargeting of Provenance to the response are
Beeexy technical MVP choices. The fixture is therefore Andrea-derived, not
Andrea-approved as a complete artifact.

## Technical MVP decisions

- Validation target: base FHIR R4 only. No US Core, national, implementation
  guide, or custom Beeexy profile is claimed, and `meta.profile` is prohibited.
- Container: one `Bundle` with `type = collection`.
- Resource set: exactly one `QuestionnaireResponse`, one software `Device`, and
  one `Provenance`. There is no `Composition`, `Patient`, or `RiskAssessment`.
- Identity: each entry receives a deterministic UUID derived from the immutable
  export identity and resource type. `resource.id` is the UUID and `fullUrl` is
  the matching `urn:uuid:<id>`.
- References: Provenance references use only those in-Bundle UUID URNs. No fake
  REST server URLs or authentication/security identifiers are used as FHIR IDs.
- Subject: `QuestionnaireResponse.subject` is omitted. It is optional in base
  R4, and omitting it is more truthful than emitting a Patient reference when no
  Patient resource or approved external Patient identity is in scope.
- Questionnaire: `QuestionnaireResponse.questionnaire` is omitted. Base R4
  permits this, and Beeexy has no approved, resolvable Questionnaire canonical
  or server identity for this MVP.
- Completion: only the immutable `PreTriageEpisode` completed-source boundary
  can create the mapping input, so the emitted status is `completed`; an active
  session cannot enter this generator and masquerade as complete.
- `linkId`: the frozen Beeexy question code stored with the historical
  questionnaire version. Display order and prompt text never determine it.
- `Provenance.target`: the generated QuestionnaireResponse. Its source entity is
  the same frozen response and its agent is the Device. This records the act of
  materializing that source response while RiskAssessment is deferred.

## Deterministic `answer.value[x]` mapping

The adapter reads the answer type from each frozen `AnswerSchemaJson` and the
value from its paired immutable `AnswerJson`. It never derives type from prompt,
position, display text, or JSON shape alone.

| Frozen Beeexy answer type | R4 value |
|---|---|
| `FREE_TEXT` | `valueString` |
| `SINGLE_CHOICE` | `valueString` |
| `SYMPTOM_SELECTION` | `valueString` |
| `MULTIPLE_CHOICE` | one `valueString` answer per selected value |
| `INTEGER_SCALE` | `valueInteger` |
| `BOOLEAN` | `valueBoolean` |
| `DURATION` | `valueQuantity` with stored numeric value and textual unit |
| `TEMPERATURE` | `valueQuantity` with stored numeric value and textual unit |

The current frozen schema has no authoritative coding-system metadata for
choice tokens, so the adapter truthfully emits strings and never invents a
SNOMED, LOINC, UCUM, or private code system. A missing schema, unsupported type,
malformed payload, empty required value, or declared-type mismatch fails
generation.

## SDK and validator

Infrastructure pins `Hl7.Fhir.R4` 6.4.0. Application and Domain remain free of
Firely model dependencies. The Infrastructure adapter uses Firely R4 POCOs and
the official FHIR JSON serializer. The production validator uses Firely's
strict R4 deserializer plus recursive POCO validation for base cardinalities,
choice types, primitive formats, required coded values, and model invariants.

Beeexy additionally checks the closed-MVP contract: collection Bundle type,
exact resource set, unique UUID `fullUrl` values, `resource.id` agreement,
absence of profile claims, completed-response/omission decisions, exact
Provenance roles, and resolution of every internal resource reference inside the
Bundle. Malformed UTF-8/JSON and non-Bundle resources are invalid content, not
validator outages.

External terminology-server expansion is deliberately not executed. This is
reported as a sanitized warning distinct from structural errors. The validator
does enforce required codes known by the generated R4 model, but it does not
claim exhaustive remote ValueSet or CodeSystem validation.

## Immutable-byte lifecycle and privacy

The concrete serializer returns one byte array. Generation calculates SHA-256
over that array, stores the same array through the private immutable artifact
store, and freezes the release/mapping metadata. Validation reloads those exact
bytes, verifies SHA-256 first with the existing fixed-time comparison, and then
passes the same byte array to the R4 validator. Validation never regenerates or
replaces the artifact.

Only sanitized diagnostic counts and generic categories cross the application
boundary. Artifact content, PHI, validator exception text, internal paths, and
raw provider details are not logged or persisted as diagnostics.

## State gate

The concrete R4 path satisfies the State A design gate: real R4 generation and
serialization are available, the exact immutable stored/checksummed bytes enter
a real validator, valid content can reach `Validated`, and invalid content can
reach `ValidationFailed`. RiskAssessment remains outside the resource set, so no
clinical values are fabricated to obtain validation success.

## Verification evidence

- The final solution build completed with zero warnings and zero errors.
- All 77 Phase 6 interoperability unit tests passed, including eight direct R4
  generation, typing, fixture, structural-invalid, reference-invalid, and real
  validation-lifecycle tests.
- The complete unit suite passed 567/567.
- All 36 focused FHIR persistence/lifecycle, migration, and OpenAPI/CORS
  integration tests passed. OpenAPI remains at 21 paths with no FHIR route.
- The full integration suite ran 345 tests: 339 passed and the same six
  pre-existing Phase 5 database-outage/demo-fixture failures remained; no FHIR
  test failed.
- EF Core reports no pending model changes. No migration was added.
- Solution-wide formatting verification and `git diff --check` passed.
