# Inherited Edit mode execution

Unity 6000.0.71f1 controls for inherited `ExecuteInEditMode`, `ExecuteAlways`, a generic base, direct attribution and plain scripts. Reflection records both `IsDefined` and `GetCustomAttributes` with inheritance enabled and disabled, plus the effective `AttributeUsage` of the fifteen neighboring core attributes.

`edit.jsonl` records per-object Awake, OnEnable, queued Update, OnDisable and re-enable messages. Each object records its own sequence; the probe asserts no order between objects. `startup.jsonl` creates ordinary, hidden, disabled and inactive objects during the editor-domain load for Play mode, then moves them to DontDestroyOnLoad in BeforeSceneLoad. The initial scene must already own these objects during editor initialization, so its first integration preserves them until that callback. The Editor leaves these plain scripts asleep until an enable or hierarchy activation changes their state. This observation is specific to that creation context; ordinary scene objects are activated on Play entry.

`transition.jsonl` records a plain scene root, a disabled script and an inactive root created in Edit mode before Play entry with domain and scene reload disabled. The first two receive Awake on Play entry; the inactive one waits for activation. This controls the existing scene activation path separately from package startup.

`startup-packages.jsonl` additionally creates the actual Addressables callback hook from the version selected by `tests/projects/packages/Packages/manifest.json`. The singleton is created while `isPlaying` is false, has HideAndDontSave flags, and must invoke an attached Update delegate after Play starts. The staged project installs Addressables and its dependencies; other packages are outside this control.

Each canonical reference has two identical, normally exited GPU Editor captures. Captures, receipts and repeats remain outside Git. All values compare exactly.

```sh
python3 tests/projects/editinheritance/run.py editor --release 6000.0.71f1 --label edit
python3 tests/projects/editinheritance/run.py publish --release 6000.0.71f1 --label edit
python3 tests/projects/editinheritance/run.py unbound --release 6000.0.71f1
# Repeat with --startup, --startup --packages, and --transition.
```

The runner makes fresh project copies and uses the existing GPU display. Engine replays use an immutable runtime selected by `UNBOUND` or `UNBOUND_CLI`.

Unity 6000.0 documentation:

- [ExecuteInEditMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ExecuteInEditMode.html) and [ExecuteAlways](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ExecuteAlways.html): scripts can receive Edit mode messages; Update is tied to view redraws rather than a continuous runtime clock. ExecuteAlways also covers Prefab mode, which this probe does not measure.
- [QueuePlayerLoopUpdate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorApplication.QueuePlayerLoopUpdate.html): explicitly requests an update even without a scene modification. Every Edit mode sampling window requests updates.
- [Execution order](https://docs.unity3d.com/6000.0/Documentation/Manual/execution-order.html): sceneLoaded follows OnEnable and precedes Start; callback order across different objects is generally unspecified. The probe records no cross-object order.
- [SceneManager](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.html): loaded/unloaded callbacks report scene transitions. No universal ordering of independent loads is inferred here.

Attribute inheritance and the pre-Play singleton sequence are measured 6000.0.71f1 behavior, not additional documentation guarantees. Edit mode repaint triggers other than explicit QueuePlayerLoopUpdate, Prefab mode and changes to runInEditMode remain outside this control.
