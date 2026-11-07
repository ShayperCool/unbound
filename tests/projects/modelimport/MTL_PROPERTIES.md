# OBJ material properties, Unity 6000.0.71f1

`mtl_properties.py` records two independent GPU Editor imports in Gamma and Linear projects,
checks byte identity before publication, and gates engine captures against the new
`Reference/unity-6000.0.71f1/mtl-properties` keys and images. It never replaces the existing
material-slot or model-import references. Run with an explicit release:

```sh
python3 tests/projects/modelimport/mtl_properties.py editor --release 6000.0.71f1 --label capture
python3 tests/projects/modelimport/mtl_properties.py publish --release 6000.0.71f1 --label capture
UNBOUND=/path/to/unbound python3 tests/projects/modelimport/mtl_properties.py gate --release 6000.0.71f1 --label candidate
```

`unbound` and `gate` both run the strict full comparison. All material API values and every
image, including the original environment-lit images, must pass. Missing and extra images fail.

The API comparator uses the existing model probe's absolute/relative 1e-5 tolerance. The new
render gate uses a maximum channel error of 8/255. Every differing value and pixel remains in
`mtl-comparison.json`, including differences below the bound. Assigned Standard materials and
unlit copies of their imported colors are both rendered. A separate second pass adds direct-light
images with flat black ambient and zero reflections. Publication requires every earlier reference
file to remain byte-identical and adds only these new images. Mesh-local transforms isolate this
measurement from the open OBJ axis/pivot question. Explicit remaps retain an authored green
material instead of the imported `Mid` material.

## Contract and cause table

The following official Unity 6000.0 pages were checked, including their limitations:

| Source | Applicable guarantee or limit |
| --- | --- |
| [Materials tab](https://docs.unity3d.com/6000.0/Documentation/Manual/FBXImporter-Materials.html) | None, Standard Legacy and description import are distinct modes. Description mode delegates conversion to postprocessors. The sRGB Albedo option belongs to legacy import and is unavailable in description mode. The description documentation describes FBX and does not specify MTL keyword conversion. |
| [Model formats](https://docs.unity3d.com/6000.0/Documentation/Manual/3D-formats.html) | OBJ is supported, with FBX recommended. This does not promise universal Wavefront keyword support, transforms or material property equivalence. |
| [Standard albedo](https://docs.unity3d.com/6000.0/Documentation/Manual/StandardShaderMaterialParameterAlbedoColor.html) | Albedo controls color and transparency; color tints a texture. Rendering mode also controls whether transparency is used. |
| [Ambient light](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderSettings-ambientLight.html) and [ambient mode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderSettings-ambientMode.html) | Flat ambient uses a single color, exposed as ambientLight. The second render pass sets it to black to isolate direct lighting. |
| [Light.color](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-color.html) and [AddComponent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GameObject.AddComponent.html) | color controls emitted light; AddComponent creates the requested component. These pages do not guarantee a constructor color. The runtime default below is a release-specific observation. |
| [AddRemap](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetImporter.AddRemap.html) | A same-type external asset replaces an imported subasset after metadata is written and the model reimported. |

These are observations in this release and built-in render pipeline, not universal contracts:

| Authored control | Native observation |
| --- | --- |
| Runtime `AddComponent<Light>().color` | White, RGBA 1. Unbound previously used the warm color of an Editor-authored directional light. Correcting that runtime default removes the material-render tint; serialized authored colors continue to load from their fields. |
| Omitted `Kd`, black, white, intermediate RGB | Diffuse default 0.8; endpoints and intermediate values preserved in Gamma description import. |
| Linear project, description mode | Untextured diffuse values become gamma values in Material.color; `useSRGBMaterialColor` has no effect. |
| Legacy with sRGB Albedo enabled/disabled | Enabled preserves diffuse values; disabled converts untextured diffuse values to gamma in both project color spaces. Textured diffuse values remain unchanged. |
| `d 0`, `d 0.25`, `d 1` | Alpha 1, 0.25, 1 respectively. Zero dissolve is opaque through the measured FBX transparency-factor conversion. |
| `Tr 0.25`; `d 0.3` before/after `Tr 0.8` | Alpha 0.75; alpha 0.3 in both orders. `d` wins. |
| One `map_Kd` in the library | Unity attaches that diffuse texture across the library's used materials. Description mode uses white RGB with imported alpha; legacy retains `Kd`. The untextured counterpart distinguishes this from an RGB conversion error. |
| `Ks`, `Ns` control | Standard metallic and glossiness stay at zero for this conversion path. No broad specular/PBR/illumination conversion is claimed. |
| Explicit remap | Authored green material wins in both library variants. |

The implementation converts OBJ properties into the existing FBX-style material description,
then uses the existing managed conversion and remap paths. MTL file bytes are cache dependencies;
model cache format 22 invalidates old descriptions. No mesh indices, slot ordering, mesh naming,
axis/pivot behavior or FBX geometry conversion changes are intended. Multiple competing diffuse maps fail explicitly when an imported texture would be consumed;
None mode and complete explicit remaps keep importing geometry. Their conversion remains
unmeasured. Other releases,
broad PBR, normal maps and `illum` remain unmeasured follow-ups.

## Validation

Before the change, each color space matched 893/1351 values and 3/30 images exactly. After the
change, each matches 1351/1351 values and 20/30 images exactly; all 30 images pass the unchanged
8/255 bound. Textured Standard images retain maximum errors of 2/255 in Gamma and 1/255 in
Linear. All untextured and unlit images are exact. The complete captures and reports stay under
Library/Unbound or the validation cache, including the unsuccessful earlier candidates.

The existing material-slot probe retains 306/306 exact API values and 20/20 exact images. The
model import API/description residuals, lightmap UV and normal residuals, humanoid and baked
images do not worsen. The runtime Light correction also changes blend-shape image worst mean
from 1.70 to 0.00. Additional builtinrp, ambient and indirectspec comparisons do not worsen.
