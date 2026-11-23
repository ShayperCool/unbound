# When an import's messages reach the log

`Assets/Editor/ImportLogsProbe.cs` records, in a project imported fresh, the messages two imports log:
a USS warning (`Assets/Styles/Bad.uss`, `-unity-font-definition: none`) and an `AssetPostprocessor`'s
warning (`Assets/Editor/LoggingPostprocessor.cs`, for `Assets/Textures/Logged.png`, as package
postprocessors such as the 2D Animation package's sprite outline warning do). A listener registered when
scripts load records the matching messages before Play mode, when Play mode starts, while Play mode
loads the two assets, while Play mode imports new copies of them (`AssetDatabase.ImportAsset`) and
loads those, forces the import of a project asset not loaded yet (`Assets/Styles/Forced.uss`,
`ImportAssetOptions.ForceUpdate`), and refreshes after writing a new USS file.

Measured on the Unity 6000.0.71f1 Editor (two identical macOS batch captures):

- The project's first import logs its messages before any script loads: they are in the Editor's log
  only. Loading those assets in Play mode logs nothing.
- An import in Play mode (`ImportAsset`, forced or of a new file, or a `Refresh` that finds a new
  file) logs its messages in the call; loading the asset after it logs nothing more.

Unbound imports an asset when it is first loaded. An asset not imported in this session's Play mode
(the project's own, as it opened) logs its import's messages to Unbound's log (its Editor.log) only, so
Play mode's handlers do not get them. A forced `ImportAsset` of an asset not loaded yet imports it in
the call, as the Editor does; a new file imported by `ImportAsset` or found by `Refresh` logs its
messages to Play mode at its first load (a known residual: the Editor logs them in the call). Messages
of loads that are not imports (a prefab's missing scripts: tests/projects/missingscripts) stay Play
mode's.

```sh
python3 tests/projects/importlogs/run.py record --label A     # twice, A and B
python3 tests/projects/importlogs/run.py publish A B
python3 tests/projects/importlogs/run.py gate
```
