#!/usr/bin/env python3
"""Editor side of the default reflection probe, in copies of the project under the validation cache.

    record.py record  --release 6000.0.71f1   # two independent GPU captures
    record.py publish --release 6000.0.71f1   # write Reference/unity-<release>/ when both agree
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

SCENES = ('Data', 'NoData')


def copy_project(destination, release):
    if destination.exists():
        raise SystemExit(f'refusing to overwrite project: {destination}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, destination/part)
    (destination/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    return destination


def outputs(directory):
    return sorted(p.name for p in Path(directory).iterdir() if p.name == 'defaultreflection.jsonl' or p.suffix == '.png')


def complete(directory):
    path = directory/'defaultreflection.jsonl'
    lines = path.read_text().splitlines() if path.is_file() else []
    if len(lines) != len(SCENES) or any(not (directory/f'{scene}.png').is_file() for scene in SCENES):
        return 'missing or incomplete defaultreflection.jsonl or images'
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['record', 'publish'])
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default='record')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = cache_artifact_path(run_root(PROBE, args.label)).resolve()
    work.mkdir(parents=True, exist_ok=True)
    captures = [work/'editor1', work/'editor2']
    if args.command == 'record':
        for out in captures:
            project = copy_project(out/'project', args.release)
            result = run(EditorJob(project, out/'editor.log', out, batch=True,
                                   args=['-executeMethod', 'ProbeRunner.Record'],
                                   env={'DEFAULT_REFLECTION_PROBE_OUT': str(out)}, timeout=900))
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
    if target.exists():
        sys.exit(f'{target} exists; not publishing over a reference')
    target.mkdir(parents=True)
    names = outputs(captures[0])
    for name in names:
        shutil.copyfile(captures[0]/name, target/name)
    print(f'published {len(names)} files to {target}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
