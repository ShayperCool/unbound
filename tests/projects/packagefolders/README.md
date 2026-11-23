# Package folders and files in the asset database

`Assets/Editor/PackageFoldersProbe.cs` records in Play mode how the asset database answers for a
built-in package's root folder and `package.json`, a registry package's root folder (which has no
`.meta` file), its `package.json`, a subfolder and a script in it, the `Packages` folder itself, a
package that is not installed, and an `Assets` folder and script to compare: whether there is a GUID
and the path it leads back to, `IsValidFolder`, the main asset's type, name and hide flags, its path
by object and by instance ID, `IsMainAsset`, `Contains`, its local file ID and
`ProjectWindowUtil.GetAncestors` of it (as paths). Also the root folders' GUIDs, whether
`GetAllAssetPaths` lists the roots and `FindAssets` finds them, `GetSubFolders` of `Packages`, a
registry root, a missing folder, a file and `Assets/Sub` (folders whose names sort apart by case,
digits and punctuation), and `EditorUtility.NaturalCompare`'s sign for 24 pairs of names.

Measured on the Unity 6000.0.71f1 Editor (two identical macOS batch captures; the values depend on
neither the OS nor the graphics device):

- Every package's root folder is a folder asset (`DefaultAsset`, `NotEditable`) with a GUID, listed by
  `GetAllAssetPaths` and found by no `FindAssets` search, as the `Assets` folder; `Packages` itself is
  no folder (`IsValidFolder` false, no subfolders). `GetAncestors` walks up to the package root, then
  gives `int.MaxValue` for `Packages`.
- A built-in package's root GUID is the MD4 of the package's name, written as Unity writes GUIDs
  (each byte's low nibble first). A registry package's is something else, not derived here (a known
  residual: Unbound gives it the MD4 of its name too).
- A `package.json` (PackageManifestImporter) loads as `UnityEditorInternal.PackageManifest`, a
  `TextAsset` named "package", `NotEditable`, at local file ID 8385607101436470782 in every package.
- `GetSubFolders` lists folders in `EditorUtility.NaturalCompare`'s order: characters
  case-insensitively by code, ASCII digit runs as numbers, then the shorter name, then more leading
  zeros first; names equal but for case compare equal.

```sh
python3 tests/projects/packagefolders/run.py record --label A     # twice, A and B
python3 tests/projects/packagefolders/run.py publish A B
python3 tests/projects/packagefolders/run.py gate
```
