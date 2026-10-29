#!/usr/bin/env python3
"""Compare the materials probe: Unity's reference against a Unbound run.

    compare.py REFERENCE.json UNBOUND.json

Every check by key, the logged errors (logNNN) in order.
"""
import json
import sys


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    want = json.load(open(sys.argv[1]))
    got = json.load(open(sys.argv[2]))
    differ = 0
    for key, value in want.items():
        if got.get(key) != value:
            differ += 1
            print(f"!! {key}\n     unity   {value}\n     unbound {got.get(key, '(missing)')}")
    for key in got:
        if key not in want:
            differ += 1
            print(f"!! {key}: only in Unbound: {got[key]}")
    print(f"{differ} of {len(want)} differ")
    sys.exit(1 if differ else 0)


if __name__ == "__main__":
    main()
