# mppmclones

End-to-end check for Multiplayer Play Mode (`com.unity.multiplayer.playmode` 1.5.0) virtual
project clones and `--virtual-player` activation in Unbound:

* Main runs with `--virtual-player 2`, which invokes MPPM's own `UnityPlayer.Activate` path.
* MPPM creates `Library/VP/mppm<hash>` inside Unbound's project view's `Library/`
  (`LibraryUnbound/Library/VP/`, leaving Unity's `Library/` untouched) with symlinked `Assets/`
  and `ProjectSettings/` and an empty `Packages/` folder, then launches the clone via `Process.Start`.
* Unbound intercepts the launch (`editor_launch.rs`), opens the clone with `-library-redirect ../..`
  and `-readonly` (resolving packages from the main project without writing to shared folders),
  and exposes sibling `VP/` metadata (`SystemData.json`) so the clone reads its arguments via
  `Environment.GetCommandLineArgs()`, sees `CurrentPlayer.IsMainEditor == false`, reads
  `CurrentPlayer.ReadOnlyTags()`, and connects to Main's `ChannelService`.
* When Main exits, MPPM's `EditorApplication.quitting` handler closes the clone.
* A follow-up run without `--virtual-player` verifies that saved state in `SystemData.json` does
  not auto-launch clones, and `--virtual-player 1` fails loudly.

Linux only until macOS launch interposition (Q831) is implemented.

```sh
python3 tests/projects/mppmclones/run.py check
```
