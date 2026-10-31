# Exportación JSON (RNF-63) — `formatVersion` 4

Contenido completo de una matriz, para integraciones y migraciones. Implementación:
`src/Auros.Ram.Core/Serialization/JsonExchange.cs`. **El JSON no está cifrado.**

```jsonc
{
  "format": "auros.ram/matrix",      // identificador fijo
  "formatVersion": 4,                // se rechaza si es mayor que la soportada o anterior a 3
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
      "shortDescription": "…",       // 1–500 caracteres visibles (sin contar las marcas de formato)
      "longDescription": "…",        // obligatoria
      "impactLevelId": "guid",       // estado actual: el de la última evaluación del historial
      "probabilityLevelId": "guid",
      "impactJustification": "",
      "probabilityJustification": "",
      "createdUtc": "…",
      "modifiedUtc": "…",
      "assessments": [               // historial de evaluaciones, de la más antigua a la más reciente
        {
          "id": "guid",
          "date": "2026-09-20",      // desde cuándo rige este impacto y esta probabilidad
          "impactLevelId": "guid",   // niveles de las escalas del proyecto
          "probabilityLevelId": "guid",
          "note": "Se aplicó un control",
          "recordedUtc": "…"         // cuándo se registró (ordena dos evaluaciones del mismo día)
        }
      ]
    }
  ]
}
```

El nivel de riesgo de cada riesgo no se exporta: se calcula con el mapa de calor (ver
`src/Auros.Ram.Core/Services/HeatMap.cs`). Por omisión, con las posiciones *i* (impacto) y *p*
(probabilidad) contadas desde 1, el puntaje *i·p / (nI·nP)* se compara con los umbrales
((k+1)/(n+1))², k = 1…n−1, siendo *n* la cantidad de niveles de riesgo. En 5 × 5
y los tres niveles por omisión (Bajo, Medio, Alto) da: producto 1–6 bajo, 8–12 medio, 15–25 alto.

Al importar se aplican las mismas validaciones que en la edición (RF-02, RF-03, RF-08, RF-11).
Un error indica el riesgo o campo que falla. La importación crea un archivo `.rmx` nuevo, con una
contraseña nueva (opcional).

Un JSON de la versión 3 (sin `assessments`) se importa con una sola evaluación por riesgo: su estado
actual, fechado el día en que se creó.

Los JSON de las versiones 1 y 2 (valor agregado y dificultad) fueron de prueba y se rechazan con un
mensaje claro.

## Formato de las descripciones

`shortDescription` y `longDescription` guardan el texto con marcas simples (ver
`src/Auros.Ram.Core/Text/RichText.cs`):

| Marca | Resultado |
|---|---|
| `**texto**` | negrita |
| `*texto*` | itálica |
| `__texto__` | subrayado |
| `{g}texto{/g}` / `{p}texto{/p}` | letra grande / chica |
| línea que empieza con `- ` | ítem con viñeta |
| línea que empieza con `1. `, `2. `… | ítem numerado |

Una marca solo cuenta si se cierra en la misma línea; si no, queda como texto. El CSV y la búsqueda
usan el texto sin marcas; los informes en pantalla, el PDF y el Excel muestran el formato.
