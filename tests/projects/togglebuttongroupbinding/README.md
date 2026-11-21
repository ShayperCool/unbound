# ToggleButtonGroupState SerializedProperty Binding Probe

Records and verifies Unity 6 UI Toolkit `BindingExtensions` (`BindProperty`, `Bind`, `Unbind`) and `PropertyField` behavior for `ToggleButtonGroupState`:

- `PropertyField` creating/configuring `ToggleButtonGroup` (`unity-button-group-<path>`, USS classes, dynamic `"0".."N-1"` buttons, `[ToggleButtonGroupStatePropertiesAttribute]` length/multi/empty options, UI value edits, external `m_Data` and `m_Length` add/remove/empty updates).
- Direct `ToggleButtonGroup.BindProperty` with custom buttons, UXML `<ui:ToggleButtonGroup>`/`<uie:PropertyField>` `binding-path`, incompatible-field warning, multi-object mixed state and edits, `PropertyField` rebinding (`ToggleButtonGroupState` -> `ToggleButtonGroupState`, `Generic` struct `Foldout`, `int`, and back), and `Unbind`/rebind + runtime `DataBinding` coexistence.

## Usage

```bash
python3 tests/projects/togglebuttongroupbinding/run.py record --label run-a
python3 tests/projects/togglebuttongroupbinding/run.py gate
```

## Reference

- `Reference/unity-6000.0.71f1/values.jsonl`: verified from two independent byte-identical Unity 6000.0.71f1 GPU Editor captures.
