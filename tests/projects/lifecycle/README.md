# Lifecycle probe

When a behaviour enabled or added at different points of a frame gets OnEnable, Start, Update and
LateUpdate: enabled from Update, from a coroutine (`yield return null`), from LateUpdate, and added
by AddComponent, each for a behaviour with a Start method and one without; and when Play mode calls
OnValidate (`Validated`: a prefab instantiated from Resources, an added component, a clone; the
prefab is made by `ProbeRunner.CreatePrefab`). `LifecycleProbe` writes
every message with its frame to `LIFECYCLE_PROBE_OUT` (JSON lines) at frame 8.

Record Unity's reference (`ProbeRunner.Create` made the empty scene):

    LIFECYCLE_PROBE_OUT=$PWD/tests/projects/lifecycle/Reference/unity-6000.0.71f1.jsonl \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/lifecycle \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/lifecycle.log

Run Unbound and compare (the records must be the same lines):

    LIFECYCLE_PROBE_OUT=/tmp/lifecycle.jsonl unbound run -p tests/projects/lifecycle --headless 12 --no-watch
    diff tests/projects/lifecycle/Reference/unity-6000.0.71f1.jsonl /tmp/lifecycle.jsonl
