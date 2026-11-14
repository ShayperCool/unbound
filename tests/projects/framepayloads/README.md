# Frame-rate scalar serialization

Unity 6000.0.71f1, recorded twice with the NVIDIA GPU. The canonical reference has 156 byte-identical rows. The gate compares every missing, extra or unequal row exactly.

The selected FrameRate declaration contains one private integer marked SerializeField. Its type has no Serializable marker. Eight scalar inputs cover zero, small positive values, signed frame rates and both integer limits. Root JSON writes the private integer; reconstruction and boxed overwrite restore it, including its drop-frame and validity state. Empty and unknown-field overwrites preserve the existing value. Constructor-created positive and drop-frame values also serialize.

A closed generic holder tests nested eligibility. Its seeded FrameRate stays omitted, and overwrite preserves that value while changing the sibling from 11 to 21. Two custom-struct controls use the same private field annotation. The unmarked type behaves like FrameRate, while the Serializable type writes its nested payload and changes from 37 to 53. Both siblings change to 23. These controls distinguish nested eligibility from private field visibility and verify that the generic holder itself works.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [SerializeField](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializeField.html) | Nondefault private scalar values appear in root JSON and reconstruct, rather than yielding an empty object or zero. |
| [Serialization rules](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization-rules.html) | Matched marked/unmarked structs differ only in type eligibility; the nested marked value changes and the unmarked value stays seeded. |
| [FromJsonOverwrite](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.FromJsonOverwrite.html) | A boxed value starts at 17, changes to each supplied signed payload, then retains that payload through unknown-field and empty overwrites. |

The source uses the existing shared serializer. This change does not add a Serializable type marker, custom schema or normalization. Frame-rate arithmetic, formatting and scheduling are outside this payload slice. Other releases remain unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/framepayloads/run.py native --label reference-a
python3 tests/projects/framepayloads/run.py native --label reference-b
python3 tests/projects/framepayloads/run.py publish reference-a reference-b
python3 tests/projects/framepayloads/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache. Existing references and tolerances are unchanged.
