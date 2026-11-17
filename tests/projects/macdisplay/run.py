#!/usr/bin/env python3
"""The display queries on macOS against the macOS Editor; see README.md.

    run.py native --label L     two macOS Editor captures (windowed), L-1 and L-2
    run.py publish L-1 L-2      two identical captures -> Reference/unity-<release>/macos.jsonl
    run.py check                an Unbound capture compared with the reference (the land gate)

The answers describe the recording machine's display: `check` compares the `unity.*` rows only
when the capture's `os.*` rows (the display's facts) equal the reference's, and otherwise says
that the reference does not describe this display and passes; on another OS it does not apply.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import reference_root, validation_cache  # noqa: E402
import macos_editor  # noqa: E402

OUT = 'values.jsonl'
WORK = validation_cache()/'runs'/PROBE.name
HEAVY = Path.home()/'.cache/compat/with-heavy-job'


def reference():
    return reference_root(PROBE)/'macos.jsonl'


def copy_project(target):
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, target/part)


def rows(path):
    return {r['k']: r['v'] for r in (json.loads(line) for line in Path(path).read_text().splitlines() if line.strip())}


def native(label):
    ok = True
    for repeat in (f'{label}-1', f'{label}-2'):
        out = WORK/repeat
        copy_project(out/'project')
        code = macos_editor.capture(out/'project', out, ['-executeMethod', 'ProbeRunner.Record'],
                                    {'MAC_DISPLAY_OUT': str(out)}, windowed=True)
        print(repeat, 'exit', code)
        ok = ok and code == 0 and (out/OUT).is_file()
    return 0 if ok else 1


def check():
    out = WORK/f'check-{time.time_ns()}'
    copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    launcher = [str(HEAVY), '--kind', 'engine-gpu'] if HEAVY.is_file() else []
    command = [*launcher, 'timeout', '600', cli, 'run', '-p', str(out/'project'), '--no-watch', '--frames', '60',
               '--size', '960x540']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=dict(os.environ, MAC_DISPLAY_OUT=str(out)), stdout=log,
                              stderr=subprocess.STDOUT).returncode
    if code or not (out/OUT).is_file():
        print(f'macdisplay: engine exit {code}, no capture; see {out}')
        return 1
    want, got = rows(reference()), rows(out/OUT)
    host = sorted(k for k in set(want) | set(got) if k.startswith('os.'))
    if any(want.get(k) != got.get(k) for k in host):
        for k in host:
            if want.get(k) != got.get(k):
                print(f'display differs {k}: reference {json.dumps(want.get(k))} here {json.dumps(got.get(k))}')
        print('macdisplay: the reference does not describe this display; not applicable (0 failures)')
        return 0
    keys = set(want) | set(got)
    failures = 0
    for key in sorted(keys):
        if want.get(key) != got.get(key):
            print(f'differs {key}: unity {json.dumps(want.get(key))} unbound {json.dumps(got.get(key))}')
            failures += 1
    print(f'macdisplay: {len(keys) - failures}/{len(keys)} values match, {failures} failures')
    return 1 if failures else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['native', 'publish', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    args = parser.parse_args()
    if args.mode == 'check' and sys.platform != 'darwin':
        print('macdisplay: macOS display queries; not applicable on this OS (0 failures)')
        return 0
    if sys.platform != 'darwin':
        raise SystemExit('macdisplay: the captures need the macOS Editor')
    if args.mode == 'native':
        if not args.label:
            parser.error('native needs --label')
        return native(args.label)
    if args.mode == 'publish':
        if len(args.labels) != 2:
            parser.error('publish takes two capture labels')
        return macos_editor.publish([WORK/label for label in args.labels], reference(), OUT)
    return check()


if __name__ == '__main__':
    sys.exit(main())
