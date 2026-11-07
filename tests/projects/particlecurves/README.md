# particlecurves

What `ParticleSystem.MinMaxCurve` keeps and returns in Unity 6000.0.71f1: the struct on its own,
the same values written to and read back from module properties (with each module's
`*Multiplier` shortcut), values authored in a prefab, script fields of the type, and simulations
that use them. Each record is one JSON line `{"key", "value": [numbers]}`; the reference is
`Reference/unity-6000.0.71f1.jsonl`.

    python3 tests/projects/particlecurves/run.py native --label ref1    # Editor capture (GPU, job pool)
    python3 tests/projects/particlecurves/run.py publish ref1 ref2      # two identical captures
    python3 tests/projects/particlecurves/run.py check                  # Unbound run + compare (the gate)

`run.py create` authored `Assets/Resources/Authored.prefab` once (`ProbeRunner.Create`). The
`outofrange` system's scalars were then set out of range in the YAML by hand (the Editor bounds
them when it writes), to record what loading does with such data. Run output stays under
`$XDG_CACHE_HOME/unbound/validation/runs/particlecurves/`. The comparator checks every key and value
at the particle probes' bound (0.0002 absolute plus 0.0002 relative).

One known difference, outside this probe's subject, stays visible: in `sim/{0..3}/c` the step
from 0.75 s to 1.25 s of a 1 s loop crosses a burst set at 1.0 s (the duration). Unity emits its
two particles (remaining lifetime 3.76015, an age of 0.23985 s rather than the 0.25 s since the
loop end); Unbound does not emit a burst whose time equals the duration. The comparator reports
these four records as known only when exactly those last two particles are missing and every
other value matches (`test_compare.py`). The burst timing at the loop end is a follow-up.

## Documented contract

| Official Unity 6000.0 source | What it says |
| --- | --- |
| [ParticleSystem.MinMaxCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MinMaxCurve.html) | Properties `constant`, `constantMin`, `constantMax`, `curveMultiplier`, `curve`, `curveMin`, `curveMax`, `mode`; `Evaluate` "based on what mode it is in". The examples assign the whole struct back after changing a curve. Nothing about what a module returns, about separate or shared storage, or about bounds. |
| [MainModule.startSizeMultiplier](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-startSizeMultiplier.html) (and the other `*Multiplier` properties) | "A multiplier for the initial size"; changing it "is more efficient than accessing the entire curve". No per-mode behaviour is documented. |

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

Unmeasured: other releases, `MinMaxGradient` storage, `Evaluate` outside time [0, 1], NaN and
infinite scalars.
