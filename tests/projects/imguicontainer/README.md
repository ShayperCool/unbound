# IMGUIContainer probe

Records Unity 6000.0.71f1 `IMGUIContainer`s in a runtime panel (a `UIDocument` with a constant
pixel size, drawn into a target texture):

- `auto.*`: a container sized by its IMGUI layout: the handler's events on each frame after it is
  added (one Layout pass, and one more on the next frame), its layout, more content without and
  with `MarkDirtyLayout`.
- `late.*`: a container added once the panel has painted, marked dirty with the same content, and
  removed and added again (one Layout pass, as it was painted before).
- `foldout.*`: the class a container has at each depth inside foldouts, and after it is moved out.
- `fixed`, `empty`: a container with a fixed size (never measured) and one without a handler.
- `mouse.*`, `key.down`: a `MouseDownEvent`/`MouseUpEvent`/`KeyDownEvent` made from an IMGUI event
  and sent to a container (a Layout pass, then the event; the handler sees the mouse where the GUI
  last saw it, here the panel's corner, not where the sent event says, so the button under the
  event's position does not take the press), and a mouse event without an IMGUI event (not
  passed on).
- `focus`: the container is focusable and cannot take focus without a control that can.
- `paint.*`: the handler gets no Repaint in a runtime panel, and nothing is drawn into the target.
- `property*`: a `PropertyField` whose drawer has only IMGUI: its `IMGUIContainer`, the heights, and
  the drawer's calls.
- `nested*`, `ranged*`: drawers that measure and draw their own property again
  (`EditorGUI.GetPropertyHeight`, `EditorGUI.PropertyField`, `EditorGUILayout.PropertyField`): they
  get the property's default GUI, for a drawer of the type and for a drawer of an attribute.
- `spaced*`, `imgui.*`: a property with a decorator: a `PropertyField` shows the decorator itself
  above its drawer's IMGUI, and `EditorGUILayout.PropertyField` in a container's handler gives it
  the top of the property's rect, with and without a drawer; a property whose IMGUI a
  `PropertyField` has drawn keeps its decorator left out of later IMGUI calls (`imgui.spaced`).

- `builtin.*`: properties with the Editor's own attributes and types (`[Header]`, `[TextArea]`,
  `[Space]`, `[Range]`, `[Multiline]`, an array, a vector): their heights inside a GUI pass and
  outside one, and the rects `EditorGUILayout.PropertyField` gives them.

## Usage

```bash
python3 tests/projects/imguicontainer/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/imguicontainer/run.py publish run-a run-b
python3 tests/projects/imguicontainer/run.py gate
```

The project takes its scene, packages and settings from `tests/projects/uitkbindings`. Values
compare exactly (tolerance 0). The captures are batch-mode ones (`macos_batch`).

## Residuals

None: the heights of Editor-context containers and of a `[TextArea]` come from the Editor's GUI
skin (`tests/projects/editorskin`).

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
