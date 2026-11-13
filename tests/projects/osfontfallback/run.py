#!/usr/bin/env python3
"""Record and compare which macOS faces fill in characters a font lacks (legacy fonts and TextCore).

    run.py record --label L     one macOS Editor capture (batch mode, no graphics device)
    run.py publish A B          two byte-identical captures -> Reference/unity-<release>/
    run.py gate [--label L]     the engine on a fresh copy, compared with Reference/
    run.py check-reference      the committed reference is complete and holds every residual key

The reference holds the faces and metrics of the macOS system fonts it was recorded with, so
record, publish and the engine comparison run on macOS only. On another host `gate` prints that
it was not run and why; `check-reference` runs anywhere.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402
from probe_paths import reference_root, run_root, unity_version  # noqa: E402

PROBE = Path(__file__).resolve().parent
VARIABLE = 'OS_FONT_FALLBACK_OUT'


def editor():
    return Path(os.environ.get('UNBOUND_PROBE_UNITY', '/Applications/Unity/Hub/Editor/'+unity_version()
                               + '/Unity.app/Contents/MacOS/Unity'))


def record(label):
    out = run_root(PROBE, label)
    project = value_probe.assemble(PROBE, out)
    capture = out/'capture'
    capture.mkdir(parents=True)
    command = [str(editor()), '-batchmode', '-nographics', '-projectPath', str(project),
               '-executeMethod', 'ProbeRunner.Record', '-logFile', str(out/'editor.log')]
    code = subprocess.run(command, env={**os.environ, VARIABLE: str(capture)}, timeout=900).returncode
    try:
        value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        raise SystemExit(f'{out}: no complete capture ({error}); Editor exit {code}')
    print('captured', capture)
    return 0


def publish(labels):
    if len(labels) != 2:
        raise SystemExit('publish takes the labels of two captures')
    first, second = (run_root(PROBE, label)/'capture'/value_probe.VALUES for label in labels)
    value_probe.read_values(first)
    if first.read_bytes() != second.read_bytes():
        raise SystemExit('the two captures differ; not publishing')
    target = reference_root(PROBE)
    target.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(first, target/value_probe.VALUES)
    print('published', target)
    return 0


def check_reference():
    values = value_probe.read_values(reference_root(PROBE)/value_probe.VALUES)
    missing = sorted(key for key in value_probe.residuals(PROBE) if key not in values)
    if missing:
        raise SystemExit('osfontfallback: residuals.json lists keys the reference does not hold: ' + ', '.join(missing))
    print(f'osfontfallback: reference complete, {len(values)} values')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate', 'check-reference'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    args = parser.parse_args()
    if args.mode == 'check-reference':
        return check_reference()
    if sys.platform != 'darwin':
        reason = 'the reference was recorded with the macOS system fonts'
        if args.mode == 'gate':
            print(f'osfontfallback: not run on {sys.platform}: {reason}')
            return 0
        sys.exit(f'osfontfallback: {reason}; run it on macOS')
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        return record(label)
    if args.mode == 'publish':
        return publish(args.labels)
    return value_probe.gate(PROBE, VARIABLE, label, 0, ['--headless', '600'])


if __name__ == '__main__':
    sys.exit(main())
