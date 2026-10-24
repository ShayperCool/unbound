#!/usr/bin/env python3
"""Compares Unbound's Editor API record with the Editor's, key by key.

    python3 tests/projects/editor/compare.py tests/projects/editor/Reference/unity-6000.0.71f1.json /tmp/editor.json

Keys in EXPECTED differ by design (the reference is recorded in batch mode, Unbound plays as an
interactive Editor) and are listed apart.
"""
import json
import sys

# The reference Editor runs in batch mode without a person; Unbound's Editor has its game window.
EXPECTED = {
    "env.inBatchMode": "Unbound is an interactive Editor (its game window), the reference a batch-mode one",
    "env.isHumanControllingUs": "the same",
    "cloud.userId.empty": "Unbound signs in to no Unity account",
    # .NET runtime, not engine: CoreCLR's Enum.ToString names a value with several names by
    # another one than Mono (ProcessLevel.Main == UMP_MASTER).
    "env.processLevel": "Enum.ToString of a value with aliases differs between Mono and CoreCLR",
}

# Records Unbound makes that the Editor cannot: Unbound has one domain, so ExitingEditMode reaches
# the subscribers [InitializeOnLoad] registered (in Unity they are in the edit-mode domain).
EXPECTED_EXTRA = {"playModeStateChanged.ExitingEditMode"}


def main():
    reference = json.load(open(sys.argv[1]))
    actual = json.load(open(sys.argv[2]))
    differences, expected, missing = [], [], []
    for key, want in reference.items():
        if key not in actual:
            missing.append(key)
            continue
        got = actual[key]
        if got == want:
            continue
        (expected if key in EXPECTED else differences).append((key, want, got))
    extra = [k for k in actual if k not in reference and k not in EXPECTED_EXTRA]
    for key, want, got in differences:
        print(f"DIFF {key}\n  unity:  {json.dumps(want)}\n  unbound: {json.dumps(got)}")
    for key in missing:
        print(f"MISSING {key}")
    for key in extra:
        print(f"EXTRA {key}: {json.dumps(actual[key])}")
    for key, want, got in expected:
        print(f"expected difference {key}: unity {json.dumps(want)}, unbound {json.dumps(got)} ({EXPECTED[key]})")
    same = len(reference) - len(differences) - len(missing) - len(expected)
    print(f"{same}/{len(reference)} records match; {len(differences)} differ, {len(missing)} missing, {len(expected)} expected differences")
    sys.exit(1 if differences or missing else 0)


if __name__ == "__main__":
    main()
