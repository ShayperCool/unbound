# clonelaunch

Launches of the Unity Editor's executable (`EditorApplication.applicationPath`) from a game Unbound
runs. Unbound's own mechanism, so there is no Editor reference (the Editor would start Unity):
`run.py check` runs the engine on a private copy of this project, whose `LaunchProbe` launches the
executable twice with `Process.Start`, and checks what happened:

* with ordinary Editor arguments (`-batchmode -projectPath ...`): refused, `Process.Start` throws
  (`Win32Exception`, "Operation not permitted"), and the launcher's standard error says why;
* as Multiplayer Play Mode starts a virtual player (the `Process` disposed right after `Start`, so
  the player's output pipes close; `-projectPath <clone> --virtual-project-clone ... -logFile
  <clone>/Logs/Editor.log ... -vpId=...`, the `Logs` folder not made yet): another `unbound run` of
  the clone project (a second private copy) starts; it sees the Editor's executable and the
  player's arguments as its command line, logs a line, writes its command line to `clone.txt` and
  exits; its console log is the `-logFile` (the line it logged there, nothing else beside it) and
  its own output is `VirtualPlayer.log` in its own project data (`LibraryUnbound`), though the
  launcher keeps its project data elsewhere (`--project-data`).

Linux only: on macOS the launch is not intercepted yet and the check does not run.

```sh
python3 tests/projects/clonelaunch/run.py check
```
