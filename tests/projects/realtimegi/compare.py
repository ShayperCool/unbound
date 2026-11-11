#!/usr/bin/env python3
"""Compares a realtimegi capture (engine) with the Unity reference.

    compare.py REFERENCE CAPTURE [--strict]

Two classes of values:

- State, always gated (exit 1 on any difference): every reference case present, no extra case,
  each renderer's realtime lightmap index and scale/offset, the lightmaps mode, the light probe
  count and baked coefficients, DynamicGI.isConverged, the recorded API defaults, what
  DynamicGI.SetEnvironmentData throws and which atlas texels renderers cover (their realtime
  lightmap UVs).
- Lighting: light probe SH (each sample point, 27 coefficients) and the ambient probe within
  ABS + REL * |reference|; per atlas (.atlas/.dir) each covered texel's RGB within ABS + REL *
  |reference|; frames (.png) report the shared image measure (mean, worst and pixels over 8 of
  the per-pixel max-channel difference). A directionality (.dir) compares its fourth channel
  too and is gated: every value within that bound. The realtime lightmaps (.atlas) are gated: at most
  ISOLATED of their values may be outside the tolerance, each within ABS + ISOLATED_REL *
  |reference|, and a missing lightmap fails. With
  DynamicGI.updateThreshold a part of the lightmap is updated only when its inputs changed by
  more than the threshold, so converged texels depend on which update last solved them; input
  differences well below the threshold move a few isolated texels by up to about 10%. The rest
  of the lighting values are reported (gated with --strict).

Every failing value is written to CAPTURE/compare-details.txt; the output lists one line per
failing case, atlas or frame.
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
# Gated realtime lightmaps: the share of their compared values allowed outside the tolerance,
# and how far those may be off.
ISOLATED = 0.005
ISOLATED_REL = 0.10
STATE_KEYS = ('converged', 'indirectScale', 'updateThreshold', 'size', 'lightmapsMode', 'wrongLength', 'noArray')


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


class Result:
    def __init__(self):
        self.state, self.gated, self.lighting, self.details, self.lines = [], [], [], [], []

    def fail(self, kind, summary, details=()):
        {'state': self.state, 'gated': self.gated}.get(kind, self.lighting).append(summary)
        self.details.append(summary)
        self.details.extend('  ' + d for d in details)


def compare_values(ref, cap, result):
    for name in sorted(set(cap) - set(ref)):
        # `<case>-noatlas`: the capture found no realtime lightmap to read (a lighting difference,
        # gated as the case's lightmap is).
        kind = 'gated' if name.endswith('-noatlas') else 'state'
        result.fail(kind, f'{name}: extra case', [json.dumps(cap[name])[:2000]])
    for name, r in ref.items():
        c = cap.get(name)
        if c is None:
            result.fail('state', f'{name}: missing')
            continue
        for key in STATE_KEYS:
            if key in r and r[key] != c.get(key):
                result.fail('state', f'{name}: {key} {c.get(key)} != {r[key]}')
        if 'renderers' in r:
            if [(x['name'], x['index']) for x in r['renderers']] != [(x['name'], x['index']) for x in c.get('renderers', [])]:
                result.fail('state', f'{name}: renderers/indices {c.get("renderers")} != {r["renderers"]}')
            else:
                for x, y in zip(r['renderers'], c['renderers']):
                    if any(abs(a - b) > 1e-6 for a, b in zip(x['st'], y['st'])):
                        result.fail('state', f'{name}: {x["name"]} st {y["st"]} != {x["st"]}')
        if 'baked' in r:
            if r.get('count') != c.get('count') or len(r['baked']) != len(c.get('baked', [])):
                result.fail('state', f'{name}: {c.get("count")} probes with {len(c.get("baked", []))} baked '
                                     f'values != {r.get("count")} with {len(r["baked"])}')
            else:
                bad = [f'[{k}] {a!r} vs {b!r}' for k, (a, b) in enumerate(zip(c['baked'], r['baked']))
                       if not close(a, b)]
                if bad:
                    result.fail('state', f'{name}: {len(bad)} baked coefficients off', bad)
        for key in ('probes', 'ambientProbe'):
            if key not in r:
                continue
            rv, cv = r[key], c.get(key)
            if key == 'ambientProbe':
                rv, cv = [rv], [cv] if cv is not None else None
            if cv is None or len(cv) != len(rv):
                result.fail('lighting', f'{name}: {key} missing or of another length')
                continue
            for i, (rs, cs) in enumerate(zip(rv, cv)):
                bad = [k for k, (a, b) in enumerate(zip(cs, rs)) if not close(a, b)]
                if bad:
                    worst = max(bad, key=lambda k: abs(cs[k] - rs[k]))
                    result.fail('lighting', f'{name}: {key}[{i}] {len(bad)} coefficients off, worst [{worst}] '
                                            f'{cs[worst]:.5g} vs {rs[worst]:.5g}',
                                [f'[{k}] {cs[k]!r} vs {rs[k]!r}' for k in bad])


def compare_atlas(name, ref_path, cap_path, result):
    lightmap = name.endswith('.atlas')
    gate = lightmap or name.endswith('.dir')
    # A lightmap's RGB (its alpha is the coverage); all four channels of a directionality.
    channels = 3 if lightmap else 4
    # Only the lightmaps have isolated values (see the module's description).
    allowance = ISOLATED if lightmap else 0.0
    if not cap_path.is_file():
        result.fail('gated' if gate else 'lighting', f'{name}: missing')
        return
    rw, rh, rv = atlas(ref_path)
    cw, ch, cv = atlas(cap_path)
    if (rw, rh) != (cw, ch):
        result.fail('gated' if gate else 'lighting', f'{name}: size {cw}x{ch} != {rw}x{rh}')
        return
    coverage, bad = [], []
    total = worst = 0.0
    compared = far = 0
    for t in range(rw * rh):
        ra, ca = rv[4 * t + 3] > 0, cv[4 * t + 3] > 0
        if ra != ca:
            coverage.append(f'texel ({t % rw},{t // rw}) covered: {ca} vs {ra}')
            continue
        if not ra:
            continue
        compared += channels
        for k in range(channels):
            a, b = cv[4 * t + k], rv[4 * t + k]
            d = abs(a - b)
            total += d
            worst = max(worst, d)
            if not close(a, b):
                bad.append(f'texel ({t % rw},{t // rw}) channel {k}: {a!r} vs {b!r}')
                far += d > ABS + ISOLATED_REL * abs(b)
    result.lines.append(f'{name}: coverage differs at {len(coverage)} texels, {len(bad)} values off, '
                        f'mean |d| {total / max(1, rw * rh):.5f}, max |d| {worst:.5f}')
    if coverage:
        # Which texels renderers cover (their realtime lightmap UVs) matches: gated.
        result.fail('state', f'{name}: coverage differs at {len(coverage)} texels', coverage)
    if gate and not far and len(bad) <= allowance * compared:
        if bad:
            result.details.append(f'{name}: {len(bad)} isolated texel values off (allowed '
                                  f'{int(allowance * compared)})')
            result.details.extend('  ' + d for d in bad)
    elif bad:
        result.fail('gated' if gate else 'lighting',
                    f'{name}: {len(bad)} texel values off (max |d| {worst:.5f})', bad)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--strict', action='store_true')
    args = parser.parse_args()
    ref, cap = Path(args.reference), Path(args.capture)
    result = Result()
    if not (cap/'values.jsonl').is_file():
        print(f'realtimegi: no values.jsonl in {cap}')
        return 1
    compare_values(records(ref/'values.jsonl'), records(cap/'values.jsonl'), result)
    for path in sorted(ref.iterdir()):
        if path.suffix in ('.atlas', '.dir'):
            compare_atlas(path.name, path, cap/path.name, result)
        elif path.suffix == '.png':
            if not (cap/path.name).is_file():
                result.fail('lighting', f'{path.name}: missing')
                continue
            diff = image_difference(path, cap/path.name)
            result.lines.append(f'{path.name}: images mean {diff[0]:.2f} worst {diff[1]} over8 {diff[2]}'
                                if diff is not None else f'{path.name}: size differs')
    for extra in sorted(p.name for p in cap.iterdir() if p.suffix in ('.atlas', '.dir', '.png')
                        and not (ref/p.name).exists()):
        result.fail('state', f'{extra}: extra output')
    (cap/'compare-details.txt').write_text('\n'.join(result.details) + '\n')
    for line in result.lines:
        print(line)
    for failure in result.state:
        print('FAIL state', failure)
    for failure in result.gated:
        print('FAIL gated lighting', failure)
    for failure in result.lighting:
        print('FAIL lighting', failure)
    print(f'realtimegi: {len(result.state)} failing state values, {len(result.gated)} failing gated '
          f'lighting values, {len(result.lighting)} failing lighting values'
          + ('' if args.strict else ' (reported)') + f'; details {cap}/compare-details.txt')
    return 1 if result.state or result.gated or (result.lighting and args.strict) else 0


if __name__ == '__main__':
    sys.exit(main())
