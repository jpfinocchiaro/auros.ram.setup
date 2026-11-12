# Arma el instalador de Windows (MSI): publica el ejecutable autocontenido y lo empaqueta con WiX.
# Uso (desde la raíz del repositorio):  pwsh tools/build-installer.ps1
# Resultado: publish/installer/auros.ram-<versión>-win-x64.msi
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = ([xml](Get-Content 'Directory.Build.props')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "auros.ram $version"

# Compilación desde cero: una compilación incremental llegó a empaquetar el programa de la versión anterior.
# Se compila en una carpeta nueva y vacía, que se borra al terminar.
$env:AUROS_ARTIFACTS = Join-Path ([IO.Path]::GetTempPath()) "auros.ram-installer-$([guid]::NewGuid().ToString('N'))"
dotnet publish src/Auros.Ram.App -c Release -r win-x64 -p:RestoreForce=true -o publish/win-x64
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación.' }

# El ejecutable tiene que ser de esta versión y del commit actual.
$commit = (git rev-parse HEAD).Trim()
$built = (Get-Item publish/win-x64/auros.ram.exe).VersionInfo.ProductVersion
if ($built -ne "$version+$commit") { throw "El ejecutable es $built y se esperaba $version+$commit." }

dotnet build installer/Auros.Ram.Installer.wixproj -c Release -p:Version=$version -o publish/installer
if ($LASTEXITCODE -ne 0) { throw 'Falló el armado del instalador.' }

Get-ChildItem publish/installer -Filter *.msi | ForEach-Object { Write-Host "Instalador: $($_.FullName) ($([math]::Round($_.Length / 1MB, 1)) MB)" }
Remove-Item -Recurse -Force $env:AUROS_ARTIFACTS -ErrorAction SilentlyContinue
