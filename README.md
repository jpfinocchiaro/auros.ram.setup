# auros.ram — Risk Assessment Matrix

Aplicación de escritorio (Windows, macOS, Linux) para crear y administrar matrices de riesgo.
Cada matriz es un archivo `.rmx`, cifrado con su propia contraseña (por ahora opcional: sin contraseña no se cifra).

Especificación: *Especificación de requerimientos — auros.ram* (códigos RF-xx / RNF-xx, citados en el código).

## Estructura

| Proyecto | Rol |
|---|---|
| `src/Auros.Ram.Core` | Dominio sin UI (RNF-07): escalas, niveles, riesgos (impacto × probabilidad), mapa de calor, validaciones, matriz de riesgo, informes, deshacer/rehacer, JSON |
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

## Instalador de Windows (RNF-49, RNF-50)

MSI armado con WiX 5 (se descarga como paquete NuGet; no hace falta instalar nada):

```bash
pwsh tools/build-installer.ps1
```

Publica el ejecutable y deja `publish/installer/auros.ram-<versión>-win-x64.msi`. Instala por máquina en
`Program Filesuros.ram`, crea accesos directos en el menú Inicio y el escritorio, y asocia `.rmx` y `.rmt` para
abrirlos con doble clic. Una versión nueva reemplaza a la anterior (el `UpgradeCode` de `installer/Package.wxs` no
se cambia nunca). Al desinstalar no se tocan las matrices ni las preferencias. El MSI no está firmado: hasta tener
un certificado de firma de código, Windows SmartScreen avisa al abrirlo.

## Paquete de macOS (RNF-49)

Se arma desde Windows (requiere Python con Pillow, para el ícono):

```bash
pwsh tools/build-macos.ps1
```

Deja `publish/macos/auros.ram-<versión>-macos-arm64.zip` (Apple Silicon) y `...-macos-x64.zip` (Intel), cada uno con
`auros.ram.app`: el ejecutable autocontenido, el ícono y la asociación de `.rmx` y `.rmt` (`installer/macos/Info.plist`).
El paquete sale sin firmar y en Apple Silicon una aplicación sin firma no abre, así que se termina en una Mac:

```bash
bash tools/macos/finish-on-mac.sh auros.ram-2.3.0-macos-arm64.zip
```

Firma la aplicación y arma el DMG (con el acceso a Aplicaciones para arrastrarla). Sin cuenta de Apple la firma es
local: en otras Mac, Gatekeeper avisa que el desarrollador no está identificado (se abre desde Ajustes del Sistema →
Privacidad y seguridad → «Abrir igualmente»). Con un certificado *Developer ID Application* y un perfil de
`notarytool`, el mismo script firma con runtime endurecido (`installer/macos/auros.ram.entitlements`) y notariza:

```bash
bash tools/macos/finish-on-mac.sh auros.ram-2.3.0-macos-arm64.zip "Developer ID Application: Auros (TEAMID)" perfil-notario
```

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
