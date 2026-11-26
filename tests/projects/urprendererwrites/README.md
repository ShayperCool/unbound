# What an asset's own load marks dirty

Whether the dirtiness an asset's load callbacks give it survives the load, and what the Editor's
exit then writes. Recorded with the Unity 6000.0.71f1 Linux Editor in batch mode on the GPU (two
identical captures).

Documented (Scripting API 6000.0,
[EditorUtility.SetDirty](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorUtility.SetDirty.html),
[ScriptableObject.OnEnable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ScriptableObject.OnEnable.html)):
SetDirty marks an object so that its changes are saved; OnEnable runs when a scriptable object is
loaded. Nothing says what SetDirty does while the object is being loaded. The Universal Render
Pipeline's renderer data (17.0.4) reloads its missing resources in OnEnable and marks itself dirty
with `EditorUtility.SetDirty` when it reloaded one (`ResourceReloader.ReloadAllNullIn`).

The project is a URP project with the VR and XR modules (so `UniversalRendererData` has its obsolete
`xrSystemData` field). `Assets/Resources/R` holds renderer data: `Current` as the Editor writes a new
one (`run.py create`), and three copies `run.py create` derives from it: `OldMissingRef` (its
`xrSystemData` names an asset that does not exist), `OldNullShader` (without its sampling debug
shader, which the reload fills) and `OldLayout` (as an older pipeline wrote it: both of those,
without the probe volume resources, with the shader resources block it had, a flow map wrapped at
the width and asset version 1). `Assets/Resources/S` holds `DirtyOnLoad` script assets that call
`EditorUtility.SetDirty` from Awake, OnEnable or OnValidate, with a field changed (`*Changed`) or
not (`*Same`).

`Assets/Editor/UrpRendererWritesProbe.cs` (`-executeMethod UrpRendererWritesProbe.Record` /
`unbound run`) enters Play mode, loads every asset at frame 2 and records whether it is dirty, its
XR system data and whether its sampling shader is set (renderers) or its field (scripts)
(`load.*`), and leaves the Editor at frame 4. `run.py` opens one fresh copy once and adds every
file of the project after the run (`exit.<path>`, tests/file_writes.py).

Measured: every asset is clean after its load, also when its load callbacks called SetDirty and
changed a field (the renderers' reload, `OnEnableChanged`'s and `OnValidateChanged`'s field reads
1), and the Editor's exit writes none of them: the files stay as they were. The missing XR system
data reads null.

Unmeasured: a prefab's behaviours' OnValidate during the prefab's load (Unbound leaves the prefab's
objects as clean as they were too), what the Editor writes for such an asset once it is saved
(a missing reference, the obsolete block, the wrapped line).

Create (once), record, publish and check:

    python3 tests/projects/urprendererwrites/run.py create
    python3 tests/projects/urprendererwrites/run.py native --label a
    python3 tests/projects/urprendererwrites/run.py native --label b
    python3 tests/projects/urprendererwrites/run.py publish a b
    python3 tests/projects/urprendererwrites/run.py check
