# Project physics defaults probe

The canonical Native oracle is `Reference/unity-6000.0.71f1.jsonl`.
It measures 582 Rigidbody and Collider records using Unity 6000.0.71f1 on Linux.
`UNBOUND_PROBE_UNITY_VERSION` selects the release in `verify.py`; no missing
release falls back to a different oracle. `Assets/Editor/ProbeRunner.cs` authors
the scene and prefab through Unity APIs, and `PhysicsDefaultsProbe.cs` records
settings, creation, active/inactive imports, cloning, runtime overrides and
motion. The deliberately non-default DynamicsManager inputs remain authored
source files.

Two independent Native recordings agree. One canonical copy is retained.
The integrated engine capture passes 582/582, with 576 exact and six solver
motion differences within the original tolerance, worst 6.15e-8 absolute.
`compare.py` includes every key and rejects missing/extra/duplicate records.
Field tolerance is 1e-6 relative to max(1, |reference|); motion retains the original
1e-3 tolerance. No records are excluded.

```bash
python3 tests/projects/physicsdefaults/verify.py
python3 tests/projects/physicsdefaults/verify.py --candidate /path/to/cached/engine.jsonl
```

These checks need no Unity, private binaries or historical run archive. To record
a release, copy Assets, Packages and ProjectSettings into a private project under
`${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/physicsdefaults/`. Select
`$HOME/Unity/Hub/Editor/${UNBOUND_PROBE_UNITY_VERSION:-6000.0.71f1}/Editor/Unity`, set
`PHYSICS_DEFAULTS_OUT` to a new cache JSONL path and run `ProbeRunner.Record`
through `~/.cache/compat/with-heavy-job` twice. Keep both logs and recordings in
the run cache. Install the two agreeing recordings explicitly with
`verify.py --record-reference <native1.jsonl> <native2.jsonl>`. The same version
variable selects the destination reference file.

Measured behavior:

- Components snapshot globals at creation, even while inactive. Allocation and
  reactivation retain that snapshot. Globals do not retune existing components.
- These runtime tuning fields are absent from Unity-authored scene/prefab YAML.
  Loading and cloning use current defaults, including cloning an overridden body.
  The authoring script deliberately sets different runtime values before saving.
- Global iteration setters clamp to 1..255. Body iteration setters clamp the
  absolute magnitude to 1..255, including int.MinValue becoming 255.
- Body angular and linear speed setters take absolute values. Global angular
  speed stores negative values; new bodies take their absolute value.
- Global and body sleep thresholds preserve assigned values, including negative
  values and NaN. Body depenetration preserves them too. Global depenetration and
  contact offset reject values <= 0 but accept NaN and positive infinity.
- A changed global affects future bodies only. Angular and depenetration motion
  confirm this, rather than relying only on managed field readback.
