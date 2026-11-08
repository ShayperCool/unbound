#!/usr/bin/env python3
"""Gate the strict comparison, retaining one documented residual family.

compare.py remains strict and prints every raw difference. This verifier admits only the
AlwaysAnimate and CullUpdateTransforms frame-35 arm-change predicates listed in
COMPATIBILITY_GAPS.md, and only while both raw arm rotations agree tightly and their dots
straddle Quaternion's equality boundary.
"""
import sys
from compare import read, same


RESIDUALS = {f'ph/{mode}/{controller}/{shape}/35' for mode in ('always', 'transforms')
             for controller in ('bare', 'controller') for shape in ('clip', 'mixer')}


def verify(reference, candidate):
    if reference.keys() != candidate.keys():
        return False, []
    accepted = []
    for key, expected in reference.items():
        actual = candidate[key]
        if same(expected, actual):
            continue
        if key not in RESIDUALS or not isinstance(actual, dict):
            return False, accepted
        if expected.get('arm') is not False or actual.get('arm') is not True:
            return False, accepted
        if not same({k: v for k, v in expected.items() if k != 'arm'},
                    {k: v for k, v in actual.items() if k != 'arm'}):
            return False, accepted
        prefix = key.rsplit('/', 1)[0]
        for frame in (34, 35):
            pose = f'{prefix}/pose/{frame}'
            if pose not in reference or pose not in candidate:
                return False, accepted
            a, b = reference[pose], candidate[pose]
            if len(a['arm']) != 4 or len(b['arm']) != 4:
                return False, accepted
            if max(abs(x - y) for x, y in zip(a['arm'], b['arm'])) > 5e-7:
                return False, accepted
        pose = f'{prefix}/pose/35'
        # Unity's float boundary is 1f - 1e-6f. Native is immediately above it (measured
        # 0.999999046 AlwaysAnimate, 0.999999166 CullUpdateTransforms: within 2e-7);
        # the candidate rounds onto it. Every continuous pose record is checked above.
        boundary = 0.9999989867210388
        a, b = reference[pose]['armDot'], candidate[pose]['armDot']
        if not (boundary < a < boundary + 2e-7 and boundary - 1e-7 < b < boundary + 2e-8):
            return False, accepted
        accepted.append(key)
    return True, accepted


def main():
    reference, candidate = map(read, sys.argv[1:3])
    passed, accepted = verify(reference, candidate)
    if passed:
        print(f'gate: {len(reference)} records checked; {len(accepted)} documented residuals')
        for key in accepted:
            print('accepted residual', key, 'arm predicate only; raw rotations and dots checked')
    else:
        print('gate: a difference is outside the documented residuals')
    return 0 if passed else 1


if __name__ == '__main__':
    sys.exit(main())
