#!/usr/bin/env python3
"""Compares Unbound's record of the input probe with the Editor's (see README.md).

    compare.py args REFERENCE          print the `unbound run` options that play the probe's steps
    compare.py REFERENCE UNBOUND        compare two records; exits 1 on a difference

Records are JSON lines (Assets/Scripts/ProbeLog.cs). What is compared, frame by frame from
frame 4 on (frames 1-3 hold the Editor driver's calibration): the raw events the native layer
queued (type, device, format and state bytes, characters), the actions' phases, values and
controls, what each PlayerInput reported, the devices' state, the uGUI events and values, the
selection, and the start-up records (devices, descriptions, device commands, key names, the
legacy Input class, settings). Times are compared relative to frames: an event's time must lie
between the start of the previous frame and the start of the frame that processes it, and an
action's time is the time of the event that triggered it (a hold performs at its start time plus
the hold time, which must fall in the frame the hold performs in).
"""
import json
import sys

FIRST_FRAME = 4
# Records whose fields are compared exactly (minus the listed volatile ones).
COMPARED = {
    "event": ("time", "id"),
    "action": ("time", "start", "duration"),
    "player": ("time",),
    "keyboard": (),
    "mouse": (),
    "ui": (),
    "click": (),
    "slider": (),
    "selected": (),
    "text": (),
    "deviceChange": (),
}
STARTUP = ("device", "ioctl", "keys", "legacy", "playerInput", "scrollPerTick", "playerLoop")


def load(path):
    with open(path) as f:
        return [json.loads(line) for line in f if line.strip()]


def steps(reference):
    meta = next(r for r in reference if r["k"] == "meta")
    # "F:kind args" tokens: a step's arguments follow its kind.
    tokens = meta["steps"].split(" ")
    out = []
    i = 0
    while i < len(tokens):
        frame, kind = tokens[i].split(":")
        out.append((int(frame), kind, tokens[i + 1]))
        i += 2
    return out


def unbound_args(reference):
    """The probe's frames are Time.frameCount values; `unbound run` counts frames from 0."""
    args = []
    for frame, kind, value in steps(reference):
        at = frame - 1
        if kind == "move":
            args.append(f"--move {at}:{value}")
        elif kind in ("press", "release"):
            args.append(f"--{kind} {at}:{value}")
        elif kind == "scroll":
            args.append(f"--scroll {at}:{value}")
        elif kind == "type":
            args.append(f"--type {at}:{value}")
    return " ".join(args)


def strip(record, volatile):
    return {k: v for k, v in record.items() if k not in volatile}


def by_frame(records):
    """Records by frame. What a hold's timeout performs happens at a time, not a frame (the
    Editor's frames are not 1/60 s): those records are compared apart, their time is checked by
    check_times."""
    frames = {}
    timeouts = set()
    for r in records:
        if r["k"] == "action" and r.get("interaction") == "HoldInteraction" and r["phase"] == "performed":
            timeouts.add((r["f"], r["action"].split("/")[-1]))
    for r in records:
        if r["k"] not in COMPARED or r["f"] < FIRST_FRAME:
            continue
        action = r.get("action", "").split("/")[-1]
        timed = (r["f"], action) in timeouts and (
            r["k"] == "action" and r["phase"] == "performed"
            or r["k"] == "player" and (r.get("phase") == "Performed" or r.get("pressed") is True))
        key = f"timeout {action}" if timed else r["f"]
        record = strip(r, COMPARED[r["k"]])
        if timed:
            record = strip(record, ("f",))
        frames.setdefault(key, []).append(record)
    return frames


def frame_starts(records):
    return {r["f"]: r["realtime"] for r in records if r["k"] == "frame"}


def check_times(name, records, problems):
    """Event times between the previous frame's start and the processing frame's start."""
    starts = frame_starts(records)
    for r in records:
        if r["k"] != "event" or r["f"] < FIRST_FRAME:
            continue
        lo, hi = starts.get(r["f"] - 1), starts.get(r["f"])
        if lo is None or hi is None:
            continue
        if not (lo - 1e-4 <= r["time"] <= hi + 1e-4):
            problems.append(f"{name}: frame {r['f']}: event time {r['time']} outside ({lo}, {hi}]")
    events = {}
    for r in records:
        if r["k"] == "event":
            events.setdefault(r["f"], []).append(r["time"])
    for r in records:
        if r["k"] != "action" or r["f"] < FIRST_FRAME or r["control"] is None:
            continue
        times = events.get(r["f"], [])
        if r["interaction"] == "HoldInteraction" and r["phase"] == "performed":
            # Performed by the timeout: start + hold time lies in the frame.
            lo, hi = starts.get(r["f"] - 1), starts.get(r["f"])
            due = r["start"] + 0.4
            if lo is not None and not (lo - 1e-3 <= due <= hi + 0.05):
                problems.append(f"{name}: frame {r['f']}: hold performed at {due} outside the frame")
            continue
        if r["value"] == "null" or not times:
            continue
        if not any(abs(t - r["time"]) < 1e-6 for t in times):
            problems.append(f"{name}: frame {r['f']}: {r['action']} {r['phase']} at {r['time']} matches no event of the frame")


def compare(reference, unbound):
    problems = []
    def startup(records, kind):
        out = [strip(r, ("f",)) for r in records if r["k"] == kind]
        if kind == "playerLoop":
            # The phase the input updates run in (where the rest of the loop matches is not
            # this probe's business).
            out = [r for r in out if r["phase"] == "PreUpdate"]
        return out

    for kind in STARTUP:
        a = startup(reference, kind)
        b = startup(unbound, kind)
        if a != b:
            for x, y in zip(a + [None] * len(b), b + [None] * len(a)):
                if x != y:
                    problems.append(f"start-up {kind}: Editor {json.dumps(x)} / Unbound {json.dumps(y)}")
                    break
    # Entering Play mode: what happens in the first two frames, in order.
    def first_frames(records):
        kinds = ("update", "lateUpdate", "deviceChange", "playMode", "selected", "ui")
        return [strip(r, ()) for r in records if r["k"] in kinds and r["f"] <= 2]
    a, b = first_frames(reference), first_frames(unbound)
    if a != b:
        problems.append(f"first frames: Editor {json.dumps(a)} / Unbound {json.dumps(b)}")
    for name, records in (("Editor", reference), ("Unbound", unbound)):
        check_times(name, records, problems)
    ra, rb = by_frame(reference), by_frame(unbound)
    matched = 0
    for frame in sorted(set(ra) | set(rb), key=lambda k: (isinstance(k, str), str(k).zfill(8))):
        a, b = ra.get(frame, []), rb.get(frame, [])
        if a == b:
            matched += len(a)
            continue
        for x in a:
            if x not in b:
                problems.append(f"frame {frame}: only the Editor: {json.dumps(x)}")
        for y in b:
            if y not in a:
                problems.append(f"frame {frame}: only Unbound: {json.dumps(y)}")
        if sorted(map(json.dumps, a)) == sorted(map(json.dumps, b)):
            problems.append(f"frame {frame}: same records in another order")
    total = sum(len(v) for v in ra.values())
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
    print(f"{matched}/{total} frame records of the Editor matched; {len(problems)} differences")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
