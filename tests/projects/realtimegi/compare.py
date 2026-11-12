#!/usr/bin/env python3
"""Compares a realtimegi capture (engine) with the Unity reference.

    compare.py REFERENCE CAPTURE [--strict]

Two classes of values:

- State, always gated (exit 1 on any difference): every reference case present, no extra case,
  each renderer's realtime lightmap index and scale/offset, the lightmaps mode, the light probe
  count and baked coefficients, DynamicGI.isConverged, the recorded API defaults, what
  DynamicGI.SetEnvironmentData throws, the default reflection (ReflectionProbe.defaultTexture's
  name, dimension and size, its decode values) and which atlas texels renderers cover (their
  realtime lightmap UVs).
- Lighting: light probe SH (each sample point, 27 coefficients; gated where a case records them
  as `interpolated`) and the ambient probe
  (RenderSettings.ambientProbe, gated) within ABS + REL * |reference|; per atlas (.atlas/.dir) each covered texel's RGB within ABS + REL *
  |reference|; frames (.png) report the shared image measure (mean, worst and pixels over 8 of
  the per-pixel max-channel difference) and are gated on its mean (at most FRAME_MEAN; a missing
  frame or one of another size fails). A directionality (.dir) holds 8-bit values, compares
  its fourth channel too and is gated: each value within DIRECTION of the reference, at most
  ISOLATED of them further, each within twice that. The realtime lightmaps (.atlas) are gated: at most
  ISOLATED of their values may be outside the tolerance, each within ABS + ISOLATED_REL *
  |reference|, and a missing lightmap fails. With
  DynamicGI.updateThreshold a part of the lightmap is updated only when its inputs changed by
  more than the threshold, so converged texels depend on which update last solved them; input
  differences well below the threshold move a few isolated texels by up to about 10%. The rest
  of the lighting values are reported (gated with --strict).

With --own-layout (a capture whose realtime GI the engine precomputed itself, `run.py --bake`) the
renderers' places in the atlas (their scale/offset, the atlas files) are the engine's own and not
compared; each renderer's lightmap in its own realtime lightmap UV space (`.rlm`) is: which of its
texels it covers is state (its realtime lightmap UVs); their values are gated per case on a share
of them outside the tolerance (OWN_SHARE: the engine's own precompute differs from the Editor's,
docs/REALTIME_GI.md), each one listed.

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
# A directionality's values (8 bits each): 2.5 steps.
DIRECTION = 2.5 / 255
# The share of a case's `.rlm` values (an engine's own precompute, --own-layout) that may be
# outside the tolerance: above what Unbound's precompute gives the probe's bake cases (1.6% and
# 2.7%, docs/REALTIME_GI.md), far below what a bake that lights nothing would.
OWN_SHARE = 0.04
# A frame's mean per-pixel max-channel difference (of 255), the image probes' bound.
FRAME_MEAN = 2.0
STATE_KEYS = ('converged', 'indirectScale', 'updateThreshold', 'size', 'lightmapsMode', 'wrongLength', 'noArray',
              'defaultTexture', 'dimension', 'reflectionSize', 'decode')


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


def compare_values(ref, cap, result, own_layout=False):
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
            elif not own_layout:
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
        for key in ('probes', 'ambientProbe', 'interpolated'):
            if key not in r:
                continue
            rv, cv = r[key], c.get(key)
            if key == 'ambientProbe':
                rv, cv = [rv], [cv] if cv is not None else None
            # RenderSettings.ambientProbe is an API's answer: gated, as are the interpolated
            # probes recorded for what a script's baked coefficients do to them (`interpolated`).
            # The light probes of the lighting cases are reported.
            kind = 'lighting' if key == 'probes' else 'gated'
            if cv is None or len(cv) != len(rv):
                result.fail(kind, f'{name}: {key} missing or of another length')
                continue
            for i, (rs, cs) in enumerate(zip(rv, cv)):
                bad = [k for k, (a, b) in enumerate(zip(cs, rs)) if not close(a, b)]
                if bad:
                    worst = max(bad, key=lambda k: abs(cs[k] - rs[k]))
                    result.fail(kind, f'{name}: {key}[{i}] {len(bad)} coefficients off, worst [{worst}] '
                                            f'{cs[worst]:.5g} vs {rs[worst]:.5g}',
                                [f'[{k}] {cs[k]!r} vs {rs[k]!r}' for k in bad])


def compare_atlas(name, ref_path, cap_path, result, tally=None):
    # A renderer's own lightmap (`.rlm`) is a lightmap, reported.
    lightmap = name.endswith('.atlas') or name.endswith('.rlm')
    gate = name.endswith('.atlas') or name.endswith('.dir')
    # A lightmap's RGB (its alpha is the coverage); all four channels of a directionality.
    channels = 3 if lightmap else 4
    # How far a value may be from reference value b, and how far an isolated one.
    near = (lambda b: ABS + REL * abs(b)) if lightmap else (lambda b: DIRECTION)
    furthest = (lambda b: ABS + ISOLATED_REL * abs(b)) if lightmap else (lambda b: 2 * DIRECTION)
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
            if d > near(b):
                bad.append(f'texel ({t % rw},{t // rw}) channel {k}: {a!r} vs {b!r}')
                far += d > furthest(b)
    if tally is not None:
        # An own precompute's renderer lightmap: counted toward its case's share.
        case = tally.setdefault(name.rsplit('-', 1)[0], [0, 0, []])
        case[0] += compared
        case[1] += len(bad)
        case[2].extend(f'{name}: {d}' for d in bad)
    result.lines.append(f'{name}: coverage differs at {len(coverage)} texels, {len(bad)} values off, '
                        f'mean |d| {total / max(1, rw * rh):.5f}, max |d| {worst:.5f}')
    if coverage:
        # Which texels renderers cover (their realtime lightmap UVs) matches: gated.
        result.fail('state', f'{name}: coverage differs at {len(coverage)} texels', coverage)
    if tally is not None:
        return
    if gate and not far and len(bad) <= ISOLATED * compared:
        if bad:
            result.details.append(f'{name}: {len(bad)} isolated texel values off (allowed '
                                  f'{int(ISOLATED * compared)})')
            result.details.extend('  ' + d for d in bad)
    elif bad:
        result.fail('gated' if gate else 'lighting',
                    f'{name}: {len(bad)} texel values off (max |d| {worst:.5f})', bad)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--strict', action='store_true')
    parser.add_argument('--own-layout', action='store_true',
                        help="the capture's renderers lie where its own precompute put them")
    args = parser.parse_args()
    ref, cap = Path(args.reference), Path(args.capture)
    result = Result()
    if not (cap/'values.jsonl').is_file():
        print(f'realtimegi: no values.jsonl in {cap}')
        return 1
    compare_values(records(ref/'values.jsonl'), records(cap/'values.jsonl'), result, args.own_layout)
    laid_out = ('.atlas', '.dir')
    own = {}
    for path in sorted(ref.iterdir()):
        if path.suffix in laid_out and args.own_layout:
            result.lines.append(f'{path.name}: not compared (the renderers lie where each precompute put them)')
        elif path.suffix in laid_out:
            compare_atlas(path.name, path, cap/path.name, result)
        elif path.suffix == '.rlm':
            compare_atlas(path.name, path, cap/path.name, result, own)
    for case, (compared, off, details) in sorted(own.items()):
        result.lines.append(f'{case} (renderers): {off} of {compared} values off (allowed {int(OWN_SHARE * compared)})')
        if off > OWN_SHARE * compared:
            result.fail('gated', f'{case}: {off} of {compared} renderer lightmap values off', details)
        elif off:
            result.details.append(f'{case}: {off} renderer lightmap values off (allowed {int(OWN_SHARE * compared)})')
            result.details.extend('  ' + d for d in details)
        elif path.suffix == '.png':
            if not (cap/path.name).is_file():
                result.fail('gated', f'{path.name}: missing')
                continue
            diff = image_difference(path, cap/path.name)
            result.lines.append(f'{path.name}: images mean {diff[0]:.2f} worst {diff[1]} over8 {diff[2]}'
                                if diff is not None else f'{path.name}: size differs')
            if diff is None or diff[0] > FRAME_MEAN:
                result.fail('gated', f'{path.name}: ' + ('size differs' if diff is None else
                                                         f'frame mean {diff[0]:.2f} over {FRAME_MEAN}'))
    for extra in sorted(p.name for p in cap.iterdir() if p.suffix in ('.atlas', '.dir', '.png', '.rlm')
                        and not (ref/p.name).exists() and not (args.own_layout and p.suffix in laid_out)):
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
