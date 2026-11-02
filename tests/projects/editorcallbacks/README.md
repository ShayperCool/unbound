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

## Root order and activation remain open

`QueryScene` enumerates `GetRootGameObjects()` before walking each root's children,
so the raw prefab records retain root order. The prefab comparator keys those
records by phase and object and does not check their ordering. The Unity reference
returns Main's roots as PlainA, Plain, OuterA, VariantA, ModelA, Holder, Unpacked,
Lone; Second as PlainB, OuterB; and Additive as VariantC, AdditiveLone. Those are
the scenes' SceneRoots lists. The saved Root capture differs in all three scenes.

`BuildHierarchy` currently resolves SceneRoots entries only as Transforms. Prefab
roots listed by PrefabInstance id are appended later. This measured root-order
defect is significant and remains open. Fixing roots alone would worsen Second's
Awake/OnEnable order, which currently matches some positions by accident.

Existing scenes cannot establish a general activation rule: document order equals
numeric fileID order in every fixture, all woken markers have execution order 0,
and the discriminating cases use one marker class. No candidate explains Main's
prefab-instance wake order. Do not infer an activation sort from the root-order
API contract. The source/evidence audit and full sequences are saved under
`validation/runs/claude-fresh-20261101b/activation-audit/`.

Two subsequent scene-activation witness attempts each stopped after their first
Editor launch crashed during shutdown. Both completed files have identical bytes,
but neither passed the strict exit gate, so they do not form a valid Native pair
and no new reference was published. Deferring the shutdown operations passed
mock controls but did not prevent the second crash. The activation rule and the
coupled root-order correction remain held. Receipts are under
`validation/runs/sceneactivation-witness-20261102/` and
`validation/runs/sceneactivation-witness2-20261102/`; their outputs are diagnostic.
