# What saving a material writes for textures it does not hold itself

What loaded materials answer for the textures of a shader whose importer gives a non-modifiable
texture and a default one, and for references to textures that do not load, and what saving them
writes. Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two identical captures):
nothing depends on the OS or the graphics device.

`Assets/Mats/Locked.shader` declares the textures `_MainTex`, `_Other` and `_Locked` (and a color);
its `.meta` gives `_Other` the default texture `Tex.png` and `_Locked` the non-modifiable texture
`Tex2.png`. The materials:

| material | its textures |
| --- | --- |
| `NoLocked` | `_MainTex` Tex, `_Missing` (no property of the shader) a GUID no asset has; no `_Locked` entry |
| `OtherLocked` | `_Locked` Tex (not the shader's), `_MainTex` none |
| `MissingProps` | `_MainTex` a GUID no asset has, `_Other` an object `Tex.png` lacks |
| `BuiltinMissing` | the Standard shader, `_MainTex` a GUID no asset has |
| `SetLocked` | `_MainTex` Tex; the probe sets `_Locked` to Tex before saving |
| `Replaced` | `_MainTex` a GUID no asset has; the probe sets it to Tex before saving |

`Assets/Scripts/MaterialSaveProbe.cs` records in Play mode into values.jsonl in `$MATERIALSAVE_OUT`
(tests/value_probe.py): `loaded/<material>/<property>` (`HasProperty`, `GetTexture`) and `/names`
(`GetTexturePropertyNames`), then every material marked dirty and `AssetDatabase.SaveAssets`:
`file/<material>` (the saved file) and `saved/*` (the same answers after the save); then a
material made with `new Material(shader)` and created as an asset (`new/made/*`, `file/New`,
`saved/New/*`).

What it shows: every material of the shader answers the non-modifiable texture for `_Locked`,
whatever its data holds or a script sets; a loaded material takes it into its data, so a save
writes it, unless a script set `_Locked`, whose texture the save writes. A reference to a texture
that does not load (a GUID no asset has, an object its file lacks), for a property of the shader or
not, reads as none and is saved as it was until a texture is set. A new material starts with the
shader's default texture and its non-modifiable one.

Record and gate:

```bash
python3 tests/projects/materialsave/run.py record --label <label>   # twice, then publish one
python3 tests/projects/materialsave/run.py gate
```
