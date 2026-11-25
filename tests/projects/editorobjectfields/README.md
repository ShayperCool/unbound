# EditorGUI colour, object, curve, gradient, mask, layer, tag and enum-flags fields

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`:

- `EditorGUI` and `EditorGUILayout` `ColorField`, `ObjectField`, `CurveField`, `GradientField`,
  `MaskField`, `LayerField`, `TagField` and `EnumFlagsField` outside `OnGUI` (the exception) and in
  Layout and Repaint: the returned value, the control ids taken, `GUI.changed`, the layout forms'
  rects, indented and mixed-value forms, a null enum, options list or object type;
- the SerializedProperty forms: `PropertyField` of colour, object reference, curve, gradient,
  layer mask, enum, flags enum, vector, rect, bounds and character properties (value, control ids,
  heights in narrow and wide mode, layout rects), `ObjectField`/`CurveField` of a property and
  `MultiPropertyField`;
- `EditorStyles.objectField`, `objectFieldThumb`, `objectFieldMiniThumb`, `objectFieldButton`,
  `colorField`, `colorPickerBox` and `layerMaskField` (fields, text colours, background names);
- the menus' choices sent back as `PopupMenuChanged` and `MaskMenuChanged` commands to the control
  they were opened for, and which DragAndDrop calls reset the drag's visual mode;
- scripted events, one per frame in place of the frame's Repaint: clicks that focus or ping, the
  Delete and Backspace keys and the Delete command, drops of the right and wrong types and of scene
  objects, Control-C/V of a colour (and the clipboard's text), an edit of a property's column, and
  clicks that open the colour picker, eyedropper, curve editor, gradient editor, mask menu and
  object picker (the windows opened are listed and closed at once).

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorobjectfields/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorobjectfields/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorobjectfields/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorobjectfields/run.py gate
```

The engine run keeps the window focused (`--focus 1:on`). Residuals: the editor windows a click
opens (see `residuals.json`).
