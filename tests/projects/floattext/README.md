# Float and double text

`Assets/Scripts/FloatTextProbe.cs` writes the text of float and double values into values.jsonl
in `$FLOAT_TEXT_OUT` (tests/value_probe.py), keyed by the value's bits and the format: values
m × 2^e (m from a fixed list of odd numbers, e from -30 to 2 for floats and -60 to 2 for doubles)
whose exact decimal expansion is short, so that many of them lie exactly halfway between two
texts at the printed precision, two neighbours of such a value, and some negatives, in the
default, `R`, `G`, `G<n>`, `E<n>`, `F<n>`, `N3` and `P1` formats; also through string.Format,
string concatenation and Vector3.ToString.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, NVIDIA GPU display; the
values depend on neither the OS nor the graphics device): Mono prints 7 (float) or 15 (double)
significant digits by default and tries those before 9 or 17 in `R`, and a value exactly halfway
between two texts at the printed precision rounds away from zero: `245f / 1024` (0.2392578125)
is "0.239257813" in `R` and `G9`, `1f / 2048` is "0.0004882813", 2^-22 is
"2.38418579101563E-07" and 2^-25 in `R` is "2.9802322387695313E-08". .NET rounds such ties to the
even digit (`Unbound.MonoNumberFormat`).

    export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
    python3 tests/projects/floattext/run.py record --label A
    python3 tests/projects/floattext/run.py record --label B
    python3 tests/projects/floattext/run.py publish A B
    python3 tests/projects/floattext/run.py gate
