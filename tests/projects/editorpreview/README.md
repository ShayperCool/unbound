# Editor preview probe

Records Unity 6000.0.71f1 object previews and object titles:

- `preview.*`: an `ObjectPreview` before and after `Initialize` with one, several, no and null
  targets: `HasPreviewGUI`, `GetPreviewTitle` (a new content each call; "N Types" for several, a
  script's class or an engine type's class name in words), `GetInfoString`, `CreatePreview`, and
  the walk of `MoveNextTarget`/`ResetTarget` with `target` (it throws past the last one).
- `attribute`: the type a `CustomPreviewAttribute` keeps.
- `names.*`: `ObjectNames.GetInspectorTitle` (with and without several objects), `GetClassName` and
  the internal `GetTypeName` of a GameObject, components (with an `[AddComponentMenu]` path, an
  empty one, one that ends with a slash, a class derived from one with a path, an obsolete class, a `MeshFilter` without and with a
  mesh), a ScriptableObject, a material, a texture, a mesh, null and destroyed objects; the
  obsolete `MangleVariableName` and `GetPropertyEditorTitle`.
- `editor.*`: an `Editor`'s preview members for one and several targets and for the generic
  inspector, its internal `targetTitle` and `referenceTargetIndex`, the walk of its targets (the
  index wraps), and `Cleanup` (it throws).
- `draw.*`: `DrawPreview` in a key event of an IMGUI container: the calls a preview and an editor
  get for one target (the whole area) and for three (squares of a grid), with the style names, and
  the errors an editor logs when it reads `targets` or `serializedObject` there (`logs`).
- `excluding*`: the height of what `Editor.DrawPropertiesExcluding` draws for several lists of
  names, and for null (an exception is logged, nothing is drawn).

## Usage

```bash
python3 tests/projects/editorpreview/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/editorpreview/run.py publish run-a run-b
python3 tests/projects/editorpreview/run.py gate
```

The project takes its scene, packages and settings from `tests/projects/uitkbindings`. Values
compare exactly (tolerance 0). The captures are batch-mode ones (`macos_batch`).

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
