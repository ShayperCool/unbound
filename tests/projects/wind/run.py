#!/usr/bin/env python3
"""Record two windowed GPU Editor captures and publish one identical wind reference."""
import argparse
import json
import os
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from editor_runner import EditorJob, publish, result_problem, run, summary, vulkan_display_env
from probe_paths import reference_root, run_root

REPEATS = ('native1', 'native2')


def complete(directory):
    result = json.loads((directory/'editor-run.json').read_text())
    if result.get('exit_code') != 0 or result.get('forced_exit'):
        return 'the Editor did not exit normally'
    try:
        rows = [json.loads(line) for line in (directory/'wind.jsonl').read_text().splitlines()]
        if ([row['case'] for row in rows] != ['defaults'] + ['scenario']*14 + ['pulse']*3
                or len({row['name'] for row in rows[1:15]}) != 14
                or any(len(row['steps']) != 5 for row in rows[1:15])
                or any(len(row['samples']) < 2 for row in rows[15:])):
            return 'incomplete wind record'
    except (OSError, ValueError, KeyError, TypeError):
        return 'invalid wind record'
    return None


def editor(work, release):
    for repeat in REPEATS:
        project, out = work/'projects'/repeat, work/repeat
        if project.exists() or out.exists():
            sys.exit(f'refusing to reuse {repeat}')
        for part in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(PROBE/part, project/part)
        (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
        job = EditorJob(project, out/'editor.log', out, batch=False, graphics=True,
            private_display=False, args=['-force-glcore', '-executeMethod', 'ProbeRunner.Record'],
            env={**vulkan_display_env(), 'WIND_PROBE_OUT': str(out)}, timeout=900,
            label='wind-'+repeat, launch_inputs={'release': release, 'paths': {
                'assets': str(project/'Assets'), 'packages': str(project/'Packages'),
                'settings': str(project/'ProjectSettings'), 'runner': str(PROBE/'run.py'),
                'supervisor': str(REPO/'tests/editor_runner.py')}})
        result = run(job)
        print(summary(result), flush=True)
        problem = result_problem(out, gpu=True) or complete(out)
        if problem:
            sys.exit(f'{repeat}: {problem}')
    if (work/'native1/wind.jsonl').read_bytes() != (work/'native2/wind.jsonl').read_bytes():
        sys.exit('the two Editor captures differ')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['editor', 'publish'])
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.release)

    def write(target, first):
        target.mkdir(parents=True)
        shutil.copyfile(first/'wind.jsonl', target/'wind.jsonl')
    return publish([work/repeat for repeat in REPEATS], reference_root(PROBE),
                   lambda directory: ['wind.jsonl'], write, complete=[complete], gpu=True)


if __name__ == '__main__':
    sys.exit(main())
