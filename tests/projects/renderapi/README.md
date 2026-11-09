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
| [Mesh.colors](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-colors.html), [colors32](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-colors32.html) | Color arrays store four Float32 components; Color32 arrays use four bytes. Reading converts between the representations. | `colorArray.*` retains HDR floats, exposes Float32/UNorm8 layouts and exact bytes, and preserves color data across position edits. |
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
  Color[] setters retain Float32 colors and HDR values; Color32[] setters select UNorm8 storage.
- The default bone-weight/index attributes occupy a second source stream. GPU changes to
  sharedMesh positions and colors do not change the measured skinned draw on this release/backend, either
  in the same frame or the next. Its GPU index buffer is shared with skinned draws: index writes
  change both the current and following frames.
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

Additional vertex-stream controls preserve packed colors and override positions, including GPU edits to both source meshes, positionless layouts, and inherited UV inputs. The Unity 6000.0.71f1 repeats retain all earlier reference records and images unchanged.

Skinned lighting controls cover unit and nonuniform renderer scale with rotation, before/after requesting the current buffer and on the next frame. BakeMesh normals with both useScale settings remain invariant under that request.

## Volume render targets

The `volume.*` controls cover 4x2 volumes of depth 1, 4 and 8, RGBA8 and RGBAFloat storage,
individual slice clears, blits and copies, point and linear sampler3D reads, and explicit mip
attachments. `volumeMore.*` also covers spatially varying writes and repeated automatic and
explicit mip generation. A 2D target uses the same shader as a control.
`volumeCopy.*` covers whole-volume copies of every mip, region copies between slices, odd
3x5x3 mip filtering, and the device's maximum volume extent. The two native repeats also
retain every earlier record and image unchanged.

The Unity 6000.0 contracts are [volumeDepth](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture-volumeDepth.html),
[dimension](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTextureDescriptor-dimension.html),
[SetRenderTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.SetRenderTarget.html)
and [Blit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.Blit.html).
The descriptor selects a volume's extent; SetRenderTarget selects a mip and depth slice; Blit
draws with the supplied shader into its destination slice. The 6000.0.71f1 OpenGL Core captures
add device observations: supports3DRenderTextures is true, a volume with a depth buffer fails
creation, and a color volume retains its requested MSAA value in its descriptor.
The 4x2x8 render target exposes three mip levels, following its width and height. The
maximum volume extent is 16384 on this device, matching the earlier URP capability capture.

Volumes use GPU 3D textures, including single-slice volumes. Their attachment views and mip
filter bindings are prepared with the resource and reused. Replacement and release discard
the cached mip bindings. Mip generation shares the 2D/array filter path and filters all three
axes for volumes. CustomRenderTexture's volume update zones remain separately unsupported.

`volumeAuto.*` measures automatic mip visibility with both a 2D target and a volume.
Changing the desired target alone leaves the previous mips visible. A subsequent draw or
clear that prepares changed attachments publishes pending automatic updates. Repeated draws
and repeated SetRenderTarget calls using the same attachments keep the preceding mip data.
Public GenerateMips logs an error and leaves automatically generated mips unchanged.
These are 6000.0.71f1 OpenGL Core observations, selected through Compat.RenderingMeasured;
other releases retain the earlier per-pass generation until measured. Odd-sized mip levels
use the area of each source texel that overlaps the destination texel, including depth.
Descriptor convenience constructors request Texture.GenerateAllMips by default.

`volumeCompute.*` covers RGBA8, RGBAFloat and RFloat volume writes at depth 1 and 4,
explicit mip writes and sampled mip reads, integer 3D loads, odd extents and a 2D write
control. Point and linear samples use the bound texture's own sampler. Two queued writes
with different uniforms are each read into a separate buffer before any CPU readback, so
reusing one uniform buffer for both dispatches cannot pass. A 512x2-thread control confirms
1024 invocations; the recorded device limits are 1024/1024/1024/64 and 2048 array slices.
Both native repeats retain every previous record and image unchanged.
`volumeGuarantee.sampleBindingMip.*` binds a sampled texture at mip 1 after mip 0 and 1
have different contents, then reads mip 0 explicitly. It tests the [Unity 6000.0
ComputeShader.SetTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ComputeShader.SetTexture.html)
guarantee that the binding mip selects an RW image only; sampled textures keep all levels.

Writable views are cached per texture mip. Dispatches reuse uniform slots between submissions
and cache binding groups by their layout and resources. Texture and buffer replacement or
release discard groups holding the old resource. Sampler changes retire its sampled groups. Missing or incompatible non-2D integer
inputs fail with a diagnostic instead of binding a floating-point default.

The [Unity 6000.0 compute portability guidance](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/shaders/writing-custom/class-compute-shader/crossplatform)
requires bound resources, initialized contents before reads and guarded thread bounds.
The controls write every texel before loading it and initialize the atomic counter explicitly.
Its image-format qualifiers apply to read/write resources; these volume fill kernels only write.

`volumeReadback.*` compares the RGBAFloat conversion of ten byte, normalized 16-bit, half and
float formats, using both 2D and 3D targets. Missing color components become 1 in the readback,
while shader sampling keeps its own component defaults. Full RGBA targets retain their alpha.
Compat.RenderingMeasured selects this conversion for the measured release; other releases
retain the older R16/RG32-only padding rule. Alpha-only input keeps its alpha mapping.


### Compute sampler states (Q274)

The Unity 6000.0.71f1 Editor on Linux OpenGL Core distinguishes a texture-named compute
sampler from a predefined sampler. `computeSamplers.*` samples the same 2D and 3D render
textures through `sampler_Sampler2D`/`sampler_Sampler3D` and `s_linear_clamp_sampler`.
With Point/Repeat textures, named samplers use point filtering and repeat outside 0..1;
the predefined sampler still interpolates and clamps. Changing the textures to Bilinear/Clamp
makes both results equal. The earlier `volumeCompute.*.point` controls use texture-named
samplers and remain unchanged.

The [Unity 6000.0 compute sampler rules](https://docs.unity.com/en-us/engine/6000.0/manual/materials-and-shaders/shaders/writing-custom/class-compute-shader/hlsl-shaderlab)
describe texture-named samplers and predefined sampler names separately. The controls use
different filter and wrap settings so those guarantees change the recorded results. The
[general graphics sampler documentation](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-SamplerStates.html)
limits inline samplers on OpenGL; the compute result above is measured separately from
ordinary vertex/fragment programs. Compute compilation preserves the sampler bindings;
ordinary graphics programs retain their measured texture-owned sampler behavior. Shader
cache format 27 prevents reusing the earlier compute output.

The STP integration check preserves the existing URP scenario sequence and compares only
`upscale_stp`, retaining the image and the original comparator's full measurements:

```sh
python3 tests/projects/renderapi/stp.py --release 6000.0.71f1 --cli /path/to/unbound --label stp-check
```
