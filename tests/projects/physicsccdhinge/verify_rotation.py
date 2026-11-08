"""Gate cause controls and retain bounded automatic-inertia contact diagnostics.

Every strict difference stays in numeric_diagnostics or unresolved; no capture
or comparison is rewritten. Planar witnesses may move within the same face.
"""
import math


# Absolute residual bounds from the rotation controls, Unity 6000.0.71f1.
# Body quantities are position, quaternion, linear velocity and angular velocity.
BODY_BOUNDS = {'0': (2e-5, 1e-4, 5e-4, 5e-3), '3': (7e-5, 3e-4, 1e-5, 5e-5)}
IMPULSE_BOUNDS = {'0': 5e-4, '3': 2e-5}
NORMAL_BOUND = 2e-4
SEPARATION_BOUND = 3e-4
FACE_EXTENT_BOUND = 1.5e-4
FACE_PLANE_BOUND = 1e-4


def numbers(tokens):
    values = [float(token) for token in tokens]
    if not all(math.isfinite(value) for value in values):
        raise ValueError('Nonfinite control value')
    return values


def within(left, right, bounds):
    return len(left) == len(right) == len(bounds) and all(
        abs(a-b) <= bound for a, b, bound in zip(left, right, bounds))


def planar_witness(key, expected, actual):
    header = key.rsplit('/', 1)[0]
    count = int(expected[header].split(',')[2])
    reference = [numbers(expected[header+'/'+str(i)].split(',')[:7]) for i in range(count)]
    candidate = [numbers(actual[header+'/'+str(i)].split(',')[:7]) for i in range(count)]
    # A face's clipped witnesses can change, but its footprint and plane cannot
    # grow or move beyond the recorded residual. Check both sets' extents.
    for axis in range(3):
        for endpoint in (min, max):
            if abs(endpoint(p[axis] for p in reference)-endpoint(p[axis] for p in candidate)) > FACE_EXTENT_BOUND:
                return False
    for points, face in ((candidate, reference), (reference, candidate)):
        for point in points:
            if min(abs(sum((point[i]-other[i])*other[i+3] for i in range(3)))
                   for other in face) > FACE_PLANE_BOUND:
                return False
    return True


def admitted_numeric(failure, expected, actual):
    key = failure['key']
    left, right = expected.get(key), actual.get(key)
    if left is None or right is None:
        return False
    a, b = left.split(','), right.split(',')
    control = key.split('/')[2]
    if len(a) != len(b):
        return False
    try:
        if key.endswith('/body'):
            p, q, v, w = BODY_BOUNDS[control]
            return within(numbers(a), numbers(b), [p]*3+[q]*4+[v]*3+[w]*3)
        if '/event/' in key:
            header = '/' not in key.split('/event/', 1)[1]
            if header:
                return (len(a) == 9 and a[:3] == b[:3]
                        and within(numbers(a[3:]), numbers(b[3:]),
                                   [IMPULSE_BOUNDS[control]]*3+[BODY_BOUNDS[control][2]]*3))
            return (len(a) == 9 and a[-2:] == b[-2:]
                    and within(numbers(a[3:7]), numbers(b[3:7]), [NORMAL_BOUND]*3+[SEPARATION_BOUND])
                    and planar_witness(key, expected, actual))
    except (ValueError, KeyError, IndexError):
        return False
    return False


def verify(expected, actual, report):
    # Flight, configuration, primitive, explicit-inertia and position-lock
    # controls are strict. Only measured unconstrained hull contacts diagnose.
    diagnostics = []
    unresolved = []
    for failure in report['failures']:
        fields = failure['key'].split('/')
        diagnostic = (len(fields) > 5 and fields[:2] == ['rotation', 'contact']
                      and fields[2] in BODY_BOUNDS and fields[3] == 'False'
                      and fields[5].isdigit())
        if diagnostic:
            diagnostics.append(failure)
            if not admitted_numeric(failure, expected, actual):
                unresolved.append(failure)
        else:
            unresolved.append(failure)
    return dict(unresolved=unresolved, numeric_diagnostics=diagnostics,
                differences=len(report['failures']))
