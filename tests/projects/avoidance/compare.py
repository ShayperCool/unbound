#!/usr/bin/env python3
"""Compares Unbound's record of the avoidance probe with the Editor's (see README.md).

    compare.py REFERENCE UNBOUND [-v]

Each line of a record is one agent of one configuration in one frame: its position, velocity,
desired velocity (the velocity its avoidance chose), next position and steering target. A record
matches when every value is within 1e-3 (positions) or 2e-3 (velocities) of the Editor's.
residuals.json ({reason: ["set config agent frame", ...]}) lists known differing records: each is
still printed and counted, and does not fail the set.
"""
import json
import math
import sys
from pathlib import Path


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


def residuals(name):
    """{(config, agent, frame): reason} listed for the set <name>."""
    path = Path(__file__).with_name("residuals.json")
    known = {}
    for reason, keys in (json.loads(path.read_text()) if path.exists() else {}).items():
        for key in keys:
            set_name, *rest = key.split()
            if set_name == name:
                known[tuple(map(int, rest))] = reason
    return known


def main():
    ref, ours = load(sys.argv[1]), load(sys.argv[2])
    verbose = "-v" in sys.argv
    known = residuals(Path(sys.argv[1]).stem)
    tol = {"p": 1e-3, "n": 1e-3, "st": 1e-3, "v": 2e-3, "dv": 2e-3}
    bad_configs = set()
    ok = listed = 0
    for key in sorted(ref):
        a, b = ref[key], ours.get(key)
        good = b is not None and all(math.dist(a[k], b[k]) <= tol[k] for k in a)
        ok += good
        if good:
            continue
        if key in known:
            listed += 1
            print("RESIDUAL", key, "unity", a, "unbound", b, "--", known[key])
            continue
        bad_configs.add(key[0])
        if verbose:
            print("DIFF", key, "unity", a, "unbound", b)
    print(f"{ok}/{len(ref)} records match; {len(bad_configs)} configuration(s) differ; {listed} listed residual record(s)")
    sys.exit(1 if bad_configs else 0)


if __name__ == "__main__":
    main()
