# ScriptedImporter file ids from the .meta's name tables

A project ScriptedImporter (`Assets/Editor/ProbePairImporter.cs`) imports each `.probepair` file as
a 2x2 Texture2D under the identifier `MainAsset` (the main object) and a `ProbePair` script object
under `Data`. The eight assets in `Assets/Imports/` differ only in the name table of their `.meta`:

| Asset | Table |
| --- | --- |
| Legacy | `fileIDToRecycleName: {2800000: MainAsset}` |
| LegacyOtherClass | `fileIDToRecycleName: {11400000: MainAsset}` (class 114, the texture is 28) |
| Named | `internalIDToNameTable: [{first: {28: 1234567}, second: MainAsset}]` |
| Empty | `internalIDToNameTable: []` |
| NamedOtherClass | `internalIDToNameTable: [{first: {114: 7654321}, second: MainAsset}]` |
| LegacyBoth | `fileIDToRecycleName: {2800000: MainAsset, 11400000: Data}` |
| NamedBoth | `internalIDToNameTable`: `{28: 1234567}` MainAsset, `{114: 7654321}` Data |
| OtherName | `fileIDToRecycleName: {2800000: Texture}` (no object has that identifier) |

`Assets/Scripts/FileIdProbe.cs` writes the local file id of each asset's main object and data
object, and what each reference of `Assets/Resources/Refs.asset` resolves to: every asset with the
file ids 2800000, 11400000, 1234567, 7654321, the two derived ids (xxHash64 of
`Type:Texture2D->MainAsset0` and `Type:MonoBehaviour->Data0`) and 5, each as an untyped
`Object`, a `Texture2D` and a `ProbePair` reference.

Measured in the Unity 6000.0.71f1 Editor (two identical captures on the NVIDIA driver): an entry
naming an object's identifier and class gives it that file id, and its derived id then names
nothing; an entry of another class, or of an identifier no object has, is ignored; a reference of
another type than the object it names is empty. This is a measured answer for this release and
these two object classes; tests/projects/shadergraph covers a Shader Graph shader named by
`4800000: MainAsset`.

    export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
    python3 tests/projects/scriptedfileids/run.py record --label A
    python3 tests/projects/scriptedfileids/run.py record --label B
    python3 tests/projects/scriptedfileids/run.py publish A B
    python3 tests/projects/scriptedfileids/run.py gate

The gate runs the engine on a fresh copy and compares every value exactly with `Reference/`.
