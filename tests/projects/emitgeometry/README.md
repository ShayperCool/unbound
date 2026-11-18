# UI geometry a scriptable pipeline draws, and the target SetupCameraProperties binds

`ScriptableRenderContext.EmitGeometryForCamera` "emits UI geometry for rendering for the
specified camera" and `EmitWorldGeometryForSceneView` "emits UI geometry into the Scene view for
rendering" (Scripting API, 6000.0:
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ScriptableRenderContext.EmitGeometryForCamera.html,
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ScriptableRenderContext.EmitWorldGeometryForSceneView.html).
URP calls the first for reflection and preview cameras and the second for the Scene view, before
culling. Neither page says how often the geometry is drawn nor what camera types draw without
it; `SetupCameraProperties` "sets up view, projection and clipping planes global shader
variables"
(https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ScriptableRenderContext.SetupCameraProperties.html)
and says nothing of the render target it leaves bound. This probe measures both.

`EmitGeometryProbe` (Linear color space, batch mode) makes a pipeline (`ProbePipelineAsset`)
that, per camera, emits as the probe asks, culls, sets the camera up, clears to black and draws
every renderer's untagged passes sorted as transparent. A disabled camera renders into a 64x64
texture a world space canvas's half-transparent red quad (left) and its own screen space camera
canvas's half-transparent green quad (right), as each camera type (Game, Reflection, Preview,
SceneView), with no emission (`none`), `EmitGeometryForCamera` (`emit`),
`EmitWorldGeometryForSceneView` (`sceneView`), both (`both`), then none again (`noneAfter`:
whether an emission lasts); then a Reflection camera that emits but whose culling mask leaves out
the canvases' layer (`emitMasked`); then, as a Game camera, the pipeline setting no target before
the clear (`target.none`) or another texture before SetupCameraProperties and none after it
(`target.otherBeforeSetup`), both textures filled with blue first (`.otherWorld`: the other
texture). Each render records the bytes at both quads' centres and what was logged (a first
render that makes the pipeline is not recorded: the Editor warns there that the probe's asset
declares no pipeline type or shader tag).

Measured in the Unity 6000.0.71f1 Editor (Linux, OpenGL Core, NVIDIA driver; two identical
captures):

- Game, Reflection and SceneView cameras draw both canvases once without emission (188, a half
  opaque quad over black in linear space); a Preview camera draws neither.
- Each call of either method draws the camera's canvases once more (225 after one, 241 after
  both), the world space canvas and the camera's own screen space canvas alike, for every camera
  type; nothing carries over to the next render; the emitted copies obey the culling mask.
- SetupCameraProperties binds the camera's target: the draws after it go into the camera's
  texture, also when the pipeline had set another one before it (left blue).
- Nothing is logged.

Values compare within 2 (bytes of blended draws). Unmeasured: emitting after culling or for a
camera that culls twice in one render (Unbound gives the emission to the camera's next culling
only), the order of the copies of one canvas among its elements, the VR camera type, other
devices, and whether SetupCameraProperties also sets the viewport (Unbound covers the whole
target, as SetRenderTarget does).
