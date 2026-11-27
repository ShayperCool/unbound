# Which importer imports a file: its extension or its .meta

Which importer the Editor uses for a file whose `.meta` names an importer other than the one its
extension has, or none, what it imports, what opening the project and `SetLabels` write into the
`.meta`, and what the `AssetDatabase` importer queries answer. Unity 6000.0.71f1, recorded with
the macOS Editor in batch mode (two identical captures): nothing depends on the OS or the graphics
device.

`Assets/Editor` holds two ScriptedImporters, `FooImporter` (`.foo`) and `BarImporter` (`.bar`),
each importing a text file as a TextAsset whose text names the importer, with an `int setting`.
`Assets/Data` holds:

| file | its `.meta` |
| --- | --- |
| `Own.foo` | FooImporter's section, `setting: 7` (the control) |
| `Other.foo` | BarImporter's section |
| `Named.bar` | FooImporter's section, `setting: 7` |
| `Unclaimed.qux` | FooImporter's section, `setting: 7` (no importer declares `.qux`) |
| `Plain.txt` | FooImporter's section, `setting: 7` (`.txt` is the TextScriptImporter's) |
| `Default.foo`, `Text.foo` | a DefaultImporter's, a TextScriptImporter's section |
| `Guid.foo`, `Guid.xyz`, `Guid.uss` | the GUID alone |
| `New.foo`, `New.part`, `New.qux` | none |

`Assets/Scripts/ImporterPickProbe.cs` records in Play mode into values.jsonl in
`$IMPORTERPICK_OUT` (tests/value_probe.py), each `.meta` with its GUID line read `guid: (guid)`:
for each file `open/<file>` (its `.meta` as the project opened), `importer/`, `imported/` (the
importer's class and setting, the main asset), `type/`, `default/`, `available/`, `override/`
(`AssetDatabase.GetImporterType`, `GetDefaultImporter`, `GetAvailableImporters`,
`GetImporterOverride`); then `labels/<file>` and `labels/<file>/meta`: `SetLabels`, the importer,
the main asset and the file. Then `Assets/Session/Refreshed.foo`, `.part` and `.qux` are written
without a `.meta` and `AssetDatabase.Refresh` called, `Imported.foo` written and imported by
`ImportAsset`: `session/<file>` (the `.meta`), `/importer` and `/imported`.

What it shows: the extension decides. A file of an extension a ScriptedImporter declares is
imported by that importer whatever its `.meta` names (none, another ScriptedImporter, the
DefaultImporter or the TextScriptImporter); a file of a native importer's extension by that
importer, of an extension nobody declares by the DefaultImporter (`AssetImporter`, a
`DefaultAsset`), whatever ScriptedImporter its `.meta` names. The `.meta`'s settings apply only to
the importer it names (`Own.foo` keeps `setting: 7`; the others read the defaults). As the project
opens the Editor writes the `.meta` whose section names another ScriptedImporter than the
extension's, with that importer's section at its defaults; the others keep their section until they
are written again (here by `SetLabels`), which then writes the importer's own section: the
ScriptedImporter's at its defaults, the TextScriptImporter's or the DefaultImporter's. The queries
answer the importer used; `GetDefaultImporter` and `GetAvailableImporters` give none for an
extension nobody declares. A file without a `.meta`, found as the project opens, by `Refresh` or
by `ImportAsset`, gets the `.meta` of its extension's importer: the ScriptedImporter's section with
the importer's fields at their defaults, or the DefaultImporter's for an extension nobody declares.

Not covered: a ScriptedImporter that declares a native importer's extension, `overrideExts`,
`SetImporterOverride`, two ScriptedImporters declaring one extension, a ScriptedImporter class
removed or renamed after its files were imported.

Record and gate:

    python3 tests/projects/importerpick/run.py record --label a   # twice, then
    python3 tests/projects/importerpick/run.py publish a b
    python3 tests/projects/importerpick/run.py gate
