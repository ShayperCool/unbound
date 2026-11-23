# projectwindowutil

What `ProjectWindowUtil` does in Unity 6000.0.71f1 Play mode, where no Project window is open
(`Assets/Scripts/ProjectWindowUtilProbe.cs`): `StartNameEditingIfProjectWindowExists` with an
end action that only writes down its calls (`Assets/Scripts/RecordingNameEditAction.cs`), for
instance ID 0 and a live object's, paths without and with an "assets/" prefix, an existing path
(`Assets/Data/Existing.txt`), a nested path, with and without selecting, and a null action: the
call's instance ID, path and resource file, whether the action survives, and the selection
afterwards (a live object's instance ID is recorded as its name). Also a new end action's hide
flags, the active folder, `GetContainingFolder`, `GetBaseFolders`, `IsFolder` and
`ShowCreatedAsset`. `create.*`: the asset-creating helpers (`CreateAsset` twice and with null,
`CreateFolder` twice, `CreateScene`, `CreateAssetWithContent`, `CreateScriptAssetFromTemplateFile`
with the template `Assets/Data/ScriptTemplate.txt`, a lower-case name, no name, a null and a missing
template, `CreateScriptAssetWithContent`): what was thrown and logged, the file created (a text
file's text, a scene's GameObject names, sorted), the object loaded from it, the selection and the
paths `AssetModificationProcessor.OnWillCreateAsset` heard (`Assets/Scripts/CreationRecorder.cs`);
`create.lineEndings` the project's line endings for new scripts; `ancestors` `GetAncestors` of a file,
a folder, a scene object, 0 and `int.MaxValue` (as asset paths), `ancestors.package` of a built-in
package's `package.json`; `create.scene` also the new scene's skybox material.

```sh
python3 tests/projects/projectwindowutil/run.py record --label a
python3 tests/projects/projectwindowutil/run.py record --label b
python3 tests/projects/projectwindowutil/run.py publish a b
python3 tests/projects/projectwindowutil/run.py gate
```
