# Float operation and rounding controls

Two independent Unity 6000.0.71f1 Linux Editor captures per Release/Debug
profile agree on 58 records per profile. Both pairs use the existing NVIDIA
GPU display. The sources match all four captured projects. The probe records
four input contexts, arithmetic and boolean results, rounding boundaries,
eight own method bodies and the public code-optimization mode.

```bash
python3 tests/projects/texturepixels/operation_control.py editor --release 6000.0.71f1 --profile release --label fresh-label
python3 tests/projects/texturepixels/operation_control.py publish --release 6000.0.71f1 --profile release --label fresh-label
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/texturepixels/operation_control.py check --release 6000.0.71f1 --profile release
```

Use `--profile debug` for the separate Debug Editor reference. The check replays
the supplied runtime's compiler mode and compares **all** records, including IL
and mode; it does not switch that runtime's compiler or filter those differences.
The Release CoreCLR replay matches 53/58 records. Five differences are add,
multiply, divide, an optimized local and a boolean comparison. For example,
`16777216f + 1f - 16777216f` is 1 in both Editor modes and 0 on CoreCLR;
`a + b == a` for those first two operands is false in both Editor modes and true
on CoreCLR. The explicit-cast comparison is true in all three contexts.
`4097f * 4097f - 16785408f` is 1 versus 0. Division followed by subtraction
(`20f / 37f - 2f`) differs by one float step (BFBACF91 versus BFBACF92).

Explicit casts, volatile-field stores, array stores and non-inlined float returns
round before the following subtraction in these controls. The Release local is
optimized away and remains wide; Debug emits a float local store and rounds.
Subtraction and remainder controls agree on these inputs, which does not establish
their width for all operands. NaN, infinity, denormals, inlining decisions,
exception paths, generic methods and other platforms remain unmeasured.

Those counts describe CoreCLR's float evaluation. With the script float rule
(tests/projects/floatrounding) every numeric and boolean record matches in both modes on macOS;
the remaining records (Release 6, Debug 5) are `gameIL/` bodies of converted methods, which show
the IL as loaded, with its conversions.

The original 62-record arithmetic reference remains unchanged. This supplement
keeps raw discrepancies visible and does not replace the texturepixels gate.

[C# 8.3.7](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/types#837-floating-point-types)
permits wider intermediates; it does not promise them. Unity documents the
[Debug/Release command-line switches](https://docs.unity3d.com/6000.0/Documentation/Manual/EditorCommandLineArguments.html).
The [Mono manual](https://github.com/mono/mono/blob/main/man/mono.1) describes
`float32`. A separate same-IL CPU experiment with the bundled Mono JIT produced
wide results with `--optimize=-float32`, and single results with its default,
`--optimize=float32` and interpreter. CoreCLR 10.0.12 ran that exact assembly with single results in default,
tiering-disabled and minimum-optimization runs.
These observations do not establish a universal Mono default.

The current C# compilation exposes optimization/debug options, with no suitable
[precision switch](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/code-generation).
No suitable CoreCLR runtime knob was found in the measured modes. Matching this
behavior should cover game and package code because it changes branches, but
requires a general typed-IL or runtime policy preserving conversions, casts,
stores, calls and control-flow merges, including precompiled assemblies. The
current IL postprocessor covers source compilation outputs only. This is an
M-sized compiler/runtime follow-up; no expression-specific rewrite is applied.

The CPU control can be rerun with explicit installed toolchain paths. It compiles
one assembly and executes those exact bytes under four Mono and three CoreCLR
modes, storing every output under Library/Unbound or the global probe cache:

```bash
python3 tests/projects/texturepixels/ArithmeticControl/Operations/runtime_control.py --mono /editor/Data/MonoBleedingEdge/bin/mono --mcs /editor/Data/MonoBleedingEdge/lib/mono/4.5/mcs.exe --dotnet /sdk/dotnet --framework-version 10.0.12 --label fresh-label
```
