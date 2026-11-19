# Input Manager defaults of a project without InputManager.asset

Unity 6000.0.71f1, batch mode, Play mode (two byte-identical captures). The project has no
`ProjectSettings/InputManager.asset`. The gate runs the engine and compares every value exactly.

`Assets/Scripts/InputDefaultsProbe.cs` writes whether the file exists in Play mode, the Input
Manager's axes as `SerializedObject` reads them (all fields of each of `m_Axes`) and what
`Input.GetAxisRaw` and `Input.GetButton` answer for the default names and for a missing one.

Measured: the Editor writes `ProjectSettings/InputManager.asset` when it opens the project (also
when the scripts do not compile), with its 18 default axes (`data/profiles/inputmanager-6000.0.asset`
is that file); Input answers them, and a name without an axis throws as with the file. Packages
may add axes on top (the render pipeline core package registers 12 debug axes in the Editor),
which this probe has none of.
