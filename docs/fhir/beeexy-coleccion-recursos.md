# Colección de recursos FHIR de Beeexy


Están agrupadas en tres bloques según su papel: lo que Beeexy **recibe** del historial, lo que Beeexy **genera**, y el **directorio** de médicos y citas. Al final, el `Bundle` que lo envuelve todo.

---

# BLOQUE 1: lo que Beeexy recibe del historial del paciente. POR AHORA OPCIONALES, DEPENDEN DE INTEGRACIÓN CON EHR

Estas fichas Beeexy no las crea, las importa vía agregador para enriquecer el triaje. 

## Patient

La ficha del paciente. Todo lo demás apunta a ella.

```json
{
  "resourceType": "Patient",
  "id": "patient-789",
  "identifier": [
    { "system": "http://beeexy.com/patient-id", "value": "BX-000789" }
  ],
  "name": [
    { "family": "García", "given": ["María"] }
  ],
  "gender": "female",
  "birthDate": "1985-03-12"
}
```

Casillas clave: `identifier` (un id que no cambia), `name`, `gender`, `birthDate`. En US Core, `identifier` y `name` son obligatorios. El resto de recursos se refieren a este paciente con `"reference": "Patient/patient-789"`, nunca copiando estos datos.

## AllergyIntolerance

Una alergia del paciente, importada.

```json
{
  "resourceType": "AllergyIntolerance",
  "id": "allergy-001",
  "clinicalStatus": {
    "coding": [{ "system": "...allergyintolerance-clinical", "code": "active" }]
  },
  "code": {
    "coding": [
      { "system": "http://snomed.info/sct", "code": "373270004", "display": "Penicillin" }
    ],
    "text": "Alergia a penicilina"
  },
  "patient": { "reference": "Patient/patient-789" }
}
```

 `code` con su `coding` (SNOMED) más `text`. Y `patient` como referencia. 
 
## MedicationStatement

Un fármaco que el paciente toma.

```json
{
  "resourceType": "MedicationStatement",
  "id": "med-001",
  "status": "active",
  "medicationCodeableConcept": {
    "coding": [
      { "system": "http://www.nlm.nih.gov/research/umls/rxnorm", "code": "197361", "display": "Betahistine" }
    ],
    "text": "Betahistina 16 mg"
  },
  "subject": { "reference": "Patient/patient-789" }
}
```

Aquí el `system` es RxNorm, la lista oficial de medicamentos, no SNOMED. Cada tipo de dato tiene su lista: síntomas en SNOMED, fármacos en RxNorm. El patrón de la casilla es idéntico.

## Condition

Un diagnóstico previo del paciente. Ojo: esto es un diagnóstico que **ya venía en su historial**, hecho por un médico. Beeexy nunca crea un Condition a partir de su modelo. Lo importa, no lo genera.

```json
{
  "resourceType": "Condition",
  "id": "cond-001",
  "clinicalStatus": {
    "coding": [{ "system": "...condition-clinical", "code": "active" }]
  },
  "code": {
    "coding": [
      { "system": "http://snomed.info/sct", "code": "49049000", "display": "Ménière's disease" }
    ],
    "text": "Enfermedad de Ménière"
  },
  "subject": { "reference": "Patient/patient-789" }
}
```


## Consent

El permiso del paciente para importar su historial. Sin esto no puedes traer nada legalmente.

```json
{
  "resourceType": "Consent",
  "id": "consent-001",
  "status": "active",
  "scope": {
    "coding": [{ "system": "...consentscope", "code": "patient-privacy" }]
  },
  "patient": { "reference": "Patient/patient-789" },
  "dateTime": "2026-07-15T10:00:00Z"
}
```

---

# BLOQUE 2: lo que Beeexy genera


## QuestionnaireResponse

Las respuestas del paciente al triaje. Es la materia prima de la que sale el RiskAssessment, y por eso el `basis` de aquel apuntaba aquí.

```json
{
  "resourceType": "QuestionnaireResponse",
  "id": "qr-456",
  "questionnaire": "Questionnaire/beeexy-triage",
  "status": "completed",
  "subject": { "reference": "Patient/patient-789" },
  "authored": "2026-07-15T10:28:00Z",
  "item": [
    {
      "linkId": "1",
      "text": “Sintoma descrito”,
      "answer": [
        {
          "valueCoding": {
            "system": "http://snomed.info/sct",
            "code": "399153001",
            "display": "Sensación de giro"
          }
        }
      ]
    },
    {
      "linkId": "2",
      "text": "¿Cuándo empezó el síntoma?”,
      "answer": [
        { "valueString": “Hace 1 - 3 días” }
      ]
    }
  ]
}
```


## Questionnaire

La plantilla del cuestionario, la estructura vacía de preguntas. Se define una vez, y cada paciente genera un QuestionnaireResponse contra ella. La relación es la de un formulario en blanco (Questionnaire) frente a un formulario relleno (QuestionnaireResponse).

## Composition

El informe de segunda opinión, el documento clínico. Un `Composition` es la ficha que da estructura a un documento: tiene sus secciones, su autor, su fecha.

```json
{
  "resourceType": "Composition",
  "id": "comp-001",
  "status": "final",
  "type": {
    "coding": [
      { "system": "http://loinc.org", "code": "11488-4", "display": "Consultation note" }
    ]
  },
  "subject": { "reference": "Patient/patient-789" },
  "date": "2026-07-16T09:00:00Z",
  "author": [
    { "reference": "Practitioner/dr-lopez" }
  ],
  "title": "Segunda opinión: vértigo posicional",
  "section": [
    {
      "title": "Motivo de consulta",
      "text": { "status": "generated", "div": "<div>Paciente con episodios de vértigo...</div>" }
    },
    {
      "title": "Valoración",
      "text": { "status": "generated", "div": "<div>Los hallazgos sugieren...</div>" }
    }
  ]
}
```

Cuando una segunda opinión la firma un médico de verdad, el `author` apunta a un `Practitioner`, no a un `Device`. Esa es la diferencia con el triaje automático: aquí sí hubo juicio humano, y la ficha lo refleja. El `type` usa LOINC, que es también la lista de tipos de documento. Las `section` son las partes del informe.

---

# BLOQUE 3: directorio y citas

## Practitioner

Un médico del directorio.

```json
{
  "resourceType": "Practitioner",
  "id": "dr-lopez",
  "identifier": [
    { "system": "http://beeexy.com/practitioner-id", "value": "DR-0042" }
  ],
  "name": [
    { "family": "López Salcedo", "given": ["Andrea"], "prefix": ["Dra."] }
  ]
}
```

Solo describe a la persona. Su especialidad y su rol van aparte, en PractitionerRole.

## PractitionerRole

El papel de ese médico: qué hace, dónde, en qué especialidad. Se separa de Practitioner porque un mismo médico puede tener varios roles en varios sitios.

```json
{
  "resourceType": "PractitionerRole",
  "id": "role-lopez-orl",
  "practitioner": { "reference": "Practitioner/dr-lopez" },
  "organization": { "reference": "Organization/hospital-01" },
  "specialty": [
    {
      "coding": [
        { "system": "http://snomed.info/sct", "code": "418960008", "display": "Otolaryngology" }
      ]
    }
  ]
}
```

Enlaza el médico (`practitioner`) con el centro (`organization`) y añade la especialidad. Es una ficha "puente": casi todo lo que tiene son referencias a otras fichas.

## Organization

El centro o clínica.

```json
{
  "resourceType": "Organization",
  "id": "hospital-01",
  "name": "Clínica ORL Madrid",
  "address": [
    { "city": "Madrid", "country": "ES" }
  ]
}
```

Sencilla. Un nombre y unos datos de contacto.

## Appointment

La cita reservada.

```json
{
  "resourceType": "Appointment",
  "id": "appt-001",
  "status": "booked",
  "start": "2026-07-20T16:00:00Z",
  "end": "2026-07-20T16:30:00Z",
  "participant": [
    { "actor": { "reference": "Patient/patient-789" }, "status": "accepted" },
    { "actor": { "reference": "Practitioner/dr-lopez" }, "status": "accepted" }
  ]
}
```

`status: booked` es cita confirmada. `participant` lista quién va: paciente y médico, cada uno una referencia. `Schedule` y `Slot` son las fichas de la disponibilidad (la agenda del médico y los huecos libres), y contra ellas se crea el Appointment. 

---

# El Bundle: el sobre que envuelve todo

Cuando Beeexy entrega el episodio, no manda las fichas sueltas. Las mete todas en un `Bundle`, con las referencias entre ellas ya resueltas.

```json
{
  "resourceType": "Bundle",
  "id": "episode-a1b2c3",
  "type": "document",
  "timestamp": "2026-07-15T10:30:00Z",
  "entry": [
    { "resource": { "resourceType": "Composition", "...": "..." } },
    { "resource": { "resourceType": "Patient", "...": "..." } },
    { "resource": { "resourceType": "QuestionnaireResponse", "...": "..." } },
    { "resource": { "resourceType": "RiskAssessment", "...": "..." } },
    { "resource": { "resourceType": "Provenance", "...": "..." } }
  ]
}
```

