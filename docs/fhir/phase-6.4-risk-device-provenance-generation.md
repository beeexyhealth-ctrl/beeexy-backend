# Phase 6.4 RiskAssessment, Device, and Provenance generation boundaries

Phase 6.4 is derived only from Andrea's three FHIR documents, the Phase 6.2 mapping inventory, the Phase 6.3 QuestionnaireResponse boundary, and Beeexy's current authoritative source models. It introduces no FHIR SDK, release choice, profile, final FHIR JSON, Bundle, export artifact, validation, persistence, or HTTP behavior.

## RiskAssessment: concrete generation blocked

The current neutral `ClinicalAssessment` truthfully provides the assessment UUID, episode UUID, frozen clinical-rule-set UUID, and assessment time. The validated Clinical History graph also provides the patient-profile and source-event UUIDs. For a completed assessment, Andrea supports the conceptual `final` status and automatic-evaluation/non-diagnosis disclaimer.

Andrea's minimum Beeexy RiskAssessment also requires a prediction outcome, decimal probability, and mitigation. The current assessment contains none of them. It also contains no qualitative risk, urgency, disposition, diagnosis, recommendation, treatment, red flag, or finding. Questionnaire answers and reported symptom intensity are not clinical predictions and are never passed into the RiskAssessment mapping boundary.

`RiskAssessmentMapper.Inspect` therefore returns only a deterministic generation-boundary description with the authoritative source identities/time, supported concepts, and exact unresolved requirements. `RiskAssessmentMapper.Map` always raises `RiskAssessmentGenerationBlockedException`; it never returns a partial representation that could be mistaken for a usable RiskAssessment. The exception prominently names the three missing authoritative clinical inputs:

- prediction outcome;
- prediction probability;
- mitigation.

The Patient identity and final resource/reference strategy are also unresolved. Andrea's vertigo outcome, `0.72`, `moderate`, referral wording, and all other example clinical values are never reused. No AI, heuristic, symptom intensity, questionnaire answer, old urgency rule, or generic clinical knowledge fills the gap.

## Device: release-neutral software identity

`DeviceMapper` deterministically preserves only the software identity Andrea establishes:

- name `Beeexy Triage Engine` and name-type concept `manufacturer-name`;
- model-number concept `triage-core`;
- the runtime software version explicitly supplied by the generation caller;
- manufacturer concept `Beeexy Inc.`;
- type text `Clinical decision support software`.

This is Beeexy's processing software, not patient hardware. The mapper supplies no UDI, serial number, hardware identity, regulatory status, identifier, owner, canonical URL, organization relationship, or example product version. Logical resource identity and final reference remain unset. The representation cannot be serialized or claimed as validated FHIR.

## Provenance: release-neutral generation trace

`ProvenanceMapper` deterministically preserves:

- the planned export UUID and internal generation identities for Provenance, target RiskAssessment, author Device, and source QuestionnaireResponse;
- patient-profile, Clinical History event, episode, and assessment source UUIDs;
- the generation trace's UTC recorded time;
- the explicit mapping-specification version;
- Andrea's `CREATE` activity, `author` agent type, and `source` entity-role concepts.

The internal generation identities preserve relationships without claiming final FHIR reference semantics. `target`, `agent`, and `source entity` FHIR reference strings remain unset pending an approved identity/reference strategy. The representation contains no Account ID, authentication token, capability, manager authorization detail, secret, private storage location, or access decision. Internal or future FHIR identities confer no patient authorization.

## Remaining TBDs and later phases

Andrea's sources still establish no exact FHIR release, canonical profile URLs/versions, or final resource/reference strategy. No R4, R4B, R5, profile, canonical, SDK, serializer, or validator is selected. Bundle/snapshot assembly remains Phase 6.5; formal validation remains Phase 6.6. Artifact checksum/storage, download/transmission, export orchestration, and Phase 6 APIs remain unimplemented.
