#!/usr/bin/env python3
"""Compares Unbound's Unity.Properties / com.unity.serialization record with the Editor's, key by key.

    python3 tests/projects/properties/compare.py tests/projects/properties/Reference/unity-6000.0.71f1.json /tmp/properties.json

Keys in EXPECTED differ by design and are listed apart.
"""
import json
import re
import sys

EXPECTED = {
}


def normalize(value):
    """.NET runtime, not engine: CoreCLR words ArgumentException's parameter differently from Mono."""
    if isinstance(value, str):
        return re.sub(r" \(Parameter '([^']*)'\)", r"\nParameter name: \1", value)
    return value

EXPECTED_EXTRA = set()


def main():
    reference = json.load(open(sys.argv[1]))
    actual = json.load(open(sys.argv[2]))
    differences, expected, missing = [], [], []
    for key, want in reference.items():
        if key not in actual:
            missing.append(key)
            continue
        got = normalize(actual[key])
        if got == normalize(want):
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
