# Built-in struct serialization

Unity 6000.0.71f1. The first 25 rows were recorded twice with the macOS Editor in batch mode; the
`json.curveGradient*` and `json.numbers*` rows were added from two byte-identical Linux Editor
captures in which those 25 rows are unchanged. The canonical reference has 38 rows. The values are serialized text and the
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

- A `Gradient` field is written in JsonUtility text as its eight key slots (`key0`..`key7`, the
  colour keys' colours with the alpha keys' alphas), their times `ctime*`/`atime*` in 1/65535, the
  mode, colour space and numbers of keys; an `AnimationCurve` field as its keys and stored infinity
  modes (`json.curveGradient`). FromJson reads both back, also from the text a gradient is saved
  as elsewhere (`.fromJson`, `.saved`). Either value outside a field writes `{}` (`.direct`).
- Number text (`json.numbers`): as few digits as read the number back, whose last digit is not
  always the closest one (1/7 is written 0.14285714285714286, 0.2f 0.20000000298023225: every
  recorded number agrees with the last digit being moved towards the number only while at most
  nine digits follow the whole part); a
  decimal while the point falls within 21 digits or after up to six zeros, else an exponent;
  negative zero without its sign.
- Reading number text (`json.numbers.fromJson`, `json.numbers.read.*`, values as bit patterns)
  does not always give the nearest double: 4/3e-5 written and read back is one unit in the last
  place higher. A number without a fraction or an exponent is an integer while it fits one (`-0`
  reads as 0, `-0.0` as -0); the same whole number followed by `.0` can read as another double.
  An exponent below -308 reads as 0 whatever the digits, so the smallest positive double written
  as `5e-324` reads back as 0. `1e400` throws ArgumentException "JSON parse error: Number too big
  to be stored in double."; `1e-400` reads as 0. A float field takes the double's nearest float
  (infinity beyond its range). `read.long` holds numbers of more digits than a 64-bit integer,
  which tell apart where the digits stop being gathered exactly and in which order the exponent
  and the fraction's digits scale the result.
- Not recorded: gradient or curve text that names only some members (the Editor does not read it
  the same way twice); negative numbers with twenty and more digits; a fraction of more than 308
  digits; numbers whose digits alone exceed a double; text that is not one number (`007`).

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
