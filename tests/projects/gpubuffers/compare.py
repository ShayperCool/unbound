#!/usr/bin/env python3
"""Compare a gpubuffers capture with the Editor's reference.

    compare.py REFERENCE ACTUAL [--report REPORT.json]

REFERENCE and ACTUAL are capture directories (values.jsonl, timing.jsonl). Every value compares
exactly over the union of keys (buffer words, readback bytes as hex, exceptions, log messages,
event sequences with the request's frame told from later ones); Mono's
argument exception messages are compared in CoreCLR's wording (see normalize). timing.jsonl holds each readback's
events with their exact frames; the reference lists every sequence the Editor captures observed.
When a request completes depends on the GPU (the same request completed one frame after it was
made in one capture and two in another), so an actual sequence passes when it has the events of
an observed one, its frame-0 events are at frame 0, and each later event's frame lies within the
range the Editor showed for that event phase over all requests (u: polled, c: callback). Every missing, extra and failing value is printed and kept in
the report; the exit status is nonzero when anything fails.
"""
import argparse
import json
import re
from pathlib import Path
import sys


def normalize(value):
    """Mono's argument exception messages ("message\\nParameter name: x") as CoreCLR writes them
    ("message (Parameter 'x')"), as tests/projects/jobs compares them."""
    if isinstance(value, str):
        return re.sub(r"\nParameter name: (.*)$", r" (Parameter '\1')", value)
    if isinstance(value, list):
        return [normalize(v) for v in value]
    if isinstance(value, dict):
        return {k: normalize(v) for k, v in value.items()}
    return value


def rows(path):
    result = {}
    for line in path.read_text().splitlines():
        if line.strip():
            row = json.loads(line)
            if row['k'] in result:
                raise SystemExit(f'{path}: duplicate key {row["k"]}')
            result[row['k']] = normalize(row['v'])
    return result


EVENT = re.compile(r'^([a-z])(\d+)(:.*)$')


def phase_ranges(reference):
    """The frames the Editor showed for each phase's later (nonzero-frame) events."""
    ranges = {}
    for sequences in reference.values():
        for seq in sequences:
            for e in seq:
                phase, frame, _ = EVENT.match(e).groups()
                if int(frame) > 0:
                    lo, hi = ranges.get(phase, (int(frame), int(frame)))
                    ranges[phase] = (min(lo, int(frame)), max(hi, int(frame)))
    return ranges


def timing_ok(observed, actual, ranges):
    got = [EVENT.match(e) for e in actual]
    if any(g is None for g in got):
        return False
    got = [g.groups() for g in got]
    shape = [(p, f == '0', st) for p, f, st in got]
    if not any([(p, f == '0', st) for p, f, st in (EVENT.match(e).groups() for e in s)] == shape for s in observed):
        return False
    for phase, frame, _ in got:
        if frame != '0':
            lo, hi = ranges.get(phase, (None, None))
            if lo is None or not lo <= int(frame) <= hi:
                return False
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    report = {'missing': [], 'extra': [], 'failing': [], 'timing': []}
    ref, act = rows(args.reference/'values.jsonl'), rows(args.actual/'values.jsonl')
    for key in ref:
        if key not in act:
            report['missing'].append({'key': key, 'reference': ref[key]})
        elif ref[key] != act[key]:
            report['failing'].append({'key': key, 'reference': ref[key], 'actual': act[key]})
    report['extra'] = [{'key': k, 'actual': act[k]} for k in act if k not in ref]
    ref_t = rows(args.reference/'timing.jsonl')
    ranges = phase_ranges(ref_t)
    act_t = rows(args.actual/'timing.jsonl') if (args.actual/'timing.jsonl').exists() else {}
    for key in sorted(set(ref_t) | set(act_t)):
        if key not in act_t or key not in ref_t or not timing_ok(ref_t[key], act_t[key], ranges):
            report['timing'].append({'key': key, 'reference': ref_t.get(key), 'actual': act_t.get(key)})
    for kind in ('missing', 'extra', 'failing', 'timing'):
        for item in report[kind]:
            print(kind, json.dumps(item)[:600])
    total = len(set(ref) | set(act))
    bad = len(report['missing']) + len(report['extra']) + len(report['failing'])
    print(f'values: {total - bad}/{total} pass ({len(report["missing"])} missing, {len(report["extra"])} extra, '
          f'{len(report["failing"])} failing); timing: {len(set(ref_t) | set(act_t)) - len(report["timing"])}/'
          f'{len(set(ref_t) | set(act_t))} within the Editor frames')
    if args.report:
        args.report.write_text(json.dumps(report, indent=1))
    return int(bad > 0 or len(report['timing']) > 0)


if __name__ == '__main__':
    sys.exit(main())
