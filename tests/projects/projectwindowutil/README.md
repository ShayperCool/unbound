# projectwindowutil

What `ProjectWindowUtil` does in Unity 6000.0.71f1 Play mode, where no Project window is open
(`Assets/Scripts/ProjectWindowUtilProbe.cs`): `StartNameEditingIfProjectWindowExists` with an
end action that only writes down its calls (`Assets/Scripts/RecordingNameEditAction.cs`), for
instance ID 0 and a live object's, paths without and with an "assets/" prefix, an existing path
(`Assets/Data/Existing.txt`), a nested path, with and without selecting, and a null action: the
call's instance ID, path and resource file, whether the action survives, and the selection
afterwards (a live object's instance ID is recorded as its name). Also a new end action's hide
flags, the active folder, `GetContainingFolder`, `GetBaseFolders`, `IsFolder` and
`ShowCreatedAsset`.

```sh
python3 tests/projects/projectwindowutil/run.py record --label a
python3 tests/projects/projectwindowutil/run.py record --label b
python3 tests/projects/projectwindowutil/run.py publish a b
python3 tests/projects/projectwindowutil/run.py gate
```
