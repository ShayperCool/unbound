# IMGUI calls outside OnGUI

`Assets/Scripts/GuiOutsideProbe.cs` (one behaviour with an `OnGUI`) calls IMGUI members outside
`OnGUI`: in `Start`, before the first pass, and in `Update` five frames later, between passes after
OnGUI ran (`Event.current`, `GUI.skin`, `GUI.color`, `GUI.depth`, `GUI.enabled`, `GUI.matrix`,
`GUI.Label`, `GUI.Button`, `GUILayout.Label`, `GUI.BeginGroup`, `GUIUtility.GetControlID`,
`GUIUtility.hotControl`, `GUIStyle.CalcSize`), recording each value or exception.

Measured in the windowed Unity 6000.0.71f1 Linux Editor (two identical captures; the batch-mode
Editor runs no OnGUI): between passes every call answers as before the first pass: `GUI.skin`,
the controls, `GUILayout`, groups and `GUI.skin.label.CalcSize` (through `GUI.skin`) throw `ArgumentException: You can only call GUI
functions from inside OnGUI.`; `Event.current` is null; `GUI.color`, `enabled` and `matrix` keep
their defaults; `GetControlID` gives an id; `hotControl` is 0.

`GUI.depth` reads 1 there (see tests/projects/guidepth).

    python3 tests/projects/guioutside/run.py record --label A   # windowed Editor capture
    python3 tests/projects/guioutside/run.py publish A B        # two identical captures
    python3 tests/projects/guioutside/run.py gate               # the engine vs Reference/
