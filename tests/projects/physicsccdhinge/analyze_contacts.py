#!/usr/bin/env python3
"""Annotate strict CCD failures without changing captures or comparator results.

Matching uses complete callback/contact multisets within one step and receiver.
An opposite witness is reported separately, never counted as slot order alone.
Unmatched values stay in the content category with their original keys/values.
"""
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'physicsbatch'))
from compare import compare, equal, load


def multiset_equal(left, right, predicate=equal):
    """One-to-one matching, including duplicate contact values."""
    if len(left) != len(right):
        return False
    assigned = {}

    def visit(i, seen):
        for j, value in enumerate(right):
            if j in seen or not predicate(left[i], value):
                continue
            seen.add(j)
            if j not in assigned or visit(assigned[j], seen):
                assigned[j] = i
                return True
        return False

    return all(visit(i, set()) for i in range(len(left)))


def alternate_witness(left, right):
    a, b = left.split(','), right.split(',')
    if len(a) != 9 or len(b) != 9 or not equal(','.join(a[3:]), ','.join(b[3:])):
        return False
    # The two surface witnesses differ by the signed normal separation.
    for sign in (-1, 1):
        shifted = ','.join(str(float(a[i]) + sign * float(a[3+i]) * float(a[6])) for i in range(3))
        if equal(shifted, ','.join(b[:3])):
            return True
    return False


def callbacks(rows):
    grouped = defaultdict(dict)
    for key, value in rows.items():
        if '/event/' not in key:
            continue
        context, slot = key.split('/event/')
        if '/' in slot:
            continue
        fields = value.split(',')
        points = [rows[key+'/'+str(i)] for i in range(int(fields[2]))]
        grouped[context][key] = (fields, points)
    return grouped


def relation(left, right):
    a, ap = left
    b, bp = right
    if not equal(','.join(a[:3]+a[6:]), ','.join(b[:3]+b[6:])):
        return None
    same_impulse = equal(','.join(a[3:6]), ','.join(b[3:6]))
    opposite_impulse = equal(','.join(str(-float(x)) for x in a[3:6]), ','.join(b[3:6]))
    if not same_impulse and not opposite_impulse:
        return None
    if multiset_equal(ap, bp):
        return 'order_only' if same_impulse else 'pair_orientation'
    if multiset_equal(ap, bp, lambda x, y: equal(x, y) or alternate_witness(x, y)):
        return 'surface_witness'
    return None


def analyze(expected, actual):
    report = compare(expected, actual)
    e, a = callbacks(expected), callbacks(actual)
    categories = {}
    # Prefer exact payload matches before orientation/witness matches. No callback
    # may be reused to explain a second expected callback.
    for context, events in e.items():
        available = dict(a.get(context, {}))
        remaining = dict(events)
        for category in ('order_only', 'pair_orientation', 'surface_witness'):
            for key, value in list(remaining.items()):
                for candidate, data in list(available.items()):
                    if relation(value, data) == category:
                        categories[('expected', key)] = category
                        categories[('actual', candidate)] = category
                        del remaining[key]
                        del available[candidate]
                        break
    annotated = []
    priority = ('unmatched_payload', 'surface_witness', 'pair_orientation', 'order_only')
    for failure in report['failures']:
        key = failure['key']
        if '/event/' in key:
            context, suffix = key.split('/event/')
            event = context+'/event/'+suffix.split('/')[0]
            sides = [categories.get((side, event), 'unmatched_payload')
                     for side, rows in (('expected', expected), ('actual', actual)) if key in rows]
            category = next(x for x in priority if x in sides)
        elif key.endswith('/body'):
            category = 'body'
        elif key.endswith('/events'):
            category = 'callback_count'
        else:
            category = 'other'
        annotated.append(dict(failure, category=category))
    return dict(total=report['total'], differences=len(annotated),
                counts=dict(Counter(x['category'] for x in annotated)), failures=annotated)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference', type=Path)
    parser.add_argument('actual', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = analyze(load(args.reference), load(args.actual))
    args.output.write_text(json.dumps(result, indent=2)+'\n')
    print(f"CCD contact analysis: {result['differences']} strict differences; {result['counts']}")
