# Built-in resource tables

`names.py <SerializedFile>` lists the objects of one of Unity's built-in resource files
(`Editor/Data/Resources/unity_builtin_extra`, `unity default resources`) as `fileID  classID  name`,
walking the file's type trees. `data/profiles/builtin-<family>.json` is made from its output for each
supported release family; references `{fileID: N, guid: 0000000000000000f000000000000000}` (extra) and
`{fileID: N, guid: 0000000000000000e000000000000000}` (default resources) resolve through it.
