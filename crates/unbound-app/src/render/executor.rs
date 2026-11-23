//! Executes the rendering command stream (render_commands) on the GPU, on the thread that submits
//! it: each submission is planned (targets, passes, pipelines, uniforms, bind groups), encoded and
//! queued at once, so later commands, `read_pixels` and the next frame see its results in order.
//! The screen is a ring of offscreen targets; a completed frame's target is handed to the window
//! (`Presented`), which the render world shows.
mod compute;
mod mesh_buffers;
#[path = "mipmaps.rs"]
mod mipmaps;
mod msaa;
mod plugins;
#[cfg(target_os = "macos")]
mod plugins_metal;
#[cfg(target_os = "macos")]
pub use plugins_metal::provide_metal_plugin_interfaces;
#[cfg(target_os = "linux")]
mod plugins_vulkan;
#[cfg(target_os = "linux")]
pub use plugins_vulkan::provide_interfaces as provide_vulkan_plugin_interfaces;
#[cfg(target_os = "linux")]
pub use plugins_vulkan::provide_interfaces_elsewhere as provide_vulkan_plugin_interfaces_elsewhere;
mod readback;
mod warmup;

use super::{
    compiled,
    device::{DeviceConventions, mirror_compare},
    pipelines::{ClearKey, DEPTH_FORMAT, DRAW_SLOT, PipelineKey, Pipelines},
    resources::{self, GpuMesh, GpuTexture, SamplerKey},
};

fn same_property_value(a: &owned::PropertyValue, b: &owned::PropertyValue) -> bool {
    use owned::PropertyValue::*;
    match (a, b) {
        (Float(x), Float(y)) => x.to_bits() == y.to_bits(),
        (Int(x), Int(y)) => x == y,
        (Buffer(x), Buffer(y)) => x == y,
        (Vector(x), Vector(y)) => x.iter().zip(y).all(|(p, q)| p.to_bits() == q.to_bits()),
        (Matrix(x), Matrix(y)) => x.iter().zip(y).all(|(p, q)| p.to_bits() == q.to_bits()),
        (Texture { handle: hx, st: sx }, Texture { handle: hy, st: sy }) => {
            hx == hy && sx.iter().zip(sy).all(|(p, q)| p.to_bits() == q.to_bits())
        }
        (
            Image {
                handle: hx,
                mip: mx,
            },
            Image {
                handle: hy,
                mip: my,
            },
        ) => hx == hy && mx == my,
        (
            Array {
                components: cx,
                data: dx,
            },
            Array {
                components: cy,
                data: dy,
            },
        ) => {
            cx == cy
                && (Arc::ptr_eq(dx, dy)
                    || (dx.len() == dy.len()
                        && dx
                            .iter()
                            .zip(dy.iter())
                            .all(|(p, q)| p.to_bits() == q.to_bits())))
        }
        _ => false,
    }
}
use crate::materials::{ResolvedMaterial, Resolver};
use bytemuck::{Pod, Zeroable};
use rustc_hash::{FxHashMap as HashMap, FxHashSet as HashSet};
use std::{
    ops::Range,
    sync::{Arc, Mutex},
    time::Instant,
};
use unbound_runtime::render_protocol::{
    TextureReadOptions,
    commands::{self, Command},
    handle, owned, texture_format,
};
use unbound_shaders::SampleKind;

/// Resource changes, applied in order with the commands.
#[derive(Debug)]
pub enum Upload {
    UploadMesh(u64, owned::Mesh),
    ReleaseMesh(u64),
    UploadTexture(u64, owned::Texture),
    TextureMipBias(u64, f32),
    TextureSampler(u64, [i32; 5]),
    ReleaseTexture(u64),
    UpdateMaterial(u64, owned::Material),
    ReleaseMaterial(u64),
}

/// What multiplies a draw's vertex colors: its color, or for a renderer's color of the vertices
/// that color as vertex color bytes (each channel clamped to 0..1, times 255, rounded half up;
/// matches the Unity 6000.0.71f1 Editor, tests/projects/vertexcolors), or nothing for a
/// renderer's color of the constants alone.
fn vertex_tint(tint: commands::DrawTint, color: [f32; 4]) -> [f32; 4] {
    match tint {
        commands::DrawTint::None => color,
        // NaN gives byte 0, as the managed side's conversion does.
        commands::DrawTint::Vertices => color.map(|c| {
            if c.is_nan() {
                0.0
            } else {
                (c.clamp(0.0, 1.0) * 255.0 + 0.5).floor() / 255.0
            }
        }),
        commands::DrawTint::Constant => [1.0; 4],
    }
}

#[repr(C)]
#[derive(Clone, Copy, Pod, Zeroable)]
struct DrawUniform {
    model: [f32; 16],
    view: [f32; 16],
    proj: [f32; 16],
    color: [f32; 4],
    screen: [f32; 4],
    misc: [f32; 4],
    pad: [f32; 4],
}

/// Idle frame retention window for cached program texture and buffer bind groups.
const PROGRAM_BINDING_IDLE_FRAMES: u64 = 8;

/// Canonical tag for a ShaderLab texture default name, disambiguating unbound/fallback slots in
/// [`TextureGroupKey`].
fn default_texture_tag(default: &str) -> u8 {
    match default {
        "white" => 1,
        "black" => 2,
        "unbound-unassigned" => 3,
        "gray" | "grey" => 4,
        "linearGray" => 5,
        "bump" => 6,
        "red" => 7,
        _ => 0,
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
struct TextureSlotKey {
    /// Effective raw handle (depth-element bit included), or 0 when unbound or unreadable as float.
    raw: u64,
    /// Texture generation (0 when unbound/missing; `default_tag` disambiguates).
    generation: u64,
    /// Default texture tag used when unbound/missing or falling back.
    default_tag: u8,
}

#[derive(Clone, Debug, PartialEq, Eq, Hash)]
struct TextureGroupKey {
    program: u32,
    space: i32,
    slots: Vec<TextureSlotKey>,
}

struct TextureGroupLookup<'a> {
    program: u32,
    space: i32,
    slots: &'a [TextureSlotKey],
}

impl std::hash::Hash for TextureGroupLookup<'_> {
    fn hash<H: std::hash::Hasher>(&self, state: &mut H) {
        self.program.hash(state);
        self.space.hash(state);
        self.slots.hash(state);
    }
}

impl bevy::platform::collections::Equivalent<TextureGroupKey> for TextureGroupLookup<'_> {
    fn equivalent(&self, key: &TextureGroupKey) -> bool {
        key.program == self.program && key.space == self.space && key.slots.as_slice() == self.slots
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
struct BufferSlotKey {
    /// Buffer handle (0 when unbound -> `empty_storage`).
    handle: u64,
    /// Whether this slot binds the buffer's hidden counter instead of its data buffer.
    counter: bool,
}

#[derive(Clone, Debug, PartialEq, Eq, Hash)]
struct BufferGroupKey {
    program: u32,
    slots: Vec<BufferSlotKey>,
}

struct BufferGroupLookup<'a> {
    program: u32,
    slots: &'a [BufferSlotKey],
}

impl std::hash::Hash for BufferGroupLookup<'_> {
    fn hash<H: std::hash::Hasher>(&self, state: &mut H) {
        self.program.hash(state);
        self.slots.hash(state);
    }
}

impl bevy::platform::collections::Equivalent<BufferGroupKey> for BufferGroupLookup<'_> {
    fn equivalent(&self, key: &BufferGroupKey) -> bool {
        key.program == self.program && key.slots.as_slice() == self.slots
    }
}

/// Cached position of a global texture property inside `UnityRenderer::globals`: globals are only
/// ever appended or updated in place, so a found slot keeps its name and an absence holds while
/// `globals.len()` stays unchanged. High 32 bits = last seen `globals.len()`, low 32 bits = `idx + 1`
/// or `0` when absent.
#[derive(Default)]
struct GlobalTexSlot(std::sync::atomic::AtomicU64);

impl GlobalTexSlot {
    #[inline]
    fn find_handle(
        &self,
        globals: &[(String, owned::PropertyValue)],
        index: &owned::NameIndex,
        name: &str,
    ) -> u64 {
        use std::sync::atomic::Ordering::Relaxed;
        let packed = self.0.load(Relaxed);
        let (length, at) = ((packed >> 32) as usize, (packed & 0xffff_ffff) as usize);
        if at > 0 {
            if let Some((n, value)) = globals.get(at - 1)
                && n == name
            {
                return match value {
                    owned::PropertyValue::Texture { handle, .. } => *handle,
                    _ => 0,
                };
            }
        } else if length == globals.len() {
            return 0;
        }
        let found = index.find_position(globals, name);
        let slot = found.map_or(0, |i| i as u64 + 1);
        self.0.store(
            ((globals.len() as u64) << 32) | (slot & 0xffff_ffff),
            Relaxed,
        );
        found.map_or(0, |i| match globals[i].1 {
            owned::PropertyValue::Texture { handle, .. } => handle,
            _ => 0,
        })
    }
}

/// Precomputed per-slot metadata for one `(GpuMaterial, program_id)` pair so steady-state
/// `program_bind_group` calls skip repeated `ResolvedMaterial` property/declaration lookups.
struct MaterialTextureSlot {
    material_handle: u64,
    default_tag: u8,
    default_name: Arc<str>,
    declares: bool,
    is_2d: bool,
    is_main_2d: bool,
    dim: unbound_shaders::TextureDim,
    slot: unbound_shaders::TextureSlot,
    global_slot: GlobalTexSlot,
}

struct MaterialTexturePlan {
    slots: Vec<MaterialTextureSlot>,
    any_uncacheable: bool,
}

impl MaterialTexturePlan {
    fn build(resolved: &ResolvedMaterial, program: &unbound_shaders::Compiled) -> Self {
        let mut slots = Vec::with_capacity(program.textures.len());
        let mut any_uncacheable = false;
        for (k, name) in program.textures.iter().enumerate() {
            let (material_handle, default) = resolved.texture_ref(name);
            let dim = program
                .texture_dims
                .get(k)
                .copied()
                .unwrap_or(unbound_shaders::TextureDim::D2);
            let slot = program.texture_slots.get(k).copied().unwrap_or_default();
            if slot.multisampled || slot.integer() {
                any_uncacheable = true;
            }
            let is_2d = dim == unbound_shaders::TextureDim::D2;
            slots.push(MaterialTextureSlot {
                material_handle,
                default_tag: default_texture_tag(default),
                default_name: Arc::from(default),
                declares: resolved.declares(name),
                is_2d,
                is_main_2d: is_2d && name == "_MainTex",
                dim,
                slot,
                global_slot: GlobalTexSlot::default(),
            });
        }
        Self {
            slots,
            any_uncacheable,
        }
    }
}

/// Summary extracted from a single scan of a draw's per-draw `properties` (`MaterialPropertyBlock`,
/// per-renderer overrides, or mesh-instancing limits).
struct DrawPropertySummary {
    /// `Some(handle)` when `_MainTex` appeared in `properties` (last wins; `Some(0)` if the last
    /// entry was a non-texture or handle 0), `None` if `_MainTex` did not appear.
    main_tex_override: Option<u64>,
    has_texture_override: bool,
    has_buffer_override: bool,
    has_mesh_batch_limits: bool,
    mesh_batch_limits: Option<[f32; 4]>,
}

/// The texture a draw reads by `name`: its own property of that name (the last), else the
/// material's; 0 when that property is not a texture (whatever it is, a draw's own property of the
/// name hides the material's).
fn layered_texture(
    overrides: &[(String, owned::PropertyValue)],
    material: &owned::Material,
    name: &str,
) -> u64 {
    let value = overrides
        .iter()
        .rev()
        .find(|(n, _)| n == name)
        .map(|(_, v)| v)
        .or_else(|| material.get(name));
    match value {
        Some(owned::PropertyValue::Texture { handle, .. }) => *handle,
        _ => 0,
    }
}

fn scan_draw_properties(properties: &[(String, owned::PropertyValue)]) -> DrawPropertySummary {
    let mut main_tex_override = None;
    let mut has_texture_override = false;
    let mut has_buffer_override = false;
    let mut has_mesh_batch_limits = false;
    let mut mesh_batch_limits = None;
    for (name, value) in properties {
        match value {
            owned::PropertyValue::Texture { handle, .. } => {
                has_texture_override = true;
                if name == "_MainTex" {
                    main_tex_override = Some(*handle);
                }
            }
            owned::PropertyValue::Buffer(_) => {
                has_buffer_override = true;
                if name == "_MainTex" {
                    main_tex_override = Some(0);
                }
            }
            owned::PropertyValue::Vector(v) if name == "<MeshInstanceBatchLimits>" => {
                has_mesh_batch_limits = true;
                if mesh_batch_limits.is_none() {
                    mesh_batch_limits = Some(*v);
                }
            }
            _ => {
                if name == "_MainTex" {
                    main_tex_override = Some(0);
                } else if name == "<MeshInstanceBatchLimits>" {
                    has_mesh_batch_limits = true;
                }
            }
        }
    }
    DrawPropertySummary {
        main_tex_override,
        has_texture_override,
        has_buffer_override,
        has_mesh_batch_limits,
        mesh_batch_limits,
    }
}

struct GpuMaterial {
    /// Shared: each draw holds it while it is planned (a copy per draw cost more than drawing).
    resolved: Arc<ResolvedMaterial>,
    /// Precomputed `_MainTex` lookup from `resolved` (avoids per-draw `String` clone + lookup).
    main_tex_handle: u64,
    main_tex_default: Arc<str>,
    declares_main_tex: bool,
    /// Cached `resolved.all` pass indices matching `UnityRenderer::pass_tags` at `pass_tags_gen`.
    tagged_passes: Option<(u64, Vec<usize>)>,
    /// Precomputed per-slot texture plans per compiled program id.
    texture_plans: rustc_hash::FxHashMap<u32, Arc<MaterialTexturePlan>>,
    /// The program variant (and whether it is the error shader's) per (pass, global keyword
    /// set, linear colour space, standalone mesh replay).
    /// By pass, keyword set, color space, instancing mode ([`InstancingMode`]) and whether the
    /// target is multisampled (a framebuffer-fetching program's variant reads samples).
    variants: rustc_hash::FxHashMap<(usize, u32, bool, InstancingMode, bool), (Option<u32>, bool)>,
    /// Its uniform block per program variant (see `CompiledPrograms::material_block`).
    blocks: rustc_hash::FxHashMap<u32, Arc<compiled::MaterialBlock>>,
}

impl GpuMaterial {
    fn new(resolved: ResolvedMaterial) -> Self {
        let (main_tex_handle, main_default) = resolved.texture_ref("_MainTex");
        let main_tex_default = Arc::from(main_default);
        let declares_main_tex = resolved.declares("_MainTex");
        Self {
            resolved: Arc::new(resolved),
            main_tex_handle,
            main_tex_default,
            declares_main_tex,
            tagged_passes: None,
            texture_plans: Default::default(),
            variants: Default::default(),
            blocks: Default::default(),
        }
    }
}

/// How many [`UnityRenderer::repack_texture`] shapes are kept.
const REPACK_TEXTURES_KEPT: usize = 4;

#[derive(Clone)]
struct RepackTexture {
    texture: wgpu::Texture,
    target: wgpu::TextureView,
    alternate: Option<wgpu::TextureView>,
}

impl RepackTexture {
    fn sample(&self, srgb: bool) -> &wgpu::TextureView {
        if self.texture.format().is_srgb() == srgb {
            &self.target
        } else {
            self.alternate
                .as_ref()
                .expect("sampled display format has two views")
        }
    }
}

/// The screen: 8-bit color like the Editor's game view (RGBA, or BGRA where the device stores it
/// so: `DeviceConventions::screen_format`), sRGB-encoded in Linear color space (draws encode,
/// samples decode, readbacks and copies see the stored bytes: measured, tests/projects/rtdepth
/// `target.screen.*.final` and `ctl.*`).
struct ScreenTarget {
    width: u32,
    height: u32,
    texture: wgpu::Texture,
    /// The stored bytes (Gamma color space, GL.sRGBWrite off).
    color: wgpu::TextureView,
    /// The bytes as sRGB-encoded colors (Linear color space).
    srgb: wgpu::TextureView,
    /// The screen's depth: one buffer for every slot of the ring, so it lasts from frame to frame
    /// as the color does.
    depth: wgpu::TextureView,
    /// That buffer's texture.
    depth_texture: wgpu::Texture,
    /// Its depth, as `CopyDepth` samples it.
    depth_sample: wgpu::TextureView,
}

impl ScreenTarget {
    fn view(&self, color_space: i32) -> (&wgpu::TextureView, wgpu::TextureFormat) {
        let format = self.texture.format();
        if color_space == 1 {
            (&self.srgb, format.add_srgb_suffix())
        } else {
            (&self.color, format)
        }
    }
}

/// The global buffer that holds the random write target at a UAV register (its number follows;
/// the managed `Graphics.SetRandomWriteTarget` sets it).
const RANDOM_WRITE_TARGET: &str = "<RandomWriteTarget>";

/// The latest complete frame, for the window.
#[derive(Clone)]
pub struct Presented {
    pub color: wgpu::TextureView,
    pub color_space: i32,
    pub size: (u32, u32),
    pub fixed_game_view: bool,
    /// The screen's rows are stored top-down (see `DeviceConventions::top_down`).
    pub top_down: bool,
    /// Changes with every frame (bind groups compare it).
    pub frame: u64,
    /// Which target of the ring it is.
    pub slot: usize,
}

/// Where draws go.
#[derive(Clone)]
struct Target {
    /// First color attachment (none for a depth-only render target).
    color: Option<wgpu::TextureView>,
    depth_slice: Option<u32>,
    /// A multisampled target's resolve: the texture's own view.
    resolve: Option<wgpu::TextureView>,
    samples: u32,
    depth: Option<wgpu::TextureView>,
    /// The depth buffer's format (with `depth`).
    depth_format: Option<wgpu::TextureFormat>,
    /// First color attachment's format (none for a depth-only render target).
    format: Option<wgpu::TextureFormat>,
    alpha_one: bool,
    width: u32,
    height: u32,
    /// Bound `(layer, mip)` subresource of the primary target (used by `same_target` even for
    /// depth-only targets without a color texture).
    subresource: (u32, u32),
    /// The screen of this frame (first use clears it).
    screen: bool,
    /// Stored as the screen is (the screen, or the render texture standing for it): drawn with
    /// the projection unflipped, its rows top-down where the device stores the screen so.
    screen_like: bool,
    /// Further colors (multiple render targets), at the outputs after the first.
    extra: Vec<ExtraColor>,
    /// The single-sample texture (and layer) holding what is drawn, which copies read.
    texture: Option<(wgpu::Texture, u32, u32)>,
    auto_mips: bool,
    defer_mips: Option<wgpu::TextureFormat>,
    /// The channel bits its texture keeps of what is drawn (`owned::PACKED_*`; 0: none, the
    /// screen's case).
    packed: u8,
}

/// A further color of a target.
#[derive(Clone)]
struct ExtraColor {
    color: wgpu::TextureView,
    depth_slice: Option<u32>,
    resolve: Option<wgpu::TextureView>,
    format: wgpu::TextureFormat,
    alpha_one: bool,
    texture: Option<(wgpu::Texture, u32, u32)>,
    auto_mips: bool,
    defer_mips: Option<wgpu::TextureFormat>,
    packed: u8,
}

impl Target {
    /// The further colors' formats, as pipeline keys carry them.
    fn extra_formats(&self) -> [Option<wgpu::TextureFormat>; 7] {
        let mut formats = [None; 7];
        for (slot, e) in formats.iter_mut().zip(&self.extra) {
            *slot = Some(e.format);
        }
        formats
    }
}

struct PlannedDraw {
    pipeline: wgpu::RenderPipeline,
    vertex: wgpu::Buffer,
    /// Channels 4..7, for programs that read them.
    extra: Option<wgpu::Buffer>,
    index: wgpu::Buffer,
    /// The mesh's 32-bit indices, or a buffer's 16-bit ones (`DrawIndices`).
    index_format: wgpu::IndexFormat,
    range: Range<u32>,
    base_vertex: i32,
    /// The vertex the vertex buffers are bound from, in place of a positive base vertex where
    /// `SV_VertexID` leaves the base vertex out (`DeviceConventions::vertex_id_from_draw_start`).
    first_bound_vertex: u32,
    material: wgpu::BindGroup,
    /// Group 2: legacy main-texture bind group, only populated when the program reads it.
    texture: Option<wgpu::BindGroup>,
    /// The program's buffers (group 3), when it reads any.
    buffers: Option<wgpu::BindGroup>,
    offset: u32,
    stencil_ref: u32,
    viewport: [f32; 4],
    scissor: [u32; 4],
    /// The compiled program: its id and the frame-buffer offsets of its uniform bindings.
    program: (u32, compiled::DrawOffsets),
    /// Instances drawn (SV_InstanceID 0..n).
    instances: u32,
    first_instance: u32,
    /// Counts read on the GPU: the arguments buffer, its offset, whether indexed.
    indirect: Option<(wgpu::Buffer, u64, bool)>,
    /// A BatchRendererGroup draw's instance data window, copied into its uniform block before
    /// the draw (uniform buffer mode, [`super::dots::window`]).
    window: Option<WindowCopy>,
}

/// What a draw plans, in its passes' order.
enum Planned {
    Draw(PlannedDraw),
    /// A `GrabPass`: the target copied into the texture, which the passes after it read.
    Grab(Copy),
    /// A texture's values converted for the pass after it (integer bits), after any grab
    /// before that pass.
    Convert(DepthCopy),
}

enum PassItem {
    /// A clear drawn over the viewport: pipeline, uniform offset, viewport.
    Clear(wgpu::RenderPipeline, u32, [f32; 4]),
    Draw(PlannedDraw),
}

/// A texture-to-texture copy.
struct Copy {
    source: wgpu::Texture,
    source_mip: u32,
    source_origin: wgpu::Origin3d,
    destination: wgpu::Texture,
    destination_mip: u32,
    destination_origin: wgpu::Origin3d,
    size: wgpu::Extent3d,
    /// Set when the color is drawn into the destination storage before copying it.
    channels: Option<ChannelCopy>,
}

impl Copy {
    /// Encodes the copy of texels from its source, which needs no repacking.
    fn encode(&self, encoder: &mut wgpu::CommandEncoder) {
        self.encode_from(encoder, (&self.source, self.source_mip, self.source_origin));
    }

    /// Encodes the copy of texels from `source` (its own, or its texels repacked), its level and
    /// origin.
    fn encode_from(
        &self,
        encoder: &mut wgpu::CommandEncoder,
        (source, mip_level, origin): (&wgpu::Texture, u32, wgpu::Origin3d),
    ) {
        encoder.copy_texture_to_texture(
            wgpu::TexelCopyTextureInfo {
                texture: source,
                mip_level,
                origin,
                aspect: wgpu::TextureAspect::All,
            },
            wgpu::TexelCopyTextureInfo {
                texture: &self.destination,
                mip_level: self.destination_mip,
                origin: self.destination_origin,
                aspect: wgpu::TextureAspect::All,
            },
            self.size,
        );
    }
}

/// A texture copied texel for texel into another of its size by a draw: a render target's depth
/// into its float color copy (what sampling it reads), or a float texture's values as integer
/// bits (what an integer slot reads of it).
struct DepthCopy {
    depth: wgpu::TextureView,
    target: wgpu::TextureView,
    kind: CopyKind,
}

/// What a [`DepthCopy`] reads and writes.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum CopyKind {
    /// A depth buffer into floats.
    Depth,
    /// A multisampled depth buffer's sample `DeviceConventions::depth_resolve_sample` into
    /// floats.
    DepthMultisampled,
    /// Depth into normalized 16-bit storage, preserving D16 interpolation precision.
    Depth16,
    Depth16Multisampled,
    /// Floats as unsigned integer bits.
    UintBits,
    /// Floats as signed integer bits.
    SintBits,
    /// 8-bit normalized texels into one channel and into four ([`ChannelCopy`]); four
    /// channels stored blue first ([`ChannelCopy`] between the screen and a texture).
    ToR8,
    ToRgba8,
    ToBgra8,
    /// RGBA8 texels with red and blue traded: the stored bytes of a texture stored blue first
    /// copied into one stored red first, or back (`DeviceConventions::bgra_copies_bytes`).
    SwapRedBlue,
    /// An RGBA8 target's texels as a 5-6-5, 5-5-5-1 or 4-4-4-4 target keeps them
    /// (`PACKED_TEXELS_WGSL`: the Editor's devices keep a value drawn into such a target at its
    /// channels' bits, measured by tests/projects/samplerstates Packed.*; Unbound stores RGBA8
    /// and packs a level drawn into before anything reads it, `mark_packed`).
    Packed565,
    Packed5551,
    Packed4444,
}

// The texture protocol supports 1, 2, 4 and 8 samples.
const COPY_SAMPLE_CLASSES: usize = 4;

impl CopyKind {
    const COUNT: usize = 13;

    fn index(self) -> usize {
        self as usize
    }
}

/// How a [`Copy`] between R8 textures kept in different 8-bit storages repacks the texels:
/// R8_UNorm is kept in one channel, R8_SRGB (sRGB in a linear project) as RGBA8. The source level
/// is drawn into a texture of the destination's storage, which is then copied. The Unity
/// 6000.0.71f1 Editor copies between R8_UNorm and R8_SRGB on the GPU, but leaves the GPU texels of
/// R8G8_UNorm and R8G8_SRGB copies as they were (tests/projects/texturecompression
/// channels.copy.*), so RG8 and RGBA8 storages are not joined.
struct ChannelCopy {
    /// The source's level (and slice), viewed as stored.
    source: wgpu::TextureView,
    size: wgpu::Extent3d,
    transfer: ChannelTransfer,
}

enum ChannelTransfer {
    Storage(CopyKind),
    Framebuffer {
        srgb: bool,
        display: Option<FramebufferDisplay>,
        destination_format: wgpu::TextureFormat,
    },
}

#[derive(Clone, Copy)]
struct FramebufferDisplay {
    format: wgpu::TextureFormat,
    resize_uniform: Option<u32>,
}

#[derive(Clone, Copy, PartialEq, Eq, Hash)]
enum FramebufferTransfer {
    LinearToDisplay,
    ResizeToDisplay,
    SrgbToTexture,
    UnormToTexture,
}

/// The [`ChannelCopy`] kind from `source` storage into `destination` storage: between R8 and
/// RGBA8, and (`screen_order`: one end is the screen stored blue first, or a grab of it) between
/// BGRA8 and RGBA8, keeping each channel's stored value as copies between the screen and render
/// textures did where both were RGBA8 (a camera seeded from the screen,
/// `CaptureScreenshotIntoRenderTexture`, a script's `CopyTexture` of `CameraTarget`; the batch-mode
/// macOS Editor copies nothing there, tests/projects/nativegfx `residuals-metal.json`). Textures
/// stored blue first elsewhere (a plugin's) keep their exact-storage rule (`raw_copy_compatible`).
fn channel_copy_kind(
    source: wgpu::TextureFormat,
    destination: wgpu::TextureFormat,
    screen_order: bool,
) -> Option<CopyKind> {
    use wgpu::TextureFormat as F;
    // Repacking reads and writes raw channel values. Physical sRGB storage need not permit
    // a linear view, and unlike equal-format copies a shader pass would convert its bytes.
    match (source, destination) {
        (F::Rgba8Unorm, F::R8Unorm) => Some(CopyKind::ToR8),
        (F::R8Unorm, F::Rgba8Unorm) => Some(CopyKind::ToRgba8),
        (F::Bgra8Unorm, F::Rgba8Unorm) if screen_order => Some(CopyKind::ToRgba8),
        (F::Bgra8Unorm, F::R8Unorm) if screen_order => Some(CopyKind::ToR8),
        (F::Rgba8Unorm, F::Bgra8Unorm) if screen_order => Some(CopyKind::ToBgra8),
        _ => None,
    }
}

/// The [`ChannelCopy`] kind of a copy that trades red and blue (one end stored blue first, the
/// other red first): between two RGBA8 storages, sRGB or not, the bytes copied as they are.
fn swapped_copy_kind(
    source: wgpu::TextureFormat,
    destination: wgpu::TextureFormat,
) -> Option<CopyKind> {
    use wgpu::TextureFormat as F;
    (source.remove_srgb_suffix() == F::Rgba8Unorm
        && destination.remove_srgb_suffix() == F::Rgba8Unorm)
        .then_some(CopyKind::SwapRedBlue)
}

/// Whether two formats keep their texels alike (they may differ in sRGB decoding): a grab
/// texture is kept for a source of the same storage only, BGRA8 and RGBA8 being two.
fn same_storage(a: wgpu::TextureFormat, b: wgpu::TextureFormat) -> bool {
    a.remove_srgb_suffix() == b.remove_srgb_suffix()
}

/// The handles of `GrabPass` and framebuffer fetch textures: one per name, 8-bit channel order
/// and whether the source is multisampled, so a name grabbed from the blue-first screen and from
/// RGBA8 targets, or fetched from multisampled and single-sampled targets, keeps a texture for
/// each (copies between the two storages or sample counts are invalid) instead of remaking one
/// at every switch. Handles count down from `handle::PLAIN_MAX`.
#[derive(Default)]
struct GrabSlots {
    by_name: HashMap<String, [Option<u64>; 4]>,
    count: u64,
}

impl GrabSlots {
    /// The handle of `name`'s texture for a source stored in `format` with `samples` samples.
    fn handle(&mut self, name: &str, format: wgpu::TextureFormat, samples: u32) -> u64 {
        let order = (format.remove_srgb_suffix() == wgpu::TextureFormat::Bgra8Unorm) as usize;
        let slot = order + 2 * (samples > 1) as usize;
        if let Some(handle) = self.by_name.get(name).and_then(|slots| slots[slot]) {
            return handle;
        }
        let handle = handle::PLAIN_MAX - self.count;
        self.count += 1;
        self.by_name.entry(name.to_string()).or_default()[slot] = Some(handle);
        handle
    }

    fn contains(&self, handle: u64) -> bool {
        handle <= handle::PLAIN_MAX && handle > handle::PLAIN_MAX - self.count
    }
}

/// Vulkan BGRA imports currently support identical physical storage. Cross-order and
/// cross-encoding copies need a conversion path; the other backends keep their existing
/// compatible-format rule.
fn raw_copy_compatible(
    source: wgpu::TextureFormat,
    destination: wgpu::TextureFormat,
    exact_bgra: bool,
) -> bool {
    use wgpu::TextureFormat as F;
    if exact_bgra
        && (matches!(source, F::Bgra8Unorm | F::Bgra8UnormSrgb)
            || matches!(destination, F::Bgra8Unorm | F::Bgra8UnormSrgb))
    {
        source == destination
    } else {
        source.remove_srgb_suffix() == destination.remove_srgb_suffix()
    }
}

/// A batch buffer's window copied on the GPU into a draw's uniform block, after the work before
/// the draw: the instance data a BatchRendererGroup draw reads in uniform buffer mode, whoever
/// wrote it (SetData, a compute kernel, a copy).
struct WindowCopy {
    source: wgpu::Buffer,
    source_offset: u64,
    /// The byte offset in the frame's uniform buffer ([`compiled::CompiledPrograms::uniform_buffer`]).
    offset: u64,
    size: u64,
}

impl WindowCopy {
    fn encode(&self, encoder: &mut wgpu::CommandEncoder, uniforms: Option<&wgpu::Buffer>) {
        if let Some(uniforms) = uniforms {
            encoder.copy_buffer_to_buffer(
                &self.source,
                self.source_offset,
                uniforms,
                self.offset,
                self.size,
            );
        }
    }
}

/// The work of a submission, in order.
enum Op {
    Pass(Pass),
    Copy(Copy),
    DepthCopy(DepthCopy),
    DepthBlit(DepthBlit),
    Dispatch(compute::DispatchOp),
    BufferCopy(compute::BufferCopyOp),
    /// An indirect draw's arguments copied into the pooled arguments buffer before the draw,
    /// where nothing earlier in the open pass could have written them.
    IndirectArgs(ArgsCopy),
    /// A draw's instance data window, where it could not be hoisted before the draw's pass.
    Window(WindowCopy),
    Mips(wgpu::Texture, wgpu::TextureFormat, u32),
}

/// An indirect draw's arguments copied on the GPU, after the work before the draw, into its
/// slot of [`IndirectArgsPool`] with the start instance zeroed: the Editor's OpenGLCore
/// `SV_InstanceID` counts from 0 on every indirect path (GL's `gl_InstanceID` never includes the
/// base instance; measured: tests/projects/cmdbuffers `render.indexed.startinstance.*` and
/// `render.indexed.indirect.rawids`), while wgpu's instance index includes it. The caller's
/// buffer keeps its arguments for the shader's `unity_IndirectDrawArgs` reads
/// (UnityIndirect.cginc's `GetIndirectInstanceID_Base` adds the start instance itself). Where
/// `SV_VertexID` counts from the draw's first vertex
/// (`DeviceConventions::vertex_id_from_draw_start`) the base vertex (indexed) or start vertex is
/// zeroed too for programs that read no vertex inputs, which it then moves nothing else for.
struct ArgsCopy {
    source: wgpu::Buffer,
    source_offset: u64,
    destination: wgpu::Buffer,
    destination_offset: u64,
    indexed: bool,
    zero_first_vertex: bool,
}

impl ArgsCopy {
    fn encode(&self, encoder: &mut wgpu::CommandEncoder) {
        // Indexed: index count, instance count, first index, base vertex, start instance;
        // not: vertex count, instance count, first vertex, start instance.
        let counts = if self.indexed { 16 } else { 12 };
        encoder.copy_buffer_to_buffer(
            &self.source,
            self.source_offset,
            &self.destination,
            self.destination_offset,
            counts,
        );
        encoder.clear_buffer(
            &self.destination,
            self.destination_offset + counts,
            Some(INDIRECT_START_INSTANCE_SIZE),
        );
        if self.zero_first_vertex {
            // The base vertex (indexed) or start vertex is the word before the start instance.
            encoder.clear_buffer(
                &self.destination,
                self.destination_offset + counts - INDIRECT_FIRST_VERTEX_SIZE,
                Some(INDIRECT_FIRST_VERTEX_SIZE),
            );
        }
    }
}

/// The base vertex and first bound vertex of a direct draw whose `SV_VertexID` must read the
/// index alone (`DeviceConventions::vertex_id_from_draw_start`; a non-indexed draw's start vertex
/// is its shape's base vertex here): a program that reads no vertex inputs draws without the
/// base vertex, which moves nothing else for it; one that does binds its vertex buffers from a
/// positive base vertex inside them (`vertices`), the same vertices with ids from the index. A
/// negative base vertex, or one past the buffers, stays added to the id (unmeasured).
fn first_vertex_binding(base_vertex: i32, vertex_inputs: bool, vertices: u64) -> (i32, u32) {
    match base_vertex {
        0 => (0, 0),
        _ if !vertex_inputs => (0, 0),
        b if b > 0 && (b as u64) < vertices => (0, b as u32),
        b => (b, 0),
    }
}

/// The bytes of an indirect draw's base vertex (indexed) or start vertex argument.
const INDIRECT_FIRST_VERTEX_SIZE: u64 = 4;

/// The bytes of an indirect draw's start instance argument.
const INDIRECT_START_INSTANCE_SIZE: u64 = 4;
/// One slot of [`IndirectArgsPool`]: the indexed arguments' 20 bytes (the others use 16).
const INDIRECT_ARGS_SLOT: u64 = 20;

/// The arguments buffer every indirect draw of a submission draws from, one slot each, grown by
/// doubling; slots are reused by the next submission, which the queue runs after this one.
#[derive(Default)]
struct IndirectArgsPool {
    buffer: Option<wgpu::Buffer>,
    used: u64,
}

impl IndirectArgsPool {
    /// The next slot: its buffer and byte offset.
    fn slot(&mut self, device: &wgpu::Device) -> (wgpu::Buffer, u64) {
        let offset = self.used * INDIRECT_ARGS_SLOT;
        if self
            .buffer
            .as_ref()
            .is_none_or(|b| b.size() < offset + INDIRECT_ARGS_SLOT)
        {
            // Draws planned before keep the smaller buffer they copied into.
            let slots = (self.used + 1).next_power_of_two().max(64);
            self.buffer = Some(device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound indirect args"),
                size: slots * INDIRECT_ARGS_SLOT,
                usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::INDIRECT,
                mapped_at_creation: false,
            }));
            self.used = 0;
        }
        let offset = self.used * INDIRECT_ARGS_SLOT;
        self.used += 1;
        (self.buffer.clone().unwrap(), offset)
    }
}

/// A depth buffer drawn into another of its size (`CopyDepth`): each destination sample takes the
/// source's sample of its index (equal sample counts) or its only or first one.
struct DepthBlit {
    #[cfg(target_os = "linux")]
    native: Option<plugins_vulkan::DepthStencilResolve>,
    #[cfg(target_os = "linux")]
    native_after: Option<plugins_vulkan::DepthStencilResolve>,
    resolve_sample: u32,
    group: Option<wgpu::BindGroup>,
    source: wgpu::TextureView,
    /// The source's sample count (1: not multisampled).
    source_samples: u32,
    destination: wgpu::TextureView,
    /// The destination depth buffer's format.
    format: wgpu::TextureFormat,
    samples: u32,
    /// The rows are read mirrored: exactly one end is stored as the screen, whose rows a
    /// top-down device stores the other way, so the depth lands in the other end's row order
    /// (unlike `CopyTexture`, which copies the rows as stored and only counts a region's rows
    /// from the screen's top).
    flip: bool,
}

/// A render pass being built: consecutive commands on one target.
struct Pass {
    native_depth: Option<DepthBlit>,
    target: Target,
    /// The previous frame's screen, copied into the frame's screen before the pass when it loads
    /// its color (the frame's first use of the screen).
    seed: Option<wgpu::Texture>,
    color_load: wgpu::LoadOp<wgpu::Color>,
    /// Depth and stencil clear values; `None` loads them.
    depth_clear: Option<(f32, u32)>,
    items: Vec<PassItem>,
    /// Indirect arguments copied before the pass begins (see [`ArgsCopy`]).
    args: Vec<ArgsCopy>,
    /// Instance data windows copied before the pass begins (see [`WindowCopy`]).
    windows: Vec<WindowCopy>,
    /// A draw in the pass binds a buffer it can write: later indirect arguments may depend on
    /// it, so they are copied after the pass instead.
    writes_buffers: bool,
}

/// The state commands set for those after them.
struct State {
    target: Option<Target>,
    viewport: [f32; 4],
    view: [f32; 16],
    projection: [f32; 16],
    /// The matrices of the last camera setup ([`commands::camera::PROPERTIES`]), for the camera
    /// built-ins; none yet: the current ones.
    camera: Option<([f32; 16], [f32; 16])>,
    overlay: bool,
    /// Front and back culling swap (`commands::camera::INVERT_CULLING`).
    invert_culling: bool,
    /// Triangles draw as lines (`commands::camera::WIREFRAME`).
    wireframe: bool,
    /// sRGB targets bound from now on store colors unconverted (`NO_SRGB_WRITE`).
    no_srgb_write: bool,
    /// The OpenGL Core window framebuffer can be sRGB even in a Gamma project.
    gamma_framebuffer_srgb: bool,
    /// The global depth bias: units, slope factor (`CommandBuffer.SetGlobalDepthBias`).
    depth_bias: [f32; 2],
    gizmo_draw: bool,
    /// A renderer list's render state overrides (`RenderStateBlock`).
    state_override: Option<commands::StateOverride>,
    /// How the next draw runs (`DrawSetup`): instances, arguments buffer, offset, indexed.
    draw_setup: Option<(u32, u64, u32, bool)>,
    instance_multiplier: u32,
    /// The next draw is this instance of a mesh-instancing call (`MeshInstance`).
    mesh_instance: Option<MeshInstanceSlot>,
    /// The next draw is a BatchRendererGroup draw command's (`DotsDraw`).
    dots: Option<commands::DotsDraw>,
    /// The next draw's indices from a buffer (`DrawIndices`): buffer, index size, first, count.
    draw_indices: Option<(u64, u8, u32, u32)>,
    /// How the next draw reads its indices (`DrawShape`).
    draw_shape: Option<DrawShape>,
}

/// A draw's place in a mesh-instancing call replayed as one draw per instance.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
struct MeshInstanceSlot {
    /// Its logical instance ID, and the element of the draw's arrays its scalar uniforms take.
    index: u32,
    /// The call's instance count.
    count: u32,
    /// The instances an instance multiplier adds past the call's count, IDs `index` to
    /// `index + added - 1`, drawn as one draw without instance data
    /// ([`UnityRenderer::multiplied_instances`]); 0: one of the call's own instances.
    added: u32,
    /// The added instances' winding: the call's last instance's (their matrix is zero).
    mirrored: bool,
}

/// How a draw's instances get their data, which selects its variant: none of their own, a
/// replayed mesh-instancing call's arrays, or a BatchRendererGroup draw's DOTS instancing data.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
enum InstancingMode {
    None,
    MeshReplay,
    Dots,
}

/// The keyword a BatchRendererGroup draw compiles its material's variant with.
const DOTS_INSTANCING_KEYWORD: &str = "DOTS_INSTANCING_ON";
/// The keyword of the instancing variant (GPU instancing, mesh-instance replays).
const INSTANCING_KEYWORD: &str = "INSTANCING_ON";
/// The renderer constant a sprite shape's color reaches programs without `unity_SpriteColor` by.
const RENDERER_COLOR: &str = "_RendererColor";
/// The draw property (and byte address buffer) of a BatchRendererGroup batch's instance data.
const DOTS_INSTANCE_DATA: &str = "unity_DOTSInstanceData";

/// A dynamically scaled render texture's full-size allocation, kept while it is scaled, with what
/// the renderer holds beside it: its pending automatic mips and its retained resolve.
struct FullResolution {
    gpu: GpuTexture,
    mips: Option<wgpu::TextureFormat>,
    resolve: Option<msaa::ResolveHistory>,
}

pub struct UnityRenderer {
    /// The adapter the device was opened on (its formats as the backend stores them).
    adapter: wgpu::Adapter,
    /// Native plugins' shared textures and callbacks (`plugins`).
    plugins: plugins::PluginState,
    /// The graphics device's projection, depth and target orientation (resolved at startup).
    conventions: DeviceConventions,
    /// Whether a comparison binding may read a depth texture's stored depth where the texture's
    /// own state does not compare ([`resources::stored_depth_reads`]).
    stored_depth_reads: bool,
    /// True when the active backend is desktop OpenGL/EGL (enables scoped EGL context pinning).
    is_gles: bool,
    /// The Vulkan device's plugin interface (None: plugins see OpenGL Core's, `plugins`).
    #[cfg(target_os = "linux")]
    vulkan_plugins: Option<plugins_vulkan::VulkanPlugins>,
    /// The Metal device's plugin interface (`plugins_metal`).
    #[cfg(target_os = "macos")]
    metal_plugins: Option<plugins_metal::MetalPlugins>,
    /// The Editor's Metal encoder over the current target exists: something was drawn into it
    /// since it was set, or a plugin's encoder was left begun, and no plugin ended it since
    /// (`plugins_metal`).
    #[cfg(target_os = "macos")]
    plugin_target_drawn: bool,
    /// Where a Metal plugin's next encoder goes: the current target until a plugin ends the
    /// encoder, then the screen until another target is set or drawn into (`plugins_metal`).
    #[cfg(target_os = "macos")]
    plugin_redirect: plugins_metal::Redirect,
    /// The last frame (counted from 1) whose GPU work is done: plugins' `safeFrameNumber`.
    frames_completed: Arc<std::sync::atomic::AtomicU64>,
    /// The current target's pass was drawing at a plugin event, or a plugin event ran inside it
    /// (until another command): where a Vulkan plugin event that does not care runs.
    plugin_pass_open: bool,
    fence_issued: u64,
    fence_completed: Arc<std::sync::atomic::AtomicU64>,
    device: wgpu::Device,
    queue: wgpu::Queue,
    resolver: Resolver,
    pipelines: Pipelines,
    mipmaps: mipmaps::Mipmaps,
    pending_mips: HashMap<wgpu::Texture, wgpu::TextureFormat>,
    /// Packed targets drawn into since their texels were last packed (`mark_packed`): by
    /// texture, mip and layer, their size and kind.
    pending_packed: HashMap<(wgpu::Texture, u32, u32), (wgpu::Extent3d, CopyKind)>,
    resolve_history: HashMap<wgpu::Texture, msaa::ResolveHistory>,
    resolve_targets: msaa::ResolveTargets,
    readback: readback::Staging,
    framebuffer_dirty: bool,
    meshes: HashMap<u64, GpuMesh>,
    mesh_buffers: HashMap<(u64, u8), mesh_buffers::Binding>,
    /// Zeros standing in for channels 4..7 of meshes without them.
    zero_extra: Option<wgpu::Buffer>,
    textures: HashMap<u64, GpuTexture>,
    /// The full-size allocations of dynamically scaled render textures while they are scaled,
    /// taken again back at full size: the Manual (6000.0, Dynamic resolution) says render
    /// targets are not reallocated, a scaled one using a portion of the full-resolution one;
    /// measured: a full-size image is whole again (tests/projects/nativegfx `dynres.content.*`).
    /// Scaled images are textures of their own here.
    full_resolution: HashMap<u64, FullResolution>,
    texture_mip_biases: HashMap<u64, f32>,
    defaults: HashMap<&'static str, GpuTexture>,
    /// Default textures of other dimensions (cube, 3D, array): views by (dimension, default).
    dim_defaults: HashMap<(unbound_shaders::TextureDim, String), wgpu::TextureView>,
    /// The error shader's program, compiled on first need.
    error_program: Option<unbound_shaders::Program>,
    /// Far-plane depth textures (2D, cube) that unbound depth slots read.
    depth_default: [Option<wgpu::TextureView>; 2],
    /// The render target with a depth copy being drawn into (a depth-only one, or one whose depth
    /// element draws sample), whose depth is copied for sampling when drawing moves on.
    depth_target: Option<u64>,
    /// How many ops this executor has encoded: with the passes not encoded yet, the position
    /// that marks where drawing into the depth target started across mid-frame submissions.
    encoded_ops: usize,
    /// Whether the dispatch being planned samples the depth target's depth copy (a depth-only
    /// render texture read through a float slot), which must be brought up to date before it.
    dispatch_reads_depth_target: bool,
    /// Depth copies the dispatch being planned made four-channel (`fill_depth_copy`): copied
    /// right before it.
    dispatch_fills: Vec<u64>,
    /// [`Self::repack_texture`]'s textures and views, oldest first.
    repack_textures: Vec<RepackTexture>,
    framebuffer_resolve_layout: Option<wgpu::BindGroupLayout>,
    framebuffer_resolve_pipelines:
        HashMap<(wgpu::TextureFormat, FramebufferTransfer), wgpu::RenderPipeline>,
    framebuffer_resolve_groups: Vec<(wgpu::TextureView, bool, wgpu::BindGroup)>,
    /// The pipelines (and their layouts) that copy a depth buffer into a float color texture:
    /// single-sample and multisampled.
    depth_copy_pipelines: [Option<(wgpu::RenderPipeline, wgpu::BindGroupLayout)>;
        CopyKind::COUNT * COPY_SAMPLE_CLASSES * 2],
    /// The `CopyDepth` pipelines, by (source multisampled, destination samples, per sample,
    /// format, resolve sample, rows mirrored).
    depth_blit_pipelines: HashMap<
        (bool, u32, bool, wgpu::TextureFormat, u32, bool),
        (wgpu::RenderPipeline, wgpu::BindGroupLayout),
    >,
    /// Integer copies of float textures that integer slots read, by texture and signedness.
    bit_views: HashMap<(u64, bool), (u32, u32, wgpu::TextureView)>,
    /// Main-texture bind groups of render targets' depth elements, by texture and color space,
    /// with the texture's generation they were made at (apart from its color's).
    depth_main_groups: HashMap<(u64, usize), (u64, wgpu::BindGroup)>,
    /// Copies the pass being planned needs before it (integer copies of its textures).
    pre_draw: Vec<DepthCopy>,
    /// Copies of the textures the draw being planned samples while it draws into them
    /// ([`Self::feedback_view`]), made before it.
    pre_copies: Vec<Copy>,
    /// The copy each such texture is sampled through, by handle.
    feedback_copies: HashMap<u64, wgpu::Texture>,
    /// Render targets whose depth element the draw being planned samples, and whether their depth
    /// copy must be brought up to date before it (made just now, or the current target's).
    depth_reads: HashMap<u64, bool>,
    /// The current target's color and depth handles (`SetTarget`).
    target_handles: (u64, u64),
    /// The render texture standing for the screen while a screen camera draws (0: none).
    screen_stand_in: u64,
    /// Integer-texel slots' defaults (zeros): signed, unsigned.
    integer_defaults: [Option<wgpu::TextureView>; 2],
    /// [`Self::integer_array_default`]'s gray, bits and array view, by (signed, Linear color
    /// space).
    integer_array_defaults:
        HashMap<(bool, bool), (wgpu::TextureView, wgpu::TextureView, wgpu::TextureView)>,
    samplers: HashMap<SamplerKey, wgpu::Sampler>,
    materials: HashMap<u64, GpuMaterial>,
    buffers: HashMap<u64, compute::GpuBuffer>,
    /// Bound for a buffer a draw reads that nothing set: zeros.
    empty_storage: Option<wgpu::Buffer>,
    kernels: compute::KernelCache,
    compute_bindings: compute::BindingCache,
    /// Cached program texture bind groups (group 1), keyed by program, color space, and resolved
    /// slot handles/generations/defaults; evicted on texture/sampler/anisotropy changes and
    /// retired after idle frames.
    program_texture_groups:
        bevy::platform::collections::HashMap<TextureGroupKey, (u64, wgpu::BindGroup)>,
    /// Cached program storage-buffer bind groups (group 3), keyed by program and resolved buffer
    /// handles; evicted on buffer release/recreation and retired after idle frames.
    program_buffer_groups:
        bevy::platform::collections::HashMap<BufferGroupKey, (u64, wgpu::BindGroup)>,
    /// Reusable scratch buffers for zero-allocation bind group cache lookups and draw planning.
    texture_slot_scratch: Vec<TextureSlotKey>,
    texture_override_scratch: Vec<usize>,
    buffer_slot_scratch: Vec<BufferSlotKey>,
    planned_scratch: Vec<Planned>,
    pass_indices_scratch: Vec<(usize, bool)>,
    /// The pass writing a counter at a byte offset that is not a multiple of 4.
    unaligned_count: Option<(wgpu::ComputePipeline, wgpu::BindGroupLayout)>,
    generation: u64,
    draw_buffer: Option<wgpu::Buffer>,
    /// Monotonic generation incremented whenever `draw_buffer` is allocated or resized.
    draw_buffer_gen: u64,
    indirect_args: IndirectArgsPool,
    draw_bind_group: Option<wgpu::BindGroup>,
    uniforms: Vec<DrawUniform>,
    /// The screen's ring of targets and the one this frame draws into.
    screens: [Option<ScreenTarget>; 3],
    screen_slot: usize,
    screen_size: (u32, u32),
    /// The screen has been drawn into this frame.
    screen_touched: bool,
    frame_number: u64,
    color_space: i32,
    /// QualitySettings.anisotropicFiltering: mode (0 off, 1 per texture, 2 forced), forced
    /// minimum, maximum level.
    anisotropy: [i32; 3],
    /// Global shader properties and keywords (`Shader.SetGlobal*`, `Shader.EnableKeyword`).
    globals: Arc<Vec<(String, owned::PropertyValue)>>,
    /// Where each global property is (rebuilt when the list grows or shrinks).
    globals_index: owned::NameIndex,
    /// Monotonic revision incremented whenever `globals` changes.
    globals_revision: u64,
    keywords: Vec<String>,
    /// An id of the global keyword set (equal sets, equal ids): what variants are cached by.
    keyword_set: u32,
    keyword_sets: rustc_hash::FxHashMap<Vec<String>, u32>,
    /// The `LightMode` tags of tagged-pass draws (renderer lists) and their monotonic generation.
    pass_tags: Vec<String>,
    pass_tags_gen: u64,
    state: State,
    presented: Arc<Mutex<Option<Presented>>>,
    fixed_game_view: bool,
    started: Instant,
    warned: HashSet<String>,
    /// Whether the instances an instance multiplier adds to mesh instancing were warned of
    /// (`multiplied_instances`), and those it does not add past a native batch.
    multiplied_instances_warned: bool,
    multiplied_past_batch_warned: bool,
    traced_frames: u64,
    compiled: compiled::CompiledPrograms,
    /// `GrabPass` textures by name and storage, and the named ones grabbed in this camera's
    /// render.
    grabs: GrabSlots,
    grabbed: HashSet<String>,
}

impl UnityRenderer {
    pub fn new(
        adapter: wgpu::Adapter,
        device: wgpu::Device,
        queue: wgpu::Queue,
        resolver: Resolver,
        compiled: compiled::CompiledPrograms,
        presented: Arc<Mutex<Option<Presented>>>,
        conventions: DeviceConventions,
    ) -> Self {
        let adapter_info = adapter.get_info();
        let pipelines = Pipelines::new(&device);
        let defaults = resources::default_textures(&device, &queue);
        UnityRenderer {
            plugins: Default::default(),
            #[cfg(target_os = "linux")]
            vulkan_plugins: (unbound_runtime::graphics_device::get()
                == unbound_compat::GraphicsDevice::Vulkan)
                .then(|| plugins_vulkan::VulkanPlugins::new(&device, &queue))
                .flatten(),
            #[cfg(target_os = "macos")]
            plugin_target_drawn: false,
            #[cfg(target_os = "macos")]
            plugin_redirect: Default::default(),
            #[cfg(target_os = "macos")]
            metal_plugins: (unbound_runtime::graphics_device::get()
                == unbound_compat::GraphicsDevice::Metal)
                .then(|| plugins_metal::MetalPlugins::new(&device, &queue))
                .flatten(),
            frames_completed: Default::default(),
            plugin_pass_open: false,
            conventions,
            stored_depth_reads: resources::stored_depth_reads(&adapter_info),
            is_gles: adapter_info.backend == wgpu::Backend::Gl,
            adapter,
            device,
            queue,
            resolver,
            pipelines,
            fence_issued: 0,
            fence_completed: Arc::new(std::sync::atomic::AtomicU64::new(0)),
            mipmaps: Default::default(),
            pending_mips: Default::default(),
            pending_packed: Default::default(),
            resolve_history: Default::default(),
            resolve_targets: Default::default(),
            readback: Default::default(),
            framebuffer_dirty: true,
            meshes: HashMap::default(),
            mesh_buffers: HashMap::default(),
            zero_extra: None,
            textures: HashMap::default(),
            texture_mip_biases: HashMap::default(),
            defaults: defaults.into_iter().collect(),
            dim_defaults: HashMap::default(),
            error_program: None,
            depth_default: [None, None],
            depth_target: None,
            encoded_ops: 0,
            dispatch_reads_depth_target: false,
            dispatch_fills: Vec::new(),
            repack_textures: Vec::new(),
            framebuffer_resolve_layout: None,
            framebuffer_resolve_pipelines: Default::default(),
            framebuffer_resolve_groups: Vec::new(),
            depth_copy_pipelines: std::array::from_fn(|_| None),
            depth_blit_pipelines: HashMap::default(),
            bit_views: HashMap::default(),
            depth_main_groups: HashMap::default(),
            pre_draw: Vec::new(),
            pre_copies: Vec::new(),
            feedback_copies: HashMap::default(),
            full_resolution: HashMap::default(),
            depth_reads: HashMap::default(),
            target_handles: (0, 0),
            screen_stand_in: 0,
            integer_defaults: [None, None],
            integer_array_defaults: HashMap::default(),
            samplers: HashMap::default(),
            materials: HashMap::default(),
            buffers: HashMap::default(),
            empty_storage: None,
            kernels: Default::default(),
            compute_bindings: Default::default(),
            program_texture_groups: Default::default(),
            program_buffer_groups: Default::default(),
            texture_slot_scratch: Vec::new(),
            texture_override_scratch: Vec::new(),
            buffer_slot_scratch: Vec::new(),
            planned_scratch: Vec::new(),
            pass_indices_scratch: Vec::new(),
            unaligned_count: None,
            generation: 1,
            draw_buffer: None,
            draw_buffer_gen: 0,
            indirect_args: IndirectArgsPool::default(),
            draw_bind_group: None,
            uniforms: Vec::new(),
            screens: [None, None, None],
            screen_slot: 0,
            screen_size: (1, 1),
            screen_touched: false,
            frame_number: 0,
            color_space: 0,
            anisotropy: [1, 9, 16],
            globals: Arc::new(Vec::new()),
            globals_index: Default::default(),
            globals_revision: 0,
            keywords: Vec::new(),
            keyword_set: 0,
            keyword_sets: Default::default(),
            pass_tags: Vec::new(),
            pass_tags_gen: 1,
            state: State {
                target: None,
                viewport: [0.0; 4],
                view: IDENTITY,
                projection: IDENTITY,
                camera: None,
                overlay: false,
                invert_culling: false,
                wireframe: false,
                no_srgb_write: false,
                gamma_framebuffer_srgb: false,
                depth_bias: [0.0; 2],
                gizmo_draw: false,
                state_override: None,
                draw_setup: None,
                instance_multiplier: 1,
                mesh_instance: None,
                dots: None,
                draw_indices: None,
                draw_shape: None,
            },
            presented,
            fixed_game_view: false,
            started: Instant::now(),
            warned: HashSet::default(),
            multiplied_instances_warned: false,
            multiplied_past_batch_warned: false,
            traced_frames: 0,
            grabs: GrabSlots::default(),
            grabbed: HashSet::default(),
            compiled,
        }
    }

    /// A buffer of at least `size` zero bytes, for channels 4..7 of meshes without them.
    fn zero_extra(&mut self, size: u64) -> wgpu::Buffer {
        let size = size.max(size_of::<resources::ExtraVertex>() as u64);
        if let Some(b) = &self.zero_extra
            && b.size() >= size
        {
            return b.clone();
        }
        let buffer = self.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("unbound zero extra vertices"),
            size: size.next_power_of_two(),
            usage: wgpu::BufferUsages::VERTEX,
            mapped_at_creation: false,
        });
        self.zero_extra = Some(buffer.clone());
        buffer
    }

    /// Puts a texture's new storage in place of `previous` (its texture and sampler): what
    /// referred to the previous texture is released, the new one's mips and resolve history
    /// prepared. `packet` describes it.
    fn install_texture(
        &mut self,
        handle: u64,
        mut gpu: GpuTexture,
        previous: Option<(wgpu::Texture, SamplerKey)>,
        packet: &owned::Texture,
    ) {
        let device = self.device.clone();
        if let Some((previous, sampler)) = &previous {
            self.evict_texture_groups(handle);
            if *sampler != gpu.sampler_key {
                self.compute_bindings.release_sampler(&gpu.texture);
            }
            if *previous != gpu.texture {
                self.plugin_texture_replaced(handle, &gpu.texture);
                if !self.storage_held(previous) {
                    self.compute_bindings.release_texture(previous);
                    self.mipmaps.release(previous);
                    self.pending_mips.remove(previous);
                    self.release_resolve_history(previous);
                }
            }
        }
        if gpu.render_target && !resources::is_integer(gpu.texture.format()) {
            self.mipmaps
                .prepare(&device, &gpu.texture, gpu.target_format(0));
            if gpu.srgb_view.is_some() {
                self.mipmaps
                    .prepare(&device, &gpu.texture, gpu.target_format(1));
            }
        }
        self.prepare_resolve_history(&gpu, packet.retain_resolve, packet.dimension);
        #[cfg(target_os = "linux")]
        if let Some(plugins) = self.vulkan_plugins.as_mut() {
            plugins.release_depth_resolve(handle);
        }
        #[cfg(target_os = "linux")]
        if let Some(plugins) = self.vulkan_plugins.as_mut()
            && let (Some(source), Some(destination)) = (&gpu.depth, &gpu.native_depth)
            && source.texture().format().has_stencil_aspect()
        {
            if plugins.prepare_depth_resolve(
                &self.adapter,
                &device,
                handle,
                source,
                &destination.view,
                self.frame_number + 1,
            ) {
                // Establish wgpu's initialization state before raw publication or plugin writes.
                let mut encoder = device.create_command_encoder(&wgpu::CommandEncoderDescriptor {
                    label: Some("unbound native depth initialization"),
                });
                for view in [source, &destination.view] {
                    drop(encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                        label: Some("unbound native depth initialization"),
                        color_attachments: &[],
                        depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                            view,
                            depth_ops: Some(wgpu::Operations {
                                load: wgpu::LoadOp::Load,
                                store: wgpu::StoreOp::Store,
                            }),
                            stencil_ops: Some(wgpu::Operations {
                                load: wgpu::LoadOp::Load,
                                store: wgpu::StoreOp::Store,
                            }),
                        }),
                        ..Default::default()
                    }));
                }
                self.queue.submit([encoder.finish()]);
            } else {
                tracing::warn!(
                    "Unbound: Vulkan depth/stencil publication requires matching 2D attachments and sample-zero depth/stencil resolve support; stencil publication is unsupported on this device"
                );
            }
        }
        if let Some(resolve) = &mut gpu.native_depth {
            if resolve.group.is_none() {
                let (_, layout) = self.depth_blit_pipeline(
                    true,
                    1,
                    false,
                    resolve.view.texture().format(),
                    self.conventions.depth_resolve_sample(gpu.samples),
                    false,
                );
                resolve.group = Some(device.create_bind_group(&wgpu::BindGroupDescriptor {
                    label: Some("unbound native depth resolve"),
                    layout: &layout,
                    entries: &[wgpu::BindGroupEntry {
                        binding: 0,
                        resource: wgpu::BindingResource::TextureView(
                            gpu.depth_msaa.as_ref().expect("multisampled depth"),
                        ),
                    }],
                }));
            }
        }
        self.textures.insert(handle, gpu);
    }

    fn warn_once(&mut self, key: String, message: impl FnOnce() -> String) {
        if self.warned.insert(key) {
            tracing::warn!("{}", message());
        }
    }

    /// The buffer a draw's external indices are read from (`DrawIndices`, render protocols 8 and
    /// 10): `count` indices of `size` bytes from `first`, drawn with the base vertex of `shape` (a
    /// `DrawShape`'s, else 0; an indirect draw's arguments give their own). None, warned once,
    /// when the planned draws cannot read them safely ([`external_index_refusal`]): the draw is
    /// skipped.
    fn external_indices(
        &mut self,
        (handle, size, first, count): (u64, u8, u32, u32),
        shape: DrawShape,
        planned: &[Planned],
    ) -> Option<wgpu::Buffer> {
        let buffer = self
            .buffers
            .get(&handle)
            .map(|b| (b.buffer.clone(), b.size));
        let draws: Vec<DrawInputs> = planned
            .iter()
            .filter_map(|p| match p {
                Planned::Draw(d) => Some(DrawInputs {
                    vertex_inputs: self.compiled.reads_vertex_inputs(d.program.0),
                    indirect: d.indirect.is_some(),
                    vertex_bytes: d.vertex.size(),
                    extra_bytes: d.extra.as_ref().map(wgpu::Buffer::size),
                }),
                _ => None,
            })
            .collect();
        let source = buffer
            .as_ref()
            .map(|(b, size)| (*size, b.usage().contains(wgpu::BufferUsages::INDEX)));
        if let Some(why) =
            external_index_refusal(source, (size, first, count), shape.base_vertex, &draws)
        {
            self.warn_once(format!("indices{handle}:{why}"), || {
                format!(
                    "a draw's {}-bit indices from buffer {handle}: {why}; skipped",
                    size * 8
                )
            });
            return None;
        }
        buffer.map(|(b, _)| b)
    }

    /// Applies a resource change.
    pub fn apply(&mut self, upload: Upload) {
        if self.is_gles {
            let device = self.device.clone();
            super::gl_interop::deferring_forgets(|| {
                device.with_gles_context_pinned(|| self.apply_inner(upload))
            });
        } else {
            self.apply_inner(upload);
        }
    }

    fn apply_inner(&mut self, upload: Upload) {
        let device = self.device.clone();
        match upload {
            Upload::UploadMesh(handle, mesh) => {
                let (vertices, indices, submeshes) = resources::build_mesh(&mesh);
                let extra_vertices = resources::build_extra(&mesh);
                let old = self.meshes.remove(&handle);
                let (old_vertex, old_extra, old_index) = match old {
                    Some(m) => (Some(m.vertex), m.extra, Some(m.index)),
                    None => (None, None, None),
                };
                let extra = extra_vertices.map(|e| {
                    resources::write_buffer(
                        &device,
                        &self.queue,
                        old_extra,
                        bytemuck::cast_slice(&e),
                        wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::STORAGE,
                        "unbound extra vertices",
                    )
                });
                let vertex = resources::write_buffer(
                    &device,
                    &self.queue,
                    old_vertex,
                    bytemuck::cast_slice(&vertices),
                    wgpu::BufferUsages::VERTEX | wgpu::BufferUsages::STORAGE,
                    "unbound vertices",
                );
                let keep_gpu_indices = self.mesh_has_gpu_indices(handle)
                    && old_index
                        .as_ref()
                        .is_some_and(|buffer| buffer.size() >= (indices.len() as u64 * 4).max(16));
                self.invalidate_mesh_buffers(handle, keep_gpu_indices);
                let index = if keep_gpu_indices {
                    old_index.unwrap()
                } else {
                    resources::write_buffer(
                        &device,
                        &self.queue,
                        old_index,
                        bytemuck::cast_slice(&indices),
                        wgpu::BufferUsages::INDEX | wgpu::BufferUsages::STORAGE,
                        "unbound indices",
                    )
                };
                self.meshes.insert(
                    handle,
                    GpuMesh {
                        vertex,
                        extra,
                        index,
                        submeshes,
                    },
                );
            }
            Upload::ReleaseMesh(handle) => {
                self.meshes.remove(&handle);
                for slot in 0..4 {
                    self.mesh_buffers.remove(&(handle, slot));
                }
            }
            Upload::UploadTexture(handle, mut texture) => {
                // Stored in the screen's channel order only where the screen is blue first.
                texture.screen_order &= self.conventions.screen_bgra;
                self.generation += 1;
                let mut old = self.textures.remove(&handle);
                let previous = old
                    .as_ref()
                    .map(|gpu| (gpu.texture.clone(), gpu.sampler_key));
                let size = (texture.width.max(1) as u32, texture.height.max(1) as u32);
                let mut restored = None;
                if let (Some(logical), Some(full)) = (texture.logical, &old)
                    && size != logical
                    && (full.width, full.height) == logical
                {
                    // Scaled from its full size: that allocation is kept, the scaled one new,
                    // its samples published first if it is still the bound target.
                    let gpu = old.take().expect("matched");
                    self.publish_resolve_now(&gpu.texture);
                    let kept = FullResolution {
                        mips: self.pending_mips.get(&gpu.texture).copied(),
                        resolve: self.resolve_history.remove(&gpu.texture),
                        gpu,
                    };
                    self.full_resolution.insert(handle, kept);
                } else if let Some(full) = self.full_resolution.remove(&handle) {
                    if (full.gpu.width, full.gpu.height) == size {
                        // At its full size again (the factors may still round up to it): the
                        // kept allocation, whose shape the upload checks.
                        restored = Some((full.gpu.texture.clone(), full.mips, full.resolve));
                        old = Some(full.gpu);
                    } else if texture.logical.is_some() {
                        // Scaled to another size: still kept.
                        self.full_resolution.insert(handle, full);
                    }
                }
                let gpu =
                    resources::upload_texture(&device, &self.queue, old, &texture, self.generation);
                // What it had when it was scaled, if it is the one kept: its pending automatic
                // mips and its retained resolve (a multisampled texture's published image).
                if let Some((_, mips, resolve)) =
                    restored.filter(|(kept, _, _)| *kept == gpu.texture)
                {
                    if let Some(format) = mips {
                        self.pending_mips.insert(gpu.texture.clone(), format);
                    }
                    if let Some(resolve) = resolve {
                        self.resolve_history.insert(gpu.texture.clone(), resolve);
                    }
                }
                self.install_texture(handle, gpu, previous, &texture);
            }
            Upload::ReleaseTexture(handle) => {
                #[cfg(target_os = "linux")]
                self.restore_external_vulkan(handle);
                self.generation += 1;
                // Before the plugins forget it: an external texture's storage stays its source's.
                if let Some(texture) = self.textures.remove(&handle)
                    && !self.storage_held(&texture.texture)
                {
                    self.compute_bindings.release_texture(&texture.texture);
                    self.mipmaps.release(&texture.texture);
                    self.pending_mips.remove(&texture.texture);
                    self.release_resolve_history(&texture.texture);
                }
                self.release_plugin_texture(handle);
                self.texture_mip_biases.remove(&handle);
                self.bit_views.retain(|(h, _), _| *h != handle);
                self.depth_main_groups.retain(|(h, _), _| *h != handle);
                self.feedback_copies.remove(&handle);
                self.full_resolution.remove(&handle);
                self.evict_texture_groups(handle);
            }
            Upload::TextureMipBias(handle, bias) => {
                self.texture_mip_biases.insert(handle, bias);
                self.evict_texture_groups(handle);
                if let Some(texture) = self.textures.get_mut(&handle) {
                    self.generation += 1;
                    let bias = if bias == 0.0 || !self.conventions.mip_bias {
                        0
                    } else {
                        bias.to_bits()
                    };
                    if texture.sampler_key.lod_bias_bits != bias {
                        self.compute_bindings.release_sampler(&texture.texture);
                        texture.sampler_key.lod_bias_bits = bias;
                    }
                    texture.generation = self.generation;
                    texture.bind_groups = [None, None];
                }
            }
            Upload::TextureSampler(handle, [filter, wrap_u, wrap_v, wrap_w, aniso]) => {
                self.evict_texture_groups(handle);
                if let Some(texture) = self.textures.get_mut(&handle) {
                    self.generation += 1;
                    if (
                        texture.sampler_key.filter,
                        texture.sampler_key.wrap_u,
                        texture.sampler_key.wrap_v,
                        texture.sampler_key.wrap_w,
                        texture.sampler_key.aniso,
                    ) != (filter, wrap_u, wrap_v, wrap_w, aniso)
                    {
                        self.compute_bindings.release_sampler(&texture.texture);
                    }
                    texture.sampler_key.filter = filter;
                    texture.sampler_key.wrap_u = wrap_u;
                    texture.sampler_key.wrap_v = wrap_v;
                    texture.sampler_key.wrap_w = wrap_w;
                    texture.sampler_key.aniso = aniso;
                    texture.generation = self.generation;
                    texture.bind_groups = [None, None];
                }
            }
            Upload::UpdateMaterial(handle, material) => {
                let resolved = self.resolver.resolve(&material);
                self.materials.insert(handle, GpuMaterial::new(resolved));
            }
            Upload::ReleaseMaterial(handle) => {
                self.materials.remove(&handle);
            }
        }
    }

    /// Drops cached program texture bind groups referencing `handle`.
    fn evict_texture_groups(&mut self, handle: u64) {
        self.program_texture_groups.retain(|key, _| {
            !key.slots
                .iter()
                .any(|slot| handle::split(slot.raw).0 == handle)
        });
    }

    /// Drops cached program storage-buffer bind groups referencing `handle`.
    pub(super) fn evict_buffer_groups(&mut self, handle: u64) {
        self.program_buffer_groups
            .retain(|key, _| !key.slots.iter().any(|slot| slot.handle == handle));
    }

    /// A texture's anisotropic level under the global setting.
    fn effective_aniso(&self, level: i32) -> i32 {
        effective_aniso(self.anisotropy, level)
    }

    /// A texture's mip bias (`Texture.mipMapBias`) as the bits of its sampler key: the
    /// sampler's own LOD bias, which keeps implicit and explicit gradients. Each device's
    /// backend has its Editor's anisotropic transition as it is (tests/projects/samplerbias,
    /// both devices). None where the device ignores the bias (`DeviceConventions::mip_bias`).
    fn sampler_lod_bias(&self, handle: u64) -> u32 {
        let bias = self.texture_mip_biases.get(&handle).copied().unwrap_or(0.0);
        if bias == 0.0 || !self.conventions.mip_bias {
            0
        } else {
            bias.to_bits()
        }
    }

    fn sampler(&mut self, key: SamplerKey) -> wgpu::Sampler {
        let device = &self.device;
        let reversed_z = self.conventions.reversed_z;
        // Bias can change continuously. Bind groups own these samplers; keeping every bias
        // in this renderer cache would prevent their release. HAL deduplicates live samplers.
        if key.lod_bias_bits != 0 {
            return resources::create_sampler(device, key, reversed_z);
        }
        self.samplers
            .entry(key)
            .or_insert_with(|| resources::create_sampler(device, key, reversed_z))
            .clone()
    }

    /// Whether the current target draws into the texture `t` (stored at `handle`): its color, a
    /// further color, or, a depth-only target, its depth.
    #[inline]
    fn drawn_into_entry(&self, handle: u64, t: &GpuTexture) -> bool {
        let Some(target) = &self.state.target else {
            return false;
        };
        // A depth-only target draws into its depth, with no color texture to compare.
        if t.depth_only && self.target_handles.0 == handle {
            return true;
        }
        if target
            .texture
            .as_ref()
            .is_some_and(|(x, ..)| *x == t.texture)
        {
            return true;
        }
        target
            .extra
            .iter()
            .any(|e| e.texture.as_ref().is_some_and(|(x, ..)| *x == t.texture))
    }

    /// Whether the current target draws into a texture (its color or a further color).
    fn drawn_into(&self, handle: u64) -> bool {
        let Some(t) = self.textures.get(&handle) else {
            return false;
        };
        self.drawn_into_entry(handle, t)
    }

    /// Whether the legacy `_MainTex` binding for `handle` resolves to an alpha-only texture
    /// (matching [`Self::main_bind_group`]'s `alpha_only` result without creating a bind group).
    #[inline]
    fn main_texture_alpha_only(&self, handle: u64) -> bool {
        if handle == 0 {
            return false;
        }
        let (base, depth_element) = handle::split(handle);
        if depth_element {
            return false;
        }
        let Some(t) = self.textures.get(&base) else {
            return false;
        };
        if !t.alpha_only || t.cube || t.cube_array || resources::is_integer(t.texture.format()) {
            return false;
        }
        if self.drawn_into_entry(base, t) && (t.layers > 1 || t.volume || t.format == 5) {
            return false;
        }
        true
    }

    /// The view a draw samples a texture it draws into through: a copy made right before the
    /// draw (sampling the texture itself would read and write it in one pass, which the device
    /// refuses), so the draw reads what the texture held before it, as the Editor does
    /// (OpenGLCore, 6000.0.71f1: tests/projects/textureformats `feedback.*`). None, warned
    /// once, for a layered or depth-only texture, whose slot then reads its default.
    fn feedback_view(&mut self, handle: u64, space: i32) -> Option<wgpu::TextureView> {
        let t = self.textures.get(&handle)?;
        if t.layers > 1 || t.volume || t.format == 5 {
            self.warn_once(format!("feedback{handle}"), || {
                format!("a draw samples the layered or depth-only render texture {handle} it draws into: the slot reads its default")
            });
            return None;
        }
        let (source, srgb) = (t.texture.clone(), t.srgb_view.is_some());
        // The copy's sRGB view takes the source's channel order (a screen camera's stand-in is
        // stored blue first where the screen is).
        let srgb_format = source.format().add_srgb_suffix();
        let fits = self.feedback_copies.get(&handle).is_some_and(|c| {
            c.size() == source.size()
                && c.format() == source.format()
                && c.mip_level_count() == source.mip_level_count()
        });
        if !fits {
            let srgb_formats = [srgb_format];
            let copy = self.device.create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound feedback copy"),
                size: source.size(),
                mip_level_count: source.mip_level_count(),
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: source.format(),
                usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
                view_formats: if srgb { &srgb_formats } else { &[] },
            });
            self.feedback_copies.insert(handle, copy);
        }
        let copy = self.feedback_copies[&handle].clone();
        for mip in 0..source.mip_level_count() {
            self.pre_copies.push(Copy {
                channels: None,
                source: source.clone(),
                source_mip: mip,
                source_origin: wgpu::Origin3d::ZERO,
                destination: copy.clone(),
                destination_mip: mip,
                destination_origin: wgpu::Origin3d::ZERO,
                size: source
                    .size()
                    .mip_level_size(mip, wgpu::TextureDimension::D2),
            });
        }
        Some(copy.create_view(&wgpu::TextureViewDescriptor {
            format: (srgb && space == 1).then_some(srgb_format),
            ..Default::default()
        }))
    }

    /// Whether a float slot cannot read a texture: an integer one, which it reads as its default
    /// (warned once; Unity leaves what it reads undefined).
    fn unreadable_as_float(&mut self, handle: u64, name: &str) -> bool {
        let integer = self
            .textures
            .get(&handle)
            .is_some_and(|t| resources::is_integer(t.texture.format()));
        if integer {
            let name = name.to_string();
            self.warn_once(format!("int-as-float{handle}{name}"), || {
                format!("texture `{name}` samples floats from the integer render texture {handle}: it reads its default")
            });
        }
        integer
    }

    fn texture_or_default(&self, handle: u64, default: &str) -> &GpuTexture {
        self.textures.get(&handle).unwrap_or_else(|| {
            self.defaults
                .get(default)
                .unwrap_or_else(|| &self.defaults["white"])
        })
    }

    /// The main-texture bind group of a texture handle (or default), cached on the texture; a
    /// depth element's (render protocol 6) reads the texture's depth copy.
    fn main_bind_group(
        &mut self,
        handle: u64,
        default: &str,
        space: i32,
    ) -> (wgpu::BindGroup, bool) {
        let slot = space.clamp(0, 1) as usize;
        let (base, depth_element) = handle::split(handle);
        if !depth_element
            && self
                .textures
                .get(&base)
                .is_some_and(|t| t.format == 5 && t.native_depth.is_some())
        {
            self.depth_element_view(base);
        }
        if depth_element {
            self.fill_depth_copy(base);
            let Some(view) = self.depth_element_view(base) else {
                return self.main_bind_group(0, default, space);
            };
            let t = &self.textures[&base];
            let generation = t.generation;
            if let Some((g, bg)) = self.depth_main_groups.get(&(base, slot))
                && *g == generation
            {
                return (bg.clone(), false);
            }
            let mut key = t.sampler_key;
            key.aniso = self.effective_aniso(key.aniso);
            key.lod_bias_bits = self.sampler_lod_bias(base);
            let sampler = self.sampler(key);
            let bg = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound main texture depth element"),
                layout: &self.pipelines.texture_layout,
                entries: &[
                    wgpu::BindGroupEntry {
                        binding: 0,
                        resource: wgpu::BindingResource::TextureView(&view),
                    },
                    wgpu::BindGroupEntry {
                        binding: 1,
                        resource: wgpu::BindingResource::Sampler(&sampler),
                    },
                ],
            });
            self.depth_main_groups
                .insert((base, slot), (generation, bg.clone()));
            return (bg, false);
        }
        // The legacy slot is a 2D texture: a cube `_MainTex` (Hidden/CubeBlur, Hidden/CubeCopy)
        // reaches its shader through the named binding only.
        if self
            .textures
            .get(&handle)
            .is_some_and(|t| t.cube || t.cube_array)
            || self.unreadable_as_float(handle, "_MainTex")
        {
            return self.main_bind_group(0, default, space);
        }
        if self.drawn_into(handle) {
            let Some(view) = self.feedback_view(handle, space) else {
                return self.main_bind_group(0, default, space);
            };
            let t = &self.textures[&handle];
            let (mut key, alpha_only) = (t.sampler_key, t.alpha_only);
            key.aniso = self.effective_aniso(key.aniso);
            key.lod_bias_bits = self.sampler_lod_bias(handle);
            let sampler = self.sampler(key);
            let bg = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound main texture feedback copy"),
                layout: &self.pipelines.texture_layout,
                entries: &[
                    wgpu::BindGroupEntry {
                        binding: 0,
                        resource: wgpu::BindingResource::TextureView(&view),
                    },
                    wgpu::BindGroupEntry {
                        binding: 1,
                        resource: wgpu::BindingResource::Sampler(&sampler),
                    },
                ],
            });
            return (bg, alpha_only);
        }
        self.fill_depth_copy(handle);
        let mut key = {
            let t = self.texture_or_default(handle, default);
            if let Some(bg) = &t.bind_groups[slot] {
                return (bg.clone(), t.alpha_only);
            }
            t.sampler_key
        };
        // The legacy main-texture binding follows the same global policy as a named 2D
        // texture binding.
        key.aniso = self.effective_aniso(key.aniso);
        key.lod_bias_bits = self.sampler_lod_bias(handle);
        let sampler = self.sampler(key);
        let layout = self.pipelines.texture_layout.clone();
        let device = self.device.clone();
        let use_default = !self.textures.contains_key(&handle);
        let t = if use_default {
            let name = if self.defaults.contains_key(default) {
                default
            } else {
                "white"
            };
            self.defaults.get_mut(name).unwrap()
        } else {
            self.textures.get_mut(&handle).unwrap()
        };
        let view = self
            .resolve_history
            .get(&t.texture)
            .map_or(t.view_for(space), |history| history.view_for(space));
        let bg = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("unbound main texture"),
            layout: &layout,
            entries: &[
                wgpu::BindGroupEntry {
                    binding: 0,
                    resource: wgpu::BindingResource::TextureView(view),
                },
                wgpu::BindGroupEntry {
                    binding: 1,
                    resource: wgpu::BindingResource::Sampler(&sampler),
                },
            ],
        });
        t.bind_groups[slot] = Some(bg.clone());
        (bg, t.alpha_only)
    }

    /// A default texture of a dimension other than 2D (all texels the default's color).
    fn dim_default(
        &mut self,
        dim: unbound_shaders::TextureDim,
        default: &str,
    ) -> wgpu::TextureView {
        let key = (dim, default.to_string());
        if let Some(v) = self.dim_defaults.get(&key) {
            return v.clone();
        }
        use unbound_shaders::TextureDim;
        let rgba: [u8; 4] = match default {
            "white" => [255, 255, 255, 255],
            "black" => [0, 0, 0, 0],
            "bump" => [127, 127, 255, 255],
            "red" => [255, 0, 0, 0],
            "unbound-unassigned" => [128, 128, 128, 128],
            _ => [127, 127, 127, 127],
        };
        let (layers, dimension) = match dim {
            TextureDim::Cube | TextureDim::CubeArray => (6, wgpu::TextureDimension::D2),
            TextureDim::D3 => (1, wgpu::TextureDimension::D3),
            _ => (1, wgpu::TextureDimension::D2),
        };
        let device = &self.device;
        let texture = device.create_texture(&wgpu::TextureDescriptor {
            label: Some("unbound default texture"),
            size: wgpu::Extent3d {
                width: 1,
                height: 1,
                depth_or_array_layers: layers,
            },
            mip_level_count: 1,
            sample_count: 1,
            dimension,
            format: wgpu::TextureFormat::Rgba8Unorm,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[],
        });
        let data: Vec<u8> = (0..layers).flat_map(|_| rgba).collect();
        self.queue.write_texture(
            texture.as_image_copy(),
            &data,
            wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(4),
                rows_per_image: Some(1),
            },
            wgpu::Extent3d {
                width: 1,
                height: 1,
                depth_or_array_layers: layers,
            },
        );
        let view = texture.create_view(&wgpu::TextureViewDescriptor {
            dimension: Some(compiled::view_dimension(dim)),
            ..Default::default()
        });
        self.dim_defaults.insert(key, view.clone());
        view
    }

    /// The textures (group 1) of a material's pass drawn with program `id`, cached on the
    /// material. A texture property takes the material's texture, else the global one
    /// (`Shader.SetGlobalTexture`), else the property's default.
    /// A program's buffers: a random write target at a written buffer's register, else by name
    /// the draw's or material's (`material` has the draw's properties over the material's), else
    /// a global one; a hidden counter block its buffer's counter; zeros (with a warning) for one
    /// nothing set (`unread`, a buffer the draw does not read, without one). Err (warned once)
    /// when a hidden counter's buffer is not set or has no counter: the pass is not drawn, as
    /// such a dispatch is not run.
    fn buffer_bind_group(
        &mut self,
        id: u32,
        shader: &str,
        overrides: &[(String, owned::PropertyValue)],
        has_buf_override: bool,
        material: &owned::Material,
        globals: &[(String, owned::PropertyValue)],
        unread: Option<&str>,
    ) -> Result<Option<wgpu::BindGroup>, ()> {
        if self.compiled.buffer_layout(id).is_none() {
            return Ok(None);
        }
        let program = self.compiled.program(id).clone();
        let globals_index = &self.globals_index;
        // The buffer bound to a slot: a random write target at its UAV register (a global the
        // managed Graphics.SetRandomWriteTarget sets; cleared to no buffer, 0) first, a released
        // one binding nothing; else one of its name.
        let handle_of = |b: &unbound_shaders::BufferSlot| {
            let target = b.register.and_then(|r| {
                let key = format!("{RANDOM_WRITE_TARGET}{r}");
                match globals_index.find(globals, &key) {
                    Some(owned::PropertyValue::Buffer(h)) if *h != 0 => Some(*h),
                    _ => None,
                }
            });
            target.or_else(|| {
                if has_buf_override
                    && let Some(h) = overrides.iter().rev().find_map(|(n, v)| match v {
                        owned::PropertyValue::Buffer(h) if *n == b.name => Some(*h),
                        _ => None,
                    })
                {
                    return Some(h);
                }
                if let Some(owned::PropertyValue::Buffer(h)) = material.get(&b.name) {
                    return Some(*h);
                }
                match globals_index.find(globals, &b.name) {
                    Some(owned::PropertyValue::Buffer(h)) => Some(*h),
                    _ => None,
                }
            })
        };

        self.buffer_slot_scratch.clear();
        let mut any_missing = false;
        for b in &program.buffers {
            let direct = handle_of(b);
            if !b.read_only
                && let Some(buffer) = direct.and_then(|h| self.buffers.get_mut(&h))
            {
                buffer.revision = buffer.revision.wrapping_add(1);
            }
            // glslang's hidden counter of an Append or Counter buffer (`<name>@count`) binds
            // that buffer's counter.
            if let Some(owner) = b.name.strip_suffix(compute::COUNTER_SUFFIX) {
                let owner_handle = program
                    .buffers
                    .iter()
                    .find(|o| o.name == owner)
                    .and_then(&handle_of);
                let has_counter = owner_handle
                    .and_then(|h| self.buffers.get_mut(&h))
                    .is_some_and(|g| {
                        g.counter_used |= g.counter.is_some();
                        g.counter.is_some()
                    });
                if !has_counter {
                    self.warn_once(format!("drawcount{shader}{owner}"), || {
                        format!(
                            "shader `{shader}` uses the counter of buffer `{owner}`, which is not set or not an Append or Counter buffer: the draw is skipped"
                        )
                    });
                    return Err(());
                }
                self.buffer_slot_scratch.push(BufferSlotKey {
                    handle: owner_handle.unwrap(),
                    counter: true,
                });
            } else if let Some(h) = direct.filter(|h| self.buffers.contains_key(h)) {
                self.buffer_slot_scratch.push(BufferSlotKey {
                    handle: h,
                    counter: false,
                });
            } else {
                any_missing = true;
                self.buffer_slot_scratch.push(BufferSlotKey {
                    handle: 0,
                    counter: false,
                });
            }
        }

        if !any_missing {
            let lookup = BufferGroupLookup {
                program: id,
                slots: &self.buffer_slot_scratch,
            };
            if let Some((last_frame, group)) = self.program_buffer_groups.get_mut(&lookup) {
                *last_frame = self.frame_number;
                return Ok(Some(group.clone()));
            }
        }

        if any_missing && self.empty_storage.is_none() {
            self.empty_storage = Some(self.device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound empty storage"),
                size: 256,
                usage: wgpu::BufferUsages::STORAGE,
                mapped_at_creation: false,
            }));
        }
        let slot_keys = self.buffer_slot_scratch.clone();
        let mut missing = Vec::new();
        let mut bound: Vec<wgpu::Buffer> = Vec::with_capacity(slot_keys.len());
        for (slot, b) in slot_keys.iter().zip(&program.buffers) {
            let buffer = if slot.counter {
                self.buffers[&slot.handle].counter.clone()
            } else if slot.handle != 0 {
                Some(self.buffers[&slot.handle].buffer.clone())
            } else {
                None
            };
            bound.push(buffer.unwrap_or_else(|| {
                missing.push(b.name.clone());
                self.empty_storage.clone().unwrap()
            }));
        }
        let entries: Vec<_> = bound
            .iter()
            .enumerate()
            .map(|(k, buffer)| wgpu::BindGroupEntry {
                binding: k as u32,
                resource: buffer.as_entire_binding(),
            })
            .collect();
        let group = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("unbound draw buffers"),
            layout: self.compiled.buffer_layout(id).ok_or(())?,
            entries: &entries,
        });
        for name in missing {
            if unread == Some(name.as_ref()) {
                continue;
            }
            self.warn_once(format!("drawbuf{shader}{name}"), || {
                format!(
                    "shader `{shader}` reads buffer `{name}`, which nothing set: it reads zeros"
                )
            });
        }
        if !any_missing {
            self.program_buffer_groups.insert(
                BufferGroupKey {
                    program: id,
                    slots: slot_keys,
                },
                (self.frame_number, group.clone()),
            );
        }
        Ok(Some(group))
    }

    fn program_bind_group(
        &mut self,
        handle: u64,
        id: u32,
        space: i32,
        globals: &[(String, owned::PropertyValue)],
        overrides: &[(String, owned::PropertyValue)],
        has_texture_override: bool,
        main_override: u64,
    ) -> Option<wgpu::BindGroup> {
        let program = self.compiled.program(id).clone();
        let plan = {
            let mat = self.materials.get_mut(&handle)?;
            if let Some(p) = mat.texture_plans.get(&id) {
                p.clone()
            } else {
                let built = Arc::new(MaterialTexturePlan::build(&mat.resolved, &program));
                mat.texture_plans.insert(id, built.clone());
                built
            }
        };
        let is_2d = |dim: unbound_shaders::TextureDim| dim == unbound_shaders::TextureDim::D2;

        // Collect only the texture overrides once when the draw actually has any.
        self.texture_override_scratch.clear();
        if has_texture_override {
            for (idx, (_, v)) in overrides.iter().enumerate() {
                if matches!(v, owned::PropertyValue::Texture { .. }) {
                    self.texture_override_scratch.push(idx);
                }
            }
        }
        let has_overrides = !self.texture_override_scratch.is_empty();

        self.texture_slot_scratch.clear();
        let mut cached = !plan.any_uncacheable;
        for (k, entry) in plan.slots.iter().enumerate() {
            let mut raw_h = entry.material_handle;
            if has_overrides {
                let name = &program.textures[k];
                for &idx in &self.texture_override_scratch {
                    if overrides[idx].0 == *name
                        && let owned::PropertyValue::Texture { handle: h, .. } = overrides[idx].1
                    {
                        raw_h = h;
                        break;
                    }
                }
            }
            if raw_h == 0 && !entry.declares {
                raw_h = entry.global_slot.find_handle(
                    globals,
                    &self.globals_index,
                    &program.textures[k],
                );
            }
            // Typed 2D _MainTex slots use the program layout, but still receive a
            // renderer's main texture (a sprite's, for example).
            if entry.is_main_2d && main_override != 0 {
                raw_h = main_override;
            }
            if raw_h == 0 {
                // Handle 0 is never a GPU texture; all built-in defaults have generation 0.
                debug_assert!(!self.textures.contains_key(&0));
                self.texture_slot_scratch.push(TextureSlotKey {
                    raw: 0,
                    generation: 0,
                    default_tag: entry.default_tag,
                });
                continue;
            }
            let (mut base, depth) = handle::split(raw_h);
            let slot = entry.slot;
            let dim = entry.dim;
            let needs_depth_copy_check =
                entry.is_2d && !slot.comparison && !slot.depth() && !slot.multisampled;

            // Fast path: single `self.textures` lookup for ordinary color textures (no depth
            // element, no native depth resolve, no depth-only format, no integer-in-float slot).
            if !depth
                && let Some(t) = self.textures.get(&base)
                && t.native_depth.is_none()
                && !(needs_depth_copy_check && t.format == 5)
                && !(!slot.integer() && resources::is_integer(t.texture.format()))
            {
                if self.drawn_into_entry(base, t) {
                    cached = false;
                }
                self.texture_slot_scratch.push(TextureSlotKey {
                    raw: raw_h,
                    generation: t.generation,
                    default_tag: entry.default_tag,
                });
                continue;
            }

            let name = &program.textures[k];
            // A handle may name the texture's depth element (render protocol 6): its view comes
            // from the depth copy, brought up to date for this draw even when cached.
            if !slot.multisampled
                && self
                    .textures
                    .get(&base)
                    .is_some_and(|t| t.native_depth.is_some())
            {
                if !depth && self.textures[&base].format == 5 {
                    self.depth_element_view(base);
                } else if depth || slot.depth() {
                    self.depth_reads.entry(base).or_insert(false);
                }
            }
            if !slot.multisampled && depth {
                if slot.sample == SampleKind::Float && entry.is_2d {
                    self.depth_element_view(base);
                } else if !(slot.depth() && entry.is_2d) {
                    let name = name.clone();
                    self.warn_once(format!("depth-element-slot{id}{name}"), || {
                        format!(
                            "texture `{name}` samples a render texture's depth element, which its slot ({:?}, {dim:?}) cannot read; the slot reads its default",
                            slot.sample
                        )
                    });
                }
            }
            if !slot.integer() && !depth && self.unreadable_as_float(base, name) {
                base = 0;
                if !slot.multisampled {
                    raw_h = 0;
                }
            }
            // A depth-only texture a slot reads outside a comparison reads its filled copy.
            if needs_depth_copy_check {
                self.fill_depth_copy(base);
            }
            // A texture the target draws into is read through a copy made for this draw.
            if self.drawn_into(base) {
                cached = false;
            }
            let generation = if entry.is_2d {
                self.texture_or_default(base, &entry.default_name)
                    .generation
            } else {
                self.textures.get(&base).map_or(0, |t| t.generation)
            };
            self.texture_slot_scratch.push(TextureSlotKey {
                raw: raw_h,
                generation,
                default_tag: entry.default_tag,
            });
        }

        if cached {
            let lookup = TextureGroupLookup {
                program: id,
                space,
                slots: &self.texture_slot_scratch,
            };
            if let Some((last_frame, bg)) = self.program_texture_groups.get_mut(&lookup) {
                *last_frame = self.frame_number;
                return Some(bg.clone());
            }
        }

        let slot_keys = self.texture_slot_scratch.clone();
        let count = slot_keys.len();
        let mut views = Vec::with_capacity(count);
        let mut samplers = Vec::with_capacity(count);
        for (k, slot_key) in slot_keys.iter().enumerate() {
            let raw_h = slot_key.raw;
            let (mut h, depth_element) = handle::split(raw_h);
            let name = &program.textures[k];
            let entry = &plan.slots[k];
            let default = &entry.default_name;
            let dim = entry.dim;
            let slot = entry.slot;
            if slot.multisampled && !slot.integer() && !depth_element {
                let is_int = self
                    .textures
                    .get(&h)
                    .is_some_and(|t| resources::is_integer(t.texture.format()));
                if is_int {
                    h = 0;
                }
            }

            // A sampler is the texture's, or the state its name spells where the program could
            // not pair it with one texture (samples through inline samplers use their texture's
            // own sampler: `unbound_shaders`' samplers module); comparison samplers compare
            // less-or-equal (Unity's shadow maps, without reversed depth).
            let mut key = match slot.inline {
                Some(inline) => resources::SamplerKey::inline(inline),
                None if is_2d(dim) => self.texture_or_default(h, default).sampler_key,
                None => self
                    .textures
                    .get(&h)
                    .map_or(resources::SamplerKey::LINEAR_CLAMP, |t| t.sampler_key),
            };
            // The global setting is every 2D and 2D array texture's, render textures included
            // (tests/projects/samplerbias `--target-control`; a render texture's own level is 0
            // where its depth buffer turns anisotropy off: managed
            // RenderTexture.SamplerAnisoLevel); shadow maps compare and cubemaps keep their own
            // level (measured, tests/projects/urp's reflection under ForceEnable).
            let planar = is_2d(dim) || dim == unbound_shaders::TextureDim::D2Array;
            if slot.inline.is_none() && !slot.comparison && planar {
                key.aniso = self.effective_aniso(key.aniso);
            }
            if slot.comparison {
                let state = self
                    .textures
                    .get(&h)
                    .filter(|t| t.depth_texture.is_some())
                    .and_then(|t| t.depth_compare);
                key.compare = resources::depth_comparison_mode(
                    state,
                    slot.inline.is_some(),
                    self.stored_depth_reads,
                );
                if state == Some(false) && slot.inline.is_none() && !self.stored_depth_reads {
                    self.warn_once(
                        resources::STORED_DEPTH_WARNING.into(),
                        resources::stored_depth_warning,
                    );
                }
            }
            if slot.inline.is_none() {
                key.lod_bias_bits = self.sampler_lod_bias(h);
            }

            if slot.multisampled {
                let Some(view) = self
                    .multisampled_view(raw_h, dim, slot.sample, space)
                    .filter(|_| !self.drawn_into(h))
                else {
                    let name = name.clone();
                    self.warn_once(format!("multisampled-slot{id}{name}{h}"), || format!(
                        "multisampled texture `{name}` requires a compatible 2D bindTextureMS render texture distinct from the draw target: the draw is skipped"));
                    return None;
                };
                samplers.push(self.sampler(resources::SamplerKey {
                    filter: 0,
                    aniso: 1,
                    compare: 0,
                    ..key
                }));
                views.push(view);
            } else if slot.integer() {
                // Integer slots read a float texture's values as their bits, within its size, and
                // zeros where nothing is bound (as Unity does on OpenGL Core: tests/projects/rtdepth
                // fmt.*.stencil.uint, fmt.*.globalStencil.uint). The bits are copied before every draw.
                let signed = slot.sample == SampleKind::Sint;
                let texture = self.textures.get(&h);
                let integer_view = if dim == unbound_shaders::TextureDim::D2Array
                    && texture.is_none()
                    && !depth_element
                {
                    Some(self.integer_array_default(signed, space))
                } else if !is_2d(dim)
                    || depth_element
                    || texture.is_some_and(|t| t.layers > 1 || t.volume)
                {
                    let name = name.clone();
                    self.warn_once(format!("integer-slot{id}{name}{h}"), || {
                        format!("integer texture `{name}` ({dim:?}) of a shader is bound to texture {h}, whose shape or element it cannot read: the draw is skipped")
                    });
                    return None;
                } else if let Some(t) = texture {
                    let format = t.texture.format();
                    if resources::is_integer(format) {
                        let same = format.sample_type(None, None)
                            == Some(if signed {
                                wgpu::TextureSampleType::Sint
                            } else {
                                wgpu::TextureSampleType::Uint
                            });
                        let view = (same && !self.drawn_into(h)).then(|| t.view.clone());
                        match view {
                            Some(view) => Some(view),
                            None if same => self.feedback_view(h, space),
                            None => {
                                let name = name.clone();
                                self.warn_once(format!("int-sign{id}{name}{h}"), || {
                                    format!("integer texture `{name}` reads the {format:?} render texture {h} of the other signedness: it reads zeros")
                                });
                                None
                            }
                        }
                    } else {
                        let (width, height, source) =
                            (t.width, t.height, self.sampled_texture_view(t, space));
                        let fits = self
                            .bit_views
                            .get(&(h, signed))
                            .is_some_and(|(w, hh, _)| *w == width && *hh == height);
                        if !fits {
                            let view = self
                                .device
                                .create_texture(&wgpu::TextureDescriptor {
                                    label: Some("unbound integer bits"),
                                    size: wgpu::Extent3d {
                                        width,
                                        height,
                                        depth_or_array_layers: 1,
                                    },
                                    mip_level_count: 1,
                                    sample_count: 1,
                                    dimension: wgpu::TextureDimension::D2,
                                    format: if signed {
                                        wgpu::TextureFormat::Rgba32Sint
                                    } else {
                                        wgpu::TextureFormat::Rgba32Uint
                                    },
                                    usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                                        | wgpu::TextureUsages::TEXTURE_BINDING,
                                    view_formats: &[],
                                })
                                .create_view(&wgpu::TextureViewDescriptor::default());
                            self.bit_views.insert((h, signed), (width, height, view));
                        }
                        let target = self.bit_views[&(h, signed)].2.clone();
                        self.pre_draw.push(DepthCopy {
                            depth: source,
                            target: target.clone(),
                            kind: if signed {
                                CopyKind::SintBits
                            } else {
                                CopyKind::UintBits
                            },
                        });
                        Some(target)
                    }
                } else {
                    None
                };
                // Integers are read unfiltered.
                samplers.push(self.sampler(resources::SamplerKey {
                    filter: 0,
                    wrap_u: 1,
                    wrap_v: 1,
                    wrap_w: 0,
                    aniso: 1,
                    compare: 0,
                    lod_bias_bits: 0,
                    base_level_only: false,
                }));
                let view = match integer_view {
                    Some(view) => view,
                    None => self.integer_default(signed),
                };
                views.push(view);
            } else if depth_element && slot.sample == SampleKind::Float && is_2d(dim) {
                samplers.push(self.sampler(key));
                let view = match self.depth_element_view(h) {
                    Some(view) => view,
                    None => self.texture_or_default(0, default).view_for(space).clone(),
                };
                views.push(view);
            } else if depth_element && !slot.depth() {
                // A depth element the slot or the texture cannot provide: the slot's default.
                samplers.push(self.sampler(key));
                views.push(match is_2d(dim) {
                    true => self.texture_or_default(0, default).view_for(space).clone(),
                    false => self.dim_default(dim, default),
                });
            } else if slot.depth() {
                samplers.push(self.sampler(key));
                // The texture's depth when its shape is the slot's, else a far-plane default.
                let cube_slot = dim == unbound_shaders::TextureDim::Cube;
                let view = self
                    .textures
                    .get(&h)
                    .filter(|t| t.cube == cube_slot)
                    .and_then(|t| {
                        t.depth_sample
                            .clone()
                            .or_else(|| t.native_depth.as_ref().map(|r| r.sample.clone()))
                    })
                    .unwrap_or_else(|| self.depth_default(cube_slot));
                views.push(view);
            } else if is_2d(dim) && self.drawn_into(h) {
                samplers.push(self.sampler(key));
                let view = match self.feedback_view(h, space) {
                    Some(view) => view,
                    None => self.texture_or_default(0, default).view_for(space).clone(),
                };
                views.push(view);
            } else if is_2d(dim) {
                samplers.push(self.sampler(key));
                views.push(self.sampled_texture_view(self.texture_or_default(h, default), space));
            } else if self.drawn_into(h) {
                samplers.push(self.sampler(key));
                let name = name.clone();
                self.warn_once(format!("feedback-dim{id}{name}{h}"), || {
                    format!("texture `{name}` ({dim:?}) samples render texture {h} while drawing into it: it reads its default")
                });
                views.push(self.dim_default(dim, default));
            } else {
                samplers.push(self.sampler(key));
                // A cubemap bound to a cube slot, a 3D texture to a 3D slot, a 2D texture (of
                // any layers) to an array slot; anything else reads the slot's default.
                use unbound_shaders::TextureDim;
                let bound = self.textures.get(&h).and_then(|t| match dim {
                    TextureDim::Cube if t.cube => Some(self.sampled_texture_view(t, space)),
                    TextureDim::CubeArray if t.cube_array => {
                        Some(self.sampled_texture_view(t, space))
                    }
                    TextureDim::D3 if t.volume => Some(self.sampled_texture_view(t, space)),
                    TextureDim::D2Array if !t.cube && !t.volume => {
                        if let Some(view) = self.resolved_array_view(t, space) {
                            return Some(view);
                        }
                        let srgb = space == 1 && t.srgb_view.is_some();
                        let format = srgb.then_some(t.texture.format().add_srgb_suffix());
                        Some(t.texture.create_view(&wgpu::TextureViewDescriptor {
                            format,
                            dimension: Some(wgpu::TextureViewDimension::D2Array),
                            usage: resources::view_usage(&t.texture, format),
                            ..Default::default()
                        }))
                    }
                    _ => None,
                });
                match bound {
                    Some(view) => views.push(view),
                    None => views.push(self.dim_default(dim, default)),
                }
            }
        }
        let mut entries = Vec::with_capacity(2 * count + program.texture_aliases.len());
        for (k, (view, sampler)) in views.iter().zip(&samplers).enumerate() {
            entries.push(wgpu::BindGroupEntry {
                binding: 2 * k as u32,
                resource: wgpu::BindingResource::TextureView(view),
            });
            // Only the samplers the program samples through are bindings of its layout.
            let slot = program.texture_slots.get(k).copied().unwrap_or_default();
            if !slot.sampler {
                continue;
            }
            entries.push(wgpu::BindGroupEntry {
                binding: 2 * k as u32 + 1,
                resource: wgpu::BindingResource::Sampler(sampler),
            });
        }
        // A further variable of a texture shows what its slot does.
        for (j, &slot) in program.texture_aliases.iter().enumerate() {
            entries.push(wgpu::BindGroupEntry {
                binding: (2 * views.len() + j) as u32,
                resource: wgpu::BindingResource::TextureView(&views[slot as usize]),
            });
        }
        let bg = self.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("unbound program textures"),
            layout: self.compiled.texture_layout(id),
            entries: &entries,
        });
        if cached {
            self.program_texture_groups.insert(
                TextureGroupKey {
                    program: id,
                    space,
                    slots: slot_keys,
                },
                (self.frame_number, bg.clone()),
            );
        }
        Some(bg)
    }

    /// The position after `passes` among all ops this executor has encoded and will encode.
    fn op_position(&self, passes: &[Op]) -> usize {
        self.encoded_ops + passes.len()
    }

    /// Queues the copy of the depth target's depth into its color copy, when ops since the
    /// position `mark` drew into it (including ops already submitted mid-frame).
    fn copy_depth(&mut self, passes: &mut Vec<Op>, mark: usize) {
        let Some(handle) = self.depth_target else {
            return;
        };
        if self.op_position(passes) <= mark {
            return;
        }
        if let Some(copy) = self.depth_copy_op(handle) {
            passes.push(Op::DepthCopy(copy));
        }
    }

    /// A direct draw where `SV_VertexID` counts from the draw's first vertex
    /// ([`first_vertex_binding`]).
    fn draw_from_first_vertex(&self, d: &mut PlannedDraw) {
        if !self.conventions.vertex_id_from_draw_start || d.indirect.is_some() {
            return;
        }
        let vertices = [
            Some(d.vertex.size() / size_of::<resources::Vertex>() as u64),
            d.extra
                .as_ref()
                .map(|e| e.size() / size_of::<resources::ExtraVertex>() as u64),
        ]
        .into_iter()
        .flatten()
        .min()
        .unwrap_or(0);
        let inputs = self.compiled.program(d.program.0).vertex_inputs;
        (d.base_vertex, d.first_bound_vertex) =
            first_vertex_binding(d.base_vertex, inputs, vertices);
    }

    /// The copy of a render target's depth into its filterable color copy.
    fn depth_copy_op(&self, handle: u64) -> Option<DepthCopy> {
        let t = self.textures.get(&handle)?;
        let target = t.depth_copy.clone()?;
        let (depth, multisampled) = match (&t.depth_sample, &t.depth_msaa) {
            (Some(depth), _) => (depth, false),
            (None, Some(depth)) => (depth, true),
            _ => return None,
        };
        let normalized = matches!(
            target.texture().format(),
            wgpu::TextureFormat::R16Unorm | wgpu::TextureFormat::Rgba16Unorm
        );
        let kind = match (multisampled, normalized) {
            (false, false) => CopyKind::Depth,
            (true, false) => CopyKind::DepthMultisampled,
            (false, true) => CopyKind::Depth16,
            (true, true) => CopyKind::Depth16Multisampled,
        };
        Some(DepthCopy {
            depth: depth.clone(),
            target,
            kind,
        })
    }

    /// Ensures a depth-only render texture (`format == 5`) has its filterable color copy when a
    /// draw or dispatch samples it outside a depth/comparison slot: created lazily (one channel,
    /// or four where the device's depth samples fill the color channels) and brought up to date
    /// before that read. Shadow maps sampled only through depth/comparison slots never allocate or
    /// copy this texture.
    /// Returns whether it made or upgraded the copy now (a draw brings it up to date before itself
    /// through `depth_reads`; a dispatch through `dispatch_fills`).
    fn fill_depth_copy(&mut self, handle: u64) -> bool {
        let Some(t) = self.textures.get(&handle) else {
            return false;
        };
        if t.format != 5 || t.layers != 1 || t.depth_sample.is_none() {
            return false;
        }
        let filled = resources::depth_copy_fills_color();
        if let Some(copy) = &t.depth_copy
            && (!filled || resources::filled_depth_copy(copy.texture().format()))
        {
            return false;
        }
        let Some(depth_format) = t.depth_format() else {
            return false;
        };
        let view =
            resources::depth_copy_view(&self.device, t.width, t.height, depth_format, filled);
        self.generation += 1;
        let generation = self.generation;
        let current = {
            let (color, depth) = self.target_handles;
            depth_owner(color, depth) == Some(handle)
        };
        let t = self.textures.get_mut(&handle).unwrap();
        t.depth_copy = Some(view);
        t.generation = generation;
        t.bind_groups = [None, None];
        self.evict_texture_groups(handle);
        if current && self.depth_target.is_none() {
            self.depth_target = Some(handle);
        }
        self.depth_reads.insert(handle, true);
        true
    }

    /// A render target's depth element for a float slot: its depth copy, made the first time
    /// (then brought up to date before the draw that samples it), and kept up to date after the
    /// passes that draw into it. None (with an error) for a texture without a depth buffer.
    fn depth_element_view(&mut self, handle: u64) -> Option<wgpu::TextureView> {
        let Some(t) = self.textures.get(&handle) else {
            self.warn_once(format!("depth-element-missing{handle}"), || {
                format!("a draw samples the depth of texture {handle}, which does not exist; the slot reads its default")
            });
            return None;
        };
        if t.depth_sample.is_none() && t.depth_msaa.is_none() {
            self.warn_once(format!("depth-element-none{handle}"), || {
                format!("a draw samples the depth of texture {handle}, which has no depth buffer; the slot reads its default")
            });
            return None;
        }
        if t.layers > 1 || t.volume {
            self.warn_once(format!("depth-element-layers{handle}"), || {
                format!("a draw samples the depth of layered render texture {handle}, which is not supported; the slot reads its default")
            });
            return None;
        }
        let current = {
            let (color, depth) = self.target_handles;
            depth_owner(color, depth) == Some(handle)
        };
        if let Some(view) = &t.depth_copy {
            let view = view.clone();
            self.depth_reads
                .entry(handle)
                .and_modify(|needed| *needed |= current)
                .or_insert(current);
            return Some(view);
        }
        let (width, height) = (t.width, t.height);
        let depth_format = t.depth_format()?;
        // Sampled through a float slot: four channels where the device's depth samples fill them.
        let view = resources::depth_copy_view(
            &self.device,
            width,
            height,
            depth_format,
            resources::depth_copy_fills_color(),
        );
        self.generation += 1;
        let generation = self.generation;
        let t = self.textures.get_mut(&handle).unwrap();
        t.depth_copy = Some(view.clone());
        t.generation = generation;
        t.bind_groups = [None, None];
        self.evict_texture_groups(handle);
        if current && self.depth_target.is_none() {
            self.depth_target = Some(handle);
        }
        self.depth_reads.insert(handle, true);
        Some(view)
    }

    /// What an integer-texel array slot with nothing bound reads: the bits of Unity's default
    /// array texture, one gray texel (8-bit 128, sampled through an sRGB view in a Linear color
    /// space project, as the default textures are), read as a float texture's bits are (by the
    /// same copy); zeros outside it (measured on OpenGL Core and Vulkan: tests/projects/rtdepth
    /// controls ctl.uintArray). Made once; the 1x1 copy runs before every draw that reads it,
    /// as the copies of bound textures do.
    fn integer_array_default(&mut self, signed: bool, space: i32) -> wgpu::TextureView {
        /// The default array texture's gray (measured: ctl.uintArray's alpha, 128/255).
        const GRAY: u8 = 128;
        let key = (signed, space == 1);
        let kind = if signed {
            CopyKind::SintBits
        } else {
            CopyKind::UintBits
        };
        if let Some((source, target, view)) = self.integer_array_defaults.get(&key) {
            let (source, target, view) = (source.clone(), target.clone(), view.clone());
            self.pre_draw.push(DepthCopy {
                depth: source,
                target,
                kind,
            });
            return view;
        }
        let one = wgpu::Extent3d {
            width: 1,
            height: 1,
            depth_or_array_layers: 1,
        };
        let gray = self.device.create_texture(&wgpu::TextureDescriptor {
            label: Some("unbound default array gray"),
            size: one,
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Rgba8Unorm,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
            view_formats: &[wgpu::TextureFormat::Rgba8UnormSrgb],
        });
        self.queue.write_texture(
            gray.as_image_copy(),
            &[GRAY; 4],
            wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(4),
                rows_per_image: Some(1),
            },
            one,
        );
        let source = gray.create_view(&wgpu::TextureViewDescriptor {
            format: key.1.then_some(wgpu::TextureFormat::Rgba8UnormSrgb),
            ..Default::default()
        });
        let bits = self.device.create_texture(&wgpu::TextureDescriptor {
            label: Some("unbound integer array default"),
            size: one,
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: if signed {
                wgpu::TextureFormat::Rgba32Sint
            } else {
                wgpu::TextureFormat::Rgba32Uint
            },
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::TEXTURE_BINDING,
            view_formats: &[],
        });
        let target = bits.create_view(&wgpu::TextureViewDescriptor::default());
        self.pre_draw.push(DepthCopy {
            depth: source.clone(),
            target: target.clone(),
            kind,
        });
        let view = bits.create_view(&wgpu::TextureViewDescriptor {
            dimension: Some(wgpu::TextureViewDimension::D2Array),
            ..Default::default()
        });
        self.integer_array_defaults
            .insert(key, (source, target, view.clone()));
        view
    }

    /// Zeros for an integer-texel slot (signed or unsigned).
    fn integer_default(&mut self, signed: bool) -> wgpu::TextureView {
        if let Some(view) = &self.integer_defaults[signed as usize] {
            return view.clone();
        }
        let format = if signed {
            wgpu::TextureFormat::Rgba32Sint
        } else {
            wgpu::TextureFormat::Rgba32Uint
        };
        // New textures are zeroed.
        let view = self
            .device
            .create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound integer default"),
                size: wgpu::Extent3d {
                    width: 1,
                    height: 1,
                    depth_or_array_layers: 1,
                },
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format,
                usage: wgpu::TextureUsages::TEXTURE_BINDING,
                view_formats: &[],
            })
            .create_view(&wgpu::TextureViewDescriptor::default());
        self.integer_defaults[signed as usize] = Some(view.clone());
        view
    }

    /// The texture a [`ChannelCopy`] draws its source level into, kept for later copies of that
    /// format and size (the copies of one submission follow each other on the GPU, so they can
    /// share it; a camera seeded from the screen copies it every frame).
    fn repack_texture(
        &mut self,
        format: wgpu::TextureFormat,
        size: wgpu::Extent3d,
        sampled: bool,
    ) -> RepackTexture {
        if let Some(entry) = self.repack_textures.iter().find(|entry| {
            entry.texture.format() == format
                && entry.texture.size() == size
                && entry.alternate.is_some() == sampled
        }) {
            return entry.clone();
        }
        let alternate = sampled.then(|| {
            if format.is_srgb() {
                format.remove_srgb_suffix()
            } else {
                format.add_srgb_suffix()
            }
        });
        let texture = self.device.create_texture(&wgpu::TextureDescriptor {
            label: Some("unbound channel copy"),
            size,
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                | wgpu::TextureUsages::COPY_SRC
                | if sampled {
                    wgpu::TextureUsages::TEXTURE_BINDING
                } else {
                    wgpu::TextureUsages::empty()
                },
            view_formats: alternate.as_slice(),
        });
        // A few shapes at a time (a resized screen leaves its old size behind).
        if self.repack_textures.len() >= REPACK_TEXTURES_KEPT {
            self.repack_textures.remove(0);
        }
        let entry = RepackTexture {
            target: texture.create_view(&Default::default()),
            alternate: alternate.map(|format| {
                texture.create_view(&wgpu::TextureViewDescriptor {
                    format: Some(format),
                    ..Default::default()
                })
            }),
            texture,
        };
        self.repack_textures.push(entry.clone());
        entry
    }

    fn framebuffer_copy_resources(
        &mut self,
        source: &wgpu::TextureView,
        format: wgpu::TextureFormat,
        transfer: FramebufferTransfer,
    ) -> (wgpu::RenderPipeline, wgpu::BindGroup) {
        let resize = transfer == FramebufferTransfer::ResizeToDisplay;
        let layout = if resize {
            self.pipelines.texture_layout.clone()
        } else {
            self.framebuffer_resolve_layout
                .get_or_insert_with(|| Self::texture_copy_layout(&self.device, false))
                .clone()
        };
        let pipeline = self
            .framebuffer_resolve_pipelines
            .entry((format, transfer))
            .or_insert_with(|| {
                let shader = match transfer {
                    FramebufferTransfer::LinearToDisplay => CHANNEL_COPY_WGSL.into(),
                    FramebufferTransfer::ResizeToDisplay => CHANNEL_COPY_WGSL.replace(
                        "textureLoad(source, vec2<i32>(p.xy), 0)", "resample_display(p.xy)",
                    ) + FRAMEBUFFER_RESIZE_WGSL,
                    FramebufferTransfer::SrgbToTexture => CHANNEL_COPY_WGSL
                        .replace("return texel;", "return vec4<f32>(texel.rgb, 1.0);"),
                    FramebufferTransfer::UnormToTexture => {
                        let output = match format {
                            wgpu::TextureFormat::R16Float
                            | wgpu::TextureFormat::Rg16Float
                            | wgpu::TextureFormat::Rgba16Float =>
                                "vec3<f32>(truncate_half(rgb.r), truncate_half(rgb.g), truncate_half(rgb.b))",
                            _ => "rgb",
                        };
                        CHANNEL_COPY_WGSL.replace(
                            "return texel;",
                            &format!("let bytes = vec3<u32>(round(clamp(texel.rgb, vec3<f32>(0.0), vec3<f32>(1.0)) * 255.0));
                                      let rgb = vec3<f32>(expand_byte(bytes.r), expand_byte(bytes.g), expand_byte(bytes.b));
                                      return vec4<f32>({output}, 1.0);"),
                        ) + FRAMEBUFFER_UNORM_COPY_WGSL
                    }
                };
                let layouts = [Some(&layout), Some(&self.pipelines.draw_layout)];
                let count = if resize { 2 } else { 1 };
                Self::texture_copy_pipeline(&self.device, shader, format, &layouts[..count])
            })
            .clone();
        if let Some((_, _, group)) = self
            .framebuffer_resolve_groups
            .iter()
            .find(|(view, filtered, _)| view == source && *filtered == resize)
        {
            return (pipeline, group.clone());
        }
        let group = if resize {
            let sampler = self.sampler(SamplerKey::LINEAR_CLAMP);
            self.device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound display resize"),
                layout: &layout,
                entries: &[
                    wgpu::BindGroupEntry {
                        binding: 0,
                        resource: wgpu::BindingResource::TextureView(source),
                    },
                    wgpu::BindGroupEntry {
                        binding: 1,
                        resource: wgpu::BindingResource::Sampler(&sampler),
                    },
                ],
            })
        } else {
            Self::channel_copy_group(&self.device, &layout, source)
        };
        if self.framebuffer_resolve_groups.len() >= REPACK_TEXTURES_KEPT {
            self.framebuffer_resolve_groups.remove(0);
        }
        self.framebuffer_resolve_groups
            .push((source.clone(), resize, group.clone()));
        (pipeline, group)
    }

    fn channel_copy_group(
        device: &wgpu::Device,
        layout: &wgpu::BindGroupLayout,
        source: &wgpu::TextureView,
    ) -> wgpu::BindGroup {
        device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("unbound channel copy"),
            layout,
            entries: &[wgpu::BindGroupEntry {
                binding: 0,
                resource: wgpu::BindingResource::TextureView(source),
            }],
        })
    }

    /// The pipeline that writes a depth buffer's depth into a float color texture (of four
    /// channels when `filled`: [`resources::filled_depth_copy`]).
    fn depth_copy_pipeline(
        &mut self,
        kind: CopyKind,
        samples: u32,
        filled: bool,
    ) -> (wgpu::RenderPipeline, wgpu::BindGroupLayout) {
        let index = (kind.index() * COPY_SAMPLE_CLASSES
            + samples.max(1).min(8).trailing_zeros() as usize)
            * 2
            + filled as usize;
        if let Some(p) = &self.depth_copy_pipelines[index] {
            return p.clone();
        }
        let device = &self.device;
        let source = match kind {
            CopyKind::Depth | CopyKind::Depth16 => DEPTH_COPY_WGSL.to_string(),
            CopyKind::DepthMultisampled | CopyKind::Depth16Multisampled => DEPTH_COPY_WGSL
                .replace("texture_2d<f32>", "texture_multisampled_2d<f32>")
                .replace(
                    "vec2<i32>(p.xy), 0)",
                    &format!(
                        "vec2<i32>(p.xy), {})",
                        self.conventions.depth_resolve_sample(samples)
                    ),
                ),
            CopyKind::UintBits => BITS_COPY_WGSL.replace("SCALAR", "u32"),
            CopyKind::SintBits => BITS_COPY_WGSL.replace("SCALAR", "i32"),
            CopyKind::ToR8 | CopyKind::ToRgba8 | CopyKind::ToBgra8 => CHANNEL_COPY_WGSL.into(),
            CopyKind::SwapRedBlue => {
                CHANNEL_COPY_WGSL.replace("return texel;", "return texel.bgra;")
            }
            CopyKind::Packed565 | CopyKind::Packed5551 | CopyKind::Packed4444 => {
                let bits = match kind {
                    CopyKind::Packed565 => resources::PACKED_565_BITS,
                    CopyKind::Packed5551 => resources::PACKED_5551_BITS,
                    _ => resources::PACKED_4444_BITS,
                };
                PACKED_TEXELS_WGSL
                    .replace(
                        "BITS",
                        &format!(
                            "vec4<u32>({}u, {}u, {}u, {}u)",
                            bits[0], bits[1], bits[2], bits[3]
                        ),
                    )
                    .replace(
                        "ROUNDS",
                        if self.conventions.packed_writes_round {
                            "true"
                        } else {
                            "false"
                        },
                    )
            }
        };
        let target_format = match kind {
            CopyKind::Depth | CopyKind::DepthMultisampled => {
                resources::float_depth_copy_format(device, filled)
            }
            CopyKind::Depth16 | CopyKind::Depth16Multisampled => {
                resources::normalized_depth_copy_format(filled).0
            }
            CopyKind::UintBits => wgpu::TextureFormat::Rgba32Uint,
            CopyKind::SintBits => wgpu::TextureFormat::Rgba32Sint,
            CopyKind::ToR8 => wgpu::TextureFormat::R8Unorm,
            CopyKind::ToRgba8
            | CopyKind::SwapRedBlue
            | CopyKind::Packed565
            | CopyKind::Packed5551
            | CopyKind::Packed4444 => wgpu::TextureFormat::Rgba8Unorm,
            CopyKind::ToBgra8 => wgpu::TextureFormat::Bgra8Unorm,
        };
        let multisampled = matches!(
            kind,
            CopyKind::DepthMultisampled | CopyKind::Depth16Multisampled
        );
        let layout = Self::texture_copy_layout(device, multisampled);
        let pipeline = Self::texture_copy_pipeline(device, source, target_format, &[Some(&layout)]);
        let result = (pipeline, layout);
        self.depth_copy_pipelines[index] = Some(result.clone());
        result
    }

    fn texture_copy_layout(device: &wgpu::Device, multisampled: bool) -> wgpu::BindGroupLayout {
        device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound texture copy"),
            entries: &[wgpu::BindGroupLayoutEntry {
                binding: 0,
                visibility: wgpu::ShaderStages::FRAGMENT,
                ty: wgpu::BindingType::Texture {
                    sample_type: wgpu::TextureSampleType::Float { filterable: false },
                    view_dimension: wgpu::TextureViewDimension::D2,
                    multisampled,
                },
                count: None,
            }],
        })
    }

    fn texture_copy_pipeline(
        device: &wgpu::Device,
        source: String,
        target_format: wgpu::TextureFormat,
        layouts: &[Option<&wgpu::BindGroupLayout>],
    ) -> wgpu::RenderPipeline {
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("unbound texture copy"),
            source: wgpu::ShaderSource::Wgsl(source.into()),
        });
        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("unbound texture copy"),
            bind_group_layouts: layouts,
            immediate_size: 0,
        });
        device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("unbound texture copy"),
            layout: Some(&pipeline_layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("vs"),
                compilation_options: Default::default(),
                buffers: &[],
            },
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some("fs"),
                compilation_options: Default::default(),
                targets: &[Some(wgpu::ColorTargetState {
                    format: target_format,
                    blend: None,
                    write_mask: wgpu::ColorWrites::ALL,
                })],
            }),
            primitive: wgpu::PrimitiveState::default(),
            depth_stencil: None,
            multisample: wgpu::MultisampleState::default(),
            multiview_mask: None,
            cache: None,
        })
    }

    /// A `CopyDepth`: the source's sampled depth and the destination's depth attachment (0: the
    /// screen's), of one size. None, with a message, when either has no depth or the sizes differ.
    fn plan_depth_blit(&mut self, source: u64, destination: u64) -> Option<DepthBlit> {
        let end = |this: &mut Self, handle: u64| -> Option<(wgpu::TextureView, u32, (u32, u32))> {
            if handle == 0 {
                let s = this.ensure_screen(this.screen_slot);
                return Some((s.depth_sample.clone(), 1, (s.width, s.height)));
            }
            let t = this.textures.get(&handle)?;
            let view = t.depth_sample.clone().or_else(|| t.depth_msaa.clone())?;
            Some((view, t.samples.max(1), (t.width, t.height)))
        };
        let from = end(self, source);
        let to_view = if destination == 0 {
            Some((
                self.ensure_screen(self.screen_slot).depth.clone(),
                DEPTH_FORMAT,
            ))
        } else {
            self.textures
                .get(&destination)
                .and_then(|t| Some((t.depth.clone()?, t.depth_format()?)))
        };
        let to = end(self, destination);
        #[cfg(target_os = "linux")]
        let native_after = self
            .vulkan_plugins
            .as_mut()
            .and_then(|p| p.depth_resolve(destination, self.frame_number + 1));
        // A depth copy between a top-down screen and a texture mirrors the rows (measured on
        // Metal and Vulkan: tests/projects/camerastack `metal/*` and `vulkan/*`
        // *.full.bH.def.persp).
        let flip = self.conventions.top_down(true)
            && self.stored_as_screen(source) != self.stored_as_screen(destination);
        match (from, to, to_view) {
            (
                Some((source, source_samples, size)),
                Some((_, samples, to_size)),
                Some((destination, format)),
            ) if size == to_size => Some(DepthBlit {
                #[cfg(target_os = "linux")]
                native: None,
                #[cfg(target_os = "linux")]
                native_after,
                resolve_sample: 0,
                group: None,
                source,
                source_samples,
                destination,
                format,
                samples,
                flip,
            }),
            _ => {
                self.warn_once(format!("copy-depth{source}/{destination}"), || {
                    format!("CopyDepth from {source} to {destination}: both need a depth buffer of one size; skipped")
                });
                None
            }
        }
    }

    fn native_depth_resolve(&mut self, handle: u64) -> Option<DepthBlit> {
        let publication = self.native_depth_publication(handle)?;
        // Vulkan already published both aspects at the preceding attachment-write boundary.
        // Resolving or sampling it again preserves later native edits (nativegfx forcedSelf).
        #[cfg(target_os = "linux")]
        if publication.native.is_some() {
            return None;
        }
        Some(publication)
    }

    fn native_depth_publication(&mut self, handle: u64) -> Option<DepthBlit> {
        let gpu = self.textures.get(&handle)?;
        let resolve = gpu.native_depth.as_ref()?;
        Some(DepthBlit {
            #[cfg(target_os = "linux")]
            native: self
                .vulkan_plugins
                .as_mut()
                .and_then(|p| p.depth_resolve(handle, self.frame_number + 1)),
            #[cfg(target_os = "linux")]
            native_after: None,
            resolve_sample: self.conventions.depth_resolve_sample(gpu.samples),
            group: resolve.group.clone(),
            source: gpu.depth_msaa.clone()?,
            source_samples: gpu.samples,
            destination: resolve.view.clone(),
            samples: 1,
            format: resolve.view.texture().format(),
            flip: false,
        })
    }

    /// The pipeline that draws a depth buffer into another (`CopyDepth`).
    fn depth_blit_pipeline(
        &mut self,
        multisampled: bool,
        samples: u32,
        per_sample: bool,
        format: wgpu::TextureFormat,
        resolve_sample: u32,
        flip: bool,
    ) -> (wgpu::RenderPipeline, wgpu::BindGroupLayout) {
        let key = (
            multisampled,
            samples,
            per_sample,
            format,
            resolve_sample,
            flip,
        );
        if let Some(p) = self.depth_blit_pipelines.get(&key) {
            return p.clone();
        }
        let source = if per_sample {
            DEPTH_BLIT_PER_SAMPLE_WGSL.to_string()
        } else if multisampled {
            DEPTH_BLIT_WGSL
                .replace("texture_2d<f32>", "texture_multisampled_2d<f32>")
                .replace(
                    "vec2<i32>(p.xy), 0)",
                    &format!("vec2<i32>(p.xy), {resolve_sample})"),
                )
        } else {
            DEPTH_BLIT_WGSL.to_string()
        };
        let source = match flip {
            true => source.replace(
                "vec2<i32>(p.xy)",
                "vec2<i32>(i32(p.x), i32(textureDimensions(depth).y) - 1 - i32(p.y))",
            ),
            false => source,
        };
        let device = &self.device;
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("unbound depth blit"),
            source: wgpu::ShaderSource::Wgsl(source.into()),
        });
        let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound depth blit"),
            entries: &[wgpu::BindGroupLayoutEntry {
                binding: 0,
                visibility: wgpu::ShaderStages::FRAGMENT,
                ty: wgpu::BindingType::Texture {
                    sample_type: wgpu::TextureSampleType::Float { filterable: false },
                    view_dimension: wgpu::TextureViewDimension::D2,
                    multisampled,
                },
                count: None,
            }],
        });
        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("unbound depth blit"),
            bind_group_layouts: &[Some(&layout)],
            immediate_size: 0,
        });
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("unbound depth blit"),
            layout: Some(&pipeline_layout),
            vertex: wgpu::VertexState {
                module: &module,
                entry_point: Some("vs"),
                compilation_options: Default::default(),
                buffers: &[],
            },
            fragment: Some(wgpu::FragmentState {
                module: &module,
                entry_point: Some("fs"),
                compilation_options: Default::default(),
                targets: &[],
            }),
            primitive: wgpu::PrimitiveState::default(),
            depth_stencil: Some(wgpu::DepthStencilState {
                format,
                depth_write_enabled: Some(true),
                depth_compare: Some(wgpu::CompareFunction::Always),
                stencil: wgpu::StencilState::default(),
                bias: wgpu::DepthBiasState::default(),
            }),
            multisample: wgpu::MultisampleState {
                count: samples,
                ..Default::default()
            },
            multiview_mask: None,
            cache: None,
        });
        self.depth_blit_pipelines.insert(key, (pipeline, layout));
        self.depth_blit_pipelines[&key].clone()
    }

    /// A 1x1 depth texture at the far plane (1): what an unbound depth texture reads, so that
    /// comparisons against it pass (an absent shadow map shadows nothing).
    fn depth_default(&mut self, cube: bool) -> wgpu::TextureView {
        if let Some(view) = &self.depth_default[cube as usize] {
            return view.clone();
        }
        let layers = if cube { 6 } else { 1 };
        // Cleared to the far depth of the device.
        let far = self.conventions.depth(1.0);
        let texture = self.device.create_texture(&wgpu::TextureDescriptor {
            label: Some("unbound default depth"),
            size: wgpu::Extent3d {
                width: 1,
                height: 1,
                depth_or_array_layers: layers,
            },
            mip_level_count: 1,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Depth32Float,
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::TEXTURE_BINDING,
            view_formats: &[],
        });
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor::default());
        for layer in 0..layers {
            let face = texture.create_view(&wgpu::TextureViewDescriptor {
                dimension: Some(wgpu::TextureViewDimension::D2),
                base_array_layer: layer,
                array_layer_count: Some(1),
                ..Default::default()
            });
            encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("unbound default depth clear"),
                color_attachments: &[],
                depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                    view: &face,
                    depth_ops: Some(wgpu::Operations {
                        load: wgpu::LoadOp::Clear(far),
                        store: wgpu::StoreOp::Store,
                    }),
                    stencil_ops: None,
                }),
                timestamp_writes: None,
                occlusion_query_set: None,
                multiview_mask: None,
            });
        }
        self.queue.submit([encoder.finish()]);
        let view = texture.create_view(&wgpu::TextureViewDescriptor {
            dimension: Some(if cube {
                wgpu::TextureViewDimension::Cube
            } else {
                wgpu::TextureViewDimension::D2
            }),
            ..Default::default()
        });
        self.depth_default[cube as usize] = Some(view.clone());
        view
    }

    /// The error shader's program.
    fn error_program(&mut self) -> unbound_shaders::Program {
        if let Some(p) = &self.error_program {
            return p.clone();
        }
        let lab = self.resolver.error_shader();
        let material = owned::Material {
            shader: lab.name.clone(),
            ..Default::default()
        };
        let program = self
            .resolver
            .resolve(&material)
            .passes
            .first()
            .map(|p| p.program.clone())
            .expect("the error shader has a pass");
        self.error_program = Some(program.clone());
        program
    }

    fn push_uniform(&mut self, u: DrawUniform) -> u32 {
        self.uniforms.push(u);
        ((self.uniforms.len() - 1) as u64 * DRAW_SLOT) as u32
    }

    /// The screen target of a ring slot, (re)created at the frame's size.
    fn ensure_screen(&mut self, slot: usize) -> &ScreenTarget {
        let (width, height) = self.screen_size;
        let fits = self.screens[slot]
            .as_ref()
            .is_some_and(|s| s.width == width && s.height == height);
        if !fits {
            let size = wgpu::Extent3d {
                width,
                height,
                depth_or_array_layers: 1,
            };
            let format = self.conventions.screen_format();
            let texture = self.device.create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound screen"),
                size,
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format,
                usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                    | wgpu::TextureUsages::TEXTURE_BINDING
                    | wgpu::TextureUsages::COPY_SRC
                    | wgpu::TextureUsages::COPY_DST,
                view_formats: &[format.add_srgb_suffix()],
            });
            let color = texture.create_view(&Default::default());
            let srgb = texture.create_view(&wgpu::TextureViewDescriptor {
                format: Some(format.add_srgb_suffix()),
                ..Default::default()
            });
            // The other slots' depth when it has this size: the screen keeps its depth.
            let shared = self
                .screens
                .iter()
                .flatten()
                .find(|s| s.width == width && s.height == height)
                .map(|s| {
                    (
                        s.depth.clone(),
                        s.depth_sample.clone(),
                        s.depth_texture.clone(),
                    )
                });
            let (depth, depth_sample, depth_texture) = shared.unwrap_or_else(|| {
                let texture = self.device.create_texture(&wgpu::TextureDescriptor {
                    label: Some("unbound screen depth"),
                    size,
                    mip_level_count: 1,
                    sample_count: 1,
                    dimension: wgpu::TextureDimension::D2,
                    format: DEPTH_FORMAT,
                    usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                        | wgpu::TextureUsages::TEXTURE_BINDING,
                    view_formats: &[],
                });
                let sample = texture.create_view(&wgpu::TextureViewDescriptor {
                    aspect: wgpu::TextureAspect::DepthOnly,
                    ..Default::default()
                });
                (texture.create_view(&Default::default()), sample, texture)
            });
            self.screens[slot] = Some(ScreenTarget {
                width,
                height,
                texture,
                color,
                srgb,
                depth,
                depth_sample,
                depth_texture,
            });
        }
        self.screens[slot].as_ref().unwrap()
    }

    /// Whether a texture (0: the screen) is stored as the screen is: the screen and the render
    /// texture standing for it.
    fn stored_as_screen(&self, texture: u64) -> bool {
        texture == 0 || texture == self.screen_stand_in
    }

    /// The screen as a target, its bytes written as `color_space` encodes them.
    fn screen_target(&mut self, color_space: i32) -> Target {
        let s = self.ensure_screen(self.screen_slot);
        let (color, format) = s.view(color_space);
        Target {
            color: Some(color.clone()),
            depth_slice: None,
            resolve: None,
            samples: 1,
            depth: Some(s.depth.clone()),
            depth_format: Some(DEPTH_FORMAT),
            format: Some(format),
            alpha_one: false,
            width: s.width,
            height: s.height,
            subresource: (0, 0),
            screen: true,
            screen_like: true,
            extra: Vec::new(),
            texture: Some((s.texture.clone(), 0, 0)),
            auto_mips: false,
            defer_mips: None,
            packed: 0,
        }
    }

    /// The target `SetTarget` names: the screen or a render texture.
    fn resolve_target(&mut self, color: u64, depth: u64, mip: i32, slice: i32) -> Option<Target> {
        // Stored as the screen is when its color or its depth is the screen's stand-in; a target
        // pairing the stand-in with another texture draws both as the stand-in is stored
        // (unmeasured: the Editor draws a screen camera into the screen itself).
        let stand_in = self.screen_stand_in;
        let screen_like = stand_in != 0 && (color == stand_in || depth == stand_in);
        let other_depth = depth != 0 && depth != commands::NO_TEXTURE && depth != stand_in;
        if screen_like && (color != stand_in || other_depth) {
            self.warn_once("stand-in-mixed".into(), || {
                "a target pairs the screen camera's target with another texture: both are drawn as the screen is stored (unmeasured)".into()
            });
        }
        // GL.sRGBWrite off: an sRGB target is bound as the plain format it is stored in.
        let space = if self.state.no_srgb_write {
            0
        } else {
            self.color_space
        };
        let mut target = if color == commands::NO_TEXTURE {
            // A None target: draws and clears go nowhere.
            return None;
        } else if color == 0 {
            let screen_space = if self.state.gamma_framebuffer_srgb {
                1
            } else {
                space
            };
            self.screen_target(screen_space)
        } else {
            let Some(t) = self.textures.get(&color).filter(|t| t.render_target) else {
                self.warn_once(format!("rt{color}"), || {
                    format!("rendering into texture {color}, which is not a render target: skipped")
                });
                return None;
            };
            let layers = if t.volume {
                t.layers.checked_shr(mip.max(0) as u32).unwrap_or(0).max(1)
            } else {
                t.layers
            };
            if mip < 0 || mip as u32 >= t.mip_count || slice < 0 || slice as u32 >= layers {
                self.warn_once(format!("rt-sub{color}/{mip}/{slice}"), || {
                    format!("render target {color}: mip {mip} / slice {slice} is out of range; draw skipped")
                });
                return None;
            }
            let mip = mip as u32;
            let layer = slice as u32;
            let depth_view = t
                .depth_texture
                .as_ref()
                .filter(|d| mip < d.mip_level_count())
                .map(|d| {
                    d.create_view(&wgpu::TextureViewDescriptor {
                        dimension: Some(wgpu::TextureViewDimension::D2),
                        base_mip_level: mip,
                        mip_level_count: Some(1),
                        base_array_layer: layer,
                        array_layer_count: Some(1),
                        ..Default::default()
                    })
                });
            if t.depth_only && depth_view.is_none() {
                self.warn_once(format!("rt-depth-only{color}/{mip}"), || {
                    format!("render target {color} is depth-only and has no depth at mip {mip}: draw skipped")
                });
                return None;
            }
            if t.depth_only {
                Target {
                    color: None,
                    depth_slice: None,
                    resolve: None,
                    samples: 1,
                    depth_format: t.depth_format(),
                    depth: depth_view,
                    format: None,
                    alpha_one: false,
                    width: (t.width >> mip).max(1),
                    height: (t.height >> mip).max(1),
                    subresource: (layer, mip),
                    screen: false,
                    screen_like,
                    extra: Vec::new(),
                    texture: None,
                    auto_mips: false,
                    defer_mips: None,
                    packed: 0,
                }
            } else {
                let view = if t.volume {
                    let (raw, srgb) = &t.volume_targets[mip as usize];
                    if space == 1 {
                        srgb.as_ref().unwrap_or(raw)
                    } else {
                        raw
                    }
                    .clone()
                } else {
                    t.texture.create_view(&wgpu::TextureViewDescriptor {
                        format: Some(t.target_format(space)),
                        usage: resources::view_usage(&t.texture, Some(t.target_format(space))),
                        dimension: Some(wgpu::TextureViewDimension::D2),
                        base_mip_level: mip,
                        mip_level_count: Some(1),
                        base_array_layer: layer,
                        array_layer_count: Some(1),
                        ..Default::default()
                    })
                };
                let (color_view, resolve, samples) = match &t.msaa {
                    Some((raw, srgb)) if mip == 0 => {
                        let samples = match (srgb, space) {
                            (Some(srgb), 1) => srgb.clone(),
                            _ => raw.clone(),
                        };
                        (samples, Some(view), t.samples)
                    }
                    _ => (view, None, 1),
                };
                let generate_mips = t.auto_generate_mips
                    && mip == 0
                    && t.mip_count > 1
                    && !resources::is_integer(t.texture.format());
                Target {
                    color: Some(color_view),
                    depth_slice: t.volume.then_some(layer),
                    resolve,
                    samples,
                    depth_format: depth_view.as_ref().and(t.depth_format()),
                    depth: depth_view,
                    format: Some(t.target_format(space)),
                    alpha_one: t.format == texture_format::RGB16_SNORM,
                    width: (t.width >> mip).max(1),
                    height: (t.height >> mip).max(1),
                    subresource: (layer, mip),
                    screen: false,
                    screen_like,
                    extra: Vec::new(),
                    texture: Some((t.texture.clone(), layer, mip)),
                    auto_mips: generate_mips && !t.defer_mips,
                    defer_mips: (generate_mips && t.defer_mips)
                        .then_some(t.target_format(self.color_space)),
                    packed: t.packed,
                }
            }
        };
        if depth == commands::NO_TEXTURE {
            target.depth = None;
            target.depth_format = None;
        } else if depth != 0 && depth != color {
            let separate = self
                .textures
                .get(&depth)
                .and_then(|d| Some((d.depth.clone()?, d.depth_format()?)));
            target.depth_format = separate.as_ref().map(|(_, format)| *format);
            target.depth = separate.map(|(view, _)| view);
        }
        if target.color.is_none() && target.depth.is_none() {
            // A depth-only target paired with no depth (or a texture without one): nothing to
            // draw into (unmeasured).
            self.warn_once(format!("rt-no-attachment{color}/{depth}"), || {
                format!("depth-only render target {color} is bound without a depth buffer: draws and clears skipped")
            });
            return None;
        }
        Some(target)
    }

    /// Whether a target command keeps the same framebuffer attachments.
    fn same_target(&self, color: u64, depth: u64, mip: i32, slice: i32, extra: &[u64]) -> bool {
        self.target_handles == (color, depth)
            && self.state.target.as_ref().is_some_and(|target| {
                target.subresource == (slice.max(0) as u32, mip.max(0) as u32)
                    && target.extra.len() == extra.len()
                    && target.extra.iter().zip(extra).all(|(bound, handle)| {
                        bound
                            .texture
                            .as_ref()
                            .zip(self.textures.get(handle))
                            .is_some_and(|((texture, _, _), resource)| *texture == resource.texture)
                    })
            })
    }

    /// Pending automatic updates become visible when drawing prepares new attachments,
    /// including a new slice of a volume (renderapi volumeMore.* and volumeAuto.*).
    fn process_mips(&mut self, operations: &mut Vec<Op>) {
        operations.extend(self.pending_mips.drain().map(|(texture, format)| {
            let layers = texture.depth_or_array_layers();
            Op::Mips(texture, format, layers)
        }));
    }

    fn defer_target_mips(&mut self) {
        let Some(target) = self.state.target.as_ref() else {
            return;
        };
        for (format, image) in std::iter::once((target.defer_mips, &target.texture)).chain(
            target
                .extra
                .iter()
                .map(|extra| (extra.defer_mips, &extra.texture)),
        ) {
            if let (Some(format), Some((texture, _, _))) = (format, image) {
                self.pending_mips.insert(texture.clone(), format);
            }
        }
    }

    /// A packed target's level drawn into is packed again before anything reads it: before
    /// other attachments are prepared, a copy, and the end of each submission's work.
    fn mark_packed(&mut self, target: &Target) {
        let size = wgpu::Extent3d {
            width: target.width,
            height: target.height,
            depth_or_array_layers: 1,
        };
        let colors = std::iter::once((target.packed, &target.texture))
            .chain(target.extra.iter().map(|e| (e.packed, &e.texture)));
        for (packed, image) in colors {
            let kind = match packed {
                owned::PACKED_565 => CopyKind::Packed565,
                owned::PACKED_5551 => CopyKind::Packed5551,
                owned::PACKED_4444 => CopyKind::Packed4444,
                _ => continue,
            };
            let Some((texture, layer, level)) = image else {
                continue;
            };
            if texture.format() != wgpu::TextureFormat::Rgba8Unorm {
                self.warn_once(format!("packed-format{:?}", texture.format()), || {
                    "a packed render target is not stored as linear RGBA8: its channel bits are not kept".into()
                });
                continue;
            }
            self.pending_packed
                .insert((texture.clone(), *level, *layer), (size, kind));
        }
    }

    /// Each pending packed level drawn again in its channels' bits (`PACKED_TEXELS_WGSL`).
    fn process_packed(&mut self, operations: &mut Vec<Op>) {
        for ((texture, mip, layer), (size, kind)) in self.pending_packed.drain() {
            let origin = wgpu::Origin3d {
                x: 0,
                y: 0,
                z: layer,
            };
            operations.push(Op::Copy(Copy {
                channels: Some(ChannelCopy {
                    source: texture.create_view(&wgpu::TextureViewDescriptor {
                        label: Some("unbound packed texels"),
                        dimension: Some(wgpu::TextureViewDimension::D2),
                        base_mip_level: mip,
                        mip_level_count: Some(1),
                        base_array_layer: layer,
                        array_layer_count: Some(1),
                        ..Default::default()
                    }),
                    size,
                    transfer: ChannelTransfer::Storage(kind),
                }),
                source: texture.clone(),
                source_mip: mip,
                source_origin: origin,
                destination: texture,
                destination_mip: mip,
                destination_origin: origin,
                size,
            }));
        }
    }

    fn prepare_framebuffer(&mut self, operations: &mut Vec<Op>) {
        if self.framebuffer_dirty {
            self.process_packed(operations);
            self.process_mips(operations);
            self.defer_target_mips();
            self.framebuffer_dirty = false;
        }
    }

    fn new_pass(&mut self, target: Target) -> Pass {
        self.mark_packed(&target);
        // Vulkan publishes both aspects after ordinary attachment writes. OpenGL retains its
        // published image until a clear, shader read or explicit self-resolve.
        #[cfg(target_os = "linux")]
        let native_depth = if self.vulkan_plugins.is_some() {
            let handle = if self.target_handles.1 == 0 {
                self.target_handles.0
            } else {
                self.target_handles.1
            };
            self.native_depth_publication(handle).filter(|r| {
                r.native.is_some()
                    && target
                        .depth
                        .as_ref()
                        .is_some_and(|d| d.texture() == r.source.texture())
            })
        } else {
            None
        };
        #[cfg(not(target_os = "linux"))]
        let native_depth = None;
        let mut pass = Pass {
            native_depth,
            target,
            seed: None,
            color_load: wgpu::LoadOp::Load,
            depth_clear: None,
            items: Vec::new(),
            args: Vec::new(),
            windows: Vec::new(),
            writes_buffers: false,
        };
        if pass.target.screen {
            if !self.screen_touched {
                // The frame's screen starts as the previous frame left it, color and depth (the
                // depth buffer is the ring's): the Editor's Game view keeps them, so a first camera
                // that clears only depth, or nothing, draws over the last frame (Manual, Camera:
                // Don't Clear; measured: tests/projects/camerastack *.first.*). The first frame and
                // a new size start transparent black (tests/projects/screenread).
                pass.seed = self.previous_screen();
                if pass.seed.is_none() {
                    pass.color_load = wgpu::LoadOp::Clear(wgpu::Color::TRANSPARENT);
                    pass.depth_clear = Some((self.conventions.depth(1.0), 0));
                }
            }
            self.screen_touched = true;
        }
        pass
    }

    /// The previous frame's screen texture, when it has the current screen's size.
    fn previous_screen(&self) -> Option<wgpu::Texture> {
        let previous = (self.screen_slot + self.screens.len() - 1) % self.screens.len();
        let (width, height) = self.screen_size;
        self.screens[previous]
            .as_ref()
            .filter(|s| s.width == width && s.height == height && self.frame_number > 1)
            .map(|s| s.texture.clone())
    }

    /// Executes commands: plans them, then encodes and queues the work.
    pub fn execute(&mut self, commands: Vec<Command>) {
        // Bound each GL context-pin burst so long warmup shader compiles still yield regularly
        // to the presenting thread well below wgpu-hal's context lock timeout.
        const GL_PIN_BURST: std::time::Duration = std::time::Duration::from_millis(50);

        self.uniforms.clear();
        self.compute_bindings.begin();
        self.compiled.begin_frame();
        let time = self.started.elapsed().as_secs_f32();
        let device = self.device.clone();
        let pin_gles = self.is_gles && !commands.iter().any(|c| matches!(c, Command::Plugin(_)));
        let mut passes: Vec<Op> = Vec::new();
        // Whether the last pass is still open for the current target.
        let mut open = false;
        let mut end_frame = false;
        let mut draws = 0usize;
        // The position (`op_position`) where the ops into the depth target being drawn start.
        let mut depth_mark = self.encoded_ops;
        // The instances an instance multiplier adds to a mesh-instancing call, drawn after its
        // last instance, from its draw, before the next command (`multiplied_instances`).
        let mut added: Option<(MeshInstanceSlot, Box<commands::Draw>)> = None;
        let mut commands = commands.into_iter();
        let mut run_burst = |deadline: Option<std::time::Instant>| -> bool {
            loop {
                if deadline.is_some_and(|until| std::time::Instant::now() >= until) {
                    return false;
                }
                let command = match added.take() {
                    Some((slot, draw)) => {
                        self.state.mesh_instance = Some(slot);
                        Command::Draw(draw)
                    }
                    None => match commands.next() {
                        Some(command) => command,
                        None => break,
                    },
                };
                if !matches!(command, Command::Plugin(_)) {
                    self.plugin_pass_open = false;
                }
                match command {
                    Command::BeginFrame {
                        screen_width,
                        screen_height,
                        color_space,
                        fixed_game_view,
                    } => {
                        self.screen_size =
                            (screen_width.max(1) as u32, screen_height.max(1) as u32);
                        self.color_space = color_space.clamp(0, 1);
                        self.fixed_game_view = fixed_game_view;
                        self.screen_slot = (self.screen_slot + 1) % self.screens.len();
                        self.screen_touched = false;
                        self.frame_number += 1;
                        self.readback.frame(self.frame_number);
                        self.compute_bindings.frame(self.frame_number);
                        let cutoff = self
                            .frame_number
                            .saturating_sub(PROGRAM_BINDING_IDLE_FRAMES - 1);
                        self.program_texture_groups
                            .retain(|_, (last, _)| *last >= cutoff);
                        self.program_buffer_groups
                            .retain(|_, (last, _)| *last >= cutoff);
                        self.state.target = None;
                        self.target_handles = (0, 0);
                        #[cfg(target_os = "macos")]
                        {
                            // A frame begins its own encoder over the screen (unmeasured).
                            self.plugin_target_drawn = false;
                            self.plugin_redirect = Default::default();
                        }
                        self.depth_target = None;
                        self.state.depth_bias = [0.0; 2];
                        self.state.state_override = None;
                        open = false;
                    }
                    Command::ResolveTexture {
                        texture,
                        destination,
                    } => {
                        if texture == destination {
                            if let Some(depth) = self.native_depth_resolve(texture) {
                                passes.push(Op::DepthBlit(depth));
                            }
                            if let Some(copies) = self.resolve_msaa(texture) {
                                passes.extend(copies.map(Op::Copy));
                            }
                        } else if self
                            .textures
                            .get(&destination)
                            .is_none_or(|t| t.samples == 1)
                        {
                            self.prepare_screen_copy(texture, destination, &mut passes);
                            let source_view = self.bound_resolve_source(texture, destination);
                            if let Some(copy) = self.plan_copy(
                                texture,
                                source_view,
                                [0, 0, -1, -1],
                                destination,
                                [0, 0],
                                [0, 0],
                                false,
                            ) {
                                passes.push(Op::Copy(copy));
                            }
                        }
                        open = false;
                    }
                    Command::ResolveFramebuffer {
                        source,
                        destination,
                        retained,
                        screen_camera,
                        screen_rect,
                    } => {
                        if screen_rect.is_some() {
                            // The camera's intermediate supplies only its rectangle. Outside it
                            // the resolve reads the display that the camera is drawn over.
                            self.prepare_screen_copy(0, destination, &mut passes);
                            if let Some(copy) = self.plan_framebuffer_resolve(
                                0,
                                destination,
                                retained,
                                screen_camera,
                                None,
                            ) {
                                passes.push(Op::Copy(copy));
                            }
                        } else {
                            self.prepare_screen_copy(source, destination, &mut passes);
                        }
                        if let Some(copy) = self.plan_framebuffer_resolve(
                            source,
                            destination,
                            retained,
                            screen_camera,
                            screen_rect,
                        ) {
                            passes.push(Op::Copy(copy));
                        }
                        open = false;
                    }
                    Command::GenerateMips { texture } => {
                        self.process_packed(&mut passes);
                        if let Some(t) = self.textures.get(&texture) {
                            if resources::is_integer(t.texture.format()) {
                                self.warn_once(format!("mips{texture}"), || format!("GenerateMips cannot filter the integer render texture {texture}: skipped"));
                            } else if t.render_target {
                                passes.push(Op::Mips(
                                    t.texture.clone(),
                                    t.target_format(self.color_space),
                                    t.layers,
                                ));
                            } else {
                                self.warn_once(format!("mips{texture}"), || {
                                    format!(
                                        "GenerateMips requires a render target ({texture}); skipped"
                                    )
                                });
                            }
                        }
                        open = false;
                    }
                    Command::Plugin(call) => {
                        // The plugin sees everything before it drawn, and draws before what follows.
                        self.copy_depth(&mut passes, depth_mark);
                        // A Metal plugin's encoder over the screen is a use of it: the frame's screen
                        // starts there, as at a pass's first use.
                        #[cfg(target_os = "macos")]
                        if self.metal_plugins.is_some()
                            && !self.screen_touched
                            && (self.target_handles.0 == 0
                                || self.plugin_redirect != plugins_metal::Redirect::Target)
                        {
                            let target = self.screen_target(self.color_space);
                            let pass = self.new_pass(target);
                            passes.push(Op::Pass(pass));
                        }
                        self.encode(std::mem::take(&mut passes));
                        depth_mark = self.encoded_ops;
                        // Whether the current target's pass was drawing: a plugin event that does not
                        // care where it runs runs inside it on the Vulkan device.
                        self.plugin_pass_open |= open;
                        self.run_plugin(&call);
                        open = false;
                        // What the callback drew into a multisampled target's samples reaches the
                        // resolved image through a pass that draws nothing.
                        if std::mem::take(&mut self.plugins.drew_into_samples)
                            && let Some(target) = self.state.target.clone()
                        {
                            let next = self.msaa_targets(Some(&target));
                            self.switch_msaa_targets(next, &mut passes);
                            self.prepare_framebuffer(&mut passes);
                            self.mark_msaa_written(&target);
                            passes.push(Op::Pass(self.new_pass(target)));
                            open = true;
                        }
                    }
                    Command::EndFrame => {
                        // A packed level is packed before its mips are made from it.
                        self.process_packed(&mut passes);
                        self.process_mips(&mut passes);
                        self.copy_depth(&mut passes, depth_mark);
                        self.depth_target = None;
                        if !self.screen_touched {
                            // Nothing drew on the screen: it shows black.
                            self.switch_msaa_targets(Default::default(), &mut passes);
                            let target = self.screen_target(self.color_space);
                            let mut pass = self.new_pass(target);
                            pass.seed = None;
                            pass.color_load = wgpu::LoadOp::Clear(wgpu::Color::BLACK);
                            pass.depth_clear = Some((self.conventions.depth(1.0), 0));
                            passes.push(Op::Pass(pass));
                        }
                        open = false;
                        end_frame = true;
                    }
                    Command::SetTarget {
                        color,
                        depth,
                        mip,
                        slice,
                        extra,
                    } => {
                        #[cfg(target_os = "macos")]
                        {
                            self.plugin_target_drawn = false;
                            self.plugin_redirect = Default::default();
                        }
                        self.copy_depth(&mut passes, depth_mark);
                        // The texture whose depth buffer is attached (the color's own, or another's:
                        // a G-buffer's) is copied where shaders sample it when it has a depth copy.
                        self.depth_target = depth_owner(color, depth).filter(|h| {
                            self.textures.get(h).is_some_and(|t| t.depth_copy.is_some())
                        });
                        depth_mark = self.op_position(&passes);
                        let changed = !self.same_target(color, depth, mip, slice, &extra);
                        self.framebuffer_dirty |= changed;
                        self.target_handles = (color, depth);
                        self.plugins.target = (mip, slice);
                        self.state.target = self.resolve_target(color, depth, mip, slice);
                        // Further colors are drawn as the first is stored.
                        let stand_in = self.screen_stand_in;
                        if stand_in != 0 && color != stand_in && extra.contains(&stand_in) {
                            self.warn_once("stand-in-extra".into(), || {
                                "the screen camera's target is a further color of a target: it is drawn as the first color is stored (unmeasured)".into()
                            });
                        }
                        let size = self
                            .state
                            .target
                            .as_ref()
                            .map(|t| (t.width, t.height, t.samples));
                        if let Some((width, height, samples)) = size.filter(|_| !extra.is_empty()) {
                            let mut further = Vec::new();
                            for e in &extra {
                                // Each further color as a target of its own, without depth.
                                let Some(t) =
                                    self.resolve_target(*e, commands::NO_TEXTURE, mip, slice)
                                else {
                                    continue;
                                };
                                if t.width != width || t.height != height || t.samples != samples {
                                    self.warn_once(format!("mrt-size{e}"), || {
                                        format!("render target {e} differs in size or samples from the first color: not bound")
                                    });
                                    continue;
                                }
                                let (Some(color_view), Some(format)) = (t.color, t.format) else {
                                    continue;
                                };
                                further.push(ExtraColor {
                                    color: color_view,
                                    depth_slice: t.depth_slice,
                                    resolve: t.resolve,
                                    format,
                                    alpha_one: t.alpha_one,
                                    texture: t.texture,
                                    auto_mips: t.auto_mips,
                                    defer_mips: t.defer_mips,
                                    packed: t.packed,
                                });
                            }
                            let target = self.state.target.as_mut().unwrap();
                            target.extra = further;
                        }
                        let next = self.msaa_targets(self.state.target.as_ref());
                        self.switch_msaa_targets(next, &mut passes);
                        if let Some(t) = &self.state.target {
                            self.state.viewport = [0.0, 0.0, t.width as f32, t.height as f32];
                        }
                        open = false;
                    }
                    Command::Viewport(v) => self.state.viewport = v,
                    Command::MeshInstance { index, count } => {
                        self.state.mesh_instance = Some(MeshInstanceSlot {
                            index,
                            count,
                            added: 0,
                            mirrored: false,
                        })
                    }
                    Command::InstanceMultiplier(multiplier) => {
                        self.state.instance_multiplier = multiplier.max(1)
                    }
                    Command::ScreenStandIn(texture) => self.screen_stand_in = texture,
                    Command::DotsDraw(dots) => self.state.dots = Some(dots),
                    Command::DrawSetup {
                        instances,
                        args,
                        offset,
                        indexed,
                    } => self.state.draw_setup = Some((instances, args, offset, indexed)),
                    Command::DrawIndices {
                        buffer,
                        size,
                        first,
                        count,
                    } => self.state.draw_indices = Some((buffer, size, first, count)),
                    Command::DrawShape {
                        topology,
                        base_vertex,
                        count,
                    } => {
                        self.state.draw_shape = Some(DrawShape {
                            topology: shape_topology(topology),
                            base_vertex,
                            count,
                            index16: false,
                        });
                    }
                    Command::Camera {
                        view,
                        projection,
                        flags,
                    } => {
                        self.state.view = view;
                        self.state.projection = projection;
                        // A scriptable pipeline sets the camera variables itself as it sets its
                        // matrices: there they follow every matrix change, as before.
                        if flags & commands::camera::PROPERTIES != 0
                            || !self.resolver.index.pipeline().is_empty()
                        {
                            self.state.camera = Some((view, projection));
                        }
                        self.state.overlay = flags & commands::camera::OVERLAY != 0;
                        self.state.invert_culling = flags & commands::camera::INVERT_CULLING != 0;
                        self.state.wireframe = flags & commands::camera::WIREFRAME != 0;
                        self.state.no_srgb_write = flags & commands::camera::NO_SRGB_WRITE != 0;
                        self.state.gamma_framebuffer_srgb =
                            flags & commands::camera::GAMMA_FRAMEBUFFER_SRGB != 0;
                        self.state.gizmo_draw = flags & commands::camera::GIZMO != 0;
                        if flags & commands::camera::BEGIN != 0 {
                            self.grabbed.clear();
                        }
                    }
                    Command::Global(name, value) => {
                        let globals = Arc::make_mut(&mut self.globals);
                        match self.globals_index.position(globals, &name) {
                            Some(i) => {
                                if !same_property_value(&globals[i].1, &value) {
                                    globals[i].1 = value;
                                    self.globals_revision = self.globals_revision.wrapping_add(1);
                                }
                            }
                            None => {
                                globals.push((name, value));
                                self.globals_revision = self.globals_revision.wrapping_add(1);
                            }
                        }
                    }
                    Command::RenderPipeline(tag) => {
                        if self.resolver.set_pipeline(&tag) {
                            // Materials draw with the SubShader the new pipeline runs.
                            self.error_program = None;
                            let handles: Vec<u64> = self.materials.keys().copied().collect();
                            for handle in handles {
                                let material = self.materials[&handle].resolved.material.clone();
                                let resolved = self.resolver.resolve(&material);
                                self.materials.insert(handle, GpuMaterial::new(resolved));
                            }
                        }
                    }
                    Command::PassTags(tags) => {
                        if self.pass_tags != tags {
                            self.pass_tags = tags;
                            self.pass_tags_gen = self.pass_tags_gen.wrapping_add(1).max(1);
                        }
                    }
                    Command::DepthBias { bias, slope } => {
                        // Toward the far plane in the device's depth direction.
                        let sign = if self.conventions.reversed_z {
                            -1.0
                        } else {
                            1.0
                        };
                        self.state.depth_bias = [bias * sign, slope * sign];
                    }
                    Command::StateOverride(o) => self.state.state_override = o,
                    Command::ColorSpace(space) => self.color_space = space.clamp(0, 1),
                    Command::Anisotropy {
                        mode,
                        forced_min,
                        global_max,
                    } => {
                        let anisotropy = [mode, forced_min, global_max];
                        if anisotropy != self.anisotropy {
                            self.anisotropy = anisotropy;
                            // Cached bind groups hold samplers of the old anisotropy levels.
                            self.program_texture_groups.clear();
                            for texture in
                                self.textures.values_mut().chain(self.defaults.values_mut())
                            {
                                texture.bind_groups = [None, None];
                            }
                            self.depth_main_groups.clear();
                        }
                    }
                    Command::Keyword(name, on) => {
                        let present = self.keywords.iter().position(|k| *k == name);
                        let changed = match (present, on) {
                            (None, true) => {
                                self.keywords.push(name);
                                true
                            }
                            (Some(i), false) => {
                                self.keywords.remove(i);
                                true
                            }
                            _ => false,
                        };
                        if changed {
                            let mut set = self.keywords.clone();
                            set.sort();
                            let next = self.keyword_sets.len() as u32 + 1;
                            self.keyword_set = *self.keyword_sets.entry(set).or_insert(next);
                        }
                    }
                    Command::Clear {
                        flags,
                        color,
                        depth,
                        stencil,
                    } => {
                        let Some(target) = self.state.target.clone() else {
                            continue;
                        };
                        let next = self.msaa_targets(Some(&target));
                        self.switch_msaa_targets(next, &mut passes);
                        self.prepare_framebuffer(&mut passes);
                        if !open {
                            let pass = self.new_pass(target.clone());
                            passes.push(Op::Pass(pass));
                            open = true;
                        }
                        let clear_color =
                            target.color.is_some() && flags & commands::clear::COLOR != 0;
                        if clear_color {
                            self.mark_msaa_written(&target);
                        }
                        let color = match target.format {
                            Some(format) => resources::unorm8_clear(format, color),
                            None => color,
                        };
                        let clear_depth =
                            flags & (commands::clear::DEPTH | commands::clear::STENCIL) != 0;
                        let viewport = self.state.viewport;
                        let full = viewport[0] <= 0.0
                            && viewport[1] <= 0.0
                            && viewport[0] + viewport[2] >= target.width as f32
                            && viewport[1] + viewport[3] >= target.height as f32;
                        // Integer targets are cleared by a pass's load (the clear pipeline writes
                        // floats): a whole-target clear after draws starts a pass of its own.
                        let integer = target.format.is_some_and(resources::is_integer);
                        if full
                            && integer
                            && (clear_color || clear_depth)
                            && !last_pass(&mut passes).items.is_empty()
                        {
                            let pass = self.new_pass(target.clone());
                            passes.push(Op::Pass(pass));
                        }
                        let conventions = self.conventions;
                        let pass = last_pass(&mut passes);
                        if full && pass.items.is_empty() {
                            // Each color's own packing rounds this when the pass begins.
                            if clear_color {
                                pass.color_load = wgpu::LoadOp::Clear(wgpu::Color {
                                    r: color[0] as f64,
                                    g: color[1] as f64,
                                    b: color[2] as f64,
                                    a: color[3] as f64,
                                });
                            }
                            if clear_depth {
                                pass.depth_clear = Some((self.conventions.depth(depth), stencil));
                            }
                        } else if clear_color || clear_depth {
                            let pipeline = self.pipelines.clear(
                                &device,
                                ClearKey {
                                    format: target.format,
                                    extra: target.extra_formats(),
                                    depth: target.depth_format,
                                    samples: target.samples,
                                    color: clear_color,
                                    clear_depth,
                                },
                            );
                            // The clear program writes one color: the first color's packing.
                            let mut color = packed_clear(
                                conventions,
                                target.packed,
                                level_of(&target.texture),
                                color,
                            );
                            if target.alpha_one {
                                color[3] = 1.0;
                            }
                            let color = if let Some(format) = target.format.filter(|_| integer) {
                                let c = resources::integer_clear(
                                    format,
                                    wgpu::Color {
                                        r: color[0] as f64,
                                        g: color[1] as f64,
                                        b: color[2] as f64,
                                        a: color[3] as f64,
                                    },
                                );
                                let signed = format.sample_type(None, None)
                                    == Some(wgpu::TextureSampleType::Sint);
                                [c.r, c.g, c.b, c.a].map(|v| {
                                    f32::from_bits(if signed { v as i32 as u32 } else { v as u32 })
                                })
                            } else {
                                color
                            };
                            let offset = self.push_uniform(DrawUniform {
                                color,
                                misc: [self.conventions.depth(depth), 0.0, 0.0, 0.0],
                                ..Zeroable::zeroed()
                            });
                            last_pass(&mut passes)
                                .items
                                .push(PassItem::Clear(pipeline, offset, viewport));
                        }
                        if clear_depth {
                            if let Some(resolve) =
                                self.native_depth_resolve(if self.target_handles.1 == 0 {
                                    self.target_handles.0
                                } else {
                                    self.target_handles.1
                                })
                            {
                                passes.push(Op::DepthBlit(resolve));
                                open = false;
                            }
                        }
                    }
                    Command::Draw(mut draw) => {
                        #[cfg(target_os = "macos")]
                        {
                            self.plugin_target_drawn = true;
                            self.plugin_redirect = Default::default();
                        }
                        if self.sync_mesh_buffers(draw.mesh, &mut passes) {
                            open = false;
                        }
                        let mesh_instance = self.state.mesh_instance.take();
                        let first_instance = mesh_instance.map_or(0, |m| m.index);
                        let dots = self.state.dots.take();
                        // Taken first: a draw's indices and shape never pass to a later draw.
                        let indices = self.state.draw_indices.take();
                        let shape = DrawShape {
                            index16: indices.is_some_and(|i| i.1 != commands::DRAW_INDEX32_SIZE),
                            ..self.state.draw_shape.take().unwrap_or_default()
                        };
                        let Some(target) = self.state.target.clone() else {
                            if self.target_handles.0 != commands::NO_TEXTURE {
                                self.warn_once("draw-no-target".into(), || {
                                    "a draw came before any render target was set: skipped".into()
                                });
                            }
                            continue;
                        };
                        let next = self.msaa_targets(Some(&target));
                        self.switch_msaa_targets(next, &mut passes);
                        self.prepare_framebuffer(&mut passes);
                        if !open {
                            let pass = self.new_pass(target.clone());
                            passes.push(Op::Pass(pass));
                            open = true;
                        }
                        self.depth_reads.clear();
                        self.pre_draw.clear();
                        self.pre_copies.clear();
                        let mut planned = std::mem::take(&mut self.planned_scratch);
                        planned.clear();
                        let mesh_batch_limits = self.plan_draw(
                            &draw,
                            &target,
                            time,
                            mesh_instance,
                            dots.as_ref(),
                            shape,
                            &mut planned,
                        );
                        if let Some(dots) = &dots {
                            // One instance per visible instance (an empty command draws nothing); an
                            // indirect command's count comes from its arguments buffer (DrawSetup).
                            if dots.indirect == 0 {
                                if dots.visible.is_empty() {
                                    planned.clear();
                                    self.planned_scratch = planned;
                                    continue;
                                }
                                for p in &mut planned {
                                    if let Planned::Draw(p) = p {
                                        p.instances = dots.visible.len() as u32;
                                    }
                                }
                            }
                        }
                        // Depth elements the draw samples whose copies lag (made just now, or the
                        // current target's): copied here, between what was drawn and this draw.
                        let mut reads = std::mem::take(&mut self.depth_reads);
                        for (texture, now) in reads.drain() {
                            if let Some(resolve) = self.native_depth_resolve(texture) {
                                open = false;
                                passes.push(Op::DepthBlit(resolve));
                            }
                            if now {
                                if let Some(copy) = self.depth_copy_op(texture) {
                                    open = false;
                                    passes.push(Op::DepthCopy(copy));
                                }
                            }
                        }
                        self.depth_reads = reads;
                        if let Some((instances, args, offset, indexed)) =
                            self.state.draw_setup.take()
                        {
                            let mut buffer = (args != 0)
                                .then(|| self.buffers.get(&args).map(|b| b.buffer.clone()))
                                .flatten();
                            if args != 0 && buffer.is_none() {
                                self.warn_once(format!("args{args}"), || {
                                    format!("an indirect draw's arguments buffer {args} does not exist: skipped")
                                });
                                self.planned_scratch = planned;
                                continue;
                            }
                            let mut draw_offset = u64::from(offset);
                            if let Some(source) = buffer.take() {
                                let size = if indexed { INDIRECT_ARGS_SLOT } else { 16 };
                                if draw_offset % 4 != 0 || draw_offset + size > source.size() {
                                    self.warn_once(format!("args-range{args}-{offset}"), || {
                                        format!("an indirect draw's arguments at byte {offset} of buffer {args} are not 4-byte aligned or lie past its end: skipped")
                                    });
                                    self.planned_scratch = planned;
                                    continue;
                                }
                                let (destination, at) = self.indirect_args.slot(&self.device);
                                // One copy serves the call's passes: zeroed only when none reads
                                // vertex inputs (whose vertices the first vertex moves).
                                let zero_first_vertex = self.conventions.vertex_id_from_draw_start
                                    && planned.iter().all(|p| match p {
                                        Planned::Draw(d) => {
                                            !self.compiled.program(d.program.0).vertex_inputs
                                        }
                                        _ => true,
                                    });
                                let copy = ArgsCopy {
                                    source,
                                    source_offset: draw_offset,
                                    destination: destination.clone(),
                                    destination_offset: at,
                                    indexed,
                                    zero_first_vertex,
                                };
                                // Hoisted before the open pass unless a draw in it could have
                                // written the arguments; then the pass ends here.
                                match passes.last_mut() {
                                    Some(Op::Pass(pass)) if open && !pass.writes_buffers => {
                                        pass.args.push(copy)
                                    }
                                    _ => {
                                        open = false;
                                        passes.push(Op::IndirectArgs(copy));
                                    }
                                }
                                buffer = Some(destination);
                                draw_offset = at;
                            }
                            for p in &mut planned {
                                if let Planned::Draw(p) = p {
                                    p.instances = instances.max(1);
                                    p.indirect = buffer.clone().map(|b| (b, draw_offset, indexed));
                                }
                            }
                        }
                        if let Some((handle, size, first, count)) = indices {
                            let Some(index) = self.external_indices(
                                (handle, size, first, count),
                                shape,
                                &planned,
                            ) else {
                                self.planned_scratch = planned;
                                continue;
                            };
                            for p in &mut planned {
                                if let Planned::Draw(p) = p {
                                    p.index = index.clone();
                                    p.index_format = if size == commands::DRAW_INDEX32_SIZE {
                                        wgpu::IndexFormat::Uint32
                                    } else {
                                        wgpu::IndexFormat::Uint16
                                    };
                                    p.range = first..first + count;
                                    p.base_vertex = shape.base_vertex;
                                }
                            }
                        }
                        // Standalone instancing whose multiplied IDs pass a native batch.
                        let mut past_batch = false;
                        for item in planned.drain(..) {
                            match item {
                                Planned::Draw(mut d) => {
                                    d.first_instance = first_instance;
                                    self.draw_from_first_vertex(&mut d);
                                    if let Some(slot) = mesh_instance
                                        && slot.added > 0
                                    {
                                        d.instances = slot.added;
                                    }
                                    // A mesh-instancing call's instances are drawn one by one, the
                                    // multiplier's are added after them (`multiplied_instances`).
                                    if d.indirect.is_none() && mesh_instance.is_none() {
                                        d.instances = d
                                            .instances
                                            .wrapping_mul(self.state.instance_multiplier);
                                        d.first_instance = d
                                            .first_instance
                                            .wrapping_mul(self.state.instance_multiplier);
                                    }
                                    if let Some(limits) = mesh_batch_limits
                                        && limits[0] > 0.0
                                    {
                                        let program = self.compiled.program(d.program.0);
                                        let Some(stride) = program.mesh_instance_stride else {
                                            self.warn_once(format!("mesh-instance-layout{}", d.program.0), ||
                                                "standalone mesh instancing has an unsupported property type: draw skipped".into());
                                            continue;
                                        };
                                        let mut capacity = limits
                                            [usize::from(program.mesh_instance_uniform_scaling)]
                                            as u32;
                                        if stride > 0 {
                                            capacity = capacity.min(limits[2] as u32 / stride);
                                        }
                                        if let Some(max) = program.mesh_instance_max_count {
                                            capacity = capacity.min(max);
                                        }
                                        if capacity == 0 {
                                            self.warn_once(format!("mesh-instance-capacity{}", d.program.0), ||
                                                "standalone mesh instancing property data exceeds a native batch: draw skipped".into());
                                            continue;
                                        }
                                        if mesh_instance.is_some_and(|slot| {
                                            slot.count
                                                .saturating_mul(self.state.instance_multiplier)
                                                > capacity
                                        }) {
                                            // Checked before any is added (`multiplied_instances`).
                                            past_batch = true;
                                        }
                                        d.first_instance %= capacity;
                                    }
                                    if !open {
                                        let pass = self.new_pass(target.clone());
                                        passes.push(Op::Pass(pass));
                                        open = true;
                                    }
                                    // The window is copied after what came before the draw: before
                                    // its pass unless a draw in it could have written the buffer;
                                    // then the pass ends here.
                                    if let Some(copy) = d.window.take() {
                                        match passes.last_mut() {
                                            Some(Op::Pass(pass)) if !pass.writes_buffers => {
                                                pass.windows.push(copy)
                                            }
                                            _ => {
                                                passes.push(Op::Window(copy));
                                                passes
                                                    .push(Op::Pass(self.new_pass(target.clone())));
                                            }
                                        }
                                    }
                                    self.mark_msaa_written(&target);
                                    draws += 1;
                                    let writes = self
                                        .compiled
                                        .program(d.program.0)
                                        .buffers
                                        .iter()
                                        .any(|b| !b.read_only);
                                    let pass = last_pass(&mut passes);
                                    pass.writes_buffers |= writes;
                                    pass.items.push(PassItem::Draw(d));
                                }
                                // A grab ends the pass: the copy reads what is drawn so far, the
                                // passes after it draw in a pass of their own.
                                Planned::Grab(copy) => {
                                    open = false;
                                    passes.push(Op::Copy(copy));
                                }
                                Planned::Convert(copy) => {
                                    open = false;
                                    passes.push(Op::DepthCopy(copy));
                                }
                            }
                        }
                        self.planned_scratch = planned;
                        if let Some(slot) = mesh_instance
                            && slot.added == 0
                            && slot.index + 1 == slot.count
                            && self.state.instance_multiplier > 1
                        {
                            added = self
                                .multiplied_instances(&mut draw, slot, past_batch)
                                .map(|slot| (slot, draw));
                        }
                    }
                    Command::MeshBuffers(binding) => self.bind_mesh_buffers(*binding),
                    Command::CreateBuffer {
                        handle,
                        size,
                        usage,
                    } => {
                        // Earlier work in this submission runs first.
                        self.flush_ops(&mut passes, &mut open);
                        self.create_buffer(handle, size, usage);
                    }
                    Command::BufferData {
                        handle,
                        offset,
                        data,
                    } => {
                        self.flush_ops(&mut passes, &mut open);
                        self.write_buffer(handle, offset, &data);
                    }
                    Command::ReleaseBuffer(handle) => {
                        self.flush_ops(&mut passes, &mut open);
                        self.release_buffer(handle);
                    }
                    Command::Dispatch(d) => {
                        self.process_packed(&mut passes);
                        open = false;
                        self.dispatch_reads_depth_target = false;
                        let planned = self.plan_dispatch(&d);
                        for texture in std::mem::take(&mut self.dispatch_fills) {
                            if let Some(copy) = self.depth_copy_op(texture) {
                                passes.push(Op::DepthCopy(copy));
                            }
                        }
                        if let Some(op) = planned {
                            // A kernel sampling the depth being drawn reads what was drawn before it.
                            if self.dispatch_reads_depth_target {
                                self.copy_depth(&mut passes, depth_mark);
                                passes.push(op);
                                depth_mark = self.op_position(&passes);
                            } else {
                                passes.push(op);
                            }
                        }
                    }
                    Command::CopyBuffer {
                        source,
                        source_offset,
                        destination,
                        destination_offset,
                        size,
                    } => {
                        open = false;
                        if let Some(copy) = self.plan_buffer_copy(
                            source,
                            source_offset,
                            destination,
                            destination_offset,
                            size,
                        ) {
                            if let Some(buffer) = self.buffers.get_mut(&destination) {
                                buffer.revision = buffer.revision.wrapping_add(1);
                            }
                            passes.push(Op::BufferCopy(copy));
                            if self
                                .buffers
                                .get(&destination)
                                .is_some_and(|b| b.shadow.is_some())
                            {
                                // Draw constants are packed from a CPU shadow. Read the completed
                                // destination so preceding dispatches and copies are reflected too.
                                self.flush_ops(&mut passes, &mut open);
                                let bytes = self
                                    .read_buffer(destination, destination_offset, size)
                                    .expect("could not synchronize a copied constant buffer");
                                let shadow = self
                                    .buffers
                                    .get_mut(&destination)
                                    .unwrap()
                                    .shadow
                                    .as_mut()
                                    .unwrap();
                                let start = destination_offset as usize;
                                shadow[start..start + bytes.len()].copy_from_slice(&bytes);
                            }
                        }
                    }
                    Command::SetCounter { buffer, value } => {
                        // Earlier work in this submission runs first, as for buffer data.
                        self.flush_ops(&mut passes, &mut open);
                        self.set_counter(buffer, value);
                    }
                    Command::CopyCounter {
                        source,
                        destination,
                        offset,
                    } => {
                        open = false;
                        if let Some(op) = self.plan_counter_copy(source, destination, offset) {
                            if let Some(buffer) = self.buffers.get_mut(&destination) {
                                buffer.revision = buffer.revision.wrapping_add(1);
                            }
                            passes.push(op);
                        }
                    }
                    Command::CopyTexture {
                        source,
                        source_slice,
                        source_mip,
                        source_rect,
                        destination,
                        destination_slice,
                        destination_mip,
                        destination_x,
                        destination_y,
                    } => {
                        open = false;
                        self.process_packed(&mut passes);
                        self.prepare_screen_copy(source, destination, &mut passes);
                        if let Some(copy) = self.plan_copy(
                            source,
                            [source_slice, source_mip],
                            source_rect,
                            destination,
                            [destination_slice, destination_mip],
                            [destination_x, destination_y],
                            true,
                        ) {
                            passes.push(Op::Copy(copy));
                        }
                    }
                    Command::CopyDepth {
                        source,
                        destination,
                    } => {
                        open = false;
                        self.copy_depth(&mut passes, depth_mark);
                        depth_mark = self.op_position(&passes);
                        self.prepare_screen_copy(source, destination, &mut passes);
                        if let Some(blit) = self.plan_depth_blit(source, destination) {
                            passes.push(Op::DepthBlit(blit));
                            // What sampling the destination's depth element reads follows.
                            if let Some(copy) = self.depth_copy_op(destination) {
                                passes.push(Op::DepthCopy(copy));
                            }
                        }
                    }
                }
            }
            if end_frame && trace_render() {
                self.traced_frames += 1;
                if self.traced_frames % 60 == 1 {
                    tracing::info!(
                        "[trace] renderer: frame {} in {} passes, {draws} draws, screen {}x{}",
                        self.frame_number,
                        passes.len(),
                        self.screen_size.0,
                        self.screen_size.1,
                    );
                }
            }
            // A depth-only target still being drawn: read back before the next submission.
            self.copy_depth(&mut passes, depth_mark);
            self.process_packed(&mut passes);
            self.encode(std::mem::take(&mut passes));
            true
        };
        if pin_gles {
            super::gl_interop::deferring_forgets(|| {
                loop {
                    let deadline = Some(std::time::Instant::now() + GL_PIN_BURST);
                    if device.with_gles_context_pinned(|| run_burst(deadline)) {
                        break;
                    }
                    std::thread::yield_now();
                }
            });
        } else {
            run_burst(None);
        }
        drop(run_burst);
        // What plugins encoded from scripts this frame runs after its work.
        #[cfg(target_os = "macos")]
        if end_frame && self.metal_plugins.is_some() {
            plugins_metal::commit_outside();
            // Scripts' encoders are over the screen of the latest frame or callback.
            self.publish_metal_screen();
        }
        #[cfg(target_os = "linux")]
        if end_frame && let Some(plugins) = self.vulkan_plugins.as_mut() {
            // What plugins used in frames the GPU finished is freed; when it finishes this one,
            // what they used in it may be.
            plugins.free_retired(
                self.frames_completed
                    .load(std::sync::atomic::Ordering::Acquire),
            );
            let completed = self.frames_completed.clone();
            let frame = self.frame_number;
            self.queue.on_submitted_work_done(move || {
                completed.fetch_max(frame, std::sync::atomic::Ordering::AcqRel);
            });
        }
        if end_frame {
            let slot = self.screen_slot;
            let space = self.color_space;
            let screen = self.ensure_screen(slot).view(space).0.clone();
            *self.presented.lock().unwrap() = Some(Presented {
                color: screen,
                size: self.screen_size,
                fixed_game_view: self.fixed_game_view,
                color_space: self.color_space,
                top_down: self.conventions.top_down(true),
                frame: self.frame_number,
                slot,
            });
        }
    }

    /// Compile exactly the program a draw would use, including its existing error fallback.
    fn variant_or_error(
        &mut self,
        shader: &str,
        source: &unbound_shaders::Program,
        keywords: &[String],
        linear: bool,
        layout: &wgpu::BindGroupLayout,
    ) -> (Option<u32>, bool) {
        let device = self.device.clone();
        let id = self
            .compiled
            .variant(&device, shader, source, keywords, linear, layout);
        if id.is_some() {
            return (id, false);
        }
        let error = self.error_program();
        (
            self.compiled.variant(
                &device,
                crate::materials::ERROR_SHADER_NAME,
                &error,
                &[],
                linear,
                layout,
            ),
            true,
        )
    }

    /// The instances an instance multiplier adds to a mesh-instancing call of `slot.count`
    /// instances drawn one by one: IDs `count..count * multiplier` (as `SetInstanceMultiplier`
    /// multiplies a draw's instance count), drawn after its last instance as one draw of that
    /// instance's (`draw`, its matrix zeroed), whose slot this returns. They
    /// have no instance data: their matrices and instanced properties read zero, what the Unity
    /// 6000.0.71f1 Editor's added instances read where no earlier instanced draw wrote
    /// (tests/projects/cmdbuffers `device.multiplier.*`). Where earlier draws wrote more
    /// instances the Editor's read what they left (`device.multiplier.stale.*`, not reproduced:
    /// warned of). They keep the last instance's winding (Unbound's choice; the Editor draws the
    /// call as one). None past a native batch of standalone instancing (unmeasured, warned of).
    fn multiplied_instances(
        &mut self,
        draw: &mut commands::Draw,
        slot: MeshInstanceSlot,
        past_batch: bool,
    ) -> Option<MeshInstanceSlot> {
        if past_batch {
            if !self.multiplied_past_batch_warned {
                self.multiplied_past_batch_warned = true;
                tracing::warn!(
                    "an instance multiplier on standalone mesh instancing past a native batch's instances is unmeasured: the instances it adds are not drawn"
                );
            }
            return None;
        }
        if !self.multiplied_instances_warned {
            self.multiplied_instances_warned = true;
            tracing::warn!(
                "an instance multiplier's added mesh instances read zero instance data; Unity's read what earlier instanced draws left in its instance buffer"
            );
        }
        let mirrored = determinant3(&draw.model) < 0.0;
        draw.model = [0.0; 16];
        Some(MeshInstanceSlot {
            index: slot.count,
            count: slot.count,
            added: slot
                .count
                .saturating_mul(self.state.instance_multiplier - 1),
            mirrored,
        })
    }

    /// The pipelines, bind groups and uniforms of a draw: one per pass it draws, appended to
    /// `planned`. Returns any `<MeshInstanceBatchLimits>` vector carried in `draw.properties`.
    #[allow(clippy::too_many_arguments)]
    fn plan_draw(
        &mut self,
        draw: &commands::Draw,
        target: &Target,
        time: f32,
        mesh_instance: Option<MeshInstanceSlot>,
        dots: Option<&commands::DotsDraw>,
        shape: DrawShape,
        planned: &mut Vec<Planned>,
    ) -> Option<[f32; 4]> {
        let device = self.device.clone();
        let space = self.color_space;
        let (tw, th) = (target.width, target.height);
        let Some(material) = self.materials.get(&draw.material) else {
            self.warn_once(format!("mat{}", draw.material), || {
                format!("a draw uses unknown material {}; skipped", draw.material)
            });
            return None;
        };
        let resolved = material.resolved.clone();
        let mat_main_tex_handle = material.main_tex_handle;
        let mat_main_tex_default = material.main_tex_default.clone();
        let mat_declares_main_tex = material.declares_main_tex;
        let Some(mesh) = self.meshes.get(&draw.mesh) else {
            self.warn_once(format!("mesh{}", draw.mesh), || {
                format!("a draw uses unknown mesh {}; skipped", draw.mesh)
            });
            return None;
        };
        let Some(mut sub) = mesh.submeshes.get(draw.submesh.max(0) as usize).cloned() else {
            self.warn_once(format!("sub{}/{}", draw.mesh, draw.submesh), || {
                format!(
                    "a draw uses missing submesh {} of mesh {}; skipped",
                    draw.submesh, draw.mesh
                )
            });
            return None;
        };
        if sub.count == 0 {
            return None;
        }
        // The indices read as another topology, and only the first ones (`DrawShape`).
        if let Some(topology) = shape.topology {
            sub.topology = topology;
        }
        if shape.count > 0 {
            sub.count = sub.count.min(shape.count);
        }
        let (vertex, index) = (mesh.vertex.clone(), mesh.index.clone());
        let mesh_extra = mesh.extra.clone();
        let extra_size = vertex.size() / size_of::<resources::Vertex>() as u64
            * size_of::<resources::ExtraVertex>() as u64;
        let viewport = clamp_viewport(self.state.viewport, tw, th);
        if viewport[2] <= 0.0 || viewport[3] <= 0.0 {
            return None;
        }
        let viewport_scissor = clamp_rect(viewport, tw, th);
        let scissor = match draw.clip {
            Some(clip) => intersect(clamp_rect(clip, tw, th), viewport_scissor),
            None => viewport_scissor,
        };
        if scissor[2] == 0 || scissor[3] == 0 {
            if trace_render() {
                tracing::info!(
                    "[trace] draw of mesh {} clipped away (scissor {scissor:?})",
                    draw.mesh
                );
            }
            return None;
        }
        let (view, projection) = (self.state.view, self.state.projection);
        // The projection as the device draws with it into this target (Unity's GPU projection:
        // flipped into render textures where UV starts at the top, depth in its range).
        let flipped = self.conventions.flips(target.screen_like);
        let gpu_projection = self.conventions.gpu_projection(&projection, flipped);
        // Rows stored top-down turn the winding (as a mirrored transform does).
        let top_down = self.conventions.top_down(target.screen_like);
        // The passes drawn: the forward ones (a renderer's), every one, or one by index.
        // Stored into a reusable scratch vector of `(index, from_all)` to avoid per-draw heap
        // allocations and `pass_tags` cloning.
        let forward = resolved.passes.len();
        let mut pass_indices = std::mem::take(&mut self.pass_indices_scratch);
        pass_indices.clear();
        match draw.pass {
            -1 => {
                for i in 0..resolved.passes.len() {
                    pass_indices.push((i, false));
                }
            }
            // A renderer list's: per tag in order, the first pass with that LightMode.
            -3 => {
                let tags_gen = self.pass_tags_gen;
                if let Some((cached_gen, cached_indices)) = self
                    .materials
                    .get(&draw.material)
                    .and_then(|m| m.tagged_passes.as_ref())
                    && *cached_gen == tags_gen
                {
                    for &i in cached_indices {
                        pass_indices.push((i, true));
                    }
                } else {
                    let mut chosen_all = Vec::new();
                    let mut errors: Vec<(usize, String)> = Vec::new();
                    for tag in &self.pass_tags {
                        let Some(i) = resolved.lab.tagged_pass(tag) else {
                            continue;
                        };
                        if chosen_all.contains(&i) {
                            continue;
                        }
                        match &resolved.all[i] {
                            Ok(_) => chosen_all.push(i),
                            Err(reason) => errors.push((i, reason.clone())),
                        }
                    }
                    for (i, reason) in errors {
                        self.warn_once(format!("{}#{i}", resolved.shader), || {
                            format!(
                                "shader `{}`: pass {i} is not drawn: {reason}",
                                resolved.shader
                            )
                        });
                    }
                    for &i in &chosen_all {
                        pass_indices.push((i, true));
                    }
                    if let Some(m) = self.materials.get_mut(&draw.material) {
                        m.tagged_passes = Some((tags_gen, chosen_all));
                    }
                }
            }
            // A blit executes shader passes without capturing framebuffer grabs.
            -4 => {
                for (i, p) in resolved.all.iter().enumerate() {
                    if p.as_ref().is_ok_and(|p| p.grab.is_none()) {
                        pass_indices.push((i, true));
                    }
                }
            }
            -2 => {
                for (i, p) in resolved.all.iter().enumerate() {
                    if p.is_ok() {
                        pass_indices.push((i, true));
                    }
                }
            }
            n => match resolved.all.get(n as usize) {
                Some(Ok(_)) => pass_indices.push((n as usize, true)),
                Some(Err(reason)) => {
                    let reason = reason.clone();
                    self.warn_once(format!("{}#{n}", resolved.shader), || {
                        format!(
                            "shader `{}`: pass {n} is not drawn: {reason}",
                            resolved.shader
                        )
                    });
                    self.pass_indices_scratch = pass_indices;
                    return None;
                }
                None => {
                    self.warn_once(format!("{}#{n}?", resolved.shader), || {
                        format!(
                            "shader `{}` has no pass {n} ({} passes): nothing drawn",
                            resolved.shader,
                            resolved.all.len()
                        )
                    });
                    self.pass_indices_scratch = pass_indices;
                    return None;
                }
            },
        }
        if pass_indices.is_empty() {
            self.pass_indices_scratch = pass_indices;
            return None;
        }
        // Single scan of the draw's per-draw properties (`MaterialPropertyBlock`, per-renderer
        // overrides, mesh-instancing limits).
        let overrides: &[(String, owned::PropertyValue)] = &draw.properties;
        let prop_summary = scan_draw_properties(overrides);
        let material_props: &owned::Material = &resolved.material;
        let texture_of = |name: &str| layered_texture(overrides, material_props, name);
        // `_MainTex`: the renderer's (a sprite's texture), else the draw override's, else the
        // material's, else a global one when the shader does not declare it.
        let layered_main = prop_summary
            .main_tex_override
            .unwrap_or(mat_main_tex_handle);
        let main_handle = match (draw.main_texture, layered_main) {
            (0, 0) if !mat_declares_main_tex => {
                match self.globals_index.find(&self.globals, "_MainTex") {
                    Some(owned::PropertyValue::Texture { handle, .. }) => *handle,
                    _ => 0,
                }
            }
            (0, material) => material,
            (renderer, _) => renderer,
        };
        let alpha_only = self.main_texture_alpha_only(main_handle);
        let mut main_texture_bg: Option<wgpu::BindGroup> = None;
        let screen = [
            tw as f32,
            th as f32,
            1.0 + 1.0 / tw as f32,
            1.0 + 1.0 / th as f32,
        ];
        let uniform = DrawUniform {
            model: draw.model,
            view,
            proj: gpu_projection,
            color: vertex_tint(draw.tint, draw.color),
            screen,
            misc: [time, space as f32, if alpha_only { 1.0 } else { 0.0 }, 0.0],
            pad: [0.0; 4],
        };
        let offset = self.push_uniform(uniform);
        // A renderer's color for the vertices goes to a program declaring `unity_SpriteColor`
        // there instead (read or not): its vertex colors are left alone (the draw's uniform with a white color,
        // made for the first such pass).
        let mut untinted_offset = None;
        let linear = space == 1;
        let mesh_replay = mesh_instance.is_some() || prop_summary.has_mesh_batch_limits;
        let mut globals = self.globals.clone();
        self.globals_index.refresh(&globals);
        for &(raw_index, from_all) in &pass_indices {
            let (pass_index, pass) = if from_all {
                (
                    forward + raw_index,
                    resolved.all[raw_index].as_ref().expect("checked ok"),
                )
            } else {
                (raw_index, &resolved.passes[raw_index])
            };
            if let Some(name) = &pass.grab {
                if let Some(copy) = self.grab(
                    name,
                    target.texture.clone(),
                    (target.width, target.height),
                    target.alpha_one,
                ) {
                    planned.push(Planned::Grab(copy));
                }
                globals = self.globals.clone();
                self.globals_index.refresh(&globals);
                continue;
            }
            let layout = self.pipelines.texture_layout.clone();
            // The pass's variant for the material's and the global keywords, chosen once per
            // keyword set (the material's cache goes with it when it changes).
            let mode = if dots.is_some() {
                InstancingMode::Dots
            } else if mesh_replay {
                InstancingMode::MeshReplay
            } else {
                InstancingMode::None
            };
            let multisampled = target.samples > 1;
            let key = (pass_index, self.keyword_set, linear, mode, multisampled);
            let cached = self
                .materials
                .get(&draw.material)
                .and_then(|m| m.variants.get(&key).copied());
            let (id, errored) = match cached {
                Some(v) => v,
                None => {
                    let mut keywords = resolved.material.keywords.clone();
                    keywords.extend(self.keywords.iter().cloned());
                    // The draw decides the instancing variant: a material's INSTANCING_ON
                    // selects nothing (tests/projects/shadertargets `BuiltinKeywords`; a global
                    // one is taken alike, unmeasured).
                    keywords.retain(|k| k != INSTANCING_KEYWORD);
                    match mode {
                        InstancingMode::MeshReplay => {
                            keywords.push(INSTANCING_KEYWORD.into());
                            keywords.push(unbound_shaders::MESH_INSTANCE_REPLAY.into());
                        }
                        InstancingMode::Dots => keywords.push(DOTS_INSTANCING_KEYWORD.into()),
                        InstancingMode::None => {}
                    }
                    let (mut id, mut errored) = self.variant_or_error(
                        &resolved.shader,
                        &pass.program,
                        &keywords,
                        linear,
                        &layout,
                    );
                    // A program reading its color targets draws into a multisampled one with
                    // its variant that reads each fragment's own sample.
                    if multisampled
                        && !errored
                        && id.is_some_and(|id| self.compiled.program(id).framebuffer_fetch != 0)
                    {
                        keywords.push(unbound_shaders::FETCH_MULTISAMPLED.into());
                        (id, errored) = self.variant_or_error(
                            &resolved.shader,
                            &pass.program,
                            &keywords,
                            linear,
                            &layout,
                        );
                    }
                    // A BatchRendererGroup draw of a pass without DOTS instancing (or that does
                    // not compile) is skipped, decided once: its instances would all draw at the
                    // draw's own place.
                    if mode == InstancingMode::Dots
                        && id.is_some_and(|id| errored || !self.compiled.dots_instanced(id))
                    {
                        id = None;
                        let shader = resolved.shader.clone();
                        self.warn_once(format!("dots{shader}#{pass_index}"), || {
                            format!(
                                "shader `{shader}` pass {pass_index} has no DOTS instancing variant: its BatchRendererGroup draws are skipped"
                            )
                        });
                    }
                    if let Some(m) = self.materials.get_mut(&draw.material) {
                        m.variants.insert(key, (id, errored));
                    }
                    (id, errored)
                }
            };
            if trace_render() {
                tracing::info!(
                    "[trace] draw mesh {} sub {}: `{}` pass {} ({}){}",
                    draw.mesh,
                    draw.submesh,
                    resolved.shader,
                    pass_index,
                    pass.program.light_mode.as_deref().unwrap_or("-"),
                    if !errored && id.is_some() {
                        ""
                    } else {
                        " -> error shader"
                    }
                );
            }
            let Some(id) = id else { continue };
            let (mut state, mut stencil_ref) = if errored {
                (Default::default(), 0)
            } else {
                (pass.state, pass.stencil_ref)
            };
            // A draw command with FlipWinding draws mirrored instances (its matrices are in the
            // batch's buffer, not the draw's).
            let flip = dots.is_some_and(|d| {
                d.header[commands::DOTS_HEADER_FLAGS] & commands::DOTS_FLAG_FLIP_WINDING != 0
            });
            // An added mesh instance keeps its call's winding (`multiplied_instances`).
            let mirrored = match mesh_instance {
                Some(slot) if slot.added > 0 => slot.mirrored,
                _ => determinant3(&draw.model) < 0.0,
            };
            (state, stencil_ref) = self.draw_state(
                state,
                stencil_ref,
                (((!draw.raw_winding && mirrored) != draw.flip_winding) != top_down) != flip,
            );
            if target.alpha_one || target.extra.iter().any(|e| e.alpha_one) {
                let mut targets = state.targets.unwrap_or(
                    [crate::materials::TargetState {
                        blend: state.blend,
                        color_mask: state.color_mask,
                    }; crate::shaderlab::MAX_TARGETS],
                );
                if target.alpha_one {
                    targets[0].color_mask &= !1;
                }
                for (i, e) in target.extra.iter().enumerate() {
                    if e.alpha_one {
                        targets[i + 1].color_mask &= !1;
                    }
                }
                state.targets = Some(targets);
            }
            let key = PipelineKey {
                state,
                topology: sub.topology,
                strip_index: strip_index(sub.topology, shape.index16),
                format: target.format,
                depth: target.depth_format,
                samples: target.samples,
                depth_bias: self.state.depth_bias.map(f32::to_bits),
                extra: target.extra_formats(),
            };
            if trace_render() {
                tracing::info!(
                    "[trace]   state cull {} zwrite {} ztest {} blend {:?} mirrored {}",
                    key.state.cull,
                    key.state.zwrite,
                    key.state.ztest,
                    key.state.blend,
                    determinant3(&draw.model) < 0.0
                );
            }
            let pipeline = match self.compiled.pipeline(&device, id, key) {
                Ok(pipeline) => pipeline,
                Err(why) => {
                    let shader = resolved.shader.clone();
                    self.warn_once(
                        format!("outputs{shader}#{pass_index}{:?}", key.format),
                        || format!("shader `{shader}` pass {pass_index} is not drawn: {why}"),
                    );
                    continue;
                }
            };
            let extra = if self.compiled.program(id).extra_vertex_inputs {
                Some(match &mesh_extra {
                    Some(e) => e.clone(),
                    None => self.zero_extra(extra_size),
                })
            } else {
                None
            };
            // A program reading its color targets (framebuffer fetch, Metal) reads a copy of each
            // made right before the draw (`unbound_shaders::fetch`).
            let reads = self.compiled.program(id).framebuffer_fetch;
            if !errored && reads != 0 {
                // A color target the draw lacks: the macOS Editor does not draw (shadertargets
                // `fetch.Second.missing`, `fetch.SecondHalf.missing`).
                let lacking = (1..unbound_shaders::fetch::TARGETS)
                    .find(|&i| reads & 1 << i != 0 && target.extra.len() < i as usize);
                if let Some(index) = lacking {
                    let shader = resolved.shader.clone();
                    self.warn_once(format!("fetchlacks{shader}#{pass_index}/{index}"), || {
                        format!(
                            "shader `{shader}` pass {pass_index} reads color target {index} (framebuffer fetch), which the draw does not have: not drawn"
                        )
                    });
                    continue;
                }
                for index in (0..unbound_shaders::fetch::TARGETS).filter(|i| reads & 1 << i != 0) {
                    // A multisampled target's samples themselves (its color view's texture).
                    let (texture, alpha_one) = match index {
                        0 if target.samples > 1 => (
                            target.color.as_ref().map(|c| (c.texture().clone(), 0, 0)),
                            target.alpha_one,
                        ),
                        0 => (target.texture.clone(), target.alpha_one),
                        _ => {
                            let e = &target.extra[index as usize - 1];
                            let texture = if target.samples > 1 {
                                Some((e.color.texture().clone(), 0, 0))
                            } else {
                                e.texture.clone()
                            };
                            (texture, e.alpha_one)
                        }
                    };
                    let name = unbound_shaders::fetch::texture(index);
                    match self.grab(name, texture, (target.width, target.height), alpha_one) {
                        Some(copy) => planned.push(Planned::Grab(copy)),
                        None => {
                            // Not an earlier draw's copy: nothing.
                            self.set_global_texture(name, 0);
                            let shader = resolved.shader.clone();
                            self.warn_once(format!("fetch{shader}#{pass_index}/{index}"), || {
                                format!(
                                    "shader `{shader}` pass {pass_index} reads color target {index} (framebuffer fetch), which has no texture to copy: it reads the default texture"
                                )
                            });
                        }
                    }
                }
                globals = self.globals.clone();
                self.globals_index.refresh(&globals);
            }
            let Some(material_bg) = self.program_bind_group(
                draw.material,
                id,
                space,
                &globals,
                &draw.properties,
                prop_summary.has_texture_override,
                draw.main_texture,
            ) else {
                self.pre_draw.clear();
                continue;
            };
            // Legacy group-2 `_MainTex` bind group is only created/bound when the program reads it.
            let texture_bg = if self.compiled.program(id).main_texture {
                Some(
                    main_texture_bg
                        .get_or_insert_with(|| {
                            self.main_bind_group(main_handle, &mat_main_tex_default, space)
                                .0
                        })
                        .clone(),
                )
            } else {
                None
            };
            // The copies of textures it draws into and the conversions this pass reads run right
            // before it, after the grabs before it.
            for copy in self.pre_copies.drain(..) {
                planned.push(Planned::Grab(copy));
            }
            for copy in self.pre_draw.drain(..) {
                planned.push(Planned::Convert(copy));
            }
            // A direct DOTS draw does not read the indirect visible instances.
            let unread = dots
                .filter(|d| d.indirect == 0)
                .map(|_| super::dots::INDIRECT_VISIBILITY_BUFFER);
            let Ok(buffers_bg) = self.buffer_bind_group(
                id,
                &resolved.shader,
                overrides,
                prop_summary.has_buffer_override,
                material_props,
                &globals,
                unread,
            ) else {
                continue;
            };
            let textures = &self.textures;
            let globals_index = &self.globals_index;
            let size = |name: &str| {
                let mut handle = if name == "_MainTex" {
                    main_handle
                } else {
                    texture_of(name)
                };
                // `<name>_TexelSize` of a global texture too, as Unity provides it (of the
                // texture the slot reads: a declared property never reads a global one).
                if handle == 0
                    && !resolved.declares(name)
                    && let Some(owned::PropertyValue::Texture { handle: h, .. }) =
                        globals_index.find(&globals, name)
                {
                    handle = *h;
                }
                // A depth element is the size of its texture; a dynamically scaled one gives
                // its size before scaling (tests/projects/nativegfx `dynres.*.texelsize`).
                textures
                    .get(&handle::split(handle).0)
                    .map(|t| t.logical.unwrap_or((t.width, t.height)))
            };
            // A buffer bound by a cbuffer's name: the draw's, the material's, else a global one.
            let draw_has_buffer = prop_summary.has_buffer_override;
            let buffers = &self.buffers;
            let constant_buffer = |name: &str| -> Option<compiled::ConstantBufferRef<'_>> {
                let handle = if draw_has_buffer {
                    draw.properties.iter().rev().find_map(|(n, v)| match v {
                        owned::PropertyValue::Buffer(h) if n == name => Some(*h),
                        _ => None,
                    })
                } else {
                    None
                }
                .or_else(|| match material_props.get(name) {
                    Some(owned::PropertyValue::Buffer(h)) => Some(*h),
                    _ => match globals_index.find(&globals, name) {
                        Some(owned::PropertyValue::Buffer(h)) => Some(*h),
                        _ => None,
                    },
                })?;
                let b = buffers.get(&handle)?;
                let bytes = b.shadow.as_deref()?;
                Some(compiled::ConstantBufferRef {
                    handle,
                    revision: b.revision,
                    bytes,
                })
            };
            let (camera_view, camera_proj) = self.state.camera.unwrap_or((view, projection));
            let values = compiled::DrawValues {
                gizmo_draw: self.state.gizmo_draw,
                color: draw.color,
                sprite_color: match draw.tint {
                    commands::DrawTint::None => [1.0; 4],
                    _ => draw.color,
                },
                model: draw.model,
                view,
                proj: projection,
                gpu_proj: gpu_projection,
                flipped,
                reversed_z: self.conventions.reversed_z,
                camera_view,
                camera_proj,
                screen: [tw as f32, th as f32],
                time,
                linear: space == 1,
                material: material_props,
                overrides,
                // A renderer's color of the constants alone reaches a program declaring
                // `unity_SpriteColor` there only: the renderer's `_RendererColor` is not set for
                // it (matches the Unity 6000.0.71f1 Editor, tests/projects/vertexcolors
                // spriteshape.urprenderercolor); the draw's other properties are.
                hidden: (draw.tint == commands::DrawTint::Constant
                    && self.compiled.declares_sprite_color(id))
                .then_some(RENDERER_COLOR),
                lab: &resolved.lab,
                globals: &globals,
                globals_index,
                globals_revision: self.globals_revision,
                textures_generation: self.generation,
                main_texture_handle: main_handle,
                main_alpha_only: alpha_only,
                texture_size: &size,
                constant_buffer: &constant_buffer,
                instance: mesh_instance.map(|m| m.index),
                instance_empty: mesh_instance.is_some_and(|m| m.added > 0),
                element: None,
            };
            if let Some(filter) = trace_uniforms()
                && resolved.shader.contains(filter)
            {
                tracing::info!(
                    "[uniforms] `{}` pass {} target {}x{}: {}",
                    resolved.shader,
                    pass_index,
                    tw,
                    th,
                    self.compiled.describe(id, &values)
                );
            }
            let block = match self
                .materials
                .get(&draw.material)
                .and_then(|m| m.blocks.get(&id))
            {
                Some(b) => b.clone(),
                None => {
                    let b = Arc::new(self.compiled.material_block(
                        id,
                        material_props,
                        &resolved.lab,
                        linear,
                    ));
                    if let Some(m) = self.materials.get_mut(&draw.material) {
                        m.blocks.insert(id, b.clone());
                    }
                    b
                }
            };
            let program_offsets = self.compiled.push(id, &values, Some(&block));
            let mut window = None;
            if let Some(dots) = dots {
                let data = draw
                    .properties
                    .iter()
                    .rev()
                    .find_map(|(n, v)| match v {
                        owned::PropertyValue::Buffer(h) if n == DOTS_INSTANCE_DATA => Some(*h),
                        _ => None,
                    })
                    .and_then(|h| self.buffers.get(&h));
                let size = data.map_or(0, |b| b.size);
                window = self
                    .compiled
                    .write_dots(id, &program_offsets, dots, &draw.properties, size)
                    .zip(data)
                    .map(|((offset, source_offset, size), b)| WindowCopy {
                        source: b.buffer.clone(),
                        source_offset,
                        offset,
                        size,
                    });
            }
            planned.push(Planned::Draw(PlannedDraw {
                pipeline,
                vertex: vertex.clone(),
                extra,
                index: index.clone(),
                index_format: wgpu::IndexFormat::Uint32,
                range: sub.start..sub.start + sub.count,
                base_vertex: sub.base_vertex,
                material: material_bg,
                texture: texture_bg,
                buffers: buffers_bg,
                offset: if draw.tint == commands::DrawTint::Vertices
                    && self.compiled.declares_sprite_color(id)
                {
                    *untinted_offset.get_or_insert_with(|| {
                        self.push_uniform(DrawUniform {
                            color: [1.0; 4],
                            ..uniform
                        })
                    })
                } else {
                    offset
                },
                stencil_ref,
                viewport,
                scissor,
                program: (id, program_offsets),
                instances: 1,
                first_instance: 0,
                first_bound_vertex: 0,
                indirect: None,
                window,
            }));
        }
        self.pass_indices_scratch = pass_indices;
        prop_summary.mesh_batch_limits
    }

    /// Sets the global texture `name` (0: none, the slots read their defaults).
    fn set_global_texture(&mut self, name: &str, handle: u64) {
        let value = owned::PropertyValue::Texture {
            handle,
            st: [1.0, 1.0, 0.0, 0.0],
        };
        let globals = Arc::make_mut(&mut self.globals);
        match globals.iter_mut().find(|(n, _)| n == name) {
            Some(slot) => {
                if !same_property_value(&slot.1, &value) {
                    slot.1 = value;
                    self.globals_revision = self.globals_revision.wrapping_add(1);
                }
            }
            None => {
                globals.push((name.to_string(), value));
                self.globals_revision = self.globals_revision.wrapping_add(1);
            }
        }
    }

    /// A `GrabPass` into `name`: the texture (the target's size and format, made or remade as
    /// needed) and its `_TexelSize` set as globals, and the copy of the target into it; none
    /// when a named texture was grabbed in this camera's render already (measured: Unity grabs
    /// those once per camera render, `_GrabTexture` for every object). Framebuffer fetch's copy
    /// is one too, made before each draw that reads it.
    fn grab(
        &mut self,
        name: &str,
        texture: Option<(wgpu::Texture, u32, u32)>,
        (width, height): (u32, u32),
        alpha_one: bool,
    ) -> Option<Copy> {
        // `_GrabTexture` and the framebuffer fetch copies are made for every draw.
        if name != "_GrabTexture"
            && !name.starts_with(unbound_shaders::fetch::TEXTURE_PREFIX)
            && !self.grabbed.insert(name.to_string())
        {
            return None;
        }
        let (source, layer, mip) = texture?;
        // A multisampled source (a framebuffer fetch's target) is copied whole, samples kept.
        let samples = source.sample_count();
        let format = match source.format().remove_srgb_suffix() {
            wgpu::TextureFormat::Rgba16Float => 2,
            wgpu::TextureFormat::R32Float => 6,
            wgpu::TextureFormat::Rgba32Float => 9,
            wgpu::TextureFormat::Rg32Float => 10,
            wgpu::TextureFormat::R16Float => 11,
            wgpu::TextureFormat::Rg16Float => 12,
            wgpu::TextureFormat::Rg11b10Ufloat => 4,
            wgpu::TextureFormat::Rg16Unorm => texture_format::RG16_UNORM,
            wgpu::TextureFormat::Rgba16Unorm => texture_format::RGBA16_UNORM,
            wgpu::TextureFormat::Rgb10a2Unorm => texture_format::RGB10A2_UNORM,
            wgpu::TextureFormat::R16Unorm => texture_format::R16_UNORM,
            wgpu::TextureFormat::R16Snorm => texture_format::R16_SNORM,
            wgpu::TextureFormat::Rg16Snorm => texture_format::RG16_SNORM,
            wgpu::TextureFormat::Rgba16Snorm => {
                if alpha_one {
                    texture_format::RGB16_SNORM
                } else {
                    texture_format::RGBA16_SNORM
                }
            }
            wgpu::TextureFormat::R8Snorm => texture_format::R8_SNORM,
            wgpu::TextureFormat::Rg8Snorm => texture_format::RG8_SNORM,
            wgpu::TextureFormat::Rgba8Snorm => texture_format::RGBA8_SNORM,
            wgpu::TextureFormat::R8Unorm => texture_format::R8_UNORM,
            wgpu::TextureFormat::Rg8Unorm => texture_format::RG8_UNORM,
            f => resources::integer_code(f).unwrap_or(0),
        };
        let handle = self.grabs.handle(name, source.format(), samples);
        let fits = self.textures.get(&handle).is_some_and(|t| {
            t.width == width
                && t.height == height
                && same_storage(t.texture.format(), source.format())
                && t.samples == samples
        });
        if !fits {
            self.generation += 1;
            let texture = owned::Texture {
                width: width as i32,
                height: height as i32,
                format,
                mip_count: 1,
                data: Vec::new(),
                // Bilinear, clamped (Unity's grab texture).
                filter_mode: 1,
                wrap_u: 1,
                wrap_v: 1,
                wrap_w: 0,
                aniso_level: 0,
                render_target: samples > 1,
                random_write: false,
                auto_generate_mips: false,
                defer_mips: false,
                retain_resolve: false,
                bind_ms: samples > 1,
                depth_compare: None,
                samples,
                depth_bits: 0,
                layers: 1,
                dimension: 2,
                logical: None,
                screen_order: false,
                blue_first: false,
                packed: 0,
            };
            // A blue-first source (the screen on such a device) is grabbed into the same storage,
            // so the copy stays a copy of its texels. The texture is rewritten in place only in
            // the source's storage: the packet's format code is the same for both orders.
            let stored = source.format().remove_srgb_suffix();
            let old = self
                .textures
                .remove(&handle)
                .filter(|t| same_storage(t.texture.format(), stored));
            let device = self.device.clone();
            let gpu = resources::upload_texture_with(
                &self.device,
                &self.queue,
                old,
                &texture,
                self.generation,
                &mut |desc| {
                    if stored == wgpu::TextureFormat::Bgra8Unorm
                        && desc.format == wgpu::TextureFormat::Rgba8Unorm
                    {
                        device.create_texture(&wgpu::TextureDescriptor {
                            format: stored,
                            view_formats: &[stored.add_srgb_suffix()],
                            ..desc.clone()
                        })
                    } else {
                        device.create_texture(desc)
                    }
                },
            );
            self.textures.insert(handle, gpu);
        }
        let destination = match (samples > 1, &self.textures[&handle].msaa) {
            (true, Some((samples_view, _))) => samples_view.texture().clone(),
            _ => self.textures[&handle].texture.clone(),
        };
        self.set_global_texture(name, handle);
        let texel = [
            1.0 / width as f32,
            1.0 / height as f32,
            width as f32,
            height as f32,
        ];
        let texel_size = |n: &str| n.strip_prefix(name) == Some("_TexelSize");
        let value = owned::PropertyValue::Vector(texel);
        let globals = Arc::make_mut(&mut self.globals);
        match globals.iter_mut().find(|(n, _)| texel_size(n)) {
            Some(slot) => {
                if !same_property_value(&slot.1, &value) {
                    slot.1 = value;
                    self.globals_revision = self.globals_revision.wrapping_add(1);
                }
            }
            None => {
                globals.push((format!("{name}_TexelSize"), value));
                self.globals_revision = self.globals_revision.wrapping_add(1);
            }
        }
        Some(Copy {
            channels: None,
            source,
            source_mip: mip,
            source_origin: wgpu::Origin3d {
                x: 0,
                y: 0,
                z: layer,
            },
            destination,
            destination_mip: 0,
            destination_origin: wgpu::Origin3d::ZERO,
            size: wgpu::Extent3d {
                width,
                height,
                depth_or_array_layers: 1,
            },
        })
    }

    /// Encodes and queues the work planned so far (a buffer change must come after it).
    fn flush_ops(&mut self, ops: &mut Vec<Op>, open: &mut bool) {
        if ops.is_empty() {
            return;
        }
        self.encode(std::mem::take(ops));
        self.uniforms.clear();
        self.compute_bindings.begin();
        self.compiled.begin_frame();
        *open = false;
    }

    /// An OpenGL resolve into a 2D target or cubemap reads the bound image of the
    /// source. The destination is its first image. See tests/projects/textureformats.
    fn bound_resolve_source(&self, source: u64, destination: u64) -> [i32; 2] {
        let image = || {
            let destination = self.textures.get(&destination)?;
            if !self.is_gles || destination.array || destination.cube_array || destination.volume {
                return None;
            }
            let source = &self.textures.get(&source)?.texture;
            let target = self.state.target.as_ref()?;
            std::iter::once(&target.texture)
                .chain(target.extra.iter().map(|attachment| &attachment.texture))
                .filter_map(Option::as_ref)
                .find_map(|(texture, layer, mip)| {
                    (source == texture).then_some([*layer as i32, *mip as i32])
                })
        };
        image().unwrap_or([0, 0])
    }

    fn prepare_screen_copy(&mut self, source: u64, destination: u64, passes: &mut Vec<Op>) {
        if (source == 0 || destination == 0) && !self.screen_touched {
            // The screen slot still holds an earlier image until its first use.
            let target = self.screen_target(self.color_space);
            passes.push(Op::Pass(self.new_pass(target)));
        }
    }

    /// Copy the active framebuffer's opaque display color into the requested resolve image.
    fn plan_framebuffer_resolve(
        &mut self,
        source: u64,
        destination: u64,
        retained: bool,
        screen_camera: bool,
        screen_rect: Option<[i32; 4]>,
    ) -> Option<Copy> {
        let linear = self.color_space == 1;
        let write_srgb = !self.state.no_srgb_write && (linear || self.state.gamma_framebuffer_srgb);
        // The screen camera resolves the display encoding for the project's color space.
        // A late GL.sRGBWrite change only affects presentation of the camera intermediate
        // (tests/projects/textureformats displayEdges).
        let srgb = if screen_camera {
            linear
        } else {
            write_srgb || self.state.gamma_framebuffer_srgb
        };
        let use_srgb_view = if screen_camera { linear } else { write_srgb };
        let camera_intermediate = screen_camera && source != 0 && source != self.screen_stand_in;
        let (src, raw, encoded_view) = if source == 0 {
            let screen = self.ensure_screen(self.screen_slot);
            (&screen.texture, &screen.color, Some(&screen.srgb))
        } else {
            let texture = self.textures.get(&source)?;
            (&texture.texture, &texture.view, texture.srgb_view.as_ref())
        };
        let encoded_view = encoded_view.filter(|_| use_srgb_view);
        let stored_display_color = matches!(
            src.format().remove_srgb_suffix(),
            wgpu::TextureFormat::Rgba8Unorm | wgpu::TextureFormat::Bgra8Unorm
        );
        let convert_display = if screen_camera {
            camera_intermediate
                && (!stored_display_color || write_srgb != srgb || (srgb && encoded_view.is_none()))
        } else {
            srgb && encoded_view.is_none()
        };
        let display_srgb = if screen_camera { write_srgb } else { srgb };
        let view = encoded_view.unwrap_or(raw).clone();
        let src = src.clone();
        let target = self.textures.get(&destination)?;
        let dst = if retained {
            self.resolve_history.get(&target.texture)?.texture.clone()
        } else if target.samples == 1 {
            target.texture.clone()
        } else {
            return None;
        };
        let format = dst.format().remove_srgb_suffix();
        if target.depth_only
            || !target
                .texture
                .usage()
                .contains(wgpu::TextureUsages::RENDER_ATTACHMENT)
            || !matches!(
                format.sample_type(None, None),
                Some(wgpu::TextureSampleType::Float { .. })
            )
        {
            self.warn_once(format!("framebuffer-resolve-{destination}"), || {
                format!("ResolveAntiAliasedSurface cannot copy framebuffer color into {format:?}: skipped")
            });
            return None;
        }
        let destination_srgb = if screen_camera { linear } else { write_srgb };
        let destination_format = if destination_srgb && target.srgb_view.is_some() {
            format.add_srgb_suffix()
        } else {
            format
        };
        let (mut source_origin, destination_origin, size, resized) = match screen_rect {
            Some([x, y, width, height]) => {
                let source_width = width.max(0) as u32;
                let source_height = height.max(0) as u32;
                let (sx, dx, width) =
                    shifted_copy_axis(x, source_width, dst.width().min(self.screen_size.0))?;
                let (sy, dy, height) =
                    shifted_copy_axis(y, source_height, dst.height().min(self.screen_size.1))?;
                (
                    wgpu::Origin3d { x: sx, y: sy, z: 0 },
                    wgpu::Origin3d { x: dx, y: dy, z: 0 },
                    wgpu::Extent3d {
                        width,
                        height,
                        depth_or_array_layers: 1,
                    },
                    (source_width != src.width() || source_height != src.height())
                        .then_some([source_width, source_height]),
                )
            }
            None => (
                wgpu::Origin3d::ZERO,
                wgpu::Origin3d::ZERO,
                wgpu::Extent3d {
                    width: src.width().min(dst.width()),
                    height: src.height().min(dst.height()),
                    depth_or_array_layers: 1,
                },
                None,
            ),
        };
        // A resized intermediate is sampled over the late viewport. The scratch image only
        // needs the visible pixels, including when the new viewport extends past the display.
        let resize_uniform = resized.map(|[width, height]| {
            let screen = [
                source_origin.x as f32,
                source_origin.y as f32,
                1.0 / width as f32,
                1.0 / height as f32,
            ];
            source_origin = wgpu::Origin3d::ZERO;
            self.push_uniform(DrawUniform {
                screen,
                ..DrawUniform::zeroed()
            })
        });
        let display = (convert_display || resize_uniform.is_some()).then_some(FramebufferDisplay {
            format: if display_srgb {
                wgpu::TextureFormat::Rgba8UnormSrgb
            } else {
                wgpu::TextureFormat::Rgba8Unorm
            },
            resize_uniform,
        });
        Some(Copy {
            source: src,
            source_mip: 0,
            source_origin,
            destination: dst,
            destination_mip: 0,
            destination_origin,
            size,
            channels: Some(ChannelCopy {
                source: view,
                size: wgpu::Extent3d {
                    width: source_origin.x + size.width,
                    height: source_origin.y + size.height,
                    depth_or_array_layers: 1,
                },
                transfer: ChannelTransfer::Framebuffer {
                    srgb,
                    display,
                    destination_format,
                },
            }),
        })
    }

    /// A copy between two textures (0: the screen), clipped to both; formats must match.
    fn plan_copy(
        &mut self,
        source: u64,
        [source_slice, source_mip]: [i32; 2],
        rect: [i32; 4],
        destination: u64,
        [destination_slice, destination_mip]: [i32; 2],
        [dx, dy]: [i32; 2],
        resolved_images: bool,
    ) -> Option<Copy> {
        let texture = |this: &mut Self, handle: u64| -> Option<wgpu::Texture> {
            if handle == 0 {
                Some(this.ensure_screen(this.screen_slot).texture.clone())
            } else {
                this.textures.get(&handle).map(|t| t.texture.clone())
            }
        };
        let (Some(src), Some(dst)) = (texture(self, source), texture(self, destination)) else {
            self.warn_once(format!("copy{source}-{destination}"), || {
                format!("CopyTexture between unknown textures {source} and {destination}: skipped")
            });
            return None;
        };
        let depth_only = |handle| self.textures.get(&handle).is_some_and(|t| t.depth_only);
        if depth_only(source) || depth_only(destination) {
            // Its texels are its depth, which CopyTexture does not copy here (unmeasured).
            self.warn_once(format!("copy-depth-only{source}-{destination}"), || {
                format!("CopyTexture from {source} to {destination} involves a depth-only render texture, which has no color image: skipped")
            });
            return None;
        }
        let opaque = |handle| {
            self.textures
                .get(&handle)
                .is_some_and(|t| t.format == texture_format::RGB16_SNORM)
        };
        if opaque(source) != opaque(destination) {
            self.warn_once(format!("copyrgb16-{source}-{destination}"), || {
                "CopyTexture requires matching three-channel signed formats: skipped".into()
            });
            return None;
        }
        // The screen, a grab of it and its stand-in are stored in the screen's channel order.
        let screen_end = |handle: u64| {
            handle == 0
                || self.grabs.contains(handle)
                || self.textures.get(&handle).is_some_and(|t| t.screen_order)
        };
        let screen_order =
            self.conventions.screen_bgra && (screen_end(source) || screen_end(destination));
        // Unity's B8G8R8A8 textures are kept red first; where the device stores them blue first
        // a CopyTexture (`resolved_images`) into or out of one stored red first moves the stored
        // bytes. A resolve (ResolveAntiAliasedSurface) keeps the channels (unmeasured).
        let blue_first = |handle: u64| self.textures.get(&handle).is_some_and(|t| t.blue_first);
        let bytes_swap = resolved_images
            && self.conventions.bgra_copies_bytes
            && !screen_end(source)
            && !screen_end(destination)
            && blue_first(source) != blue_first(destination);
        let channel_kind = bytes_swap
            .then(|| swapped_copy_kind(src.format(), dst.format()))
            .flatten()
            .or_else(|| channel_copy_kind(src.format(), dst.format(), screen_order))
            .filter(|_| {
                src.dimension() == wgpu::TextureDimension::D2
                    && dst.dimension() == wgpu::TextureDimension::D2
            });
        #[cfg(target_os = "linux")]
        let exact_bgra = self.vulkan_plugins.is_some();
        #[cfg(not(target_os = "linux"))]
        let exact_bgra = false;
        if !raw_copy_compatible(src.format(), dst.format(), exact_bgra) && channel_kind.is_none() {
            self.warn_once(format!("copyfmt{source}-{destination}"), || {
                format!(
                    "CopyTexture from {:?} to {:?}: the formats differ (Unity needs them compatible): skipped",
                    src.format(),
                    dst.format()
                )
            });
            return None;
        }
        // Texture copies access the resolved image and leave the multisampled attachment intact.
        let src = if resolved_images {
            self.resolve_history
                .get(&src)
                .map_or(src.clone(), |history| history.texture.clone())
        } else {
            src
        };
        let destination_attachment = dst.clone();
        let dst = if let Some(history) = self.resolve_history.get(&dst) {
            history.texture.clone()
        } else {
            dst
        };
        let (smip, dmip) = (source_mip.max(0) as u32, destination_mip.max(0) as u32);
        if smip >= src.mip_level_count() || dmip >= dst.mip_level_count() {
            return None;
        }
        let source_depth = src
            .size()
            .mip_level_size(smip, src.dimension())
            .depth_or_array_layers;
        let destination_depth = dst
            .size()
            .mip_level_size(dmip, dst.dimension())
            .depth_or_array_layers;
        if source_slice < 0
            || source_slice as u32 >= source_depth
            || destination_slice < 0
            || destination_slice as u32 >= destination_depth
        {
            self.warn_once(format!("copy-slice{source}/{smip}/{source_slice}-{destination}/{dmip}/{destination_slice}"), || {
                "CopyTexture element is outside the source or destination mip: skipped".into()
            });
            return None;
        }
        let sw = (src.width() >> smip).max(1) as i32;
        let sh = (src.height() >> smip).max(1) as i32;
        let dw = (dst.width() >> dmip).max(1) as i32;
        let dh = (dst.height() >> dmip).max(1) as i32;
        let [mut x, mut y, mut w, mut h] = rect;
        let whole_mip = w < 0;
        if whole_mip {
            let extent = if self.plugins.external_copy_layouts.contains(&source) {
                [1, 1]
            } else {
                [sw, sh]
            };
            [x, y, w, h] = [0, 0, extent[0], extent[1]];
        }
        let w = w.min(sw - x).min(dw - dx);
        let h = h.min(sh - y).min(dh - dy);
        if w <= 0 || h <= 0 || x < 0 || y < 0 || dx < 0 || dy < 0 {
            return None;
        }
        // Regions are bottom-up (Unity's); a screen stored top-down (or its stand-in) counts its
        // rows from the top. The texels are copied as stored, as the device copies its screen.
        let top_down = self.conventions.top_down(true);
        let y = if top_down && self.stored_as_screen(source) {
            sh - y - h
        } else {
            y
        };
        let dy = if top_down && self.stored_as_screen(destination) {
            dh - dy - h
        } else {
            dy
        };
        let Some(size) = resources::copy_extent(
            src.format(),
            whole_mip,
            [x as u32, y as u32],
            [dx as u32, dy as u32],
            [w as u32, h as u32],
        ) else {
            self.warn_once(format!("copy-blocks{source}-{destination}"), || {
                "CopyTexture region origins and dimensions must align to the texture's block footprint: skipped".into()
            });
            return None;
        };
        self.mark_resolve_copied(&destination_attachment);
        let channels = channel_kind.map(|kind| ChannelCopy {
            source: src.create_view(&wgpu::TextureViewDescriptor {
                label: Some("unbound channel copy source"),
                format: Some(src.format().remove_srgb_suffix()),
                dimension: Some(wgpu::TextureViewDimension::D2),
                base_mip_level: smip,
                mip_level_count: Some(1),
                base_array_layer: source_slice.max(0) as u32,
                array_layer_count: Some(1),
                ..Default::default()
            }),
            size: wgpu::Extent3d {
                width: sw as u32,
                height: sh as u32,
                depth_or_array_layers: 1,
            },
            transfer: ChannelTransfer::Storage(kind),
        });
        Some(Copy {
            channels,
            source: src,
            source_mip: smip,
            source_origin: wgpu::Origin3d {
                x: x as u32,
                y: y as u32,
                z: source_slice.max(0) as u32,
            },
            destination: dst,
            destination_mip: dmip,
            destination_origin: wgpu::Origin3d {
                x: dx as u32,
                y: dy as u32,
                z: destination_slice.max(0) as u32,
            },
            size,
        })
    }

    /// Uploads the planned uniforms, encodes the passes and queues them.
    fn encode(&mut self, passes: Vec<Op>) {
        if passes.is_empty() {
            return;
        }
        self.encoded_ops += passes.len();
        let device = self.device.clone();
        let bytes = self.uniforms.len().max(1) as u64 * DRAW_SLOT;
        if self.draw_buffer.as_ref().is_none_or(|b| b.size() < bytes) {
            let buffer = device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound draws"),
                size: bytes.next_power_of_two().max(DRAW_SLOT * 64),
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            });
            self.draw_bind_group = Some(device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound draws"),
                layout: &self.pipelines.draw_layout,
                entries: &[wgpu::BindGroupEntry {
                    binding: 0,
                    resource: wgpu::BindingResource::Buffer(wgpu::BufferBinding {
                        buffer: &buffer,
                        offset: 0,
                        size: wgpu::BufferSize::new(size_of::<DrawUniform>() as u64),
                    }),
                }],
            }));
            self.draw_buffer = Some(buffer);
            self.draw_buffer_gen = self.draw_buffer_gen.wrapping_add(1).max(1);
        }
        if !self.uniforms.is_empty() {
            const _: () = assert!(size_of::<DrawUniform>() == DRAW_SLOT as usize);
            self.queue.write_buffer(
                self.draw_buffer.as_ref().unwrap(),
                0,
                bytemuck::cast_slice(&self.uniforms),
            );
        }
        self.compiled.upload(
            &device,
            &self.queue,
            self.draw_buffer.as_ref().unwrap(),
            self.draw_buffer_gen,
            size_of::<DrawUniform>() as u64,
        );
        let draw_bg = self.draw_bind_group.clone().unwrap();
        let mut encoder = device.create_command_encoder(&wgpu::CommandEncoderDescriptor {
            label: Some("unbound commands"),
        });
        for op in &passes {
            let pass = match op {
                Op::Pass(pass) => pass,
                Op::Mips(texture, format, layers) => {
                    self.mipmaps
                        .encode(&mut encoder, texture, *format, 0, *layers);
                    continue;
                }
                Op::Dispatch(d) => {
                    compute::encode(&mut encoder, d);
                    continue;
                }
                Op::IndirectArgs(copy) => {
                    copy.encode(&mut encoder);
                    continue;
                }
                Op::Window(copy) => {
                    copy.encode(&mut encoder, self.compiled.uniform_buffer());
                    continue;
                }
                Op::BufferCopy(c) => {
                    compute::encode_copy(&mut encoder, c);
                    continue;
                }
                Op::DepthBlit(b) => {
                    #[cfg(target_os = "linux")]
                    if let Some(native) = &b.native {
                        native.encode(&mut encoder);
                        continue;
                    }
                    let (pipeline, layout) = self.depth_blit_pipeline(
                        b.source_samples > 1,
                        b.samples,
                        b.samples > 1 && b.samples == b.source_samples,
                        b.format,
                        b.resolve_sample,
                        b.flip,
                    );
                    let group = b.group.clone().unwrap_or_else(|| {
                        device.create_bind_group(&wgpu::BindGroupDescriptor {
                            label: Some("unbound depth blit"),
                            layout: &layout,
                            entries: &[wgpu::BindGroupEntry {
                                binding: 0,
                                resource: wgpu::BindingResource::TextureView(&b.source),
                            }],
                        })
                    });
                    let mut rp = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                        label: Some("unbound depth blit"),
                        color_attachments: &[],
                        depth_stencil_attachment: Some(wgpu::RenderPassDepthStencilAttachment {
                            view: &b.destination,
                            depth_ops: Some(wgpu::Operations {
                                load: wgpu::LoadOp::Load,
                                store: wgpu::StoreOp::Store,
                            }),
                            stencil_ops: b.format.has_stencil_aspect().then_some(
                                wgpu::Operations {
                                    load: wgpu::LoadOp::Load,
                                    store: wgpu::StoreOp::Store,
                                },
                            ),
                        }),
                        timestamp_writes: None,
                        occlusion_query_set: None,
                        multiview_mask: None,
                    });
                    rp.set_pipeline(&pipeline);
                    rp.set_bind_group(0, &group, &[]);
                    rp.draw(0..3, 0..1);
                    drop(rp);
                    #[cfg(target_os = "linux")]
                    if let Some(native) = &b.native_after {
                        native.encode(&mut encoder);
                    }
                    continue;
                }
                Op::DepthCopy(c) => {
                    let filled = resources::filled_depth_copy(c.target.texture().format());
                    let (pipeline, layout) =
                        self.depth_copy_pipeline(c.kind, c.depth.texture().sample_count(), filled);
                    let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
                        label: Some("unbound depth copy"),
                        layout: &layout,
                        entries: &[wgpu::BindGroupEntry {
                            binding: 0,
                            resource: wgpu::BindingResource::TextureView(&c.depth),
                        }],
                    });
                    let mut rp = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                        label: Some("unbound depth copy"),
                        color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                            view: &c.target,
                            depth_slice: None,
                            resolve_target: None,
                            ops: wgpu::Operations {
                                load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                                store: wgpu::StoreOp::Store,
                            },
                        })],
                        depth_stencil_attachment: None,
                        timestamp_writes: None,
                        occlusion_query_set: None,
                        multiview_mask: None,
                    });
                    rp.set_pipeline(&pipeline);
                    rp.set_bind_group(0, &group, &[]);
                    rp.draw(0..3, 0..1);
                    continue;
                }
                Op::Copy(c) => {
                    // Texels in another storage are drawn into a level of the destination's first.
                    let repacked = c.channels.as_ref().map(|channels| {
                        let (pipeline, group, format) = match channels.transfer {
                            ChannelTransfer::Storage(kind) => {
                                let (pipeline, layout) =
                                    self.depth_copy_pipeline(kind, c.source.sample_count(), false);
                                let group =
                                    Self::channel_copy_group(&device, &layout, &channels.source);
                                (pipeline, group, c.destination.format().remove_srgb_suffix())
                            }
                            ChannelTransfer::Framebuffer {
                                srgb,
                                display,
                                destination_format,
                            } => {
                                let repack;
                                let view = if let Some(FramebufferDisplay {
                                    format,
                                    resize_uniform,
                                }) = display
                                {
                                    repack = self.repack_texture(format, channels.size, true);
                                    let transfer = if resize_uniform.is_some() {
                                        FramebufferTransfer::ResizeToDisplay
                                    } else {
                                        FramebufferTransfer::LinearToDisplay
                                    };
                                    let (pipeline, group) = self.framebuffer_copy_resources(
                                        &channels.source,
                                        format,
                                        transfer,
                                    );
                                    draw_channel_copy(
                                        &mut encoder,
                                        &repack.target,
                                        &pipeline,
                                        &group,
                                        resize_uniform.map(|offset| (&draw_bg, offset)),
                                    );
                                    repack.sample(srgb)
                                } else {
                                    &channels.source
                                };
                                let transfer = if srgb {
                                    FramebufferTransfer::SrgbToTexture
                                } else {
                                    FramebufferTransfer::UnormToTexture
                                };
                                let (pipeline, group) = self.framebuffer_copy_resources(
                                    view,
                                    destination_format,
                                    transfer,
                                );
                                (pipeline, group, destination_format)
                            }
                        };
                        let repacked = self.repack_texture(format, channels.size, false);
                        draw_channel_copy(&mut encoder, &repacked.target, &pipeline, &group, None);
                        repacked.texture
                    });
                    let (source, source_mip, source_origin) = match &repacked {
                        Some(texture) => (
                            texture,
                            0,
                            wgpu::Origin3d {
                                z: 0,
                                ..c.source_origin
                            },
                        ),
                        None => (&c.source, c.source_mip, c.source_origin),
                    };
                    c.encode_from(&mut encoder, (source, source_mip, source_origin));
                    continue;
                }
            };
            for copy in &pass.args {
                copy.encode(&mut encoder);
            }
            for copy in &pass.windows {
                copy.encode(&mut encoder, self.compiled.uniform_buffer());
            }
            if let (Some(seed), wgpu::LoadOp::Load) = (&pass.seed, pass.color_load)
                && let Some((texture, ..)) = &pass.target.texture
                && seed.size() == texture.size()
            {
                encoder.copy_texture_to_texture(
                    seed.as_image_copy(),
                    texture.as_image_copy(),
                    texture.size(),
                );
            }
            // The first color, then the further ones (cleared or loaded alike; integer ones
            // store the clear color's bits, packed ones round it as their device does).
            let conventions = self.conventions;
            let load = |format, alpha_one, packed, level| match pass.color_load {
                wgpu::LoadOp::Clear(mut c) => {
                    if alpha_one {
                        c.a = 1.0;
                    }
                    let c = resources::unorm8_clear_color(format, c);
                    let [r, g, b, a] = packed_clear(
                        conventions,
                        packed,
                        level,
                        [c.r as f32, c.g as f32, c.b as f32, c.a as f32],
                    );
                    let c = if packed == 0 {
                        c
                    } else {
                        wgpu::Color {
                            r: r as f64,
                            g: g as f64,
                            b: b as f64,
                            a: a as f64,
                        }
                    };
                    wgpu::LoadOp::Clear(resources::integer_clear(format, c))
                }
                load => load,
            };
            let colors: Vec<_> = pass
                .target
                .color
                .as_ref()
                .zip(pass.target.format)
                .map(|(view, format)| {
                    Some(wgpu::RenderPassColorAttachment {
                        view,
                        depth_slice: pass.target.depth_slice,
                        resolve_target: pass.target.resolve.as_ref(),
                        ops: wgpu::Operations {
                            load: load(
                                format,
                                pass.target.alpha_one,
                                pass.target.packed,
                                level_of(&pass.target.texture),
                            ),
                            store: wgpu::StoreOp::Store,
                        },
                    })
                })
                .into_iter()
                .chain(pass.target.extra.iter().map(|e| {
                    Some(wgpu::RenderPassColorAttachment {
                        view: &e.color,
                        depth_slice: e.depth_slice,
                        resolve_target: e.resolve.as_ref(),
                        ops: wgpu::Operations {
                            load: load(e.format, e.alpha_one, e.packed, level_of(&e.texture)),
                            store: wgpu::StoreOp::Store,
                        },
                    })
                }))
                .collect();
            let mut rp = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                label: Some("unbound pass"),
                color_attachments: &colors,
                depth_stencil_attachment: pass.target.depth.as_ref().map(|depth| {
                    wgpu::RenderPassDepthStencilAttachment {
                        view: depth,
                        depth_ops: Some(wgpu::Operations {
                            load: match pass.depth_clear {
                                Some((d, _)) => wgpu::LoadOp::Clear(d),
                                None => wgpu::LoadOp::Load,
                            },
                            store: wgpu::StoreOp::Store,
                        }),
                        // A buffer without stencil (16 bits) has no stencil to load or clear.
                        stencil_ops: pass
                            .target
                            .depth_format
                            .is_some_and(|f| f.has_stencil_aspect())
                            .then_some(wgpu::Operations {
                                load: match pass.depth_clear {
                                    Some((_, s)) => wgpu::LoadOp::Clear(s),
                                    None => wgpu::LoadOp::Load,
                                },
                                store: wgpu::StoreOp::Store,
                            }),
                    }
                }),
                timestamp_writes: None,
                occlusion_query_set: None,
                multiview_mask: None,
            });
            let mut current_viewport = None;
            let mut current_pipeline: Option<&wgpu::RenderPipeline> = None;
            let mut current_group1: Option<&wgpu::BindGroup> = None;
            let mut current_group2: Option<&wgpu::BindGroup> = None;
            let mut current_group3: Option<&wgpu::BindGroup> = None;
            let mut current_vb0: Option<(&wgpu::Buffer, u64)> = None;
            let mut current_vb1: Option<(&wgpu::Buffer, u64)> = None;
            let mut current_ib: Option<(&wgpu::Buffer, wgpu::IndexFormat)> = None;
            let mut current_stencil: Option<u32> = None;
            let mut current_scissor: Option<[u32; 4]> = None;
            // Viewports and scissors are bottom-up (Unity's); a target stored top-down counts its
            // rows from the top.
            let target_height = pass.target.height;
            let top_down = self.conventions.top_down(pass.target.screen_like);
            let stored = |[x, y, w, h]: [f32; 4]| {
                [
                    x,
                    if top_down {
                        target_height as f32 - y - h
                    } else {
                        y
                    },
                    w,
                    h,
                ]
            };
            let stored_rect = |[x, y, w, h]: [u32; 4]| {
                [
                    x,
                    if top_down {
                        target_height.saturating_sub(y + h)
                    } else {
                        y
                    },
                    w,
                    h,
                ]
            };
            for item in &pass.items {
                let viewport = match item {
                    PassItem::Clear(_, _, v) => {
                        clamp_viewport(*v, pass.target.width, pass.target.height)
                    }
                    PassItem::Draw(d) => d.viewport,
                };
                if viewport[2] <= 0.0 || viewport[3] <= 0.0 {
                    continue;
                }
                if current_viewport != Some(viewport) {
                    let [x, y, w, h] = stored(viewport);
                    rp.set_viewport(x, y, w, h, 0.0, 1.0);
                    current_viewport = Some(viewport);
                }
                match item {
                    PassItem::Clear(pipeline, offset, _) => {
                        let s = stored_rect(clamp_rect(
                            viewport,
                            pass.target.width,
                            pass.target.height,
                        ));
                        if current_pipeline != Some(pipeline) {
                            rp.set_pipeline(pipeline);
                            current_pipeline = Some(pipeline);
                        }
                        rp.set_bind_group(0, &draw_bg, &[*offset]);
                        current_group1 = None;
                        current_group2 = None;
                        current_group3 = None;
                        if current_stencil != Some(0) {
                            rp.set_stencil_reference(0);
                            current_stencil = Some(0);
                        }
                        if current_scissor != Some(s) {
                            rp.set_scissor_rect(s[0], s[1], s[2], s[3]);
                            current_scissor = Some(s);
                        }
                        rp.draw(0..3, 0..1);
                    }
                    PassItem::Draw(d) => {
                        let (id, ref program_offsets) = d.program;
                        let Some(uniform_group) = self.compiled.uniform_group(id) else {
                            continue;
                        };
                        if current_pipeline != Some(&d.pipeline) {
                            rp.set_pipeline(&d.pipeline);
                            current_pipeline = Some(&d.pipeline);
                        }
                        let (offsets, count) = program_offsets.dynamic_offsets(d.offset);
                        rp.set_bind_group(0, uniform_group, &offsets[..count]);
                        if current_group1 != Some(&d.material) {
                            rp.set_bind_group(1, &d.material, &[]);
                            current_group1 = Some(&d.material);
                        }
                        if let Some(texture) = &d.texture
                            && current_group2 != Some(texture)
                        {
                            rp.set_bind_group(2, texture, &[]);
                            current_group2 = Some(texture);
                        }
                        if let Some(buffers) = &d.buffers
                            && current_group3 != Some(buffers)
                        {
                            rp.set_bind_group(3, buffers, &[]);
                            current_group3 = Some(buffers);
                        }
                        let first = u64::from(d.first_bound_vertex);
                        let vb0_offset = first * size_of::<resources::Vertex>() as u64;
                        if current_vb0 != Some((&d.vertex, vb0_offset)) {
                            rp.set_vertex_buffer(0, d.vertex.slice(vb0_offset..));
                            current_vb0 = Some((&d.vertex, vb0_offset));
                        }
                        if let Some(extra) = &d.extra {
                            let vb1_offset = first * size_of::<resources::ExtraVertex>() as u64;
                            if current_vb1 != Some((extra, vb1_offset)) {
                                rp.set_vertex_buffer(1, extra.slice(vb1_offset..));
                                current_vb1 = Some((extra, vb1_offset));
                            }
                        }
                        if current_ib != Some((&d.index, d.index_format)) {
                            rp.set_index_buffer(d.index.slice(..), d.index_format);
                            current_ib = Some((&d.index, d.index_format));
                        }
                        if current_stencil != Some(d.stencil_ref) {
                            rp.set_stencil_reference(d.stencil_ref);
                            current_stencil = Some(d.stencil_ref);
                        }
                        let s = stored_rect(d.scissor);
                        if current_scissor != Some(s) {
                            rp.set_scissor_rect(s[0], s[1], s[2], s[3]);
                            current_scissor = Some(s);
                        }
                        match &d.indirect {
                            Some((args, offset, true)) => rp.draw_indexed_indirect(args, *offset),
                            Some((args, offset, false)) => rp.draw_indirect(args, *offset),
                            None => rp.draw_indexed(
                                d.range.clone(),
                                d.base_vertex,
                                d.first_instance..d.first_instance.saturating_add(d.instances),
                            ),
                        }
                    }
                }
            }
            drop(rp);
            #[cfg(target_os = "linux")]
            if let Some(native) = pass.native_depth.as_ref().and_then(|d| d.native.as_ref()) {
                native.encode(&mut encoder);
            }
            if pass.target.auto_mips {
                if let (Some((texture, layer, _)), Some(format)) =
                    (&pass.target.texture, pass.target.format)
                {
                    self.mipmaps
                        .encode(&mut encoder, texture, format, *layer, 1);
                }
            }
            for extra in &pass.target.extra {
                if extra.auto_mips {
                    if let Some((texture, layer, _)) = &extra.texture {
                        self.mipmaps
                            .encode(&mut encoder, texture, extra.format, *layer, 1);
                    }
                }
            }
        }
        self.queue.submit([encoder.finish()]);
        // The next submission's argument copies run after this one's draws.
        self.indirect_args.used = 0;
    }

    /// The pixels of a rectangle of a texture (0: this frame's screen) after everything queued:
    /// RGBA rows bottom first, 4 bytes per pixel, or 4 `f32`s when `float`.
    pub fn read_pixels(&mut self, texture: u64, rect: [i32; 4], float: bool) -> Option<Vec<u8>> {
        let options = TextureReadOptions {
            float,
            current_attachment: true,
            ..Default::default()
        };
        self.read_texture(texture, 0, 0, rect, options)
    }

    /// A read of the part of `rect` inside a screen of `size` that no pass has started:
    /// transparent black, four bytes or floats per pixel.
    fn unstarted_screen(size: (u32, u32), rect: [i32; 4], float: bool) -> Vec<u8> {
        let (tw, th) = (size.0 as i32, size.1 as i32);
        let w = ((rect[0] + rect[2]).clamp(0, tw) - rect[0].clamp(0, tw)) as usize;
        let h = ((rect[1] + rect[3]).clamp(0, th) - rect[1].clamp(0, th)) as usize;
        vec![0; w * h * if float { 16 } else { 4 }]
    }

    /// As [`Self::read_pixels`], from a mip level of a layer (an array slice, a cube face or a
    /// volume's depth slice at that level); `None` when the texture has no such level or layer.
    pub fn read_texture(
        &mut self,
        texture: u64,
        mip: u32,
        layer: u32,
        rect: [i32; 4],
        options: TextureReadOptions,
    ) -> Option<Vec<u8>> {
        let TextureReadOptions {
            float,
            current_attachment,
            unorm16_reciprocal,
        } = options;
        let source = if texture == 0 {
            if self.screen_touched {
                self.screens[self.screen_slot].as_ref()?.texture.clone()
            } else {
                // Before this frame's first screen pass the screen still shows the last frame:
                // its own slot holds an older one until that pass carries the screen forward. On
                // the first frame and after a resize it is transparent black, as that pass
                // starts it (tests/projects/screenread).
                match self.previous_screen() {
                    Some(previous) => previous,
                    // Before any frame has begun there is no screen to read.
                    None if self.frame_number == 0 => return None,
                    None if mip != 0 || layer != 0 => return None,
                    None => return Some(Self::unstarted_screen(self.screen_size, rect, float)),
                }
            }
        } else {
            let gpu = self.textures.get(&texture)?;
            if gpu.depth_only {
                self.warn_once(format!("read-depth-only{texture}"), || {
                    format!("reading the pixels of depth-only render texture {texture}, which has no color image (unmeasured): nothing read")
                });
                return None;
            }
            let current = &self.textures.get(&texture)?.texture;
            if !current_attachment {
                self.resolve_history
                    .get(current)
                    .map_or(current, |history| &history.texture)
                    .clone()
            } else {
                current.clone()
            }
        };
        if mip >= source.mip_level_count() {
            return None;
        }
        let level = source.size().mip_level_size(mip, source.dimension());
        if layer >= level.depth_or_array_layers {
            return None;
        }
        let format = source.format();
        // Integer texels have no colors to read (ReadPixels refuses those targets); blocks are
        // no texels.
        if resources::is_integer(format) || format.is_compressed() {
            return None;
        }
        let (tw, th) = (level.width as i32, level.height as i32);
        let x0 = rect[0].clamp(0, tw);
        let y0 = rect[1].clamp(0, th);
        let x1 = (rect[0] + rect[2]).clamp(0, tw);
        let y1 = (rect[1] + rect[3]).clamp(0, th);
        let (w, h) = ((x1 - x0) as u32, (y1 - y0) as u32);
        if w == 0 || h == 0 {
            return Some(Vec::new());
        }
        // A screen stored top-down (or its stand-in) is read from its last rows up, so rows
        // still come bottom first.
        let top_down = self.stored_as_screen(texture) && self.conventions.top_down(true);
        let y0 = if top_down { th - y1 } else { y0 };
        let texel = format.block_copy_size(None)?;
        let row = (w * texel).next_multiple_of(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT);
        self.read_gpu_data(
            row as u64 * h as u64,
            "unbound read pixels",
            |encoder, staging| {
                encoder.copy_texture_to_buffer(
                    wgpu::TexelCopyTextureInfo {
                        texture: &source,
                        mip_level: mip,
                        origin: wgpu::Origin3d {
                            x: x0 as u32,
                            y: y0 as u32,
                            z: layer,
                        },
                        aspect: wgpu::TextureAspect::All,
                    },
                    wgpu::TexelCopyBufferInfo {
                        buffer: staging,
                        layout: wgpu::TexelCopyBufferLayout {
                            offset: 0,
                            bytes_per_row: Some(row),
                            rows_per_image: Some(h),
                        },
                    },
                    wgpu::Extent3d {
                        width: w,
                        height: h,
                        depth_or_array_layers: 1,
                    },
                );
            },
            |data| {
                let mut out = Vec::with_capacity((w * h) as usize * if float { 16 } else { 4 });
                for r in 0..h as usize {
                    let r = if top_down { h as usize - 1 - r } else { r };
                    let line = &data[r * row as usize..r * row as usize + (w * texel) as usize];
                    match format {
                        wgpu::TextureFormat::Rgba8Unorm
                        | wgpu::TextureFormat::Rgba8UnormSrgb
                        | wgpu::TextureFormat::Bgra8Unorm
                        | wgpu::TextureFormat::Bgra8UnormSrgb => {
                            for pixel in line.chunks_exact(4) {
                                for b in resources::rgba8_texel_to_rgba(format, pixel)? {
                                    if float {
                                        out.extend_from_slice(&(b as f32 / 255.0).to_le_bytes());
                                    } else {
                                        out.push(b);
                                    }
                                }
                            }
                        }
                        wgpu::TextureFormat::Rgb9e5Ufloat => {
                            for pixel in line.as_chunks::<4>().0 {
                                let rgb =
                                    unbound_gi::solver::decode_rgb9e5(u32::from_le_bytes(*pixel));
                                for v in [rgb[0], rgb[1], rgb[2], 1.0] {
                                    if float {
                                        out.extend_from_slice(&v.to_le_bytes());
                                    } else {
                                        out.push((v.clamp(0.0, 1.0) * 255.0).round() as u8);
                                    }
                                }
                            }
                        }
                        wgpu::TextureFormat::R32Float
                        | wgpu::TextureFormat::Rg32Float
                        | wgpu::TextureFormat::Rgba32Float
                        | wgpu::TextureFormat::R16Float
                        | wgpu::TextureFormat::Rg16Float
                        | wgpu::TextureFormat::Rgba16Float => {
                            let half = matches!(
                                format,
                                wgpu::TextureFormat::R16Float
                                    | wgpu::TextureFormat::Rg16Float
                                    | wgpu::TextureFormat::Rgba16Float
                            );
                            for pixel in line.chunks_exact(texel as usize) {
                                let mut rgba = [0.0f32, 0.0, 0.0, 1.0];
                                for (i, bytes) in
                                    pixel.chunks_exact(if half { 2 } else { 4 }).enumerate()
                                {
                                    rgba[i] = if half {
                                        resources::f16_to_f32(u16::from_le_bytes(
                                            bytes.try_into().unwrap(),
                                        ))
                                    } else {
                                        f32::from_le_bytes(bytes.try_into().unwrap())
                                    };
                                }
                                for v in rgba {
                                    if float {
                                        out.extend_from_slice(&v.to_le_bytes());
                                    } else {
                                        out.push((v.clamp(0.0, 1.0) * 255.0).round() as u8);
                                    }
                                }
                            }
                        }
                        wgpu::TextureFormat::R16Unorm
                        | wgpu::TextureFormat::Rg16Unorm
                        | wgpu::TextureFormat::Rgba16Unorm
                        | wgpu::TextureFormat::Rgb10a2Unorm
                        | wgpu::TextureFormat::R8Snorm
                        | wgpu::TextureFormat::Rg8Snorm
                        | wgpu::TextureFormat::R16Snorm
                        | wgpu::TextureFormat::Rg16Snorm
                        | wgpu::TextureFormat::Rgba16Snorm
                        | wgpu::TextureFormat::Rgba8Snorm => {
                            for pixel in line.chunks_exact(texel as usize) {
                                let rgba = resources::norm_texel_to_f32(
                                    format,
                                    pixel,
                                    unorm16_reciprocal,
                                )?;
                                for v in rgba {
                                    if float {
                                        out.extend_from_slice(&v.to_le_bytes());
                                    } else {
                                        // Negative (signed) values read as 0.
                                        out.push((v.clamp(0.0, 1.0) * 255.0).round() as u8);
                                    }
                                }
                            }
                        }
                        // One- and two-channel 8-bit texels read as they sample.
                        wgpu::TextureFormat::R8Unorm | wgpu::TextureFormat::Rg8Unorm => {
                            for pixel in line.chunks_exact(texel as usize) {
                                let g = pixel.get(1).copied().unwrap_or(0);
                                for b in [pixel[0], g, 0, 255] {
                                    if float {
                                        out.extend_from_slice(&(b as f32 / 255.0).to_le_bytes());
                                    } else {
                                        out.push(b);
                                    }
                                }
                            }
                        }
                        wgpu::TextureFormat::Rg11b10Ufloat => {
                            for c in line.as_chunks::<4>().0 {
                                let [r, g, b] = resources::rg11b10_to_f32(u32::from_le_bytes(*c));
                                for v in [r, g, b, 1.0] {
                                    if float {
                                        out.extend_from_slice(&v.to_le_bytes());
                                    } else {
                                        out.push((v.clamp(0.0, 1.0) * 255.0).round() as u8);
                                    }
                                }
                            }
                        }
                        _ => {
                            for &b in line {
                                if float {
                                    out.extend_from_slice(&(b as f32 / 255.0).to_le_bytes());
                                } else {
                                    out.push(b);
                                }
                            }
                        }
                    }
                }
                Some(out)
            },
        )
    }
}

/// The texture whose depth buffer a target binds (`SetTarget`'s color and depth handles, as
/// `resolve_target` attaches them): none for [`commands::NO_TEXTURE`] or the screen, the color's
/// own for 0 or the color itself, else the depth handle's.
fn depth_owner(color: u64, depth: u64) -> Option<u64> {
    match depth {
        commands::NO_TEXTURE => None,
        0 => (color != 0).then_some(color),
        d => Some(d),
    }
}

/// Copies a float texture's values into an integer texture of its size as their bits, texel for
/// texel (`SCALAR`: `u32` or `i32`).
/// Copies 8-bit normalized texels into a texture of their level's size in another channel count
/// ([`ChannelCopy`]).
const CHANNEL_COPY_WGSL: &str = r#"
@group(0) @binding(0) var source: texture_2d<f32>;

@vertex
fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
}

@fragment
fn fs(@builtin(position) p: vec4<f32>) -> @location(0) vec4<f32> {
    let texel = textureLoad(source, vec2<i32>(p.xy), 0);
    return texel;
}
"#;

// Resampling uses the same frame uniform buffer as draws. The screen field holds the
// visible region's offset in the presentation rectangle and its reciprocal extent.
const FRAMEBUFFER_RESIZE_WGSL: &str = r#"
struct DisplayUniform {
    model: mat4x4<f32>,
    view: mat4x4<f32>,
    projection: mat4x4<f32>,
    color: vec4<f32>,
    viewport: vec4<f32>,
}
@group(0) @binding(1) var source_sampler: sampler;
@group(1) @binding(0) var<uniform> display: DisplayUniform;

fn resample_display(pixel: vec2<f32>) -> vec4<f32> {
    let uv = (pixel + display.viewport.xy) * display.viewport.zw;
    return textureSampleLevel(source, source_sampler, uv, 0.0);
}
"#;

// The OpenGL Core framebuffer copy expands its normalized colors and truncates them when the
// destination is half precision (Unity 6000.0.71f1, tests/projects/textureformats screenColor).
const FRAMEBUFFER_UNORM_COPY_WGSL: &str = r#"
fn expand_byte(byte: u32) -> f32 {
    if (byte == 0u) {
        return 0.0;
    }
    // Round the integer quotient to a 24-bit significand. GPU reciprocals introduce an extra
    // rounding step for some of the bytes.
    let shift = countLeadingZeros(byte);
    let significand = ((byte << shift) + 127u) / 255u;
    let exponent = 150u - shift;
    return bitcast<f32>((exponent << 23u) + significand - (1u << 23u));
}

fn truncate_half(value: f32) -> f32 {
    let bits = pack2x16float(vec2<f32>(value, 0.0));
    let nearest = unpack2x16float(bits).x;
    return select(nearest, unpack2x16float(bits - 1u).x, nearest > value);
}
"#;

/// The visible part of an image placed at a display coordinate, on one axis.
fn shifted_copy_axis(origin: i32, source: u32, destination: u32) -> Option<(u32, u32, u32)> {
    let from = origin.saturating_neg().max(0) as u32;
    let to = origin.max(0) as u32;
    let size = source
        .saturating_sub(from)
        .min(destination.saturating_sub(to));
    (size > 0).then_some((from, to, size))
}

fn draw_channel_copy(
    encoder: &mut wgpu::CommandEncoder,
    target: &wgpu::TextureView,
    pipeline: &wgpu::RenderPipeline,
    group: &wgpu::BindGroup,
    resize: Option<(&wgpu::BindGroup, u32)>,
) {
    let mut pass = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
        label: Some("unbound channel copy"),
        color_attachments: &[Some(wgpu::RenderPassColorAttachment {
            view: target,
            depth_slice: None,
            resolve_target: None,
            ops: wgpu::Operations {
                load: wgpu::LoadOp::Clear(wgpu::Color::BLACK),
                store: wgpu::StoreOp::Store,
            },
        })],
        depth_stencil_attachment: None,
        timestamp_writes: None,
        occlusion_query_set: None,
        multiview_mask: None,
    });
    pass.set_pipeline(pipeline);
    pass.set_bind_group(0, group, &[]);
    if let Some((group, offset)) = resize {
        pass.set_bind_group(1, group, &[offset]);
    }
    pass.draw(0..3, 0..1);
}

/// The mip level of a target's single-sample texture (texture, layer, level); 0 without one.
fn level_of(texture: &Option<(wgpu::Texture, u32, u32)>) -> u32 {
    texture.as_ref().map_or(0, |(_, _, level)| *level)
}

/// A clear color as a packed color keeps it: rounded to its channels' bits where the device's
/// clears round (`DeviceConventions::packed_clears_round`), else as given (its packing then
/// truncates it as a draw's). On Vulkan only a texture's first level rounds so (the Vulkan
/// Editor's clear of mip level 1 and a further color's clear at level 0 differ, Unity
/// 6000.0.71f1, tests/projects/samplerstates `Packed.*.mip.clear`, `Packed.*.mrt.clear`); where
/// the device's writes round too (Metal) a clear rounds at every level, to the nearest level's
/// byte (`resources::packed_clear`; `metal.jsonl` `Packed.*.mip.clear`: 0.7 clears mip 1 of a
/// 4-4-4-4 target to 11/15).
fn packed_clear(
    conventions: DeviceConventions,
    packed: u8,
    level: u32,
    color: [f32; 4],
) -> [f32; 4] {
    // Where writes round (Metal) every level's clear rounds; on Vulkan only the first level's.
    if !conventions.packed_clears_round || (level != 0 && !conventions.packed_writes_round) {
        return color;
    }
    match resources::packed_bits(packed) {
        Some(bits) => resources::packed_clear(color, bits, conventions.packed_writes_round),
        None => color,
    }
}

/// A packed target's texels (`CopyKind::Packed565`): each 8-bit channel keeps its top `BITS` bits,
/// repeated below them to fill the byte, or (`ROUNDS`, `DeviceConventions::packed_writes_round`)
/// rounds to the nearest of its `BITS` bits' levels, kept as the nearest 8-bit value
/// (`resources::level_byte`); 0 bits: no channel, read as 1.
const PACKED_TEXELS_WGSL: &str = r#"
@group(0) @binding(0) var source: texture_2d<f32>;

@vertex
fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
}

fn keep(value: f32, bits: u32) -> f32 {
    if (bits == 0u) {
        return 1.0;
    }
    if (ROUNDS) {
        let levels = f32((1u << bits) - 1u);
        let level = floor(clamp(value, 0.0, 1.0) * levels + 0.5);
        return floor(level * 255.0 / levels + 0.5) / 255.0;
    }
    let byte = u32(round(clamp(value, 0.0, 1.0) * 255.0));
    var widened = (byte >> (8u - bits)) << (8u - bits);
    var filled = bits;
    loop {
        if (filled >= 8u) {
            break;
        }
        widened = widened | (widened >> filled);
        filled = filled * 2u;
    }
    return f32(widened & 255u) / 255.0;
}

@fragment
fn fs(@builtin(position) p: vec4<f32>) -> @location(0) vec4<f32> {
    let texel = textureLoad(source, vec2<i32>(p.xy), 0);
    let bits = BITS;
    return vec4<f32>(keep(texel.r, bits.x), keep(texel.g, bits.y), keep(texel.b, bits.z), keep(texel.w, bits.w));
}
"#;

const BITS_COPY_WGSL: &str = r#"
@group(0) @binding(0) var source: texture_2d<f32>;

@vertex
fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
}

@fragment
fn fs(@builtin(position) p: vec4<f32>) -> @location(0) vec4<SCALAR> {
    return bitcast<vec4<SCALAR>>(textureLoad(source, vec2<i32>(p.xy), 0));
}
"#;

// The depth shaders read depth through a float texture binding (an unfilterable-float view of a
// depth texture, its depth in x): every backend can load from it, OpenGL's GLSL included.

/// Copies a depth buffer into a float color texture of its size, texel for texel.
const DEPTH_COPY_WGSL: &str = r#"
@group(0) @binding(0) var depth: texture_2d<f32>;

@vertex
fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
}

@fragment
fn fs(@builtin(position) p: vec4<f32>) -> @location(0) vec4<f32> {
    // The depth in red, green and blue: a one-channel copy keeps red alone.
    return vec4<f32>(textureLoad(depth, vec2<i32>(p.xy), 0).xxx, 1.0);
}
"#;

/// `CopyDepth` from one sample: the source's only (or first) one into every destination sample.
const DEPTH_BLIT_WGSL: &str = r#"
@group(0) @binding(0) var depth: texture_2d<f32>;

@vertex
fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
}

@fragment
fn fs(@builtin(position) p: vec4<f32>) -> @builtin(frag_depth) f32 {
    return textureLoad(depth, vec2<i32>(p.xy), 0).x;
}
"#;

/// `CopyDepth` between equal sample counts: each sample from the source's of its index.
const DEPTH_BLIT_PER_SAMPLE_WGSL: &str = r#"
@group(0) @binding(0) var depth: texture_multisampled_2d<f32>;

@vertex
fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    return vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
}

@fragment
fn fs(@builtin(position) p: vec4<f32>, @builtin(sample_index) s: u32) -> @builtin(frag_depth) f32 {
    return textureLoad(depth, vec2<i32>(p.xy), i32(s)).x;
}
"#;

fn last_pass(ops: &mut [Op]) -> &mut Pass {
    match ops.last_mut() {
        Some(Op::Pass(pass)) => pass,
        _ => unreachable!("a pass is open"),
    }
}

const IDENTITY: [f32; 16] = [
    1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
];

/// A viewport (x, y, w, h; bottom-up rows) cut to a target: wgpu's must lie inside it.
fn clamp_viewport(v: [f32; 4], w: u32, h: u32) -> [f32; 4] {
    let x0 = v[0].clamp(0.0, w as f32);
    let y0 = v[1].clamp(0.0, h as f32);
    let x1 = (v[0] + v[2]).clamp(0.0, w as f32);
    let y1 = (v[1] + v[3]).clamp(0.0, h as f32);
    [x0, y0, (x1 - x0).max(0.0), (y1 - y0).max(0.0)]
}

/// A pixel rect (x, y, w, h; bottom-up rows) clamped to a target.
fn clamp_rect(r: [f32; 4], w: u32, h: u32) -> [u32; 4] {
    let x0 = r[0].max(0.0).min(w as f32);
    let y0 = r[1].max(0.0).min(h as f32);
    let x1 = (r[0] + r[2]).max(0.0).min(w as f32);
    let y1 = (r[1] + r[3]).max(0.0).min(h as f32);
    let (x0, y0) = (x0.round() as u32, y0.round() as u32);
    let (x1, y1) = (x1.round() as u32, y1.round() as u32);
    [x0, y0, x1.saturating_sub(x0), y1.saturating_sub(y0)]
}

fn intersect(a: [u32; 4], b: [u32; 4]) -> [u32; 4] {
    let x0 = a[0].max(b[0]);
    let y0 = a[1].max(b[1]);
    let x1 = (a[0] + a[2]).min(b[0] + b[2]);
    let y1 = (a[1] + a[3]).min(b[1] + b[3]);
    [x0, y0, x1.saturating_sub(x0), y1.saturating_sub(y0)]
}

/// `UNBOUND_TRACE` includes `render`: the renderer reports what it skips.
/// `UNBOUND_TRACE_UNIFORMS`: the shaders (by a part of the name) whose draws log their uniforms.
fn trace_uniforms() -> Option<&'static str> {
    static FILTER: std::sync::OnceLock<Option<String>> = std::sync::OnceLock::new();
    FILTER
        .get_or_init(|| std::env::var("UNBOUND_TRACE_UNIFORMS").ok())
        .as_deref()
}

pub fn trace_render() -> bool {
    static ON: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
    *ON.get_or_init(|| {
        std::env::var("UNBOUND_TRACE")
            .map(|v| v == "all" || v.split(',').any(|c| c == "render" || c == "render-detail"))
            .unwrap_or(false)
    })
}

/// A `RenderStateBlock`'s overrides over a pass's render state; the stencil reference drawn with.
fn apply_override(
    state: &mut crate::materials::RenderState,
    stencil_ref: u32,
    o: &commands::StateOverride,
) -> u32 {
    use commands::state_mask;
    if o.mask & state_mask::BLEND != 0 {
        let [write_mask, src, dst, src_alpha, dst_alpha, op, op_alpha] = o.blend;
        // One Zero for color and alpha is Blend Off.
        state.blend = if [src, dst, src_alpha, dst_alpha] == [1, 0, 1, 0] {
            None
        } else {
            Some([src, dst, src_alpha, dst_alpha])
        };
        state.blend_op = [op, op_alpha];
        state.color_mask = write_mask;
        // A render state block's blend state is every target's.
        state.targets = None;
    }
    if o.mask & state_mask::RASTER != 0 {
        state.cull = o.cull;
        state.offset = [o.offset_factor.round() as i32, o.offset_units];
    }
    if o.mask & state_mask::CULL != 0 {
        state.cull = o.cull;
    }
    if o.mask & state_mask::DEPTH != 0 {
        state.zwrite = o.zwrite;
        state.ztest = o.ztest;
        state.gui_ztest = false;
    }
    if o.mask & state_mask::ENGINE_STENCIL != 0 {
        // Compare Always (8), pass Replace (2), keep (0) on failures, every bit written: the
        // engine's bits with the kept bits of the pass's own reference, whatever its test,
        // operation and write mask (tests/projects/builtinfx `deferredStencil.*`).
        let bits = o.stencil_ref as u32;
        let kept = o.stencil_read_mask as u32;
        state.stencil = Some(crate::materials::StencilState {
            comp: 8,
            pass: 2,
            fail: 0,
            zfail: 0,
            read_mask: 0xff,
            write_mask: 0xff,
            back: [8, 2, 0, 0],
        });
        return (stencil_ref & kept) | bits;
    }
    if o.mask & state_mask::STENCIL == 0 {
        return stencil_ref;
    }
    // Front faces, then back faces (the block's own).
    state.stencil = o.stencil_enabled.then(|| crate::materials::StencilState {
        comp: o.stencil[0],
        pass: o.stencil[1],
        fail: o.stencil[2],
        zfail: o.stencil[3],
        read_mask: o.stencil_read_mask,
        write_mask: o.stencil_write_mask,
        back: [o.stencil[4], o.stencil[5], o.stencil[6], o.stencil[7]],
    });
    o.stencil_ref as u32
}

/// A texture's anisotropic level under QualitySettings.anisotropicFiltering (`[mode, forced
/// minimum, maximum]`, mode 0 off, 1 per texture, 2 forced on): forced on, every texture filters
/// at least at the forced minimum, except one whose anisoLevel is 0, which opts out (the 6000.0
/// Texture.anisoLevel contract; the Cookie importer writes 0: tests/projects/urp
/// cookie_directional); never above the maximum.
fn effective_aniso([mode, forced_min, global_max]: [i32; 3], level: i32) -> i32 {
    match mode {
        0 => 1,
        2 if level == 0 => 1,
        2 => level.max(forced_min).min(global_max),
        _ => level.min(global_max),
    }
}

/// The determinant of a column-major 4x4 matrix's upper 3x3 (the sign of its handedness).
fn determinant3(m: &[f32; 16]) -> f32 {
    m[0] * (m[5] * m[10] - m[9] * m[6]) - m[4] * (m[1] * m[10] - m[9] * m[2])
        + m[8] * (m[1] * m[6] - m[5] * m[2])
}

/// What a planned draw binds, for [`external_index_refusal`]: whether its program reads vertex
/// inputs, whether its counts are read on the GPU, and the sizes of its vertex buffers (channels
/// 0..3, and 4..7 when bound).
struct DrawInputs {
    vertex_inputs: bool,
    indirect: bool,
    vertex_bytes: u64,
    extra_bytes: Option<u64>,
}

/// How a draw reads its indices (`DrawShape`; the default: as its submesh says).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
struct DrawShape {
    /// The topology they are read as (none: the submesh's).
    topology: Option<resources::Topology>,
    /// Added to external indices (`DrawIndices`).
    base_vertex: i32,
    /// At most this many of the submesh's indices (0: all).
    count: u32,
    /// The indices are a buffer's 16-bit ones (`DrawIndices`), not the mesh's 32-bit ones.
    index16: bool,
}

/// The index format a draw of `topology` names in its pipeline: a strip's (16-bit for a buffer's
/// 16-bit indices, else 32-bit, as a mesh's are), none for a list.
pub(crate) fn strip_index(
    topology: resources::Topology,
    index16: bool,
) -> Option<wgpu::IndexFormat> {
    (topology == resources::Topology::LineStrip).then_some(if index16 {
        wgpu::IndexFormat::Uint16
    } else {
        wgpu::IndexFormat::Uint32
    })
}

/// The topology a `DrawShape` reads a draw's indices as (none: its submesh's own; the decoder
/// admits only these and that).
fn shape_topology(topology: u8) -> Option<resources::Topology> {
    match topology {
        commands::DRAW_SHAPE_TRIANGLES => Some(resources::Topology::Triangles),
        commands::DRAW_SHAPE_LINES => Some(resources::Topology::Lines),
        commands::DRAW_SHAPE_LINE_STRIP => Some(resources::Topology::LineStrip),
        commands::DRAW_SHAPE_POINTS => Some(resources::Topology::Points),
        _ => None,
    }
}

/// The vertices a draw of 16-bit indices with base vertex 0 can fetch: one per index value,
/// 0..=0xFFFF.
const INDEXED_VERTICES: u64 = 1 << 16;

/// Why draws cannot read `count` indices of `size` bytes from index `first` of a buffer (its size
/// in bytes and whether it can be read as indices; none: it does not exist), if they cannot. They
/// can when the buffer holds the range (a strip's pipeline names its index format, so any
/// topology a draw reads indices as is drawn), and each draw either reads no vertex inputs
/// (its pipeline binds no vertex buffer: "a draw call on the GPU, without a vertex buffer", the
/// indexed DrawProcedural 6000.0, so any index, base vertex or indirect count is safe) or is a
/// direct draw of 16-bit indices whose every vertex buffer holds a vertex for each of the 2^16
/// index values (the procedural stand-in's 65536): with base vertex 0, no index fetches past
/// vertex 0xFFFF (another base vertex could, so such a draw is refused).
fn external_index_refusal(
    buffer: Option<(u64, bool)>,
    (size, first, count): (u8, u32, u32),
    base_vertex: i32,
    draws: &[DrawInputs],
) -> Option<&'static str> {
    let Some((bytes, index_usage)) = buffer else {
        return Some("the buffer does not exist");
    };
    if !index_usage {
        return Some("the buffer cannot be read as indices");
    }
    if (u64::from(first) + u64::from(count)) * u64::from(size) > bytes {
        return Some("the indices pass the end of the buffer");
    }
    let vertex = size_of::<resources::Vertex>() as u64;
    let extra = size_of::<resources::ExtraVertex>() as u64;
    for d in draws.iter().filter(|d| d.vertex_inputs) {
        if size != commands::DRAW_INDEX_SIZE {
            return Some("the program reads vertex inputs, which 32-bit indices can pass");
        }
        if d.indirect {
            return Some("an indirect draw's base vertex is read on the GPU");
        }
        if base_vertex != 0 {
            return Some("the program reads vertex inputs, which a base vertex can pass");
        }
        if d.vertex_bytes / vertex < INDEXED_VERTICES
            || d.extra_bytes.is_some_and(|e| e / extra < INDEXED_VERTICES)
        {
            return Some("the draw's vertex buffers do not hold a vertex for every 16-bit index");
        }
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The engine's stencil bits (a deferred G-buffer pass's) are set with no test beside the
    /// kept bits of the pass's own reference, whatever its test, operation and write mask
    /// (tests/projects/builtinfx `deferredStencil.*`: 0xC0 over a Standard object; 0xC2 for
    /// Ref 2, 0xC1 for Ref 1 Equal Keep and Ref 1 IncrSat, 0xC8 for Ref 8 WriteMask 0, 0xC0 for
    /// Ref 16 and Ref 32).
    #[test]
    fn engine_stencil_bits_join_the_passes_own() {
        let engine = commands::StateOverride {
            mask: commands::state_mask::ENGINE_STENCIL,
            blend: [0; 7],
            cull: 0,
            offset_units: 0,
            offset_factor: 0.0,
            zwrite: false,
            ztest: 0,
            stencil_enabled: false,
            stencil_ref: 0xC0,
            stencil_read_mask: 0x0F,
            stencil_write_mask: 0,
            stencil: [0; 8],
        };
        let mut plain = crate::materials::RenderState::default();
        assert_eq!(apply_override(&mut plain, 0, &engine), 0xC0);
        let s = plain.stencil.expect("the engine's stencil");
        assert_eq!(
            (s.comp, s.pass, s.fail, s.zfail, s.write_mask),
            (8, 2, 0, 0, 0xff)
        );
        let declared = |comp, pass, write_mask| crate::materials::RenderState {
            stencil: Some(crate::materials::StencilState {
                comp,
                pass,
                fail: 0,
                zfail: 0,
                read_mask: 0xff,
                write_mask,
                back: [comp, pass, 0, 0],
            }),
            ..Default::default()
        };
        // Equal with Keep, IncrSat, a zero write mask: the reference's kept bits all the same.
        for (state, reference, drawn) in [
            (declared(3, 0, 0xff), 1, 0xC1),
            (declared(8, 3, 0xff), 1, 0xC1),
            (declared(8, 2, 0), 8, 0xC8),
            (declared(8, 2, 0xff), 16, 0xC0),
            (declared(8, 2, 0xff), 32, 0xC0),
            // An idle block (Always, Keep: no stencil state) keeps its reference too.
            (crate::materials::RenderState::default(), 8, 0xC8),
        ] {
            let mut state = state;
            assert_eq!(apply_override(&mut state, reference, &engine), drawn);
            let s = state.stencil.expect("the engine's stencil");
            assert_eq!((s.comp, s.pass, s.write_mask, s.back[0]), (8, 2, 0xff, 8));
        }
    }

    /// A packed clear rounds to the channels' bits on a device whose clears round, on Vulkan at a
    /// texture's first level only, on Metal at every level (each color by its own packing);
    /// elsewhere it stays for the packing to truncate (tests/projects/samplerstates
    /// `Packed.*.clear`, `.mip.clear`,
    /// `.mrt.clear`, Vulkan and OpenGL Core Editors).
    #[test]
    fn packed_clears_round_at_the_levels_where_the_device_rounds() {
        use unbound_compat::GraphicsDevice;
        let vulkan = DeviceConventions::of(GraphicsDevice::Vulkan);
        let gl = DeviceConventions::of(GraphicsDevice::OpenGLCore);
        let color = [0.25, 0.5, 0.3, 0.7];
        // 0.5 of 31 rounds to 16 (132/255), of 63 to 32 (130/255); alpha 1 of 1555 rounds to 1.
        assert_eq!(
            packed_clear(vulkan, owned::PACKED_5551, 0, color),
            [66.0 / 255.0, 132.0 / 255.0, 74.0 / 255.0, 1.0]
        );
        assert_eq!(
            packed_clear(vulkan, owned::PACKED_565, 0, color)[1],
            130.0 / 255.0
        );
        assert_eq!(packed_clear(vulkan, owned::PACKED_565, 1, color), color);
        assert_eq!(packed_clear(gl, owned::PACKED_565, 0, color), color);
        assert_eq!(packed_clear(vulkan, 0, 0, color), color);
        // Metal rounds each channel to its nearest level, 4-4-4-4 included (0.7 at half
        // precision is 11 of 15), kept as that level's nearest byte (`metal.jsonl` Packed.*.clear).
        let metal = DeviceConventions::of(GraphicsDevice::Metal);
        let rounded = [68.0 / 255.0, 136.0 / 255.0, 85.0 / 255.0, 187.0 / 255.0];
        assert_eq!(packed_clear(metal, owned::PACKED_4444, 0, color), rounded);
        // A further level rounds there too (`Packed.*.mip.clear`), unlike Vulkan's: 0.25, 0.5,
        // 0.3 read 8/31, 16/31, 9/31 in a 1-5-5-5 target (66, 132, 74 as bytes).
        assert_eq!(packed_clear(metal, owned::PACKED_4444, 1, color), rounded);
        assert_eq!(
            packed_clear(metal, owned::PACKED_5551, 1, color),
            [66.0 / 255.0, 132.0 / 255.0, 74.0 / 255.0, 1.0]
        );
    }

    #[test]
    fn a_renderers_color_of_the_vertices_multiplies_them_as_bytes() {
        let color = [0.657, -0.1, 1.25, 0.5];
        assert_eq!(
            vertex_tint(commands::DrawTint::Vertices, color),
            [168.0 / 255.0, 0.0, 1.0, 128.0 / 255.0]
        );
        // 0.7 * 255 is 178.5 in single precision: rounded up.
        assert_eq!(
            vertex_tint(commands::DrawTint::Vertices, [0.7, f32::NAN, 0.0, 1.0]),
            [179.0 / 255.0, 0.0, 0.0, 1.0]
        );
        assert_eq!(vertex_tint(commands::DrawTint::None, color), color);
        assert_eq!(vertex_tint(commands::DrawTint::Constant, color), [1.0; 4]);
    }

    #[test]
    fn raw_copies_keep_borrowed_storage_boundaries() {
        use wgpu::TextureFormat as F;
        assert!(raw_copy_compatible(F::Bgra8Unorm, F::Bgra8Unorm, true));
        assert!(raw_copy_compatible(
            F::Bgra8UnormSrgb,
            F::Bgra8UnormSrgb,
            true
        ));
        assert!(raw_copy_compatible(F::Rgb9e5Ufloat, F::Rgb9e5Ufloat, true));
        assert!(!raw_copy_compatible(F::Bgra8Unorm, F::Rgba8Unorm, true));
        assert!(!raw_copy_compatible(F::Rgba8Unorm, F::Bgra8Unorm, true));
        assert!(!raw_copy_compatible(F::Bgra8Unorm, F::Bgra8UnormSrgb, true));
        assert!(!raw_copy_compatible(F::Bgra8UnormSrgb, F::Bgra8Unorm, true));
        assert!(!raw_copy_compatible(F::Rgb9e5Ufloat, F::Rgba32Float, true));
        assert!(raw_copy_compatible(F::Rgba8Unorm, F::Rgba8UnormSrgb, true));
        assert!(raw_copy_compatible(F::Rg16Unorm, F::Rg16Unorm, true));
        assert!(raw_copy_compatible(F::Bgra8Unorm, F::Bgra8UnormSrgb, false));
        assert!(raw_copy_compatible(F::Bgra8UnormSrgb, F::Bgra8Unorm, false));
    }

    /// A read of a screen no pass has started is transparent black over the part of the rect
    /// inside the screen, four bytes or floats per pixel.
    #[test]
    fn unstarted_screen_reads_transparent_black_within_the_screen() {
        let read = UnityRenderer::unstarted_screen((64, 32), [0, 0, 64, 32], false);
        assert_eq!(read.len(), 64 * 32 * 4);
        assert!(read.iter().all(|&b| b == 0));
        assert_eq!(
            UnityRenderer::unstarted_screen((64, 32), [60, 30, 10, 10], true).len(),
            4 * 2 * 16
        );
        assert!(UnityRenderer::unstarted_screen((64, 32), [70, 0, 4, 4], false).is_empty());
    }

    /// Copies between R8 storages repack their texels; others (RG8 with RGBA8) do not. The
    /// screen's blue-first storage and RGBA8 swap channels only where one end is that screen.
    #[test]
    fn channel_copies_join_r8_and_rgba8_storages() {
        use wgpu::TextureFormat as F;
        for screen in [false, true] {
            assert_eq!(
                channel_copy_kind(F::Rgba8Unorm, F::R8Unorm, screen),
                Some(CopyKind::ToR8)
            );
            assert_eq!(
                channel_copy_kind(F::R8Unorm, F::Rgba8Unorm, screen),
                Some(CopyKind::ToRgba8)
            );
            assert_eq!(
                channel_copy_kind(F::Rgba8UnormSrgb, F::R8Unorm, screen),
                None
            );
            assert_eq!(
                channel_copy_kind(F::R8Unorm, F::Rgba8UnormSrgb, screen),
                None
            );
            assert_eq!(channel_copy_kind(F::Rg8Unorm, F::Rgba8Unorm, screen), None);
            assert_eq!(channel_copy_kind(F::Rgba8Unorm, F::Rg8Unorm, screen), None);
            assert_eq!(channel_copy_kind(F::R8Unorm, F::Rg8Unorm, screen), None);
            assert_eq!(channel_copy_kind(F::Rgba16Float, F::R8Unorm, screen), None);
            assert_eq!(
                channel_copy_kind(F::Bgra8Unorm, F::R8Unorm, screen),
                screen.then_some(CopyKind::ToR8)
            );
            assert_eq!(
                channel_copy_kind(F::Bgra8UnormSrgb, F::Rgba8Unorm, screen),
                None
            );
        }
        assert_eq!(
            channel_copy_kind(F::Bgra8Unorm, F::Rgba8Unorm, true),
            Some(CopyKind::ToRgba8)
        );
        assert_eq!(
            channel_copy_kind(F::Rgba8Unorm, F::Bgra8Unorm, true),
            Some(CopyKind::ToBgra8)
        );
        assert_eq!(channel_copy_kind(F::Bgra8Unorm, F::Rgba8Unorm, false), None);
        assert_eq!(channel_copy_kind(F::Rgba8Unorm, F::Bgra8Unorm, false), None);
    }

    /// A copy between a B8G8R8A8 texture and a red-first one, where the device stores the first
    /// blue first (Metal), trades red and blue between RGBA8 storages, sRGB or not
    /// (tests/projects/cmdbuffers metal.jsonl `metal.copy.*`, `tarray.slices`); other storages
    /// keep their own rule.
    #[test]
    fn blue_first_copies_trade_red_and_blue_between_rgba8_storages() {
        use wgpu::TextureFormat as F;
        for (a, b) in [
            (F::Rgba8Unorm, F::Rgba8Unorm),
            (F::Rgba8UnormSrgb, F::Rgba8Unorm),
            (F::Rgba8Unorm, F::Rgba8UnormSrgb),
        ] {
            assert_eq!(swapped_copy_kind(a, b), Some(CopyKind::SwapRedBlue));
        }
        assert_eq!(swapped_copy_kind(F::Rgba8Unorm, F::R8Unorm), None);
        assert_eq!(swapped_copy_kind(F::Rgba16Float, F::Rgba8Unorm), None);
        assert_eq!(swapped_copy_kind(F::Bgra8Unorm, F::Rgba8Unorm), None);
    }

    /// Where `SV_VertexID` reads the index without the base vertex (Metal, tests/projects/
    /// cmdbuffers metal.jsonl `metal.vertexid.*`): a program without vertex inputs drops the
    /// base vertex; one with them binds its buffers from it, the same vertices; a negative base
    /// vertex or one past the buffers stays.
    #[test]
    fn direct_draws_bind_from_the_base_vertex_where_ids_leave_it_out() {
        assert_eq!(first_vertex_binding(0, true, 90), (0, 0));
        assert_eq!(first_vertex_binding(9, false, 90), (0, 0));
        assert_eq!(first_vertex_binding(-2, false, 90), (0, 0));
        assert_eq!(first_vertex_binding(9, true, 90), (0, 9));
        assert_eq!(first_vertex_binding(89, true, 90), (0, 89));
        assert_eq!(first_vertex_binding(90, true, 90), (90, 0));
        assert_eq!(first_vertex_binding(-2, true, 90), (-2, 0));
    }

    /// A grab of the blue-first screen and one of an RGBA8 target of the same size do not share
    /// a texture (copies between the two storages are invalid); alternating between them keeps
    /// both. A multisampled target's fetch copy has its own as well.
    #[test]
    fn grabs_keep_their_source_storage() {
        use wgpu::TextureFormat as F;
        let mut slots = GrabSlots::default();
        assert!(!slots.contains(handle::PLAIN_MAX));
        let rgba = slots.handle("_GrabTexture", F::Rgba8Unorm, 1);
        let bgra = slots.handle("_GrabTexture", F::Bgra8Unorm, 1);
        let samples = slots.handle("_GrabTexture", F::Rgba8Unorm, 4);
        assert!(rgba != bgra && samples != rgba && samples != bgra);
        for _ in 0..3 {
            assert_eq!(slots.handle("_GrabTexture", F::Rgba8UnormSrgb, 1), rgba);
            assert_eq!(slots.handle("_GrabTexture", F::Bgra8UnormSrgb, 1), bgra);
            assert_eq!(slots.handle("_GrabTexture", F::Rgba8UnormSrgb, 4), samples);
        }
        // Other storages share the red-first slot (`grab` remakes its texture: `same_storage`).
        assert_eq!(slots.handle("_GrabTexture", F::Rgba16Float, 1), rgba);
        assert_eq!(slots.handle("_GrabTexture", F::Rgba16Float, 2), samples);
        let named = slots.handle("_Named", F::Rgba8Unorm, 1);
        assert!(![rgba, bgra, samples].contains(&named));
        assert!(slots.contains(rgba) && slots.contains(bgra) && slots.contains(named));
        assert!(!slots.contains(named - 1) && !slots.contains(0));
        assert!(same_storage(F::Bgra8Unorm, F::Bgra8UnormSrgb));
        assert!(same_storage(F::Rgba8UnormSrgb, F::Rgba8Unorm));
        assert!(!same_storage(F::Bgra8Unorm, F::Rgba8Unorm));
        assert!(!same_storage(F::Rgba8Unorm, F::Bgra8UnormSrgb));
        assert!(!same_storage(F::Rgba16Float, F::Rgba8Unorm));
    }

    #[test]
    fn forced_anisotropy_spares_level_zero() {
        // Forced on with the default limits (9..16): 1..9 become 9, above 9 stay, 0 opts out.
        let forced = [2, 9, 16];
        assert_eq!(effective_aniso(forced, 0), 1);
        assert_eq!(effective_aniso(forced, 1), 9);
        assert_eq!(effective_aniso(forced, 12), 12);
        assert_eq!(effective_aniso([2, 9, 10], 16), 10);
        // Per texture: as set, up to the maximum; off: none.
        assert_eq!(effective_aniso([1, 9, 16], 4), 4);
        assert_eq!(effective_aniso([1, 9, 2], 4), 2);
        assert_eq!(effective_aniso([0, 9, 16], 16), 1);
    }

    fn stand_in(extra: bool) -> DrawInputs {
        DrawInputs {
            vertex_inputs: true,
            indirect: false,
            vertex_bytes: INDEXED_VERTICES * size_of::<resources::Vertex>() as u64,
            extra_bytes: extra
                .then(|| INDEXED_VERTICES * size_of::<resources::ExtraVertex>() as u64),
        }
    }

    /// A direct list draw of the 65536-vertex stand-in reads a buffer's 16-bit indices: every
    /// index value has its vertex, in both vertex buffers.
    #[test]
    fn the_stand_in_covers_every_16_bit_index() {
        let index = Some((12, true));
        assert_eq!(INDEXED_VERTICES, u64::from(u16::MAX) + 1);
        assert_eq!(
            external_index_refusal(index, (2, 0, 6), 0, &[stand_in(false)]),
            None
        );
        assert_eq!(
            external_index_refusal(index, (2, 3, 3), 0, &[stand_in(true)]),
            None
        );
        assert_eq!(
            external_index_refusal(index, (2, 0, 6), 0, &[stand_in(false), stand_in(true)]),
            None
        );
    }

    /// Anything else is refused: a vertex short in either buffer, an indirect draw, a missing
    /// buffer or one without index usage, a range past its end.
    #[test]
    fn uncovered_or_unsafe_index_draws_are_refused() {
        let index = Some((12, true));
        let mut short = stand_in(false);
        short.vertex_bytes -= size_of::<resources::Vertex>() as u64;
        let mut short_extra = stand_in(true);
        short_extra.extra_bytes = short_extra
            .extra_bytes
            .map(|e| e - size_of::<resources::ExtraVertex>() as u64);
        let mut indirect = stand_in(false);
        indirect.indirect = true;
        for draws in [
            vec![short],
            vec![short_extra],
            vec![stand_in(false), indirect],
        ] {
            assert!(external_index_refusal(index, (2, 0, 6), 0, &draws).is_some());
        }
        let covered = [stand_in(false)];
        assert!(external_index_refusal(None, (2, 0, 6), 0, &covered).is_some());
        assert!(external_index_refusal(Some((12, false)), (2, 0, 6), 0, &covered).is_some());
        assert!(external_index_refusal(index, (2, 4, 3), 0, &covered).is_some());
        assert!(external_index_refusal(index, (2, 0, 7), 0, &covered).is_some());
        // A base vertex can move a 16-bit index past the stand-in's vertices.
        assert!(external_index_refusal(index, (2, 0, 6), 1, &covered).is_some());
    }

    /// A strip's pipeline names the index format of what it reads: a buffer's 16-bit indices, or
    /// 32-bit ones (a mesh's, or a buffer's); lists name none.
    #[test]
    fn strip_pipelines_name_the_index_format_they_read() {
        use resources::Topology;
        assert_eq!(
            strip_index(Topology::LineStrip, true),
            Some(wgpu::IndexFormat::Uint16)
        );
        assert_eq!(
            strip_index(Topology::LineStrip, false),
            Some(wgpu::IndexFormat::Uint32)
        );
        for list in [Topology::Triangles, Topology::Lines, Topology::Points] {
            assert_eq!(strip_index(list, true), None);
            assert_eq!(strip_index(list, false), None);
        }
    }

    /// A draw whose program reads no vertex inputs binds no vertex buffer: it reads 32-bit
    /// indices, indirect counts and base vertices whatever its stand-in holds. 32-bit indices
    /// still need the buffer to hold them, and a program reading inputs refuses them.
    #[test]
    fn draws_without_vertex_inputs_read_any_indices() {
        let index = Some((24, true));
        let mut procedural = stand_in(false);
        procedural.vertex_inputs = false;
        procedural.vertex_bytes = 4;
        let mut indirect = DrawInputs { ..procedural };
        indirect.indirect = true;
        for draws in [vec![procedural], vec![indirect]] {
            for size in [2, 4] {
                for base_vertex in [0, 4, -2] {
                    assert_eq!(
                        external_index_refusal(index, (size, 0, 6), base_vertex, &draws),
                        None
                    );
                }
            }
        }
        let mut procedural = stand_in(false);
        procedural.vertex_inputs = false;
        assert!(external_index_refusal(index, (4, 1, 6), 0, &[procedural]).is_some());
        assert!(external_index_refusal(index, (4, 0, 6), 0, &[stand_in(false)]).is_some());
    }

    #[test]
    fn texture_and_buffer_group_lookups_match_owned_keys_and_disambiguate_defaults() {
        let mut tex_map: bevy::platform::collections::HashMap<TextureGroupKey, u32> =
            Default::default();
        let slots_white = vec![
            TextureSlotKey {
                raw: 42,
                generation: 7,
                default_tag: default_texture_tag("white"),
            },
            TextureSlotKey {
                raw: 0,
                generation: 0,
                default_tag: default_texture_tag("white"),
            },
        ];
        let slots_bump = vec![
            TextureSlotKey {
                raw: 42,
                generation: 7,
                default_tag: default_texture_tag("white"),
            },
            TextureSlotKey {
                raw: 0,
                generation: 0,
                default_tag: default_texture_tag("bump"),
            },
        ];
        tex_map.insert(
            TextureGroupKey {
                program: 3,
                space: 1,
                slots: slots_white.clone(),
            },
            100,
        );
        tex_map.insert(
            TextureGroupKey {
                program: 3,
                space: 1,
                slots: slots_bump.clone(),
            },
            200,
        );
        assert_eq!(
            tex_map.get(&TextureGroupLookup {
                program: 3,
                space: 1,
                slots: &slots_white,
            }),
            Some(&100)
        );
        assert_eq!(
            tex_map.get(&TextureGroupLookup {
                program: 3,
                space: 1,
                slots: &slots_bump,
            }),
            Some(&200)
        );
        assert_eq!(
            tex_map.get(&TextureGroupLookup {
                program: 3,
                space: 0,
                slots: &slots_white,
            }),
            None
        );
        // Different generation invalidates lookup even for the same raw handle.
        let mut stale = slots_white.clone();
        stale[0].generation = 8;
        assert_eq!(
            tex_map.get(&TextureGroupLookup {
                program: 3,
                space: 1,
                slots: &stale,
            }),
            None
        );

        let mut buf_map: bevy::platform::collections::HashMap<BufferGroupKey, u32> =
            Default::default();
        let buf_slots = vec![
            BufferSlotKey {
                handle: 11,
                counter: false,
            },
            BufferSlotKey {
                handle: 11,
                counter: true,
            },
        ];
        buf_map.insert(
            BufferGroupKey {
                program: 9,
                slots: buf_slots.clone(),
            },
            300,
        );
        assert_eq!(
            buf_map.get(&BufferGroupLookup {
                program: 9,
                slots: &buf_slots,
            }),
            Some(&300)
        );
        let mut non_counter = buf_slots.clone();
        non_counter[1].counter = false;
        assert_eq!(
            buf_map.get(&BufferGroupLookup {
                program: 9,
                slots: &non_counter,
            }),
            None
        );

        // Every built-in default texture name maps to a distinct non-zero tag (gray/grey share).
        let distinct = [
            "white",
            "black",
            "unbound-unassigned",
            "gray",
            "linearGray",
            "bump",
            "red",
        ];
        let mut tags = std::collections::HashSet::new();
        for name in distinct {
            let tag = default_texture_tag(name);
            assert_ne!(tag, 0);
            assert!(tags.insert(tag), "duplicate tag for {name}");
        }
        assert_eq!(default_texture_tag("gray"), default_texture_tag("grey"));
    }

    #[test]
    fn global_tex_slot_tracks_additions_and_in_place_updates() {
        let slot = GlobalTexSlot::default();
        let mut globals: Vec<(String, owned::PropertyValue)> = Vec::new();
        let mut index = owned::NameIndex::default();
        index.refresh(&globals);
        assert_eq!(slot.find_handle(&globals, &index, "_CameraDepthTexture"), 0);

        globals.push((
            "_Other".into(),
            owned::PropertyValue::Texture {
                handle: 11,
                st: [1.0, 1.0, 0.0, 0.0],
            },
        ));
        index.refresh(&globals);
        assert_eq!(slot.find_handle(&globals, &index, "_CameraDepthTexture"), 0);

        globals.push((
            "_CameraDepthTexture".into(),
            owned::PropertyValue::Texture {
                handle: 42,
                st: [1.0, 1.0, 0.0, 0.0],
            },
        ));
        index.refresh(&globals);
        assert_eq!(
            slot.find_handle(&globals, &index, "_CameraDepthTexture"),
            42
        );

        // In-place update is observed immediately via the cached position.
        globals[1].1 = owned::PropertyValue::Texture {
            handle: 99,
            st: [1.0, 1.0, 0.0, 0.0],
        };
        assert_eq!(
            slot.find_handle(&globals, &index, "_CameraDepthTexture"),
            99
        );

        // Overwriting with a non-texture returns 0.
        globals[1].1 = owned::PropertyValue::Float(1.0);
        assert_eq!(slot.find_handle(&globals, &index, "_CameraDepthTexture"), 0);
    }

    #[test]
    fn a_draw_property_of_a_texture_name_hides_the_material_texture_whatever_its_type() {
        let texture = |handle| owned::PropertyValue::Texture {
            handle,
            st: [1.0, 1.0, 0.0, 0.0],
        };
        let material = owned::Material {
            properties: vec![("_DetailTex".into(), texture(5))],
            ..Default::default()
        };
        assert_eq!(layered_texture(&[], &material, "_DetailTex"), 5);
        let float = [("_DetailTex".to_string(), owned::PropertyValue::Float(1.0))];
        assert_eq!(layered_texture(&float, &material, "_DetailTex"), 0);
        let last = [
            ("_DetailTex".to_string(), texture(7)),
            ("_DetailTex".to_string(), texture(9)),
        ];
        assert_eq!(layered_texture(&last, &material, "_DetailTex"), 9);
        assert_eq!(layered_texture(&float, &material, "_OtherTex"), 0);
    }

    #[test]
    fn scan_draw_properties_summarizes_last_wins_main_tex_and_flags() {
        let empty = scan_draw_properties(&[]);
        assert_eq!(empty.main_tex_override, None);
        assert!(!empty.has_texture_override);
        assert!(!empty.has_buffer_override);
        assert!(!empty.has_mesh_batch_limits);
        assert_eq!(empty.mesh_batch_limits, None);

        let props = vec![
            (
                "_MainTex".into(),
                owned::PropertyValue::Texture {
                    handle: 10,
                    st: [1.0, 1.0, 0.0, 0.0],
                },
            ),
            (
                "_MainTex".into(),
                owned::PropertyValue::Texture {
                    handle: 20,
                    st: [1.0, 1.0, 0.0, 0.0],
                },
            ),
            (
                "unity_DOTSInstanceData".into(),
                owned::PropertyValue::Buffer(7),
            ),
            (
                "<MeshInstanceBatchLimits>".into(),
                owned::PropertyValue::Vector([511.0, 1023.0, 16384.0, 0.0]),
            ),
        ];
        let s = scan_draw_properties(&props);
        assert_eq!(s.main_tex_override, Some(20));
        assert!(s.has_texture_override);
        assert!(s.has_buffer_override);
        assert!(s.has_mesh_batch_limits);
        assert_eq!(s.mesh_batch_limits, Some([511.0, 1023.0, 16384.0, 0.0]));

        // Trailing non-texture `_MainTex` masks an earlier texture override.
        let masked = vec![
            (
                "_MainTex".into(),
                owned::PropertyValue::Texture {
                    handle: 10,
                    st: [1.0, 1.0, 0.0, 0.0],
                },
            ),
            ("_MainTex".into(), owned::PropertyValue::Float(1.0)),
        ];
        let sm = scan_draw_properties(&masked);
        assert_eq!(sm.main_tex_override, Some(0));
        assert!(sm.has_texture_override);
    }
}
