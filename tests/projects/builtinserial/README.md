# Built-in struct serialization

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 25 rows. The values are serialized text and the
values read back, so they do not depend on the OS or the graphics device. The gate compares every
missing, extra or unequal row exactly.

The probe holds `RenderingLayerMask` and `SphericalHarmonicsL2` fields at the root of a
ScriptableObject, in a nested serializable class, in an array and in a list.

- Neither type is CLR serializable (`Type.IsSerializable` is false), yet both serialize as
  built-in values. A mask is `{"serializedVersion":"0","m_Bits":N}`, and harmonics are 27
  coefficients named `sh[ 0]` to `sh[26]` (red, green, blue) in JsonUtility and EditorJsonUtility
  text. Serializing either value outside a field writes `{}`.
- FromJson reads the text back. FromJsonOverwrite and EditorJsonUtility.FromJsonOverwrite replace
  diverged values. Text that names only some members (`"mask":{}`, two coefficients) leaves the
  others as they were.
- Instantiate copies the values. An authored asset (`Assets/Resources/Authored.asset`, in the
  Editor's text form) loads them. A text-serialization round trip
  (`InternalEditorUtility.SaveToSerializedFileAndForget`, then `LoadSerializedFileAndForget`)
  keeps them.
- In the property tree, a mask is a `RenderingLayerMask` property with one hidden `uint` child
  `m_Bits`. Its int, uint and long values are the bits (−1, 4294967295 and 4294967295 for all
  bits set). Harmonics are a Generic `SphericalHarmonicsL2` property with 27 visible float
  children. Writes through the properties reach the object.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [JsonUtility.ToJson](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.ToJson.html), [JsonUtility.FromJsonOverwrite](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.FromJsonOverwrite.html) | `json.*` over values that differ from the text; `json.partial` names only some members. |
| [Script serialization](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization.html) | `type.*`, `authored`, `clone`, `text.roundTrip`. |
| [SerializedProperty](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedProperty.html) | `tree.*` and `property.*`. |

Not compared: the exact text Unbound writes, which a managed test pins to the Editor's text.
`AssetDatabase.CreateAsset` (not supported by Unbound). The tree of the array containers
themselves, whose depths differ for every managed array, not only these types (an open row in
`docs/COMPATIBILITY_GAPS.md`). Other releases are
unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/builtinserial/run.py native --label reference-a
python3 tests/projects/builtinserial/run.py native --label reference-b
python3 tests/projects/builtinserial/run.py publish reference-a reference-b
python3 tests/projects/builtinserial/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
