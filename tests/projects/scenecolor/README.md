# Scene Color and Scene Depth under the Universal Render Pipeline

Unity 6000.0.71f1, Linux Editor (OpenGL Core, NVIDIA driver), windowed; two identical captures.
The project is `tests/projects/missingmaterial`'s (URP 17, Linear color space, a Forward
renderer; its scene's own objects are turned off). `SceneColorProbe` builds a lit ground with
red balls and green posts and stripe on it, and three transparent surfaces a little above it
(`Probe/SceneColorWater`, the way water graphs read what is behind them), and renders them into
a 320x180 render texture:

- left: the camera's opaque texture (Scene Color), displaced by a ripple, tinted;
- middle: white fading to a water colour with how far behind the surface the opaque one lies
  (Scene Depth: the camera's depth texture against the fragment's view depth);
- right: both, the scene color fading to the water colour with that depth.

Scenarios (each image `<name>.png`; `values.jsonl` keeps whether the camera asked for the
opaque and depth textures):

| Scenario | Rendering | Depth priming | Depth copied | Other |
| --- | --- | --- | --- | --- |
| `after_transparents` | Forward | off | after transparents (the renderer's default) | |
| `after_opaques` | Forward | off | after opaques | |
| `forward_plus_primed` | Forward+ | forced | after transparents | |
| `after_transparents_ssao` | Forward | off | after transparents | screen space ambient occlusion (depth and normals source) |
| `forward_plus_primed_ssao` | Forward+ | forced | after transparents | screen space ambient occlusion |
| `none` | Forward | off | after opaques | neither texture asked for |
| `terrain_after_opaques` | Forward | off | after opaques | a terrain of URP's terrain shader for ground |
| `terrain_forward_plus_primed_ssao` | Forward+ | forced | after transparents | the terrain; ambient occlusion |
| `terrain_instanced_*` | as the two above | | | the terrain drawn instanced, per-pixel normals |
| `scaled_after_opaques` | Forward | off | after opaques | render scale 0.8 |
| `scaled_forward_plus_primed_ssao` | Forward+ | forced | after transparents | render scale 0.8; ambient occlusion |
| `scaled_terrain_instanced_forward_plus_primed_ssao` | Forward+ | forced | after transparents | render scale 0.8; instanced terrain; ambient occlusion |

Measured: the opaque texture holds the opaque objects in every scenario that asks for it. The
depth texture a transparent object reads holds the opaque objects when it is copied after the
opaque ones, or when screen space ambient occlusion's prepass of depth and normals draws it
(Forward and Forward+ alike); copied after the transparent objects, with or without depth
priming, it holds nothing yet when they draw (the middle surface stays white). With neither
texture asked for (`none`), the scene color is black and the depth gives no fade. A terrain is in
both, drawn as patches or instanced, and a render scale changes neither. Unbound matches each
image to 1/255 but for the instanced terrain's own shading (up to 2/255, 3/255 after the render
scale's upscale; `image_limits.json`).

| Unity 6000.0 documentation | Control |
| --- | --- |
| [Universal Renderer: Depth Texture Mode](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/urp-universal-renderer.html) says "After Transparents" copies the depth after the transparent objects; [Scene Depth node](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Scene-Depth-Node.html) and [Scene Color node](https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/Scene-Color-Node.html) name the camera's depth and opaque textures. | `none` (no textures) against the others; the plane against the terrain. |

Not measured: MSAA, opaque texture downsampling other than the asset's (2x bilinear), deferred
rendering, camera stacking, XR, other devices and releases.

    python3 tests/projects/scenecolor/run.py record --label A    # and B; then publish A B
    python3 tests/projects/scenecolor/run.py gate

Captures and engine outputs stay in the run cache.
