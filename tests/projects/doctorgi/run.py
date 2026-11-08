#!/usr/bin/env python3
"""Realtime GI without precomputed output, plus doctor inventory of the same scenes."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import run_root, reference_root, unity_version
import editor_runner

HEAVY = Path.home()/'.cache/compat/with-heavy-job'


def project(out):
    target = out/'project'
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT.parent/'lightingassets'/folder, target/folder)
    for folder in ('Editor', 'Scripts'):
        shutil.copytree(ROOT/'Assets'/folder, target/'Assets'/folder, dirs_exist_ok=True)
    (target/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+unity_version()+'\n')
    return target


def record(label):
    out = run_root(ROOT, label)
    path = project(out)
    job = editor_runner.EditorJob(path, out/'editor.log', out,
        ['-executeMethod', 'DoctorGIRunner.Record'], batch=True, graphics=True,
        env={'DOCTOR_GI_OUT': str(out)}, timeout=1200, label='doctorgi-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or editor_runner.jsonl_complete('values.jsonl', {'case': 'end'})(out)
    if problem:
        raise SystemExit(problem)


def gate(label):
    out = run_root(ROOT, label)
    path = project(out)
    # A scene with no realtime precomputation, and an unassigned enabled asset. These edits
    # are confined to this probe's disposable copy.
    settings = path/'Assets/Settings/RealtimeOff.lighting'
    settings.write_text(settings.read_text().replace('m_EnableRealtimeLightmaps: 0', 'm_EnableRealtimeLightmaps: 1'))
    unused = path/'Assets/Settings/Unused.lighting'
    shutil.copy2(settings, unused)
    unused.with_suffix('.lighting.meta').write_text('fileFormatVersion: 2\nguid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n')
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(REPO/'target/release/unbound')
    with (out/'doctor.json').open('w') as report, (out/'doctor.log').open('w') as log:
        subprocess.run([str(HEAVY), '--kind', 'tool', cli, 'doctor', '-p', str(path), '--json'], stdout=report, stderr=log, check=True)
    data = json.loads((out/'doctor.json').read_text())
    reference = [json.loads(line) for line in (reference_root(ROOT)/'values.jsonl').read_text().splitlines()]
    assert reference[0] == {'case': 'no-precompute', 'enabled': True, 'contributors': 0, 'changedPixels': 0}, reference
    assert reference[1]['contributors'] > 0, reference
    entries = data['realtimeGI']
    by_scene = {entry['scene']: entry for entry in entries if entry['scene']}
    assert by_scene['Assets/Scenes/Realtime.unity']['reading'] == 'Unsupported', entries
    assert by_scene['Assets/Scenes/Realtime.unity']['contributors'] == reference[1]['contributors'], entries
    assert by_scene['Assets/Scenes/RealtimeOff.unity']['reading'] == 'Informational', entries
    assert by_scene['Assets/Scenes/Lit.unity']['reading'] == 'Informational', entries
    assert any(entry['settings'] == 'Assets/Settings/Unused.lighting' and entry['reading'] == 'Informational' for entry in entries), entries
    print(f'doctorgi: {len(entries)} impact entries, native contributor count matches, no-output controls informational')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['record', 'publish', 'gate'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default='check-'+str(time.time_ns()))
    args = parser.parse_args()
    if args.mode == 'record':
        record(args.label)
    elif args.mode == 'gate':
        gate(args.label)
    else:
        def write(path, first):
            path.mkdir(parents=True)
            shutil.copy2(first/'values.jsonl', path/'values.jsonl')
        return editor_runner.publish([run_root(ROOT, label) for label in args.labels], reference_root(ROOT),
            lambda _: ['values.jsonl'], write,
            complete=[editor_runner.jsonl_complete('values.jsonl', {'case': 'end'})], gpu=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
