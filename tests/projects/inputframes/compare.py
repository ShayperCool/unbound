#!/usr/bin/env python3
"""Compares records of the input-frames probe (see README.md).

    compare.py args REFERENCE           print the `unbound run` options that play the probe's steps
    compare.py REFERENCE OTHER          compare Unbound's record with the Editor's; exits 1 on a difference
    compare.py --native A B             compare two Editor records (the same checks)

Records are JSON lines (Assets/Scripts/ProbeLog.cs). Every record of frames FIRST_FRAME on whose
kind is in COMPARED is compared exactly, in order within its frame, minus the listed clock
fields; every missing and extra record is printed. Times are checked against frames instead: an
input event's time lies after the previous frame's input clock and no later than its own
frame's, and an action's time is the time of an event of its frame (or an earlier one, for what
a fixed update processes later).
"""
import difflib
import json
import sys

# Frames 1-6 hold the Editor driver's calibration moves.
FIRST_FRAME = 8
COMPARED = {
    "legacy": (),
    "inputSystem": (),
    "event": ("time",),
    "action": ("time", "start"),
    "text": (),
    "gui": (),
    "ui": (),
    "onClick": (),
    "onMouse": (),
    "fixed": ("unscaled",),
    "inputUpdate": (),
}


def load(path):
    with open(path) as f:
        return [json.loads(line) for line in f if line.strip()]


def steps(reference):
    meta = next(r for r in reference if r["k"] == "meta")
    out = []
    for step in meta["steps"].split(" "):
        frame, text = step.split(":", 1)
        # Tokens are joined with commas; a move (@X,Y) holds one itself.
        tokens = []
        for part in text.split(","):
            if tokens and tokens[-1][0] == "@" and "," not in tokens[-1]:
                tokens[-1] += "," + part
            else:
                tokens.append(part)
        out.append((int(frame), tokens))
    return out


def unbound_args(reference):
    """The probe's frames are Time.frameCount values; `unbound run` counts frames from 0. A step's
    pointer moves come first, then its presses and releases in their order."""
    args = []
    for frame, tokens in steps(reference):
        at = frame - 1
        keys = [t for t in tokens if t[0] in "+-"]
        for token in tokens:
            if token[0] == "@":
                args.append(f"--move {at}:{token[1:]}")
        if keys:
            args.append(f"--keys {at}:{','.join(keys)}")
    return " ".join(args)


def key(record):
    volatile = COMPARED[record["k"]]
    return json.dumps({k: v for k, v in record.items() if k not in volatile and k != "f"}, sort_keys=True)


def by_frame(records):
    frames = {}
    for r in records:
        if r["k"] in COMPARED and r["f"] >= FIRST_FRAME:
            frames.setdefault(r["f"], []).append(r)
    return frames


def check_times(records, label):
    """Input events between the previous frame's input clock and their own frame's."""
    problems = []
    clock = {r["f"]: r["inputTime"] for r in records if r["k"] == "frame"}
    for r in records:
        if r["k"] != "event" or r["f"] < FIRST_FRAME or r["f"] not in clock:
            continue
        before = clock.get(r["f"] - 1, float("-inf"))
        if not before < r["time"] <= clock[r["f"]] + 1e-9 and r.get("update") != "Fixed":
            problems.append(f"{label} frame {r['f']}: event time {r['time']} outside ({before}, {clock[r['f']]}]")
    return problems


def compare(reference, other, label):
    ref, oth = by_frame(reference), by_frame(other)
    last = max(r["f"] for r in reference if r["k"] == "frame")
    counts = {}
    lines = []
    for frame in range(FIRST_FRAME, last + 1):
        a = [key(r) for r in ref.get(frame, [])]
        b = [key(r) for r in oth.get(frame, [])]
        matcher = difflib.SequenceMatcher(a=a, b=b, autojunk=False)
        matched = set()
        for block in matcher.get_matching_blocks():
            matched.update(range(block.a, block.a + block.size))
        for i, r in enumerate(ref.get(frame, [])):
            total = counts.setdefault(r["k"], [0, 0])
            total[1] += 1
            if i in matched:
                total[0] += 1
        for op, i1, i2, j1, j2 in matcher.get_opcodes():
            if op == "equal":
                continue
            for line in a[i1:i2]:
                lines.append(f"frame {frame}: missing {line}")
            for line in b[j1:j2]:
                lines.append(f"frame {frame}: extra   {line}")
    lines += check_times(other, label)
    for line in lines:
        print(line)
    matched = sum(c[0] for c in counts.values())
    total = sum(c[1] for c in counts.values())
    extra = sum(1 for line in lines if " extra " in line)
    for kind in sorted(counts):
        print(f"{kind}: {counts[kind][0]}/{counts[kind][1]}")
    print(f"{label}: {matched}/{total} reference records matched, {extra} extra, "
          f"{sum(1 for line in lines if 'outside' in line)} time problems")
    return 0 if not lines else 1


def main(argv):
    if len(argv) == 2 and argv[0] == "args":
        print(unbound_args(load(argv[1])))
        return 0
    if len(argv) == 3 and argv[0] == "--native":
        reference, other = load(argv[1]), load(argv[2])
        problems = check_times(reference, "native A")
        for line in problems:
            print(line)
        return compare(reference, other, "native B") or int(bool(problems))
    if len(argv) == 2:
        return compare(load(argv[0]), load(argv[1]), "unbound")
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
