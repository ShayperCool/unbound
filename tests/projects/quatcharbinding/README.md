# Quaternion and Character SerializedProperty Binding Probe

Records and verifies Unity 6 UI Toolkit `BindingExtensions` (`BindProperty`, `Bind`, `Unbind`) and `PropertyField` behavior for `SerializedPropertyType.Character` (`char`) and `SerializedPropertyType.Quaternion` (`Quaternion` and `Transform.m_LocalRotation`):

- `Character`: `PropertyField` creating/reusing `TextField` (`maxLength = 1`, toggling to `-1` when rebound to `string`), direct `TextField` (`INotifyValueChanged<string>`) and custom `BaseField<char>` (`INotifyValueChanged<char>`) binding, empty/multi-character/null-character edge cases, multi-object mixed state, and incompatible-field warning.
- `Quaternion`: `PropertyField` creating a `Foldout` with lazy/pre-expanded `x`/`y`/`z`/`w` `FloatField` children (including native `Transform.m_LocalRotation`), direct custom `BaseField<Quaternion>` (`INotifyValueChanged<Quaternion>`) binding, component vs whole-quaternion edits, multi-object mixed state, cross-type `PropertyField` rebinding (`Quaternion` <-> `Vector4` <-> `Character`), incompatible-field warning, and `Unbind`/rebind + runtime `DataBinding` coexistence.

## Usage

```bash
python3 tests/projects/quatcharbinding/run.py record --label run-a
python3 tests/projects/quatcharbinding/run.py gate
```

## Reference

- `Reference/unity-6000.0.71f1/values.jsonl`: verified from two independent byte-identical Unity 6000.0.71f1 GPU Editor captures.
