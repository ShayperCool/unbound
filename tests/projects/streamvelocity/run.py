#!/usr/bin/env python3
"""Record or check the two phases of animation job velocity writes."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import inside_git_checkout, private_label, reference_file, run_root  # noqa: E402
import value_probe  # noqa: E402

BASE = (PROBE.parent/'animculling', ('Assets/Scenes', 'Packages', 'ProjectSettings'))
METHOD = 'StreamVelocityRunner.Record'
VARIABLE = 'STREAM_VELOCITY_OUT'
TOLERANCE = 2e-4


def directory(label):
    if not label or label in ('.', '..') or Path(label).name != label:
        raise SystemExit(f'{label}: expected one run directory name')
    out = run_root(PROBE, '').resolve()/label
    if out.resolve() != out or inside_git_checkout(out):
        raise SystemExit(f'{out}: run must stay in the cache outside any checkout')
    return out


def publish(roots, target):
    value_probe.verify_sealed_value_repeats(roots)
    for root in roots:
        command = value_probe.editor_runner.load_result(root)['command']
        if ('-executeMethod', METHOD) not in zip(command, command[1:]):
            raise SystemExit(f'{root}: expected a capture from {METHOD}')
        settings = (root/'project/ProjectSettings/ProjectVersion.txt').read_text().splitlines()
        if f'm_EditorVersion: {value_probe.unity_version()}' not in settings:
            raise SystemExit(f'{root}: recording was made for a different release')
        shutil.copyfile(root/value_probe.editor_runner.RESULT, root/'capture'/value_probe.editor_runner.RESULT)

    def write(reference, first):
        reference.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(first/value_probe.VALUES, reference)
        print(f'published {reference}')

    return value_probe.editor_runner.publish(
        [root/'capture' for root in roots], target, lambda _: [value_probe.VALUES], write,
        complete=[value_probe.editor_runner.jsonl_complete()], gpu=True)


def gate(out, target):
    expected = value_probe.read_values(target)
    project = value_probe.assemble(PROBE, out, BASE)
    capture = out/'engine'
    capture.mkdir()
    command = [str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600',
               value_probe.engine_cli(), 'run', '-p', str(project), '--headless', '30000', '--no-watch']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env={**os.environ, VARIABLE: str(capture)},
                              stdout=log, stderr=subprocess.STDOUT).returncode
    try:
        actual = value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        print(f'{PROBE.name}: no complete capture ({error}), engine exit {code}, log {out}')
        return 1
    matched, failures = value_probe.compare(expected, actual, TOLERANCE)
    for failure in failures:
        print(json.dumps(failure))
    print(f'{PROBE.name}: {matched}/{len(expected)} records match, {len(failures)} failures, engine exit {code}, log {out}')
    return int(bool(failures or code))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--release', required=True)
    parser.add_argument('--label')
    args = parser.parse_intermixed_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    target = reference_file(PROBE, suffix='jsonl')
    if args.mode == 'publish':
        return publish([directory(label) for label in args.labels], target)
    if args.labels:
        parser.error('use --label for record and gate')
    out = directory(args.label or private_label())
    if args.mode == 'record':
        value_probe.record(PROBE, METHOD, VARIABLE, str(out), batch=True, base=BASE, macos_batch=True)
        value_probe.seal_value_capture(out)
        return 0
    return gate(out, target)


if __name__ == '__main__':
    sys.exit(main())
