#!/usr/bin/env python3
"""Compares two records of the messages probe (JSON lines), frame by frame.

    compare.py REFERENCE RECORD     compare (exit 1 when they differ)
    compare.py args REFERENCE       the `unbound run` options that play the reference's input

Every record is compared exactly, in order within its frame, except: the Editor driver's
calibration record (not in Unbound's), and the fixed-step records (`time`), whose clocks follow
each run's frame times: there the checks are that after the frame's fixed steps the fixed clock is
within one step behind the game clock, that a slow frame's delta is `maximumDeltaTime`, and that
the frame ran as many fixed steps as its delta covers (within one).
"""
import json
import sys

FIXED_STEP = 0.02


def load(path):
    with open(path) as f:
        return [json.loads(line) for line in f if line.strip()]


def parse_steps(reference):
    meta = next(r for r in reference if r["k"] == "meta")
    tokens = meta["steps"].split()
    out = []
    i = 0
    while i < len(tokens):
        frame, kind = tokens[i].split(":", 1)
        value = None
        if kind != "unfocus":
            value = tokens[i + 1]
            i += 1
        out.append((int(frame), kind, value))
        i += 1
    return out


def unbound_args(reference):
    """The probe's frames are Time.frameCount values; `unbound run` counts frames from 0. A focus
    change the Editor reported after frame F (between frames) is `--focus F:...`."""
    args = []
    for frame, kind, value in parse_steps(reference):
        at = frame - 1
        if kind in ("move", "motion"):
            args.append(f"--{kind} {at}:{value}")
        elif kind in ("press", "release"):
            args.append(f"--{kind} {at}:{value}")
    focus = None
    for r in reference:
        if r["k"] == "app" and r["m"] == "Focus" and r["f"] > 0 and r["v"] != focus:
            focus = r["v"]
            args.append(f"--focus {r['f']}:{'on' if focus else 'off'}")
    return " ".join(args)


def by_frame(records):
    frames = {}
    for r in records:
        if r["k"] in ("calibration", "time"):
            continue
        frames.setdefault(r["f"], []).append(r)
    return frames


def check_times(records, name):
    problems = []
    for r in records:
        if r["k"] != "time":
            continue
        f = r["f"]
        lag = r["time"] - r["fixedTime"]
        if not (-1e-4 <= lag < FIXED_STEP + 1e-4):
            problems.append(f"{name} frame {f}: fixed clock {r['fixedTime']} not within a step of {r['time']}")
        if f in (371, 375) and abs(r["deltaTime"] - r["maximumDeltaTime"]) > 1e-4:
            problems.append(f"{name} frame {f}: deltaTime {r['deltaTime']} after a slow frame, maximumDeltaTime {r['maximumDeltaTime']}")
        if abs(r["fixedSteps"] - r["deltaTime"] / FIXED_STEP) > 1.0001:
            problems.append(f"{name} frame {f}: {r['fixedSteps']} fixed steps for a delta of {r['deltaTime']}")
    return problems


def compare(reference, record):
    problems = check_times(reference, "reference") + check_times(record, "record")
    a, b = by_frame(reference), by_frame(record)
    total = sum(len(v) for v in a.values())
    matched = 0
    for frame in sorted(set(a) | set(b)):
        ra, rb = a.get(frame, []), b.get(frame, [])
        for i in range(max(len(ra), len(rb))):
            x = ra[i] if i < len(ra) else None
            y = rb[i] if i < len(rb) else None
            if x == y:
                matched += 1
            else:
                problems.append(f"frame {frame}: expected {json.dumps(x)}, got {json.dumps(y)}")
                break
    return matched, total, problems


def main():
    if len(sys.argv) == 3 and sys.argv[1] == "args":
        print(unbound_args(load(sys.argv[2])))
        return 0
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    matched, total, problems = compare(load(sys.argv[1]), load(sys.argv[2]))
    for p in problems:
        print(p)
    print(f"{matched}/{total} records of the Editor matched; {len(problems)} differences")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
