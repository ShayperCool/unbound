# ReorderableList input and callbacks

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
gives scripted mouse and key events, one per frame in place of the frame's Repaint, to lists
drawn with `DoList` in a game's `OnGUI`, and records after each event the callbacks called (in
order, with the list's index and count), the elements, index and selection, the hot and keyboard
control, whether the event was used and `GUI.changed`:

- an IList of strings with every callback: a click on a row, the arrow keys on the focused list
  (wrapping at both ends), a row dragged below another and dropped, a drag where Escape is pressed
  before the drop, the footer's add and remove with callbacks and with an add drop-down callback;
- an IList without callbacks: the default add (an empty string) and remove (the selected row),
  Delete on the focused list;
- a list whose add and remove are refused (`onCanAddCallback`, `onCanRemoveCallback`);
- a multi-select list: a click, action-key clicks toggling, a shift click for a range;
- a non-draggable list: mouse up on its selected row and elsewhere;
- a serialized int array: the default add, a drag, the default remove.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/reorderableinput/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/reorderableinput/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/reorderableinput/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/reorderableinput/run.py gate
```

The probe uses the imguiskin project's scene, packages and settings. Unmeasured: the macOS keys
(Command-Backspace), the left and right arrows on serialized elements, rows of other heights.
