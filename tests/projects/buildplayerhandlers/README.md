# BuildPlayerWindow build handlers

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode in Play mode (two byte-identical
captures). None of the values depends on the OS or the graphics device; the gate compares every
value exactly.

`Assets/Editor/BuildPlayerHandlersProbe.cs` (an editor script run when Play mode starts) calls
`BuildPlayerWindow.RegisterBuildPlayerHandler` with a first handler, a second one, the second again,
null and a third, and `RegisterGetBuildPlayerOptionsHandler` with a first, a second, null and a
third. Each row: the call's result, the logs it wrote and which handler the window keeps
(`buildPlayerHandler` / `getBuildPlayerOptionsHandler`, read by reflection: this one, another, null).

Measured: each call keeps the handler it was given; one given while another (or the same) is kept
logs the warning "The build player handler in BuildPlayerWindow is being reassigned!" (for the
options handler "The get build player options handler in BuildPlayerWindow is being reassigned!");
null resets it without a warning.

Unbound builds no player and has no Build Settings window, so the kept handlers are never called.
