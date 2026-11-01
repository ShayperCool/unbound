#!/usr/bin/env python3
"""Prints a culling-group record compactly: steps, callbacks, and each group's state when it changes."""
import json
import sys

prev = {}
for line in open(sys.argv[1]):
    r = json.loads(line)
    k = r['k']
    if k in ('api', 'log', 'query'):
        continue
    if k in ('awake', 'start', 'update', 'lateupdate', 'rendered', 'replaced'):
        s = (r['vis'], r['dist'])
        if prev.get(r['g']) != s or k in ('rendered', 'replaced'):
            print(r['f'], k, r['g'], *s)
            prev[r['g']] = s
    elif k == 'event':
        print(r['f'], 'event', r['g'], r['cam'], r['i'], 'vis', int(r['vis']), 'was', int(r['was']), 'cur', r['cur'], 'prev', r['prev'])
    elif k == 'step':
        print(r['f'], 'step', r['what'])
    elif k.startswith('OnBecame') or (k == 'OnPreCull' and r['f'] == 8):
        print(r['f'], k, r.get('cam'))
