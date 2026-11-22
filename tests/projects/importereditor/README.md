# importereditor

What `AssetImporterEditor` and `ScriptedImporterEditor` do in Unity 6000.0.71f1 Play mode for
editors made with `Editor.CreateEditor`, where no Inspector window shows them
(`Assets/ImporterEditor/ImporterEditorProbe.cs`). Two scripted importers
(`ProbeImporter` with a custom editor, `PlainImporter` without), editor subclasses that give the
probe the protected members (`ProbeImporterEditors.cs`), one asset per scenario
(`Assets/Things`) and a texture. Each step's row has what it throws, the messages it logs, the
calls the editors write down (all but `HasModified`, which the Editor also asks on its own), and
the state afterwards: the importer's value (asked for by path each time, since an import leaves
an earlier reference with the values it had), whether the editor is modified, `hasUnsavedChanges`,
whether the importer is dirty, the value in the `.meta` file and the imports of the path.

- `class.*`: which editor class inspects an importer, the abstract base class, an editor made
  with `CreateInstance`.
- `settings.*`: the members an Inspector would fill, and settings changed through the editor's
  SerializedObject: discarded, applied, saved, reset.
- `importer.*`: settings changed on the importer itself and its user data, `SaveAndReimport`, and
  what `WriteImportSettingsIfDirty` and `SaveAndReimport` write of a field set with and without
  `EditorUtility.SetDirty`.
- `shared.*`: two editors of one importer, and an editor made after the others were destroyed.
- `extra.*`: an extra data type: the object made per importer, its part in the saved settings, a
  second editor, the object's end.
- `unsaved.*`: what applying, saving and discarding do to `hasUnsavedChanges`.
- `mistake.*`: an extra data type that is no ScriptableObject, one whose initializer is left to
  the base class, an `OnEnable` that skips the base class's, and what the editors that failed in
  `OnEnable` leave saved.
- `several.*`: two targets.
- `applyRevert.*`: an editor that asks for the Apply and Revert buttons: saving writes the
  `.meta` and imports the asset.
- `outside.*`: `ApplyRevertGUI`, `OnInspectorGUI` and the button members called where no GUI runs.
- `texture.*`: a TextureImporter with an editor of the abstract base class.

Not recorded: the buttons and the Asset PostProcessors list as they are drawn (batch mode runs no
GUI), an editor an Inspector shows (the asset members, the Apply and Revert buttons by default),
how often and when the Editor asks `HasModified` itself, and whether an import runs inside the
call that asks for it or with the next load of the asset (the probe loads the asset before it
reads the state, and counts a path's imports from its first load on where that matters).

Unity's Scripting API for 6000.0 (`AssetImporters.AssetImporterEditor`,
docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/assetimporters/assetimportereditor)
describes `needsApplyRevert`, `extraDataType` with `InitializeExtraDataInstance`, `HasModified`,
`Apply`, `DiscardChanges` and `ApplyRevertGUI`, and says `OnEnable` and `OnDisable` overrides must
call the base class's; the captures agree with it where they record the same thing.

The textures come from `tests/projects/spriteatlaseditor`.

```sh
python3 tests/projects/importereditor/run.py record --label a
python3 tests/projects/importereditor/run.py record --label b
python3 tests/projects/importereditor/run.py publish a b
python3 tests/projects/importereditor/run.py gate
```
