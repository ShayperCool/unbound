# SetPixelData element transfer

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 201 rows. Every value is a CPU copy read back with
`GetPixelData<byte>`, so the rows do not depend on the OS or the graphics device. The gate compares
every missing, extra or unequal row exactly.

The probe calls the managed-array and NativeArray `SetPixelData` overloads of Texture2D,
Texture2DArray, Texture3D and CubemapArray. Each call records `ok` or its exception, the log lines
it produced and the level's stored bytes.

- Order of checks: a negative start index throws before readability, readability before missing
  data (null or empty arrays and default or empty NativeArrays), then the texture's element, face and
  mip level (a mip level is checked before the size), then the size.
- Element size: a managed array's elements count as the marshaled size of the first element's type,
  not the array's own stride. A `bool[]` starting at index 2 starts at byte 8; a `char[]` counts one
  byte per element, so its level is the first half of the UTF-16 storage; a `{bool, byte}` struct
  counts eight bytes over a two-byte stride. A NativeArray counts its element type's own size.
- Element types: strings, `object` and plain classes cannot be marshaled and throw; a null first
  reference throws a null-reference error. A struct holding a string and an integer is accepted: only
  the integer half of each element is compared, since the other half holds an address.
- The size check: `(length − start) × element size` must cover the level.

Exceptions the runtime's marshaling raises are recorded by type only; Unity's own messages are
recorded in full.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [Texture2D.SetPixelData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.SetPixelData.html) | `r8.bool.start2`, `r8.char.*` and `r8.boolpair.*` distinguish the marshaled element size from the array stride; `rgba.color32.mip1` writes a smaller mip level from a start offset. |
| [Texture2DArray.SetPixelData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2DArray.SetPixelData.html), [Texture3D.SetPixelData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture3D.SetPixelData.html), [CubemapArray.SetPixelData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CubemapArray.SetPixelData.html) | The same element sizes and messages, invalid elements, faces and mip levels, and an untouched second slice and face. |

Not compared: a start index beyond the array length and a marshaled size larger than the array's
stride make the reference read memory after the array, whose contents are undefined; Unbound throws
instead. Cubemap.SetPixelData is not supported yet. Other releases are unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/pixeltransfer/run.py native --label reference-a
python3 tests/projects/pixeltransfer/run.py native --label reference-b
python3 tests/projects/pixeltransfer/run.py publish reference-a reference-b
python3 tests/projects/pixeltransfer/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
