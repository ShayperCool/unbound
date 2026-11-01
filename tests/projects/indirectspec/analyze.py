#!/usr/bin/env python3
"""Tests the predeclared unity_IndirectSpecColor source rules against the witness captures.

    analyze.py LINEAR.jsonl GAMMA.jsonl [--l0 R G B]

Inputs are the published references (Reference/unity-<release>/{linear,gamma}.jsonl) or two
complete captures. L0 is the lighting data's serialized m_BakedAmbientProbeInLinear DC term
(sh[0], sh[9], sh[18]) as recorded by the witness; --l0 supplies it from an offline parse of
the same LightingData.asset when the Editor did not expose the property (it must then agree
with any recorded value).

Predeclared before any capture, nothing fitted:
* the measured value is the dump's unity_IndirectSpecColor of each "settled" capture (and
  "immediate" is reported against it);
* a rule is (A, R, M): ISC = M(L0 * A(ambientIntensity) * R(reflectionIntensity)), where A and R
  are each one of `one` (1), `x` (the intensity) or `g2l` (Mathf.GammaToLinearSpace(intensity),
  the sRGB curve), and M is the color space mapping: linear projects take the value as is;
  for gamma projects M is one of `srgb` (Mathf.LinearToGammaSpace per channel of the
  product), `srgb-then-scale` (sRGB of L0, then times A and R) or `none` (the linear value);
* a rule holds when every setting of both profiles (4 x 2) agrees within TOLERANCE in R, G
  and B. The analysis prints every candidate's largest error, so near misses stay visible.
  Only a single rule that holds for all eight settings is a proven source rule; otherwise the
  result is negative evidence and no rule is proposed.
* the Standard consumer is a consistency check, not part of the rule: its float centre over
  the measured constant must equal the BRDF's factor for the recorded tier
  (BRDF1: 0.5 * 0.04 linear, 0.72 * 0.220916301 gamma; BRDF2: 0.48 * 0.04 linear,
  0.72 * 0.220916301 gamma; BRDF3: 0.04 linear, 0.220916301 gamma), and its 8-bit centre the
  encoding of that product (sRGB in linear, x 255 in gamma, rounded).
"""
import argparse
import itertools
import json
import math
from pathlib import Path

TOLERANCE = 2e-5
SETTINGS = ('ambient 1 reflection 1', 'ambient 1 reflection 0.25',
            'ambient 0.5 reflection 1', 'ambient 0.5 reflection 0.25')
INTENSITY = {'ambient 1 reflection 1': (1.0, 1.0), 'ambient 1 reflection 0.25': (1.0, 0.25),
             'ambient 0.5 reflection 1': (0.5, 1.0), 'ambient 0.5 reflection 0.25': (0.5, 0.25)}
FACTOR = {('High', 'linear'): 0.5*0.04, ('High', 'gamma'): 0.72*0.220916301,
          ('Medium', 'linear'): 0.48*0.04, ('Medium', 'gamma'): 0.72*0.220916301,
          ('Low', 'linear'): 0.04, ('Low', 'gamma'): 0.220916301}


def g2l(x):
    return x/12.92 if x <= 0.04045 else ((x+0.055)/1.055)**2.4


def l2g(x):
    return x*12.92 if x <= 0.0031308 else 1.055*x**(1/2.4)-0.055


FORMS = {'one': lambda x: 1.0, 'x': lambda x: x, 'g2l': g2l}
MAPPINGS = {'linear': {'as-is': lambda l0, s: [c*s for c in l0]},
            'gamma': {'srgb': lambda l0, s: [l2g(c*s) for c in l0],
                      'srgb-then-scale': lambda l0, s: [l2g(c)*s for c in l0],
                      'none': lambda l0, s: [c*s for c in l0]}}


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    if not rows or rows[-1] != {'k': 'complete', 'v': True}:
        raise SystemExit(f'{path}: incomplete (no final completion marker)')
    return {r['k']: r['v'] for r in rows[:-1]}


def floats(text):
    return [float(t) for t in text.split()]


def recorded_l0(values):
    probe = values.get('lightingData m_BakedAmbientProbeInLinear')
    if not probe:
        return None
    count, numbers = probe.split(':', 1)
    numbers = floats(numbers)
    if int(count) != 27 or len(numbers) != 27:
        raise SystemExit(f'unexpected m_BakedAmbientProbeInLinear: {probe}')
    return [numbers[0], numbers[9], numbers[18]]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('linear')
    parser.add_argument('gamma')
    parser.add_argument('--l0', nargs=3, type=float)
    args = parser.parse_args()
    data = {'linear': load(args.linear), 'gamma': load(args.gamma)}
    l0 = None
    for space, values in data.items():
        recorded = recorded_l0(values)
        if recorded and args.l0 and max(abs(a-b) for a, b in zip(recorded, args.l0)) > 1e-7:
            raise SystemExit(f'{space}: recorded L0 {recorded} disagrees with --l0 {args.l0}')
        if recorded and l0 and recorded != l0:
            raise SystemExit(f'the profiles record different L0: {l0} vs {recorded}')
        l0 = l0 or recorded
    l0 = l0 or args.l0
    if l0 is None:
        raise SystemExit('no L0: the witness did not record m_BakedAmbientProbeInLinear; pass --l0')
    print('L0 (linear):', ' '.join(repr(c) for c in l0))

    measured = {}
    for space, values in data.items():
        print(f'\n== {space}: activeColorSpace {values.get("activeColorSpace")}, '
              f'tier {values.get("tier standardShaderQuality")}')
        for name in SETTINGS:
            settled = floats(values[f'{name} settled dump unity_IndirectSpecColor'])
            immediate = floats(values[f'{name} immediate dump unity_IndirectSpecColor'])
            measured[space, name] = settled
            print(f'  {name}: settled {settled}  immediate {"same" if immediate == settled else immediate}')
            print(f'    RenderSettings {values[f"{name} settled RenderSettings"]}')
            print(f'    hdr decode {values[f"{name} settled defaultTextureHDRDecodeValues"]}  '
                  f'specCube0_HDR {values[f"{name} settled dump unity_SpecCube0_HDR"]}')
            tier = values.get('tier standardShaderQuality')
            factor = FACTOR.get((tier, space))
            centre = floats(values[f'{name} settled standard floats centre'])
            byte = values[f'{name} settled standard bytes centre']
            if factor is not None:
                expected = [factor*c for c in settled[:3]]
                encode = (lambda v: round(255*l2g(v))) if space == 'linear' else (lambda v: round(255*v))
                print(f'    standard float centre {centre[:3]} vs factor x ISC {expected} '
                      f'(max err {max(abs(a-b) for a, b in zip(centre, expected)):.3g}); '
                      f'bytes {byte} vs {[encode(v) for v in expected]}')
            else:
                print(f'    standard float centre {centre}, bytes {byte} (unknown tier {tier})')

    print('\n== candidate rules (A, R, gamma mapping): largest |error| per profile')
    holding = []
    for a, r, mapping in itertools.product(FORMS, FORMS, MAPPINGS['gamma']):
        if a == r == 'one' and mapping == 'srgb-then-scale':
            continue    # the same function as srgb when nothing scales
        errors = {}
        for space in ('linear', 'gamma'):
            fn = MAPPINGS[space]['as-is' if space == 'linear' else mapping]
            worst = 0.0
            for name in SETTINGS:
                ai, ri = INTENSITY[name]
                predicted = fn(l0, FORMS[a](ai)*FORMS[r](ri))
                worst = max(worst, max(abs(p-m) for p, m in zip(predicted, measured[space, name][:3])))
            errors[space] = worst
        ok = all(e <= TOLERANCE for e in errors.values())
        if ok:
            holding.append((a, r, mapping))
        print(f'  A={a:4} R={r:4} M={mapping:15} linear {errors["linear"]:.3g}  gamma {errors["gamma"]:.3g}'
              + ('  HOLDS' if ok else ''))
    print()
    if len(holding) == 1:
        print('PROVEN (one rule, all 8 settings within', TOLERANCE, '):', holding[0])
        return 0
    print('NEGATIVE: no single predeclared rule holds for all 8 settings' if not holding
          else f'AMBIGUOUS: several rules hold {holding}')
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
