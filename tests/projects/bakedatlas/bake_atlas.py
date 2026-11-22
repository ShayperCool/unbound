#!/usr/bin/env python3
"""The layout of baked lightmaps: which lightmap each renderer goes to, its lightmap scale and
offset, and each lightmap's size, after Lightmapping.Bake in Edit mode.

    bake_atlas.py create              # authoring: the Editor makes Assets/Scenes (AtlasSetup.Create)
    bake_atlas.py native --label L    # an Editor capture (batch mode on the GPU runner)
    bake_atlas.py publish L1 L2       # two byte-identical captures -> Reference/unity-<version>/atlas.jsonl
    bake_atlas.py engine --label L    # Unbound (unbound test, Edit mode)
    bake_atlas.py compare --candidate FILE
    bake_atlas.py check               # engine run + compare

`Assets/Editor/AtlasRecord.cs` opens each scene of Assets/Scenes, bakes it and records every
MeshRenderer's lightmap index and scale and offset (float bits) and the lightmaps' sizes. Unbound
does not bake lightmaps yet: its bake writes the layout to
Library/Unbound/BakedGI/<scene GUID>/atlas.json, from which the candidate takes each renderer's
index, scale and offset and the lightmaps' sizes. A renderer without a lightmap (index outside
0..65533 in the Editor, absent from the layout in Unbound) is recorded as `none`. Every record
is compared exactly."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
import editor_runner  # noqa: E402
from probe_paths import editor_path, private_label, project_copy, reference_root, run_root  # noqa: E402

WORK = run_root(PROBE, 'bakedatlas')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'atlas.jsonl'


def reference():
    return reference_root(PROBE)/OUT


def editor(out, method, env, label, graphics=True, progress=None):
    job = editor_runner.EditorJob(out/'project', out/'editor.log', out, ['-executeMethod', method],
                                  batch=True, graphics=graphics, env=env,
                                  progress_files=[progress] if progress else [],
                                  progress_timeout=1200 if progress else None, timeout=2400,
                                  label='bakedatlas-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    return int(result.get('exit_code') != 0 or bool(result.get('forced_exit')))


def create():
    scenes = PROBE/'Assets/Scenes'
    if scenes.exists():
        sys.exit(f'refusing to overwrite {scenes}')
    out = WORK/private_label('create')
    project_copy(PROBE, out/'project', package_cache=False)
    if editor(out, 'AtlasSetup.Create', {}, 'create', graphics=False):
        return 1
    shutil.copytree(out/'project/Assets/Scenes', scenes)
    shutil.copy2(out/'project/Assets/Scenes.meta', PROBE/'Assets/Scenes.meta')
    return 0


def native(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project_copy(PROBE, out/'project', package_cache=False)
    # The Editor and its LightBaker load their plug-ins (the denoisers') by bare name from its
    # Data/Tools folder, which must be on the library path.
    tools = str(editor_path().parent/'Data/Tools')
    env = {'BAKED_ATLAS_OUT': str(out/OUT),
           'LD_LIBRARY_PATH': tools + (':' + os.environ['LD_LIBRARY_PATH'] if os.environ.get('LD_LIBRARY_PATH') else '')}
    code = editor(out, 'AtlasRecord.Record', env, label, progress=out/OUT)
    return code or int(not (out/OUT).is_file())


def rows(path):
    result = {}
    for line in Path(path).read_text().splitlines():
        row = json.loads(line)
        if row['key'] in result:
            raise ValueError(f'duplicate key {row["key"]} in {path}')
        result[row['key']] = row['value']
    if result.get('complete') != '1':
        raise ValueError(f'incomplete capture {path}')
    return result


def scenes():
    return sorted(p.stem for p in (PROBE/'Assets/Scenes').glob('*.unity'))


def placement(key):
    """The scene and the renderer's path of a renderer's record, or None."""
    scene, _, rest = key.partition('.')
    if scene not in scenes() or rest in ('bake', 'lightmaps'):
        return None
    return scene, rest


def normalized(values):
    """A renderer's record without a lightmap is `none` (its scale and offset are not used)."""
    out = {}
    for key, value in values.items():
        if placement(key) and value != 'none':
            index = int(value.split()[0])
            value = value if 0 <= index <= 65533 else 'none'
        out[key] = value
    return out


def bits(value):
    return '%08x' % struct.unpack('<I', struct.pack('<f', value))[0]


def guid_of(project, scene):
    meta = (project/'Assets/Scenes'/(scene + '.unity.meta')).read_text()
    return re.search(r'(?m)^guid: ([0-9a-f]{32})$', meta).group(1)


def engine(label):
    out = WORK/label
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    project = project_copy(PROBE, out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, BAKED_ATLAS_OUT=str(out/'capture.jsonl'), BAKED_ATLAS_AUTO='1')
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '1200', cli, 'test', '-p', str(project),
               '--platform', 'EditMode']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print('engine exit', code, 'log', out/'engine.log')
    if code or not (out/'capture.jsonl').is_file():
        return code or 1
    captured = rows(out/'capture.jsonl')
    candidate = {}
    layouts = {}
    for scene in scenes():
        path = project/'Library/Unbound/BakedGI'/guid_of(project, scene)/'atlas.json'
        layouts[scene] = json.loads(path.read_text()) if path.is_file() else None
    for key, value in captured.items():
        where = placement(key)
        if key.endswith('.lightmaps') and key[:-len('.lightmaps')] in layouts:
            layout = layouts[key[:-len('.lightmaps')]]
            value = ' '.join(f'{w}x{h}' for w, h in layout['lightmaps']) if layout else ''
        elif where:
            layout = layouts[where[0]]
            found = [r for r in (layout or {}).get('renderers', []) if r['path'] == where[1]]
            if len(found) > 1:
                raise ValueError(f'{key}: two renderers of one path in the layout')
            value = (str(found[0]['lightmap']) + ' ' + ' '.join(bits(v) for v in found[0]['scaleOffset'])) if found else 'none'
        candidate[key] = value
    with (out/OUT).open('w') as f:
        for key, value in candidate.items():
            f.write(json.dumps({'key': key, 'value': value}) + '\n')
    return 0


def publish(labels):
    if len(labels) < 2:
        sys.exit('publishing needs two independent repeats')
    captures = [(WORK/label/OUT).read_bytes() for label in labels]
    for label in labels:
        rows(WORK/label/OUT)
    if any(capture != captures[0] for capture in captures):
        sys.exit('the repeats differ; not publishing')
    target = reference()
    if target.exists():
        sys.exit(f'refusing to overwrite reference: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(captures[0])
    return 0


def compare(candidate):
    expected, actual = normalized(rows(reference())), normalized(rows(candidate))
    failed = 0
    for key in sorted(expected.keys() | actual.keys()):
        if expected.get(key) != actual.get(key):
            print('FAIL', key, 'expected', repr(expected.get(key)), 'actual', repr(actual.get(key)))
            failed += 1
    matched = sum(actual.get(key) == value for key, value in expected.items())
    print(f'bakedatlas: {matched}/{len(expected)} records match; {failed} differ')
    return int(failed != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['create', 'native', 'engine', 'publish', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    labels = args.labels + ([args.label] if args.label else [])
    if any(Path(label).name != label or label in ('.', '..') for label in labels):
        sys.exit('labels are plain names')
    if args.mode == 'create':
        return create()
    if args.mode == 'native':
        return native(labels[0] if labels else private_label('native'))
    if args.mode == 'engine':
        return engine(labels[0] if labels else private_label('engine'))
    if args.mode == 'publish':
        return publish(labels)
    if args.mode == 'compare':
        return compare(args.candidate)
    label = private_label('check')
    code = engine(label)
    return code or compare(WORK/label/OUT)


if __name__ == '__main__':
    sys.exit(main())
