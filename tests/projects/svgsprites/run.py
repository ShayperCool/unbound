#!/usr/bin/env python3
"""SVG importer sprite, UI image and texture outputs: capture twice independently; publish;
check committed fixtures without an Editor. The inputs are authored once by author.py.

On macOS arm64 publish writes and check reads Reference/unity-R/metal.json (the macOS Editor
draws with Metal: tests/probe_paths.py device_reference)."""
import argparse
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
import authored_project
import editor_runner
from probe_paths import device_reference, reference_file, run_root, unity_version

WORK = run_root(ROOT, 'runs')
OUT = 'capture.json'


def assembled(project):
    authored_project.assemble(ROOT.name, unity_version(), 'svg', project)


def native(label):
    out = WORK/label
    if out.exists():
        raise SystemExit(f'{out} already exists')
    project = out/'project'
    assembled(project)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-quit', '-executeMethod', 'SvgSpritesSetup.Record'],
                                  batch=True, graphics=True, env={'SVG_SPRITES_OUT': str(out/OUT)},
                                  timeout=1200, label='svgsprites-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result), flush=True)
    return 0 if result['exit_code'] == 0 else 1


def publish(labels):
    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/OUT, path)
    def complete(directory):
        if not (directory/OUT).is_file() or json.loads((directory/OUT).read_text()).get('complete') is not True:
            return 'capture completion marker is missing'
    return editor_runner.publish([WORK/label for label in labels], device_reference(reference_file(ROOT), ROOT, publishing=True),
                                 lambda d: [OUT], write, complete=[complete])


def compare(reference, candidate, out):
    problems = []
    accepted = []
    count = 0
    # Listed residuals (residuals.json): only these image pixels may differ, by at most `max`.
    residual = json.loads((ROOT/'residuals.json').read_text()).get('image', {}) if (ROOT/'residuals.json').is_file() else {}
    residual_pixels = set(residual.get('pixels', []))

    def walk(a, b, key):
        nonlocal count
        if isinstance(a, dict) and isinstance(b, dict):
            for name in sorted(a.keys() | b.keys()):
                child = key+'.'+name
                if name not in a or name not in b:
                    problems.append(f'{child}: reference={a.get(name, "<missing>")} candidate={b.get(name, "<missing>")}')
                else:
                    walk(a[name], b[name], child)
        elif isinstance(a, list) and isinstance(b, list):
            if len(a) != len(b):
                problems.append(f'{key}: lengths {len(a)} vs {len(b)}')
            for i in range(max(len(a), len(b))):
                if i >= min(len(a), len(b)):
                    problems.append(f'{key}[{i}]: reference={a[i] if i < len(a) else "<missing>"} candidate={b[i] if i < len(b) else "<missing>"}')
                else:
                    walk(a[i], b[i], f'{key}[{i}]')
        elif (key == '.image' or key.endswith('.pixels')) and isinstance(a, str) and isinstance(b, str):
            # Images: RGB bytes; texture pixels: RGBA bytes. Each channel within 2/255.
            stride = 3 if key == '.image' else 4
            pa, pb = bytes.fromhex(a), bytes.fromhex(b)
            if len(pa) != len(pb):
                problems.append(f'{key}: byte counts {len(pa)} vs {len(pb)}')
            for i in range(min(len(pa), len(pb))):
                count += 1
                if abs(pa[i] - pb[i]) > 2:
                    text = f'{key} pixel {i//stride} channel {i%stride}: {pa[i]} vs {pb[i]}'
                    if key == '.image' and i//stride in residual_pixels and abs(pa[i] - pb[i]) <= residual['max']:
                        accepted.append(text)
                    else:
                        problems.append(text)
        else:
            count += 1
            equal = a == b
            if isinstance(a, float) and math.isnan(a):
                # A NaN the Editor records (the package's own arithmetic) is matched by a NaN only.
                equal = isinstance(b, float) and math.isnan(b)
            elif isinstance(a, (int, float)) and not isinstance(a, bool) and isinstance(b, (int, float)):
                equal = math.isfinite(b) and abs(a-b) <= 1e-5 * max(1, abs(a))
            if not equal:
                problems.append(f'{key}: {a!r} vs {b!r}')
    walk(reference, candidate, '')
    (out/'differences.json').write_text(json.dumps({'differences': problems, 'listed_residuals': accepted}, indent=2)+'\n')
    print(f'svgsprites: {count} values, {len(problems)} differences, {len(accepted)} listed residual values; {out}')
    for text in accepted:
        print('listed residual:', text)
    for problem in problems[:20]:
        print(problem)
    return bool(problems)


def check():
    out = WORK/(time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid()))
    project = out/'project'
    assembled(project)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    command = [str(editor_runner.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli,
               'run', '-p', str(project), '--no-watch', '--frames', '12']
    env = dict(os.environ, SVG_SPRITES_OUT=str(out/OUT))
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    if code or not (out/OUT).is_file():
        print(f'engine exit {code}; missing capture: {not (out/OUT).is_file()}; {out}/engine.log')
        return 1
    return compare(json.loads(device_reference(reference_file(ROOT), ROOT).read_text()), json.loads((out/OUT).read_text()), out)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['native', 'publish', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    if args.mode == 'native':
        if not args.label:
            parser.error('--label is required')
        code = native(args.label)
    elif args.mode == 'publish':
        code = publish(args.labels)
    else:
        code = check()
    sys.exit(code)
