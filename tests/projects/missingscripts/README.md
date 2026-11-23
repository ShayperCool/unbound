# Objects whose script cannot be found

What the Editor logs and gives in Play mode for objects whose `m_Script` names a GUID no file of
the project has (`make_project.py` writes the assets): a scene's component (the scene loaded
twice), a prefab's component (`Resources.Load`, then `Instantiate` twice), ScriptableObject assets
(`AssetDatabase.LoadAssetAtPath`, `LoadAllAssetsAtPath`, `Resources.Load`), a missing-script
sub-asset beside a main object that loads (`Mixed`), one the main object references (`Linked`),
two in one file (`Twice`), an asset whose script reference names nothing (`Empty`), and a component's references to such objects. Each step records the
log messages it raised (type and first line) and what the calls return; values compare exactly.

```sh
/usr/bin/python3 tests/projects/missingscripts/make_project.py
python3 tests/projects/missingscripts/run.py record --label A     # twice, A and B
python3 tests/projects/missingscripts/run.py publish A B
python3 tests/projects/missingscripts/run.py gate
```
