# RiskAssessment de Beeexy

Ejemplo de un episodio de triaje real, con un paciente que describe síntomas de vértigo. Cada campo lleva su explicación. Esto es lo que la capa de traducción de Beeexy tendría que generar a partir de un episodio interno.

---

```json
{
  "resourceType": "RiskAssessment",
  "id": "beeexy-triage-a1b2c3",
  "status": "final",
  "subject": {
    "reference": "Patient/patient-789"
  },
  "occurrenceDateTime": "2026-07-15T10:30:00Z",
  "basis": [
    { "reference": "QuestionnaireResponse/qr-456" }
  ],
  "prediction": [
    {
      "outcome": {
        "coding": [
          {
            "system": "http://snomed.info/sct",
            "code": "399153001",
            "display": "Peripheral vertigo"
          }
        ],
        "text": "Vértigo de probable origen periférico"
      },
      "probabilityDecimal": 0.72,
      "qualitativeRisk": {
        "coding": [
          {
            "system": "http://terminology.hl7.org/CodeSystem/risk-probability",
            "code": "moderate",
            "display": "Moderate likelihood"
          }
        ]
      }
    }
  ],
  "mitigation": "Se recomienda valoración por otorrinolaringología en un plazo de 2 a 4 semanas. No se identifican signos de alarma que requieran atención urgente.",
  "note": [
    {
      "text": "Evaluación generada automáticamente por Beeexy a partir de las respuestas del paciente. No constituye un diagnóstico médico."
    }
  ]
}
```

---

## Ahora cada casilla, una a una

### `resourceType`
```json
"resourceType": "RiskAssessment"
```
Qué tipo de ficha es. Siempre `RiskAssessment` en este recurso. 

### `id`
```json
"id": "beeexy-triage-a1b2c3"
```
El identificador único de este episodio de triaje concreto, puesto por Beeexy. Sirve para poder referirse a él después. Puedes usar cualquier esquema que no se repita.

### `status`
```json
"status": "final"
```
En qué estado está la evaluación. Los valores útiles para vosotros: `final` cuando el triaje ha terminado y el resultado es definitivo, `preliminary` si aún está en curso. Obligatorio (cardinalidad 1..1). Casi siempre será `final`.

### `subject`
```json
"subject": { "reference": "Patient/patient-789" }
```
De quién es esta evaluación. Obligatorio (1..1). No lleva los datos del paciente dentro, lleva una **referencia** a la ficha `Patient` que va aparte en el mismo Bundle. Así el paciente se describe una sola vez aunque tenga varios recursos. `patient-789` es el id de esa ficha Patient.

### `occurrenceDateTime`
```json
"occurrenceDateTime": "2026-07-15T10:30:00Z"
```
Cuándo se hizo la evaluación. Fecha y hora en formato estándar (la Z del final significa hora universal). Importa más de lo que parece: un triaje tiene fecha de caducidad clínica, y esta marca de tiempo es lo que permite saber si el resultado sigue vigente.

### `basis`
```json
"basis": [ { "reference": "QuestionnaireResponse/qr-456" } ]
```
En qué se basa esta evaluación. Referencia a las respuestas que dio el paciente, que viven en su propia ficha `QuestionnaireResponse`. Este campo es el hilo de trazabilidad: quien reciba el resultado puede tirar de aquí y ver exactamente qué contestó el paciente para llegar a él. Es opcional en la norma, pero para Beeexy es imprescindible ponerlo, porque es lo que hace la recomendación auditable en vez de una afirmación caída del cielo.

### `prediction`
Este es el corazón del recurso. Es una lista, porque una evaluación puede arrojar varias hipótesis con distinta probabilidad. Cada elemento de la lista es una predicción. Dentro:

**`outcome`**
```json
"outcome": {
  "coding": [
    { "system": "http://snomed.info/sct", "code": "399153001", "display": "Peripheral vertigo" }
  ],
  "text": "Vértigo de probable origen periférico"
}
```
Qué predice esta hipótesis. Fíjate en la estructura, porque se repite en todo FHIR: hay un `coding` con el código oficial (aquí SNOMED, el `system` dice de qué lista es, el `code` es el número, el `display` es el texto de esa lista), y un `text` libre en lenguaje humano. El código es para las máquinas, el texto para las personas. Los dos juntos.


**`probabilityDecimal`**
```json
"probabilityDecimal": 0.72
```
La probabilidad que el modelo asigna a esta hipótesis, de 0 a 1. Aquí, 72%. Este número es la honestidad estadística de Beeexy hecha explícita. No dice "es vértigo periférico", dice "hay un 72% de probabilidad de que lo sea". Esa es exactamente la diferencia entre `RiskAssessment` y `Condition` que hace que Beeexy no sea un dispositivo médico diagnóstico.

**`qualitativeRisk`**
```json
"qualitativeRisk": {
  "coding": [
    { "system": ".../risk-probability", "code": "moderate", "display": "Moderate likelihood" }
  ]
}
```
La misma probabilidad, pero en palabras: bajo, moderado, alto. Es opcional y redundante con el número, pero útil, porque un médico lee "moderado" más rápido que interpreta un 0.72. Usa una lista de códigos ya definida por FHIR.

### `mitigation`
```json
"mitigation": "Se recomienda valoración por otorrinolaringología en un plazo de 2 a 4 semanas..."
```
Qué hacer al respecto. Aquí va la recomendación de actuación: a quién derivar, con qué urgencia, si hay o no signos de alarma. Es texto libre. Este es el campo que el paciente y su médico van a leer con más atención, así que la redacción importa.

### `note`
```json
"note": [ { "text": "Evaluación generada automáticamente por Beeexy... No constituye un diagnóstico médico." } ]
```
Notas libres. Aquí es donde se mete, sí o sí, el descargo: que esto lo generó un sistema automático y que no es un diagnóstico. Es una lista, puede llevar varias notas.

---

## Lo que falta y que iría en otras fichas del Bundle

Este `RiskAssessment` no viaja solo. En el Bundle completo lo acompañarían:

- La ficha `Patient` a la que apunta `subject`.
- La ficha `QuestionnaireResponse` a la que apunta `basis`, con todas las respuestas del paciente.
- Una ficha `Provenance` que diga qué modelo de Beeexy generó este RiskAssessment, en qué versión y cuándo. Esa es la trazabilidad de la IA de la sección 5 del documento, y va en su propio recurso apuntando a este.
