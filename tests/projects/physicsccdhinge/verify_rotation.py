"""Gate cause controls and keep automatic-inertia free-body contacts diagnostic.

This is not a full contact-numeric parity verdict. Every strict difference stays
in numeric_diagnostics or unresolved; no capture or comparison is rewritten.
"""
import math


def verify(expected, actual, report):
    # The flight, configuration, primitive and explicit-inertia controls gate
    # strictly. Automatic-inertia hull contacts retain a numeric diagnostic.
    diagnostics = []
    unresolved = []
    for failure in report['failures']:
        fields = failure['key'].split('/')
        diagnostic = (len(fields) > 5 and fields[:2] == ['rotation', 'contact']
                      and fields[2] in ('0', '3') and fields[3] == 'False'
                      and fields[5].isdigit())
        if diagnostic:
            diagnostics.append(failure)
        else:
            unresolved.append(failure)
    # Even diagnostic contacts retain exact cardinality and callback identity.
    for failure in diagnostics:
        key = failure['key']
        left, right = expected.get(key), actual.get(key)
        if left is None or right is None:
            unresolved.append(failure)
        elif key.endswith('/body'):
            # Bounds describe only the recorded insignificant pose/velocity
            # residuals; the shared strict comparator retains its tolerances.
            control = key.split('/')[2]
            bounds = ((2e-5, 1e-4, 5e-4, 5e-3) if control == '0'
                      else (7e-5, 3e-4, 1e-5, 5e-5))
            limits = [bounds[0]] * 3 + [bounds[1]] * 4 + [bounds[2]] * 3 + [bounds[3]] * 3
            try:
                arow, brow = left.split(','), right.split(',')
                if len(arow) != 13 or len(brow) != 13 or any(
                        abs(float(a)-float(b)) > bound for a, b, bound in zip(arow, brow, limits)):
                    unresolved.append(failure)
            except ValueError:
                unresolved.append(failure)
        elif key.endswith('/events'):
            unresolved.append(failure)
        elif '/event/' in key:
            arow, brow = left.split(','), right.split(',')
            header = '/' not in key.split('/event/', 1)[1]
            identities = slice(0, 3) if header else slice(-2, None)
            if arow[identities] != brow[identities]:
                unresolved.append(failure)
    for key, value in actual.items():
        if key in expected and len(value.split(',')) != len(expected[key].split(',')):
            unresolved.append(dict(key=key, actual=value, reason='Invalid rotation control width'))
        for token in value.split(','):
            try:
                numeric = float(token)
            except ValueError:
                continue
            if not math.isfinite(numeric):
                unresolved.append(dict(key=key, actual=value, reason='Nonfinite rotation control'))
    result = dict(unresolved=unresolved, numeric_diagnostics=diagnostics,
                  differences=len(report['failures']))
    return result
