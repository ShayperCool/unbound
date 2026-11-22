# Renderers whose material is missing

Unity 6000.0.71f1, Linux Editor (OpenGL Core, NVIDIA driver), windowed; two identical captures.
The project is `tests/projects/stpmotion`'s (URP 17, Linear color space, a Forward renderer; its
scene's own objects are turned off). `MissingMaterialProbe` builds the objects, renders them
into a 320x180 render texture under the Universal Render Pipeline and then, with the pipeline
assets taken off (`GraphicsSettings.defaultRenderPipeline` and `QualitySettings.renderPipeline`
null), under the built-in pipeline, and writes each image:

- `Missing`: a cube whose `sharedMaterial` is null, with a red ball half behind it;
- `Halves`: a mesh of two sub-meshes with the materials `{ red, null }`;
- `NoSlots`: a cube with no material slot at all;
- `Named`: a cube whose material names `Hidden/InternalErrorShader`, as a control;
- `Sprite`, `Skinned`, `Line`, `Text`: a SpriteRenderer, a SkinnedMeshRenderer, a LineRenderer
  and a TextMesh's MeshRenderer whose material is null;
- `Extra`: a cube of one sub-mesh with the materials `{ translucent red, null }`;
- `Destroyed`: a cube whose material was destroyed;
- a lit ground under a directional light.

`*_scene` is the camera's image; `*_depth` adds a quad over the whole view that draws the
camera's depth texture (URP: copied after the opaque objects).

Measured, the same under both pipelines: a slot without a material draws in the error shader's
magenta, opaque and writing depth (the ball is hidden behind the cube), for mesh, skinned mesh,
sprite and line renderers and a TextMesh's text alike, sub-mesh by sub-mesh; so does a slot whose
material was destroyed, and a slot beyond the mesh's sub-meshes, which draws the last sub-mesh
again (`Extra` is the red over the magenta); a renderer with no slot draws nothing.
URP's depth texture, copied after the opaque objects, holds them; the built-in pipeline's, drawn
from the shaders' shadow caster passes, holds neither them nor the unlit ball.

| Unity 6000.0 documentation | Control |
| --- | --- |
| [Renderer.sharedMaterial](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-sharedMaterial.html) and [Material](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Material.html) do not say what a renderer without a material draws; the Manual's [shader loading](https://docs.unity3d.com/6000.0/Documentation/Manual/shader-loading.html) page names the magenta error shader for shaders that cannot draw. | `Missing`, `Halves`, `Sprite`, `Skinned`, `Line` against `Named` and `NoSlots`. |

Not measured: shadows (this project's quality settings draw none), trail, particle (see the
particle probes), terrain, tilemap and effect renderers, which of two opaque draws at one depth
comes first (a slot beyond the sub-meshes over an opaque first material), `Graphics.DrawMesh` and command buffer draws with a null material, other devices and releases.

    python3 tests/projects/missingmaterial/run.py record --label A    # and B; then publish A B
    python3 tests/projects/missingmaterial/run.py gate

Captures and engine outputs stay in the run cache.
