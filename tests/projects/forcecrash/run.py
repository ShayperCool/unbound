#!/usr/bin/env python3
"""Utils.ForceCrash in each category: the lines printed before the crash and the signal the process
dies of. The Editor crashes, so each category is its own run; see README.md.

    run.py record LABEL      one Linux GPU Editor run per category (Linux only)
    run.py publish A B       two identical records become the reference
    run.py gate              one engine run per category, compared with the reference
"""
import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402
from probe_paths import run_root, reference_root  # noqa: E402
import editor_runner  # noqa: E402

CATEGORIES = ['AccessViolation', 'FatalError', 'Abort', 'PureVirtualFunction', 'MonoAbort']
MARKER = 'ForceCrashProbe: forcing '
# Where the crash handler's report or the shutdown starts: the lines before it are ForceCrash's.
ENDS = ('Caught fatal signal', 'Application is shutting down', '=====', 'Native Crash Reporting')
# Stack and location lines of logged messages, in either stack format: only what ForceCrash itself
# prints is compared.
STACK = ('UnityEngine.', 'ForceCrashProbe.', 'System.', 'Unbound.', '(Filename:', '[Assets/', '#', 'Obtained ', 'at ')


def crash_lines(log_text, category):
    """The lines logged after the probe's marker, up to the crash report, without stacks."""
    lines = log_text.splitlines()
    marker = MARKER + category
    start = next((i for i, line in enumerate(lines) if line.strip().endswith(marker)), None)
    if start is None:
        return None
    found = []
    for line in lines[start + 1:]:
        text = line.strip()
        if text.startswith(ENDS) or '\x1b' in line:
            break
        if not text or text.startswith(STACK):
            continue
        found.append(text)
    return ' | '.join(found)


def summary(category, log_text, capture, death):
    values = {
        f'{category}.before': (capture/'before.txt').read_text() if (capture/'before.txt').exists() else 'missing',
        f'{category}.returned': (capture/'after.txt').exists(),
        f'{category}.lines': crash_lines(log_text, category),
    }
    if death:
        values[f'{category}.signal'] = death
    return values


def write(path, values):
    with path.open('w') as out:
        for key, value in values.items():
            out.write(json.dumps({'k': key, 'v': value}) + '\n')
        out.write(json.dumps({'k': 'complete', 'v': True}) + '\n')


def read(path):
    rows = [json.loads(line) for line in path.read_text().splitlines()]
    return {row['k']: row['v'] for row in rows}


def record(label):
    if sys.platform != 'linux':
        raise SystemExit('forcecrash records only with the Linux GPU Editor')
    root = run_root(PROBE, label)
    values = {}
    for category in CATEGORIES:
        out = root/category
        project = value_probe.assemble(PROBE, out)
        capture = out/'capture'
        capture.mkdir(parents=True)
        job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ForceCrashRunner.Record'],
                                      batch=False, graphics=True, label=f'forcecrash-{label}-{category}',
                                      # FatalError never exits by itself: the watchdog ends it.
                                      timeout=120 if category == 'FatalError' else 300,
                                      env={'FORCE_CRASH_OUT': str(capture), 'FORCE_CRASH_CATEGORY': category})
        result = editor_runner.run(job)
        print(editor_runner.summary(result))
        # A forced end (the watchdog) is the runner's, not the Editor's: no signal is recorded.
        death = None if result.get('forced_exit') else result.get('signal')
        values.update(summary(category, (out/'editor.log').read_text(errors='replace'), capture, death))
    write(root/'values.jsonl', values)
    print('recorded', root/'values.jsonl')


def publish(labels):
    captures = [read(run_root(PROBE, label)/'values.jsonl') for label in labels]
    if len(captures) < 2 or any(c != captures[0] for c in captures[1:]):
        raise SystemExit('publishing needs at least two identical records')
    target = reference_root(PROBE)
    if target.exists():
        raise SystemExit(f'refusing to overwrite {target}')
    target.mkdir(parents=True)
    write(target/'values.jsonl', {k: v for k, v in captures[0].items() if k != 'complete'})
    print('published', target)


def gate():
    expected = read(reference_root(PROBE)/'values.jsonl')
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(PROBE.parents[2]/'target/release/unbound')
    root = run_root(PROBE, time.strftime('check-%Y%m%d-%H%M%S') + f'-{os.getpid()}')
    actual = {}
    for category in CATEGORIES:
        out = root/category
        project = value_probe.assemble(PROBE, out)
        capture = out/'capture'
        capture.mkdir(parents=True)
        env = {**os.environ, 'FORCE_CRASH_OUT': str(capture), 'FORCE_CRASH_CATEGORY': category}
        with (out/'engine.log').open('w') as log:
            try:
                code = subprocess.run([cli, 'run', '-p', str(project), '--no-watch', '--headless', '120'], env=env,
                                      stdout=log, stderr=subprocess.STDOUT, timeout=300).returncode
            except subprocess.TimeoutExpired:
                code = None
        death = signal.Signals(-code).name if code is not None and code < 0 else None
        console = project/'Library'/'Unbound'/'Editor.log'
        log_text = console.read_text(errors='replace') if console.exists() else ''
        actual.update(summary(category, log_text, capture, death))
        print(f'{category}: engine exit {code} ({death or "no signal"})')
    failures = []
    for key, value in expected.items():
        if key == 'complete':
            continue
        if actual.get(key) != value:
            failures.append({'key': key, 'expected': value, 'actual': actual.get(key)})
    for failure in failures:
        print(json.dumps(failure))
    matched = len(expected) - 1 - len(failures)
    print(f'forcecrash: {matched}/{len(expected) - 1} values match, {len(failures)} failures; logs {root}')
    return 1 if failures else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=['record', 'publish', 'gate'])
    parser.add_argument('labels', nargs='*')
    args = parser.parse_args()
    if args.mode == 'record':
        return record(args.labels[0])
    if args.mode == 'publish':
        return publish(args.labels)
    return gate()


if __name__ == '__main__':
    sys.exit(main())
