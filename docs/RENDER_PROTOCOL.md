# Render protocol (managed engine → native renderer)

The managed engine decides *what* is drawn and *in which order*, with Unity's rules (cameras by
depth, render queues, sorting layers and orders, distance sorting of transparent objects, canvases
after cameras, command buffers, blits). Everything that renders becomes a **command stream** the
native renderer (wgpu, `crates/unbound-app`) executes in order, with each material's shader compiled
from its Unity source. Every structure below is `#[repr(C)]`
in Rust (`crates/unbound-runtime/src/render_protocol.rs`) and `LayoutKind.Sequential` in C#
(`managed/UnityEngine/Graphics/RenderProtocol.cs`); both sides must change together, and
`NativeApi.Version` is bumped when they do.

## Handles

Meshes, textures and materials are identified by `u64` handles the managed side allocates
(never 0). The managed side uploads an object's data when it changes and releases it when the object
is destroyed. Imported textures are uploaded by the native side itself (`import_texture`), which
knows the asset pipeline and its cache.

### Mesh instances under an instance multiplier (NativeApi 65)

Opcode 30 `MeshInstance` carries `u32 index, u32 count`: the call's instance count follows the
index. A mesh instance's draw keeps one instance at its index whatever the multiplier; after
the draw of instance `count - 1` the renderer adds, under a multiplier m above one, the draws
of IDs `count` to `count · m − 1` with zero matrices and zero instanced properties (scalar
uniforms the variant declares instanced and those the draw gives as arrays), as the Unity
6000.0.71f1 Editor draws a mesh-instancing call's multiplied instances where no earlier
draw wrote their instance data (tests/projects/cmdbuffers `device.multiplier.placed.*`,
`device.multiplier.camera.*`); where earlier draws wrote it, the Editor's read what they left
(`device.multiplier.stale.*`, warned of, not reproduced). The added draws keep the call's last
instance's winding. Standalone instancing whose multiplied IDs pass a native batch does not
add them (warned, unmeasured).

### BatchRendererGroup draws (NativeApi 60)

`DotsDraw` (opcode 36: `u32 header[8]`, `u32 window_offset`, `u32 window_size`, `u32 count`,
`u32 visible[count]`) makes the next `Draw` a BatchRendererGroup draw command's: its material's
`DOTS_INSTANCING_ON` variant, `count` instances (at most 256, the length of the
`unity_DOTSVisibleInstances` array DOTS instancing shaders declare), instance `i` reading batch
index `visible[i]`. The renderer writes the program's DOTS instancing data
(`render/dots.rs`): each `unity_DOTSInstancing<type><bytes>_Metadata<name>` uniform from the
draw's `Int` property `<DOTS><name>` (zero without one), the visible instances' `x`, the header
in the `z` and `w` of the first four elements (rendering layer mask, flags, the mesh's bounds
centre and extent), and in uniform buffer mode the cbuffer `unity_DOTSInstanceData` from
`window_offset` of the buffer the draw's `unity_DOTSInstanceData` property names, `window_size`
bytes of it (0: as much as the cbuffer holds). On the other devices that property binds the
byte address buffer of the name. Header flag 1 (flip winding) mirrors the draw's culling.

### Screen stand-in (NativeApi 50)

`ScreenStandIn` (opcode 35: `u64 texture`, 0 for none) names the render texture that stands for
the screen while a built-in pipeline screen camera with command buffers draws into it. The
renderer stores it as the screen is: drawn with the projection unflipped and, where the device
stores the screen top-down (Vulkan), its rows top-down, so copies, readbacks and blits of it read
the screen's rows as stored (tests/projects/rtdepth `vulkan`).

### Render protocol 15: native plugin calls (NativeApi 41)

`Plugin` (opcode 34: `u8 kind`, `u64 callback`, then the kind's fields) runs a native plugin's
rendering callback when everything before it has run on the GPU, before what follows it, on the
caller's thread, with the current target bound in the plugins' OpenGL context
(`crates/unbound-app/src/render/gl_interop.rs`). Kinds: 0 `UnityRenderingEvent` (`i32 id`); 1
`UnityRenderingEventAndData` (`i32 id`, `u64 data`); 2 and 3 a V1 and a V2 texture update (`u64
texture`, `u32 user_data`, `u32 format` as `UnityRenderingExtTextureFormat`, `u32
bytes_per_pixel`: the begin event, the upload of the data the plugin gives into level 0, the end
event); 4 a custom blit (`u32 command`, `u32 source` texture ID, `u64 destination` render buffer
pointer, `u32 param`, `u32 flags`). The managed side hands the stream over right after the
command, so the callback has run when the call that issued it returns, as in the Editor
(tests/projects/nativegfx). A 40 side and a 41 side refuse each other at startup.

### Render protocol 14: instance multiplier (NativeApi 33)

Opcode 32 carries one `u32` multiplier. It persists across submissions and frames, starts
at one, and zero resets it to one. Direct draws multiply their instance count; indirect
arguments remain unchanged. Measured in `tests/projects/cmdbuffers device.multiplier.*`.

### Render protocol 13: mesh GPU streams (NativeApi 32)

MeshPacket colors are float4 values in NativeApi 32, preserving packed floating-point colors
through the ordinary upload path.

Camera flag 64 scopes a gizmo draw: its batch color comes from the draw color, and its normals
use a direct model transform before the view normal transform. Ordinary draws retain their
material/global values and inverse-transpose normals.

Opcode 31, `MeshBuffers`, associates a mesh with its existing GraphicsBuffer streams. It carries
`u64 mesh`, `u8 sourceSlot`, `u32 vertexMask`, `u32 vertexCount`, four pairs of `u64 buffer, u32 stride`, a `u32` attribute count
and four `u32`s per attribute, followed by `u64 indexBuffer, u32 indexSize, u32 logicalIndexCount`, a `u32` submesh
count and three `u32`s per submesh. Attributes are attribute, format, dimension and stream;
submeshes are source index start, count and topology. Buffer writes retain their command-stream
order. Cached conversions update the drawing layout after writes, including 16-bit indices and submesh ranges. Quad buffers retain the logical indices followed
by the expanded triangle ranges; draws read those appended ranges. Slot 0 binds the mesh's own buffers;
slot 1 shares source indices with deformed meshes, and slots 2/3 supply the base/override vertex
attributes of additional vertex streams. The attribute mask keeps these sources disjoint,
including inherited UV channels. An empty description clears a source binding.
GPU changes leave CPU mesh channels unchanged. CPU vertex edits
upload the vertex streams; layout and size changes replace storage. NativeApi 31 and 32
refuse each other at startup.

### Render protocol 12: shared arrays and mesh-instance elements (NativeApi 28)

Opcode 30 is `MeshInstance` (`u32 index`, the bytes of protocol 11's `FirstInstance`): the next
draw is instance `index` of a mesh-instancing call, emitted for every instance including 0. It
sets the draw's first logical instance and is consumed even if that draw is skipped. The
instances of one call carry the same properties, the block's arrays whole; the renderer selects
the element. A scalar uniform whose draw property is an array takes element `index` (a float,
vector or matrix for 1, 4 or 16 components), or the material's value past the array's end. A
single value of a property the compiled variant declares instanced (shader cache format 19)
reaches instance 0 only; the others keep the material's. Outside a mesh instance, an instanced
property's array keeps the material's value. Uniform arrays keep every element. Protocol 11's
`<InstanceArray>`, `<InstanceElement>` and `<InstanceScalar>` properties are gone.

Property kinds 8 `SharedArray` (`u32 id`, then an array's `u32 components, u32 count, f32...`)
and 9 `ArrayRef` (`u32 id`) let many draws share one array: ids count from 0 in each submitted
stream, a definition out of order or a reference to an undefined id rejects the stream, and the
renderer keeps one copy of the data for all its references. Indirect draws' `unity_BaseCommandID`
is an ordinary `Int` draw property. A 27 side and a 28 side refuse each other at startup.

### Render protocol 11: logical instance IDs (NativeApi 22)

`FirstInstance` (opcode 30, `u32 first`) sets the first logical instance of the next draw and is
consumed even if that draw is skipped. Command-buffer mesh instancing emits each matrix through
the ordinary draw path with its original instance ID. `DrawSetup` keeps its existing bytes.
Property-block arrays were sent whole with each instance's element beside them (superseded by
protocol 12). NativeApi 21 and 22 refuse each other at startup.

### Render protocol 10: 32-bit indices (NativeApi 22)

`DrawIndices` takes an index size of 2 or 4. A draw reads 32-bit indices, and an indirect draw
(whose `DrawSetup` names an arguments buffer, `indexed` set) reads a buffer's indices with the
arguments' first index and base vertex, only when its program reads no vertex inputs: such a
program's pipeline binds no vertex buffer ("without a vertex buffer", the indexed
`CommandBuffer.DrawProcedural`), so no index can fetch past one. A program that reads vertex
inputs keeps protocol 8's limits; refused draws are skipped with a warning. A 21 side and a 22
side refuse each other at startup. NativeApi 21 carries main's audio reverb-zone frame layout;
22 adds both render protocols 10 and 11 while preserving that layout.

### Render protocol 9: depth copies (NativeApi 19)

`CopyDepth` (opcode 29: source `u64`, destination `u64`) draws the whole depth buffer of one
render texture into another's of the same size; handle 0 is the screen's. Between sample counts
each destination sample takes the source's sample of its index (equal counts) or its only or first
one. The destination's stencil is cleared; a texture without a depth buffer, or a size mismatch,
skips the copy with a logged message. The screen's depth buffer is one for the whole ring of
screen targets, so it lasts from frame to frame, and the frame's screen starts as the previous
frame left it (a new size starts black). A 18 side and a 19 side refuse each other at startup.

### Render protocol 8: indexed draws from a buffer (NativeApi 17 alone; 18 with audio filters)

`DrawIndices` (opcode 28) follows a `DrawSetup` when the next `Draw` reads indices from a
GraphicsBuffer instead of its mesh, for `Graphics.RenderPrimitivesIndexed`. It selects `count`
16-bit indices starting at `first`, with base vertex 0. The current combined implementation
decodes it only under NativeApi 18. A missing buffer, another index size, zero count or an
overflowing range rejects the stream as malformed. The stream has no version field or header.
32-bit and indirect indexed draws are refused on the managed side; see COMPATIBILITY_GAPS
row 212. Another NativeApi version is refused at startup before any packet is decoded.

### Render protocol 7: buffer copies, counters, indirect dispatches (NativeApi 15)

NativeApi 15 adds the commands `CopyBuffer`, `SetCounter`, `CopyCounter` and `DispatchIndirect`
(below) and the `read_texture` call, which reads a mip level of a texture's layer (an array
slice, cube face or volume depth slice). Append and Counter buffers (`CreateBuffer` usage bits 64
and 128) have a hidden counter of their own, bound where a kernel declares the buffer's counter
block (`<name>@count`, as glslang compiles `Append`, `Consume` and `IncrementCounter`); the
renderer copies a counter only after a dispatch has bound it, as Unity does. A 14 side and a 15
side refuse each other at startup in either pairing.

A `Dispatch` buffer property named after one of the kernel's cbuffers (a constant buffer, usage
bit 512) binds that cbuffer directly to the GPU buffer, from the byte offset in the int property
`<name>@offset`, for `<name>@size` bytes (`ComputeShader.SetConstantBuffer`). Preceding GPU
writes remain ordered; the cbuffer's own uniform properties are then not used. An invalid binding
skips the dispatch with a warning. Shader cache format 19 also records active instanced-property
declarations so only their scalar overrides fall back after instance zero.

### Render protocol 6: texture handle element bits (NativeApi 13)

NativeApi 14 appends `wrap_w` to `TexturePacket` and `ImportedTextureInfo`, and the
`texture-sampling-properties` call carries `wrapW`: a texture's own sampler takes its W wrap mode
(a Texture3D's depth; arrays and cubemaps ignore it). An inline sampler state has the W wrap mode its
name spells (the whole sampler's, or `...W`'s); the default samplers repeat along W. The managed side refuses a native side of another
version at startup, so a 13 side and a 14 side refuse each other in either pairing.

Allocated handles never set bits 62 and 63 (`render_protocol::handle::RESERVED`, `3 << 62`);
the renderer's own textures (`GrabPass`) count down from `handle::PLAIN_MAX` (`!RESERVED`).
A **sampled binding** may set bit 62, `DEPTH_ELEMENT` (`1 << 62`): the render texture's depth
buffer, sampled as `(d, 0, 0, 1)` (`RenderTextureSubElement.Depth`). Sampled bindings are a
material's texture property (`update_material`), a draw's per-draw texture property (`Draw`) and a
global texture (`Global`). Everywhere else a handle must be plain: uploads (`video_frame`'s planes too), imports, releases,
read backs, `SetTarget`/`SetTargets` (the depth's `u64::MAX` "none" stays valid), `CopyTexture`,
`GenerateMips`, a draw's main texture, image properties and kernel (`Dispatch`) properties. A
reserved bit where it is not allowed, bit 63, or bit 62 alone is an error: the stream is rejected
as malformed, the material update or the call is refused, with a logged message.

The renderer binds a depth element through the texture's depth copy (a float color texture of its
size), made the first time a draw samples it. A multisampled depth buffer copies one sample (the
one a quarter pixel left of the center at 2 samples). The copy is brought up to date before the
draw that samples it, also when the texture's depth buffer is the current target's (its own or
attached to another color). It is then copied after every group of passes that draws into that
depth buffer. Comparison (depth) slots read the depth buffer itself. Bind groups are keyed by the
handle as bound, element bits included, and the texture's generation, so a texture's color and
depth element never share one; `_MainTex` too, and `<name>_TexelSize` is the texture's. Managed getters
(`Material.GetTexture`) return the texture: the bit exists only on the wire.

The managed side refuses a native side of another `NativeApi.Version` at startup (`Native.Initialize`),
before anything renders, in both directions.

## Functions (in `NativeApi`, after the version 1 entries)

| Function | Purpose |
| --- | --- |
| `upload_mesh(handle, *const MeshPacket)` | Create or replace a mesh's GPU data. |
| `release_mesh(handle)` | Free it. |
| `upload_texture(handle, *const TexturePacket)` | Create or replace a texture made by code (pixels may be null for render targets). |
| `import_texture(handle, guid, guid_len, *mut ImportedTextureInfo) -> i32` | Import an asset texture (`.png`, `.psd`, ...) into the handle; 0 on success. Its sprites come from the `texture-sprites` JSON call. |
| `release_texture(handle)` | Free it. |
| `update_material(handle, *const MaterialPacket)` | Shader, render queue, keywords and properties of a material. |
| `release_material(handle)` | Free it. |
| `submit_commands(*const u8, i64)` | Rendering commands (below), executed on the GPU in order before returning (version 9). |
| `read_pixels(texture, x, y, w, h, float, *mut NativeBuffer) -> i32` | A rectangle of a texture (0: the screen) after everything submitted: RGBA, rows bottom first, 4 bytes per pixel or 4 floats (version 9). |
| `read_buffer(buffer, offset, size, *mut NativeBuffer) -> i32` | A buffer's bytes after everything submitted (version 10). |
| `read_texture(texture, mip, layer, x, y, w, h, float, *mut NativeBuffer) -> i32` | As `read_pixels`, from a mip level of a layer; 0 when the texture has no such level or layer (version 15). |
| `font_glyph(font, codepoint, size, style, *mut GlyphInfo, *mut NativeBuffer) -> i32` | Rasterizes a glyph of a font opened with the `font-open` call (version 3); 1 when the font has it. The buffer holds `width * height` coverage bytes, top row first. |
| `plugin_load(load)`, `plugin_set_log(sink)`, `plugin_shutdown()` | A native plugin's `UnityPluginLoad` with the plugin interfaces; where plugin messages go; the device's Shutdown events at the end (version 41, `plugin_api.rs`). |
| `native_texture(texture, depth) -> u64` | The OpenGL texture name plugins see for a texture (its depth buffer's), sharing its memory; 0 when it cannot be shared (version 41). |
| `external_texture(texture, native, mips, *mut NativeBuffer) -> i32` | Makes a texture show a native OpenGL texture through that many of its mip levels (`CreateExternalTexture`); 0 on success, else the reason (version 41; 55: the levels, where the declared size and color space were). |

## Conventions

- Matrices are Unity's `Matrix4x4` (column-major, `m00, m10, m20, m30, m01, ...`). A camera's `view` is
  `Camera.worldToCameraMatrix` (right-handed camera space, looking down -Z) and `projection` is
  `Camera.projectionMatrix` (OpenGL convention, clip z in [-w, w]). The renderer remaps z to wgpu's
  [0, w] and keeps Unity's winding (front faces are clockwise on screen).
- Colors are in the project's color space (`ColorSpace`, `BeginFrame.color_space`: 0 gamma, 1
  linear; a built-in camera's clear color is converted to it). In gamma
  projects shading and blending happen on gamma values, as Unity does, in a non-sRGB target.
- Texture rows are bottom-up (Unity's order); uv (0,0) is the bottom-left texel.
- Viewports and clip rectangles are in pixels, origin bottom-left.

## Structures

```
MeshPacket {
  i32 vertex_count;
  *const Vector3 positions;   // required
  *const Vector3 normals;     // nullable
  *const Vector4 tangents;    // nullable
  *const Color32 colors;      // nullable (white)
  *const Vector4 uv[4];       // nullable each; channels are widened to 4 components
  i32 index_count;  *const u32 indices;
  i32 submesh_count; *const SubMeshPacket submeshes;
}
SubMeshPacket { i32 index_start; i32 index_count; i32 topology; i32 base_vertex; }   // topology: MeshTopology

TexturePacket {
  i32 width, height;
  i32 format;          // 0 RGBA8 color, 1 RGBA8 linear, 2 float RGBA stored as half, 3 Alpha8,
                       // 4 packed 11/11/10 float, 5 depth-only RT, 6 R32F,
                       // 7/8 native BC7 color/data, 9 RGBA32F, 10 RG32F, 11 R16F, 12 RG16F,
                       // 13 RG16 unorm, 14 RGBA16 unorm, 15 RGB10A2 unorm, 16 R16 unorm (float
                       // RGBA wire data; RGBA8 with a warning where the device cannot keep them),
                       // 17..=34 integer render targets: 17 + (channel index (R, RG, RGBA) × 3 +
                       // bit index (8, 16, 32)) × 2 + signed; no data, single-sampled,
                       // 35 R8 snorm, 36 RG8 snorm, 37 RGBA8 snorm (float RGBA wire data; RGBA8
                       // with a warning where the device cannot keep them)
  i32 mip_count;       // levels present in data, largest first
  *const u8 data; i64 data_length;   // null: uninitialized (render targets); float formats: RGBA f32 wire data
  i32 filter_mode, wrap_u, wrap_v, aniso_level;
  i32 render_target;   // bits: 1 render attachment, 2 storage binding, 4 auto-generate mips;
                       // bits 8..15 contain the MSAA sample count
  i32 depth_bits;      // render targets: 0, 16, 24 (with stencil)
  i32 layers, dimension; // each layer has a mip chain; dimension is Unity's TextureDimension
  i32 wrap_w;          // TextureWrapMode along W (NativeApi 14): a Texture3D's depth
}

ImportedTextureInfo { i32 width, height, mip_count, srgb, filter_mode, wrap_u, wrap_v, aniso_level, readable, sprite_count,
  format,      // TexturePacket format: 0 sRGB RGBA8, 1 linear RGBA8, 2 RGBAHalf, 9 RGBAFloat
  dimension;   // TextureDimension: 2 2D, 3 Texture3D, 4 cubemap, 5 Texture2DArray; an array's
               // slices and a volume's depth: the `texture-layers` JSON call
  wrap_w;      // the importer's wrapW (NativeApi 14)
  texture_format; // Unity's TextureFormat the Editor reports (Alpha8, R8, RGB24 for automatic
                  // uncompressed formats), 0: the one `format`'s data gives (NativeApi 31)
}

MaterialPacket {
  *const u8 shader_name; i32 shader_name_length;
  i32 render_queue;
  *const u8 keywords; i32 keywords_length;      // space separated
  i32 property_count; *const MaterialProperty properties;
}
MaterialProperty {
  *const u8 name; i32 name_length;
  i32 kind;            // 0 float, 1 vector/color, 2 texture, 3 int
  Vector4 value;       // floats and ints in x
  u64 texture;         // kind 2: texture handle (0 none)
  Vector4 texture_st;  // kind 2: scale (xy) and offset (zw)
}

```

## The command stream

`GfxStream` (managed/UnityEngine/Graphics/CommandStream.cs) records commands; `render_commands.rs`
decodes them. Little endian; each command is a `u8` opcode and its fields; strings are `u32` length
and UTF-8; matrices 16 `f32`. The renderer keeps the state commands set (target, viewport, camera
matrices, global properties and keywords) from one command to the next, across submissions and
frames. The managed side hands recorded commands over at the end of a frame and before anything
that must come after them: a resource upload or release (so it applies between the commands
before and after it, as on Unity's render thread) and a read back.

```
1 BeginFrame  i32 screen_width, screen_height, color_space
2 EndFrame                                    // the screen is complete and shown
3 SetTarget   u64 color (0: screen), u64 depth (0: color's own, u64::MAX: none), i32 mip, slice
4 Clear       u8 flags (1 color, 2 depth, 4 stencil), f32x4 color, f32 depth, u32 stencil   // the viewport
5 Viewport    f32 x, y, width, height         // pixels, origin bottom-left
6 Camera      mat view, mat projection, u32 flags (1: overlay — unity_GUIZTestMode is Always)
7 Global      str name, property               // Shader.SetGlobal*
8 Keyword     str name, u8 enabled             // Shader.EnableKeyword / DisableKeyword
9 Draw        u64 mesh, i32 submesh, u64 material, i32 pass (-1: the forward passes),
              mat model, f32x4 color, u64 main_texture, u8 clip_enabled, f32x4 clip,
              u32 count, property[count]       // per-draw properties (MaterialPropertyBlock)
10 CopyTexture  u64 src, i32 slice, mip, i32x4 rect (width < 0: the whole level), u64 dst, i32 slice, mip, x, y
11 CreateBuffer u64 handle, u64 size, u32 usage (GraphicsBuffer.Target flags)
12 BufferData   u64 handle, u64 offset, u32 length, bytes
13 ReleaseBuffer u64 handle
14 Dispatch     str file (.compute), str kernel, str keywords, u32 groups x, y, z, u32 count, property[count]
15 RenderPipeline str tag                      // the SubShader RenderPipeline tag materials draw with ("" built-in)
16 PassTags     u32 count, str[count]            // the LightMode tags pass TAGGED_PASSES draws pick
17 DepthBias    f32 bias, f32 slope              // CommandBuffer.SetGlobalDepthBias (reset each frame)
18 StateOverride u8 present, then u32 mask, u8 blend[7], u8 cull, i32 offset_units, f32 offset_factor,
              u8 zwrite, u8 ztest, u8 stencil_enabled, ref, read_mask, write_mask, u8 stencil[8]
                                                // a RenderStateBlock over the passes' states (reset each frame)
19 ColorSpace   i32 color_space                  // before any frame begins (a camera rendered from Start)
20 Anisotropy   i32 mode (0 off, 1 per texture, 2 forced), forced_min, global_max   // 2D texture assets only
21 SetTargets   u32 count (2..8), u64 color[count], u64 depth, i32 mip, slice
                                                // several colors at once: the fragment program's outputs in order
22 DrawSetup    u32 instances, u64 args (0: none), u32 offset, u8 indexed
                                                // how the next Draw runs: instances, or counts read on the GPU
                                                // (indexed: index count, instances, first index, base vertex,
                                                // first instance; else vertex count, instances, first vertex,
                                                // first instance)
23 GenerateMips u64 texture                      // filter a render target's lower mip levels
24 CopyBuffer   u64 src, u64 src_offset, u64 dst, u64 dst_offset, u64 size   // bytes, multiples of 4
25 SetCounter   u64 buffer, u32 value               // an Append/Counter buffer's hidden counter
26 CopyCounter  u64 src, u64 dst, u32 offset         // the counter (a u32) into dst at a byte offset;
                                                // nothing before a dispatch has bound the counter
27 DispatchIndirect u64 args, u32 offset, then Dispatch's fields   // group counts: 3 u32s of args
28 DrawIndices  u64 buffer, u8 index_size (2; 4 from protocol 10), u32 first, u32 count   // next Draw's indices, base vertex 0 or the indirect arguments' (protocol 8)
29 CopyDepth    u64 source, u64 destination       // protocol 9
30 MeshInstance u32 index, u32 count            // next Draw is this instance of a mesh-instancing call of count instances (protocols 11, 12; count NativeApi 65)
34 Plugin       u8 kind, u64 callback, fields    // a native plugin's callback (protocol 15)
35 ScreenStandIn u64 texture                     // the screen's stand-in, 0 none (NativeApi 50)
36 DotsDraw     u32 header[8], u32 window_offset, u32 window_size, u32 count, u32 visible[count]
                                                // next Draw is a BatchRendererGroup command's (NativeApi 60)
property := str name, u8 kind (0 float f32, 1 vector f32x4, 2 texture u64 (element bits in Draw and Global
            only, see render protocol 6) + f32x4 st, 3 int i32, 4 matrix,
            5 buffer u64, 6 array u32 components (0: consecutive values) u32 n f32[n], 7 image u64 texture i32 mip,
            8 shared array u32 id then as 6, 9 array reference u32 id (protocol 12))
```

A buffer command hands over the work planned before it, so it lands between what came before and
after; copies and counter copies are encoded in order with the passes around them. A dispatch compiles its kernel from the `.compute` file (`unbound-shaders::compute`: resources
keep their names, bound in group 0; RW textures compile in the formats of the textures bound to
them) and binds its own properties, then the global ones, by name.

A camera of the built-in pipeline is `SetTarget`, `Viewport`, `Clear` (per its clear flags),
`Camera`, then its sorted draws. Before the cameras each frame sets the lighting environment's
built-in variables and fog keywords as globals.

## The renderer (`crates/unbound-app`)

Calls run on the GPU as they arrive, on the game's thread (`GpuBackend` → `executor.rs`): a
submission is planned (passes per target, pipelines, uniforms, bind groups), encoded and queued at
once, so a read back or the next frame sees its results. Every render texture is stored bottom-up
(Unity's row order), so it samples upright with Unity's uvs, except a screen camera's stand-in,
stored as the screen is; the screen is a ring of three
offscreen targets (`Rgba8Unorm` with gamma values in gamma projects, `Rgba16Float` in linear ones);
`EndFrame` hands the frame's to the window, whose render schedule shows the latest complete one.
Format 0 textures are sampled raw in gamma projects and through an sRGB view in linear ones;
Alpha8 samples as `(0, 0, 0, a)`, and `_TextureSampleAdd` defaults to `(1, 1, 1, 0)` for it as
Unity's canvas sets it.

A texture slot's texel type comes from its declaration (shader cache format 16): floats, signed or
unsigned integers (`Texture2D<uint2>`, read unfiltered) or depth. An integer slot reads a float
texture's values as their bits within its size and zeros where nothing is bound, as Unity does on
OpenGL Core (tests/projects/rtdepth); the bits are copied into an integer texture right before
each pass that reads them (after a `GrabPass` before it). An integer slot that is not 2D (bound or
not), or bound to a layered texture or a depth element, skips the draw with a message.
The fixed legacy `_MainTex` group accepts filtered float 2D slots only. Integer and
comparison `_MainTex` declarations retain their typed program slots, including
the renderer's 2D main-texture override. The cache version rejects earlier entries
whose `_MainTex` slot classification differs.

A material's shader name selects its shader: the project's `.shader` source, else Unity's built-in
one (compiled from the release's `builtin_shaders` sources). The renderer draws the forward passes
of the SubShader the built-in pipeline runs, each with its compiled program (see the Shaders bullet
of `ARCHITECTURE.md`) and its render states (Blend, BlendOp, ZWrite, ZTest, Cull, ColorMask, Offset,
Stencil, with `[_Property]` values). Missing shaders and passes that do not compile draw with
Unity's error shader (logged once).

A draw's `color` multiplies the vertex color (as the canvas does); `clip` becomes a scissor
rectangle.
