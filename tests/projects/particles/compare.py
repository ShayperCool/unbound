#!/usr/bin/env python3
"""Compares two particles probe recordings (Unity's reference, then Unbound's) record by record.

usage: compare.py REFERENCE.jsonl CANDIDATE.jsonl [--scenario PREFIX] [--details N]

Numbers match within an absolute and relative tolerance of 1e-3 (colors within 1 of 255), also
inside logged messages; real-time values (unscaledDeltaTime) are not compared.
Particles are compared in buffer order. The random_* scenarios (and the cone volume and donut
shapes) place particles with Unity's random generator, so only their counts, lifetimes and sizes
are compared.
"""
import json
import re
import sys

TOLERANCE = 1e-3
NUMBER = re.compile(r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?")
# Real time, not the engine's.
IGNORED = {"unscaledDeltaTime"}
RANDOM_FIELDS = ("startLife", "startSize", "startColor")
# Placed with Unity's random generator: the cone's length, the donut's tube angle.
RANDOM_SCENARIOS = ("random_", "shape_cone_volume_loop", "shape_donut_loop")


def close(a, b, color=False):
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(close(x, y, color) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return all(k in IGNORED or close(v, b.get(k), color) for k, v in a.items())
    if isinstance(a, str) and isinstance(b, str) and a != b:
        # Messages with numbers in them (collision points): the numbers within tolerance.
        na, nb = NUMBER.findall(a), NUMBER.findall(b)
        return NUMBER.sub("#", a) == NUMBER.sub("#", b) and len(na) == len(nb) and all(close(float(x), float(y)) for x, y in zip(na, nb))
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool):
        if color:
            return abs(a - b) <= 1
        return abs(a - b) <= TOLERANCE + TOLERANCE * max(abs(a), abs(b))
    return a == b


def compare_particles(scenario, a, b):
    if len(a) != len(b):
        return [f"{len(b)} particles, Unity {len(a)}"]
    fields = RANDOM_FIELDS if scenario.startswith(RANDOM_SCENARIOS) else None
    problems = []
    for i, (pa, pb) in enumerate(zip(a, b)):
        for key, va in pa.items():
            if fields and key not in fields:
                continue
            vb = pb.get(key)
            if not close(va, vb, key in ("color", "startColor")):
                problems.append(f"particle {i} {key} {vb}, Unity {va}")
    return problems


def compare(scenario, a, b):
    problems = []
    for key, va in a.items():
        if key in ("scenario", "step"):
            continue
        vb = b.get(key)
        if key == "particles":
            problems += compare_particles(scenario, va, vb or [])
        elif not close(va, vb):
            problems.append(f"{key} {json.dumps(vb)[:200]}, Unity {json.dumps(va)[:200]}")
    return problems


def load(path):
    records = {}
    for line in open(path):
        r = json.loads(line)
        records[(r["scenario"], r["step"])] = r
    return records


def main():
    args = sys.argv[1:]
    prefix, details = "", 3
    if "--scenario" in args:
        i = args.index("--scenario"); prefix = args[i + 1]; del args[i:i + 2]
    if "--details" in args:
        i = args.index("--details"); details = int(args[i + 1]); del args[i:i + 2]
    ref, cand = load(args[0]), load(args[1])
    bad_scenarios = {}
    total = bad = 0
    for key, a in ref.items():
        scenario, step = key
        if not scenario.startswith(prefix):
            continue
        total += 1
        b = cand.get(key)
        problems = ["missing"] if b is None else compare(scenario, a, b)
        if problems:
            bad += 1
            bad_scenarios.setdefault(scenario, []).append((step, problems))
    for key in cand:
        if key not in ref and key[0].startswith(prefix):
            bad += 1
            bad_scenarios.setdefault(key[0], []).append((key[1], ["not in Unity's records"]))
    for scenario, steps in bad_scenarios.items():
        print(f"{scenario}: {len(steps)} record(s) differ")
        for step, problems in steps[:details]:
            print(f"  step {step}: " + "; ".join(problems[:details]) + (f" (+{len(problems) - details})" if len(problems) > details else ""))
    scenarios = {k[0] for k in ref if k[0].startswith(prefix)}
    print(f"{total - bad}/{total} records match; {len(bad_scenarios)} of {len(scenarios)} scenario(s) differ")


if __name__ == "__main__":
    main()
