# IMGUI control id numbering

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
records, in a game's `OnGUI` (two components on one object):

- two permanent ids (`GUIUtility.GetPermanentControlID`, through reflection) and a
  `GUIUtility.GetControlID` outside `OnGUI`, after the scene loads;
- the ids a fixed sequence of controls (passive, keyboard, hinted passive, a permanent id
  between them, passive, hinted keyboard) gets in the Layout and Repaint passes of six frames:
  the same sequence twice, one control appended, one inserted first, the keyboard control left
  out, the first sequence again; and the ids a second component's `OnGUI` gets;
- the same ids in canonical form (`canon/*`): an id first seen earlier in the run as its ordinal,
  a new id as its offset from a permanent id taken at the start of the pass, so the numbering's
  structure compares apart from where the counter stands; and the order of the `OnGUI` calls.

Measured: one counter gives every new control id and every permanent id (a permanent id between
two new controls sits between their ids; `GetControlID` outside `OnGUI` takes the next number);
a pass finds its controls' ids by hint from where the last one was found, and a control not found
gets a new id, as every later control of that pass does.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/imguicontrolids/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/imguicontrolids/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/imguicontrolids/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/imguicontrolids/run.py gate
```

Residuals (`residuals.json`): the raw numbers (the Editor's other views take ids from the same
counter before and between the game's passes; Unbound draws none), and the order of the `OnGUI`
calls across components (Q771).
