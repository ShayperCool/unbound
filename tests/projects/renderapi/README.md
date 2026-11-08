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
requires every key and frame to match, including the Game-view gizmo frames. Doctor must not
retain the former mesh-write or non-finite sprite-rendering limitations.

## Contracts and measured answers

All links below are the official 6000.0 Scripting API. Their documented guarantees are distinct
from the answers observed in this probe. Other graphics backends and Unity releases are not
certified by this reference.

| API and documentation | Documented contract | Observed in this capture |
| --- | --- | --- |
| [Mesh.GetVertexBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetVertexBuffer.html), [GetIndexBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetIndexBuffer.html) | These expose GPU data. GPU changes do not update CPU mesh channels. CPU edits can recreate storage, requiring the caller to reacquire the buffer. The sample disposes a view after binding it to a shader. | Views are distinct objects sharing a buffer. Disposing one leaves its aliases and mesh intact. Same-size edits reuse storage; layout and size changes invalidate old views. Invalidated views retain metadata and their previous readable snapshot while a view is held. Non-readable uploaded meshes still expose buffers. Empty and invalid streams throw the recorded argument errors. Disposed metadata and data access throw. Outstanding views retain metadata after mesh destruction, but GetData leaves its destination untouched. |
| [vertexBufferTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-vertexBufferTarget.html), [indexBufferTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-indexBufferTarget.html) | Defaults are Vertex and Index. Additional targets are required for shader access. [Structured buffers](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.Target.Structured.html) require an HLSL-compatible stride; DirectX 11 forbids combining Structured with Vertex/Index. Raw access uses ByteAddressBuffer. | Assignments retain the mandatory bit. Raw/Structured index buffers pad a 16-bit odd index count to a complete 32-bit word. Counts, strides and bytes are recorded for plain and multi-stream layouts, including exact Float32 HDR colors, UInt32 attributes, partial byte writes and unrelated CPU edits. Compute and vertex-shader read inputs remain usable after the temporary views are disposed. Recorded CopyBuffer commands also retain their source storage when the mesh remains alive. |
| [SortingLayer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SortingLayer.html), [onLayerAdded](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SortingLayer-onLayerAdded.html), [onLayerRemoved](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SortingLayer-onLayerRemoved.html) | Callbacks are public delegate fields. Layers come from TagManager; IDs are identifiers, not ordering values. Unknown IDs/names have the documented fallback answers. There is no runtime layer creation/removal API. | Loading an already authored three-layer project, subscribing, querying layers and creating/destroying runtime objects produces no callbacks. Layer values are relative to Default's position. This does not claim that live edits made through Editor tools cannot fire callbacks. |
| [PixelPerfectRendering](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/U2D.PixelPerfectRendering.html), [pixelSnapSpacing](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/U2D.PixelPerfectRendering-pixelSnapSpacing.html) | Sprite translations are quantized at rendering time without changing the Transform. The recommended spacing for an orthographic view is its world-space pixel size; full configuration also needs PixelPerfectCamera. | Default zero; positive, negative, NaN and infinity assignments are retained. Positive and negative finite nonzero spacing snap the sprite to the same grid. The Transform remains unchanged. Frames cover zero, 0.25, 0.5 and -0.25. NaN and both infinities suppress the sprite while leaving its Transform unchanged. |
| [Gizmos](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Gizmos.html) | Drawing belongs in OnDrawGizmos or OnDrawGizmosSelected, called by view repaints; selected callbacks require selection. CalculateLOD is a Scene-view calculation. | Outside repaint callbacks the initial matrix is zero, color is white and exposure is null. Color uses 8-bit channels with half values rounded up. State is settable. Drawing throws the recorded argument error except DrawFrustum, which has no effect here. An odd DrawLineList throws its UnityException before the callback-context error. CalculateLOD answers NaN without a gizmo view. Camera.Render excludes gizmos. Game-view Gizmos off has no callback or cube; on invokes the callback and draws a red cube in its frame. |

The Game-view toggle is explicitly set and read back through Editor-only C# reflection in the
runner. The runtime probe never treats a missing toggle as a successful change. The runner
binds completion and toggle callbacks at EnteredPlayMode, after domain reload.

## GPU writes and ordering

The `followup.*`, `extended.*`, `packed.*` and `hdr.*` controls extend the same Play-mode
capture. Two fresh GPU Editor runs matched before each prefix was published. The append
publisher verifies all earlier records and frames byte for byte, then adds only the new prefix.
It never changes the comparator's tolerances.

- Raw and Structured vertex-buffer writes move the mesh's next draw, including non-readable
  uploaded meshes. GPU vertex and index changes leave CPU mesh channels unchanged.
- UploadMeshData without a CPU edit retains GPU data. SetVertices restores vertex data while
  retaining GPU-written indices; changing the vertex layout invalidates the old vertex buffer.
- SetData, Graphics.CopyBuffer and recorded SetBufferData commands change the drawing copy.
  A dispatch followed by DrawMesh in the same CommandBuffer needs no readback between them.
- Four-stream controls cover half-float positions and UVs, normalized colors and extra UVs,
  16-bit GPU-written indices, quads and CPU vertex edits after GPU writes to other streams. A vertex edit refreshes every vertex
  stream from CPU data, while index data remains separate.
  HDR controls preserve floating-point vertex colors through both CPU uploads and GPU writes.
- The default bone-weight/index attributes occupy a second source stream. GPU changes to
  sharedMesh positions do not change the measured skinned draw on this release/backend, either
  in the same frame or the next.
- SkinnedMeshRenderer.GetVertexBuffer and vertexBufferTarget are present in 6000.0.71f1.
  The current-frame buffer exposes renderer-local deformed positions. Its writes affect the
  current frame and the next skinning update replaces them. The transformed-renderer control
  distinguishes these positions from world-space positions.

Quad index-buffer controls cover 16/32-bit layouts with and without Raw access and mixed
triangle/quad submeshes. The buffer holds the logical indices followed by each quad range's
expanded triangles. GPU edits to the logical prefix leave the draw unchanged; edits to the
appended range change it. An explicit upload alone retains those edits, while a CPU index edit
rebuilds both parts. Alignment padding follows the appended ranges.

Unused alignment padding in the original 16-bit index controls is explicitly initialized before
readback. Its old allocation contents are unspecified; initialization preserves all existing
oracle bytes and avoids depending on allocator contents.

The implementation shares GraphicsBuffer storage and the mesh-data packer. Mesh layouts cache
conversion pipelines and bindings; buffer revisions trigger conversion before the next draw.
Unchanged draws allocate no conversion storage and create no pipelines or GPU buffers.

The relevant skinned-buffer contracts are [GetVertexBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer.GetVertexBuffer.html)
and [vertexBufferTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SkinnedMeshRenderer-vertexBufferTarget.html).
The current buffer may alternate with the previous frame's buffer when motion-vector rendering
uses it. GetPreviousVertexBuffer and buffer access with active Cloth remain explicitly unsupported
and are listed by doctor.

## Game-view gizmos

The `followup.gizmos.*` frames independently toggle Gizmos and selection. With the toggle off,
neither repaint callback runs. With it on, OnDrawGizmos runs and draws a solid cube, a transformed
wire cube, a sphere and a line. Selecting the component's GameObject additionally invokes
OnDrawGizmosSelected and draws its magenta cube. Camera.Render excludes gizmos. A further control covers a disabled script drawing a rotated, nonuniformly scaled sphere,
with the camera using either LDR or HDR. Sphere geometry is an 80-triangle icosphere. The runtime
uses the same Game-view toggle and Selection state as Editor scripts.

Game-view drawing reuses the release's published `Hidden/Editor Gizmo` and `Hidden/Editor Gizmo Lit`
shader sources. Icon, GUI texture and frustum drawing remain explicit unsupported operations in
callbacks; doctor lists them. No Scene-view renderer is provided.
