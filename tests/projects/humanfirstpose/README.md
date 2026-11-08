# Human first pose

Records HumanA immediately before and after `Animator.Update(0)`. A second fresh
HumanA instance uses the original HumanAIK controller and callback, once at zero
time. No elapsed animation steps, retargeting combinations, or muscle sweeps run.

The runner copies `humanoid/Assets`, `Packages`, and `ProjectSettings` into the
validation cache. Models, model metas, and controllers remain byte-identical.
It derives the staged script from the committed humanoid fixture, preserving its
traits/model/avatar loading order, initial handler reads, avatar construction,
and first-play setup. It omits the handler muscle writes and the later plays.
The original fixture and its reference remain unchanged. Staging fails if the
expected source fragments change.

The capture uses public APIs and records:

- Root position, localPosition, rotation, localRotation, localScale, lossyScale,
  and localToWorldMatrix, before and after the update.
- Animator humanScale, bodyPosition, bodyRotation, rootPosition, and rootRotation.
- All imported Scene RootT/RootQ keys, tangents, weights, and `Evaluate(0)` through
  AnimationUtility, plus the active clip, weight, normalized time, and callback.

Every float carries its round-trip decimal and raw f32 bits. The bits preserve
signed zero even when the runtime's decimal formatter prints both signs as `0`.
The comparator reports every bit difference and the complete missing/extra/value
union. It separately reports the original humanoid tolerance, `2e-4` absolute or
relative. It does not normalize quaternions or replace captured inputs.

```bash
python3 tests/projects/humanfirstpose/run.py editor --release 6000.0.71f1 --label <pair>
python3 tests/projects/humanfirstpose/run.py publish --release 6000.0.71f1 --label <pair>
python3 tests/projects/humanfirstpose/run.py unbound --release 6000.0.71f1 --label <engine> \
  --frozen-runtime <directory-containing-runtime-manifest.json>
python3 tests/projects/humanfirstpose/compare.py \
  tests/projects/humanfirstpose/Reference/unity-6000.0.71f1.jsonl \
  ~/.cache/unbound/validation/runs/humanfirstpose/<engine>/unbound/values.jsonl \
  --json ~/.cache/unbound/validation/runs/humanfirstpose/<engine>/full-union.json
```

The driver already admits each heavy job through `~/.cache/compat/with-heavy-job`.
Publication requires complete, byte-identical independent Native captures and
the original first body/root values. The old reference has no raw bits, so its
zero signs are explicitly marked unknown in the cache comparison. A frozen
engine capture verifies every manifest entry in the frozen runtime and in its
shared runtime snapshot. It never builds the runtime or uses a shared build
target.

All projects, logs, manifests, comparisons, and failed attempts remain under
`$XDG_CACHE_HOME/unbound/validation/runs/humanfirstpose/<label>`. Only one canonical
reference per release belongs in `Reference/`. A validator-only failure can resume
the second independent capture with `editor --repeat native2`; existing captures
and projects are never overwritten.
