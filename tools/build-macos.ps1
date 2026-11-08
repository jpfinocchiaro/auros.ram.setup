# Arma el paquete de macOS (auros.ram.app) para Apple Silicon (arm64) e Intel (x64), desde Windows.
# Uso (desde la raíz del repositorio):  pwsh tools/build-macos.ps1
# Resultado: publish/macos/auros.ram-<versión>-macos-arm64.zip y ...-macos-x64.zip
# El paquete queda sin firmar: se termina en una Mac con tools/macos/finish-on-mac.sh (firma y DMG).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "auros.ram $version"

foreach ($rid in 'osx-arm64', 'osx-x64') {
    dotnet publish src/Auros.Ram.App -c Release -r $rid -p:RestoreForce=true -o "publish/macos/$rid/bin"
    if ($LASTEXITCODE -ne 0) { throw "Falló la publicación para $rid." }
    python tools/package_macos.py $rid $version
    if ($LASTEXITCODE -ne 0) { throw "Falló el armado del paquete para $rid." }
}
