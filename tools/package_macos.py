"""Arma auros.ram.app a partir del ejecutable publicado para macOS y lo comprime en un .zip.

Uso: python tools/package_macos.py <rid> <versión>      (rid: osx-arm64 u osx-x64)
Lee publish/macos/<rid>/bin/auros.ram y deja publish/macos/auros.ram-<versión>-macos-<arm64|x64>.zip.

El .zip guarda el permiso de ejecución del binario (un .zip armado con las herramientas de Windows lo pierde y la
aplicación no abre). El paquete queda sin firmar: hay que terminarlo en una Mac con tools/macos/finish-on-mac.sh.
"""
import shutil
import sys
import zipfile
from pathlib import Path

from PIL import Image

root = Path(__file__).resolve().parent.parent
rid, version = sys.argv[1], sys.argv[2]
arch = rid.split('-', 1)[1]
out = root / 'publish' / 'macos'
binary = out / rid / 'bin' / 'auros.ram'
if not binary.exists():
    sys.exit(f'falta el ejecutable publicado: {binary}')

app = out / rid / 'auros.ram.app'
shutil.rmtree(app, ignore_errors=True)
(app / 'Contents' / 'MacOS').mkdir(parents=True)
(app / 'Contents' / 'Resources').mkdir(parents=True)

shutil.copy2(binary, app / 'Contents' / 'MacOS' / 'auros.ram')
plist = (root / 'installer' / 'macos' / 'Info.plist').read_text(encoding='utf-8').replace('$(Version)', version)
(app / 'Contents' / 'Info.plist').write_text(plist, encoding='utf-8')
(app / 'Contents' / 'PkgInfo').write_text('APPL????', encoding='ascii')

# Ícono de macOS (.icns) desde el ícono de Windows (trae hasta 256 px).
icon = Image.open(root / 'src' / 'Auros.Ram.App' / 'Assets' / 'Brand' / 'auros_ram.ico')
icon.size = max(icon.ico.sizes())
icon.load()
icon.convert('RGBA').save(app / 'Contents' / 'Resources' / 'auros.ram.icns')

target = out / f'auros.ram-{version}-macos-{arch}.zip'
target.unlink(missing_ok=True)
with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as z:
    for path in sorted(app.rglob('*')):
        name = str(Path('auros.ram.app') / path.relative_to(app)).replace('\\', '/')
        info = zipfile.ZipInfo(name + ('/' if path.is_dir() else ''))
        info.create_system = 3   # Unix: así se respetan los permisos
        executable = path.is_dir() or path.parent.name == 'MacOS'
        info.external_attr = ((0o40755 if path.is_dir() else (0o100755 if executable else 0o100644)) << 16)
        if path.is_dir():
            z.writestr(info, '')
        else:
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, path.read_bytes())

print(f'{target} ({target.stat().st_size / 1024 / 1024:.1f} MB)')
