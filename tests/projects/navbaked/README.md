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
| `additive` | after `LoadSceneAsync("Second", Additive)`: both NavMeshes' triangulation, samples over the second, whether its agent is on the NavMesh, paths to and within it |
| `agent2` | the second scene's agent's position then |
| `unloaded` | after `UnloadSceneAsync("Second")`: the first scene's NavMesh alone again |
| `height` | the Height scene (baked with Build Height Mesh: a 15 degree ramp, five 0.2 m stairs up to a 1 m landing, a 0.1 m bump), loaded additively last: SamplePosition along the ramp and stairs and round the bump, raycasts up the stairs and the ramp (clear and into a wall), path corners and the closest edge (controls: not on the height mesh) |
| `height.agents` | agents walking up the stairs, up the ramp and over the bump: where they are placed, their destination and path corners, position, transform height and vertical velocity every 5 frames; one warped onto a stair |
| `resume.*` | where in the player loop asynchronous operations complete and the coroutines waiting for them resume (`Assets/Scripts/ResumeTiming.cs`; a marker before every player-loop system names the next one): the additive load and unload above (`load`, `unload`, with the second scene's agent as found and moved), the same load held by `allowSceneActivation` (`held`), `Resources.LoadAsync` with completed handlers added before and after a coroutine waits and a second waiting coroutine, then yielded again once done (`resources`), `InstantiateAsync` (`instantiate`) |

The level is `ProbeSetup.Create`'s (`Assets/Editor`): a ground with a Mud strip, a step, a 1.5 m
platform (OffMeshLink generation on, ledge drop height 2), the OffMeshLink and an agent; the second
scene is a ground 2 m beyond the first one's and an agent; Jump and Height are described in
`ProbeSetup`. The scenes, their NavMesh assets and `ProjectSettings/EditorBuildSettings.asset` are
committed as made by the Editor once (`NAVBAKED_SETUP=ProbeSetup.CreateHeight run.py setup` makes
only the Height scene).

    python3 tests/projects/navbaked/run.py setup   --release 6000.0.71f1 --label L   # scenes + bakes
    python3 tests/projects/navbaked/run.py editor  --release 6000.0.71f1 --label L   # two Editor captures
    python3 tests/projects/navbaked/run.py publish --release 6000.0.71f1 --label L   # identical? -> Reference/
    UNBOUND_CLI=.../unbound python3 tests/projects/navbaked/run.py check --release 6000.0.71f1  # the gate

`check` runs Unbound on a fresh copy under `$XDG_CACHE_HOME/unbound/validation/runs/navbaked/`
and compares it with the reference using `tests/projects/navmesh/compare.py` (numbers within 1e-3,
text exactly; every missing, extra or differing value is listed). It exits with the scenarios of the
scene's NavMesh itself (`awake`, `areas`, `baked`, `paths`, `additive`, `unloaded`), the second
scene's agent (`agent2`) and the resume points (`resume.unload`, `resume.held`,
`resume.resources`, `resume.instantiate`), the samples (`sample`), the jump-across links (`jump`)
and the queries over the height mesh (`height`); `raycast`, `agent`, `height.agents` and
`resume.load` (the first load's frame) measure the general queries, the agent model and scene loading time and are
listed, not gated (their residuals: docs/COMPATIBILITY_GAPS.md, the legacy-baked NavMesh and
asynchronous operation rows).

The reference `Reference/unity-6000.0.71f1.json` holds 2302 values, SHA-256
`529288ab8ae396a427df458714036cec5ffef78f0da0db166d7d234475b371d2`.
