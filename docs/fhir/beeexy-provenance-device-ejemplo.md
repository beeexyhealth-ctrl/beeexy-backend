# Provenance + Device de Beeexy, casilla por casilla

Este es el par que representa a la inteligencia artificial como autora de una recomendación. `Device` describe al modelo (el "quién"), y `Provenance` registra el acto de generación (el "quién hizo qué, cuándo y sobre qué"). Juntos convierten una salida de IA en algo auditable.

Casi ninguna app de triaje implementa esto. Es el diferenciador de Beeexy en una conversación con un hospital o un regulador.

---

## Primero el Device: la ficha del modelo

`Device` normalmente describe un aparato físico (una bomba de infusión, un marcapasos). Aquí lo usamos para describir un aparato de software: el modelo de Beeexy.

```json
{
  "resourceType": "Device",
  "id": "beeexy-triage-engine",
  "deviceName": [
    {
      "name": "Beeexy Triage Engine",
      "type": "manufacturer-name"
    }
  ],
  "modelNumber": "triage-core",
  "version": [
    {
      "value": "2.4.1"
    }
  ],
  "manufacturer": "Beeexy Inc.",
  "type": {
    "text": "Clinical decision support software"
  }
}
```

### Casilla por casilla

**`deviceName`**: el nombre del modelo. El `type` "manufacturer-name" solo dice que es el nombre que le da su fabricante.

**`modelNumber`**: qué motor concreto es. Si Beeexy tiene varios (uno de triaje, otro de segunda opinión), aquí se distinguen.

**`version`**: la versión exacta del modelo. 

**`manufacturer`**: quién lo hace. Beeexy.

**`type`**: qué clase de software es. "Clinical decision support software" es la etiqueta que lo sitúa como apoyo a la decisión, no como diagnóstico. Coherente con toda la estrategia.

---

## Ahora el Provenance: el registro del acto

`Provenance` responde a "¿de dónde viene este dato?". No describe al modelo (eso lo hace Device), describe el **acto** de haber generado algo: qué se generó, cuándo, y quién o qué lo hizo.

```json
{
  "resourceType": "Provenance",
  "id": "prov-a1b2c3",
  "target": [
    { "reference": "RiskAssessment/beeexy-triage-a1b2c3" }
  ],
  "recorded": "2026-07-15T10:30:00Z",
  "activity": {
    "coding": [
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-DataOperation",
        "code": "CREATE",
        "display": "create"
      }
    ]
  },
  "agent": [
    {
      "type": {
        "coding": [
          {
            "system": "http://terminology.hl7.org/CodeSystem/provenance-participant-type",
            "code": "author",
            "display": "Author"
          }
        ]
      },
      "who": {
        "reference": "Device/beeexy-triage-engine"
      }
    }
  ],
  "entity": [
    {
      "role": "source",
      "what": {
        "reference": "QuestionnaireResponse/qr-456"
      }
    }
  ]
}
```

### Casilla por casilla

**`target`**: sobre qué recurso trata esta procedencia. Apunta al `RiskAssessment` que se generó. Es decir: "este registro explica de dónde viene aquel resultado de triaje". Obligatorio.

**`recorded`**: cuándo se registró. Fecha y hora.

**`activity`**: qué se hizo. `CREATE` significa que este acto fue crear algo nuevo. Usa una lista de códigos ya definida por FHIR.

**`agent`**: el quién. Este es el campo importante. Dentro:
- `type` con código `author`: dice que este agente es el autor de lo generado.
- `who` apuntando a `Device/beeexy-triage-engine`: y el autor **es el modelo**, la ficha Device de arriba.


**`entity`**: sobre qué materia prima trabajó. Con `role: "source"` apuntando a las respuestas del paciente: dice que el modelo partió de ese `QuestionnaireResponse`. Cierra el círculo de trazabilidad: sabes qué modelo, en qué versión, a partir de qué datos, produjo qué resultado, y cuándo.

