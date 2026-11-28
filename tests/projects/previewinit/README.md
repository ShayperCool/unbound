# Preview object preparation

The Unity 6000.0.71f1 captures cover `InitInstantiatedPreviewRecursive` and
`ConfigurePreviewObjectSRP` in batch Edit and Play. The recursive helper sets
`HideAndDontSave` on the existing hierarchy, including inactive descendants.
The SRP helper sets the selected renderer's light probe usage to `Off`.
The probe gives renderers a different starting value, checks the quality
override and fallback after destroying it, and calls the SRP helper directly
on the parent with a child that has its own setting. The parent starts with
`HideInInspector`, which recursive preparation clears.

Unity documents the `OnDisable` and `OnEnable` callbacks associated with
hide flags. The probe also assigns the flags directly, and records the order
for both paths. The gate keeps the exact inherited root/child order difference
visible. The two helpers prepare already-created objects. Animator preview
creation and interactive rendering have their own native requirements.
[Unity 6.0 HideFlags](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/HideFlags.html) · [Unity 6.0 Renderer.lightProbeUsage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-lightProbeUsage.html) · [Unity 6000.0.71f1 C#](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0.71f1/Editor/Mono/EditorUtility.cs#L573-L588)

Each capture uses a private project and records the mode. Publish a reference
after two distinct agreeing Linux Editor launches for its mode.

```sh
python3 tests/projects/previewinit/run.py record --profile edit --label edit-a
python3 tests/projects/previewinit/run.py record --profile edit --label edit-b
python3 tests/projects/previewinit/run.py publish --profile edit edit-a edit-b
python3 tests/projects/previewinit/run.py gate
```
