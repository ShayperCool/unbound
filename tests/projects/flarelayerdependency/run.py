#!/usr/bin/env python3
"""Capture independent Editor references or compare an engine run with a release reference."""
import argparse
import os
import re
import time
from pathlib import Path
import shutil
import subprocess
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from editor_runner import EditorJob, run, result_problem, summary
from probe_paths import run_root, reference_root
PROBE = Path(__file__).resolve().parent

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['editor', 'publish', 'extend', 'check'])
    parser.add_argument('--release', required=True)
    parser.add_argument('--profile', choices=['async-observation', 'sync-shaders'], default='sync-shaders')
    parser.add_argument('--label')
    parser.add_argument('--cli')
    args = parser.parse_args()
    if args.label and (Path(args.label).name != args.label or args.label in ('.', '..')):
        parser.error('label must be one directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    root = run_root(PROBE, args.label or ('gate-' + str(time.time_ns())))
    ref = reference_root(PROBE)
    if args.profile == 'sync-shaders':
        ref = ref / 'sync-shaders'
    if args.mode in ('publish', 'extend'):
        a, b = [root / r / 'values.txt' for r in ('native1', 'native2')]
        assert a.read_bytes() == b.read_bytes() and 'complete=true' in a.read_text()
        assert (root / 'native1' / 'load.txt').read_bytes() == (root / 'native2' / 'load.txt').read_bytes()
        for repeat in ('native1', 'native2'):
            assert not result_problem(root / repeat, gpu=True)
        if args.mode == 'extend':
            previous = dict(line.split('=', 1) for line in (ref/'values.txt').read_text().splitlines())
            current = dict(line.split('=', 1) for line in a.read_text().splitlines())
            assert all(current.get(key) == val for key, val in previous.items()), 'existing reference values changed'
            assert (ref/'load.txt').read_bytes() == (root/'native1/load.txt').read_bytes()
        else:
            ref.mkdir(parents=True, exist_ok=False)
        shutil.copy2(a, ref / 'values.txt')
        shutil.copy2(root / 'native1' / 'load.txt', ref / 'load.txt')
        return
    repeats = ('native1', 'native2') if args.mode == 'editor' else ('unbound',)
    for repeat in repeats:
        project = root / 'projects' / repeat
        out = root / repeat
        out.mkdir(parents=True, exist_ok=False)
        for part in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(PROBE / part, project / part)
        env = {**os.environ, 'FLARE_DEPENDENCY_OUT': str(out)}
        env['FLARE_DEPENDENCY_SYNC_SHADERS'] = '1' if args.profile == 'sync-shaders' else '0'
        if args.mode == 'editor':
            result = run(EditorJob(project, out / 'editor.log', out, env=env, timeout=600,
                                   args=['-executeMethod', 'DependencyRunner.Run']))
            print(summary(result), flush=True)
            assert not result_problem(out, gpu=True)
            messages = sorted(re.findall(r'Creating missing Camera component for FlareLayer in [^\r\n]+', (out / 'editor.log').read_text()))
            (out / 'load.txt').write_text('\n'.join(messages) + '\n')
        else:
            env['FLARE_DEPENDENCY_ENGINE'] = '1'
            cli = args.cli or os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
            assert cli, '--cli or UNBOUND_CLI required'
            with (out / 'engine.log').open('w') as log:
                result = subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                    cli, 'run', '-p', str(project), '--no-watch', '--frames', '300', '--size', '128x128'], env=env, stdout=log, stderr=subprocess.STDOUT)
            assert result.returncode == 0, out / 'engine.log'
            messages = sorted(re.findall(r'Creating missing Camera component for FlareLayer in [^\r\n]+', (out / 'engine.log').read_text()))
            expected_load = (ref / 'load.txt').read_text()
            assert '\n'.join(messages) + '\n' == expected_load, messages
            actual = (out / 'values.txt').read_text()
            expected = (ref / 'values.txt').read_text()
            if actual != expected:
                import difflib
                print(''.join(difflib.unified_diff(expected.splitlines(True), actual.splitlines(True))))
                raise SystemExit(1)
            print('FlareLayer dependency: all values match', flush=True)

if __name__ == '__main__':
    main()
