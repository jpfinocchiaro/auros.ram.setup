"""Genera un .resx a partir de un archivo de texto Clave=Valor (\\n = salto de línea).
Uso: python mkresx.py entrada.txt salida.resx"""
import sys
from xml.sax.saxutils import escape

HEADER = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
'''

src, dst = sys.argv[1], sys.argv[2]
seen = {}
for raw in open(src, encoding='utf-8'):
    line = raw.rstrip('\n')
    if not line.strip() or line.lstrip().startswith('#'):
        continue
    key, _, value = line.partition('=')
    key = key.strip()
    if key in seen:
        sys.exit(f'clave duplicada: {key}')
    seen[key] = value.replace('\\n', '\n')

out = [HEADER]
for k, v in seen.items():
    out.append(f'  <data name="{escape(k)}" xml:space="preserve"><value>{escape(v)}</value></data>\n')
out.append('</root>\n')
open(dst, 'w', encoding='utf-8').write(''.join(out))
print(f'{len(seen)} claves -> {dst}')
