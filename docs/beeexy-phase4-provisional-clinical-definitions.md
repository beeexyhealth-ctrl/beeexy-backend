# Beeexy Pre-Triage — Provisional Clinical Definitions for MVP

## Scope and Important Limitation

This document defines a **provisional MVP clinical configuration primarily derived from the abdominal/stomach pain assessment flows** observed in the reference platforms tested for Beeexy: Doctronic, Prana Health, Astrid, and Mediktor.

The definitions below are intended to unblock implementation of the Beeexy Phase 4 Pre-Triage MVP. They should be treated as **reference-platform-derived and provisional**, not as a complete clinical knowledge base.

At this stage:

- The detailed branching, red flags, and urgency criteria below are focused primarily on **abdominal/stomach pain**.
- General intake questions may later be reused across other symptom pathways where appropriate.
- **Do not create or infer symptom-specific clinical rules yet for headache, chest pain, fever, respiratory symptoms, back pain, or other symptom categories from this document.**
- Each additional symptom category should receive its own questionnaire, branching logic, red flags, urgency rules, and clinical review.
- The Pre-Triage system is intended for **orientation and urgency assessment**, not autonomous diagnosis or prescription.
- These definitions should remain marked as **provisional / pending formal clinical review** until reviewed by the Beeexy medical team.

---

## 1. What urgency levels should the Pre-Triage support?

For the MVP, the Pre-Triage should support five urgency levels:

### CRITICAL

Emergency situations involving an immediate threat to life and requiring immediate emergency attention.

### HIGH

Emergency or very urgent situations with foreseeable serious risk where the time to medical assistance matters.

### MEDIUM

Situations that require prompt medical evaluation and could become serious if they are not assessed.

### LOW

Non-urgent situations without apparent immediate life-threatening risk, but which could persist or become complicated and may require medical consultation.

### VERY_LOW

Non-urgent situations with no identified immediate life-threatening risk and which do not appear to require immediate medical attention.

### Stable internal codes

```text
CRITICAL
HIGH
MEDIUM
LOW
VERY_LOW
```

Urgency and disposition should remain separate concepts. The urgency level represents the assessed risk, while the disposition describes what the patient should do next.

---

## 2. What minimum questions should every patient answer?

The following represents the proposed core assessment dataset. The system should not necessarily ask every question explicitly if the information has already been extracted reliably from the patient's natural-language input.

1. **What is your main symptom?**
2. **Where is the symptom located?**
3. **When did it start / how long have you had it?**
4. **How intense is it?** Use a 0–10 scale where applicable.
5. **Did it begin suddenly or gradually?**
6. **Is it getting better, getting worse, or staying about the same?**
7. **How would you describe it?** For pain, examples may include sharp, cramping, aching, burning, pressure, etc.
8. **Is it constant or does it come and go?**
9. **What makes it worse?**
10. **What makes it better?**
11. **Are you experiencing any other symptoms at the same time?**
12. **Do you have any relevant chronic conditions or major medical history?**
13. **Are you currently taking any medications or supplements?**
14. **Do you have any allergies?**

Demographic/contextual information such as age and clinically relevant sex may also be collected when required by the assessment pathway.

### AI-assisted collection

If the patient writes:

> "I have cramping pain in my lower abdomen that started about two hours ago. It's around a 4 out of 10."

the AI layer may extract:

```json
{
  "primarySymptom": "abdominal_pain",
  "location": "lower_abdomen",
  "character": "cramping",
  "duration": {
    "value": 2,
    "unit": "hours"
  },
  "intensity": 4
}
```

The questionnaire engine should then avoid asking questions whose answers have already been captured with sufficient confidence.

---

## 3. What answers should generate additional questions?

The following branching rules are primarily intended for the **abdominal/stomach pain MVP pathway**.

### General pain branching

**If the patient reports pain →**
ask about location, intensity, character, onset, progression, duration, and whether the pain is constant or intermittent.

**If the pain started suddenly →**
ask about current intensity, whether it is worsening, and immediately screen for relevant red flags.

**If the pain is worsening →**
prioritize red-flag screening before lower-priority assessment questions.

**If the patient reports high-intensity pain →**
perform red-flag screening before continuing the ordinary questionnaire.

### Abdominal pain branching

**If the patient reports abdominal/stomach pain →**
ask for the specific abdominal location and associated gastrointestinal symptoms.

**If the patient reports lower abdominal pain →**
clarify whether it is predominantly right-sided, left-sided, or central and evaluate relevant urinary/reproductive symptoms when applicable.

**If the patient reports fever →**
ask for the measured temperature, if known, and when the fever began.

**If the patient reports abdominal pain with associated symptoms →**
ask about nausea, vomiting, diarrhea, constipation, blood in stool, and blood in urine as applicable.

**If the patient reports vomiting →**
ask whether it is persistent, whether the patient can keep fluids down, and whether blood is present.

**If the patient reports blood →**
determine whether it is present in vomit, stool, or urine and immediately evaluate the applicable red-flag rule.

**If the patient reports urinary symptoms →**
ask about pain or burning during urination and increased urinary urgency/frequency.

### Background branching

**If the patient reports relevant medical history →**
capture the relevant condition(s).

**If the patient takes medications or supplements →**
capture which ones.

**If the patient reports allergies →**
capture the allergen and, if required by future clinical definitions, the reaction.

---

## 4. What signs or symptoms should be considered red flags?

The following red flags are specifically proposed for the **abdominal/stomach pain pathway** based on the tested reference flows:

- Sudden severe abdominal pain.
- Abdominal pain that is rapidly worsening.
- Pain that becomes sharp or significantly more intense.
- Fever of **38 °C (100.4 °F) or higher** associated with abdominal pain.
- Persistent vomiting.
- Inability to keep fluids down.
- Blood in vomit.
- Blood in stool.
- Black or tarry stool.
- Abdominal pain radiating to the back, chest, or shoulder.
- Abdominal pain accompanied by shortness of breath.
- Pain radiating to the flank or groin, particularly when associated with urinary symptoms.
- Pain accompanied by painful urination should trigger additional evaluation and may require prompt medical assessment.

A red flag is **not a diagnosis**. It is a finding that may raise the minimum urgency/disposition required by the assessment.

---

## 5. What criteria should determine the urgency level?

The tested reference material does **not expose the complete proprietary clinical algorithm** used by systems such as Mediktor. Therefore, the MVP should not attempt to reverse-engineer or invent a diagnostic scoring algorithm.

Instead, Beeexy should use conservative deterministic rules and red-flag precedence.

### CRITICAL

Assign `CRITICAL` when the available information indicates a potential immediate threat to life requiring immediate emergency attention.

The current abdominal-pain reference tests do not provide enough information to define a complete exhaustive set of `CRITICAL` rules. These rules therefore require additional clinical definition before production use.

### HIGH

Examples of provisional rules:

```text
sudden severe abdominal pain
→ HIGH
```

```text
rapidly worsening / sharply intensifying abdominal pain
→ HIGH
```

```text
blood in vomit OR blood in stool OR black/tarry stool
→ HIGH
```

```text
abdominal pain + shortness of breath
→ HIGH
```

These combinations should lead to urgent/emergency evaluation rather than continued routine questioning.

### MEDIUM

Examples of provisional rules:

```text
abdominal pain + temperature >= 38 °C
→ at least MEDIUM
```

```text
persistent vomiting
→ at least MEDIUM
```

```text
inability to keep fluids down
→ at least MEDIUM
```

```text
abdominal pain + concerning urinary symptoms
→ at least MEDIUM
```

The exact escalation from `MEDIUM` to `HIGH` may depend on additional findings and should remain configurable.

### LOW

Example:

```text
abdominal pain persists > 24 hours
+ no improvement
+ no HIGH/CRITICAL red flags
→ LOW
```

This represents a situation that does not appear immediately life-threatening but warrants non-emergency clinical assessment.

### VERY_LOW

Example:

```text
mild-to-moderate symptoms
+ stable or improving
+ able to eat/drink
+ no identified red flags
→ VERY_LOW
```

The patient may continue monitoring the symptoms while being instructed to seek medical attention if the condition worsens or new red flags appear.

### Red-flag precedence

Urgency must follow a strict precedence rule:

```text
VERY_LOW < LOW < MEDIUM < HIGH < CRITICAL
```

A lower-severity rule must **never downgrade** an urgency level already established by a higher-priority red flag.

For example:

```text
Rule A → VERY_LOW
Rule B → HIGH

Final urgency = HIGH
```

---

## 6. What recommendation should the patient receive according to the urgency level?

### CRITICAL

**Recommendation:**

Seek emergency medical attention immediately.

The production system should eventually provide location-appropriate emergency instructions.

### HIGH

**Recommendation:**

Go to an emergency department or hospital as soon as possible. The symptoms reported may require urgent medical evaluation.

### MEDIUM

**Recommendation:**

A prompt medical evaluation is recommended. Beeexy may help the patient find an appropriate healthcare professional or service.

### LOW

**Recommendation:**

Immediate emergency care does not appear to be required based on the information provided, but medical consultation is recommended if the symptoms persist, worsen, or new concerning symptoms appear.

### VERY_LOW

**Recommendation:**

No immediate warning signs requiring urgent medical attention were identified from the answers provided. Continue monitoring the symptoms and seek medical care if they worsen, persist, or new warning signs appear.

---

## Safety Boundary

Regardless of urgency level, the Beeexy Pre-Triage should remain an orientation system.

It should not:

- Present its result as a definitive medical diagnosis.
- Prescribe medication.
- Provide a prescription when the patient insists.
- Allow user instructions to override clinical safety restrictions.
- Continue ordinary questioning when a rule requires immediate escalation.

It may:

- Collect and interpret symptoms.
- Ask symptom-dependent follow-up questions.
- Detect predefined red flags.
- Determine a predefined urgency/disposition using deterministic rules.
- Explain the result in patient-friendly language.
- Direct the patient toward appropriate professional care.

---

## Clinical Content Status

For the Phase 4 MVP, definitions created from these reference-platform observations should be represented approximately as:

```text
Source: REFERENCE_PLATFORM_DERIVED
ReviewStatus: PROVISIONAL
ClinicalApproval: PENDING_FORMAL_REVIEW
```

The current detailed clinical content is primarily suitable for the first:

```text
ABDOMINAL_PAIN
```

symptom pathway.

### Not yet defined

Do **not** derive detailed clinical rules from this document for:

```text
HEADACHE
CHEST_PAIN
FEVER
RESPIRATORY_SYMPTOMS
BACK_PAIN
OTHER_SYMPTOMS
```

Those pathways require their own evidence/reference collection, symptom-dependent questions, branching rules, red flags, thresholds, urgency rules, dispositions, and subsequent clinical review.

This separation allows the Beeexy Phase 4 architecture to be implemented now while the clinical knowledge base is expanded incrementally and versioned independently.
