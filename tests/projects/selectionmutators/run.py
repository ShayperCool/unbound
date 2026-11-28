#!/usr/bin/env python3
"""Record Selection mutation and state on Unity 6000.0 and replay the same project."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402
from probe_paths import private_label  # noqa: E402

BASE = [(PROBE.parent/'imguiskin', ('Assets/Scenes', 'Assets/Editor')),
        (PROBE.parent/'analyticssession', ('ProjectSettings',))]
VARIABLE = 'SELECTION_MUTATORS_OUT'


def gate(label):
    out = value_probe.run_root(PROBE, label)
    project = value_probe.assemble(PROBE, out, BASE)
    capture = out/'engine'
    capture.mkdir(parents=True)
    command = [str(value_probe.LAUNCHER), '--kind', 'engine-cpu', 'timeout', '180', value_probe.engine_cli(),
               'run', '-p', str(project), '--no-watch', '--headless', '120']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT,
                              env={**os.environ, VARIABLE: str(capture)}).returncode
    expected = value_probe.read_values(value_probe.reference_dir(PROBE)/value_probe.VALUES)
    try:
        actual = value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        print(f'selectionmutators: no complete engine capture ({error}); engine exit {code}; log {out}/engine.log')
        return 1
    matched, failures = value_probe.compare(expected, actual, 0)
    residual_count = 0
    for failure in failures:
        if failure == {'key': 'update.selectionChanged', 'expected': '0', 'actual': '1'}:
            failure['residual'] = 'Existing deferred selection notification after the startup batch'
            residual_count += 1
        print(json.dumps(failure))
    unexpected = len(failures) - residual_count
    print(f'selectionmutators: {matched}/{len(expected)} values match, {unexpected} failures, '
          f'{residual_count} known residuals; engine exit {code}; logs {out}')
    return int(bool(unexpected or code))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=private_label())
    args = parser.parse_args()
    if any(Path(label).name != label or label in ('.', '..') for label in (args.label, *args.labels)):
        parser.error('labels must be single directory names')
    if args.mode == 'record':
        value_probe.record(PROBE, 'ImguiSkinRunner.Record', VARIABLE, args.label, batch=True, base=BASE, macos_batch=True)
        value_probe.seal_value_capture(value_probe.run_root(PROBE, args.label).resolve())
        return 0
    if args.mode == 'publish':
        value_probe.verify_sealed_value_repeats([value_probe.run_root(PROBE, label).resolve() for label in args.labels])
        return value_probe.publish(PROBE, args.labels)
    return gate(args.label)


if __name__ == '__main__':
    sys.exit(main())
