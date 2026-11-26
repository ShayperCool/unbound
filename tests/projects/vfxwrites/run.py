#!/usr/bin/env python3
"""What the Editor writes into a Visual Effect Graph's file, and when; see README.md.

    run.py record --label L     one Editor capture (batch mode, Edit mode)
    run.py publish A B          two identical captures -> Reference/unity-<release>/values.jsonl
    run.py gate [--label L]     Unbound (unbound test, Edit mode) on a fresh copy, compared with it
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
sys.dont_write_bytecode = True  # nothing is written into the checkout
import editor_runner  # noqa: E402
import value_probe  # noqa: E402
from probe_paths import run_root  # noqa: E402

VARIABLE = 'VFX_WRITES_OUT'
# Unbound runs the probe as an Edit-mode test; the entry is written into the run's copy only (the
# Editor's project has no test framework).
ENTRY = ('using NUnit.Framework;\n'
         'public static class VfxWritesEntry { [Test] public static void Run() { VfxWritesProbe.Record(); } }\n')


GRAPH = 'Assets/Graphs/BoundsAuto.vfx'
CREATED = 'Assets/Graphs/Created.vfx'


def with_exit_row(values_path, project):
    """The capture's rows with `exit/initialEventName`, the graph's initial event as the file holds
    it after the run ended (the probe left that setting unsaved for the exit), and `exit/created
    documents`, the created graph's document count after the run (its model was left edited), and
    `exit/will save`, the paths the processor got from the exit's save (a line per call)."""
    lines = Path(values_path).read_text().splitlines()
    event = next((line.split(':', 1)[1].strip() for line in (project/GRAPH).read_text().splitlines()
                  if line.strip().startswith('m_InitialEventName:')), '<none>')
    documents = sum(line.startswith('--- ') for line in (project/CREATED).read_text().splitlines())
    calls = Path(values_path).parent/'exit-will-save.txt'
    # The Editor's order of several paths differs from run to run: each call's paths as a set.
    hook = ' | '.join(', '.join(sorted(line.split(', '))) for line in calls.read_text().splitlines()) if calls.exists() else '<no call>'
    rows = [json.dumps({'k': 'exit/initialEventName', 'v': event}), json.dumps({'k': 'exit/created documents', 'v': str(documents)}),
            json.dumps({'k': 'exit/will save', 'v': hook})]
    if lines and lines[-1].startswith('{"k": "complete"'):
        lines[len(lines) - 1:len(lines) - 1] = rows
    Path(values_path).write_text('\n'.join(lines) + '\n')


def record(label):
    out = run_root(PROBE, label)
    project = value_probe.assemble(PROBE, out)
    capture = out/'capture'
    capture.mkdir(parents=True)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'VfxWritesProbe.RecordAndExit'], batch=True,
                                  graphics=True, env={VARIABLE: str(capture)}, timeout=1800, label='vfxwrites-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True) or editor_runner.jsonl_complete()(capture)
    if problem:
        raise SystemExit(f'{out}: {problem}')
    with_exit_row(capture/value_probe.VALUES, project)
    print('captured', capture)
    return 0


def gate(label):
    known = value_probe.residuals(PROBE)
    out = run_root(PROBE, label)
    project = value_probe.assemble(PROBE, out)
    (project/'Assets/Editor/VfxWritesEntry.cs').write_text(ENTRY)
    capture = out/'engine'
    capture.mkdir(parents=True)
    with (out/'engine.log').open('w') as log:
        code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', value_probe.engine_cli(), 'test',
                               '--graphics', '-p', str(project), '--platform', 'EditMode', '--filter', 'VfxWritesEntry.Run'],
                              env={**os.environ, VARIABLE: str(capture)}, stdout=log, stderr=subprocess.STDOUT).returncode
    if (capture/value_probe.VALUES).is_file():
        with_exit_row(capture/value_probe.VALUES, project)
    expected = value_probe.read_values(value_probe.reference_dir(PROBE)/value_probe.VALUES)
    try:
        actual = value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        print(f'vfxwrites: no complete engine capture ({error}); engine exit {code}; log {out}/engine.log')
        return 1
    failures, residual = 0, 0
    for key in sorted(expected.keys() | actual.keys()):
        if expected.get(key) == actual.get(key):
            continue
        reason = known.get(key)
        print(f'{"known" if reason else "DIFF"} {key}\n  expected: {expected.get(key)!r}\n  actual:   {actual.get(key)!r}')
        if reason:
            residual += 1
        else:
            failures += 1
    matched = sum(1 for k in expected if expected[k] == actual.get(k))
    print(f'vfxwrites: {matched}/{len(expected)} values match, {failures} failures, {residual} known residuals; engine exit {code}; logs {out}')
    return 1 if failures or code else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        return record(label)
    if args.mode == 'publish':
        return value_probe.publish(PROBE, args.labels)
    return gate(label)


if __name__ == '__main__':
    sys.exit(main())
