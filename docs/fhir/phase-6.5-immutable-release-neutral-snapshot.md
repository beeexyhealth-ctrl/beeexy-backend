# Phase 6.5 immutable release-neutral interoperability snapshot

Phase 6.5 assembles and stores an immutable interoperability artifact from the Phase 6.1-6.4 contracts. The artifact is deliberately named a **release-neutral interoperability snapshot**. It is not official FHIR JSON, a Bundle, a validated FHIR export, or a substitute for the unresolved Andrea requirements.

## Artifact boundary

The deterministic snapshot contains the supported representations in this fixed order:

1. `QuestionnaireResponse` concept;
2. `Device` concept;
3. `Provenance` concept.

The envelope explicitly records that it is incomplete, is not official FHIR JSON, is not a complete FHIR export, and cannot be submitted to a FHIR validator. It uses the private media type `application/vnd.beeexy.interoperability-snapshot+json` and format marker `beeexy-release-neutral-interoperability-snapshot` version `1`. It contains no `resourceType`, Bundle, selected FHIR release, invented canonical/profile, or final FHIR reference.

The snapshot freezes the export/generation identity and time, mapping version, authoritative patient/history-event/episode/assessment/rule-set identities, historical questionnaire UUID/code/version/content hash, frozen question and answer facts, explicitly supplied runtime software version, and internal Provenance relationships. Serialization uses a fixed property and resource order, compact UTF-8 JSON, invariant UUID/timestamp formatting, and explicit enum translations. The same frozen snapshot serializes to identical bytes.

## Mandatory RiskAssessment blocker

Andrea's collection requires RiskAssessment for the final export, but the authoritative neutral assessment has no prediction outcome, probability, or mitigation. Phase 6.5 does not omit that requirement while claiming completeness and does not fabricate the vertigo example or any other clinical value.

Instead, `RiskAssessment` appears under `blockedRequiredResources` with an explicit missing-authoritative-clinical-input status, the supported final/disclaimer concepts, source occurrence time, and unresolved requirements. The final standards-compliant FHIR export therefore remains blocked even though this internal snapshot is a complete Phase 6.5 deliverable.

## Generation and lifecycle

The internal `GenerateFhirExport` orchestration:

1. validates the command and freezes the injected server clock at PostgreSQL precision;
2. starts a database transaction and acquires a transaction-scoped PostgreSQL advisory lock for patient plus idempotency key;
3. reloads the authoritative Clinical History event, completed episode, neutral assessment, frozen questionnaire, questions, and answers from PostgreSQL;
4. returns an existing matching export for a repeated idempotent request;
5. creates and saves a Phase 6.1 `Pending` export using `UNRESOLVED_RELEASE_NEUTRAL_SNAPSHOT`, the supplied mapping version, and null profile metadata;
6. assembles and serializes the snapshot, calculates SHA-256 over those exact bytes, and atomically stores them under a new opaque private reference;
7. transitions the export to `Generated`, persists the exact checksum/private reference, and commits.

The per-patient Phase 6.1 unique constraint remains the database authority. The advisory lock serializes concurrent requests in the same patient/idempotency scope so one creates the export/artifact and the other returns it. Different keys can create distinct exports, and the same key is isolated across patients.

Storage failure leaves no committed `Pending` export. If persistence or commit fails after a successful write, generation deletes the private artifact with a non-cancelled cleanup operation. Cleanup failure raises an explicit reconciliation-required exception rather than hiding a possible orphan. A stored reference is only armed for cleanup after the immutable write succeeds.

## Private immutable storage

`IFhirArtifactStore` is replaceable infrastructure. The local adapter uses a configured private filesystem root, a cryptographically random 256-bit lowercase-hex opaque key, and an internal `beeexy-private-artifact://local-store/...` reference. The reference contains no patient, Clinical History, FHIR resource, or export identifier and is not an HTTP/public URL or authorization mechanism.

Writes go to a uniquely named temporary file and are atomically moved without overwrite. Existing artifact identities are rejected; exact stored bytes can be read only through the internal adapter; generation never logs artifact contents. There is no public content route.

## Checksum and immutability

The checksum algorithm is `SHA-256`, encoded as 64 lowercase hexadecimal characters. It is computed over the exact byte array passed to storage. The Phase 6.1 one-way `Pending -> Generated` transition makes artifact identity/checksum immutable and prevents regeneration in place. Phase 6.5 creates no validation result and enters no validation state.

## Remaining requirements and out-of-scope behavior

The following remain explicitly unresolved and are not inferred:

- exact FHIR release;
- canonical profile applicability, URLs, and versions;
- Patient, Questionnaire, and generated-resource identity/reference strategy;
- Questionnaire version encoding and item `linkId` strategy;
- Beeexy answer schema/JSON translation to FHIR `answer.value[x]`;
- authoritative RiskAssessment prediction outcome, probability, and mitigation.

No FHIR SDK, official serializer, validator, validation transition, download/API endpoint, public URL, external server transmission, amendment mapping, or additional FHIR resource was introduced by Phase 6.5. The subsequent Phase 6.6 pipeline recognizes this artifact as standards-validation blocked and preserves it in `Generated`; see `phase-6.6-validation-pipeline.md`.

## Persistence and verification

The Phase 6.1 schema already represents the required generation status, mapping/release marker, checksum algorithm/value, opaque private URI, timestamps, source, and per-patient idempotency key. Phase 6.5 therefore adds no migration or EF model change.

Verification completed with a zero-warning/error Debug solution build; 16 focused unit tests and 4 focused real-PostgreSQL Phase 6.5 tests; 47/47 Phase 6.1-6.4 unit regressions; 545/545 complete unit tests; and all 14 migration-behavior regressions. The complete integration suite ran 339 tests: 333 passed and exactly the six pre-existing Phase 5 fixture/startup failures remained; no Phase 6.5 test failed. EF has no pending model changes, OpenAPI remains 21 paths with no FHIR route, the Domain has no FHIR SDK dependency, and formatting plus diff checks passed.
