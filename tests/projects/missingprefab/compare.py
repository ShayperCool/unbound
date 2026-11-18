#!/usr/bin/env python3
"""Compare one variant's records with the Editor's, exactly.

Every missing, extra and differing key is printed. `residuals.json` (next to this file) lists the
known differences as {reason: ["<variant>:<key>", ...]}: such a key that differs is printed with
its reason and does not fail; one that matches again is printed as resolved (remove it).
"""
import argparse
import json
from pathlib import Path

PROBE = Path(__file__).resolve().parent


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    result = {r['k']: r['v'] for r in rows}
    if not rows or len(rows) != len(result):
        raise SystemExit(f'{path}: empty or duplicate record')
    return result


def main():
    p = argparse.ArgumentParser()
    p.add_argument('reference')
    p.add_argument('actual')
    p.add_argument('--variant', required=True)
    a = p.parse_args()
    ref, got = load(a.reference), load(a.actual)
    reasons = {}
    for reason, keys in json.loads((PROBE / 'residuals.json').read_text()).items():
        for key in keys:
            variant, _, name = key.partition(':')
            if variant == a.variant:
                reasons[name] = reason
    failed = recorded = 0
    for key in sorted(ref.keys() | got.keys()):
        if key in ref and key in got and ref[key] == got[key]:
            if key in reasons:
                print(f'resolved {key}: matches again; remove its recorded residual')
            continue
        line = f'{key}: Unity {ref.get(key)!r}, Unbound {got.get(key)!r}'
        if key in reasons and key in ref:
            recorded += 1
            print(f'recorded {line} ({reasons[key]})')
        else:
            failed += 1
            print(line)
    stale = sorted(k for k in reasons if k not in ref)
    for key in stale:
        print(f'FAIL {key}: a recorded residual for a key the reference does not have')
    matching = len(ref) - sum(1 for k in ref if k not in got or ref[k] != got[k])
    print(f'missingprefab {a.variant}: {matching}/{len(ref)} values match, {recorded} recorded residuals, {failed} failed')
    return int(bool(failed or stale))


if __name__ == '__main__':
    raise SystemExit(main())
