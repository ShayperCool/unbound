#!/usr/bin/env python3
"""Compares an Unbound light probe capture with the Editor's: every key, every value.

    python3 tests/projects/lightprobes/compare.py REFERENCE.jsonl CANDIDATE.jsonl [--report OUT.json]

Values are compared token by token. Numbers must be equal as single-precision floats, except
`pixels ...` (8-bit channels, which may differ by --pixel-tolerance, default 1). Every missing,
extra and differing value is listed; the exit code is the number of failing keys (capped at 100).
.NET's "(Parameter 'x')" exception suffix is compared in Mono's form ("Parameter name: x").
"""
import argparse
import json
import re
import struct
import sys


def mono_message(value):
    """.NET's argument exception suffix in Mono's form (the probe joins lines with spaces):
    the class libraries differ, not the engine."""
    return re.sub(r" \(Parameter '([^']*)'\)", r" Parameter name: \1", value)


def load(path):
    out = {}
    with open(path) as f:
        for line in f:
            if line.strip():
                entry = json.loads(line)
                key = entry['key']
                n = 2
                while key in out:
                    key = f"{entry['key']} #{n}"
                    n += 1
                out[key] = mono_message(entry['value'])
    return out


def number(token):
    try:
        return struct.unpack('f', struct.pack('f', float(token)))[0]
    except (ValueError, OverflowError):
        return None


def compare_value(key, a, b, pixel_tolerance):
    ta, tb = a.split(' '), b.split(' ')
    if len(ta) != len(tb):
        return [f'{len(ta)} tokens vs {len(tb)}']
    problems = []
    tolerance = pixel_tolerance if key.startswith('pixels ') else 0
    for i, (x, y) in enumerate(zip(ta, tb)):
        if x == y:
            continue
        fx, fy = number(x), number(y)
        if fx is None or fy is None:
            problems.append(f'[{i}] {x!r} vs {y!r}')
        elif not (fx == fy or (fx != fx and fy != fy)) and abs(fx - fy) > tolerance:
            problems.append(f'[{i}] {x} vs {y} (diff {fy - fx:.9g})')
    return problems


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('candidate')
    parser.add_argument('--pixel-tolerance', type=float, default=1)
    parser.add_argument('--report')
    args = parser.parse_args()
    ref, cand = load(args.reference), load(args.candidate)
    missing = [k for k in ref if k not in cand]
    extra = [k for k in cand if k not in ref]
    failing = {}
    values = passed = 0
    for key in ref:
        if key not in cand:
            continue
        values += 1
        problems = compare_value(key, ref[key], cand[key], args.pixel_tolerance)
        if problems:
            failing[key] = {'reference': ref[key], 'candidate': cand[key], 'problems': problems}
        else:
            passed += 1
    for key in missing:
        print('MISSING', key)
    for key in extra:
        print('EXTRA', key)
    for key, f in failing.items():
        print('FAIL', key, '|', '; '.join(f['problems'][:6]), '...' if len(f['problems']) > 6 else '')
    print(f'{passed}/{len(ref)} keys pass; {len(failing)} differ, {len(missing)} missing, {len(extra)} extra')
    if args.report:
        with open(args.report, 'w') as f:
            json.dump({'passed': passed, 'total': len(ref), 'missing': missing, 'extra': extra, 'failing': failing}, f, indent=1)
    return min(len(failing) + len(missing) + len(extra), 100)


if __name__ == '__main__':
    sys.exit(main())
