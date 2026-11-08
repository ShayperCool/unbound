#!/usr/bin/env python3
"""Fixed-step Invoke clocks and sub-frame repeating calls; see README.md."""
import argparse
import json
import os
from pathlib import Path
import shutil
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import value_probe  # noqa: E402
import editor_runner  # noqa: E402
from probe_paths import reference_root, run_root  # noqa: E402

PROBE = Path(__file__).resolve().parent


def publish_edges(labels):
    roots = [run_root(PROBE, label) for label in labels]

    def write(target, first):
        shutil.copyfile(first/'capture/edges.jsonl', target)
        print('published', target)

    return editor_runner.publish(roots, reference_root(PROBE)/'edges.jsonl',
                                 lambda _: ['capture/values.jsonl', 'capture/edges.jsonl'], write,
                                 complete=[editor_runner.jsonl_complete('capture/edges.jsonl')], gpu=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('record', 'publish', 'publish-edges', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label', default=None)
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        return value_probe.record(PROBE, 'InvokeStepsRunner.Record', 'INVOKE_STEPS_OUT', label, False)
    if args.mode == 'publish':
        result = value_probe.publish(PROBE, args.labels)
        return result or publish_edges(args.labels)
    if args.mode == 'publish-edges':
        return publish_edges(args.labels)
    result = value_probe.gate(PROBE, 'INVOKE_STEPS_OUT', label, 0, ['--frames', '400', '--size', '160x90'])
    try:
        expected = value_probe.read_values(reference_root(PROBE)/'edges.jsonl')
        actual = value_probe.read_values(run_root(PROBE, label)/'engine/edges.jsonl')
    except (OSError, ValueError) as error:
        print('invokesteps edges: no complete capture:', error)
        return 1
    matched, failures = value_probe.compare(expected, actual, 0)
    for failure in failures:
        print(json.dumps(failure))
    print(f'invokesteps edges: {matched}/{len(expected)} values match, {len(failures)} failures')
    return int(bool(result or failures))

if __name__ == '__main__':
    sys.exit(main())
