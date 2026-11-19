#!/usr/bin/env python3
"""Record or replay URP strip shadows on NVIDIA OpenGL Core.

Staging combines shadergraph's URP project with this probe's authored scripts and shaders.
A renderer feature reads the shadow atlas within the render graph. Comparisons reuse the
line probe's numeric and float-image tolerances, retaining every failing, missing or extra key.
"""
import argparse
import base64
import gzip
import hashlib
import importlib.util
import json
import math
import struct
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
import editor_runner as editor  # noqa: E402
from probe_paths import reference_root, run_root  # noqa: E402
spec = importlib.util.spec_from_file_location('strip_compare', PROBE.parent/'lines/compare.py')
compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare)
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
PARTS = {'assets': 'Assets', 'packages': 'Packages', 'settings': 'ProjectSettings'}
CONTROLS = ('view0', 'viewBias', 'turnedLight', 'transformZ')
CASCADE_CONTROLS = ('cascade2', 'cascade2Wide', 'cascade3', 'cascade4', 'cascade4Wide', 'cascade4Ortho', 'cascade3Close',
                    'cascade4WideClose')
CASCADES = False
KEYS = [f'{light}.{renderer}.{control}.{suffix}'
        for light in ('Directional', 'Spot', 'Point') for renderer in ('line', 'trail', 'mesh')
        for control in CONTROLS for suffix in ('normal.pixels', 'map.covered', 'map.pixels', 'bake')] + ['complete']

BASE_KEYS = KEYS.copy()
CASCADE_KEYS = [f'Directional.{renderer}.{control}.{suffix}' for renderer in ('line','trail','mesh')
                for control in CASCADE_CONTROLS for suffix in ('normal.pixels','map.covered','map.pixels','bake')]


def select_cascades(enabled):
    global CASCADES, KEYS
    CASCADES = enabled
    KEYS = [f'{light}.{renderer}.{control}.{suffix}'
            for light in ('Directional','Spot','Point') for renderer in ('line','trail','mesh')
            for control in (CONTROLS+CASCADE_CONTROLS if enabled and light=='Directional' else CONTROLS)
            for suffix in ('normal.pixels','map.covered','map.pixels','bake')] + ['complete']



def stage(project):
    if project.exists():
        raise RuntimeError(f'refusing to overwrite {project}')
    source = PROBE.parent/'shadergraph'
    for part in ('Packages', 'ProjectSettings'):
        shutil.copytree(source/part, project/part)
    shutil.copytree(source/'Assets', project/'Assets', ignore=lambda p, names:
                    ['Scripts', 'Scripts.meta', 'Editor', 'Editor.meta'] if Path(p) == source/'Assets' else [])
    shutil.copytree(PROBE/'Assets', project/'Assets', dirs_exist_ok=True)
    release = os.environ['UNBOUND_PROBE_UNITY_VERSION']
    if f'm_EditorVersion: {release}\n' not in (project/'ProjectSettings/ProjectVersion.txt').read_text():
        raise RuntimeError('the staged URP project must select the requested release')
    guid = next(line.split()[1] for line in (PROBE/'Assets/Scripts/StripShadowFeature.cs.meta').read_text().splitlines()
                if line.startswith('guid:'))
    renderer = project/'Assets/Settings/ProbeRenderer.asset'
    text = renderer.read_text()
    if '  m_RendererFeatures: []' not in text:
        raise RuntimeError('the source renderer must have no features')
    text = text.replace('  m_RendererFeatures: []', '  m_RendererFeatures:\n  - {fileID: 11400001}')
    text += ('\n--- !u!114 &11400001\nMonoBehaviour:\n  m_ObjectHideFlags: 0\n  m_GameObject: {fileID: 0}\n'
             '  m_Enabled: 1\n  m_EditorHideFlags: 0\n  m_Script: {fileID: 11500000, guid: '+guid+', type: 3}\n'
             '  m_Name: StripShadowFeature\n  m_EditorClassIdentifier: \n  m_Active: 1\n')
    renderer.write_text(text)
    return project


def capture_names(directory):
    return ['values.jsonl', *sorted(p.name for p in directory.glob('*.rgba'))]


def complete(directory):
    path = directory/'values.jsonl'
    if not path.is_file():
        return 'missing values.jsonl'
    try:
        rows = [json.loads(line) for line in path.read_text().splitlines()]
        if [row['k'] for row in rows] != KEYS or rows[-1]['v'] is not True:
            return 'capture keys or completion marker differ from the declared controls'
        counts = {row['k'].removesuffix('.pixels')+'.rgba': row['v'] for row in rows if row['k'].endswith('.pixels')}
        if sorted(counts) != capture_names(directory)[1:]:
            return 'images differ from the recorded counts'
        if any(type(count) is not int or count <= 0 or (directory/name).stat().st_size != count*16 for name, count in counts.items()):
            return 'image size differs from its pixel count'
        if any(not math.isfinite(value) for name in counts for (value,) in struct.iter_unpack('<f', (directory/name).read_bytes())):
            return 'nonfinite image channel'
    except (ValueError, KeyError, TypeError):
        return 'invalid capture records'
    return None


def native(work):
    for repeat in ('native1', 'native2'):
        project = stage(work/'projects'/repeat)
        out = work/repeat
        job = editor.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'StripRunner.Record'], batch=True,
            env={'STRIP_SHADOW_OUT': str(out), 'STRIP_SHADOW_CASCADES': '1' if CASCADES else ''}, timeout=1200, progress_files=[out/'values.jsonl', out/'editor.log'],
            progress_timeout=600, label='stripshadows-'+repeat,
            launch_inputs={'release': os.environ['UNBOUND_PROBE_UNITY_VERSION'], 'paths':
                {key: str(project/part) for key, part in PARTS.items()}})
        print(editor.summary(editor.run(job)), flush=True)
        reason = editor.result_problem(out, gpu=True) or complete(out)
        if reason:
            raise RuntimeError(f'{repeat}: {reason}')
    problems = editor.differences([work/r for r in ('native1', 'native2')], capture_names)
    if problems:
        raise RuntimeError(f'repeats differ: {problems}')
    return 0


def validate_extension(reference, candidate):
    """Keep all prior records in order and every prior image byte-identical."""
    old_lines=(reference/'values.jsonl').read_bytes().splitlines(keepends=True)
    new_lines=(candidate/'values.jsonl').read_bytes().splitlines(keepends=True)
    keys={json.loads(line)['k'] for line in old_lines}
    if [line for line in new_lines if json.loads(line)['k'] in keys] != old_lines:
        raise RuntimeError('existing reference records changed, disappeared or moved')
    for image in reference.glob('*.rgba.gz'):
        new=candidate/image.name
        if not new.exists() or gzip.decompress(image.read_bytes()) != gzip.decompress(new.read_bytes()):
            raise RuntimeError(f'existing reference image changed: {image.name}')
    return b''.join(line for line in new_lines if json.loads(line)['k'] not in keys)


def comparison_reference(work):
    reference=reference_root(PROBE)
    if not CASCADES: return reference
    output=work/'comparison-reference'
    output.mkdir(parents=True)
    for image in reference.glob('*.rgba.gz'):
        (output/image.name).symlink_to(image.resolve())
    old=(reference/'values.jsonl').read_bytes().splitlines(keepends=True)
    extra=(reference/'cascade.values.jsonl').read_bytes()
    complete=[line for line in old if json.loads(line)['k']=='complete']
    (output/'values.jsonl').write_bytes(b''.join(line for line in old if json.loads(line)['k']!='complete')+extra+b''.join(complete))
    return output


def publish(work):
    # Rebuild the exact launch inputs, independently of any files the Editor saved later.
    expected = stage(work/('source-check-'+str(time.time_ns())))
    current = {key+'/'+str(p.relative_to(expected/part)): hashlib.sha256(p.read_bytes()).hexdigest()
               for key, part in PARTS.items() for p in (expected/part).rglob('*') if p.is_file()}
    for repeat in ('native1', 'native2'):
        receipt = json.loads((work/repeat/'prelaunch-inputs.json').read_text())
        recorded = {key: item['sha256'] for key, item in receipt['files'].items()}
        if receipt['selected_release'] != os.environ['UNBOUND_PROBE_UNITY_VERSION'] or recorded != current:
            raise RuntimeError(f'{repeat}: pre-launch inputs differ from the authored probe')
        if any(hashlib.sha256(base64.b64decode(item['bytes_base64'])).hexdigest() != item['sha256'] for item in receipt['files'].values()):
            raise RuntimeError(f'{repeat}: invalid source receipt')
    def write(target, first):
        target.mkdir(parents=True)
        shutil.copyfile(first/'values.jsonl', target/'values.jsonl')
        for name in capture_names(first)[1:]:
            (target/(name+'.gz')).write_bytes(gzip.compress((first/name).read_bytes(), mtime=0))
    reference=reference_root(PROBE)
    candidate=work/('reference-candidate-'+str(time.time_ns())) if CASCADES else reference
    if CASCADES and not reference.exists():
        raise RuntimeError('cascade publication requires the earlier reference')
    editor.publish([work/r for r in ('native1', 'native2')], candidate, capture_names,
                   write, complete=[complete], gpu=True)
    if CASCADES:
        extra=validate_extension(reference,candidate)
        if [json.loads(line)['k'] for line in extra.splitlines()] != CASCADE_KEYS:
            raise RuntimeError('unexpected cascade control records')
        # Earlier cascade records stay byte-identical and in order; controls are only added.
        cascades=reference/'cascade.values.jsonl'
        old=cascades.read_bytes().splitlines(keepends=True) if cascades.exists() else []
        known={json.loads(line)['k'] for line in old}
        if [line for line in extra.splitlines(keepends=True) if json.loads(line)['k'] in known] != old:
            raise RuntimeError('existing cascade records changed, disappeared or moved')
        for image in candidate.glob('*.rgba.gz'):
            target=reference/image.name
            if not target.exists():
                with target.open('xb') as stream: stream.write(image.read_bytes())
        cascades.write_bytes(extra)
        print('published',len(CASCADE_KEYS)-len(old),'new cascade records; every prior record and image unchanged')
    return 0


def check(work):
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
    if not cli:
        raise RuntimeError('set UNBOUND_CLI to the completed checkout build')
    project = stage(work/'project')
    out = work/'capture'
    out.mkdir(parents=True)
    env = dict(os.environ, STRIP_SHADOW_OUT=str(out), STRIP_SHADOW_CASCADES='1' if CASCADES else '')
    with (out/'unbound.log').open('w') as log:
        code = subprocess.run([str(LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', cli, 'run', '-p', str(project),
            '--no-watch', '--frames', '30', '--size', '64x64', '--force-glcore'], env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    if code or complete(out):
        raise RuntimeError(f'engine exit {code}, {complete(out)}; log {out/"unbound.log"}')
    adapter = next((line for line in (out/'unbound.log').read_text().splitlines() if 'AdapterInfo' in line), '')
    if 'NVIDIA' not in adapter:
        raise RuntimeError('NVIDIA engine renderer was not verified')
    failures, count = compare.failures(comparison_reference(work), out)
    (out/'comparison.json').write_text(json.dumps({'keys': count, 'failures': failures}, indent=2)+'\n')
    for key, reason in failures.items():
        print(f'{key}: {reason}')
    print(f'stripshadows: {count-len(failures)}/{count} keys match')
    return int(bool(failures))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('native', 'publish', 'check'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    parser.add_argument('--cascades',action='store_true',help='record or strictly compare the separate multi-cascade diagnostics')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    select_cascades(args.cascades)
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    return {'native': native, 'publish': publish, 'check': check}[args.mode](run_root(PROBE, args.label))


if __name__ == '__main__':
    raise SystemExit(main())
