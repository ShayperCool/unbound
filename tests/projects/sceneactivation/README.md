# Scene activation order witness

Native evidence for the order of Awake, OnEnable and Start when a scene is entered, and for
`Scene.GetRootGameObjects()` order. One fixture, two phases in one Play session:

1. **Play entry**: a windowed Editor opens `Assets/Scenes/Wake.unity` in Edit mode and enters
   Play mode (domain and scene reload, the editor-callback probe's settings).
2. **Single load**: on the driver's second Update, `SceneManager.LoadScene("WakeCopy",
   LoadSceneMode.Single)`. `WakeCopy.unity` is a byte copy of `Wake.unity` under another GUID.

Packages and ProjectSettings are the editorcallbacks probe's, byte for byte (only
`EditorBuildSettings.asset` lists these two scenes).

## Fixture (authored offline, no Editor setup)

`make_fixture.py` wrote the scene and prefab YAML from the Unity-saved editorcallbacks templates.
`fixture.py` reads the committed bytes back on its own and writes `fixture.json`: the manifest
of every Assets/Packages/ProjectSettings file, plus the bindings of every marker. Unlike a Unity
save, the documents are not in fileID order.

| object | kind, class | SceneRoots index / parent | doc group | fileID key |
|---|---|---|---|---|
| A | plain, W | root 0 | 3 | GO 5000 |
| D | plain, W2 | root 1 | 1 | GO 2000 |
| B | plain, W | root 2 | 4 | GO 1000 |
| B/B1 | plain, W, child with a lower fileID than its parent | under B | 2 | GO 500 |
| C | plain, W | root 3 | 0 | GO 3000 |
| E | plain, WE `[DefaultExecutionOrder(-10)]` | root 4 | 5 | GO 4000 |
| P1, P1/Xc | instance of X.prefab (X, child Xc) | root 5 | 7 | PrefabInstance 9000 |
| P1/Added | plain GO added to the instance (insertIndex -1) | under P1 | 10 | GO 800 |
| Q | plain, W | root 6 | 9 | GO 1500 |
| P2, P2/X, P2/X/Xc | instance of Y.prefab, which nests X | root 7 | 8 | PrefabInstance 7000 |
| P3, P3/Xc | instance of X.prefab | root 8 | 6 | PrefabInstance 6000 |

Plain objects have Transform = GO+1 and MonoBehaviour = GO+2. Every GameObject carries exactly
one marker. Inside X and Y, document order, fileID order and hierarchy order agree, so the
in-prefab order cannot tell them apart (limit). The recorder adds no object to either scene. The
driver is created at AfterSceneLoad in DontDestroyOnLoad and has only Update, with no lifecycle
or render callbacks.

## Predeclared candidates (`predictions.json`)

There are 7 base orders × {±execution-order grouping} × {±type grouping}, so 28 sequences. They
are evaluated on the committed fixture table, are pairwise distinct, and equal the previously
proposed sequences. The base orders are: document (outer-first / nested-first), fileID
(outer-first / nested-first), hierarchy pre-order, Unbound creation order, and instances first
then plain objects by fileID.

They are candidates, not an exhaustive set: none or several matches is a valid result. Each phase's
Awake, OnEnable and Start sequences are compared on their own (exact equality, plus the
out-of-order count against every candidate). The analysis also reports:
- root and pre-order observations;
- Awake/OnEnable interleaving;
- the position of E (execution order) and D (type);
- whether wake order follows the instance-id order.

## Contract (`contract.py`)

The schema is strict: exact keys and types. The capture gate checks:
- completeness: one Awake, OnEnable and Start per marker per phase, plus the bind, loaded, roots,
  tree, request and unloaded records;
- uniqueness;
- root and pre-order membership as sets;
- the single last completion marker;
- identity consistency;
- the GlobalObjectId binding of plain objects.

Order is never gated or normalized. `run.py check` adds:
- strict exit 0, with no forced stop of any kind;
- the NVIDIA renderer and a logged shutdown;
- no watchdog and no compile error;
- the prelaunch receipt's project bytes and runner sources equal the committed fixture;
- after the run, Assets/Packages are byte-unchanged and the two scenes are equal. If the Editor
  rewrote them, that output is kept and the capture is refused.

The canonical form keeps every raw line in raw order. Only raw instance ids are replaced by their
predeclared per-phase rank and sign. The raw ids go to `ids.jsonl`. Ranks test an id-order
hypothesis; they do not measure allocation chronology. Publication requires two gate-passing
captures whose canonical forms are byte-identical.

```
python3 tests/projects/sceneactivation/fixture.py check
python3 tests/projects/sceneactivation/offline_compile.py --donor <Editor-compiled editorcallbacks copy> --unity <Editor dir> --out <new cache dir>
python3 tests/projects/sceneactivation/test_contract.py <new cache dir>
python3 tests/projects/sceneactivation/run.py editor --release 6000.0.71f1 --label <label> --repeat native1
python3 tests/projects/sceneactivation/run.py check  --release 6000.0.71f1 --label <label> --repeat native1
python3 tests/projects/sceneactivation/run.py editor --release 6000.0.71f1 --label <label> --repeat native2
python3 tests/projects/sceneactivation/run.py check  --release 6000.0.71f1 --label <label> --repeat native2
python3 tests/projects/sceneactivation/run.py publish --release 6000.0.71f1 --label <label>
```

There is no Unbound mode. The probe is Native evidence only.
