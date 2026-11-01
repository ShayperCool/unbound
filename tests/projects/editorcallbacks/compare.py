#!/usr/bin/env python3
"""Compares an Unbound capture of the editor callbacks probe with the Editor's reference.

    compare.py REFERENCE CAPTURE [--report REPORT.json]

Two parts, both exact (no tolerances: every value is a name, a flag, a count or a frame):

* the event sequence (every record but the per-object prefab records), in order. The Editor
  reloads its domain on entering Play mode: what its Edit mode domain recorded (before
  ExitingEditMode) is not part of Play mode, and ExitingEditMode itself is sent from that domain,
  so its position is not compared (that it is sent once is). Every missing and extra record is
  listed, with its position.
* the prefab records, by phase and object: every field that differs, every object recorded on
  one side only.

Exits 1 when anything differs.
"""
import argparse
import difflib
import json
import sys


def load(path):
    with open(path) as f:
        return [json.loads(line) for line in f if line.strip()]


def split(records, reference):
    exiting = [i for i, r in enumerate(records) if r.get('event') == 'playModeStateChanged' and r.get('state') == 'ExitingEditMode']
    if reference and exiting:
        records = records[exiting[-1]:]
    events, prefabs = [], {}
    for r in records:
        if r.get('event') == 'prefab':
            key = (r.get('phase'), r.get('object'))
            prefabs.setdefault(key, []).append(r)
        else:
            events.append(r)
    exits = [r for r in events if r.get('event') == 'playModeStateChanged' and r.get('state') == 'ExitingEditMode']
    events = [r for r in events if r not in exits]
    return events, prefabs, len(exits)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--report')
    args = parser.parse_args()
    ref_events, ref_prefabs, ref_exits = split(load(args.reference), True)
    cap_events, cap_prefabs, cap_exits = split(load(args.capture), False)

    report = {'exitingEditMode': {'reference': ref_exits, 'capture': cap_exits},
              'events': {'reference': len(ref_events), 'capture': len(cap_events), 'missing': [], 'extra': []},
              'prefab': {'reference': len(ref_prefabs), 'capture': len(cap_prefabs), 'fields': 0,
                         'differences': [], 'missing': [], 'extra': []}}

    a = [json.dumps(r, sort_keys=True) for r in ref_events]
    b = [json.dumps(r, sort_keys=True) for r in cap_events]
    for op, i1, i2, j1, j2 in difflib.SequenceMatcher(None, a, b, autojunk=False).get_opcodes():
        if op in ('replace', 'delete'):
            report['events']['missing'] += [{'at': i, 'record': ref_events[i]} for i in range(i1, i2)]
        if op in ('replace', 'insert'):
            report['events']['extra'] += [{'at': j, 'record': cap_events[j]} for j in range(j1, j2)]

    for key, refs in ref_prefabs.items():
        caps = cap_prefabs.get(key)
        if caps is None:
            report['prefab']['missing'].append(list(key))
            continue
        for n, ref in enumerate(refs):
            if n >= len(caps):
                report['prefab']['missing'].append(list(key) + [n])
                continue
            cap = caps[n]
            for field in sorted(set(ref) | set(cap)):
                report['prefab']['fields'] += 1
                if ref.get(field, '<absent>') != cap.get(field, '<absent>'):
                    report['prefab']['differences'].append({'phase': key[0], 'object': key[1], 'field': field,
                                                            'reference': ref.get(field, '<absent>'),
                                                            'capture': cap.get(field, '<absent>')})
        for n in range(len(refs), len(caps)):
            report['prefab']['extra'].append(list(key) + [n])
    for key in cap_prefabs:
        if key not in ref_prefabs:
            report['prefab']['extra'].append(list(key))

    p, e = report['prefab'], report['events']
    failing = (len(e['missing']) + len(e['extra']) + len(p['differences']) + len(p['missing']) + len(p['extra'])
               + (ref_exits != 1) + (cap_exits != 1))
    report['failing'] = failing
    print(f"events: {e['reference']} reference, {e['capture']} capture, {len(e['missing'])} missing, {len(e['extra'])} extra")
    print(f"prefab records: {p['reference']} reference, {p['capture']} capture, {len(p['missing'])} missing, "
          f"{len(p['extra'])} extra; fields {p['fields']}, {len(p['differences'])} differing")
    print(f"ExitingEditMode: reference {ref_exits}, capture {cap_exits}")
    for m in e['missing']:
        print('  missing event', m['at'], json.dumps(m['record']))
    for m in e['extra']:
        print('  extra event  ', m['at'], json.dumps(m['record']))
    for d in p['differences']:
        print('  prefab', d['phase'], d['object'], d['field'], 'reference', json.dumps(d['reference']), 'capture', json.dumps(d['capture']))
    for m in p['missing']:
        print('  missing prefab record', m)
    for m in p['extra']:
        print('  extra prefab record', m)
    if args.report:
        with open(args.report, 'w') as f:
            json.dump(report, f, indent=1)
    return 1 if failing else 0


if __name__ == '__main__':
    sys.exit(main())
