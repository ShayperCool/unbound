# Class table probe

Unity's own table of native classes for a release: the class ids that scenes, prefabs and assets
carry (`--- !u!<id>`), the classes of the Manual's "YAML Class ID Reference"
(https://docs.unity3d.com/6000.0/Documentation/Manual/ClassIDReference.html).
`Assets/Editor/ClassTable.cs` writes UnityEditor's list of classes, read through reflection: one
JSON line per class, sorted by id, with its name, base class, native namespace, module and the
abstract, sealed and editor-only flags, then a completion marker.

The canonical Native oracle is `Reference/unity-6000.0.71f1.jsonl` (333 classes, 100 of them
editor-only and 28 abstract; Unity 6000.0.71f1 on Linux, GPU Editor in batchmode). Two independent
captures were byte-identical. The engine embeds it (`managed/UnityEngine/UnityEngine.csproj`):
`ClassCoverage` checks the registered classes against it, and `unbound doctor` names the classes a
project holds that the release does not have and lists the release's classes without a reader.

The Manual's table includes old classes such as `GUILayer` (92) that the measured Editor no
longer loads. `LegacyComponents.cs` opens a copied scene containing that component and independent
AudioListener/FlareLayer controls, then records Edit and Play Mode component lists. The two
independent captures in `Reference/unity-6000.0.71f1-legacy.txt` match: GUILayer is absent in both
modes; the enabled-only controls keep `enabled = false`. Doctor classifies the old GUILayer as
ignored for that measured release and both controls as read, including its release-wide list.
Other releases keep the unsupported-class diagnostic until measured.

FlareLayer's control also acquires a Camera on load. Matching that dependency repair remains a
rendering follow-up; it does not add serialized properties to FlareLayer itself. Unity 6000.0's
[FlareLayer API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/FlareLayer.html)
documents that it has no own properties. The base Behaviour enabled flag has a shared reader.
[Preset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Presets.Preset.html) is an
Editor API for stored properties and applying them to objects. Unbound does not implement that
asset's reader or API and doctor lists ClassID 181963792 explicitly as unsupported.

The ids 100000 to 100011 (`int`, `bool`, `Vector3f`, ...) are serialization placeholders without a
base class, never objects of a file.

## Running

From the repository root, with the release in `UNBOUND_PROBE_UNITY_VERSION`:

```sh
python3 tests/projects/classids/run.py native --label native1   # and native2
python3 tests/projects/classids/run.py publish native1 native2
python3 tests/projects/classids/run.py legacy --label legacy1   # and legacy2
python3 tests/projects/classids/run.py publish-legacy legacy1 legacy2
python3 tests/projects/classids/run.py check                    # the land gate (gate.json)
```

`check` runs `unbound doctor --all --json` on a private copy with the component controls (`UNBOUND_CLI`, else `$UNBOUND`, else
`target/release/unbound`) and checks that the doctor found the release's table and lists as
unread only concrete, non-editor classes of the reference. Run output goes to
`$XDG_CACHE_HOME/unbound/validation/runs/classids/runs/<label>/`.

The second gate command, `python3 tests/projects/classids/field_coverage.py`, copies the
assetbundles fixture and its committed binary bundle into the cache. It checks that both bundle
scripts resolve and their fields are read, while an independent zero-valued unknown field remains
visible and the metadata exception carries a reason. No project callbacks or renderer run.
