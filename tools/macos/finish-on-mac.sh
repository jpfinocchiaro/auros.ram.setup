#!/bin/bash
# Termina el paquete de macOS en una Mac: firma auros.ram.app y arma el DMG para distribuir.
#
# Uso:
#   bash finish-on-mac.sh auros.ram-2.3.0-macos-arm64.zip                     (firma local, sin cuenta de Apple)
#   bash finish-on-mac.sh auros.ram-2.3.0-macos-arm64.zip "Developer ID Application: Auros (TEAMID)" perfil-notario
#
# Sin identidad, la firma es "ad hoc": alcanza para usarla en esa Mac (en Apple Silicon una aplicación sin firma
# no abre), pero en otra Mac Gatekeeper avisa que el desarrollador no está identificado (se abre desde
# Ajustes del Sistema → Privacidad y seguridad → "Abrir igualmente").
# Con un certificado "Developer ID Application" y un perfil de notarización (xcrun notarytool store-credentials),
# el DMG queda firmado y notarizado y abre sin avisos en cualquier Mac.
set -euo pipefail

zip="${1:?Indicar el .zip armado por tools/build-macos.ps1}"
identity="${2:--}"
profile="${3:-}"
here="$(cd "$(dirname "$0")" && pwd)"
entitlements="$here/auros.ram.entitlements"
name="$(basename "$zip" .zip)"
work="$(mktemp -d)"

ditto -x -k "$zip" "$work"
app="$work/auros.ram.app"
xattr -cr "$app"

if [ "$identity" = "-" ]; then
    codesign --force --deep --sign - "$app"
else
    codesign --force --deep --options runtime --timestamp --entitlements "$entitlements" --sign "$identity" "$app"
fi
codesign --verify --deep --strict "$app"

# DMG con la aplicación y un acceso a Aplicaciones para arrastrarla.
stage="$work/dmg"
mkdir "$stage"
cp -R "$app" "$stage/"
ln -s /Applications "$stage/Aplicaciones"
dmg="$(pwd)/$name.dmg"
rm -f "$dmg"
hdiutil create -volname "auros.ram" -srcfolder "$stage" -ov -format UDZO "$dmg"

if [ "$identity" != "-" ]; then
    codesign --sign "$identity" --timestamp "$dmg"
    if [ -n "$profile" ]; then
        xcrun notarytool submit "$dmg" --keychain-profile "$profile" --wait
        xcrun stapler staple "$dmg"
    fi
fi

rm -rf "$work"
echo "Listo: $dmg"
