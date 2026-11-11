#!/usr/bin/env python3
"""Compare every lifecycle and reflection record exactly."""
import argparse
import json
from pathlib import Path

def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    result = {r['k']: r['v'] for r in rows}
    if not rows or len(rows) != len(result):
        raise ValueError('empty or duplicate record')
    return result

def main():
    p = argparse.ArgumentParser()
    p.add_argument('reference'); p.add_argument('actual'); p.add_argument('--name', default='editinheritance')
    a = p.parse_args()
    ref, got = load(a.reference), load(a.actual)
    bad = [k for k in sorted(ref.keys() | got.keys()) if k not in ref or k not in got or ref[k] != got[k]]
    for k in bad:
        print(f'{k}: Unity {ref.get(k)!r}, Unbound {got.get(k)!r}')
    print(f'{a.name}: {len(ref)-len(bad)}/{len(ref)} values match')
    return int(bool(bad))
if __name__ == '__main__':
    raise SystemExit(main())
