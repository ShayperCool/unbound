# Rendering API in Play mode

Unity 6000.0.71f1, Linux OpenGLCore, built-in pipeline. The same runtime script runs in the
Editor and Unbound. `run.py editor` records independent fresh project copies through the
GPU Editor supervisor; `publish` requires complete JSON, complete images, successful supervised
exits and byte-identical records and frames. Repeats and all run outputs stay in the validation
cache. `Reference/unity-6000.0.71f1` holds one canonical copy.

```sh
python3 tests/projects/renderapi/run.py editor --release 6000.0.71f1 --label capture
python3 tests/projects/renderapi/run.py publish --release 6000.0.71f1 --label capture
python3 tests/projects/renderapi/gate.py --release 6000.0.71f1 --cli /path/to/unbound
```

The gate runs the engine only. It keeps the full key union and strict image comparison from
`tests/projects/lines/compare.py`, including every missing, extra and failing value. Numeric
tolerance is 1e-5 absolute plus 1e-5 relative; image channels use 2/255. `comparison.json` always
retains the two Game-view gizmo differences described below; the gate permits exactly those
keys and checks the doctor limitations and unchanged frame.

## Contracts and measured answers

All links below are the official 6000.0 Scripting API. Their documented guarantees are distinct
from the answers observed in this probe. Other graphics backends and Unity releases are not
certified by this reference.

| API and documentation | Documented contract | Observed in this capture |
| --- | --- | --- |
| [Mesh.GetVertexBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetVertexBuffer.html), [GetIndexBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetIndexBuffer.html) | These expose GPU data. GPU changes do not update CPU mesh channels. CPU edits can recreate storage, requiring the caller to reacquire the buffer. The sample disposes a view after binding it to a shader. | Views are distinct objects sharing a buffer. Disposing one leaves its aliases and mesh intact. Same-size edits reuse storage; layout and size changes invalidate old views. Invalidated views retain metadata and their previous readable snapshot while a view is held. Non-readable uploaded meshes still expose buffers. Empty and invalid streams throw the recorded argument errors. Disposed metadata and data access throw. Outstanding views retain metadata after mesh destruction, but GetData leaves its destination untouched. |
| [vertexBufferTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-vertexBufferTarget.html), [indexBufferTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-indexBufferTarget.html) | Defaults are Vertex and Index. Additional targets are required for shader access. [Structured buffers](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.Target.Structured.html) require an HLSL-compatible stride; DirectX 11 forbids combining Structured with Vertex/Index. Raw access uses ByteAddressBuffer. | Assignments retain the mandatory bit. Raw/Structured index buffers pad a 16-bit odd index count to a complete 32-bit word. Counts, strides and bytes are recorded for plain and multi-stream layouts. Compute and vertex-shader read inputs remain usable after the temporary views are disposed. |
| [SortingLayer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SortingLayer.html), [onLayerAdded](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SortingLayer-onLayerAdded.html), [onLayerRemoved](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SortingLayer-onLayerRemoved.html) | Callbacks are public delegate fields. Layers come from TagManager; IDs are identifiers, not ordering values. Unknown IDs/names have the documented fallback answers. There is no runtime layer creation/removal API. | Loading an already authored three-layer project, subscribing, querying layers and creating/destroying runtime objects produces no callbacks. Layer values are relative to Default's position. This does not claim that live edits made through Editor tools cannot fire callbacks. |
| [PixelPerfectRendering](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/U2D.PixelPerfectRendering.html), [pixelSnapSpacing](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/U2D.PixelPerfectRendering-pixelSnapSpacing.html) | Sprite translations are quantized at rendering time without changing the Transform. The recommended spacing for an orthographic view is its world-space pixel size; full configuration also needs PixelPerfectCamera. | Default zero; positive, negative, NaN and infinity assignments are retained. Positive and negative finite nonzero spacing snap the sprite to the same grid. The Transform remains unchanged. Frames cover zero, 0.25, 0.5 and -0.25. NaN/infinite render positions are unmeasured. |
| [Gizmos](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Gizmos.html) | Drawing belongs in OnDrawGizmos or OnDrawGizmosSelected, called by view repaints; selected callbacks require selection. CalculateLOD is a Scene-view calculation. | Outside repaint callbacks the initial matrix is zero, color is white and exposure is null. Color uses 8-bit channels with half values rounded up. State is settable. Drawing throws the recorded argument error except DrawFrustum, which has no effect here. An odd DrawLineList throws its UnityException before the callback-context error. CalculateLOD answers NaN without a gizmo view. Camera.Render excludes gizmos. Game-view Gizmos off has no callback or cube; on invokes the callback and draws a red cube in its frame. |

The Game-view toggle is explicitly set and read back through Editor-only C# reflection in the
runner. The runtime probe never treats a missing toggle as a successful change. The runner
binds completion and toggle callbacks at EnteredPlayMode, after domain reload.

## Explicit renderer gaps

- Non-finite pixelSnapSpacing values are retained by get/set, but attempting to render a sprite
  with such spacing throws NotSupportedException. That rendering case is unmeasured.

- GPU edits to mesh buffers are unsupported. CPU SetData and buffer/counter copies into mesh
  buffers throw NotSupportedException. Writable compute or fragment bindings warn and skip the
  dispatch/draw. Use the mesh CPU setters instead. Read-only shader and compute inputs and
  GetData use the existing GraphicsBuffer renderer path.
- Unbound has no Scene/Game-view gizmo repaint pass or toggle. Gizmos state and API checks
  outside callbacks match the capture; doctor reports that enabling Game-view drawing and its OnDrawGizmos/OnDrawGizmosSelected
  callbacks is unsupported. The strict comparison retains `gizmos.game.True.callback` and
  `gizmos.game.True.pixels`. No engine image or oracle is changed to hide them.

`unbound doctor` lists these limitations. Enabling gizmo drawing and writable mesh geometry
are follow-ups, not implementations implied by this probe's passing gate.
