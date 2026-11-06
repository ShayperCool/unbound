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

The Manual's table lists classes the 6000.0 Editor no longer has, such as `GUILayer` (92). Content
still holding one (written as `Behaviour`) is listed by `unbound doctor` as "not a class of Unity
6000.0.71f1". The ids 100000 to 100011 (`int`, `bool`, `Vector3f`, ...) are serialization
placeholders without a base class, never objects of a file.

## Running

From the repository root, with the release in `UNBOUND_PROBE_UNITY_VERSION`:

```sh
python3 tests/projects/classids/run.py native --label native1   # and native2
python3 tests/projects/classids/run.py publish native1 native2
python3 tests/projects/classids/run.py check                    # the land gate (gate.json)
```

`check` runs `unbound doctor --all --json` on this project (`UNBOUND_CLI`, else `$UNBOUND`, else
`target/release/unbound`) and checks that the doctor found the release's table and lists as
unread only concrete, non-editor classes of the reference. Run output goes to
`$XDG_CACHE_HOME/unbound/validation/runs/classids/runs/<label>/`.
