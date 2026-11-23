# PlayerSettings values

Records Unity 6000.0.71f1 `PlayerSettings` and `EditorUserBuildSettings` members that editor scripts
read and set, in Play mode (batch mode Editor, macOS; the values do not depend on the OS):

- `project`: the values as `ProjectSettings/ProjectSettings.asset` has them, every one changed from a
  new project's (the splash screen and its Unity logo off, the play mode test runner on, a resizable
  window, Windowed full screen mode, the iOS and tvOS simulator SDKs, automatic signing on, a team
  ID, iOS and tvOS provisioning profile IDs and types, WebGL closing on quit).
- `activeBuildTargetGroup`: the internal `EditorUserBuildSettings.activeBuildTargetGroup` (the
  standalone target itself is the OS's and is not recorded).
- `set`: each value after setting it to another; `set.file` the file's lines for them after
  `AssetDatabase.SaveAssets()` (unchanged: Play mode changes are not saved);
  `set.automaticSigningAgain` automatic signing set on again.

## Usage

```bash
python3 tests/projects/playersettingsvalues/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/playersettingsvalues/run.py publish run-a run-b
python3 tests/projects/playersettingsvalues/run.py gate
```

The project takes its scene, packages and other settings from `tests/projects/fileutildelete`.
Values compare exactly.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
