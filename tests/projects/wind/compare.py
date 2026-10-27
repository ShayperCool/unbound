#!/usr/bin/env python3
"""compare.py REFERENCE CANDIDATE: the wind probe's records side by side (wind.jsonl in each
directory). Defaults and scenarios compare number by number within a tolerance; the pulse
samples are taken at frame times that differ between the runs, so each candidate sample is
compared with the reference interpolated at its time."""
import json, os, sys

TOLERANCE = 1e-4
PULSE_TOLERANCE = 0.02  # linear interpolation over 0.1 s of the fastest pulse
# Scenarios whose wind pulses: they run at whatever Time.time the frames reached, which differs
# between the runs (the pulse records check the pulse itself).
EXPECTED = {"pulse", "pulse fast"}


def load(directory):
    records = []
    for line in open(os.path.join(directory, "wind.jsonl")):
        line = line.strip()
        if line:
            records.append(json.loads(line))
    return records


def numbers(value, path=""):
    if isinstance(value, bool) or value is None or isinstance(value, str):
        yield path, value
    elif isinstance(value, (int, float)):
        yield path, float(value)
    elif isinstance(value, list):
        for i, v in enumerate(value):
            yield from numbers(v, f"{path}[{i}]")
    elif isinstance(value, dict):
        for k, v in value.items():
            yield from numbers(v, f"{path}.{k}")


def compare(a, b):
    worst, problems = 0.0, []
    fa, fb = dict(numbers(a)), dict(numbers(b))
    for key in fa.keys() | fb.keys():
        x, y = fa.get(key, "<missing>"), fb.get(key, "<missing>")
        if isinstance(x, float) and isinstance(y, float):
            d = abs(x - y)
            worst = max(worst, d)
            if d > TOLERANCE * max(1.0, abs(x)):
                problems.append(f"{key}: {x} vs {y}")
        elif x != y:
            problems.append(f"{key}: {x} vs {y}")
            worst = float("inf")
    return worst, problems


def compare_pulse(a, b):
    if (a["magnitude"], a["frequency"]) != (b["magnitude"], b["frequency"]):
        return float("inf"), ["different zones"]
    ref = a["samples"]
    worst, problems = 0.0, []
    for t, value in b["samples"]:
        if t < ref[0][0] or t > ref[-1][0]:
            continue
        k = next(i for i in range(1, len(ref)) if ref[i][0] >= t)
        (t0, v0), (t1, v1) = ref[k - 1], ref[k]
        expected = v0 + (v1 - v0) * (t - t0) / (t1 - t0) if t1 > t0 else v0
        d = abs(value - expected)
        worst = max(worst, d)
        if d > PULSE_TOLERANCE:
            problems.append(f"t {t}: {expected} vs {value}")
    return worst, problems


def main():
    ref, cand = load(sys.argv[1]), load(sys.argv[2])
    by_case = {}
    for i in range(max(len(ref), len(cand))):
        a = ref[i] if i < len(ref) else {"case": "missing"}
        b = cand[i] if i < len(cand) else {"case": "missing"}
        case = a.get("case", "?")
        if case == "pulse" and b.get("case") == "pulse":
            worst, problems = compare_pulse(a, b)
        else:
            worst, problems = compare(a, b)
        name = a.get("name")
        if name in EXPECTED:
            case = "scenario (pulsing, expected to differ)"
        stats = by_case.setdefault(case, [0, 0, 0.0, []])
        stats[0] += 1
        if not problems:
            stats[1] += 1
        stats[2] = max(stats[2], worst)
        if problems and len(stats[3]) < 3:
            stats[3].append(([name] if name else []) + problems[:3])
    total = sum(s[0] for s in by_case.values())
    matched = sum(s[1] for s in by_case.values())
    for case, (n, ok, worst, examples) in by_case.items():
        print(f"{case}: {ok}/{n} match, largest difference {worst:.3g}")
        for e in examples:
            print("    " + "; ".join(e))
    print(f"wind: {matched}/{total} records match")


main()
