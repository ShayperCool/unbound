# Settings assets

The project's `ProjectSettings/*.asset` as editor code sees them. `SettingsProbe` records, in Play
mode, what AssetDatabase loads at each path (count, class, name, GUID), every property of a
SerializedObject over each object in the iterator's order (path, type, type name, depth, array size,
value), the objects `Unsupported.GetSerializedAssetInterfaceSingleton` hands out by name; then it
edits through SerializedObjects — an input axis as URP's `InputRegistering` adds one, a tag, a
layer, the product name, the gravity — and records what the engine makes of them at once and
whether AssetDatabase.SaveAssets writes the files (in Play mode it does not).

Measured (see `managed/UnityEngine/Editor/SettingsAssets.cs`): settings objects are of the Editor's
classes (`UnityEngine.Object` for PhysicsManager, NavMeshProjectSettings and the others without a
managed class) with fixed GUIDs; SerializedObjects iterate their native type trees — a string's
characters as an array of chars, an array's `Array` node at its own depth, a reference's
`m_FileID` holding the loaded object's instance ID, arrays of numbers kept in the file as one hex
string, the QualitySettings per-platform defaults shown empty. Applied edits take effect at once.
In Edit mode SaveAssets writes the edited files, re-written whole yet unchanged but for the edits.

The type trees Unbound iterates come from the Editor: `ProbeRunner.Schema` dumps them
(`Reference/schema.jsonl`, every empty array given one new element so its element's tree and a new
element's values are there), `schema.py` turns them into
`managed/UnityEngine/Editor/Data/settings-types-6000.0.json`.

Make the probe scene (once; it is committed):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -nographics -projectPath tests/projects/settings \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/settings-create.log

Record Unity's reference (the Editor needs a display), the files it saves in Edit mode, the schema:

    SETTINGS_PROBE_OUT=$PWD/tests/projects/settings/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/settings \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/settings.log
    for m in Save Schema; do ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -nographics \
        -projectPath tests/projects/settings -executeMethod ProbeRunner.$m -logFile ~/.cache/settings-$m.log; done
    python3 tests/projects/settings/schema.py tests/projects/settings/Reference \
        managed/UnityEngine/Editor/Data/settings-types-6000.0.json

Run Unbound and compare:

    SETTINGS_PROBE_OUT=/tmp/settings unbound run -p tests/projects/settings --headless 300000 --fixed-fps 60
    python3 tests/projects/settings/compare.py tests/projects/settings/Reference /tmp/settings

Every record matches (all 4548 properties of the 19 files). The saved files are checked by
`SettingsAssetTests.SavedSettingsAreTheEditors` (the same edits in Edit mode against
`Reference/saved`, byte for byte), and every probe project's settings file reads and writes back
unchanged (`UnityYamlTextWritesFilesBackAsTheyWere`).
