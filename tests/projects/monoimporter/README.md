# monoimporter

What `MonoImporter.GetExecutionOrder` answers in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/MonoImporterProbe.cs`; two byte-identical macOS batch Editor captures): for
scripts ordered by their `.meta` file (`MetaOrdered`, 50), by `[DefaultExecutionOrder]` only
(`AttributeOrdered`, 100), by both (`BothOrdered`, -20 in the `.meta`, 30 in the attribute) and by
neither (`Unordered`), for the script of a behaviour (`MonoScript.FromMonoBehaviour`) and for null;
and `MonoImporter.GetScript` of each script's importer (`getScript.*`).

Measured: the `.meta` order (the Script Execution Order settings) alone; the attribute does not
count (0 for `AttributeOrdered`, -20 for `BothOrdered`); null throws ArgumentNullException. `GetScript` gives the script asset itself, the object
`AssetDatabase.LoadAssetAtPath` loads.

```sh
python3 tests/projects/monoimporter/run.py record --label a
python3 tests/projects/monoimporter/run.py record --label b
python3 tests/projects/monoimporter/run.py publish a b
python3 tests/projects/monoimporter/run.py gate
```
