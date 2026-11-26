# Entering Play mode with scene reload disabled

What entering Play mode does to the open start scene's objects under each Enter Play Mode Options
setting. Recorded with the Unity 6000.0.71f1 Linux Editor in batch mode on the GPU (two captures
identical after sorting the teardown as the Editor leaves, whose order changes between runs).

Documented (Manual 6000.0,
[Configurable Enter Play Mode](https://docs.unity3d.com/6000.0/Documentation/Manual/ConfigurableEnterPlayMode.html),
[Scene Reloading](https://docs.unity3d.com/6000.0/Documentation/Manual/SceneReloading.html)):
with scene reload disabled Unity does not reload the open scenes when entering Play mode. It "still
calls the same event functions (such as `OnEnable`, `OnDisable` and `OnDestroy`) as if the scene
were freshly loaded", and scripts with `[ExecuteInEditMode]` or `[ExecuteAlways]` "don't receive
`OnDestroy` or `Awake` calls". Where the runtime initializers, sceneLoaded and scene processing
fall is not documented: the probe measures it.

`Assets/Scenes/Main.unity` (authored by the Editor, `run.py create`) holds four objects with a
`Lifecycle` behaviour that records Awake, OnEnable, Start, OnDisable, OnDestroy and its first Update
with Edit or Play mode and how many times this instance has had the callback (a field that is not
serialized), and whose Awake deactivates a child template in Play mode only (as TMP_Dropdown hides
its template): `Always` (`[ExecuteAlways]`), `Plain` (Play mode only), `AlwaysInactive` (an
inactive object) and `AlwaysDisabled` (the behaviour disabled). `Assets/Scripts/PlayModeEntry.cs`
records the runtime initializers (`[RuntimeInitializeOnLoadMethod]` of each load type),
SceneManager.sceneLoaded and activeSceneChanged; `Assets/Editor/SceneReloadProbe.cs`
(`-executeMethod SceneReloadProbe.Record`) opens the scene in Edit mode and enters Play mode
(`unbound run --scene` plays it); it records domain loads, Play mode state changes,
`[InitializeOnEnterPlayMode]` (with the options it is given), `[PostProcessScene]`, the templates'
state at Play mode's third frame (found through the scene's roots, inactive ones included), and
leaves the Editor at the fifth. The rows give each object's own callbacks in order with the lines of
no object among them (the order of different objects' Awake is not specified: MonoBehaviour.Awake,
Scripting API 6000.0), and its callbacks after the probe leaves. `run.py` runs four profiles on
fresh copies, with `ProjectSettings/EditorSettings.asset`'s options: `none` (on, nothing disabled),
`domain` (domain reload disabled), `scene` (scene reload disabled), `both` (domain and scene reload
disabled).

Measured. In every profile the open scene's `[ExecuteAlways]` objects get Awake and OnEnable in Edit
mode (a disabled one Awake only), then ExitingEditMode. With the domain kept (`domain`, `both`)
`[InitializeOnEnterPlayMode]` follows, then the Edit-mode objects stop in Edit mode: with the scene
reloaded (`domain`) they are destroyed (OnDisable, OnDestroy), with the scene kept (`both`) the
enabled ones get OnDisable. With the domain reloaded (`none`, `scene`) the scripts get OnDisable,
then `[InitializeOnEnterPlayMode]` and the reload, after which the Edit-mode scripts get OnEnable
and OnDisable in Edit mode again (and, with the scene reloaded, OnDestroy). Then the runtime
initializers SubsystemRegistration, AfterAssembliesLoaded, BeforeSplashScreen and BeforeSceneLoad
in Play mode. With the scene reloaded (`none`, `domain`) the scene's objects are new and wake in
Play mode (every template hidden), the scene is processed, activeSceneChanged (from no scene) and
sceneLoaded (mode 4) follow. With the scene kept (`scene`, `both`) the same instances play: the
Edit-mode scripts get OnEnable in Play mode without a second Awake (so their templates stay
active), the Play-mode script wakes, the scene is processed, and neither activeSceneChanged nor
sceneLoaded is raised. AfterSceneLoad, Start and the first Update follow in every profile. The
inactive object's template stays active in every profile (no callback runs on it).

Residuals (`residuals.json`, tests/residuals.py: each pins the value Unbound gives now): with the
domain reloaded (`none`, `scene`) Unbound has one domain load where the Editor has its Edit-mode
domain and the reload for Play mode; Unbound opens the start scene in Edit mode only when the
options keep the domain or the scene (with both reloads on, the Edit-mode objects are discarded
before Play mode starts).

Create (once; the Editor's batch quit after authoring may crash at its shutdown once the scene is
written, which `create` accepts), record, publish and check:

    python3 tests/projects/scenereloadoff/run.py create
    python3 tests/projects/scenereloadoff/run.py native --label a
    python3 tests/projects/scenereloadoff/run.py native --label b
    python3 tests/projects/scenereloadoff/run.py publish a b
    python3 tests/projects/scenereloadoff/run.py check
