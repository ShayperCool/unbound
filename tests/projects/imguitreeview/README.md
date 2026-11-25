# IMGUI TreeView in a game's OnGUI

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures `UnityEditor.IMGUI.Controls.TreeView`, a plain tree and one with a two-column
`MultiColumnHeader`, from a game's `OnGUI`:

- before and after `Reload` and outside `OnGUI`: rows, `totalHeight`, expansion, the tree's
  metrics (`rowHeight`, `baseIndent`, `foldoutWidth`, `depthIndentWidth`, ...) and the header's;
- the first Layout and Repaint: the control ids each tree takes, `GUI.changed`, what each draw
  logs (the header's default styles come from the game skin at their first use, which lacks them),
  every row's `RowGUI` arguments (rects, cells, indents), `GetRowRect` outside the tree and the state;
- the root clip of the game's `OnGUI` (`clip/*`): `GUIClip.visibleRect` and `topmostRect` (read by
  reflection; a tree reads them for its row width) and a tree's `GetRowRect` at the root, in groups
  that leave the screen in part, in whole and nested, in empty and edge-touching groups and in a
  scroll view, in Layout and in Repaint;
- a script of one event per step, in place of a Repaint, given to both trees: a mouse down and up
  far outside them, `SetFocus`, arrow keys with Shift and Control, Home, End, Page Up, Return, a
  search set and cleared, Control-A. Each step records rows, selection, expansion, focus, the
  callbacks (selection, clicks, expansion, `KeyEvent` with the event's type and key code, search),
  keyboard and hot control, the event the first row saw, and whether the scripted event still has
  its own position after the trees' clips are popped.

A mouse event inside a clip takes the pass's real mouse position (the event the pass started
with), so a scripted position does not reach the rows: the trees span y 10-210, or 260-460 when
the display's resting cursor is in the upper band, and nothing recorded depends on the band.
After each step the trees are not drawn for 0.25 s: an expansion or collapse animates for at most
0.07 s of real time, so it ends in the same pass whatever the frame rate.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/imguitreeview/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/imguitreeview/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/imguitreeview/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/imguitreeview/run.py gate
```

The project is `tests/projects/imguiskin`'s scenes, editor runner, packages and settings with this
probe's script over them. The engine run keeps the window focused (`--focus 1:on`).
