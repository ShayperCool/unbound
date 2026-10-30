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
| `font_glyph(font, codepoint, size, style, *mut GlyphInfo, *mut NativeBuffer) -> i32` | Rasterizes a glyph of a font opened with the `font-open` call (version 3); 1 when the font has it. The buffer holds `width * height` coverage bytes, top row first. |

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
                       // 4 packed 11/11/10 float, 5 depth-only RT, 6 R32F RT,
                       // 7/8 native BC7 color/data, 9 RGBA32F RT
  i32 mip_count;       // levels present in data, largest first
  *const u8 data; i64 data_length;   // null: uninitialized (render targets)
  i32 filter_mode, wrap_u, wrap_v, aniso_level;
  i32 render_target;   // bits: 1 render attachment, 2 storage binding, 4 auto-generate mips;
                       // bits 8..15 contain the MSAA sample count
  i32 depth_bits;      // render targets: 0, 16, 24 (with stencil)
  i32 layers, dimension; // each layer has a mip chain; dimension is Unity's TextureDimension
}

ImportedTextureInfo { i32 width, height, mip_count, srgb, filter_mode, wrap_u, wrap_v, aniso_level, readable, sprite_count; }

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
property := str name, u8 kind (0 float f32, 1 vector f32x4, 2 texture u64 + f32x4 st, 3 int i32, 4 matrix,
            5 buffer u64, 6 array u32 components (0: consecutive values) u32 n f32[n], 7 image u64 texture i32 mip)
```

A buffer command hands over the work planned before it, so it lands between what came before and
after. A dispatch compiles its kernel from the `.compute` file (`unbound-shaders::compute`: resources
keep their names, bound in group 0; RW textures compile in the formats of the textures bound to
them) and binds its own properties, then the global ones, by name.

A camera of the built-in pipeline is `SetTarget`, `Viewport`, `Clear` (per its clear flags),
`Camera`, then its sorted draws. Before the cameras each frame sets the lighting environment's
built-in variables and fog keywords as globals.

## The renderer (`crates/unbound-app`)

Calls run on the GPU as they arrive, on the game's thread (`GpuBackend` → `executor.rs`): a
submission is planned (passes per target, pipelines, uniforms, bind groups), encoded and queued at
once, so a read back or the next frame sees its results. Every target is stored bottom-up (Unity's
row order), so render textures sample upright with Unity's uvs; the screen is a ring of three
offscreen targets (`Rgba8Unorm` with gamma values in gamma projects, `Rgba16Float` in linear ones);
`EndFrame` hands the frame's to the window, whose render schedule shows the latest complete one.
Format 0 textures are sampled raw in gamma projects and through an sRGB view in linear ones;
Alpha8 samples as `(0, 0, 0, a)`, and `_TextureSampleAdd` defaults to `(1, 1, 1, 0)` for it as
Unity's canvas sets it.

A material's shader name selects its shader: the project's `.shader` source, else Unity's built-in
one (compiled from the release's `builtin_shaders` sources). The renderer draws the forward passes
of the SubShader the built-in pipeline runs, each with its compiled program (see the Shaders bullet
of `ARCHITECTURE.md`) and its render states (Blend, BlendOp, ZWrite, ZTest, Cull, ColorMask, Offset,
Stencil, with `[_Property]` values). Missing shaders and passes that do not compile draw with
Unity's error shader (logged once).

A draw's `color` multiplies the vertex color (as the canvas does); `clip` becomes a scissor
rectangle.
