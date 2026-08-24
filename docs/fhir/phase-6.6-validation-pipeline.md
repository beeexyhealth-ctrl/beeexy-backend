# Phase 6.6 FHIR validation pipeline

**Status: COMPLETE in State B. Validation pipeline implemented; concrete standards validation blocked by unresolved authoritative FHIR/clinical requirements.**

Phase 6.6 adds the internal validation boundary and lifecycle orchestration required to validate an immutable export safely. It does not claim that the Phase 6.5 release-neutral snapshot is official FHIR or standards-valid, and it does not select a FHIR SDK or validator without an authoritative specification.

## Authoritative-material review and decision

The Phase 6.6 review covered the complete Andrea collection in `beeexy-coleccion-recursos.md`, the Provenance/Device and RiskAssessment examples, the Phase 6.2 mapping inventory, the Phase 6.3 QuestionnaireResponse decision, the Phase 6.4 RiskAssessment/Device/Provenance boundary, the Phase 6.5 immutable snapshot contract, and the implemented Phase 6.1-6.5 domain, mapping, generation, serialization, persistence, and storage code.

Those sources still do not establish:

- the exact FHIR release;
- whether profiles apply and, if so, their canonical URLs and versions;
- final Patient, Questionnaire, and generated-resource identities and references;
- Questionnaire version and item `linkId` encoding;
- translation of Beeexy answer-schema JSON to FHIR `QuestionnaireResponse.answer.value[x]`;
- authoritative RiskAssessment prediction outcome, probability, and mitigation;
- an approved validation specification against which a validator can be configured.

The Phase 6.5 artifact also deliberately identifies itself as `UNRESOLVED_RELEASE_NEUTRAL_SNAPSHOT`, uses Beeexy's private snapshot media type instead of `application/fhir+json`, and records mandatory RiskAssessment as blocked. Therefore concrete validation remains externally blocked. No R4, R4B, R5, profile, canonical, default package, validator, or clinical value was inferred.

## Eligibility boundary

`IFhirValidationPrerequisiteEvaluator` separates factual eligibility from validator execution. An eligible decision must carry an exact `FhirValidationSpecification`: FHIR release, mapping version, and an explicitly resolved profile decision (`NotApplicable` or canonical plus version). That specification must exactly match the immutable export metadata before invocation.

The production evaluator recognizes the current Phase 6.5 snapshot and returns a typed blocked decision covering all three blocker categories:

- release-neutral representation: the artifact is not official FHIR JSON;
- unresolved specification: release, profiles, identities/references, `linkId`, `value[x]`, and approved validation specification are unresolved;
- unavailable required content: mandatory RiskAssessment content and therefore the complete required resource set are unavailable.

Any other export is also blocked unless an approved evaluator is deliberately introduced with the exact specification. Blocked validation preserves `Generated`, creates no `FhirValidationResult`, and cannot be confused with invalid FHIR content.

## Orchestration and checksum integrity

`ValidateFhirExport` performs the following sequence inside a database transaction:

1. acquires a PostgreSQL transaction advisory lock derived from the export UUID;
2. loads the export in the supplied patient scope and its optional validation evidence;
3. returns an existing final result idempotently, or rejects a non-`Generated` export;
4. reads the exact immutable bytes from private storage;
5. recomputes Phase 6.5 SHA-256 and compares the lowercase checksum using a fixed-time byte comparison;
6. evaluates eligibility only after integrity succeeds;
7. invokes `IFhirValidator` only for an eligible, exactly matching specification;
8. atomically records validator evidence and applies `Generated -> Validated` or `Generated -> ValidationFailed` only for a completed validator result.

A missing/unreadable artifact, checksum mismatch, blocked decision, unsupported specification, validator outage, or validator exception does not create validation evidence and does not change the export from `Generated`. Validation never writes, deletes, regenerates, or replaces artifact bytes, checksum, or private storage identity.

## Validator abstraction and production status

`IFhirValidator` receives the export identity, exact verified bytes, persisted checksum association, and exact validation specification. Its result distinguishes valid content, invalid content, unavailable infrastructure, and unsupported specification. Completed valid/invalid results require validator name and version; valid results cannot contain errors, and invalid results require at least one error.

The production registration is deliberately an unavailable validator adapter, and the production prerequisite evaluator blocks before that adapter can be invoked for Phase 6.5 artifacts. This is not a concrete FHIR SDK integration. Controlled test validators prove both final lifecycle transitions without claiming that their synthetic bytes or test release are real standards-valid FHIR.

## Lifecycle, persistence, retries, and concurrency

The existing Phase 6.1 model remains the authority:

- `Pending -> Validated` and `Pending -> ValidationFailed` remain impossible;
- only `Generated` with immutable artifact metadata may create validation evidence;
- passed evidence produces `Validated`; failed content evidence produces `ValidationFailed`;
- evidence freezes validator identity/version, exact artifact checksum algorithm/value, error/warning counts, and the repository-clock completion timestamp;
- export release, mapping, and profile metadata preserve the validated specification identity;
- a final validation result is returned idempotently and cannot be overwritten with a contradictory outcome.

The advisory lock serializes concurrent attempts for the same export. A second attempt reloads the committed final state and does not invoke the validator or create another result. Infrastructure failures leave the export retryable; a later successful attempt can complete it.

The current schema already represents all durable Phase 6.6 evidence, and its one-to-one validation-result constraint prevents duplicate evidence. No migration or EF model change is required.

## Privacy and security

Artifact bodies are passed only through internal storage and validator boundaries and are not logged. Validator free-text details and exception messages are neither persisted nor returned. Application diagnostics contain only error/warning counts, generic summaries, and fixed generic category codes; provider codes and details are discarded because no approved safe-code allowlist exists. The persisted result contains counts and validator/artifact metadata, not raw diagnostic content or stack traces.

The private storage reference is never included in diagnostic or exception text. This internal use case introduces no HTTP surface, and patient scoping is enforced while loading the export. Export IDs, FHIR logical identities, and private storage identifiers remain identifiers—not authorization capabilities.

## Verification coverage

Focused tests cover the full blocker set, unresolved-profile rejection, repeated blocked behavior, pending rejection, exact-byte checksum ordering, tampering without validator invocation or artifact rewrite, passed/failed evidence, repository-clock timestamps, diagnostic sanitization, validator exceptions, infrastructure retry, final-result idempotency, and PostgreSQL serialization of concurrent attempts. PostgreSQL tests also verify final status/result atomicity and immutable checksum/storage identity.

The final verification evidence is recorded in `IMPLEMENTATION_PLAN.md`. Phase 6.6 adds no API, content/download route, public URL, external FHIR transmission, FHIR SDK dependency, artifact regeneration, new FHIR resource, or clinical inference. Those boundaries remain unchanged for Phase 6.7 or a separately authorized specification-unblocking change.
