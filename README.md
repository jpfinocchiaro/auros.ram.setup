# auros.ram — Risk Assessment Matrix

Aplicación de escritorio (Windows, macOS, Linux) para crear y administrar matrices de riesgo.
Cada matriz es un archivo `.rmx` cifrado con su propia contraseña.

Especificación: *Especificación de requerimientos — auros.ram* (códigos RF-xx / RNF-xx, citados en el código).

## Estructura

| Proyecto | Rol |
|---|---|
| `src/Auros.Ram.Core` | Dominio sin UI (RNF-07): escalas, niveles, riesgos, validaciones, matriz de priorización, informes, deshacer/rehacer, JSON |
| `src/Auros.Ram.Persistence` | Único acceso a disco y a criptografía (RNF-08): formato `.rmx`, SQLCipher, Argon2id, guardado atómico, recuperación, plantillas |
| `src/Auros.Ram.Export` | Excel (ClosedXML), PDF (PDFsharp/MigraDoc) y CSV |
| `src/Auros.Ram.App` | Interfaz Avalonia UI, patrón MVVM (CommunityToolkit.Mvvm) |
| `tests/Auros.Ram.Tests` | Dominio, formato de archivo (ida y vuelta, corrupción, contraseña, rendimiento) y exportaciones |
| `tests/Auros.Ram.App.Tests` | Pruebas de interfaz sin pantalla (Avalonia Headless) |

## Requisitos para compilar

- .NET SDK 10 (LTS). No hace falta nada más: SQLCipher viene incluido en el paquete NuGet.

## Compilar, probar y ejecutar

```bash
dotnet build Auros.Ram.slnx
```

```bash
dotnet test Auros.Ram.slnx
```

```bash
dotnet run --project src/Auros.Ram.App
```

Como el repositorio está dentro de Dropbox, las salidas de compilación (`bin`/`obj`) se mandan a
`%LOCALAPPDATA%\auros.ram-build` para que el sincronizador no bloquee archivos
(ver `Directory.Build.props`; se puede cambiar con la variable `AUROS_ARTIFACTS`).

## Publicar (RNF-10)

Ejecutable autocontenido de un solo archivo, sin requerir .NET instalado:

```bash
dotnet publish src/Auros.Ram.App -c Release -r win-x64 -o publish/win-x64
```

Otros identificadores: `osx-arm64`, `osx-x64`, `linux-x64`.

## Textos e idiomas (RNF-42)

Los textos están en archivos `.resx` (`Resources/Strings.resx`, `Messages.resx`, …). Se generan desde
los `*.es.txt` vecinos (formato `Clave=Valor`):

```bash
python tools/mkresx.py src/Auros.Ram.App/Resources/Strings.es.txt src/Auros.Ram.App/Resources/Strings.resx
```

Para inglés o portugués, agregar `Strings.en.resx` / `Strings.pt.resx` con las mismas claves.

## Documentación

- [Formato de archivo `.rmx`](docs/formato-archivo.md)
- [Exportación JSON](docs/formato-json.md)
- [Estado de cumplimiento de requerimientos](docs/estado-requerimientos.md)
- Licencias de terceros: `THIRD-PARTY-NOTICES.txt` (también en Ayuda → Acerca de)
