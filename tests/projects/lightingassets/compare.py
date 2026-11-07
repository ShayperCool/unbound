#!/usr/bin/env python3
"""Compares Unbound's lightingassets capture with Unity's reference.

    compare.py REFERENCE_DIR CANDIDATE_DIR [--log ENGINE_LOG]

values.jsonl: every record must equal the reference's, except the RESIDUALS below, which are
reported (with both values) and must still differ as recorded: one that starts to match is
reported too, so the list is kept current. Images: the mean over pixels of the largest channel
difference (tests/projects/particles/compare_images.py) at most MEAN_LIMIT for the frames
Unbound draws as Unity does; the frames lit by Enlighten Realtime GI are reported only, and the
engine log must hold the warning that says Realtime GI is not drawn.
"""
import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
# The keyword checks of ShaderVariantCollection.ShaderVariant's constructor that need each pass's
# own keyword sets (which pass declares a keyword, which keywords one multi_compile set holds):
# Unbound checks keywords against the shader's whole keyword space only.
RESIDUALS = {
    ('variant', 'plain other pass keyword'),
    ('variant', 'forward PLAIN_ON'),
    ('variant', 'plain ONE_A ONE_B'),
}
# Frames Unbound draws as Unity does; the realtime ones carry Enlighten's bounced light, which
# Unbound does not draw (it warns instead).
GATED_IMAGES = ['lit-authored', 'lit-edited', 'lit-assigned', 'realtimeoff-red']
REPORTED_IMAGES = ['realtime-red', 'realtime-blue']
MEAN_LIMIT = 0.5
REALTIME_WARNING = re.compile(r'Enlighten Realtime Global Illumination precomputed for \d+ renderers')


def values(reference, candidate):
    ref = [json.loads(line) for line in (reference/'values.jsonl').read_text().splitlines() if line.strip()]
    path = candidate/'values.jsonl'
    if not path.is_file():
        print('values.jsonl: MISSING')
        return 1
    got = [json.loads(line) for line in path.read_text().splitlines() if line.strip()]
    failures = 0
    if len(ref) != len(got):
        print(f'values: {len(got)} records, the reference has {len(ref)}')
        failures += 1
    seen = set()
    for i, (r, g) in enumerate(zip(ref, got)):
        k = (r.get('case'), r.get('name') or r.get('step'))
        if r == g:
            if k in RESIDUALS:
                print(f'residual now matches, update RESIDUALS: {k}')
                failures += 1
            continue
        if k in RESIDUALS:
            seen.add(k)
            print(f'residual {k}: unity {json.dumps(r.get("result"))} unbound {json.dumps(g.get("result"))}')
            continue
        print(f'line {i + 1} {k}:\n  unity  {json.dumps(r)}\n  unbound {json.dumps(g)}')
        failures += 1
    for k in sorted(RESIDUALS - seen, key=str):
        if not any((r.get('case'), r.get('name') or r.get('step')) == k for r in ref):
            print(f'residual {k} is not in the reference')
            failures += 1
    print(f'values: {len(ref)} records, {failures} failing, {len(seen)} recorded residuals')
    return failures


def images(reference, candidate):
    out = subprocess.run([sys.executable, str(HERE.parent/'particles/compare_images.py'), str(reference), str(candidate)],
                         capture_output=True, text=True)
    means = {}
    for line in out.stdout.splitlines():
        m = re.match(r'(\S+): mean ([0-9.]+|inf)', line)
        if m:
            means[m.group(1)] = float(m.group(2))
        elif line.endswith(': MISSING'):
            means[line.split(':')[0]] = float('inf')
    failures = 0
    for name in GATED_IMAGES + REPORTED_IMAGES:
        mean = means.get(name, float('inf'))
        gated = name in GATED_IMAGES
        bad = gated and not mean <= MEAN_LIMIT
        failures += bad
        print(f'image {name}: mean {mean:.2f}{" FAIL" if bad else "" if gated else " (reported: Realtime GI is not drawn)"}')
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference', type=Path)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--log', type=Path)
    args = parser.parse_args()
    failures = values(args.reference, args.candidate) + images(args.reference, args.candidate)
    if args.log is not None:
        text = args.log.read_text(errors='replace') if args.log.is_file() else ''
        if not REALTIME_WARNING.search(text):
            print('engine log: no warning that Realtime GI is not drawn')
            failures += 1
    print(f'lightingassets: {failures} failing')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
