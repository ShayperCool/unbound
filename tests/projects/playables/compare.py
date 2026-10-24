#!/usr/bin/env python3
"""Compares two playables probe recordings (Unity's reference, then Unbound's) value by value.

Numbers compare within a tolerance, also inside event text ("t=0.3 pos=(1, 2, 0)"): texts match
when their words match and their numbers agree.

usage: compare.py REFERENCE.json CANDIDATE.json [--scenario PREFIX] [--details N] [--tolerance T]
"""
import json
import math
import re
import sys

NUMBER = re.compile(r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?")


def numbers_close(a, b, tolerance):
    return abs(a - b) <= tolerance * max(1.0, abs(a))


def text_diff(a, b, tolerance):
    """0 when the texts agree, else the largest number difference (inf for other changes)."""
    if a == b:
        return 0.0
    if NUMBER.sub("#", a) != NUMBER.sub("#", b):
        return math.inf
    worst = 0.0
    for x, y in zip(NUMBER.findall(a), NUMBER.findall(b)):
        fx, fy = float(x), float(y)
        if not numbers_close(fx, fy, tolerance):
            worst = max(worst, abs(fx - fy))
    return worst


def main():
    args = sys.argv[1:]
    prefix, details, tolerance = "", 8, 1e-4
    if "--scenario" in args:
        i = args.index("--scenario"); prefix = args[i + 1]; del args[i:i + 2]
    if "--details" in args:
        i = args.index("--details"); details = int(args[i + 1]); del args[i:i + 2]
    if "--tolerance" in args:
        i = args.index("--tolerance"); tolerance = float(args[i + 1]); del args[i:i + 2]
    ref = json.load(open(args[0]))
    cand = json.load(open(args[1]))
    total_bad = 0
    for scenario, values in ref.items():
        if not scenario.startswith(prefix):
            continue
        other = cand.get(scenario)
        if other is None:
            print(f"{scenario}: MISSING in candidate")
            total_bad += 1
            continue
        problems = []
        for key, a in values.items():
            if key not in other:
                problems.append((math.inf, key, a, "<missing>"))
                continue
            b = other[key]
            if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
                if not numbers_close(a, b, tolerance):
                    problems.append((abs(a - b), key, a, b))
            elif isinstance(a, str) and isinstance(b, str):
                diff = text_diff(a, b, tolerance)
                if diff > 0:
                    problems.append((diff, key, a, b))
            elif a != b:
                problems.append((math.inf, key, a, b))
        extra = [k for k in other if k not in values]
        for k in extra:
            problems.append((math.inf, k, "<missing>", other[k]))
        if not problems:
            print(f"{scenario}: ok ({len(values)} values)")
            continue
        total_bad += 1
        worst = max(p[0] for p in problems)
        print(f"{scenario}: {len(problems)}/{len(values)} differ (worst {worst:.4g})")
        shown = sorted(problems, key=lambda p: list(values.keys()).index(p[1]) if p[1] in values else 1e9)
        for diff, key, a, b in shown[:details]:
            print(f"    {key}: unity {a!r}  unbound {b!r}")
    print(f"\n{total_bad} scenario(s) differ")
    return 1 if total_bad else 0


sys.exit(main())
