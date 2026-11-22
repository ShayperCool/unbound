# filepanels

What `EditorUtility`'s file and folder panels and `DisplayDialog` answer and log in a batch-mode
Unity 6000.0.71f1 Editor's Play mode, where nobody can answer a panel
(`Assets/Panels/FilePanelProbe.cs`): `SaveFilePanelInProject` (both overloads, null arguments, a
path outside the project), `SaveFilePanel`, `OpenFilePanel`, `OpenFilePanelWithFilters` (also an
odd number of filters, none, null), `OpenFolderPanel`, `SaveFolderPanel`, and `DisplayDialog`
with and without buttons and text. Each row has the answer (or what is thrown, with its message)
and the messages logged.

Every panel is cancelled with the assertion "Cancelling FileDialog: <title>" (the project save
panel adds its message) and a second line "This should not be called in batch mode."; the dialog
likewise as "Cancelling DisplayDialog: <title> <message>" (`tests/projects/editorui` records that
assertion's first line).

Not recorded: a windowed Editor, where the panels open. An open panel without a title is named
"Open" on the macOS Editor these captures come from.

```sh
python3 tests/projects/filepanels/run.py record --label a
python3 tests/projects/filepanels/run.py record --label b
python3 tests/projects/filepanels/run.py publish a b
python3 tests/projects/filepanels/run.py gate
```
