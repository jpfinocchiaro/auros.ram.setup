# Formato de archivo `.rmx` / `.rmt` (contenedor v1, esquema v1)

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
| 11 | 1 | Derivación de clave: `1` Argon2id, `2` PBKDF2-HMAC-SHA512 |
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

## Esquema v1

| Tabla | Contenido |
|---|---|
| `meta` | clave/valor (`app_version`) |
| `project` | una fila: datos del proyecto, logos (BLOB) y las tres escalas asignadas |
| `scale` | escalas (id, posición, nombre, tipo 1 Riesgo / 2 Valor / 3 Dificultad) |
| `level` | niveles (id, escala, nombre, color `#RRGGBB`, orden 0–100) |
| `risk` | riesgos (id, posición, título, descripciones, los tres niveles, justificaciones, fechas UTC ISO-8601) |

`PRAGMA user_version` guarda la versión del esquema.

## Cómo agregar una versión del esquema (RNF-69)

1. Agregar un paso nuevo **al final** de `Schema.Steps` (nunca editar uno publicado), idempotente.
2. Ajustar `DocumentStore` para leer y escribir las columnas nuevas.
3. Generar el archivo de prueba de la versión nueva (ver `tests/Auros.Ram.Tests/FixtureGenerator.cs`)
   y conservar los anteriores en `tests/Auros.Ram.Tests/Fixtures` (RNF-68).

## Archivos auxiliares (RNF-25, RNF-37)

| Archivo | Ubicación | Cifrado |
|---|---|---|
| Trabajo (`*.db`) | `%LOCALAPPDATA%/auros.ram/work` (se limpia al iniciar) | SQLCipher, misma clave |
| Recuperación (`<hash>.rmx`) | `%LOCALAPPDATA%/auros.ram/recovery` | formato `.rmx`, misma clave |
| Temporal de guardado | junto al original | formato `.rmx`, misma clave |

Nunca se escribe texto plano al disco, salvo en las exportaciones que el usuario pide en forma
explícita (Excel, PDF, CSV, JSON), y siempre con una advertencia previa (RNF-38).
