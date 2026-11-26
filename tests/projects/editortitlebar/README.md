# EditorGUI and EditorGUILayout InspectorTitlebar

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`:

- `EditorGUI.InspectorTitlebar` (the titlebar without a foldout, the foldable one for an object and
  for objects, expandable or not, and for an `Editor`) and the six `EditorGUILayout` forms, outside
  `OnGUI` (the exceptions) and in Layout and Repaint: the returned value, the control ids taken,
  `GUI.changed` and the layout forms' rects, for a behaviour, a disabled behaviour, a Transform, a
  renderer, a ScriptableObject, a GameObject, two behaviours and an Editor;
- what the titlebar's parts depend on: `EditorUtility.GetObjectEnabled` (also for behaviours with
  one event function each, and one inheriting one), `PresetType.IsValid`, `Help.HasHelpForObject`,
  `AssetPreview.GetMiniThumbnail` and `ObjectNames.GetInspectorTitle`, and the "IN Title" style;
- a scripted sequence of mouse and keyboard events, one per frame in place of the frame's
  Repaint: the title clicked (and released off it), left and right arrows on the focused titlebar,
  the enabled toggle of one behaviour and of two behaviours, one of them disabled;
- `titlebars.png`: the game view (320x180) with six titlebars drawn after the script.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editortitlebar/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editortitlebar/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editortitlebar/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editortitlebar/run.py gate
```

A control's id count reads "used" when the call used the event (GetControlID answers -1 then)
and "used before" when the event already was used.

The probe uses the imguiskin project's scene, packages and settings. The engine run keeps the
window focused (`--focus 1:on`). `titlebars.png` is compared without a limit: Unbound does not
draw the style catalog's styles (the bar's background, foldout arrow and settings button) yet.
