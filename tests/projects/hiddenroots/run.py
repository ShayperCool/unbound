#!/usr/bin/env python3
"""Record and replay scene roots after realtime reflection and particle lighting."""
import argparse
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from editor_runner import EditorJob, run, summary, vulkan_display_env
from probe_paths import run_root
spec = importlib.util.spec_from_file_location('edit_mode_runner', PROBE.parent / 'editinheritance/run.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
shared.PROBE = PROBE


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=('editor', 'publish', 'check'))
    p.add_argument('--release', required=True)
    p.add_argument('--label', default='quit-' + str(time.time_ns()))
    a = p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
    work = run_root(PROBE, a.label)
    reference = PROBE / 'Reference' / ('unity-' + a.release + '.jsonl')
    if a.mode == 'publish':
        captures = [work / r for r in ('native1', 'native2')]
        for capture in captures:
            problem = shared.capture_problem(capture)
            if problem: raise SystemExit(problem)
        first, second = [c / 'values.jsonl' for c in captures]
        if first.read_bytes() != second.read_bytes(): raise SystemExit('Editor repeats differ')
        reference.parent.mkdir(parents=True, exist_ok=True)
        with reference.open('xb') as f: f.write(first.read_bytes())
        return 0
    repeats = ('native1', 'native2') if a.mode == 'editor' else ('unbound',)
    for repeat in repeats:
        project, out = work / 'projects' / repeat, work / repeat
        shared.prepare(project, a.release)
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.particlesystem":"1.0.0"}}')
        out.mkdir(parents=True)
        overrides = {'HIDDEN_ROOTS_OUT': str(out / 'values.jsonl')}
        env = dict(os.environ, **overrides)
        if a.mode == 'editor':
            result = run(EditorJob(project, out / 'editor.log', out, ['-force-glcore'],
                private_display=False, batch=False, graphics=True, timeout=300,
                env={**vulkan_display_env(), **overrides},
                launch_inputs={'release': a.release, 'paths': {'assets': str(project / 'Assets'),
                    'settings': str(project / 'ProjectSettings'), 'packages': str(project / 'Packages'),
                    'runner': str(PROBE / 'run.py')}}))
            print(summary(result))
            problem = shared.capture_problem(out)
            if problem: raise SystemExit(problem)
        else:
            cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
            if not cli: p.error('set UNBOUND_CLI to an immutable runtime')
            with (out / 'engine.log').open('w') as f:
                code = subprocess.run([str(Path.home() / '.cache/compat/with-heavy-job'), '--kind', 'engine-gpu',
                    'timeout', '240', cli, 'test', '-p', str(project), '--platform', 'EditMode'],
                    env=env, stdout=f, stderr=subprocess.STDOUT).returncode
            if code: return code
            return subprocess.run([sys.executable, str(PROBE.parent / 'editinheritance/compare.py'),
                str(reference), str(out / 'values.jsonl'), '--name', 'hiddenroots']).returncode
    first, second = [work / r / 'values.jsonl' for r in repeats]
    if first.read_bytes() != second.read_bytes(): raise SystemExit('Editor repeats differ')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
