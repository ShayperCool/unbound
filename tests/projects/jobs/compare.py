#!/usr/bin/env python3
"""Compares two jobs probe recordings (Unity's reference, then Unbound's) value by value.

usage: compare.py REFERENCE.json CANDIDATE.json [--scenario PREFIX] [--details N]

Exception messages are compared after normalizing what the .NET runtime itself formats
differently from Mono (not the engine): ArgumentException's parameter name suffix and the
NullReferenceException text. Values that differ because of the runtime, not the engine, are
listed in RUNTIME_DIFFERENCES and reported apart.
"""
import json
import re
import sys


# What CoreCLR does differently from Mono in code the packages run, outside the engine:
RUNTIME_DIFFERENCES = {
    # Enum.ToString of a value two names share (Default = NonDeterministic = 0).
    ("burst.compiler", "executionMode"),
    # Marshal.GetFunctionPointerForDelegate: Mono shares one thunk for delegates of the same
    # static method, CoreCLR makes one per delegate (Burst compiles a new delegate each time).
    ("burst.functionPointer", "sameValue"),
    # typeof(int).AssemblyQualifiedName names mscorlib in Mono, System.Private.CoreLib in CoreCLR.
    ("burst.runtime", "hash32Type"),
    ("burst.runtime", "hash64Type"),
}


def normalize(value):
    if not isinstance(value, str):
        return value
    # Mono: "message\nParameter name: x"; CoreCLR: "message (Parameter 'x')".
    value = re.sub(r"\nParameter name: (\w+)", r" (Parameter '\1')", value)
    value = value.replace("Object reference not set to an instance of an object.", "Object reference not set to an instance of an object")
    return value


def main():
    args = sys.argv[1:]
    prefix, details = "", 8
    if "--scenario" in args:
        i = args.index("--scenario"); prefix = args[i + 1]; del args[i:i + 2]
    if "--details" in args:
        i = args.index("--details"); details = int(args[i + 1]); del args[i:i + 2]
    ref = json.load(open(args[0]))
    cand = json.load(open(args[1]))
    total_bad = 0
    values_ok = values_total = 0
    runtime = []
    for scenario, values in ref.items():
        if not scenario.startswith(prefix):
            continue
        other = cand.get(scenario)
        if other is None:
            print(f"{scenario}: MISSING in candidate")
            total_bad += 1
            values_total += len(values)
            continue
        problems = []
        for key, a in values.items():
            values_total += 1
            if key not in other:
                problems.append((key, a, "<missing>"))
                continue
            b = other[key]
            if (scenario, key) in RUNTIME_DIFFERENCES and normalize(a) != normalize(b):
                runtime.append((scenario, key, a, b))
                continue
            if isinstance(a, float) or isinstance(b, float):
                if isinstance(a, (int, float)) and isinstance(b, (int, float)) and abs(a - b) <= 1e-4 * max(1.0, abs(a)):
                    values_ok += 1
                    continue
            if normalize(a) != normalize(b):
                problems.append((key, a, b))
            else:
                values_ok += 1
        for k in other:
            if k not in values:
                problems.append((k, "<missing>", other[k]))
        if not problems:
            print(f"{scenario}: ok ({len(values)} values)")
            continue
        total_bad += 1
        print(f"{scenario}: {len(problems)}/{len(values)} differ")
        for key, a, b in problems[:details]:
            print(f"    {key}: unity {a!r}\n    {' ' * len(key)}  unbound {b!r}")
    for scenario in cand:
        if scenario.startswith(prefix) and scenario not in ref:
            print(f"{scenario}: only in candidate {cand[scenario]}")
            total_bad += 1
    if runtime:
        print("\nruntime differences (CoreCLR vs Mono, not the engine):")
        for scenario, key, a, b in runtime:
            print(f"    {scenario}/{key}: unity {a!r}  unbound {b!r}")
    print(f"\n{values_ok}/{values_total - len(runtime)} values match ({len(runtime)} runtime differences); {total_bad} scenario(s) differ")
    return 1 if total_bad else 0


if __name__ == "__main__":
    sys.exit(main())
