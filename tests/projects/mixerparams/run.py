#!/usr/bin/env python3
"""Capture exposed mixer parameter ownership, compare complete key unions, or gate engine only."""
import argparse
import datetime
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

FIXTURE = Path(__file__).resolve().parent
REPO = FIXTURE.parents[2]
CACHE = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/q100'
CASES = ('lp_render', 'dist_render', 'lp_idle', 'dist_idle')
STEPS = (
    'initial', 'pre.set', 'pre.set.get', 'pre.clear', 'pre.clear.get.0', 'pre.clear.get.1',
    'pre.clear.again', 'pre.clear.again.get', 'missing.get', 'missing.set', 'missing.clear',
    'nonexposed.get', 'nonexposed.set', 'nonexposed.clear', 'pre.setafterclear',
    'active.block.0', 'active.block.1', 'active.block.2', 'active.block.3', 'active.beforeclear',
    'active.clear', 'active.clear.get.0', 'active.clear.get.1', 'active.clear.again',
    'active.clear.again.get', 'active.setafterclear', 'active.unapplied.clear',
    'active.sameframe.render', 'active.nextframe', 'active.nextframe.render',
    'snapshot.overridden.immediate', 'snapshot.overridden.applied', 'snapshot.clear.immediate',
    'snapshot.clear.applied', 'snapshot.base.immediate', 'snapshot.base.applied')


def header(root):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result: raise ValueError(f'duplicate capture key: {key}')
            result[key] = value
        return result
    return json.loads((root/'capture.json').read_text(), object_pairs_hook=unique)


def verify(root, release, rate):
    data = header(root)
    expected = {'release', 'rate', 'complete'} | {f'{case}.{step}' for case in CASES for step in STEPS}
    if data.keys() != expected: raise ValueError(f'capture key mismatch: {sorted(data.keys() ^ expected)}')
    if data['release'] != release or data['rate'] != rate or data['complete'] is not True:
        raise ValueError('wrong release/rate or incomplete capture')


def compare(reference, candidate, report):
    left, right = [header(root) for root in (reference, candidate)]
    failures = [dict(key=k, reference=left.get(k), candidate=right.get(k))
                for k in sorted(left.keys() | right.keys()) if k not in left or k not in right or left[k] != right[k]]
    result = dict(tested=len(left.keys() | right.keys()), failed=len(failures), failures=failures)
    report.write_text(json.dumps(result, indent=2)+'\n')
    print(f'{result["tested"]} values, {result["failed"]} failures; {report}', flush=True)
    return bool(failures)


def capture(args):
    work = args.work; work.mkdir(parents=True, exist_ok=False)
    project = work/'project'
    if args.mode == 'native':
        if os.environ.get('UNBOUND_SUITE_NO_EDITOR'): raise SystemExit('Native recording forbidden in engine-only suite')
        shutil.copytree(FIXTURE, project, ignore=shutil.ignore_patterns('Reference', 'Authored', 'Library', '__pycache__', '*.py', '*.md'))
        command = [str(Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Unity'), '-batchmode', '-nographics',
                   '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(work/'editor.log')]
    else:
        for name in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(args.project/name, project/name)
        for script in (FIXTURE/'Assets/Scripts').glob('*.cs'): shutil.copy2(script, project/'Assets/Scripts'/script.name)
        command = [str(args.unbound), 'run', '-p', str(project), '--scene', 'Assets/Scenes/Probe.unity',
                   '--headless', '120', '--no-watch', '--fixed-fps', '60']
    env = dict(os.environ, PARAMS_RATE=str(args.rate), PARAMS_OUT=str(work/'capture'), PARAMS_ENGINE='1' if args.mode == 'engine' else '0')
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '900', *command]
    with (work/'run.log').open('w') as log: subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    verify(work/'capture', args.release, args.rate)
    print('captured', work, flush=True)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=('native', 'engine', 'compare', 'publish', 'gate'))
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(44100, 48000), default=48000)
    p.add_argument('--work', type=Path)
    p.add_argument('--project', type=Path)
    p.add_argument('--unbound', type=Path, default=os.environ.get('UNBOUND'))
    p.add_argument('--reference', type=Path)
    p.add_argument('--candidate', type=Path)
    p.add_argument('--repeat', type=Path)
    p.add_argument('--report', type=Path)
    a = p.parse_args()
    reference = a.reference or FIXTURE/'Reference'/('unity-'+a.release)/('rate-'+str(a.rate))
    if a.mode in ('native', 'engine'):
        capture(a)
    elif a.mode == 'compare':
        return compare(reference, a.candidate, a.report)
    elif a.mode == 'publish':
        if not a.repeat or a.candidate.resolve() == a.repeat.resolve(): p.error('two independent captures required')
        for source in (a.candidate, a.repeat): verify(source, a.release, a.rate)
        if compare(a.candidate, a.repeat, a.report): raise SystemExit('Native repeats disagree')
        reference.mkdir(parents=True, exist_ok=False)
        shutil.copy2(a.candidate/'capture.json', reference/'capture.json')
    else:
        work = a.work or CACHE/('gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
        work.mkdir(parents=True, exist_ok=False)
        subprocess.run([sys.executable, str(REPO/'tests/authored_project.py'), 'assemble', 'mixerparams', 'default',
                        '--release', a.release, '--out', str(work/'authored')], check=True)
        failed = False
        for rate in (48000, 44100):
            command = [sys.executable, str(__file__), 'engine', '--release', a.release, '--rate', str(rate),
                       '--unbound', str(a.unbound), '--project', str(work/'authored'), '--work', str(work/f'engine-{rate}')]
            subprocess.run(command, check=True)
            failed |= compare(FIXTURE/'Reference'/('unity-'+a.release)/f'rate-{rate}', work/f'engine-{rate}/capture', work/f'report-{rate}.json')
        return failed
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
