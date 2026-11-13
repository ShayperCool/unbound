# Game float arithmetic control

Two independent Unity 6000.0.71f1 Editor captures on the existing NVIDIA display
agree on all 62 values. This supplement isolates the expressions that supply the
texturepixels RGFloat volume. It records bit patterns before any texture stores,
with eight integer inputs, explicit casts, a volatile field, method-call boundaries,
an optimized local and an explicit double calculation. It also records the probe's
own four method bodies through the public reflection API and the Release code mode.

```bash
python3 tests/projects/texturepixels/arithmetic_control.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/texturepixels/arithmetic_control.py publish --release 6000.0.71f1 --label fresh-label
UNBOUND_MONO_FLOATS=1 UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/texturepixels/arithmetic_control.py check --release 6000.0.71f1
```

The Editor keeps wider intermediate arithmetic in the measured expressions;
Unbound rounds each float arithmetic operation. Both produce the same Single
conversion/division/subtraction method body for Red. An explicit cast, field store
or non-inlined float return rounds before the later subtraction. The optimized
local does not introduce a storage boundary. Large integer inputs distinguish the
initial float conversion from an explicit conversion to double.

For input 20, Red produces BFBACF91 in the Editor and BFBACF92 in Unbound. The cast,
field and call controls produce BFBACF92 in both. This is a game runtime precision
gap. Changing Texture3D storage or readback would corrupt correctly supplied data.

The check command reports every raw difference. Before the script float rule
(tests/projects/floatrounding) it matched 55/62 values, with all four game-method bodies and the
Release mode key. With the rule, an opt-in of the engine (`UNBOUND_MONO_FLOATS=1`, as in the command above; off by
default since the user decision of 2026-11-13, which gives 55/62 again), every numeric value and the
mode key match (59/62 on macOS); the
`gameIL.*` bodies of the three converted methods show the IL as loaded, with its conversions.
This causal supplement does not replace the parent parity gate, whose three
strict VolumeRGFloat rows per color space remain open. A general compatibility
implementation must preserve input conversions, casts, stores, call boundaries
and optimized expressions across game and precompiled assemblies. That compiler
and runtime work is deferred; no expression-name or texture-name patch is applied.
The Operations supplement now measures Debug/Release modes and more operators;
async state machines and other platform runtimes remain unmeasured.
