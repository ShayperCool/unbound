# Local physics scenes

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference is `Reference/unity-6000.0.71f1.jsonl`. The values
are managed state, scene identities and positions after a few steps rounded to four decimals.
None depends on the graphics device. The gate compares every row exactly, except the rows that
`known.json` lists with their reason (recorded residuals, reported by `compare`).

`Assets/Scripts/LocalPhysicsProbe.cs` runs in Play mode (both simulation modes FixedUpdate) and
records, for 3D (`p3.*`) and 2D (`p2.*`) scenes made with `SceneManager.CreateScene` and a
`LocalPhysicsMode`:

- `create.*`, `lone.*`, `move.*`: the scene's physics scene differs from the default one, is
  valid and empty; a static collider alone makes it non-empty; the other dimension's physics
  scene stays the default one.
- `auto.*`: a local body overlapping a default body at the same place is not simulated by the
  automatic simulation, and the two do not touch (the default body falls straight down).
- `sim.*`: the local scene's `Simulate` steps its body, also in FixedUpdate mode, and leaves the
  default body where it was; `defaultSim.*`: the default scene's Simulate in FixedUpdate mode warns
  and does not run.
- `query.*`: the static queries do not see local colliders; the scene's own queries see only its
  colliders; a body's sweep or cast hits a farther local wall, not a nearer default wall.
- `contact.*`: trigger and collision enter messages during the local steps, by step.
- `active.*`: objects made or instantiated while the local scene is active join it.
- `back.*`, `into.*`: moving a body between physics scenes keeps its velocity in 3D and zeroes its
  linear and angular velocity in 2D.
- `gravity.*`: Physics.gravity and Physics2D.gravity apply to the local scenes.
- `joint.*`: a joint refuses a connected body of another physics scene with a warning, keeping its
  connected body null.
- `unload.*`: after a created scene's unload its physics scene is invalid; IsEmpty and Simulate
  throw InvalidOperationException; the 3D raycast answers false, the 2D one hits nothing.
- `both.*`: a scene with both flags gets both local physics scenes, LocalPhysicsMode.None neither.
- `loaded*`: `Assets/Scenes/Sim.unity` (a 3D and a 2D floor at the origin, authored by
  `ProbeRunner.MakeScenes`) loaded additively with each mode.

`*.logs` and `*.log<i>` rows hold the messages logged during each case.

Recorded residual (`known.json`): a scene loaded with `LoadSceneAsync` keeps its local physics
scenes valid and empty after its unload in the Editor, three frames later too, where a created
scene's are invalid; Unbound follows the documented lifetime for both. Not recorded: a scene's
physics scene after a scene merge, parenting across scenes, objects moved during a step's
callbacks, interpolation in local scenes, other releases.

```bash
python3 tests/projects/localphysics/run.py native --label mac-a   # and mac-b: two Editor captures
python3 tests/projects/localphysics/run.py publish mac-a mac-b    # identical repeats only
python3 tests/projects/localphysics/run.py check                  # engine run and comparison
```
