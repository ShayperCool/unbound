#!/usr/bin/env python3
"""The Editor DCC model formats Unbound implements; see README.md."""
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402

# The Blender that converted the Reference's file (the values are its FBX exporter's).
REFERENCE_BLENDER = '5.2.2'


def blender_version():
    """The version of the Blender Unbound converts with (`blender --version`), or None."""
    app = Path('/Applications/Blender.app/Contents/MacOS/Blender')
    blender = str(app) if sys.platform == 'darwin' and app.is_file() else shutil.which('blender')
    if blender is None:
        return None
    try:
        out = subprocess.run([blender, '--version'], capture_output=True, text=True, timeout=120).stdout
    except (OSError, subprocess.SubprocessError):
        return None
    words = out.split()
    return words[1] if len(words) > 1 and words[0] == 'Blender' else None


if __name__ == '__main__':
    if 'gate' in sys.argv[1:]:
        found = blender_version()
        print(f'dccmodels: converting with Blender {found or "(none found)"}; the Reference was converted by Blender {REFERENCE_BLENDER}')
        if found != REFERENCE_BLENDER:
            print('dccmodels: WARNING: another Blender converts here: differences may be its FBX exporter\'s, '
                  'not Unbound\'s (record a reference with this Blender to tell)')
    sys.exit(value_probe.main(Path(__file__).resolve().parent, 'ProbeRunner.Record', 'DCCMODELS_OUT', tolerance=1e-4,
                              engine_args=['--frames', '10', '--size', '96x64'], macos_batch=True))
