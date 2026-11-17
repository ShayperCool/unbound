"""Exact image gates with recorded residuals, shared by urp/compare.py (one image) and
particles/compare_images.py (a directory of images).

A residual list is a JSON object {"<key>": {"pixels": N, "worst": W, "reason": "..."}}: that
image may differ from its reference in at most N pixels by at most W (the largest channel
difference, 0-255); every other image must match pixel for pixel. A listed image that matches
again is named (NOW MATCHES) so that its entry can be withdrawn.
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
import residuals as residual_lists  # noqa: E402


def load(path):
    """The residual list at `path`, or none (tests/residuals.py, with integer image bounds)."""
    if not path:
        return {}
    try:
        residuals = residual_lists.load(path)
    except residual_lists.ResidualListError as error:
        raise SystemExit(str(error))
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
    if residual_lists.admits(allowed, {"pixels": differing, "worst": worst}):
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
