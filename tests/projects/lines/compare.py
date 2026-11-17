#!/usr/bin/env python3
"""Compares a line/trail probe capture with the Unity reference.

    compare.py <reference dir> <capture dir> [--report out.json] [--before <capture dir>]
               [--mode canonical|capcount|miter|shadow] [--residuals RESIDUALS.json]

Keeps the whole key union: every missing, extra and differing key is reported. Numbers match
within an absolute 1e-5 plus a relative 1e-5 (float geometry); strings, booleans, list lengths
and object keys match exactly. Images (`<key>.rgba`, float RGBA, in the reference as `.rgba.gz`)
match when no channel differs by more than 2/255; the report counts the differing pixels.
With --before, each failure is classified as retained (the before capture fails it too),
introduced (it passed before) or resolved (failed before, passes now).

--mode selects the reference records: `canonical` (default) reads values.jsonl alone;
`capcount` also reads the verified capcount.values.jsonl control file of the same release
directory (see capcount_layout.py). The capture is always read whole.
`miter` also includes the disjoint reversal controls and their image.
`shadow` adds the opt-in shadow-caster diagnostics, with the same strict comparison.
With --residuals (tests/residuals.py) the exit status is 0 when every failing key is listed there
within its bounds: a value's differing leaves "values" and their largest difference "max_abs"
(values of another shape: the capture's "actual"), an image's differing "pixels" and "max_delta".
"""
import argparse
import gzip
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import residuals as residual_lists  # noqa: E402

ABS = 1e-5
REL = 1e-5
PIXEL = 2.0/255.0


def load(directory):
    rows = {}
    for line in (Path(directory)/'values.jsonl').read_text().splitlines():
        if line.strip():
            row = json.loads(line)
            rows[row['k']] = row['v']
    return rows


def image(directory, key):
    d = Path(directory)
    raw = d/(key+'.rgba')
    if raw.exists():
        data = raw.read_bytes()
    elif (d/(key+'.rgba.gz')).exists():
        data = gzip.decompress((d/(key+'.rgba.gz')).read_bytes())
    else:
        return None
    return struct.unpack('<%df' % (len(data)//4), data)


def differ(a, b, path=''):
    """The first difference (path, description, size) or None."""
    if isinstance(a, bool) or isinstance(b, bool) or isinstance(a, str) or isinstance(b, str) or a is None or b is None:
        return None if a == b else (path, f'{a!r} != {b!r}', math.inf)
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        d = abs(a-b)
        return None if d <= ABS+REL*abs(a) else (path, f'{a!r} != {b!r}', d)
    if isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            return (path, f'length {len(a)} != {len(b)}', math.inf)
        worst = None
        for i, (x, y) in enumerate(zip(a, b)):
            r = differ(x, y, f'{path}[{i}]')
            if r and (worst is None or r[2] > worst[2]):
                worst = r
        return worst
    if isinstance(a, dict) and isinstance(b, dict):
        if set(a) != set(b):
            return (path, f'keys {sorted(set(a) ^ set(b))}', math.inf)
        worst = None
        for k in a:
            r = differ(a[k], b[k], f'{path}.{k}')
            if r and (worst is None or r[2] > worst[2]):
                worst = r
        return worst
    return (path, f'{type(a).__name__} != {type(b).__name__}', math.inf)


def differing(a, b):
    """How many leaves of two values of the same shape differ beyond the tolerance."""
    if isinstance(a, list) and isinstance(b, list):
        return sum(differing(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return sum(differing(a[k], b[k]) for k in a)
    return 1 if differ(a, b) else 0


def load_reference(directory, mode='canonical'):
    if mode == 'canonical':
        return load(directory)
    if mode in ('miter', 'shadow'):
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from miter_reference import load as load_miter
        return load_miter(directory, shadows=mode == 'shadow')
    spec = importlib.util.spec_from_file_location('capcount_layout', Path(__file__).with_name('capcount_layout.py'))
    layout = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(layout)
    return layout.load_reference(directory, mode)


def failures(reference, capture, mode='canonical', measures=None):
    """Every failing key with its description, and the size of the key union. `measures`, when
    given, receives what a residual list bounds for each differing value and image."""
    measures = {} if measures is None else measures
    ref, cap = load_reference(reference, mode), load(capture)
    out = {}
    for k in sorted(set(ref) | set(cap)):
        if k not in cap:
            out[k] = 'missing'
            continue
        if k not in ref:
            out[k] = 'extra'
            continue
        if k.endswith('.pixels'):
            name = k[:-len('.pixels')]
            a, b = image(reference, name), image(capture, name)
            if a is None or b is None or len(a) != len(b):
                out[k] = 'image missing or of another size'
                continue
            bad = sum(1 for i in range(0, len(a), 4) if any(abs(a[i+c]-b[i+c]) > PIXEL for c in range(4)))
            if bad:
                out[k] = f'{bad} of {len(a)//4} pixels differ by more than 2/255'
                measures[k] = {'pixels': bad, 'max_delta': max(abs(x-y) for x, y in zip(a, b))}
            continue
        r = differ(ref[k], cap[k])
        if r:
            out[k] = f'{r[0]}: {r[1]}'
            measures[k] = ({'values': differing(ref[k], cap[k]), 'max_abs': r[2]} if math.isfinite(r[2])
                           else {'actual': cap[k]})
    return out, len(set(ref) | set(cap))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--before')
    parser.add_argument('--report')
    parser.add_argument('--mode', choices=('canonical', 'capcount', 'miter', 'shadow'), default='canonical')
    parser.add_argument('--residuals')
    args = parser.parse_args()
    listed = residual_lists.load(args.residuals)
    measures = {}
    now, union = failures(args.reference, args.capture, args.mode, measures)
    report = {'mode': args.mode, 'keys': union, 'failing': len(now), 'failures': now}
    if args.before:
        before, _ = failures(args.reference, args.before, args.mode)
        report['retained'] = sorted(k for k in now if k in before)
        report['introduced'] = sorted(k for k in now if k not in before)
        report['resolved'] = sorted(k for k in before if k not in now)
        report['before_failing'] = len(before)
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=1)+'\n')
    for k, v in now.items():
        print(f'{k}: {v}')
    summary = f'{union - len(now)}/{union} keys match'
    if args.before:
        summary += f"; before {union - report['before_failing']}/{union}; introduced {len(report['introduced'])}, resolved {len(report['resolved'])}, retained {len(report['retained'])}"
    if args.residuals:
        verdict = residual_lists.Gate(listed)
        reference = load_reference(args.reference, args.mode)
        for k in sorted(set(reference) | set(now)):
            if k not in now:
                verdict.exact_key(k)
            elif k in measures:
                verdict.differs(k, measures[k])
            else:
                verdict.missing(k, now[k])
        verdict.finish(reference)
        print('\n'.join(verdict.lines()))
    print(summary)
    if args.residuals:
        return 0 if verdict.ok else 1
    return 1 if now else 0


if __name__ == '__main__':
    sys.exit(main())
