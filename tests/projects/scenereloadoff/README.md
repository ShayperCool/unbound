# Entering Play mode with scene reload disabled

What entering Play mode does to the open start scene's objects under each Enter Play Mode Options
setting. Recorded with the Unity 6000.0.71f1 Linux Editor in batch mode on the GPU (two captures
identical after sorting the teardown as the Editor leaves, whose order changes between runs).

Documented (Manual 6000.0,
[Configurable Enter Play Mode](https://docs.unity3d.com/6000.0/Documentation/Manual/ConfigurableEnterPlayMode.html),
[Scene Reloading](https://docs.unity3d.com/6000.0/Documentation/Manual/SceneReloading.html)):
with scene reload disabled Unity does not reload the open scenes when entering Play mode.

`Assets/Scenes/Main.unity` (authored by the Editor, `run.py create`) holds four objects with a
`Lifecycle` behaviour that records Awake, OnEnable, Start, OnDisable, OnDestroy and its first Update
with Edit or Play mode and how many times this instance has had the callback (a field that is not
serialized), and whose Awake deactivates a child template in Play mode only (as TMP_Dropdown hides
its template): `Always` (`[ExecuteAlways]`), `Plain` (Play mode only), `AlwaysInactive` (an
inactive object) and `AlwaysDisabled` (the behaviour disabled). `Assets/Editor/SceneReloadProbe.cs`
(`-executeMethod SceneReloadProbe.Record`) opens the scene in Edit mode and enters Play mode
(`unbound run --scene` plays it); it records domain loads and Play mode state changes, the templates'
state at Play mode's third frame, and leaves the Editor at the fifth. `run.py` runs three profiles on
fresh copies, with `ProjectSettings/EditorSettings.asset`'s options: `none` (on, nothing disabled),
`scene` (scene reload disabled), `both` (domain and scene reload disabled).

Measured. With scene reload disabled (`scene`, `both`): the open scene's `[ExecuteAlways]` objects
get Awake and OnEnable in Edit mode (a disabled one Awake only), OnDisable after ExitingEditMode, and
in Play mode OnEnable again (the same instances) without a second Awake, then Start; the Play-mode
behaviour wakes in Play mode; so the `[ExecuteAlways]` objects' templates stay active. With domain
reload on (`scene`) the reload adds an OnEnable/OnDisable pair in Edit mode before Play mode's
OnEnable. With the options on and nothing disabled (`none`) the scene's Edit-mode objects get their
Edit-mode callbacks and are torn down as the domain reloads, and the reloaded scene's objects wake
in Play mode (every template hidden).

Residuals (`residuals.json`): Unbound loads the start scene in Play mode (queue row Q811), and folds
the Editor's Edit-mode start and Play mode's reload into one domain load.

Create (once; the Editor's batch quit after authoring may crash at shutdown, the files are written
before it), record, publish and check:

    python3 tests/projects/scenereloadoff/run.py create
    python3 tests/projects/scenereloadoff/run.py native --label a
    python3 tests/projects/scenereloadoff/run.py native --label b
    python3 tests/projects/scenereloadoff/run.py publish a b
    python3 tests/projects/scenereloadoff/run.py check
