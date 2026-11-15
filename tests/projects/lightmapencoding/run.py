#!/usr/bin/env python3
"""Engine side of the lightmap encoding probe: `run.py check` runs Unbound on a copy of the project
per profile (profiles.py; `--profile` limits them) and compares its lightmapencoding.jsonl with
Reference/unity-<release>/<profile>.jsonl. Rows other than captures must be equal. Capture pixels
compare channel by channel within a relative 1e-3 (absolute 1e-5 near zero): the Editor's
readbacks of 8-bit sRGB data carry half-float rounding (a relative 2^-11), while one 8-bit step
changes a value by more than 0.9%. Every differing row and pixel is counted; any fails the check,
except the exact pairs residuals.json lists (an Editor row, the engine's row in its place and the
reason), which are printed as residuals. An entry that no longer occurs fails the check too."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
sys.path.insert(0, str(PROBE))
from probe_paths import cache_artifact_path, reference_root, run_root  # noqa: E402
from profiles import PROFILES, project  # noqa: E402

RELATIVE, ABSOLUTE = 1e-3, 1e-5


def close(a, b):
    return abs(a - b) <= max(ABSOLUTE, RELATIVE * max(abs(a), abs(b)))


def compare(profile, expected, actual, residuals):
    """(rows checked, failures, residuals seen) printing every failure and residual."""
    failures = 0
    accepted = [(r['editor'], r['engine'], r['reason']) for r in residuals if r['profile'] == profile]
    seen = 0
    for editor, engine, reason in accepted:
        if editor in expected and editor not in actual and engine in actual and engine not in expected:
            seen += 1
            print(f'lightmapencoding: {profile}: residual: Editor {json.dumps(editor)} engine {json.dumps(engine)} ({reason})')
            expected = [row for row in expected if row != editor]
            actual = [row for row in actual if row != engine]
        else:
            failures += 1
            print(f'lightmapencoding: {profile}: listed residual no longer occurs: {json.dumps(editor)}')
    captures = {row['capture']: row['pixels'] for row in actual if 'capture' in row}
    plain = [row for row in actual if 'capture' not in row]
    for row in expected:
        if 'capture' not in row:
            if row not in plain:
                failures += 1
                print(f'lightmapencoding: {profile}: missing record {json.dumps(row)}')
            continue
        name = row['capture']
        got = captures.get(name)
        if got is None or len(got) != len(row['pixels']):
            failures += 1
            print(f'lightmapencoding: {profile}: missing capture {name}')
            continue
        bad = [(i, e, g) for i, (e, g) in enumerate(zip(row['pixels'], got)) if not all(close(x, y) for x, y in zip(e, g))]
        if bad:
            failures += 1
            i, e, g = bad[0]
            print(f'lightmapencoding: {profile}: {name}: {len(bad)}/{len(got)} pixels differ, first ({i % 16},{i // 16}) '
                  f'Editor {e} engine {g}')
    for row in plain:
        if row not in expected:
            failures += 1
            print(f'lightmapencoding: {profile}: extra record {json.dumps(row)}')
    extra = set(captures) - {row['capture'] for row in expected if 'capture' in row}
    for name in sorted(extra):
        failures += 1
        print(f'lightmapencoding: {profile}: extra capture {name}')
    return len(expected) + seen, failures, seen


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['check'])
    parser.add_argument('--label', default='check')
    parser.add_argument('--profile', action='append', choices=sorted(PROFILES))
    args = parser.parse_args()
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI')
    if not cli:
        parser.error('set UNBOUND or UNBOUND_CLI')
    reference = reference_root(PROBE)
    work = cache_artifact_path(run_root(PROBE, args.label)).resolve()
    if work.exists():
        shutil.rmtree(work)
    total = failed = residual = 0
    residuals = json.loads((PROBE/'residuals.json').read_text())
    for profile in args.profile or list(PROFILES):
        expected_path = reference/f'{profile}.jsonl'
        if not expected_path.is_file():
            print(f'lightmapencoding: {profile}: no reference {expected_path}')
            failed += 1
            continue
        out = work/profile/'out'
        target = project(work/profile/'project', profile)
        env = dict(os.environ, LIGHTMAP_ENCODING_PROBE_OUT=str(out))
        command = [str(Path.home()/'.cache/compat/with-heavy-job'), cli, 'run', '-p', str(work/profile/'project'),
                   '--target', target, '--frames', '30', '--size', '160x90', '--no-watch']
        log_path = work/profile/'engine.log'
        with log_path.open('w') as log:
            if subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=1800).returncode != 0:
                print(f'lightmapencoding: {profile}: engine failed; log {log_path}')
                failed += 1
                continue
        expected = [json.loads(line) for line in expected_path.read_text().splitlines()]
        actual_path = out/'lightmapencoding.jsonl'
        actual = [json.loads(line) for line in actual_path.read_text().splitlines()] if actual_path.is_file() else []
        rows, failures, seen = compare(profile, expected, actual, residuals)
        total += rows
        failed += failures
        residual += seen
        print(f'lightmapencoding: {profile}: {rows - failures - seen}/{rows} records, {seen} residuals')
    print(f'lightmapencoding: {total - failed - residual}/{total} records, {residual} residuals, {failed} failures')
    return int(failed > 0)


if __name__ == '__main__':
    sys.exit(main())
