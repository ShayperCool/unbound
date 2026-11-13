# Hash128 values

`Assets/Scripts/Hash128Probe.cs` writes Hash128 values into values.jsonl in `$HASH128_OUT`
(tests/value_probe.py): `Compute` and `Append` over strings (empty, null, ASCII, non-ASCII, and
lengths around the 32-byte and 192-byte steps of the hash), ints, floats (keyed by their bits),
arrays, ranges, lists, native arrays, unmanaged values and raw memory; append chains, also from a
seeded value; `HashUtilities` (AppendHash, quantised vector and matrix hashes, ComputeHash128) and
`HashUnsafeUtilities`; the argument checks (non-blittable elements, bad ranges) with their
messages; parsing, printing and comparing.

Measured in the Unity 6000.0.71f1 Editor (two identical captures on the NVIDIA driver): every
overload hashes the bytes of its input with SpookyHash V2 (128-bit), continuing from the current
value, strings as UTF-8; `Compute` starts from the zero hash.

    export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
    python3 tests/projects/hash128/run.py record --label A
    python3 tests/projects/hash128/run.py record --label B
    python3 tests/projects/hash128/run.py publish A B
    python3 tests/projects/hash128/run.py gate
