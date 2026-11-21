# sceneviewtypes

What Unity 6000.0.71f1 answers in Play mode for the data types `SceneView.CameraMode` and
`SceneView.SceneViewState` (`Assets/Scripts/SceneViewTypesProbe.cs`): a new state's switches and
derived "…Enabled" answers, after `SetAllEnabled`, with the effects switch off, mixed, copied, the
obsolete members, and what `JsonUtility` writes; a camera mode's default and made values, its text,
equality (shown flag, each field, null against empty), `Equals`, hash agreement and JSON.

```sh
python3 tests/projects/sceneviewtypes/run.py record --label a
python3 tests/projects/sceneviewtypes/run.py record --label b
python3 tests/projects/sceneviewtypes/run.py publish a b
python3 tests/projects/sceneviewtypes/run.py gate
```
