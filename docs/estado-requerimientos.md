# Estado de requerimientos — versión 0.1.0

✅ implementado y probado · 🟡 parcial · ⏳ pendiente (fuera del alcance de esta versión)

## Parte I — Funcionales

| Código | Estado | Dónde / nota |
|---|---|---|
| F1, RF-01 a RF-05 | ✅ | **Por decisión del cliente, dos dimensiones: Impacto y Probabilidad de ocurrencia**; el nivel de riesgo sale de un **mapa de calor** editable (Escalas). `RiskMatrixDocument`, `Validation`, `HeatMap`; confirmación al quitar o eliminar. Cada nivel tiene **color de fondo y color de fuente**, con aviso de contraste bajo |
| RF-06 | ✅ | Escalas → *Guardar como plantilla* (`.rmt` cifrado); Nueva matriz → *desde plantilla* |
| RF-07 a RF-09 | ✅ | Ficha del proyecto: **una matriz corresponde a una sola empresa** (por decisión del cliente no existe la consultora); logo de la empresa; escalas bloqueadas cuando hay riesgos |
| RF-10 a RF-12 | ✅ | Editor con botones de color y justificación por dimensión, y el nivel de riesgo resultante a la vista; búsqueda, filtros por nivel de riesgo, impacto y probabilidad, y orden |
| RF-13, RF-14 | ✅ | Matriz de riesgo impacto × probabilidad pintada como mapa de calor; tamaño según escalas, celdas de igual altura, *tooltip* y clic para abrir la ficha |
| RF-15 | ✅ | Logo de la empresa en pantalla y en PDF; exportación PDF/Excel e impresión |
| RF-16 a RF-19 | ✅ | Ficha resumen, ficha detallada y distribución; todo se calcula en el momento |
| RN-02 a RN-12 | ✅ | RN-06: se aplica la regla más estricta de RF-05 (una escala asignada nunca se elimina) |
| F6 | 🟡 | Probabilidad × impacto implementado (mapa de calor). Controles: no existen todavía; el formato es versionado para agregarlos con una migración |

## Parte II — No funcionales

| Código | Estado | Nota |
|---|---|---|
| RNF-01, 02, 03, 05 | 🟡 | Multiplataforma por diseño (Avalonia/.NET 10); probado solo en Windows 11. Sin red. DPI por monitor |
| RNF-04, 18, 23 | 🟡 | Arranque y ~200 MB de memoria con la ventana abierta medidos en este equipo; falta medir en hardware mínimo |
| RNF-06 a RNF-10 | ✅ | .NET 10 LTS + Avalonia + MVVM; capas separadas; licencias MIT/BSD/Apache; `dotnet publish -r <rid>` autocontenido |
| RNF-11 a RNF-14 | ✅ | `.rmx` = cabecera + SQLCipher; versionado y migración con copia del original |
| RNF-15, RNF-16 | ✅ | Pestañas con clave y sesión independientes; configuración en la carpeta de datos del usuario |
| RNF-17 | ✅ | Plantillas con el mismo formato cifrado (escalas; "categorías y criterios" no existen aún en el modelo) |
| RNF-19, 21, 22 | ✅ | Probado con 5.000 riesgos: guardado y apertura < 2 s; guardado en segundo plano |
| RNF-20 | 🟡 | 5.000 riesgos probados; controles e historial no existen todavía en el modelo (F6) |
| RNF-24 a RNF-30 | ✅ | Escritura atómica, autoguardado cifrado con recuperación, integridad, copia antes de migrar, 100 niveles de deshacer, log sin datos de negocio |
| RNF-31 a RNF-37 | 🟡 | AES-256 (SQLCipher 4) + Argon2id; sin recuperación de contraseña; mínimo 12 caracteres con indicador; cambio de contraseña con recifrado; bloqueo por inactividad. **Por decisión del cliente, la contraseña es opcional por ahora**: un archivo sin contraseña no se cifra ni se bloquea (ver `docs/formato-archivo.md`) |
| RNF-38 | ✅ | Advertencia antes de exportar; PDF con contraseña opcional |
| RNF-39 | ✅ | La aplicación no hace ninguna conexión de red |
| RNF-40, RNF-41 | 🟡 | Versiones fijadas (gestión central, dependencia transitiva vulnerable corregida). Firma de código ⏳ (requiere certificados) |
| RNF-42 | 🟡 | Todos los textos en `.resx`; faltan las traducciones a inglés y portugués |
| RNF-43 | 🟡 | Nuevo, Abrir, Guardar, Guardar como, Recientes, cambios sin guardar y confirmación al cerrar. El doble clic en el explorador requiere el instalador (RNF-50); la app ya abre el archivo que recibe como argumento |
| RNF-44, 45, 47, 48 | ✅ | Atajos Ctrl/Cmd; tema claro/oscuro del sistema; mensajes accionables; ventana mínima de 1024 × 700 |
| RNF-46 | 🟡 | Texto `[ABR]` además del color en matriz e informes, y ✓ en el nivel elegido; falta auditar el contraste WCAG AA |
| RNF-49 a RNF-52, RNF-54 | ⏳ | Instaladores (MSI, DMG, AppImage, .deb), asociación de extensión y actualización |
| RNF-53 | ✅ | El formato no depende del sistema operativo (little endian fijo, SQLite) |
| RNF-55 a RNF-58 | ⏳ | Licenciamiento, activación y período de prueba |
| RNF-59, RNF-60 | ✅ | Visor de licencias en Acerca de; versión semántica en Acerca de y en la cabecera del archivo |
| RNF-61 | ✅ | Excel con formato, PDF y CSV (separador `;`) de la lista de riesgos y de la matriz de riesgo |
| RNF-62 | ⏳ | Importación desde Excel/CSV |
| RNF-63 | ✅ | Exportación e importación JSON documentada y versionada |
| RNF-64 | ⏳ | Copiar y pegar con Excel |
| RNF-65 | 🟡 | *Imprimir* genera el PDF y lo abre en el visor del sistema, desde donde se imprime: Avalonia no tiene API de impresión nativa |
| RNF-66 a RNF-71 | ✅ | CI (GitHub Actions, 3 plataformas); cobertura Core 84 %, Persistence 88 %, Export 94 %; archivo de prueba v1; migraciones acumulativas; analizadores; documentación en `docs/` |
