# ObjectReference Binding Probe (`objectbinding`)

Measures Unity 6 `ObjectReference` `SerializedProperty` binding behavior across standalone `ObjectField`,
direct `BindProperty`, typed and untyped custom `INotifyValueChanged<T>` elements, multi-object mixed
selection, tree `Bind`, `PropertyField` type inference and cross-type rebinding, missing/destroyed object
references, `Unbind`/rebind, runtime `DataBinding` coexistence, and incompatible field warnings.

Run:
```bash
python3 tests/projects/objectbinding/run.py gate
```
