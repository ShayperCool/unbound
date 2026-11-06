# Legacy baked NavMesh probe

A scene whose NavMesh was baked the legacy way (`UnityEditor.AI.NavMeshBuilder.BuildNavMesh`, as
the Navigation window does: the scene's `NavMeshSettings.m_NavMeshData` references
`Assets/Scenes/<Scene>/NavMesh.asset`), measured identically in the Unity Editor and in Unbound by
`BakedProbe` (`Assets/Scripts`):

| scenario | what it records |
| --- | --- |
| `awake` | in the probe's Awake: whether the scene's agent is on the NavMesh, a sample |
| `areas`, `baked` | the Mud area (4, cost 3), the triangulation (sorted triangles, area) |
| `sample`, `raycast`, `paths` | a 2 m sample grid; raycasts over the Mud strip (with and without its area), to a wall and up a 0.3 m step; paths across the Mud (cost, mask), up the step, up the platform over a one-way OffMeshLink component and down over the bake's generated drop link |
| `agent` | an agent walked up the platform and back down at a fixed 0.02 s step: position, `isOnOffMeshLink`, the link's type, every 5 frames |
| `additive` | after `LoadSceneAsync("Second", Additive)`: both NavMeshes' triangulation, samples over the second, its agent, paths to and within it |
| `unloaded` | after `UnloadSceneAsync("Second")`: the first scene's NavMesh alone again |

The level is `ProbeSetup.Create`'s (`Assets/Editor`): a ground with a Mud strip, a step, a 1.5 m
platform (OffMeshLink generation on, ledge drop height 2), the OffMeshLink and an agent; the second
scene is a ground 2 m beyond the first one's and an agent. Both scenes, their NavMesh assets and
`ProjectSettings/EditorBuildSettings.asset` are committed as made by the Editor once.

    python3 tests/projects/navbaked/run.py setup   --release 6000.0.71f1 --label L   # scenes + bakes
    python3 tests/projects/navbaked/run.py editor  --release 6000.0.71f1 --label L   # two Editor captures
    python3 tests/projects/navbaked/run.py publish --release 6000.0.71f1 --label L   # identical? -> Reference/
    UNBOUND_CLI=.../unbound python3 tests/projects/navbaked/run.py check --release 6000.0.71f1  # the gate

`check` runs Unbound on a fresh copy under `$XDG_CACHE_HOME/unbound/validation/runs/navbaked/`
and compares it with the reference using `tests/projects/navmesh/compare.py` (numbers within 1e-3,
text exactly; every missing, extra or differing value is listed).

The reference `Reference/unity-6000.0.71f1.json` holds 1291 values, SHA-256
`2a3f72a3a51c20abac116dd3dc93f1b8190f4b5153916970f43a6f346c00a126`.
