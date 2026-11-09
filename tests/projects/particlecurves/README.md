# particlecurves

What `ParticleSystem.MinMaxCurve` keeps and returns in Unity 6000.0.71f1: the struct on its own,
the same values written to and read back from module properties (with each module's
`*Multiplier` shortcut), values authored in a prefab, script fields of the type, and simulations
that use them. Each record is one JSON line `{"key", "value": [numbers]}`; the reference is
`Reference/unity-6000.0.71f1.jsonl`.

    python3 tests/projects/particlecurves/run.py native --label ref1    # Editor capture (GPU, job pool)
    python3 tests/projects/particlecurves/run.py publish ref1 ref2      # two identical captures
    python3 tests/projects/particlecurves/run.py check                  # Unbound run + compare (the gate)

`run.py create` authored `Assets/Resources/Authored.prefab` once (`ProbeRunner.Create`), and
`run.py create-gradients` authored `Assets/Resources/Gradients.prefab` (`ProbeRunner.CreateGradients`):
MinMaxGradient script fields and a system with authored two-gradient and random-color values.
`Assets/Scripts/GradientProbe.cs` records `gradient/*` (the struct, module colour properties,
script fields, particle start and current colours in every mode) and `deprecated/*` (the
deprecated `ParticleSystem.start*` properties on values in each mode). The
`outofrange` system's scalars were then set out of range in the YAML by hand (the Editor bounds
them when it writes), to record what loading does with such data. Run output stays under
`$XDG_CACHE_HOME/unbound/validation/runs/particlecurves/`. The comparator checks every key and value
at the particle probes' bound (0.0002 absolute plus 0.0002 relative), with the differences in
`known.json`. The reference has 3004 records (two byte-identical GPU Editor captures; the 2726
earlier records are unchanged).

Every record is compared strictly. The four `sim/{0..3}/c` records include the burst set at the
1 s duration, which fires when the step from 0.75 s to 1.25 s crosses the loop end (the loop-end
burst rule is measured in tests/projects/particleburstloop).

## Documented contract

| Official Unity 6000.0 source | What it says |
| --- | --- |
| [ParticleSystem.MinMaxCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MinMaxCurve.html) | Properties `constant`, `constantMin`, `constantMax`, `curveMultiplier`, `curve`, `curveMin`, `curveMax`, `mode`; `Evaluate` "based on what mode it is in". The examples assign the whole struct back after changing a curve. Nothing about what a module returns, about separate or shared storage, or about bounds. |
| [MainModule.startSizeMultiplier](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-startSizeMultiplier.html) (and the other `*Multiplier` properties) | "A multiplier for the initial size"; changing it "is more efficient than accessing the entire curve". No per-mode behaviour is documented. |
| [ParticleSystem.MinMaxGradient](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MinMaxGradient.html) | Modes Color, Gradient, TwoColors, TwoGradients, RandomColor; `Evaluate(time, lerpFactor)` "based on what mode it is in". No storage, no byte arithmetic of particle colours. |
| [MainModule.startColor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-startColor.html), deprecated [ParticleSystem.startColor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-startColor.html) and the other deprecated `start*` properties | "The initial color of particles when the Particle System first spawns them"; the deprecated properties point to the main module and its `*Multiplier`s. |

Everything below is measured in this release, not documented.

## Measured behaviour

| Records | 6000.0.71f1 |
| --- | --- |
| `struct/*` | The struct keeps six values apart: mode, curve multiplier, both curves, both constants. `constant` is `constantMax`. `MinMaxCurve(c)` sets only `constantMax`; `(min, max)` only the constants; the curve constructors only the multiplier and curves (all other values 0 or null). Setters and mode switches change only their own value. `Evaluate`: Constant → `constantMax`; TwoConstants → `Lerp(min, max, t)`; Curve → `curveMax(time) × multiplier` (the lerp is ignored); TwoCurves → `Lerp(curveMin, curveMax, t) × multiplier`; the lerp is clamped to [0, 1]; a curve mode without its curve throws. |
| `module/*`, `extra/*` | A module stores one scalar: the constant (upper constant) in constant modes, the curve multiplier in curve modes, plus the lower constant. The getter returns only what the mode uses: constant modes give a curve multiplier of 1, no curves and (Constant) a lower constant of 0; curve modes give constants of 0 and (Curve) no `curveMin`. A curve mode given no curve stores the constant curve 1 (one key at time 0); a curve does not survive a pass through Constant. `*Multiplier` reads and writes the stored scalar in every mode. Same for every module property, `customData.GetVector`, `Burst.count` through `SetBursts`/`GetBursts` (a `Burst` alone keeps what it was given) and the `ParticleSystemForceField` curves. |
| `module/*/neg*`, `big*`, `extra/*` | Each property keeps both scalars within one range, applied by the setter and the multiplier in every mode without reordering: [0, ∞) for start size (X/Y/Z), start delay, rates, limits (and X/Z), drag, light range and intensity, trail width, the external-forces multiplier and burst counts; [0.0001, ∞) start lifetime; [0, 1] collision dampen and lifetime loss and trail lifetime; [0, 2] bounce; [0, 0.9999] texture-sheet frame over time and start frame; no bound for the others. |
| `authored/*` | Scene data reads the same way: a constant mode hides serialized curves and `minScalar`; out-of-range serialized scalars are bounded on load (`outofrange`). |
| `fields/*` | A script field serializes the six struct fields (`m_Mode`, `m_CurveMultiplier`, `m_CurveMin`, `m_CurveMax`, `m_ConstantMin`, `m_ConstantMax`) and keeps all of them; after loading or Instantiate, missing curves are empty curves, while a new component's are null. |
| `sim/*`, `authored/*/simulate` | Particles use the stored scalar: a constant set on a curve-mode struct has no effect, and switching the read value's mode starts from the getter's values. Defaults: a force field's gravity and vector-field speed are 1, lifetime-by-emitter-speed is the curve 1 → 0.2. |
| `gradient/struct/*` | Constructors set only their parts; `Evaluate` in a gradient mode without its gradient throws; PerceptualBlend returns colours in 8-bit steps (Blend and Fixed the keys' values). |
| `gradient/module/*`, `gradient/authored/*` | A module's getter hands out the module's own gradients (changing a read gradient's keys changes the module) and fills only what the mode uses (the rest is not initialized: the probe writes -777 for it); the setter keeps copies, a missing gradient becoming the default white one. |
| `gradient/fields/*` | A script field serializes `m_Mode`, `m_GradientMin`, `m_GradientMax`, `m_ColorMin`, `m_ColorMax` (not a module's `minMaxState`/`minColor`/...), and keeps all of them through loading and Instantiate. |
| `gradient/particles/*` | A particle's colour is bytes rounded half up (0.3·255 = 76.5 → 77). Gradient and RandomColor evaluate the gradient, TwoColors lerps the colours, then round; TwoGradients rounds both gradients and blends the bytes as (min·(256 − s) + max·s + 128)/256 with s = random·255 truncated. Colour over lifetime and by speed modulate the start colour as c·(f + 1)/256, truncated. |
| `deprecated/*` | `startSpeed`, `startSize` (X), `startLifetime`, `startRotation`, `startRotation3D`, `startDelay` and `gravityModifier` read and write the stored scalar (the `*Multiplier`) and keep the mode; `emissionRate` sets a constant rate; `startColor` sets Color mode (its getter reads the module colour, defined in the colour modes only). |

Known differences (`known.json`): `perceptual-precision` (4 records: PerceptualBlend values 1/255
apart about once in thirty, the Oklab round trip differing by hundredths of a byte). Gradient key
times and the time looked up are measured in steps of 1/65535, which also places the Blend values
that lie within 0.001 of a half byte as the Editor does.

Unmeasured: other releases, `Evaluate` outside time [0, 1], NaN and infinite scalars, trail colours
and custom data colours of particles, a single-key gradient set through a module's read value.
