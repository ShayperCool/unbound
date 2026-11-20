# MenuCommand and menu items that take one

`UnityEditor.MenuCommand`'s constructors and fields, and what `[MenuItem]` commands and validators
that take a MenuCommand receive when the Editor runs them by path
(`EditorApplication.ExecuteMenuItem`, `Menu.GetEnabled`): whether each runs, the command (its
context and user data) and the result. Controls: a parameterless command, a validator that
answers false, one that throws, an item of an object's context menu (`CONTEXT/Transform/...`)
and a command run with a GameObject selected. Recorded in Play mode, in batch mode, with the
Unity 6000.0.71f1 Editor; two identical captures.

Measured: the constructors keep the context and user data (0 without it); a command or validator
run by its path receives a new MenuCommand with no context and user data 0, also for a context
menu item and with a selection; a validator that answers false or throws (its exception logged)
leaves the item disabled and the command not run (ExecuteMenuItem answers false). Not measured:
menu methods with other parameters (Unbound does not run them), whether one command object is
shared between a validator and its command.

The scene and the Play-mode runner are tests/projects/imguiskin's, the packages and project
settings tests/projects/analyticssession's. Values compare exactly.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/menucommand/run.py record --label a
python3 tests/projects/menucommand/run.py record --label b
python3 tests/projects/menucommand/run.py publish a b
UNBOUND=/path/to/unbound python3 tests/projects/menucommand/run.py gate
```
