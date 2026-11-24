# Graphics members pipelines, packages and editor tools reach at run time

Unity 6000.0.71f1, URP 17 (the pipeline asset, renderer, scene and global settings of
tests/projects/rpglobalsettings, with the renderer in Forward+, the GPU Resident Drawer in
Instanced Drawing and the BatchRendererGroup variants kept), recorded on the OpenGL Core device
(`Reference/unity-6000.0.71f1/`) and on Vulkan (`.../vulkan/`), each from two byte-identical
windowed GPU captures. `Assets/Scripts/GraphicsGapsProbe.cs` writes `values.jsonl` on the third
frame of Play mode, section by section (a section that throws records `<section>.exception`):

* `startup.logs`, `*.logs`: the lines logged meanwhile (start-up lines once each, without
  "Unbound:" notes and the Editor's note on its own objects of a disabled built-in package);
  undefined Queue values are gathered apart (`queue.errors`, `queue.errors.unknown`).
* `context.*`: the device, pipeline and its tag, `SystemInfo.supportsSparseTextures`,
  `supportsTextureWrapMirrorOnce`, `BatchRendererGroup.BufferTarget`.
* `select.<case>.<shader>.*` (`Assets/Shaders/Sel*.shader`): `Shader.renderQueue`, `passCount`, a
  new material's queue and pass count, `GetTag` of `SubId` and `Other` with and without fallbacks,
  and the colour pass 0 of the selected SubShader draws through `CommandBuffer.DrawMesh` (red: the
  SubShader's code). Cases: the pipeline's own tag, then `Shader.globalRenderPipeline` set to
  empty, `HDRenderPipeline`, `UniversalPipeline,Foo`, `Foo, UniversalPipeline`,
  `universalpipeline`, `Foo`, `HDRenderPipeline,UniversalPipeline`; SelE's maximum LOD 200, 75, 10
  and the global one 150. The shaders' SubShaders are tagged for URP, HDRP, both, another pipeline,
  a name with a leading space, in lower case, an empty tag and none, in their own SubShaders and
  their Fallbacks'.
* `select.restricted.<shader>.*` (`Assets/Shaders/SelX`, `SelY`, `SelO`, `SelG`, `SelT`, read
  last): the same values, each SubShader's pass count and `FindPass` of a named pass, for shaders
  whose first SubShader the device may not run: its only pass excludes OpenGL Core, one of its two
  passes does, its only pass runs on Vulkan alone, a pass has a geometry stage, a pass has
  tessellation stages (each with an untagged second SubShader of another Queue and `SubId`).
* `builtin.<urp|none>.<shader>.*`: queue, pass and SubShader counts and tags of built-in and URP
  shaders under the pipeline's tag and under none.
* `queue.SelQ<n>`: `Shader.renderQueue` of Queue values: `Bogus`, `Geometry+-3`, ` Transparent`,
  `transparent`, `2000.5`, `Geometry + 1`, `Overlay+5000`, `-5`, `2450`, `Geometry-1`,
  `Geometry+0`, `GEOMETRY`, `Geometry+1 `.
* `keywords.<shader>.*`: keyword spaces of shaders whose Fallback chains add keywords
  (`Kw*.shader`) and of URP shaders, their flags, and a material's keywords.
* `sparse.*`: a 256x256 RGBA32 SparseTexture's tiles, one tile loaded, sampled by a blit.
* `anamorphism.*`, `lightmapping.*`: Camera.anamorphism set out of range and copied, its effect on
  the projection; Lightmapping.giWorkflowMode.
* `materialProperty.*`: `MaterialEditor.GetMaterialProperties` of `Probe/Props` (every property
  type and flag), with two materials, and writes through a property.
* `meshUtility.*`: MeshUtility mesh compression and `Optimize` of two meshes.
* `objectId.*`: `Camera.SubmitRenderRequest` with an `ObjectIdRequest` into targets with and
  without depth (the mapping, the pixels), a standard request and a null one.
* `layers.*`: `RenderPipelineEditorUtility` (pipeline type of an asset type, adding, renaming and
  removing rendering layer names, up to the limit and past it).
* `rebind.*`: `RenderPipelineGraphicsSettingsEditorUtility.Rebind` of URP's default volume profile
  settings.
* `cameraUtils.*`: `EditorCameraUtils` filtering results and hidden bits of two renderers, one
  hidden by `SceneVisibilityManager` (only renderer instance IDs: another ID crashes the Editor).

Measured: a SubShader the device runs none of the passes of is not among the shader's
SubShaders (OpenGL Core: `subshaderCount` 1 for SelX and SelO, whose second SubShader answers);
one that lost a pass keeps the others; the geometry and tessellation SubShaders are the ones
selected and drawn on OpenGL Core and on Vulkan. The GPU Resident Drawer logs that the platform does not support its buffer target on
OpenGL Core (constant buffers) and nothing on Vulkan (raw buffers); URP shaders have one more pass
on Vulkan than on OpenGL Core. An ObjectId request's destination reads back zero everywhere in
Play mode (with and without depth); its mapping lists the camera's renderers far to near after a
null entry.

    python3 tests/projects/graphicsgaps/run.py record --device opengl --label <L>   # twice; vulkan too
    python3 tests/projects/graphicsgaps/run.py publish [--device vulkan] <L1> <L2>
    UNBOUND=<build>/unbound python3 tests/projects/graphicsgaps/run.py gate          # both devices

Values compare exactly. Known residuals are in `residuals.json` with their cause.
