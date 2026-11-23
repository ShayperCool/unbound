# Editor internals probe

Records Unity 6000.0.71f1 `InternalEditorUtility` members that editor scripts use:

- `reload.requested`: the internal `IsScriptReloadRequested` with nothing requested.
- `repaint`: `RepaintAllViews` (it throws nothing).
- `expanded.*`: `GetIsInspectorExpanded` and `SetIsInspectorExpanded`: the defaults of a GameObject,
  components, a script component, a ScriptableObject, a texture (expanded) and a material
  (collapsed); null and a destroyed object; after collapsing each object, the other objects of the
  same types (collapsed with them, also ones made afterwards, except another material) and an
  object of another type; expanding one object of a type again; `expanded.scripts.*`: a second
  MonoBehaviour and a second ScriptableObject class, each changed while the first is in the other
  state (each script class has its own state).
- `version.*`: `GetFullUnityVersion`, `GetUnityVersionDigits`, `GetUnityDisplayVersion`(`Verbose`),
  `GetUnityBuildBranch`, `GetUnityBuildHash`, `GetUnityRevision`, `GetUnityVersionDate`,
  `GetUnityProductName`, `GetUnityCopyright` and `Application.unityVersion`: the release's own
  answers. Unbound takes the version and the revision from the project and the installation, the
  rest (branch, build numbers, revision number, date, product name, copyright) from the measured
  release for every release of its family (`Compat.EditorBuild`); other families are unmeasured.
- `layers.*`: the named layers, `ConcatenatedLayersMaskToLayerMask` (bit n is the n-th named layer;
  bits past the last one name nothing) and `LayerMaskToConcatenatedLayersMask` (a layer without a
  name counts as the named layer before it) over lists of masks.

## Usage

```bash
python3 tests/projects/editorinternals/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/editorinternals/run.py publish run-a run-b
python3 tests/projects/editorinternals/run.py gate
```

The project takes its scene, packages and settings from `tests/projects/fileutildelete` (the
built-in layers only). Values compare exactly (tolerance 0). The captures are batch-mode ones
(`macos_batch`), each in a fresh project copy: the expanded state the Editor keeps in a project's
Library between sessions is not recorded.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
