# Formato de archivo `.rmx` / `.rmt` (contenedor v1, esquema v4)

Cada matriz (`.rmx`) y cada plantilla de escalas (`.rmt`) es un único archivo autocontenido,
independiente del sistema operativo (RNF-11, RNF-53). Implementación: `src/Auros.Ram.Persistence`.

```
┌───────────────────────────────┐  0
│ Cabecera en claro (148 bytes) │  firma, versiones, parámetros de derivación, sumas SHA-256
├───────────────────────────────┤ 148
│ Base SQLite cifrada (SQLCipher 4) │  páginas, índices y metadatos: todo cifrado
└───────────────────────────────┘
```

## Cabecera (little endian)

| Offset | Bytes | Campo |
|---:|---:|---|
| 0 | 8 | Firma ASCII `AUROSRAM` |
| 8 | 2 | Versión del contenedor (`1`) |
| 10 | 1 | Tipo: `1` matriz, `2` plantilla |
| 11 | 1 | Derivación de clave: `0` sin contraseña, `1` Argon2id, `2` PBKDF2-HMAC-SHA512 |
| 12 | 4 | Iteraciones (Argon2id: pasadas; PBKDF2: ≥ 256.000) |
| 16 | 4 | Memoria Argon2id en KiB |
| 20 | 4 | Paralelismo Argon2id |
| 24 | 32 | Sal aleatoria (por archivo; se renueva al cambiar la contraseña) |
| 56 | 4 | Versión del esquema de la base |
| 60 | 16 | Versión de la aplicación que escribió el archivo (ASCII, relleno con ceros) |
| 76 | 8 | Largo del contenido cifrado |
| 84 | 32 | SHA-256 del contenido cifrado |
| 116 | 32 | SHA-256 de los bytes 0–115 |

Ningún dato de negocio queda en la cabecera (RNF-13).

## Cifrado (RNF-31 a RNF-33)

- Clave de 256 bits derivada de la contraseña con **Argon2id** (por defecto 64 MiB, 3 pasadas,
  paralelismo 4) o PBKDF2-HMAC-SHA512. Los parámetros se leen de la cabecera, así que pueden
  endurecerse en versiones futuras sin romper archivos existentes.
- La clave se entrega a **SQLCipher 4** como clave en bruto (`PRAGMA key = "x'…'"`):
  AES-256-CBC con HMAC-SHA512 por página.
- La contraseña nunca se guarda. En memoria se conserva solo la clave derivada, que se borra al
  cerrar el archivo (`FileKey.Dispose`).

## Archivos sin contraseña (temporal)

Por ahora la contraseña es opcional. Si se deja en blanco, la cabecera lleva derivación `0`
(iteraciones, memoria y paralelismo en cero; la sal aleatoria se conserva para identificar el
archivo de recuperación) y la base se guarda como SQLite **sin cifrar**: no se pide contraseña al
abrir y el archivo no se bloquea por inactividad. *Cambiar contraseña* permite agregarle una a un
archivo existente o quitarla. Los archivos de trabajo y recuperación siguen el mismo criterio que
el archivo original.

## Apertura

1. Se verifica la firma y el SHA-256 de la cabecera → si falla: *archivo dañado*.
2. Si la versión del esquema es mayor que la vigente → *versión más nueva* (no se toca el archivo).
3. Se verifica el SHA-256 del contenido → si falla: *archivo dañado*.
4. Se deriva la clave y se abre la base en un archivo de trabajo (cifrado) en la carpeta de datos
   del usuario. Si SQLCipher no la reconoce → *contraseña incorrecta* (RNF-30).
5. `PRAGMA cipher_integrity_check` + `PRAGMA integrity_check` (RNF-26).
6. Si el esquema es anterior: copia del original como `nombre.vN.rmx` en la misma carpeta (RNF-27),
   migración y guardado en el formato vigente (RNF-14).

## Guardado (RNF-24)

La base se arma en un archivo de trabajo cifrado, se le antepone la cabecera y se escribe en un
temporal junto al original (`.nombre.rmx.<guid>.tmp`, con *write-through*). Luego un renombrado
reemplaza el original. En ningún momento el original queda a medio escribir.

## Esquema v4

| Tabla | Contenido |
|---|---|
| `meta` | clave/valor (`app_version`) |
| `project` | una fila: nombre, empresa, fecha, estado, logo de la empresa (BLOB) y las escalas de riesgo, impacto y probabilidad |
| `scale` | escalas (id, posición, nombre, tipo 1 Nivel de riesgo / 2 Impacto / 3 Probabilidad) |
| `level` | niveles (id, escala, nombre, color de fondo `#RRGGBB`, color de fuente `text_color` `#RRGGBB`, orden 0–100) |
| `heat_cell` | mapa de calor: nivel de riesgo elegido para una combinación de impacto y probabilidad (las que faltan usan el valor por omisión) |
| `risk` | riesgos (id, posición, título, descripciones, nivel de impacto y de probabilidad actuales, sus justificaciones, fechas UTC ISO-8601) |
| `risk_assessment` | historial de evaluaciones de cada riesgo (id, riesgo, fecha `yyyy-MM-dd`, nivel de impacto y de probabilidad, comentario, momento del registro UTC); la última es el estado actual |

El nivel de riesgo de cada riesgo no se guarda: se calcula con el mapa de calor.
`PRAGMA user_version` guarda la versión del esquema.

### Historial

| Versión | Cambio |
|---|---|
| 1, 2 | Versiones de prueba con tres dimensiones (riesgo, valor agregado, dificultad). **No se migran**: al abrirlas, la aplicación avisa que son de una versión de prueba y no toca el archivo (`Schema.OldestSupportedVersion`) |
| 3 | Impacto y probabilidad de ocurrencia; nivel de riesgo por mapa de calor; una empresa por matriz; color de fuente por nivel |
| 4 | Historial de evaluaciones (`risk_assessment`). Al migrar, cada riesgo empieza su historial con su estado actual fechado el día en que se creó |

## Cómo agregar una versión del esquema (RNF-69)

1. Agregar un paso nuevo **al final** de `Schema.Steps` (nunca editar uno publicado), idempotente.
2. Ajustar `DocumentStore` para leer y escribir las columnas nuevas.
3. Generar el archivo de prueba de la versión nueva (ver `tests/Auros.Ram.Tests/FixtureGenerator.cs`)
   y conservar los anteriores en `tests/Auros.Ram.Tests/Fixtures` (RNF-68). Los de las versiones de
   prueba están en `Fixtures/trial` y solo se usan para comprobar que se rechazan.

## Archivos auxiliares (RNF-25, RNF-37)

| Archivo | Ubicación | Cifrado |
|---|---|---|
| Trabajo (`*.db`) | `%LOCALAPPDATA%/auros.ram/work` (se limpia al iniciar) | SQLCipher, misma clave |
| Recuperación (`<hash>.rmx`) | `%LOCALAPPDATA%/auros.ram/recovery` | formato `.rmx`, misma clave |
| Temporal de guardado | junto al original | formato `.rmx`, misma clave |

Nunca se escribe texto plano al disco, salvo en las exportaciones que el usuario pide en forma
explícita (Excel, PDF, CSV, JSON), y siempre con una advertencia previa (RNF-38).
