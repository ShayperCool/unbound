#!/usr/bin/env python3
"""The environment lighting a bake makes: the ambient probe and the default reflection.

    environment_bake.py create [--scene A,B]   # makes Assets/Scenes (the Editor, batch mode)
    environment_bake.py native --label L       # an Editor capture (the GPU Editor runner)
    environment_bake.py publish L1 L2          # two identical captures -> Reference/unity-<version>
    environment_bake.py engine --label L       # Unbound's capture (`unbound test`, Edit mode)
    environment_bake.py compare L              # Unbound's capture against the reference
    environment_bake.py check                  # engine + compare

`Assets/Editor/EnvironmentRecord.cs` opens each scene, bakes it with Lightmapping.Bake and records
the ambient probe, the default reflection (every texel of every level) and what the bake wrote
(see README.md).
"""
import argparse
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import zlib

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
sys.dont_write_bytecode = True  # nothing is written into the checkout
import editor_runner  # noqa: E402
from probe_paths import private_label, project_copy, reference_root, run_root  # noqa: E402
from baked_files import keep_written, written_files  # noqa: E402

WORK = run_root(PROBE, 'bakeenvironment')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
# The ambient probe's coefficients, within this times the probe's largest: the sky is drawn by
# the graphics device and projected in 32-bit floats (the worst seen is 4.5e-4, in each sky's
# scene alike: the intensity scales the probe).
AMBIENT_TOLERANCE = 1e-3
# A texel of the default reflection, within this times its level's largest value (as
# tests/projects/reflectionprobes compares imported glossy convolution, there within 0.5%): the
# first level is the sky the graphics device draws, as half floats (the worst seen is one half
# float step); the others its convolution in 32-bit floats, as half floats again (the worst seen
# is 1.46e-3, one level of each sky's scene).
CUBE_TOLERANCE = 2e-3


def scenes():
    return sorted(p.stem for p in (PROBE/'Assets/Scenes').glob('*.unity'))


def editor(out, method, env, label, graphics=True):
    job = editor_runner.EditorJob(out/'project', out/'editor.log', out, ['-executeMethod', method],
                                  batch=True, graphics=graphics, env=env, timeout=1800,
                                  label='bakeenvironment-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return int(result.get('exit_code') != 0 or bool(result.get('forced_exit')))


def create(names=None):
    """Makes all the scenes, or adds those named (comma-separated); nothing that exists is replaced."""
    target = PROBE/'Assets/Scenes'
    names = names.split(',') if names else None
    if names is None and any(target.glob('*.unity')):
        sys.exit(f'refusing to overwrite {target}')
    for name in names or []:
        if (target/(name + '.unity')).exists():
            sys.exit(f'refusing to overwrite {name}')
    out = WORK/private_label('create')
    project_copy(PROBE, out/'project', package_cache=False)
    env = {'ENVIRONMENT_SCENES': ','.join(names)} if names else {}
    # (On the graphics device: a scene saved with a bake's lighting data is baked there.)
    if editor(out, 'EnvironmentSetup.Create', env, 'create'):
        return 1
    made = out/'project/Assets'
    for path in sorted((made/'Scenes').iterdir()):
        if (names is None or any(path.name.startswith(name) for name in names)) and not (target/path.name).exists():
            (shutil.copytree if path.is_dir() else shutil.copy2)(path, target/path.name)
    for meta in ('Scenes.meta', 'Editor.meta'):
        if not (PROBE/'Assets'/meta).exists():
            shutil.copy2(made/meta, PROBE/'Assets'/meta)
    return 0


def keep(raw, target):
    """A capture's files as the reference keeps them: the record, the import settings (but for
    the asset's GUID, new with every bake), and the cube's texels compressed."""
    for scene in scenes():
        (target/scene).mkdir(parents=True)
        if (raw/scene/'record.txt').is_file():
            shutil.copy2(raw/scene/'record.txt', target/scene/'record.txt')
        if (raw/scene/'import.txt').is_file():
            lines = (raw/scene/'import.txt').read_text().splitlines(keepends=True)
            (target/scene/'import.txt').write_text(''.join('guid: (new)\n' if line.startswith('guid: ') else line for line in lines))
        if (raw/scene/'cube.f32').is_file():
            (target/scene/'cube.f32.z').write_bytes(zlib.compress((raw/scene/'cube.f32').read_bytes(), 9))
        keep_written(raw/scene/'written', target/scene/'written')


def native(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project_copy(PROBE, out/'project', package_cache=False)
    (out/'raw').mkdir()
    code = editor(out, 'EnvironmentRecord.Record', {'ENVIRONMENT_OUT': str(out/'raw')}, label)
    if code or not (out/'raw/log.txt').is_file():
        return code or 1
    keep(out/'raw', out/'reference')
    return 0


def tree(root):
    return {str(p.relative_to(root)): p.read_bytes() for p in sorted(root.rglob('*')) if p.is_file()}


def publish(labels):
    if len(labels) < 2:
        sys.exit('publishing needs two independent repeats')
    captures = [tree(WORK/label/'reference') for label in labels]
    differing = [name for capture in captures[1:] for name in sorted(captures[0].keys() | capture.keys())
                 if captures[0].get(name) != capture.get(name)]
    if not captures[0] or differing:
        sys.exit(f'the repeats differ in {differing}; not publishing')
    target = reference_root(PROBE)
    present = tree(target) if target.exists() else {}
    changed = [name for name, data in present.items() if captures[0].get(name) != data]
    if changed:
        sys.exit(f'the captures differ from the reference in {changed}; not publishing')
    for name, data in captures[0].items():
        if name not in present:
            (target/name).parent.mkdir(parents=True, exist_ok=True)
            (target/name).write_bytes(data)
    return 0


def engine(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project = project_copy(PROBE, out/'project')
    (out/'raw').mkdir()
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, ENVIRONMENT_OUT=str(out/'raw'), ENVIRONMENT_AUTO='1')
    # With a graphics device: the bake draws the skybox.
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '900', cli, 'test', '-p', str(project),
               '--platform', 'EditMode', '--graphics']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print('engine exit', code, 'log', out/'engine.log')
    if code or not (out/'raw/log.txt').is_file():
        return code or 1
    keep(out/'raw', out/'reference')
    return 0


def read(folder):
    """A scene's records: {key: text}, the import settings and the cube's texels (or None)."""
    records = {}
    if (folder/'record.txt').is_file():
        for line in (folder/'record.txt').read_text().splitlines():
            key, _, value = line.partition(': ')
            records[key] = value
    records['import'] = (folder/'import.txt').read_text() if (folder/'import.txt').is_file() else 'none'
    cube = folder/'cube.f32.z'
    texels = None
    if cube.is_file():
        data = zlib.decompress(cube.read_bytes())
        texels = struct.unpack(f'<{len(data) // 4}f', data)
    return records, texels


def within(got, want, allowed, unit):
    """How many values differ beyond `allowed(reference)`, and the worst in `unit`s."""
    if len(got) != len(want):
        return f'{len(got)} values, expected {len(want)}'
    # (Nothing allowed: equal values, as for a probe of zeros.)
    errors = [abs(a - b) / allowed(b) if allowed(b) > 0 else 0.0 if a == b else float('inf') for a, b in zip(got, want)]
    off = [k for k, e in enumerate(errors) if not e <= 1.0]
    worst = max(errors)
    return f'{len(off)} of {len(want)} values differ (worst {worst:.3g} {unit} at {errors.index(worst)})' if off else None


def cube_difference(got, want, size):
    """Each level's texels within CUBE_TOLERANCE times the level's largest value."""
    if len(got) != len(want):
        return f'{len(got)} values, expected {len(want)}'
    problems, at, level = [], 0, 0
    while at < len(want):
        side = max(size >> level, 1)
        count = 6 * side * side * 4
        theirs = want[at:at + count]
        # (Of the colours: alpha is 1.)
        allowed = CUBE_TOLERANCE * max(abs(v) for k, v in enumerate(theirs) if k % 4 != 3)
        problem = within(got[at:at + count], theirs, lambda _: allowed, f'x {CUBE_TOLERANCE:g} of the largest')
        if problem:
            problems.append(f'level {level}: {problem}')
        at += count
        level += 1
    return '; '.join(problems) or None


def exr_header(data):
    """An OpenEXR file's magic, version and attributes (up to the offset table): the texels after
    them are compared as imported (the scene's `cube`)."""
    at = 8
    while data[at] != 0:
        at = data.index(b'\0', at) + 1  # the name
        at = data.index(b'\0', at) + 1  # the type
        size, = struct.unpack_from('<i', data, at)
        at += 4 + size
    return data[:at + 1]


def file_difference(name, got, want, probe):
    """How a written file differs from the Editor's: byte for byte, but for the lighting data's
    ambient probe (its 27 floats where the Editor's file holds `probe`, the scene's stored one,
    within AMBIENT_TOLERANCE as `storedProbe`) and the reflection's texels (header only)."""
    if got is None or want is None:
        return 'not written' if got is None else 'written where the Editor writes none'
    if name.endswith('.exr'):
        got, want = exr_header(got), exr_header(want)
    region = ''
    if name == 'LightingData.asset' and probe is not None and len(got) == len(want):
        at = want.find(struct.pack('<27f', *probe))
        if at >= 0:
            mine = struct.unpack_from('<27f', got, at)
            scale = AMBIENT_TOLERANCE * max(abs(v) for v in probe)
            region = within(list(mine), probe, lambda _: scale, f'x {AMBIENT_TOLERANCE:g} of the largest') or ''
            got = got[:at] + want[at:at + 108] + got[at + 108:]
            region = f'ambient probe at {at}: {region}' if region else ''
    if got == want:
        return region or None
    first = next((k for k, (x, y) in enumerate(zip(got, want)) if x != y), min(len(got), len(want)))
    return f'{len(got)} bytes, expected {len(want)}; first difference at {first}' + (f'; {region}' if region else '')


def compare(label):
    want_root, got_root = reference_root(PROBE), WORK/label/'reference'
    failed = matched = 0
    for scene in scenes():
        want, want_cube = read(want_root/scene)
        got, got_cube = read(got_root/scene)
        for key in sorted(want.keys() | got.keys()):
            a, b = got.get(key), want.get(key)
            if key in ('ambientProbe', 'storedProbe') and a is not None and b is not None:
                theirs = [float(v) for v in b.split()]
                scale = AMBIENT_TOLERANCE * max(abs(v) for v in theirs)
                problem = within([float(v) for v in a.split()], theirs, lambda _: scale, f'x {AMBIENT_TOLERANCE:g} of the largest')
            else:
                problem = None if a == b else f'expected {b!r} actual {a!r}'
            if problem:
                failed += 1
                print('FAIL', f'{scene}.{key}', problem)
            else:
                matched += 1
        # The files the bake wrote beside the scene, byte for byte (normalized as kept).
        mine_files, their_files = written_files(got_root/scene), written_files(want_root/scene)
        stored = [float(v) for v in want['storedProbe'].split()] if 'storedProbe' in want else None
        for name in sorted(mine_files.keys() | their_files.keys()):
            problem = file_difference(name, mine_files.get(name), their_files.get(name), stored)
            if problem:
                failed += 1
                print('FAIL', f'{scene}.written/{name}', problem)
            else:
                matched += 1
        if want_cube is None and got_cube is None:
            continue
        problem = 'no texels' if got_cube is None else 'texels where the Editor has none' if want_cube is None \
            else cube_difference(got_cube, want_cube, int(want['default'].split()[2]))
        if problem:
            failed += 1
            print('FAIL', f'{scene}.cube', problem)
        else:
            matched += 1
    print(f'bakeenvironment: {matched}/{matched + failed} records match; {failed} differ')
    return int(failed > 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['create', 'native', 'engine', 'publish', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--scene', help='create: add these scenes only (comma-separated)')
    args = parser.parse_args()
    labels = args.labels + ([args.label] if args.label else [])
    if any(Path(label).name != label or label in ('.', '..') for label in labels):
        sys.exit('labels are plain names')
    if args.mode == 'create':
        return create(args.scene)
    if args.mode == 'native':
        return native(labels[0] if labels else private_label('native'))
    if args.mode == 'publish':
        return publish(labels)
    if args.mode in ('engine', 'compare') and not labels:
        labels = [private_label(args.mode)]
    if args.mode == 'engine':
        return engine(labels[0])
    if args.mode == 'compare':
        return compare(labels[0])
    label = private_label('check')
    return engine(label) or compare(label)


if __name__ == '__main__':
    sys.exit(main())
