#!/usr/bin/env python3
"""Record and compare legacy dynamic glyph metrics and alpha pixels."""
from pathlib import Path
import shutil
import subprocess
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe

# OS faces the reference was recorded with (Debian/Ubuntu packages fonts-freefont-ttf,
# fonts-dejavu-core, fonts-liberation, fonts-noto-cjk): the built-in font, fallback and styled
# requests draw from them, so a host without them cannot reproduce the reference.
REQUIRED_FACES = {
    'FreeSans': ['Regular', 'Bold', 'Oblique', 'Bold Oblique'],
    'DejaVu Sans': ['Book', 'Bold'],
    'Liberation Sans': ['Regular', 'Bold', 'Italic', 'Bold Italic'],
    'Liberation Serif': ['Regular', 'Bold', 'Italic', 'Bold Italic'],
    'Noto Sans CJK JP': ['Regular', 'Bold'],
}


def missing_faces():
    if shutil.which('fc-list') is None:
        return ['fc-list (fontconfig) is not installed']
    listed = subprocess.run(['fc-list', '--format', '%{family}\t%{style}\n'], capture_output=True,
                            text=True, check=True).stdout
    present = set()
    for line in listed.splitlines():
        families, _, styles = line.partition('\t')
        for family in families.split(','):
            for style in styles.split(','):
                present.add((family, style))
    return [f'{family} {style}' for family, styles in REQUIRED_FACES.items()
            for style in styles if (family, style) not in present]


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] in ('record', 'gate'):
        missing = missing_faces()
        if missing:
            sys.exit('glyphraster: the reference needs these installed OS fonts: ' + ', '.join(missing))
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'GLYPH_RASTER_OUT',
                              tolerance=0, engine_args=['--frames', '600', '--size', '32x32']))
