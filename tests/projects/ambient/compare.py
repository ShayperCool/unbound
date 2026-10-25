#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the ambient probe's records (ambient.jsonl in each directory):
each sky's ambient probe within PROBE_TOLERANCE per coefficient (Unbound draws the sky into a
64-texel cube; the Editor's procedural-sky probes come out up to 0.6 % brighter), the rest equal."""
import json, os, sys

PROBE_TOLERANCE = 0.0025


def load(directory):
    return [json.loads(line) for line in open(os.path.join(directory, "ambient.jsonl")) if line.strip()]


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    matched = 0
    for a, b in zip(ref, cand):
        name = a.get("name", a["case"])
        worst = max(abs(x - y) for x, y in zip(a["probe"], b["probe"]))
        problems = []
        if worst > PROBE_TOLERANCE:
            problems.append(f"probe differs by {worst:.4g}")
        for key in ("mode", "sky", "intensity", "reflection", "decode"):
            if key in a and a.get(key) != b.get(key):
                problems.append(f"{key}: {a.get(key)} vs {b.get(key)}")
        if problems:
            print(f"{name}: " + "; ".join(problems))
        else:
            matched += 1
        print(f"  {name}: probe within {worst:.2g}")
    print(f"ambient: {matched}/{max(len(ref), len(cand))} skies match")


main()
