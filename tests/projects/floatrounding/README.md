# Float rounding in script code

Where script code's float arithmetic keeps a wider intermediate and where it is rounded to float,
in the Unity 6000.0.71f1 Editor, which runs C# on Mono. Every control is a static method taking its
operands as arguments (no compiler can fold them). Most use a = 16777216, b = 1, c = -16777216:
`(a + b) + c` is 1 when `a + b` keeps its exact value through the construct under test and 0 when
it is rounded to float there. Others use operands that overflow or underflow float (1e30, 1e-30),
a quotient one float step from its float result (20 / 37), or a square root of a value float
cannot hold. Randomized rows (`fuzz/`) record 24 results each of common expression shapes and of
Unity's managed math API (Mathf, Vector3, Quaternion, Matrix4x4), with operands built from random
bits. `api/` rows record 24 results each of more of the math methods Unity implements in C# (Mathf,
Vector2/3/4, Quaternion, Matrix4x4, Color, Color32, Plane, Rect, Pose, GeometryUtility), `native/`
rows of the math methods Unity binds to native code (Quaternion Slerp, Lerp, Euler, eulerAngles,
LookRotation, AngleAxis, FromToRotation, Inverse; Vector3 Slerp, RotateTowards, OrthoNormalize;
Matrix4x4 TRS, inverse, determinant, rotation, lossyScale; Bounds SqrDistance, Contains,
ClosestPoint). `il/` rows record each control's compiled instructions, with tokens shown as the names
they resolve to.

```bash
python3 tests/projects/floatrounding/run.py record --profile release --label native1   # this host's Editor
python3 tests/projects/floatrounding/run.py publish native1 native2 --profile release
python3 tests/projects/floatrounding/run.py gate --profile release                     # the engine
```

`--profile debug` selects the Debug code optimization mode (`-debugCodeOptimization` in the
Editor, `UNBOUND_CODE_OPTIMIZATION=debug` in the engine). The Linux x86_64 Editor and the macOS arm64
Editor (both in batch mode without a graphics device) each recorded two captures per mode; all four
captures of a mode are byte-identical, so one reference per mode serves both hosts.

## Measured rule

In both modes and on both hosts, a float value on the evaluation stack is a double:

- arithmetic chains (`a + b + c`, `a * b + c`, `a / b - c`, `%`, negation) keep double
  intermediates; `1e30f * 1e30f * 1e-30f` is 1e30 rather than infinity, `1e-30f * 1e-30f * 1e30f`
  is 1e-30 rather than 0;
- comparisons and branches compare the double (`a + b == a` is false);
- conversions convert the double: `(int)(a + b)`, `checked((int)(a + b))`, `(long)(a + b)`,
  `(double)(a + b)` and `Math.Sqrt(a + b)` see 16777217;
- a value left on the stack keeps its precision across calls and branch joins (a conditional
  expression's value, a value the compiler keeps on the stack instead of a local);
- `box` immediately followed by `unbox.any` of float keeps the value.

It is rounded to float where the IL stores it into a float location: a local, an argument, a static,
instance or volatile field, an array element, an indirect store (`ref` locals, spans, stack
allocations), a struct field, a box that is kept, a field of a closure, an iterator or an async
state machine, every call argument and every return value (methods the runtime inlines, generic and
interface methods, delegates and `Mathf`/`Math`/`MathF` float overloads alike), and an explicit
`(float)` cast or integer-to-float conversion.

The two modes differ only where the compiler's output does: Debug keeps a store into each declared
local, so `float t = a + b; return t + c;` rounds `t` in Debug and not in Release, where the local
is gone from the IL. The `il/` rows show each case.

Unity's own C# for its math structs follows the same rule in the Editor (`Vector3.Dot` and
`sqrMagnitude` sum double products, for example), and every local these methods declare rounds:
`Vector3.Distance` rounds each coordinate difference, `ClampMagnitude` each normalized coordinate.
The methods bound to native code do not follow this rule (`native/` rows); in these captures they
give the same values on both hosts.

## Engine

For releases whose compatibility profile has this rule (`Compat.ScriptFloatsWide`, 6000.0.71f1),
`managed/Unbound.Host/WideFloats.cs` converts the float loads of script, package and plugin
assemblies to double when they load, under the expressions whose result the double evaluation can
change: an operation on another operation's result, or a result that is compared, converted or
used in any other way than rounding it to float. A single operation on float values that is
rounded straight away gives the same float either way (double has more than twice float's
precision), so it is left as it is. The runtime rounds a double stored into a float location or
passed as a float argument (ECMA-335 III.1.1.1, III.1.6). `[BurstCompile]` methods and the
`Execute` of `[BurstCompile]` job structs keep float arithmetic; the other methods of such types
are script code and are converted. A hot-reload edit is patched in place unless an edited method
needs conversions or calls converted math (below); then the session restarts (the conversions do
not fit the edit's method bodies).

A game assembly's calls to the math methods Unity implements in C# go to copies of those methods
added to the assembly, converted as script code is (`managed/Unbound.Host/WideFloats.EngineMath.cs`);
the engine's own calls keep float32. The copies come from a second build of the engine's math
sources without IL optimization (`managed/Unbound.ScriptMath`), where every declared local is a
store as in the Editor. Methods Unity binds to native code keep the engine's float32 implementation.

The gate matches every control in both modes. Its residuals (`residuals.json`):

- `il/` rows of the expressions that were converted: reflection on a method body shows the IL as
  loaded, with the conversions. In the measured runs the instructions apart from them (and the
  shifted branch offsets) were the Editor's; the gate lists these rows as residuals and does not
  compare them further.
- `il/` rows of controls that call converted math: their calls name the copies.
- `native/` rows: Unbound's float32 implementations of Unity's native math do not round in the
  Editor's order (queued), and the two C# methods that use a native result (`TransformPlane` uses
  the inverse, `Quaternion.RotateTowards` uses `SlerpUnclamped`).

A float-heavy benchmark on macOS arm64 (best of 7 runs, tiered JIT warm): a particle-style loop over
a three-float struct with operators, `Dot` and `sqrMagnitude` takes 16 ms instead of 10.4 ms; a
scalar polynomial-and-lerp loop 108 ms instead of 89 ms. Converting every float load of every
method would take 34 ms and 124 ms: the larger operators then no longer get inlined. A per-object
update loop over Unity's math API (normalize, Lerp, quaternion products, TRS, MultiplyPoint3x4,
Dot, Distance, Cross) takes 32.6 ms with the copies instead of 25 ms.

Not measured: other Unity releases, NaN payloads, code Burst compiles in the Editor (and the
methods such code calls, which are converted like any other), and Unity's managed code outside its
math structs (RectTransformUtility, UI Toolkit, ...), which stays engine code in float32.
