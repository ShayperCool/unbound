#!/usr/bin/env python3
"""Editor side of the grass probe, in copies of the project under the validation cache.

    record.py create  --release 6000.0.71f1   # run ProbeRunner.CreateDensity, copy its assets back
    record.py record  --release 6000.0.71f1   # two independent GPU captures
    record.py publish --release 6000.0.71f1   # replace Reference/ when both captures agree

`publish` refuses unless both captures ran on the NVIDIA GPU, are byte-identical, and the
committed reference rows are an unchanged prefix of the new grass.jsonl; images already in
Reference/ are kept as they are and only new images are added.
"""
import argparse
import os
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, run_root  # noqa: E402
from editor_runner import EditorJob, differences, result_problem, run  # noqa: E402

# What ProbeRunner.CreateDensity adds or changes.
CREATED = ['Assets/Grass/Slab.asset', 'Assets/Grass/Slab.prefab', 'Assets/Grass/SlabBare.asset',
           'Assets/Grass/SlabBare.prefab', 'Assets/Grass/DensityCounts.asset',
           'Assets/Grass/DensityCoverage.asset', 'Assets/Scenes/Probe.unity']


def copy_project(destination, release):
    if destination.exists():
        raise SystemExit(f'refusing to overwrite project: {destination}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, destination/part)
    (destination/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    return destination


def outputs(directory):
    return sorted(p.name for p in Path(directory).iterdir() if p.suffix in ('.jsonl', '.png'))


def complete(directory):
    path = directory/'grass.jsonl'
    if not path.is_file():
        return 'missing grass.jsonl'
    lines = path.read_text().splitlines()
    if not lines or not any('"case":"legacy"' in line for line in lines):
        return 'incomplete grass.jsonl'
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['create', 'record', 'publish'])
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default='record')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = cache_artifact_path(run_root(PROBE, args.label)).resolve()
    work.mkdir(parents=True, exist_ok=True)
    captures = [work/'editor1', work/'editor2']
    if args.command == 'create':
        out = work/'create'
        project = copy_project(out/'project', args.release)
        result = run(EditorJob(project, out/'editor.log', out, batch=True,
                               args=['-quit', '-executeMethod', 'ProbeRunner.CreateDensity'], timeout=900))
        reason = result_problem(out, gpu=True)
        if reason or result.get('exit_code') != 0:
            print(f'Editor create failed: {reason or result}', file=sys.stderr)
            return 1
        for name in CREATED:
            for path in (name, name+'.meta'):
                if (project/path).is_file():
                    shutil.copyfile(project/path, PROBE/path)
        return 0
    if args.command == 'record':
        for out in captures:
            project = copy_project(out/'project', args.release)
            result = run(EditorJob(project, out/'editor.log', out, batch=True,
                                   args=['-executeMethod', 'ProbeRunner.Record'],
                                   env={'GRASS_PROBE_OUT': str(out)}, timeout=900))
            reason = result_problem(out, gpu=True) or complete(out)
            if reason or result.get('exit_code') != 0:
                print(f'Editor capture {out.name} failed: {reason or result}', file=sys.stderr)
                return 1
        return 0
    for out in captures:
        reason = result_problem(out, gpu=True) or complete(out)
        if reason:
            sys.exit(f'{out.name}: {reason}; not publishing')
    problems = differences(captures, outputs)
    if problems:
        sys.exit('the captures differ ('+'; '.join(problems)+'); not publishing')
    reference = PROBE/'Reference'
    old = (reference/'grass.jsonl').read_text().splitlines(keepends=True)
    new = (captures[0]/'grass.jsonl').read_text().splitlines(keepends=True)
    if new[:len(old)] != old:
        sys.exit('the committed reference rows changed; not publishing')
    shutil.copyfile(captures[0]/'grass.jsonl', reference/'grass.jsonl')
    for name in outputs(captures[0]):
        if name.endswith('.png') and not (reference/name).exists():
            shutil.copyfile(captures[0]/name, reference/name)
    print(f'published {len(new) - len(old)} added rows')
    return 0


if __name__ == '__main__':
    sys.exit(main())
