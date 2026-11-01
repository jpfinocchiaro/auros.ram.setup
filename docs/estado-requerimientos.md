# Estado de requerimientos — versión 0.1.0

✅ implementado y probado · 🟡 parcial · ⏳ pendiente (fuera del alcance de esta versión)

## Parte I — Funcionales

| Código | Estado | Dónde / nota |
|---|---|---|
| F1, RF-01 a RF-05 | ✅ | **Por decisión del cliente, dos dimensiones: Impacto y Probabilidad de ocurrencia**; el nivel de riesgo sale de un **mapa de calor** editable (Escalas). Al crear una matriz se elige el tamaño (de 2 × 2 a 10 × 10) con una miniatura de su mapa de calor; el nivel de riesgo por omisión es Bajo (verde), Medio (amarillo) y Alto (rojo). `RiskMatrixDocument`, `Validation`, `HeatMap`; confirmación al quitar o eliminar. Cada nivel tiene **color de fondo y color de fuente**, con aviso de contraste bajo (menos de ~3:1). La fuente automática es blanca desde ~3:1 (verdes, naranjas y rojos oscuros) y negra en los fondos claros |
| RF-06 | ✅ | Escalas → *Guardar como plantilla* (`.rmt` cifrado); Nueva matriz → *desde plantilla* |
| RF-07 a RF-09 | ✅ | Ficha del proyecto: **una matriz corresponde a una sola empresa** (por decisión del cliente no existe la consultora); logo de la empresa; escalas bloqueadas cuando hay riesgos |
| RF-10 a RF-12 | ✅ | Descripciones con formato directo (negrita, itálica, subrayado, tamaño, listas) con el editor `Simplecto.Avalonia.RichTextBox` (MIT); se guardan como texto con marcas; editor con botones de color y justificación por dimensión, y el nivel de riesgo resultante a la vista; búsqueda, filtros por nivel de riesgo, impacto y probabilidad, y orden |
| RF-13, RF-14 | ✅ | Matriz de riesgo impacto × probabilidad pintada como mapa de calor; tamaño según escalas, celdas de igual altura, *tooltip* y clic para abrir la ficha; doble clic en una celda agrega un riesgo con ese impacto y esa probabilidad; las abreviaturas de nivel se pueden ocultar (Preferencias) |
| RF-15 | ✅ | Logo de la empresa en pantalla y en PDF; exportación PDF/Excel e impresión |
| RF-16 a RF-19 | ✅ | Ficha resumen, ficha detallada y distribución; todo se calcula en el momento |
| Evolución de los riesgos (pedido del 31/10/2026) | ✅ | Dos acciones separadas: *Editar* corrige los datos del riesgo (un cambio de impacto o probabilidad corrige la evaluación vigente, sin agregar otra) y *Nueva evaluación* registra el impacto y la probabilidad en una fecha (editable, nunca futura) con un comentario; si ya hay una evaluación ese día, la corrige; la fecha de una evaluación registrada se puede corregir desde el historial; en Preferencias se elige si se permiten fechas anteriores a hoy en el alta, anteriores a una evaluación registrada (se intercalan y el estado actual sigue siendo el más reciente) y anteriores a hoy en la modificación; si se permiten, el editor solo advierte; se puede quitar una evaluación cargada por error. El editor muestra el historial con un gráfico del nivel en el tiempo; la matriz se puede ver a una fecha y dibujar el recorrido de un riesgo; el informe *Evolución de los riesgos* compara el inicio y el fin de un período (pantalla y PDF); Excel suma la hoja *Historial* y la ficha detallada en PDF incluye el historial |
| Película de la matriz (pedido del 31/10/2026) | ✅ | Informe interactivo en Informes: las "fotos" de la matriz entre dos fechas (la del comienzo y una por cada fecha con evaluaciones) se muestran una tras otra, cada una los segundos elegidos (0,5 a 60); reproducir/pausa, foto anterior/siguiente, primera/última, línea de tiempo y repetición; los riesgos que cambiaron en cada foto se resaltan y se explican al costado |
| Montos y porcentajes (pedido del 31/10/2026) | ✅ | Cada nivel de Impacto puede tener un rango de monto (desde–hasta, 2 decimales) y cada nivel de Probabilidad uno de 0 a 100 % (las escalas nuevas de probabilidad traen rangos parejos); cada riesgo y cada evaluación guarda el monto del impacto y la probabilidad en %; en Preferencias se elige, para cada uno, si se ingresa el nivel, el valor o cualquiera de los dos y si el nivel se ajusta automáticamente según el rango (se propone y se puede cambiar, con aviso si no coincide); Excel, CSV y la ficha PDF incluyen monto y porcentaje |
| Imagen de la matriz (pedido del 31/10/2026) | ✅ | Informe *Imagen de la matriz*: la matriz (a hoy o a una fecha) sobre fondo blanco, con cada parte opcional (título, proyecto y fecha, logo, niveles de impacto y probabilidad, nombres de los ejes, riesgos en las celdas, abreviaturas y leyenda), en 1280, 2560 o 3840 px de ancho; se guarda como PNG o se copia al portapapeles de Windows en formato PNG y mapa de bits (CF_DIB) para pegar en PowerPoint, Word u otros programas |
| Consecuencias, materialidad, grilla jerárquica (pedido del 31/10/2026) | ✅ | Cada riesgo tiene una lista de consecuencias potenciales y un criterio de materialidad, elegidos de combos editables (lo escrito que no existe se agrega al catálogo); los catálogos son de cada matriz, arrancan con una lista sugerida y se administran en Configuración → Consecuencias y criterios de materialidad (no se puede quitar una opción en uso). La lista de riesgos es jerárquica (cada riesgo despliega sus evaluaciones) y permite marcar riesgos y evaluaciones para eliminarlos juntos, con aviso de cuántas evaluaciones se borran con cada riesgo. En la matriz (y en su imagen) se puede mostrar el monto del impacto de cada riesgo; dentro de cada celda los riesgos van del mayor monto al menor, sin monto al final |
| Clonar riesgos y evaluaciones (pedido del 01/11/2026) | ✅ | Desde la grilla de riesgos (botón Clonar o menú contextual) y desde la matriz: clonar un riesgo abre «Nuevo riesgo» con todo copiado (título con «(copia)», descripciones, consecuencias, criterio, niveles, justificaciones, monto y %), que arranca con una sola evaluación; clonar una evaluación abre «Nueva evaluación» del mismo riesgo con sus valores ya cargados |
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
