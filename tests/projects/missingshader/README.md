# Materials whose shader is missing

What a material asset reports when its shader reference names no shader that loads, recorded by
`Assets/Scripts/MissingShaderProbe.cs` in Play mode into values.jsonl in `$MISSINGSHADER_OUT`
(tests/value_probe.py). The values depend on neither the OS nor the graphics device (macOS batch
captures are the reference; `Shader.isSupported`, which the graphics device answers, is not recorded).

`Assets/Materials` holds one material per kind of reference, each with the same saved data (keywords
`_KWA` valid and `_KWB` invalid, `_Color`, `_MainTex` scale and offset, `_Glossiness`, `_Probe`, which
no shader declares, instancing on):

| material | `m_Shader` |
| --- | --- |
| `Present` | `Assets/Shaders/ProbeShader.shader` (`Probe/Transparent`, queue Transparent), the control |
| `NoProperties` | `Assets/Shaders/NoProperties.shader` (`Probe/NoProperties`: no properties), the control of a shader that declares nothing |
| `DanglingGuid` | a GUID of no asset, file id 4800000 |
| `DanglingGraph` | a GUID of no asset with a shader graph's sub-asset file id |
| `DanglingQueue` | a GUID of no asset, with a custom render queue 2450 |
| `NullShader` | `{fileID: 0}` |
| `WrongFileId` | the project shader's GUID with a file id it has no object for |
| `BuiltinUnknown` | the built-in resources' GUID with a file id of no shader |

Rows:

* `load/<material>`, `material/<material>/*`: the shader (name, whether it is the object
  `Shader.Find("Hidden/InternalErrorShader")` returns, its passes, properties and queue), the
  render queue, passes, keywords, `HasProperty`, typed getters (with the errors they log), texture
  scale and offset, property names, a tag and the instancing and GI flags.
* `error_shader/find_same`: two `Shader.Find` calls give one object.
* `copy/<material>/*`: the same of `new Material(source)`.
* `assign/null`: a new Unlit/Color material given `shader = null`; `assign/unlit`, `assign/error`:
  a copy of `DanglingQueue` given Unlit/Color, then the error shader (queue reset).
* `scripted/<shader>/before`, `/after`: a new material of the error shader, `Probe/NoProperties` and
  `Probe/Transparent`: `GetFloat("_New")` before and after `SetFloat`, `HasProperty`, a colour set
  for a name the shader lacks, the float property names.
* `save/<material>`, `/file`: a copy of the material (`AssetDatabase.CopyAsset` into `Assets/Saved`)
  given `_Probe` 2 and a `_Color`, marked dirty and saved with `SaveAssetIfDirty`: what it reports
  after the set and after the save, and the file written.

What it shows (Unity 6000.0.71f1): every reference of no shader gives the material the error
shader (`Hidden/InternalErrorShader`, the same object `Shader.Find` returns: 3 passes, no properties,
queue 2000); the material keeps its custom render queue, keywords, flags and saved texture scale and
offset; getters of its saved names return 0 and log nothing under the error shader (a shader without
properties logs the missing-property error). A save writes the reference as it was read, with every
keyword invalid. `shader = null` gives the error shader; a later shader change resets the queue.

Not covered: a reference to an object that is not a shader (the Editor logs `PPtr cast failed when
dereferencing!` wherever the material reads its shader), a shader imported after the material was
loaded, and the reference a save writes after `shader = null` or an explicit assignment of the error
shader.

Record and gate:

    python3 tests/projects/missingshader/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/missingshader/run.py publish A B
    python3 tests/projects/missingshader/run.py gate

`residuals.json` names the open differences (Material property names and saves, a follow-up).
