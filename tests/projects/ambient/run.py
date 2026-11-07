#!/usr/bin/env python3
"""Release-explicit, engine-only ambient gate; optional independent sun-control recording."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, reference_root, run_root
from editor_runner import EditorJob, run, publish


def copy_project(destination, release):
    if destination.exists():
        raise SystemExit(f'refusing to overwrite project: {destination}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, destination/part)
    (destination/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    return destination


def complete(directory):
    path = directory/'sun.jsonl'
    if not path.is_file():
        return 'missing sun.jsonl'
    records = [json.loads(line) for line in path.read_text().splitlines()]
    expected = {f'{sky} {disk}' for sky in ('default', 'tinted') for disk in ('none', 'hq', 'zero')}
    if len(records) != 6 or {r['name'] for r in records} != expected:
        return 'missing/extra/duplicate sun control'
    if any(len(r.get(k, [])) != 27 for r in records for k in ('ambient', 'rendered')):
        return 'incomplete coefficients'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['gate', 'record', 'publish'])
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default='ambient-gate')
    parser.add_argument('--work', type=Path)
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    reference = reference_root(PROBE)/'sun.jsonl'
    # The original flat reference is kept byte-for-byte, and belongs to this release only.
    if args.release != '6000.0.71f1':
        parser.error('the original ambient oracle is measured only on 6000.0.71f1')
    work = cache_artifact_path(args.work or run_root(PROBE, args.label)).resolve()
    work.mkdir(parents=True, exist_ok=True)
    if args.command == 'record':
        for index in (1, 2):
            out = work/f'editor{index}'
            project = copy_project(out/'project', args.release)
            run(EditorJob(project, out/'editor.log', out, batch=True,
                          args=['-executeMethod', 'ProbeRunner.Record'],
                          env={'AMBIENT_SUN_CONTROLS': str(out)}, timeout=360))
        return 0
    if args.command == 'publish':
        def write(target, first):
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(first/'sun.jsonl', target)
        return publish([work/'editor1', work/'editor2'], reference, ['sun.jsonl'], write,
                       complete=[complete], gpu=True)
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI')
    if not cli:
        parser.error('set UNBOUND or UNBOUND_CLI')
    project = copy_project(work/'project', args.release)
    launcher = Path.home()/'.cache/compat/with-heavy-job'
    for name, variable in [('ambient', 'AMBIENT_PROBE_OUT'), ('sun', 'AMBIENT_SUN_CONTROLS')]:
        env = dict(os.environ)
        env.pop('AMBIENT_PROBE_OUT', None)
        env.pop('AMBIENT_SUN_CONTROLS', None)
        env[variable] = str(work/name)
        command = [str(launcher), cli, 'run', '-p', str(project), '--frames', '1400',
                   '--size', '320x180', '--no-watch']
        with (work/(name+'-engine.log')).open('w') as log:
            subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=360)
    # Preserve the original coefficient and raw image reports as well as the strict detailed gate.
    for script, ref in [(PROBE/'compare.py', PROBE/'Reference'),
                        (PROBE.parent/'particles/compare_images.py', PROBE/'Reference')]:
        subprocess.run([sys.executable, str(script), str(ref), str(work/'ambient')], check=True)
    return subprocess.run([sys.executable, str(PROBE/'verify.py'), '--release', args.release,
                           '--actual', str(work), '--report', str(work/'report.json')]).returncode


if __name__ == '__main__':
    sys.exit(main())
