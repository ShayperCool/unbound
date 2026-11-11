#!/usr/bin/env python3
"""Copy the probe's two uncommitted fonts into a project copy before an engine run.

    fonts.py PROJECT REFERENCE

Liberation Sans comes from the OS and Inter from the Unity Editor's resources (never executed),
as `ProbeSetup.Create` copies them for the Editor. Each file must have the SHA-256 the reference
records for it; a missing or different font fails here, with its path, instead of leaving the
engine to skip every case of that font. `TEXTCORE_LIBERATION_SANS` and `TEXTCORE_INTER` name the
files on a machine that keeps them elsewhere.
"""
import gzip
import hashlib
import json
import os
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import probe_paths  # noqa: E402


def editor_resources():
    """The Editor's Resources folder: beside `Data` on Linux, inside the bundle on macOS."""
    editor = probe_paths.editor_path()
    for root in (editor.parent/'Data', *(p/'Contents' for p in editor.parents if p.suffix == '.app')):
        if (root/'Resources').is_dir():
            return root/'Resources'
    return editor.parent/'Data/Resources'


def sources():
    liberation = os.environ.get('TEXTCORE_LIBERATION_SANS',
                                '/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf')
    inter = os.environ.get('TEXTCORE_INTER', str(editor_resources()/'Fonts/Inter-Regular.ttf'))
    return {'LiberationSans-Regular': Path(liberation), 'Inter-Regular': Path(inter)}


def recorded_hashes(reference):
    """Font name -> SHA-256 from the reference's `font` records."""
    opener = gzip.open if str(reference).endswith('.gz') else open
    hashes = {}
    with opener(reference, 'rt', encoding='utf-8') as lines:
        for line in lines:
            if '"type":"font"' not in line:
                continue
            record = json.loads(line)
            hashes[record['name']] = record['sha256']
    return hashes


def install(project, reference):
    """Copy both fonts into PROJECT/Assets/Fonts; return a list of error messages."""
    hashes = recorded_hashes(reference)
    folder = Path(project)/'Assets/Fonts'
    errors = []
    for name, source in sources().items():
        if name not in hashes:
            errors.append(f'{reference} has no font record named {name}')
            continue
        if not (folder/f'{name}.ttf.meta').is_file():
            errors.append(f'{folder} has no {name}.ttf.meta: not the textcore project')
            continue
        if not source.is_file():
            errors.append(f'{name}: {source} does not exist')
            continue
        digest = hashlib.sha256(source.read_bytes()).hexdigest()
        if digest != hashes[name]:
            errors.append(f'{name}: {source} has SHA-256 {digest}, the reference was recorded with {hashes[name]}')
            continue
        target = folder/f'{name}.ttf'
        if not target.is_file() or hashlib.sha256(target.read_bytes()).hexdigest() != digest:
            shutil.copyfile(source, target)
    return errors


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    errors = install(sys.argv[1], sys.argv[2])
    for error in errors:
        print(f'textcore fonts: {error}', file=sys.stderr)
    if errors:
        sys.exit(1)
    print('textcore fonts: Liberation Sans and Inter match the reference')


if __name__ == '__main__':
    main()
