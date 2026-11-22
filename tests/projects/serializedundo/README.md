# SerializedObject undo probe

Records the Undo records Unity 6000.0.71f1 makes for `SerializedObject.ApplyModifiedProperties`,
with the current group, its name (`Undo.GetCurrentGroupName`) and the `Undo.undoRedoEvent` names:

- `one.*`: one property applied (the name before and after `Undo.FlushUndoRecordObjects`), undone
  and redone.
- `none.applied`: an apply without changes.
- `many.*`, `size.*`, `element.*`, `sizeOnly.*`, `grow.*`, `name.*`: the record's name for several
  properties, an array element, an array's size growing and shrinking, a camel-case and an `m_` field.
- `twice.*`, `split.*`: two applies in one group and in two groups.
- `without.*`: `ApplyModifiedPropertiesWithoutUndo` between recorded applies.
- `mixed.*`, `named.*`, `records.*`: an apply after `Undo.RecordObject` in one group, in a group
  named by `Undo.SetCurrentGroupName`, and two `RecordObject` records in one group.
- `priority.*`: which record names a group, for object records, created objects, added components
  and an apply, in both orders.
- `multi.*`, `component.*`: several targets, and a component of a GameObject.
- `stale.*`: a SerializedObject read before an undo, then updated.

## Usage

```bash
python3 tests/projects/serializedundo/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/serializedundo/run.py publish run-a run-b
python3 tests/projects/serializedundo/run.py gate
```

The values depend on neither the OS nor the graphics device (`macos_batch`).

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical captures.
