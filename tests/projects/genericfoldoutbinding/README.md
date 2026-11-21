# Generic Inline-Foldout Binding Probe

Measures Unity 6000.0.71f1 Editor `PropertyField` and `BindingExtensions` (`BindProperty`, `Bind`, `Unbind`) behavior for generic inline-foldout `SerializedProperty` containers (serializable structs and classes, excluding arrays/lists, `ToggleButtonGroupState`, `ManagedReference`, and custom `PropertyDrawer`s).

## Documented Unity 6000.0 guarantees exercised
- `UnityEditor.UIElements.PropertyField` creates a `Foldout` for non-array `Generic` properties with visible children and binds its expansion state (`SerializedProperty.isExpanded`) and child `PropertyField` controls (`PropertyField`, `BindingExtensions.Bind`, `BindingExtensions.BindProperty`, `BindingExtensions.Unbind`).
- `SerializedProperty.hasVisibleChildren`, `SerializedProperty.isExpanded`, `SerializedProperty.NextVisible`, and `SerializedProperty.GetEndProperty` govern inline child traversal and visibility (`[HideInInspector]` excluded).
- Runtime UI Toolkit `DataBinding` coexists with `SerializedObject` bindings and survives `BindingExtensions.Unbind`.

## Controls recorded
- `prop.meta.*`: `propertyType`, `type`, `hasChildren`, `hasVisibleChildren`, `isArray`, `displayName` across basic, nested, empty, hidden-only, mixed-visibility, and string-first serializable structs/classes.
- `pf.basic.collapsed`, `pf.basic.expanded`, `pf.basic.ui_to_so`, `pf.basic.external_to_ui`, `pf.basic.collapsed_after`: lazy child creation on expansion (`foldout.childCount` 0 while collapsed), child names/paths/types/labels, bidirectional UI and external target edits, `SerializedPropertyChangeEvent` dispatch, and child retention on collapse.
- `pf.pre_expanded`: `isExpanded == true` prior to `Bind` populates and binds children immediately.
- `pf.nested`: two-level nested generic foldouts (`outer` -> `outer.inner`), scalar/`Vector3`/`Color` siblings, lazy inner expansion, and two-way edits.
- `pf.labels`: explicit custom label, empty string `""` label fallback, and dynamic `PropertyField.label` updates across `null`, explicit text, and `""`.
- `pf.empty_and_hidden`: empty struct, hidden-only struct, and mixed-visibility struct (`[HideInInspector]` field omitted).
- `direct.foldout_and_tree`: direct `Foldout.BindProperty` two-way `isExpanded` sync and `Bind` tree with nested `BindableElement` + `PropertyField` + relative scalar field.
- `pf.multi`: multi-object mixed and aligned child fields inside a generic foldout and editing a mixed child.
- `pf.rebind`: `Generic` -> `Generic` reuse/trimming (more/fewer children), expanded -> collapsed rebind, `Generic` -> empty `Generic`, and `Generic` <-> scalar replacement.
- `unbind_rebind`: `Unbind` stops foldout and child sync while preserving runtime `DataBinding`, and rebinding restores sync.
- `pf.string_first_elem_title`: dynamic foldout title label binding for an array-element struct whose first field is a `string`.

## Reference
Recorded from two independent Unity 6000.0.71f1 Linux GPU Editor captures into `Reference/unity-6000.0.71f1/values.jsonl`. Other Unity releases are unmeasured.
