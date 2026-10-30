# Built-in render pipeline probe

What the built-in pipeline's camera does beyond lighting (`Assets/Scripts/BuiltinProbe.cs`, linear
color space, the Ultra quality level: MSAA 2, soft particles on; the project's GraphicsSettings sort
transparents along a custom axis, (0, 1, 0)):

* **Camera events**: a command buffer at every `CameraEvent` copies a clock and advances it, and
  samples `_CameraDepthTexture`, `_CameraDepthNormalsTexture` and `_CameraMotionVectorsTexture` at
  the middle of the view; `ClockMessages` does the same in `OnPreRender`, `OnRenderObject` and
  `OnPostRender`. So `events.<case>.order` lists the events that fired in their order (and
  `orderWithMessages` the messages among them), per case: nothing asked, each
  `depthTextureMode`, a shadowed sun, image effects ([ImageEffectOpaque] or not), a skybox, a
  skybox clear without a skybox material, soft particles.
* **Camera textures** shown by a command buffer after everything: the depth texture (`depth`),
  depth and normals (`depthnormals`: a transparent sphere, a cutout quad and a shader without a
  RenderType or shadow caster are left out or cut), the depth a shadowed sun makes, motion vectors
  over three frames rendered with `Camera.Render` and five frames the player loop renders
  (`motion_*`, raw values in `motion*.samples`).
* **Matrices**: `nonJitteredProjectionMatrix` against `projectionMatrix` when either is set or reset,
  `previousViewProjectionMatrix` before and after renders.
* **[ImageEffectOpaque]** (`effect_*`), **disabled shader passes** (`disabled_passes`, `passes.*`:
  ShadowCaster disabled from script and in a material asset, ForwardBase, Always), **transparency
  sorting** (`sort_*`: the project's axis, a camera's mode and axis, the project's setting changed
  from script, sprite sort points, renderer priority), **render texture assets** (`rt.*`, `rt_asset`:
  a camera rendering into one), **per-material property blocks** (`property_blocks`, `blocks.*`),
  **Graphics.DrawMesh** lighting and shadows (`draw_mesh`), the **main light**'s culling mask and
  cookie (`main_light_*`), **SubShader LODs** (`lod.*`).

`ProbeRunner.Create` made the scene and the assets in `Assets/Resources` (the Editor wrote them).

Record Unity's reference (the Editor needs a display; it renders on the GPU):

    BUILTIN_PROBE_OUT=$PWD/tests/projects/builtinrp/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/builtinrp \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/builtinrp.log

Run Unbound (in a window at the batch-mode Editor's screen size: a camera's first previous
view-projection follows the screen it was made on) and compare:

    BUILTIN_PROBE_OUT=/tmp/builtinrp unbound run -p tests/projects/builtinrp --frames 30 --size 640x480 --no-watch
    python3 tests/projects/builtinrp/compare.py tests/projects/builtinrp/Reference /tmp/builtinrp

## What the Editor does (6000.0.71f1, Linux)

* Events: OnPreRender, Before/AfterDepthTexture (Depth or MotionVectors asked, or a shadowed
  directional light), Before/AfterDepthNormalsTexture, Before/AfterForwardOpaque,
  Before/AfterSkybox (only when a skybox material is drawn), the motion vectors, Before/After
  ImageEffectsOpaque (always), Before/AfterForwardAlpha, OnPostRender, Before/After
  HaloAndLensFlares (always), OnRenderObject, Before/AfterImageEffects, AfterEverything. No
  GBuffer, Lighting, Reflections or FinalPass events in forward; soft particles alone ask for no
  depth texture. After a camera its depth textures are unbound (the next one reads gray), its
  motion vectors stay.
* Depth normals: Hidden/Internal-DepthNormalsTexture by RenderType, cleared to (0.5, 0.5, 1, 1),
  point sampled. Motion vectors: the camera's motion from the depth texture against its previous
  view-projection, then the objects that moved, tested against the depth.
* `previousViewProjectionMatrix` is the view-projection of the camera's last player-loop render
  (OpenGL clip space); a new camera starts with its own at creation (with the screen's aspect and
  the default field of view); `Camera.Render` changes nothing, nor the objects' previous matrices.
  `nonJitteredProjectionMatrix` follows the projection until it is set; then it stays, even across
  `ResetProjectionMatrix`. `useJitteredProjectionMatrixForTransparentRendering` is true.
* [ImageEffectOpaque] effects run on the opaque image; transparents draw over their output.
* Disabled passes: ForwardBase and Always are skipped; a disabled ShadowCaster still casts.
* A new camera takes the project's transparency sort mode and axis; renderer priority does not
  sort built-in draws; a sprite's sort point Pivot sorts it at its transform.
* Render textures: a new one clamps, cube and depth-only ones report no anisotropy, a depth-only one
  is Depth or Shadowmap by its sampling mode, a descriptor's sampling mode starts at None.
* A per-material property block replaces the renderer's for that material (they do not combine).
* Graphics.DrawMesh draws are lit by the forward lights, cast and receive shadows as their
  arguments say; ShadowsOnly ones only cast.
* The base pass's light is the brightest directional light without a cookie whose culling mask has
  the object's layer; a directional light with a cookie draws in ForwardAdd.
* SubShader LOD: `maximumLOD` (-1 follows `globalMaximumLOD`, int.MaxValue) picks the first
  SubShader whose LOD is at most it (none: LOD 0), else the fallback's; with none, nothing draws.

## What differs

* Objects made and moved in the frame of their first motion vector render: the Editor draws the
  cubes as moving from where they were made and the spheres and plane as still; Unbound takes all
  of them to be still then (`compare.py` lists these as expected). Later frames match.
* `draw_mesh`: the faces of the instanced cubes the red point light reaches (mean 1.5).
