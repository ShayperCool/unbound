#!/usr/bin/env python3
"""The display queries on macOS against the macOS Editor; see README.md.

    run.py native --label L     two macOS Editor captures (windowed), L-1 and L-2
    run.py publish L-1 L-2      two identical captures -> Reference/unity-<release>/macos.jsonl
    run.py check                an Unbound capture compared with the reference (the land gate)

The answers describe the recording machine's display: `check` compares the `unity.*` rows only
when the capture's `os.*` rows (the display's facts) equal the reference's, and otherwise says
that the reference does not describe this display and passes; a capture that failed (a `fatal`
row, no `complete`, an `os.*` row missing) fails. `check` also runs the engine with a window
larger than the screen: what scripts see at SubsystemRegistration must be what they see on the
fifth frame (the window's fitted size, smaller than the request, and the display's resolution).
On another OS the probe does not apply.
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


# A window larger than any screen it may open on (the consistency run).
OVERSIZED = (8000, 6000)


def engine(label, size):
    """An engine capture with a window of `size`: its rows, else None (with the reason printed)."""
    out = WORK/f'{label}-{time.time_ns()}'
    copy_project(out/'project')
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')
    launcher = [str(HEAVY), '--kind', 'engine-gpu'] if HEAVY.is_file() else []
    command = [*launcher, 'timeout', '600', cli, 'run', '-p', str(out/'project'), '--no-watch', '--frames', '60',
               '--size', f'{size[0]}x{size[1]}']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=dict(os.environ, MAC_DISPLAY_OUT=str(out)), stdout=log,
                              stderr=subprocess.STDOUT).returncode
    got = rows(out/OUT) if (out/OUT).is_file() else {}
    if code or 'fatal' in got or got.get('complete') is not True:
        print(f'macdisplay: engine exit {code}, {got.get("fatal", "incomplete capture")}; see {out}')
        return None
    return got


def consistent():
    """Failures of the oversized window's run: what scripts see first must stay."""
    got = engine('oversized', OVERSIZED)
    if got is None:
        return 1
    failures = 0
    for key in ('unity.screen', 'unity.currentResolution'):
        if got.get(key) != got.get(key + '.atStart'):
            print(f'oversized: {key} at SubsystemRegistration {json.dumps(got.get(key + ".atStart"))}, '
                  f'on the fifth frame {json.dumps(got.get(key))}')
            failures += 1
    width, height = (int(n) for n in got['unity.screen'].split('x'))
    if not (width <= OVERSIZED[0] and height <= OVERSIZED[1] and (width, height) != OVERSIZED):
        print(f'oversized: the window kept the requested {OVERSIZED[0]}x{OVERSIZED[1]} ({width}x{height})')
        failures += 1
    print(f'oversized window: {width}x{height}, {failures} failures')
    return failures


def check():
    got = engine('check', (960, 540))
    if got is None:
        return 1
    failures = consistent()
    want = rows(reference())
    missing = sorted(k for k in want if k.startswith('os.') and k not in got)
    if missing:
        print(f'macdisplay: the capture lacks {", ".join(missing)}')
        return 1
    host = sorted(k for k in set(want) | set(got) if k.startswith('os.'))
    if any(want.get(k) != got.get(k) for k in host):
        for k in host:
            if want.get(k) != got.get(k):
                print(f'display differs {k}: reference {json.dumps(want.get(k))} here {json.dumps(got.get(k))}')
        print(f'macdisplay: the reference does not describe this display; not applicable ({failures} failures)')
        return 1 if failures else 0
    keys = set(want) | set(got)
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
