# Phase 6.3 QuestionnaireResponse generation boundary

Phase 6.3 implements the concrete `QuestionnaireResponseMapper` behind the Phase 6.2 provider-independent mapping contract. It is derived only from Andrea's three FHIR documents and the Phase 6.2 inventory.

The mapper is deterministic and side-effect free. Its release-neutral representation preserves:

- the authoritative Clinical History event, completed episode, and patient-profile UUIDs;
- the frozen questionnaire UUID, code, version, and content hash;
- `status: completed` and the episode completion time as the authored time;
- each submitted answer's source UUID, frozen question UUID/code/text/order and answer schema;
- the exact source answer JSON, its JSON value kind, and its recorded time.

Only submitted answers become representation items. Items are ordered by the frozen question display order. Explicit JSON `false` remains a submitted Boolean value, whereas an unanswered question produces no item. Free text and object payloads remain unchanged. No AI, terminology normalization, clinical inference, persistence, validation, or HTTP component participates in mapping.

## Deliberately unresolved fields

Andrea's documents still do not select a FHIR release or profiles and do not define production strategies for the QuestionnaireResponse logical ID, Patient reference, Questionnaire reference/version encoding, item `linkId`, or translation from Beeexy's versioned answer schemas/JSON payloads to FHIR `answer.value[x]` choices.

The representation therefore exposes those requirements explicitly, leaves the corresponding FHIR fields unresolved, and reports that it cannot yet be serialized as FHIR. No R4, R4B, R5, canonical URL, profile, example ID, link ID, or answer translation is guessed. No FHIR SDK or serializer is introduced, and the output is not claimed to be validated FHIR.

`RiskAssessment` remains blocked because the neutral authoritative assessment has no truthful prediction outcome, probability, or mitigation. Device, Provenance, Bundle generation, full export orchestration, validation, artifact/checksum storage, download, transmission, and APIs remain outside Phase 6.3.
