# Arma el instalador de Windows (MSI): publica el ejecutable autocontenido y lo empaqueta con WiX.
# Uso (desde la raíz del repositorio):  pwsh tools/build-installer.ps1
# Resultado: publish/installer/auros.ram-<versión>-win-x64.msi
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "auros.ram $version"

dotnet publish src/Auros.Ram.App -c Release -r win-x64 -p:RestoreForce=true -o publish/win-x64
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación.' }

dotnet build installer/Auros.Ram.Installer.wixproj -c Release -p:Version=$version -o publish/installer
if ($LASTEXITCODE -ne 0) { throw 'Falló el armado del instalador.' }

Get-ChildItem publish/installer -Filter *.msi | ForEach-Object { Write-Host "Instalador: $($_.FullName) ($([math]::Round($_.Length / 1MB, 1)) MB)" }
