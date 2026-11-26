# Names a material is given from script

Which property names a material lists and has once a script sets values for names its shader does
not declare, recorded by `Assets/Scripts/MaterialNamesProbe.cs` in Play mode into values.jsonl in
`$MATERIALNAMES_OUT` (tests/value_probe.py). The values depend on neither the OS nor the graphics
device (macOS batch captures are the reference).

`Probe/Names` declares `_Color`, `_MainTex`, `_Glossiness` (Range) and `_Count` (Integer) in that
order; `Probe/Other` declares `_Saved`, `_Fresh`, `_FreshColor` and `_FreshTex`; `Probe/Kinds` declares
`_Saved` as a colour.
`Assets/Materials/Base.mat` is a `Probe/Names` material that also saves `_Saved: 7`.

Rows:

* `order/*`: a new material given `_Post` then `_Pre` (whose id was taken first), colours alike, and
  names the built-in shaders use (`_Stencil`, `_Cutoff`, `_TintColor`); which ids come first
  (comparisons only, never the ids).
* `fresh/*`, `set/*`: a new material before and after values of every kind are set from script, in an
  order that is neither by name nor by kind: floats (one declared, one set twice), an integer, a
  legacy int, a colour, a vector, textures (one null), a matrix, a float, vector and matrix array, one
  name set as a float and as a colour, and a texture scale and a texture offset for names never given
  a texture:
  `GetPropertyNames` of every kind, `GetTexturePropertyNames`, the `Has*` answers and the getters.
* `copy/*`, `instantiate/*`, `renderer_material/*`, `copy_properties/*`: `new Material(source)`,
  `Object.Instantiate`, a renderer's own material (`renderer.material`) and
  `CopyPropertiesFromMaterial` of that material.
* `switched/*`, `switched_back/*`: a copy given `Probe/Other`, then `Probe/Names` again.
* `declared_float_texture`: a texture set for `_Glossiness`, which the shader declares as a float.
* `serialized/*`: a material given a matrix, a float array and a float from script, then a queue
  through a SerializedObject (`ApplyModifiedPropertiesWithoutUndo`).
* `save_if_dirty/*`, `save_assets/*`, `not_dirty/*`: a copy of `Base.mat` (`AssetDatabase.CopyAsset`)
  given `_Saved`, `_Fresh`, `_FreshColor`, `_FreshTex`, `_FreshM`, `_FreshA` (an array) and
  `_Glossiness`, saved with `SaveAssetIfDirty` after `SetDirty`, with `SaveAssets`, and with
  `SaveAssetIfDirty` alone (a set dirties the asset): what it has before and after, the file written,
  and the values under `Probe/Other` and back.
* `kinds/*`: a copy of `Base.mat` given a colour for `_Saved` (which it saves as a float), saved:
  the file, and `_Saved` under `Probe/Kinds` and `Probe/Other`.
* `create/*`: a new material given those values and made an asset with `CreateAsset`, then given
  `_Later` and saved again.

What it shows (Unity 6000.0.71f1): `GetPropertyNames` lists a kind's names by property id: the
shader's (whose names get ids in declaration order when it loads, a texture's `_ST`, `_TexelSize`
and `_HDR` with it), then the names set from script, a texture with its three vectors; an array set
from script is a property of its element's kind whose getter reads its first element; a texture scale
or offset set for a name makes it a texture property. `GetTexture` of a texture set from script for a
name the shader does not declare as a texture (a float's name too) and the material's data does not
list logs the missing property and returns null, though `HasTexture` is true and the scale and offset
read back, and `GetTexturePropertyNames` leaves it out. A copy (`new Material`, `Instantiate`) and
every save drop the names set from script for which the shader has no property, matrices and arrays
too: `Has*` false, getters log and return defaults, and under a shader declaring them they read its
defaults, except a value the material's data lists as that kind (`_Saved` as a float, not a colour
set for it), which the file keeps and that shader reads. A SerializedObject apply drops matrices and
arrays as well. `renderer.material` and `CopyPropertiesFromMaterial` keep them. The names Unity's
built-in shaders use have ids below a project's (`order.early_ids`), so `_MainTex`'s vectors come
before a `_Color` declared first.

Not covered: material variants, buffers, a material saved inside another asset.

Record and gate:

    python3 tests/projects/materialnames/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/materialnames/run.py publish A B
    python3 tests/projects/materialnames/run.py gate

`residuals.json` names the open difference (the built-in names' property ids, a follow-up).
