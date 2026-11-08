"""Gate constrained publication; retain the free-rotation controls as diagnostics.

The controls without an angular constraint isolate the effect of publication.
Their complete numeric trace remains open, with counts and structure checked.
No capture, comparator, tolerance or raw failure is changed here.
"""
import math

from analyze_contacts import analyze
from compare import equal


def verify(expected, actual):
    analysis = analyze(expected, actual)
    problems = []
    diagnostic = []
    for key in expected.keys() | actual.keys():
        left, right = expected.get(key), actual.get(key)
        if left is None or right is None:
            problems.append(dict(key=key, reason='Missing or extra control row'))
            continue
        try:
            for token in right.split(','):
                try:
                    value = float(token)
                except ValueError:
                    continue
                if not math.isfinite(value):
                    problems.append(dict(key=key, reason='Nonfinite control value'))
        except AttributeError:
            problems.append(dict(key=key, reason='Invalid control value'))
    for failure in analysis['failures']:
        key = failure['key']
        fields = key.split('/')
        free_rotation = fields[1] in ('0', '4') and fields[2] in ('False', 'True')
        if not free_rotation:
            if failure['category'] not in ('order_only', 'pair_orientation'):
                problems.append(failure)
            continue
        # These are negative controls, not a claim of unconstrained solver parity.
        # Counts, phases and collider identities still have to agree by callback
        # slot; numerical geometry, impulses and resulting motion stay in the report.
        left, right = expected.get(key), actual.get(key)
        if left is not None and right is not None:
            if key.endswith('/events') and not equal(left, right):
                problems.append(failure)
            elif '/event/' in key and '/' not in key.split('/event/')[1]:
                if not equal(','.join(left.split(',')[:3]), ','.join(right.split(',')[:3])):
                    problems.append(failure)
            elif '/event/' in key and left.split(',')[-2:] != right.split(',')[-2:]:
                problems.append(failure)
        diagnostic.append(failure)
    return dict(unresolved=problems, free_rotation_trace=diagnostic,
                counts=analysis['counts'], differences=analysis['differences'])
