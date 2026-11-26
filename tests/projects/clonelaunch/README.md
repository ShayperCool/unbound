# clonelaunch

Launches of the Unity Editor's executable (`EditorApplication.applicationPath`) from a game Unbound
runs. Unbound's own mechanism, so there is no Editor reference (the Editor would start Unity):
`run.py check` runs the engine on a private copy of this project, whose `LaunchProbe` launches the
executable twice with `Process.Start`, and checks what happened:

* with ordinary Editor arguments (`-batchmode -projectPath ...`): refused, `Process.Start` throws
  (`Win32Exception`, "Operation not permitted");
* as Multiplayer Play Mode starts a virtual player (`-projectPath <clone> --virtual-project-clone
  ... -logFile <file> ... -vpId=...`): another `unbound run` of the clone project (a second private
  copy) starts; it sees the Editor's executable and the player's arguments as its command line,
  writes them to `clone.txt` and exits 0; its output goes to the `-logFile`.

Linux only: on macOS the launch is not intercepted yet and the check does not run.

```sh
python3 tests/projects/clonelaunch/run.py check
```
