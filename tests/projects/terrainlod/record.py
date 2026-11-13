#!/usr/bin/env python3
"""Editor side of the terrain LOD probe, in copies of the project under the validation cache.

    record.py create  --release 6000.0.71f1   # run ProbeRunner.Create, copy Assets/ back
    record.py record  --release 6000.0.71f1   # two independent GPU captures
    record.py publish --release 6000.0.71f1   # write Reference/unity-<release>/ when both agree; an existing
                                              # reference only gains rows after its own and new images
"""
import argparse
import os
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, reference_root, run_root  # noqa: E402
from editor_runner import EditorJob, differences, result_problem, run  # noqa: E402


def copy_project(destination, release):
    if destination.exists():
        raise SystemExit(f'refusing to overwrite project: {destination}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, destination/part)
    (destination/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    return destination


def outputs(directory):
    # The probe's own files: the runner may leave logs of its own (hub-sweeps.jsonl) beside them.
    return sorted(p.name for p in Path(directory).iterdir() if p.name == 'terrainlod.jsonl' or p.suffix == '.png')


def complete(directory):
    path = directory/'terrainlod.jsonl'
    lines = path.read_text().splitlines() if path.is_file() else []
    if sum('"image"' in line for line in lines) != 16 or not any('"basemap"' in line for line in lines) \
            or sum('"patches"' in line for line in lines) != 4:
        return 'missing or incomplete terrainlod.jsonl'
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
                               args=['-quit', '-executeMethod', 'ProbeRunner.Create'], timeout=900))
        reason = result_problem(out, gpu=True)
        if reason or result.get('exit_code') != 0:
            print(f'Editor create failed: {reason or result}', file=sys.stderr)
            return 1
        shutil.rmtree(PROBE/'Assets')
        shutil.copytree(project/'Assets', PROBE/'Assets')
        shutil.copyfile(project/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
        return 0
    if args.command == 'record':
        for out in captures:
            project = copy_project(out/'project', args.release)
            result = run(EditorJob(project, out/'editor.log', out, batch=True,
                                   args=['-executeMethod', 'ProbeRunner.Record'],
                                   env={'TERRAIN_LOD_PROBE_OUT': str(out)}, timeout=900))
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
    target = reference_root(PROBE)
    names = outputs(captures[0])
    if target.exists():
        # Rows and images already published stay as they are; only additions are taken.
        old = (target/'terrainlod.jsonl').read_text().splitlines(keepends=True)
        new = (captures[0]/'terrainlod.jsonl').read_text().splitlines(keepends=True)
        if new[:len(old)] != old:
            sys.exit('the committed reference rows changed; not publishing')
        changed = [n for n in names if n.endswith('.png') and (target/n).exists() and (target/n).read_bytes() != (captures[0]/n).read_bytes()]
        if changed:
            sys.exit(f'committed images changed ({", ".join(changed)}); not publishing')
        names = ['terrainlod.jsonl'] + [n for n in names if n.endswith('.png') and not (target/n).exists()]
    target.mkdir(parents=True, exist_ok=True)
    for name in names:
        shutil.copyfile(captures[0]/name, target/name)
    print(f'published {len(names)} files to {target}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
