# Array and List Binding Probe

Measures Unity 6000.0.71f1 Editor `PropertyField`, `ListView`, and `BindingExtensions` (`BindProperty`, `Bind`, `Unbind`) behavior for basic serialized arrays and `List<T>` properties (`int[]`, `List<string>`, and serializable struct arrays/lists; excluding `MultiColumnListView`, drag-and-drop reordering, `InspectorElement`, custom `PropertyDrawer`, and `ManagedReference`).

## Documented Unity 6000.0 guarantees exercised
- `UnityEditor.UIElements.PropertyField` creates a `ListView` for array/list `SerializedProperty` values with foldout header, bound collection size field, add/remove footer, and `PropertyField` item rows (`PropertyField`, `ListView`, `BindingExtensions.Bind`, `BindingExtensions.BindProperty`, `BindingExtensions.Unbind`).
- Direct `ListView.BindProperty` and tree `Bind` bind `itemsSource` to the serialized array, synchronize element edits and array-size changes (UI size field, controller `AddItems`/`RemoveItem`/`RemoveItems`/`Move`/`ClearItems`, and external target mutations), and clean up on `Unbind`.
- Multi-object editing exposes `minArraySize`, marks the bound size field mixed when target array lengths differ, and applies unified size edits across all targets.
- Runtime UI Toolkit `DataBinding` coexists with `SerializedObject` list bindings and survives `BindingExtensions.Unbind`.

## Controls recorded
- `pf.int_array.init`, `pf.int_array.edits`, `pf.int_array.resize`: `PropertyField` `ListView` configuration, header foldout expansion, element UI->SO and external->UI edits, size-field growth/shrink/zero, controller `AddItems`/`RemoveItem`/`Move`/`ClearItems`, and external array resize.
- `pf.string_list`: `List<string>` binding via `PropertyField`, element edits, external `Add`/`RemoveAt`, and empty-list state.
- `pf.struct_array`: array of serializable structs with nested `PropertyField` foldouts, first-string dynamic element titles, child field edits, and external struct mutation.
- `direct.listview_default`, `direct.listview_custom`: direct `ListView.BindProperty` with default item callbacks (`showFoldoutHeader = false` and `true`) and with custom `makeItem`/`bindItem`/`unbindItem` callbacks.
- `direct.listview_incompatible`: binding a `ListView` to a non-array struct or scalar property logs the Editor warnings and stays unbound.
- `pf.multi`: multi-object same-size mixed element edits and different-size mixed size field (`minArraySize`, resizing both targets, clearing mixed).
- `pf.rebind`: `PropertyField` rebinding across `int[]` -> `List<string>` (`ListView` reuse), `int[]` -> generic struct (`Foldout`), and `int[]` -> scalar (`IntegerField`) -> `int[]`.
- `unbind_rebind`: `Unbind` resets `ListView` bindings and stops sync while preserving runtime `DataBinding`, and rebinding restores sync.

## Reference
Recorded from two independent Unity 6000.0.71f1 Linux GPU Editor captures into `Reference/unity-6000.0.71f1/values.jsonl`. Other Unity releases are unmeasured.
