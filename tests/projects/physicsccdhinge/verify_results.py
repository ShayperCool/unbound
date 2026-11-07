"""Retain strict failures and identify bounded angle roundoff and pair impulse orientation.

Counts, phases, points, normals, separations, velocities and body poses must still
match the unchanged strict comparator. No capture is sorted or rewritten.
"""
import math
from compare import equal


def verify(expected, actual, report):
    angles, resting, orientations, unresolved = [], [], [], []
    for failure in report['failures']:
        key = failure['key']
        left, right = expected.get(key), actual.get(key)
        if left is None or right is None:
            unresolved.append(failure)
            continue
        a, b = left.split(','), right.split(',')
        accepted = False
        if key.startswith('hinge/') and key.endswith('/hinge') and len(a) == len(b) == 8:
            # A few quaternion ulps are magnified by acos near zero. At the wrap,
            # the same roundoff can put a reading on the opposite side of ±180.
            try:
                delta = abs((float(a[0]) - float(b[0]) + 180) % 360 - 180)
                if math.isfinite(delta) and delta <= .006 and equal(','.join(a[1:]), ','.join(b[1:])):
                    angles.append(dict(failure, angle_error_degrees=delta))
                    accepted = True
            except ValueError:
                pass
            try:
                av, bv = float(a[1]), float(b[1])
                if (math.isfinite(av) and math.isfinite(bv) and max(abs(av), abs(bv)) <= .001
                        and abs(av-bv) <= .0004 and equal(a[0], b[0])
                        and equal(','.join(a[2:]), ','.join(b[2:]))):
                    resting.append(dict(failure, velocity_error_degrees_per_second=abs(av-bv)))
                    accepted = True
            except ValueError:
                pass
        if '/event/' in key and key.rsplit('/', 1)[-1].isdigit() and len(a) == len(b) == 9:
            # Collision.impulse retains the native pair direction, unlike the
            # self-facing contact normal. Require the entire contact set to match.
            try:
                count = int(a[2])
                points = [key+'/'+str(i) for i in range(count)]
                if (count > 0 and a[:3] == b[:3]
                        and equal(','.join(a[6:]), ','.join(b[6:]))
                        and equal(','.join(a[3:6]), ','.join(str(-float(v)) for v in b[3:6]))
                        and all(p in expected and p in actual and equal(expected[p], actual[p]) for p in points)):
                    orientations.append(failure)
                    accepted = True
            except ValueError:
                pass
        if not accepted:
            unresolved.append(failure)
    return dict(unresolved=unresolved, angle_roundoff=angles, resting_velocity_roundoff=resting,
                pair_orientation=orientations)
