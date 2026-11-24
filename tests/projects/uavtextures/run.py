#!/usr/bin/env python3
"""Record the UAV textures & packed storage buffer probe in the Unity Editor (native),
run Unbound (engine), publish, verify.

    run.py native --label L [--vulkan]  two independent GPU Editor captures (L-1, L-2)
    run.py publish L-1 L-2              two byte-identical captures -> Reference/unity-<version>.jsonl
    run.py engine --label L             an Unbound capture
    run.py verify --candidate PATH      compare a capture with the reference
    run.py check                        an engine run into a new directory, then verify
"""
import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, run_root, unity_version  # noqa: E402
import editor_runner  # noqa: E402

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'
OUT = 'values.jsonl'
TOL = 1e-2


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, target/folder)
    (target/'ProjectSettings/ProjectVersion.txt').write_text(f'm_EditorVersion: {unity_version()}\n')


def native(label, vulkan=False):
    jobs = []
    for repeat in (f'{label}-1', f'{label}-2'):
        out = WORK/repeat
        copy_project(out/'project')
        jobs.append(editor_runner.EditorJob(
            out/'project', out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
            batch=True, graphics=True, vulkan=vulkan,
            env={'UAVTEXTURES_PROBE_OUT': str(out/OUT)},
            timeout=240, label='uavtextures-' + repeat))
    for job in jobs:
        result = editor_runner.run(job)
        print(editor_runner.summary(result))
    return 0 if all((job.out/OUT).exists() for job in jobs) else 1


def engine(label, vulkan=False):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'refusing to overwrite {out}')
    copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    env = dict(os.environ, UAVTEXTURES_PROBE_OUT=str(out/OUT))
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p', str(out/'project'),
               '--no-watch', '--frames', '120', '--size', '64x32']
    if vulkan:
        command.append('--force-vulkan')
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    print(label, 'exit', code)
    return 0 if code == 0 and (out/OUT).exists() else 1


def publish(labels):
    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    target = reference_file(ROOT, 'jsonl')
    return editor_runner.publish(
        [WORK/label for label in labels], target, lambda d: [OUT], write,
        complete=[editor_runner.jsonl_complete(OUT)], gpu=True)


def equal_val(a, b):
    if isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        return all(equal_val(x, y) for x, y in zip(a, b))
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool):
        return math.isfinite(a) and math.isfinite(b) and abs(a - b) <= TOL
    return a == b


def verify(candidate_path):
    ref_path = reference_file(ROOT, 'jsonl')
    ref = {row['k']: row['v'] for row in (json.loads(l) for l in ref_path.read_text().splitlines() if l.strip())}
    got = {row['k']: row['v'] for row in (json.loads(l) for l in Path(candidate_path).read_text().splitlines() if l.strip())}
    ok = 0
    bad = []
    for k, rv in ref.items():
        if k not in got:
            bad.append(f'MISSING {k}: expected={rv!r}')
        elif not equal_val(rv, got[k]):
            bad.append(f'DIFF {k}: expected={rv!r} actual={got[k]!r}')
        else:
            ok += 1
    for k in got:
        if k not in ref:
            bad.append(f'EXTRA {k}: actual={got[k]!r}')
    for line in bad:
        print('FAIL:', line)
    print(f'{ok}/{len(ref)} records match ({len(bad)} discrepancies)')
    return 0 if not bad else 1


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest='cmd', required=True)
    n = sub.add_parser('native')
    n.add_argument('--label', required=True)
    n.add_argument('--vulkan', action='store_true')
    pub = sub.add_parser('publish')
    pub.add_argument('labels', nargs=2)
    eng = sub.add_parser('engine')
    eng.add_argument('--label', required=True)
    eng.add_argument('--vulkan', action='store_true')
    ver = sub.add_parser('verify')
    ver.add_argument('--candidate', required=True)
    chk = sub.add_parser('check')
    chk.add_argument('--vulkan', action='store_true')
    args = p.parse_args()
    if args.cmd == 'native':
        return native(args.label, vulkan=args.vulkan)
    if args.cmd == 'publish':
        return publish(args.labels)
    if args.cmd == 'engine':
        return engine(args.label, vulkan=args.vulkan)
    if args.cmd == 'verify':
        return verify(args.candidate)
    label = f'check-{int(time.time())}-{os.getpid()}'
    code = engine(label, vulkan=args.vulkan)
    return verify(WORK/label/OUT) if code == 0 else code


if __name__ == '__main__':
    sys.exit(main())
