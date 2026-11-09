#!/usr/bin/env python3
"""Run the engine and require every line and trail record to match, except the open residuals.

The open residuals stay failing in comparison.json and every failing component is retained
in components.json. No tolerance or existing assertion is changed.
"""
import argparse
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import time

import compare
import run


def components(reference, capture):
    """All failing leaves, including missing/extra values and every failing image channel."""
    out = {}

    def walk(a, b, path):
        if isinstance(a, dict) and isinstance(b, dict):
            for k in sorted(a.keys() | b.keys()):
                if k not in a or k not in b:
                    out[path+'.'+k] = dict(reference=a.get(k), actual=b.get(k),
                                           kind='extra' if k not in a else 'missing')
                else:
                    walk(a[k], b[k], path+'.'+k)
        elif isinstance(a, list) and isinstance(b, list):
            if len(a) != len(b):
                out[path+'.length'] = dict(reference=len(a), actual=len(b))
            for i in range(max(len(a), len(b))):
                at = f'{path}[{i}]'
                if i >= len(a) or i >= len(b):
                    out[at] = dict(reference=a[i] if i < len(a) else None,
                                   actual=b[i] if i < len(b) else None,
                                   kind='extra' if i >= len(a) else 'missing')
                else:
                    walk(a[i], b[i], at)
        elif compare.differ(a, b):
            out[path] = dict(reference=a, actual=b)

    ref = compare.load_reference(reference, 'miter')
    cap = compare.load(capture)
    walk(ref, cap, '')
    for k in ref.keys() & cap.keys():
        if not k.endswith('.pixels'):
            continue
        name = k.removesuffix('.pixels')
        a, b = compare.image(reference, name), compare.image(capture, name)
        if a is None or b is None or len(a) != len(b):
            out[k+'.image'] = dict(kind='image missing or of another size')
        else:
            for i, (x, y) in enumerate(zip(a, b)):
                if abs(x-y) > compare.PIXEL:
                    out[f'{name}.pixel[{i//4}].channel[{i%4}]'] = dict(reference=x, actual=y)
    return out


# Open residuals (docs/COMPATIBILITY_GAPS.md, LineRenderer/TrailRenderer geometry): the probe's
# frame count during trails (player-loop scope) and one byte of a perceptual gradient (the
# gradient's sRGB conversion). Every other key, missing or extra key fails the gate.
OPEN = re.compile(r'trail\.f\d+\.time|line\.color\.PerceptualBlend')


def open_residual(key):
    return OPEN.fullmatch(key) is not None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', default=os.environ.get('UNBOUND_PROBE_UNITY_VERSION'), required=False)
    parser.add_argument('--capture', type=Path)
    parser.add_argument('--before', type=Path)
    args = parser.parse_args()
    if not args.release:
        parser.error('select --release or UNBOUND_PROBE_UNITY_VERSION')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    if not args.capture:
        cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
        if not cli:
            parser.error('set UNBOUND_CLI or UNBOUND to this checkout build')
        os.environ['UNBOUND_CLI'] = cli
        label = 'miter-check-'+str(time.time_ns())
        code = run.unbound(run.run_root(run.PROBE, label))
        if code:
            return code
        args.capture = run.run_root(run.PROBE, label)/'unbound'
    from probe_paths import cache_artifact_path
    cache_artifact_path(args.capture)
    reference = run.reference_root(run.PROBE)
    report = args.capture/'comparison.json'
    command = [sys.executable, str(run.PROBE/'compare.py'), str(reference), str(args.capture),
               '--mode', 'miter', '--report', str(report)]
    if args.before:
        command += ['--before', str(args.before)]
    code = subprocess.run(command).returncode
    if code not in (0, 1) or not report.is_file():
        return code or 1
    full = json.loads(report.read_text())
    detail = dict(after=components(reference, args.capture))
    if args.before:
        detail['before'] = components(reference, args.before)
        detail['introduced'] = sorted(detail['after'].keys() - detail['before'].keys())
        detail['resolved'] = sorted(detail['before'].keys() - detail['after'].keys())
    (args.capture/'components.json').write_text(json.dumps(detail, indent=1)+'\n')
    failures = full['failures']
    bad = [k for k, v in failures.items() if not open_residual(k) or v in ('missing', 'extra')]
    cap = compare.load(args.capture)
    if cap.get('complete') is not True or any(k == 'fatal' or k.endswith('.fatal') for k in cap):
        bad.append('incomplete capture')
    print(f'lines gate: {len(bad)} failures; full strict report retains {full["failing"]} failing keys: {report}')
    return int(bool(bad))


if __name__ == '__main__':
    sys.exit(main())
