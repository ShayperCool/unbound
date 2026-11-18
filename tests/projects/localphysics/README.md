# Local physics scenes

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode, then again with the
Linux GPU Editor in batch mode with the `stale.*` and `reparent.*` cases added (every earlier row
the same); each pair of captures is byte-identical and the canonical reference is
`Reference/unity-6000.0.71f1.jsonl`. The values
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
  default body where it was; `defaultSim.*`, `defaultScene.*`: the default scene's Simulate in
  FixedUpdate mode (Physics2D.Simulate, the default PhysicsScene(2D)'s) warns and does not run.
- `query.*`: the static queries do not see local colliders; the scene's own queries see only its
  colliders; a body's sweep or cast hits a farther local wall, not a nearer default wall; a local
  collider's Overlap finds its local partner, not the default collider at the same place.
- `contact.*`: trigger and collision enter messages during the local steps, by step, and what a
  static raycast finds from inside the collision callback (the default scene's floor).
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
  `ProbeRunner.MakeScenes`) loaded additively with each mode. A loaded scene's local physics
  scenes stay valid and empty after its unload, three frames later too, unlike a created
  scene's.
- `stale.*`: those physics scenes in use after the unload: Simulate runs and does nothing (2D
  answers true), raycasts and overlaps find nothing, nothing is logged; the scene loaded again
  gets new physics scenes, and the old ones stay valid and empty beside them.
- `reparent.*`: a 3D and a 2D body moved by SetParent under an inactive parent of a local
  scene, then the parent activated, then back under an inactive parent of the main scene; and in
  one scene, under an inactive parent and out to an active one (`sameScene`, `toActive`): the
  bodies join their parent's scene; OnDisable (or OnEnable) comes before
  OnTransformParentChanged, in the new scene; the local scene holds them only once active; a 3D
  body keeps its velocity, a 2D one starts at rest; the local scene's Simulate steps them, the
  automatic simulation does not.

`*.logs` and `*.log<i>` rows hold the messages logged during each case.

The LocalPhysicsMode page documents that "the lifetime of the physics Scene(s) is the same as the
Scene"; the Editor follows it for a created scene and keeps a loaded scene's, which Unbound
matches for 6000.0 (Compat.LoadedScenePhysicsOutlivesUnload; other releases follow the
documented lifetime). Not recorded: a scene's physics scene after a scene merge, objects moved
during a step's callbacks, interpolation in local scenes, a scene loaded synchronously
(LoadScene, taken as a loaded one), other releases.

```bash
python3 tests/projects/localphysics/run.py native --label mac-a   # and mac-b: two Editor captures
python3 tests/projects/localphysics/run.py publish mac-a mac-b    # identical repeats only
python3 tests/projects/localphysics/run.py check                  # engine run and comparison
```
