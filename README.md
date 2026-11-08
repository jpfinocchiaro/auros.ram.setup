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
`Program Files\auros.ram`, crea accesos directos en el menú Inicio y el escritorio, y asocia `.rmx` y `.rmt` para
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

## Versión gratuita y licencias

Sin licencia, auros.ram funciona como **versión gratuita**: cada matriz admite 1 empresa y 5 riesgos. Una matriz que ya
los supera (hecha con la versión completa) se abre solo para consultar y exportar. Con una licencia no hay límites.

La licencia está atada al equipo: en **Ayuda → Licencia** el cliente ve el *código de este equipo* y lo envía; con ese
código se genera su clave, que solo funciona en ese equipo (si cambia de equipo o reinstala el sistema, necesita otra).
La clave va firmada (ECDSA P-256) y la aplicación la verifica sin internet.

Para emitir una licencia (cada licencia es una venta):

```
dotnet run --project tools/Auros.Ram.Licencias -- emitir "<código de equipo>" "<cliente>" --email <correo> --monto 120 --pago "Mercado Pago"
```

`--email`, `--monto`, `--moneda` (USD si no se indica), `--pago` y `--nota` son opcionales. Si la venta tiene email, la
licencia **se le envía al cliente** con las instrucciones para activarla, en el idioma de `--idioma` (`es`, `en`, `pt` o
`fr`; si no se indica, `es`). Con `--no-enviar` se anota la venta sin enviar nada. Si el envío falla, la venta queda
anotada igual y se puede volver a enviar:

```
dotnet run --project tools/Auros.Ram.Licencias -- reenviar "<código de equipo>" [--email <otro correo>] [--idioma en]
```

Para ver las ventas, con totales por moneda y por mes:

```
dotnet run --project tools/Auros.Ram.Licencias -- ventas
```

**Cuenta de correo para los envíos.** La primera vez que se intenta enviar, la herramienta crea `correo.json` en la
carpeta de las licencias; hay que completarlo:

```json
{
  "servidor": "smtp.gmail.com",
  "puerto": 587,
  "usuario": "tu-cuenta@gmail.com",
  "clave": "<contraseña de aplicación>",
  "remitente": "auros.ram <tu-cuenta@gmail.com>",
  "copia": "tu-cuenta@gmail.com"
}
```

Con Gmail, la clave no es la contraseña de la cuenta sino una *contraseña de aplicación* (Cuenta de Google → Seguridad →
Verificación en dos pasos → Contraseñas de aplicaciones). `copia` recibe una copia oculta de cada envío; puede quedar
vacío. Si prefieres no guardar la clave en el archivo, déjala vacía y define la variable de entorno `AUROS_SMTP_CLAVE`.
Sirve cualquier servidor SMTP con STARTTLS (puerto 587); el puerto 465 no está soportado.

La clave privada está en `%USERPROFILE%\auros.ram-licencias\clave-privada.pem`, y cada venta se anota en `ventas.csv`, en
la misma carpeta (separado por «;», se abre con Excel): fecha, cliente, email, equipo, monto, moneda, medio de pago, nota
y la clave entregada. **Hay que guardar una copia de seguridad de esa carpeta**: sin la clave privada no se pueden emitir
más licencias, y quien la tenga puede emitirlas. Ni la clave privada ni `correo.json` van al repositorio. La herramienta no se distribuye con la
aplicación.

## Landing page (site/)

La página para descargar y comprar auros.ram está en `site/` (HTML, CSS y JS, sin dependencias). Se publica sola en
GitHub Pages con `.github/workflows/pages.yml` al cambiar algo en `site/`; antes hay que elegir una sola vez
**Settings → Pages → Source: GitHub Actions** en el repositorio.

- Precio, link de pago y email de ventas: al principio de `site/app.js` (`CONFIG`). Mientras estén vacíos, la página
  muestra «Precio a confirmar» y los botones de pago y de envío del código quedan deshabilitados.
- WhatsApp: con un número en `whatsapp` (código de país y número, sin «+» ni espacios, por ejemplo `5491112345678`)
  aparece un botón flotante «Habla con un consultor» y un enlace en el pie. Abre el chat con un mensaje inicial en el
  idioma de la página. Sin número, no se muestra.
- Descargas: apuntan a la última versión publicada en GitHub Releases, con los nombres
  `auros.ram-<versión>-win-x64.msi`, `auros.ram-<versión>-macos-arm64.dmg` y `auros.ram-<versión>-macos-x64.dmg`.
  Al publicar una versión nueva, actualizar `version` en `CONFIG`.
- Idiomas: español, inglés, portugués y francés, con su bandera (España, Estados Unidos, Brasil y Francia, como en la
  aplicación). Los textos están en `site/i18n.js`. La página elige el idioma del navegador; también se puede forzar con
  `?lang=en`, `?lang=pt` o `?lang=fr`.
- Para verla en el equipo: `python -m http.server 8765 --directory site` y abrir http://localhost:8765.

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
