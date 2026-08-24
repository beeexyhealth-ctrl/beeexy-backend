# Phase 6.2 Andrea FHIR mapping inventory

This inventory is derived only from:

- `docs/fhir/beeexy-coleccion-recursos.md`
- `docs/fhir/beeexy-provenance-device-ejemplo.md`
- `docs/fhir/beeexy-riskassessment-ejemplo.md`

It defines contracts and known requirements, not generated FHIR resources. Example identifiers are examples, not authorization credentials or production identifier rules.

## QuestionnaireResponse

Andrea establishes a completed `QuestionnaireResponse` with its own identity, a `Questionnaire` reference, a Patient subject reference, authored time, and items containing `linkId`, question text, and typed answers. The examples show a SNOMED-coded symptom answer and a string answer.

The current mapping input preserves the patient-owned `PreTriageEpisode`, Clinical History source event, frozen questionnaire UUID/code/version, completion time, exact question UUID/code/text/order, exact answer JSON/time, and reported symptom terminology without translating any value.

Unresolved: the FHIR release and profiles; the QuestionnaireResponse's own resource identity; Patient and Questionnaire resource identities; how the frozen questionnaire version is represented in the Questionnaire reference; the `linkId` rule; and the exact translation from each Beeexy answer schema/JSON value to a FHIR answer choice. The example's `qr-456`, `Patient/patient-789`, `Questionnaire/beeexy-triage`, link IDs, and vertigo code are not hardcoded.

## RiskAssessment

Andrea establishes `status: final` for a completed assessment, a Patient subject, occurrence time, a mandatory Beeexy `basis` reference to the `QuestionnaireResponse`, prediction content, mitigation text, and the exact automatic-evaluation/non-diagnosis note shown in the source document. The prediction example contains a SNOMED outcome, decimal probability, and optional qualitative risk.

The authoritative current Beeexy assessment is deliberately neutral. It truthfully supplies only patient/source/episode/assessment/rule-set identities and occurrence time. It supplies no prediction, probability, qualitative risk, mitigation, urgency, disposition, diagnosis, treatment, or recommendation. Consequently its mapping input is explicitly not ready for resource generation. Prediction outcome, probability, and mitigation remain unresolved clinical input requirements; the vertigo outcome, `0.72`, `moderate`, and referral wording are example data and are never reused.

## Device

Andrea establishes the Beeexy software identity fields: name `Beeexy Triage Engine`, name type `manufacturer-name`, model number `triage-core`, manufacturer `Beeexy Inc.`, and type text `Clinical decision support software`. The exact runtime software version is required input. The example's `2.4.1` is not treated as the current configured version or silently defaulted.

Unresolved: the FHIR release, profiles, and actual runtime software version supplied by the future generation component.

## Provenance

Andrea establishes that Provenance targets the generated `RiskAssessment`, records generation time, uses the `CREATE` activity from `http://terminology.hl7.org/CodeSystem/v3-DataOperation`, identifies the Device as `author` using `http://terminology.hl7.org/CodeSystem/provenance-participant-type`, and identifies the `QuestionnaireResponse` with entity role `source`.

The mapping input preserves those three typed outbound relationships plus the export UUID and the internal patient, Clinical History event, episode, and assessment UUIDs. Outbound logical IDs support references only and confer no patient authorization.

## Mapping specification identity

Every future mapper must receive an explicit Beeexy mapping-version identity. FHIR release and profile applicability have distinct unresolved states. An export-version snapshot cannot be created until the release is supplied and profiles are explicitly specified or explicitly declared not applicable.

Andrea's documents specify no FHIR release, canonical profile URLs, or profile versions. Phase 6.2 therefore defines no `R4`, US Core profile, Beeexy canonical URL, or other inferred value.
