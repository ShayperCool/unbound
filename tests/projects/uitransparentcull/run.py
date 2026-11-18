#!/usr/bin/env python3
"""Canvas renderers left out for transparency; see README.md.

    run.py record  --release R [--label L]   two windowed Editor captures (GPU), L/editor1 and L/editor2
    run.py publish --release R [--label L]   two identical captures -> Reference/unity-R/values.jsonl
    run.py gate    --release R [--label L]   an engine capture compared with the reference

Every value is a byte colour (or a short string) and must be equal: missing, extra and differing
keys are all reported and all fail.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, reference_root, run_root  # noqa: E402
from editor_runner import EditorJob, run, publish, result_problem  # noqa: E402

OUT = 'values.jsonl'
SIZE = '256x256'


def copy_project(destination, release):
    if destination.exists():
        raise SystemExit(f'refusing to overwrite project: {destination}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, destination/part)
    (destination/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    return destination


def rows(path):
    records = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    keys = [r['k'] for r in records]
    if len(keys) != len(set(keys)):
        raise ValueError('duplicate keys')
    return {r['k']: r['v'] for r in records}


def complete(directory):
    path = Path(directory)/OUT
    if not path.is_file():
        return f'missing {OUT}'
    try:
        got = rows(path)
    except (OSError, ValueError) as error:
        return f'malformed {OUT}: {error}'
    if got.get('complete') is not True:
        return 'capture did not complete'
    if got.get('overlay.screen') != SIZE:
        return f'overlay pass ran on a {got.get("overlay.screen")} screen, not {SIZE}'
    return None


def compare(reference, actual):
    want, got = rows(reference), rows(actual)
    failures = 0
    for key in sorted(set(want) | set(got)):
        if key not in got:
            print(f'missing {key}: reference {json.dumps(want[key])}')
        elif key not in want:
            print(f'extra {key}: engine {json.dumps(got[key])}')
        elif want[key] != got[key]:
            print(f'differs {key}: reference {json.dumps(want[key])} engine {json.dumps(got[key])}')
        else:
            continue
        failures += 1
    print(f'uitransparentcull: {len(want) - failures}/{len(want)} values match, {failures} failing')
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['record', 'publish', 'gate'])
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', default='current')
    args = parser.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    reference = reference_root(PROBE)/OUT
    work = cache_artifact_path(run_root(PROBE, args.label)).resolve()
    work.mkdir(parents=True, exist_ok=True)
    if args.command == 'record':
        for index in (1, 2):
            out = work/f'editor{index}'
            project = copy_project(out/'project', args.release)
            result = run(EditorJob(project, out/'editor.log', out, args=['-executeMethod', 'ProbeRunner.Record'],
                                   env={'UI_CULL_OUT': str(out)}, timeout=600))
            reason = result_problem(out, gpu=True) or complete(out)
            if reason:
                print(f'Editor capture {index} failed: {reason} ({result.get("exit_code")})', file=sys.stderr)
                return 1
        return 0
    if args.command == 'publish':
        def write(target, first):
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(first/OUT, target)
        return publish([work/'editor1', work/'editor2'], reference, lambda directory: [OUT], write,
                       complete=[complete], gpu=True)
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI')
    if not cli:
        parser.error('set UNBOUND or UNBOUND_CLI')
    if not reference.is_file():
        print(f'uitransparentcull: no reference for {args.release} ({reference})')
        return 1
    out = work/'engine'
    project = copy_project(out/'project', args.release)
    launcher = Path.home()/'.cache/compat/with-heavy-job'
    command = ([str(launcher), '--kind', 'engine-gpu'] if launcher.is_file() else []) + \
        [cli, 'run', '-p', str(project), '--frames', '60', '--size', SIZE, '--no-watch']
    with (out/'engine.log').open('w') as log:
        try:
            code = subprocess.run(command, env=dict(os.environ, UI_CULL_OUT=str(out)), stdout=log,
                                  stderr=subprocess.STDOUT, timeout=600).returncode
        except subprocess.TimeoutExpired:
            code = 'timeout'
    reason = complete(out)
    if code != 0 or reason:
        print(f'uitransparentcull: engine exit {code}, {reason or "capture written"}; see {out}')
        return 1
    return 1 if compare(reference, out/OUT) else 0


if __name__ == '__main__':
    sys.exit(main())
