# Exportación JSON (RNF-63) — `formatVersion` 1

Contenido completo de una matriz, para integraciones y migraciones. Implementación:
`src/Auros.Ram.Core/Serialization/JsonExchange.cs`. **El JSON no está cifrado.**

```jsonc
{
  "format": "auros.ram/matrix",      // identificador fijo
  "formatVersion": 1,                // se rechaza si es mayor que la soportada
  "exportedUtc": "2026-10-30T12:00:00Z",
  "project": {
    "name": "Relevamiento 2026",     // obligatorio
    "client": "ACME S.A.",
    "consultancy": "Auros",
    "date": "2026-10-30",
    "status": "inProgress",          // inProgress | finished
    "consultancyLogo": "iVBORw0…",   // PNG/JPG en base64 (opcional)
    "clientLogo": null,
    "riskScaleId": "guid",           // las tres son obligatorias y del tipo correcto
    "valueScaleId": "guid",
    "difficultyScaleId": "guid"
  },
  "scales": [
    {
      "id": "guid",
      "name": "Riesgo estándar",     // 1–150 caracteres
      "type": "risk",                // risk | value | difficulty
      "levels": [
        { "id": "guid", "name": "Bajo", "color": "#2E9E44", "order": 1 }  // nombre 1–80, color #RRGGBB, orden 0–100
      ]
    }
  ],
  "risks": [
    {
      "id": "guid",
      "title": "…",                  // 1–200
      "shortDescription": "…",       // 1–500
      "longDescription": "…",        // obligatoria
      "riskLevelId": "guid",         // nivel de la escala de Riesgo del proyecto
      "valueLevelId": "guid",        // nivel de la escala de Valor del proyecto
      "difficultyLevelId": "guid",   // nivel de la escala de Dificultad del proyecto
      "riskJustification": "",
      "valueJustification": "",
      "difficultyJustification": "",
      "createdUtc": "…",
      "modifiedUtc": "…"
    }
  ]
}
```

Al importar se aplican las mismas validaciones que en la edición (RF-02, RF-03, RF-08, RF-11).
Un error indica el riesgo o campo que falla. La importación crea un archivo `.rmx` nuevo, con una
contraseña nueva.
