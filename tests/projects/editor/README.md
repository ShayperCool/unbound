# Editor API probe

What the Editor API answers in Play mode, recorded by the same script in the Unity Editor and in
Unbound (`Assets/Editor/EditorApiProbe.cs`, frame by frame from `EnteredPlayMode`):
EditorJsonUtility and JsonUtility (formats, asset and instance references, engine objects, fields
the JSON does not name, struct callbacks), Selection (and its deferred `selectionChanged`), Undo
(groups and their names, undo/redo of recorded objects, created objects and added components,
collapsing, reverting, the events), dirty counts, GlobalObjectId for assets, scene objects and
unsaved objects, Progress (changes apply on the next update; finished tasks leave),
animated values, `Load/SaveToSerializedFileAndForget` (the exact text), a compute shader an importer
makes from source with `ShaderUtil.CreateComputeShaderAsset` (dispatched and read back),
EditorUtility.FormatBytes, analytics, cloud project settings, skin, scene views and gizmos.

`Editor/ProbeSetup.Build` made the assets and the scene in the Editor:

    Unity -batchmode -projectPath tests/projects/editor -executeMethod ProbeSetup.Build -quit

Record Unity's reference:

    EDITOR_PROBE_OUT=$PWD/tests/projects/editor/Reference/unity-6000.0.71f1.json DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/editor \
        -executeMethod ProbeRunner.Record -logFile /tmp/editor.log

Run Unbound (in a window: the compute dispatch needs the GPU) and compare:

    EDITOR_PROBE_OUT=/tmp/editor.json unbound run -p tests/projects/editor --frames 20 --size 320x180 --no-watch
    python3 tests/projects/editor/compare.py tests/projects/editor/Reference/unity-6000.0.71f1.json /tmp/editor.json

`compare.py` lists apart the records that differ by design: the reference Editor runs in batch
mode (Unbound plays as an interactive Editor), signed in to an account (Unbound is not), and
CoreCLR names enum values with aliases differently from Mono.
