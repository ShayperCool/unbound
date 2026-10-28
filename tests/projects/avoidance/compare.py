#!/usr/bin/env python3
"""Compares Unbound's record of the avoidance probe with the Editor's (see README.md).

    compare.py REFERENCE UNBOUND [-v]

Each line of a record is one agent of one configuration in one frame: its position, velocity,
desired velocity (the velocity its avoidance chose), next position and steering target. A record
matches when every value is within 1e-3 (positions) or 2e-3 (velocities) of the Editor's.
"""
import math
import sys


def load(path):
    out = {}
    for line in open(path):
        t = line.split()
        if not t:
            continue
        rec = {}
        for x in t[3:]:
            for pre in ("dv", "st", "p", "v", "n"):
                if x.startswith(pre):
                    rec[pre] = tuple(map(float, x[len(pre):].split(",")))
                    break
        out[(int(t[0]), int(t[1]), int(t[2]))] = rec
    return out


def main():
    ref, ours = load(sys.argv[1]), load(sys.argv[2])
    verbose = "-v" in sys.argv
    tol = {"p": 1e-3, "n": 1e-3, "st": 1e-3, "v": 2e-3, "dv": 2e-3}
    bad_configs = set()
    ok = 0
    for key in sorted(ref):
        a, b = ref[key], ours.get(key)
        good = b is not None and all(math.dist(a[k], b[k]) <= tol[k] for k in a)
        ok += good
        if not good:
            bad_configs.add(key[0])
            if verbose:
                print("DIFF", key, "unity", a, "unbound", b)
    print(f"{ok}/{len(ref)} records match; {len(bad_configs)} configuration(s) differ")
    sys.exit(1 if bad_configs else 0)


if __name__ == "__main__":
    main()
