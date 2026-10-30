# Exportación JSON (RNF-63) — `formatVersion` 3

Contenido completo de una matriz, para integraciones y migraciones. Implementación:
`src/Auros.Ram.Core/Serialization/JsonExchange.cs`. **El JSON no está cifrado.**

```jsonc
{
  "format": "auros.ram/matrix",      // identificador fijo
  "formatVersion": 3,                // se rechaza si es mayor que la soportada o anterior a 3
  "exportedUtc": "2026-10-30T12:00:00Z",
  "project": {
    "name": "Relevamiento 2026",     // obligatorio
    "company": "ACME S.A.",          // una matriz = una empresa
    "date": "2026-10-30",
    "status": "inProgress",          // inProgress | finished
    "companyLogo": "iVBORw0…",       // PNG/JPG en base64 (opcional)
    "impactScaleId": "guid",         // las tres son obligatorias y del tipo correcto
    "probabilityScaleId": "guid",
    "riskScaleId": "guid",
    "heatMap": [                     // celdas fijadas a mano; las que faltan toman el nivel por omisión
      { "impactLevelId": "guid", "probabilityLevelId": "guid", "riskLevelId": "guid" }
    ]
  },
  "scales": [
    {
      "id": "guid",
      "name": "Impacto estándar",    // 1–150 caracteres
      "type": "impact",              // impact | probability | risk
      "levels": [
        { "id": "guid", "name": "Menor", "color": "#A9C4E4", "textColor": "#000000", "order": 2 }
        // nombre 1–80; color de fondo y de fuente #RRGGBB; orden 0–100
      ]
    }
  ],
  "risks": [
    {
      "id": "guid",
      "title": "…",                  // 1–200
      "shortDescription": "…",       // 1–500
      "longDescription": "…",        // obligatoria
      "impactLevelId": "guid",       // nivel de la escala de Impacto del proyecto
      "probabilityLevelId": "guid",  // nivel de la escala de Probabilidad del proyecto
      "impactJustification": "",
      "probabilityJustification": "",
      "createdUtc": "…",
      "modifiedUtc": "…"
    }
  ]
}
```

El nivel de riesgo de cada riesgo no se exporta: se calcula con el mapa de calor (ver
`src/Auros.Ram.Core/Services/HeatMap.cs`). Por omisión, con las posiciones *i* (impacto) y *p*
(probabilidad) contadas desde 1, el puntaje *i·p / (nI·nP)* se compara con los umbrales
((k+1)/(n+1))², k = 1…n−1, siendo *n* la cantidad de niveles de riesgo. En 5 × 5 con cuatro niveles
da la matriz clásica: 1–4 bajo, 5–9 medio, 10–16 alto, 20–25 crítico.

Al importar se aplican las mismas validaciones que en la edición (RF-02, RF-03, RF-08, RF-11).
Un error indica el riesgo o campo que falla. La importación crea un archivo `.rmx` nuevo, con una
contraseña nueva (opcional).

Los JSON de las versiones 1 y 2 (valor agregado y dificultad) fueron de prueba y se rechazan con un
mensaje claro.
