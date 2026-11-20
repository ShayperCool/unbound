# ReorderableList state

The probe records the list's construction, backing data and programmatic selection in Play mode on
Unity 6000.0.71f1. It reuses the empty scene and launch script from `tests/projects/imguiskin`.

For an `IList`, editing or replacing the collection changes the count while the selection persists.
Without explicit selection, `index` is the last row or −1. An appended selection is sorted and has
no duplicates. A range needs `multiSelect`, while `Select` with `append` works with either setting.
The returned selection view reflects later edits. The controls also record what happens with invalid
indices, a null backing property, and a scalar or read-only property.

For a serialized array, the Scripting API defines `minArraySize` as the smallest size across all
targets, even above the multi-edit limit. The capture confirms that `ReorderableList.count` uses that
size up to the configured limit and returns zero above it for multiple targets. The single-target
control retains its actual size above the same limit.
[Unity 6000.0 `SerializedProperty.minArraySize`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedProperty-minArraySize.html)

The height controls include the empty row, positive and zero or negative row heights, a hidden
header, disabled buttons and the serialized multi-edit limit. The callback controls record the
indices visited during the first measurement, after invalidation, and after the list grows or
shrinks. A stable height is cached. Assigning a new callback alone leaves the cached rows in use.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/reorderablelist/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/reorderablelist/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/reorderablelist/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/reorderablelist/run.py gate
```
