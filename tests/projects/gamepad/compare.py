#!/usr/bin/env python3
"""Compares Unbound's record of the gamepad probe with the Editor's (see README.md).

    compare.py REFERENCE UNBOUND        compare two records; exits 1 on an unexpected difference

Records are JSON lines (Assets/Scripts/GamepadProbe.cs), one per frame and kind. The pad's
arrival lands a frame or two either way (udev, then SDL, find it in real time): the device lists
are compared, and the frames from the arrival to SETTLED compare only what does not depend on it.
From SETTLED on every record is compared frame by frame (floats to 1e-5).
"""
import json
import sys

SETTLED = 20
ARRIVAL = range(5, SETTLED)
# Unity's embedded SDL holds back a trigger's first move until the pad's next event (the system
# SDL reports it at once): the right trigger pulled to half in frame 104 shows in frame 109 in
# the Editor, in frame 105 in Unbound.
EXPECTED = {("gamepad", f, "rt") for f in range(105, 110)}


def load(path):
    with open(path) as f:
        records = [json.loads(line) for line in f if line.strip()]
    return {(r.pop("case"), r.pop("frame")): r for r in records}


def same(a, b):
    if isinstance(a, float) or isinstance(b, float):
        return isinstance(a, (int, float)) and isinstance(b, (int, float)) and abs(a - b) <= 1e-5
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    return a == b


def main():
    reference, unbound = load(sys.argv[1]), load(sys.argv[2])
    failures, expected, compared = [], [], 0

    # The device lists, in order (the arrival's frame may differ).
    def lists(records):
        return [(f, r["devices"], r["gamepads"], r["joysticks"]) for (k, f), r in sorted(records.items(), key=lambda i: i[0][1]) if k == "devices"]

    ref_lists, our_lists = lists(reference), lists(unbound)
    compared += 1
    if len(ref_lists) != len(our_lists) or not all(same(a[1:], b[1:]) for a, b in zip(ref_lists, our_lists)):
        failures.append(f"device lists: Unity {[(f, len(d)) for f, d, *_ in ref_lists]}, Unbound {[(f, len(d)) for f, d, *_ in our_lists]}")
    else:
        for (rf, *_), (of, *_) in zip(ref_lists, our_lists):
            if rf != of and not (rf in ARRIVAL and of in ARRIVAL):
                failures.append(f"device list changes in frame {of}, Unity's in frame {rf}")

    for key in sorted(set(reference) | set(unbound), key=lambda k: (k[1], k[0])):
        kind, frame = key
        if kind == "devices":
            continue
        ref, ours = reference.get(key), unbound.get(key)
        if frame in ARRIVAL and kind != "create":
            # Before the pad settles only the legacy input that does not depend on it is compared.
            if kind == "legacy" and ref and ours:
                ref, ours = dict(ref, names=None), dict(ours, names=None)
            else:
                continue
        compared += 1
        if ref is None or ours is None:
            failures.append(f"frame {frame} {kind}: only in {'Unbound' if ref is None else 'Unity'}")
            continue
        for field in sorted(set(ref) | set(ours)):
            if same(ref.get(field), ours.get(field)):
                continue
            line = f"frame {frame} {kind}.{field}: Unity {ref.get(field)}, Unbound {ours.get(field)}"
            (expected if (kind, frame, field) in EXPECTED else failures).append(line)

    for line in expected:
        print("expected:", line)
    for line in failures:
        print("DIFF:", line)
    print(f"{compared - len(failures)}/{compared} records match ({len(expected)} expected differences)")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
