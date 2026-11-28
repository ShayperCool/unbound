# Selected asset paths and Project window framing

This probe records `AssetDatabase.GetUniquePathNameAtSelectedPath` in Unity 6000.0.71f1
batch Play mode. It covers project and package folders, selected assets, scene or empty
selection, file names that need validation, and collisions with case variants and numbered names.
The probe measures unimported files, existing directories, and a selected folder removed externally.
It calls the query twice to check that the result is stable and checks that no output or sidecar
is reserved. All fixture writes stay in the private project copy. The meta-only control distinguishes it from
`AssetDatabase.GenerateUniqueAssetPath`.

The framing controls call `ProjectWindowUtil.FrameObjectInProjectWindow` with a folder ID,
zero, and an unknown ID while no Project Browser is open. The probe captures the logger and
checks the selected folder afterward. Interactive framing and selected-path answers on other
release families remain outside the captures.

```sh
python3 tests/projects/selectedassetpath/run.py record --label a
python3 tests/projects/selectedassetpath/run.py record --label b
python3 tests/projects/selectedassetpath/run.py publish a b
python3 tests/projects/selectedassetpath/run.py gate
```
