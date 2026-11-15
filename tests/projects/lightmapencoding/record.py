#!/usr/bin/env python3
"""Editor side of the lightmap encoding probe, in copies of the project under the validation cache.

    record.py create  --release 6000.0.71f1   # run ProbeRunner.Create, copy Assets/ back
    record.py record  --release 6000.0.71f1   # two independent GPU captures of every profile
    record.py publish --release 6000.0.71f1   # write Reference/unity-<release>/<profile>.jsonl when
                                              # both captures agree; never overwrites a file

`--profile NAME` (repeatable) limits record/publish to those profiles (profiles.py).
"""
import argparse
import os
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
sys.path.insert(0, str(PROBE))
from probe_paths import cache_artifact_path, reference_root, run_root  # noqa: E402
from editor_runner import EditorJob, differences, result_problem, run  # noqa: E402
from profiles import PROFILES, project  # noqa: E402

OUTPUT = 'lightmapencoding.jsonl'


def complete(directory):
    path = directory/OUTPUT
    lines = path.read_text().splitlines() if path.is_file() else []
    if sum('"capture"' in line for line in lines) != 13:
        return f'missing or incomplete {OUTPUT}'
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['create', 'record', 'publish'])
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default='record')
    parser.add_argument('--profile', action='append', choices=sorted(PROFILES))
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = cache_artifact_path(run_root(PROBE, args.label)).resolve()
    work.mkdir(parents=True, exist_ok=True)
    profiles = args.profile or list(PROFILES)
    if args.command == 'create':
        out = work/'create'
        destination = out/'project'
        if destination.exists():
            sys.exit(f'refusing to overwrite project: {destination}')
        for part in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(PROBE/part, destination/part)
        (destination/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+args.release+'\n')
        result = run(EditorJob(destination, out/'editor.log', out, batch=True,
                               args=['-quit', '-executeMethod', 'ProbeRunner.Create'], timeout=900))
        reason = result_problem(out, gpu=True)
        if reason or result.get('exit_code') != 0:
            print(f'Editor create failed: {reason or result}', file=sys.stderr)
            return 1
        shutil.rmtree(PROBE/'Assets')
        shutil.copytree(destination/'Assets', PROBE/'Assets')
        shutil.copyfile(destination/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
        return 0
    if args.command == 'record':
        for profile in profiles:
            for repeat in ('editor1', 'editor2'):
                out = work/profile/repeat
                target = project(out/'project', profile, args.release)
                result = run(EditorJob(out/'project', out/'editor.log', out, batch=True,
                                       args=['-buildTarget', target, '-executeMethod', 'ProbeRunner.Record'],
                                       env={'LIGHTMAP_ENCODING_PROBE_OUT': str(out)}, timeout=1200))
                reason = result_problem(out, gpu=True) or complete(out)
                if reason or result.get('exit_code') != 0:
                    print(f'Editor capture {profile}/{repeat} failed: {reason or result}', file=sys.stderr)
                    return 1
        return 0
    target = reference_root(PROBE)
    for profile in profiles:
        captures = [work/profile/'editor1', work/profile/'editor2']
        for out in captures:
            reason = result_problem(out, gpu=True) or complete(out)
            if reason:
                sys.exit(f'{profile}/{out.name}: {reason}; not publishing')
        problems = differences(captures, lambda d: [OUTPUT, 'settings.json'])
        if problems:
            sys.exit(f'{profile}: the captures differ ('+'; '.join(problems)+'); not publishing')
        path = target/f'{profile}.jsonl'
        if path.exists():
            sys.exit(f'{path} exists; not publishing')
    target.mkdir(parents=True, exist_ok=True)
    for profile in profiles:
        shutil.copyfile(work/profile/'editor1'/OUTPUT, target/f'{profile}.jsonl')
    print(f'published {len(profiles)} profiles to {target}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
