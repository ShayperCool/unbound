# Hash128 Binding Probe (`hash128binding`)

Measures Unity 6 `Hash128` `SerializedProperty` binding behavior across standalone `Hash128Field`,
direct `BindProperty`, multi-object mixed selection, tree `Bind`, `PropertyField` creation/reuse/cross-type
rebinding, `Unbind`/rebind, runtime `DataBinding` coexistence, and incompatible field warnings.

Run:
```bash
python3 tests/projects/hash128binding/run.py gate
```
