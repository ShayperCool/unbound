"""Exact image gates with recorded residuals, shared by urp/compare.py (one image) and
particles/compare_images.py (a directory of images).

A residual list is a JSON object {"<key>": {"pixels": N, "worst": W, "reason": "..."}}: that
image may differ from its reference in at most N pixels by at most W (the largest channel
difference, 0-255); every other image must match pixel for pixel. A listed image that matches
again is named (NOW MATCHES) so that its entry can be withdrawn.
"""
import json


def load(path):
    """The residual list at `path`, or none."""
    if not path:
        return {}
    with open(path) as handle:
        residuals = json.load(handle)
    for key, entry in residuals.items():
        if not (isinstance(entry, dict) and isinstance(entry.get("pixels"), int) and isinstance(entry.get("worst"), int)
                and entry.get("reason")):
            raise SystemExit(f"{path}: {key}: an entry needs integer pixels and worst and a reason")
    return residuals


def judge(key, differing, worst, residuals):
    """(passes, line to print or None) for an image of `differing` pixels differing by up to `worst`."""
    allowed = residuals.get(key)
    if differing == 0:
        return True, (f"NOW MATCHES {key}: its recorded residual can be withdrawn" if allowed else None)
    if allowed is not None and differing <= allowed["pixels"] and worst <= allowed["worst"]:
        return True, f"recorded {key}: {differing} pixels differ by up to {worst} ({allowed['reason']})"
    bound = f"recorded {allowed['pixels']} pixels up to {allowed['worst']}" if allowed else "not recorded"
    return False, f"FAIL {key}: {differing} pixels differ by up to {worst} ({bound})"


def take_gate(args):
    """Removes --gate [RESIDUALS.json] from `args`: (whether gated, the residual list's path or None).
    The argument after --gate is the list only when it ends in .json."""
    if "--gate" not in args:
        return False, None
    at = args.index("--gate")
    path = None
    if at + 1 < len(args) and args[at + 1].endswith(".json"):
        path = args[at + 1]
        del args[at + 1]
    del args[at]
    return True, path
