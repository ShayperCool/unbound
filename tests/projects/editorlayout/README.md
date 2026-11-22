# Editor layout groups, spacing, and utility helpers

The Unity 6000.0.71f1 Play mode capture measures `EditorGUILayout.BeginFadeGroup` /
`EndFadeGroup` / `FadeGroupScope`, `EditorGUILayout.BeginScrollView` / `EndScrollView` /
`ScrollViewScope`, `EditorGUILayout.Separator` / `Space`, and `EditorGUIUtility.PointsToPixels` /
`PixelsToPoints` / `PingObject` / `LoadRequired` both outside and inside `OnGUI`.

The scroll view rows also put a view with `MaxWidth` and `MaxHeight` options into a larger area:
it keeps its maximum, stretching content fills it, and content wider than the view scrolls.

Unity 6000.0 Scripting Reference:
- [`EditorGUILayout.BeginFadeGroup`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.BeginFadeGroup.html)
- [`EditorGUILayout.BeginScrollView`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.BeginScrollView.html)
- [`EditorGUILayout.Separator`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.Separator.html)
- [`EditorGUILayout.Space`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUILayout.Space.html)
- [`EditorGUIUtility.PointsToPixels`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUIUtility.PointsToPixels.html)
- [`EditorGUIUtility.PixelsToPoints`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUIUtility.PixelsToPoints.html)
- [`EditorGUIUtility.PingObject`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUIUtility.PingObject.html)
- [`EditorGUIUtility.LoadRequired`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUIUtility.LoadRequired.html)

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorlayout/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorlayout/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorlayout/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorlayout/run.py gate
```
