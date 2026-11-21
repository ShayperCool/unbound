# LayerMask and RenderingLayerMask Binding Probe (`maskbinding`)

Measures Unity 6 `MaskField`, `LayerMaskField`, `RenderingLayerMaskField`, and `PropertyField`
binding behavior for `LayerMask` and `RenderingLayerMask` `SerializedProperty` values across
standalone controls, direct `BindProperty`, multi-object mixed selection, tree `Bind`,
same-type reuse, cross-type rebinding, `Unbind`/rebind, runtime `DataBinding` coexistence,
and incompatible field warnings.

Run:
```bash
python3 tests/projects/maskbinding/run.py gate
```
