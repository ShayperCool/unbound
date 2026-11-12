# Editor callbacks and prefab queries probe

What the Editor runs around entering Play mode and loading scenes, and what PrefabUtility and
SerializedProperty answer in Play mode. Recorded by the same scripts in the Unity Editor and in
Unbound, one JSON line per record (`EDITOR_CALLBACKS_OUT`):

* `Assets/Editor/SceneCallbacks.cs`: `[InitializeOnLoad]`, `[InitializeOnLoadMethod]`,
  `[InitializeOnEnterPlayMode]`, `[DidReloadScripts]` (orders -3, default, 0, 5),
  `playModeStateChanged`, and the scene processors: `IProcessSceneWithReport` (orders -10, 3,
  10, with the report they get), the obsolete `IProcessScene` (3) and `[PostProcessScene]`
  methods (-5, default, 0, 7 throwing, 10), with the scene they see.
* `Assets/Scripts/ProbeDriver.cs`: the `[RuntimeInitializeOnLoadMethod]` phases, `sceneLoaded`,
  `sceneUnloaded`, `activeSceneChanged`; then, frame by frame, every PrefabUtility query and the
  SerializedProperty prefab state (`prefabOverride`, `isDefaultOverride`, `isInstantiatedPrefab`)
  for each object of: the first scene's prefab instances (overrides, an added child and
  component, an instance under a plain object, an unpacked instance, a model instance), the prefab
  assets loaded with AssetDatabase (regular, nesting, variant, model), runtime copies
  (`Object.Instantiate` of an asset, of an instance, of a nesting asset;
  `PrefabUtility.InstantiatePrefab`), the instances after Play mode edits (a field write, a
  Transform write, a SerializedObject apply, an Undo-recorded write with
  `RecordPrefabInstancePropertyModifications`), and the scenes loaded by `LoadScene` (single),
  `LoadScene` (additive) and `LoadSceneAsync`. `ProbeMarker` records Awake/OnEnable/Start with
  the values they see.

After `LoadSceneAsync`, records count frames from the first later frame (`asyncFrame`): when the
load completes depends on the loading time.

`ProbeSetup.Build` made the prefabs, the scenes and the build settings in the Editor
(`run.py create`, in a cache copy; the files were copied here).

```
python3 tests/projects/editorcallbacks/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/editorcallbacks/run.py publish --release 6000.0.71f1 --label <fresh-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/editorcallbacks/run.py unbound --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/editorcallbacks/compare.py tests/projects/editorcallbacks/Reference/unity-6000.0.71f1.jsonl \
    ~/.cache/unbound/validation/runs/editorcallbacks/<label>/unbound/values.jsonl --report <cache>/report.json
```

`editor` runs two independent interactive Editors (windows on a private Xvfb, on the GPU through
VirtualGL; `--batchmode` for batch-mode ones) on fresh cache copies through `tests/editor_runner.py`
(each in its own GPU lease, hard deadline 900 s, stopped after 300 s without a new capture line);
`publish` requires both Editor results to be complete with the NVIDIA renderer, both captures to
reach their `done` record and be byte-identical, and no existing reference. `compare.py` compares the
event sequence in order and every prefab record field by field, exactly, and lists every
difference (see its docstring for the one normalization: the Editor's Edit mode domain).

## Scene roots and activation

`QueryScene` enumerates `GetRootGameObjects()` before walking each root's children,
so the raw prefab records retain root order. The prefab comparator keys those
records by phase and object and does not check their ordering. The Unity reference
returns Main's roots as PlainA, Plain, OuterA, VariantA, ModelA, Holder, Unpacked,
Lone; Second as PlainB, OuterB; and Additive as VariantC, AdditiveLone. Those are
the scenes' SceneRoots lists.

The root-query discrepancy is closed for these fixtures. `BuildHierarchy` resolves
both Transform and PrefabInstance entries in SceneRoots, retains unlisted roots,
and excludes nested transforms from prefab-instance root aliases. The SceneLoop
companions compare root order and sibling indices strictly. The original callback
and prefab reference and comparator are unchanged.

These fixtures do not establish a general activation rule: document order equals
numeric fileID order, all woken markers have execution order 0, and the
discriminating cases use one marker class. Root-query order does not justify a
new activation sort. The remaining wake-order differences stay in the raw report.
Earlier activation-witness attempts that exited abnormally remain diagnostic;
they are not canonical references for the current root-query controls.

The separate `tests/projects/hiddenroots` control also confirms that internal
realtime reflection and particle-light holders consume no scene root or later
sibling index. With domain reload disabled, the Editor's reflection camera
survives stopping Play. Detached holders therefore remain registered in Edit mode
and are destroyed at final process shutdown; particle systems destroy the light
holders they own. The ordinary public HideAndDontSave flag transition is a
separate open gap, recorded in COMPATIBILITY_GAPS.

## Teardown and queued-load controls

`quit_control.py` records direct `EditorApplication.Exit` from Play mode and
`ExitPlaymode`, with both the default domain reload and disabled domain reload.
Each profile is recorded twice on the existing GPU display. The reference records
each object's own message sequence, including a persistent object, a second
scene, and disabled and inactive controls. It does not prescribe callback order
between different objects.

Unity 6000.0 [OnApplicationQuit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnApplicationQuit.html)
promises that stopping Play sends the message, including to disabled scripts on
active objects. In these 6000.0.71f1 captures, stopping Play sends it before object
teardown and reports no sceneUnloaded events. Direct Editor exit omits that
message and unloads both scenes after all three witnessed objects are destroyed;
the event still sees a valid, unloaded scene and isPlaying true.
[SceneManager.sceneUnloaded](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager-sceneUnloaded.html)
documents the unload notification, without prescribing its relation to every
shutdown callback. Those details are observations for the recorded contexts.

`quit_control.py --throw-unloaded` records the same direct exit with an unload
subscriber that throws. Both scenes still notify it and the Editor exits normally;
a later subscriber in the same event invocation is skipped. The capture records
both exceptions independently. Shutdown continues scene cleanup, preference saving
and plugin shutdown even when an unload callback throws.

`scene_loop.py --queue-three` checks completion order, final progress, scene and
root counts for three queued additive loads. Completion frames and intermediate
progress are retained in its diagnostic sidecar. Two full Editor repeats finished
at frames 2/4/6 and 2/3/4, so the profile makes no exact-duration assertion.
[LoadSceneAsync](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html)
promises an asynchronous operation, not a fixed number of frames. The original
scene-queue source path, reference and strict comparator retain the two-load
timing discrepancy.

The completion times depend on elapsed loading work and the remaining integration
budget. Unbound keeps the existing equal-priority FIFO, completed progress and
activation hold behavior. It does not claim that its deterministic completion
frames reproduce the Editor's wall-clock schedule. The original strict queue
report remains 0/9 streams, with 64 Editor-only and 44 Unbound-only lines.

The Unity 6000.0 contracts distinguish the controls:

* [AsyncOperation.allowSceneActivation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-allowSceneActivation.html)
  holds progress at 0.9 and isDone false, blocking later operations such as an
  unload until activation is allowed. The existing held and forced SceneLoop
  profiles exercise the hold with later work pending.
* [Application.backgroundLoadingPriority](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-backgroundLoadingPriority.html)
  limits total main-thread integration time in a built Player to 2, 4, 10 or
  50 milliseconds for Low, BelowNormal, Normal or High. The documented limitation
  says this setting has no effect in the Editor. These captures are Editor
  captures, so changing this property cannot establish a Player loading budget.
* [AsyncOperation.priority](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-priority.html)
  selects higher-priority queued work first, with no effect after background work
  starts. The FIFO controls use equal default priorities. Non-default priorities
  need a separate discriminating control and queue implementation.

These contracts give no exact completion frame. Unequal priority scheduling,
large-scene work spanning several budgets and built Player loading remain open;
no frame-delay constant is inferred from the two-load capture.

## Remaining main-record differences

The main comparison retains 24 missing and 24 extra events. Every pair is an
Awake, OnEnable or Start receiver-order difference. The exit sceneUnloaded
notification is present, and the independent 114-value teardown controls pass.
Unity 6000.0 [Awake](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Awake.html)
and [execution order](https://docs.unity3d.com/6000.0/Documentation/Manual/ExecutionOrder.html)
do not guarantee an order between these instances. The raw sequences remain
strictly compared; no global wake sort is added.

The 21 prefab-field differences come from the SerializedObject property tree.
Its GameObject view omits components, icon, navigation layer and static editor
flags. Its Transform view omits proportional-scale state, children and the
Euler-angle hint, including that hint's default overrides. BoxCollider's own
fields are absent. This affects the engine-property bindings shared by editor
queries, beyond the callback recorder.
[SerializedObject](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedObject.html)
provides buffered reads and writes applied through ApplyModifiedProperties.
The follow-up must bind real values and preserve authored metadata, with
read/write and structural-array controls. A property-name-only patch would
hide the missing behavior. This remains a functional gap, with all 21 strict
field differences retained.
