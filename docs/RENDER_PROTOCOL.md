# Render protocol (managed engine → native renderer)

The managed engine decides *what* is drawn and *in which order*, with Unity's rules (cameras by
depth, render queues, sorting layers and orders, distance sorting of transparent objects, canvases
after cameras). The native renderer (Bevy/wgpu, `crates/unbound-app`) executes the ordered draw lists
with each material's shader compiled from its Unity source. Every structure below is `#[repr(C)]`
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
| `submit_frame(*const FramePacket)` | The frame's cameras and draw lists. The native side copies what it needs before returning. |
| `font_glyph(font, codepoint, size, style, *mut GlyphInfo, *mut NativeBuffer) -> i32` | Rasterizes a glyph of a font opened with the `font-open` call (version 3); 1 when the font has it. The buffer holds `width * height` coverage bytes, top row first. |

## Conventions

- Matrices are Unity's `Matrix4x4` (column-major, `m00, m10, m20, m30, m01, ...`). A camera's `view` is
  `Camera.worldToCameraMatrix` (right-handed camera space, looking down -Z) and `projection` is
  `Camera.projectionMatrix` (OpenGL convention, clip z in [-w, w]). The renderer remaps z to wgpu's
  [0, w] and keeps Unity's winding (front faces are clockwise on screen).
- Colors are in the project's color space (`FramePacket.color_space`: 0 gamma, 1 linear). In gamma
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
  i32 format;          // 0 RGBA8 (color data, gamma/sRGB), 1 RGBA8 linear data, 2 RGBA32F, 3 R8 alpha
  i32 mip_count;       // levels present in data, largest first
  *const u8 data; i64 data_length;   // null: uninitialized (render targets)
  i32 filter_mode, wrap_u, wrap_v, aniso_level;
  i32 render_target;   // 1: can be drawn into (RenderTexture)
  i32 depth_bits;      // render targets: 0, 16, 24 (with stencil)
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

FramePacket {
  i32 screen_width, screen_height;
  i32 color_space;
  i32 camera_count; *const CameraPacket cameras;     // in rendering order
  i32 draw_count;   *const DrawPacket draws;
  i32 global_count; *const MaterialProperty globals; // Shader.SetGlobal* (floats, ints, vectors, textures)
  *const u8 keywords; i32 keywords_length;          // Shader.EnableKeyword, space-separated UTF-8
}
CameraPacket {
  Matrix4x4 view; Matrix4x4 projection;
  Rect viewport;            // pixels: x, y, width, height
  i32 clear_flags;          // CameraClearFlags: 1 skybox, 2 solid color, 3 depth, 4 nothing
  Color clear_color;
  i32 first_draw, draw_count;
  u64 target_texture;       // 0: the screen
}
DrawPacket {
  u64 mesh; i32 submesh; u64 material;
  Matrix4x4 model;
  Color color;              // multiplies the material color (CanvasRenderer color and alpha)
  u64 main_texture;         // 0: the material's _MainTex; else overrides it (CanvasRenderer.SetTexture)
  Rect clip;                // pixels; used when clip_enabled
  i32 clip_enabled;
  i32 flags;                // reserved
}
```

## The renderer (`crates/unbound-app`)

The backend queues calls in order and hands them to the render world at extraction, so a frame's
uploads and draws travel together. Every target is stored bottom-up (Unity's row order), so render
textures sample upright with Unity's uvs; the screen is an intermediate target (`Rgba8Unorm` with
gamma values in gamma projects, `Rgba16Float` in linear ones) presented on the window at the end.
Format 0 textures are sampled raw in gamma projects and through an sRGB view in linear ones;
Alpha8 samples as `(0, 0, 0, a)`, and `_TextureSampleAdd` defaults to `(1, 1, 1, 0)` for it as
Unity's canvas sets it.

A material's shader name selects its shader: the project's `.shader` source, else Unity's built-in
one (compiled from the release's `builtin_shaders` sources). The renderer draws the forward passes
of the SubShader the built-in pipeline runs, each with its compiled program (see the Shaders bullet
of `ARCHITECTURE.md`) and its render states (Blend, BlendOp, ZWrite, ZTest, Cull, ColorMask, Offset,
Stencil, with `[_Property]` values). Missing shaders and passes that do not compile draw with
Unity's error shader (logged once).

`DrawPacket.color` multiplies the vertex color (as the canvas does); `clip` becomes a scissor
rectangle.
