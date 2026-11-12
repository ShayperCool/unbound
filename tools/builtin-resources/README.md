# Built-in resource tables

`names.py <SerializedFile>` lists the objects of one of Unity's built-in resource files
(`Editor/Data/Resources/unity_builtin_extra`, `unity default resources`) as `fileID  classID  name`,
walking the file's type trees. `data/profiles/builtin-<family>.json` is made from its output for each
supported release family; references `{fileID: N, guid: 0000000000000000f000000000000000}` (extra) and
`{fileID: N, guid: 0000000000000000e000000000000000}` (default resources) resolve through it.

Each entry's `path` is the name the release registers the object under for
`Resources.GetBuiltinResource` (default resources) and `AssetDatabase.GetBuiltinExtraResource`
(extra), one per object. `names.py` does not produce it (the files do not store it): a new
release's table gets its paths separately; without them every lookup answers null with Unity's
not-found assertion and error. `removed` lists the paths the release no longer registers, each with the path looked up
instead and the message the lookup then throws. `tests/projects/builtinresources` asks the Editor for every path with the
object's class and records which object answers, so a release's paths are confirmed by recording
that probe (its `Assets/Resources/BuiltinPaths.txt` lists them) against the release's Editor.
