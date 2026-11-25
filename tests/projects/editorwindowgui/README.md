# EditorWindow rootVisualElement, EditorGUIUtility icons/cursor, Handles drawing, ReorderableList drawing

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures) measures:

- `EditorGUIUtility.SetIconForObject` and `EditorGUIUtility.GetIconForObject` on runtime objects,
  components, null/destroyed objects, overwrites, clearing, and destroyed icons.
- `EditorWindow.rootVisualElement` and `CreateGUI()` lifecycle across `CreateInstance`,
  `GetWindow`, `Show`, child attachment, and `Close`.
- `EditorGUIUtility.AddCursorRect` outside and inside `OnGUI`.
- `Handles` drawing helpers (`DrawAAPolyLine` overloads, `DrawPolyLine`, `DrawLines`,
  `DrawDottedLine`, `DrawDottedLines`, `DrawBezier`, `DrawWireCube`, `DrawWireDisc`,
  `DrawWireArc`, `DrawSolidDisc`, `DrawSolidArc`, `DrawAAConvexPolygon`,
  `DrawSolidRectangleWithOutline`, `Label` overloads) and `HandleUtility.Repaint` outside and
  inside `OnGUI`.
- `ReorderableList.DoList` / `DoLayoutList` outside and inside `OnGUI` (callback invocation order,
  exact header/element/none/footer rects, `isActive`/`isFocused`, visible-rect culling, and default
  drawing).

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorwindowgui/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorwindowgui/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorwindowgui/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorwindowgui/run.py gate
```

Each part outside `OnGUI` runs on its own (an exception is recorded as `<part>/error`). The root
visual element's name counts the Editor's windows, so its digits are recorded as `#`. In Play mode
the first default drawing of a `ReorderableList` logs the styles the game skin lacks (`RL ...`); the
draws inside `OnGUI` record their log lines.
