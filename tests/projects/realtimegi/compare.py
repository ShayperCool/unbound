#!/usr/bin/env python3
"""Compares a realtimegi capture (engine) with the Unity reference.

    compare.py REFERENCE CAPTURE [--log ENGINE_LOG] [--strict]

Per case of values.jsonl: the renderers' realtime lightmap index and scale/offset and the
lightmaps mode must be equal; light probe SH (each sample point, 27 coefficients) and the ambient
probe within ABS + REL * |reference|. Per atlas (.atlas/.dir): the covered texels must be the
same, and each covered texel's RGB within ABS + REL * |reference|. Frames (.png) report the shared image
measure (mean, worst and pixels over 8 of the per-pixel max-channel difference) only. Every failing value is listed (no truncation of the counts).

Without --strict the comparison is a report (exit 0): Unbound does not draw precomputed realtime
GI yet, so the atlases and probes are expected to differ. With --strict any failure exits 1.
"""
import argparse
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from value_probe import image_difference  # noqa: E402

ABS = 0.02
REL = 0.05


def records(path):
    out = {}
    for line in Path(path).read_text().splitlines():
        if line.strip():
            record = json.loads(line)
            out[record['case']] = record
    return out


def atlas(path):
    data = Path(path).read_bytes()
    width, height = struct.unpack_from('<ii', data)
    count = width * height * 4
    values = struct.unpack_from('<%de' % count, data, 8)
    return width, height, values


def close(a, b):
    return abs(a - b) <= ABS + REL * abs(b)


def compare_values(ref, cap, failures):
    for name, r in ref.items():
        c = cap.get(name)
        if c is None:
            failures.append(f'{name}: missing')
            continue
        for key in ('lightmapsMode',):
            if r.get(key) != c.get(key):
                failures.append(f'{name}: {key} {c.get(key)} != {r.get(key)}')
        if 'renderers' in r:
            if [(x['name'], x['index']) for x in r['renderers']] != [(x['name'], x['index']) for x in c.get('renderers', [])]:
                failures.append(f'{name}: renderers/indices {c.get("renderers")} != {r["renderers"]}')
            else:
                for x, y in zip(r['renderers'], c['renderers']):
                    if any(abs(a - b) > 1e-6 for a, b in zip(x['st'], y['st'])):
                        failures.append(f'{name}: {x["name"]} st {y["st"]} != {x["st"]}')
        for key in ('probes', 'ambientProbe'):
            if key not in r:
                continue
            rv, cv = r[key], c.get(key)
            if key == 'ambientProbe':
                rv, cv = [rv], [cv] if cv is not None else None
            if cv is None or len(cv) != len(rv):
                failures.append(f'{name}: {key} missing or of another length')
                continue
            for i, (rs, cs) in enumerate(zip(rv, cv)):
                bad = [k for k, (a, b) in enumerate(zip(cs, rs)) if not close(a, b)]
                if bad:
                    worst = max(bad, key=lambda k: abs(cs[k] - rs[k]))
                    failures.append(f'{name}: {key}[{i}] {len(bad)} coefficients off, worst [{worst}] '
                                    f'{cs[worst]:.5g} vs {rs[worst]:.5g}')
        if 'baked' in r:
            if r.get('count') != c.get('count') or len(r['baked']) != len(c.get('baked', [])):
                failures.append(f'{name}: {c.get("count")} probes with {len(c.get("baked", []))} baked values '
                                f'!= {r.get("count")} with {len(r["baked"])}')
            else:
                bad = sum(not close(a, b) for a, b in zip(c['baked'], r['baked']))
                if bad:
                    failures.append(f'{name}: {bad} baked coefficients off')
        for key in ('converged', 'indirectScale', 'updateThreshold', 'size'):
            if key in r and r[key] != c.get(key):
                failures.append(f'{name}: {key} {c.get(key)} != {r[key]}')


def compare_atlas(name, ref_path, cap_path, failures, lines):
    if not cap_path.is_file():
        failures.append(f'{name}: missing')
        return
    rw, rh, rv = atlas(ref_path)
    cw, ch, cv = atlas(cap_path)
    if (rw, rh) != (cw, ch):
        failures.append(f'{name}: size {cw}x{ch} != {rw}x{rh}')
        return
    coverage = bad = 0
    total = worst = 0.0
    for t in range(rw * rh):
        ra, ca = rv[4 * t + 3] > 0, cv[4 * t + 3] > 0
        if ra != ca:
            coverage += 1
            continue
        if not ra:
            continue
        for k in range(3):
            a, b = cv[4 * t + k], rv[4 * t + k]
            d = abs(a - b)
            total += d
            worst = max(worst, d)
            if not close(a, b):
                bad += 1
    lines.append(f'{name}: coverage differs at {coverage} texels, {bad} values off, '
                 f'mean |d| {total / max(1, rw * rh):.5f}, max |d| {worst:.5f}')
    if coverage:
        failures.append(f'{name}: coverage differs at {coverage} texels')
    if bad:
        failures.append(f'{name}: {bad} texel values off (max |d| {worst:.5f})')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--log')
    parser.add_argument('--strict', action='store_true')
    args = parser.parse_args()
    ref, cap = Path(args.reference), Path(args.capture)
    failures, lines = [], []
    if not (cap/'values.jsonl').is_file():
        print(f'realtimegi: no values.jsonl in {cap}')
        return 1
    compare_values(records(ref/'values.jsonl'), records(cap/'values.jsonl'), failures)
    for path in sorted(ref.iterdir()):
        if path.suffix in ('.atlas', '.dir'):
            compare_atlas(path.name, path, cap/path.name, failures, lines)
        elif path.suffix == '.png':
            if not (cap/path.name).is_file():
                failures.append(f'{path.name}: missing')
                continue
            diff = image_difference(path, cap/path.name)
            lines.append(f'{path.name}: images mean {diff[0]:.2f} worst {diff[1]} over8 {diff[2]}'
                         if diff is not None else f'{path.name}: size differs')
    for line in lines:
        print(line)
    for failure in failures:
        print('FAIL', failure)
    print(f'realtimegi: {len(failures)} failing' + ('' if args.strict else ' (report)'))
    return 1 if failures and args.strict else 0


if __name__ == '__main__':
    sys.exit(main())
