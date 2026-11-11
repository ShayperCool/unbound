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
use crate::materials::{ResolvedMaterial, Resolver};
use bytemuck::{Pod, Zeroable};
use rustc_hash::{FxHashMap as HashMap, FxHashSet as HashSet};
use std::{
    ops::Range,
    sync::{Arc, Mutex},
    time::Instant,
};
use unbound_runtime::render_protocol::{
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

/// A compiled program's texture bind group and what it was built from (program, color space,
/// per slot the handle as bound, element bits included, and its texture's generation).
type ProgramTextures = (wgpu::BindGroup, (u32, i32, Vec<(u64, u64)>));

struct GpuMaterial {
    /// Shared: each draw holds it while it is planned (a copy per draw cost more than drawing).
    resolved: Arc<ResolvedMaterial>,
    /// Per pass: the textures of the program it last drew with.
    pass_textures: Vec<Option<ProgramTextures>>,
    /// The program variant (and whether it is the error shader's) per (pass, global keyword
    /// set, linear colour space, standalone mesh replay).
    variants: rustc_hash::FxHashMap<(usize, u32, bool, bool), (Option<u32>, bool)>,
    /// Its uniform block per program variant (see `CompiledPrograms::material_block`).
    blocks: rustc_hash::FxHashMap<u32, Arc<compiled::MaterialBlock>>,
}

/// The screen: 8-bit RGBA like the Editor's game view, sRGB-encoded in Linear color space
/// (draws encode, samples decode, readbacks and copies see the stored bytes: measured,
/// tests/projects/rtdepth `target.screen.*.final` and `ctl.*`).
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
    /// Its depth, as `CopyDepth` samples it.
    depth_sample: wgpu::TextureView,
}

impl ScreenTarget {
    fn view(&self, color_space: i32) -> (&wgpu::TextureView, wgpu::TextureFormat) {
        if color_space == 1 {
            (&self.srgb, wgpu::TextureFormat::Rgba8UnormSrgb)
        } else {
            (&self.color, SCREEN_FORMAT)
        }
    }
}

const SCREEN_FORMAT: wgpu::TextureFormat = wgpu::TextureFormat::Rgba8Unorm;
/// The global buffer that holds the random write target at a UAV register (its number follows;
/// the managed `Graphics.SetRandomWriteTarget` sets it).
const RANDOM_WRITE_TARGET: &str = "<RandomWriteTarget>";

/// The latest complete frame, for the window.
#[derive(Clone)]
pub struct Presented {
    pub color: wgpu::TextureView,
    pub color_space: i32,
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
    color: wgpu::TextureView,
    depth_slice: Option<u32>,
    /// A multisampled target's resolve: the texture's own view.
    resolve: Option<wgpu::TextureView>,
    samples: u32,
    depth: Option<wgpu::TextureView>,
    format: wgpu::TextureFormat,
    width: u32,
    height: u32,
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
}

/// A further color of a target.
#[derive(Clone)]
struct ExtraColor {
    color: wgpu::TextureView,
    depth_slice: Option<u32>,
    resolve: Option<wgpu::TextureView>,
    format: wgpu::TextureFormat,
    texture: Option<(wgpu::Texture, u32, u32)>,
    auto_mips: bool,
    defer_mips: Option<wgpu::TextureFormat>,
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
    material: wgpu::BindGroup,
    texture: wgpu::BindGroup,
    /// The program's buffers (group 3), when it reads any.
    buffers: Option<wgpu::BindGroup>,
    offset: u32,
    stencil_ref: u32,
    viewport: [f32; 4],
    scissor: [u32; 4],
    /// The compiled program: its id and the offset of the draw's uniform block.
    program: (u32, u32),
    /// Instances drawn (SV_InstanceID 0..n).
    instances: u32,
    first_instance: u32,
    /// Counts read on the GPU: the arguments buffer, its offset, whether indexed.
    indirect: Option<(wgpu::Buffer, u64, bool)>,
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
    /// Set when the textures keep their texels in different 8-bit storages.
    channels: Option<ChannelCopy>,
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
    /// Floats as unsigned integer bits.
    UintBits,
    /// Floats as signed integer bits.
    SintBits,
    /// 8-bit normalized texels into one channel and into four ([`ChannelCopy`]).
    ToR8,
    ToRgba8,
}

impl CopyKind {
    const COUNT: usize = 6;

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
    kind: CopyKind,
}

/// The [`ChannelCopy`] kind from `source` storage into `destination` storage: between R8 and
/// RGBA8.
fn channel_copy_kind(
    source: wgpu::TextureFormat,
    destination: wgpu::TextureFormat,
) -> Option<CopyKind> {
    use wgpu::TextureFormat as F;
    match (
        source.remove_srgb_suffix(),
        destination.remove_srgb_suffix(),
    ) {
        (F::Rgba8Unorm, F::R8Unorm) => Some(CopyKind::ToR8),
        (F::R8Unorm, F::Rgba8Unorm) => Some(CopyKind::ToRgba8),
        _ => None,
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
    Mips(wgpu::Texture, wgpu::TextureFormat, u32),
}

/// An indirect draw's arguments copied on the GPU, after the work before the draw, into its
/// slot of [`IndirectArgsPool`] with the start instance zeroed: the Editor's OpenGLCore
/// `SV_InstanceID` counts from 0 on every indirect path (GL's `gl_InstanceID` never includes the
/// base instance; measured: tests/projects/cmdbuffers `render.indexed.startinstance.*` and
/// `render.indexed.indirect.rawids`), while wgpu's instance index includes it. The caller's
/// buffer keeps its arguments for the shader's `unity_IndirectDrawArgs` reads
/// (UnityIndirect.cginc's `GetIndirectInstanceID_Base` adds the start instance itself).
struct ArgsCopy {
    source: wgpu::Buffer,
    source_offset: u64,
    destination: wgpu::Buffer,
    destination_offset: u64,
    indexed: bool,
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
    }
}

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
    source: wgpu::TextureView,
    /// The source's sample count (1: not multisampled).
    source_samples: u32,
    destination: wgpu::TextureView,
    samples: u32,
}

/// A render pass being built: consecutive commands on one target.
struct Pass {
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
    /// The global depth bias: units, slope factor (`CommandBuffer.SetGlobalDepthBias`).
    depth_bias: [f32; 2],
    gizmo_draw: bool,
    /// A renderer list's render state overrides (`RenderStateBlock`).
    state_override: Option<commands::StateOverride>,
    /// How the next draw runs (`DrawSetup`): instances, arguments buffer, offset, indexed.
    draw_setup: Option<(u32, u64, u32, bool)>,
    instance_multiplier: u32,
    /// The next draw is this instance of a mesh-instancing call (`MeshInstance`).
    mesh_instance: Option<u32>,
    /// The next draw's indices from a buffer (`DrawIndices`): buffer, index size, first, count.
    draw_indices: Option<(u64, u8, u32, u32)>,
}

pub struct UnityRenderer {
    /// The adapter the device was opened on (its formats as the backend stores them).
    adapter: wgpu::Adapter,
    /// Native plugins' shared textures and callbacks (`plugins`).
    plugins: plugins::PluginState,
    /// The graphics device's projection, depth and target orientation (resolved at startup).
    conventions: DeviceConventions,
    /// The Vulkan device's plugin interface (None: plugins see OpenGL Core's, `plugins`).
    #[cfg(target_os = "linux")]
    vulkan_plugins: Option<plugins_vulkan::VulkanPlugins>,
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
    resolve_history: HashMap<wgpu::Texture, msaa::ResolveHistory>,
    resolve_targets: msaa::ResolveTargets,
    readback: readback::Staging,
    framebuffer_dirty: bool,
    meshes: HashMap<u64, GpuMesh>,
    mesh_buffers: HashMap<(u64, u8), mesh_buffers::Binding>,
    /// Zeros standing in for channels 4..7 of meshes without them.
    zero_extra: Option<wgpu::Buffer>,
    textures: HashMap<u64, GpuTexture>,
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
    /// The pipelines (and their layouts) that copy a depth buffer into a float color texture:
    /// single-sample and multisampled.
    depth_copy_pipelines: [Option<(wgpu::RenderPipeline, wgpu::BindGroupLayout)>; CopyKind::COUNT],
    /// The `CopyDepth` pipelines, by (source multisampled, destination samples, per sample).
    depth_blit_pipelines: HashMap<(bool, u32, bool), (wgpu::RenderPipeline, wgpu::BindGroupLayout)>,
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
    depth_reads: Vec<(u64, bool)>,
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
    /// The pass writing a counter at a byte offset that is not a multiple of 4.
    unaligned_count: Option<(wgpu::ComputePipeline, wgpu::BindGroupLayout)>,
    generation: u64,
    draw_buffer: Option<wgpu::Buffer>,
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
    keywords: Vec<String>,
    /// An id of the global keyword set (equal sets, equal ids): what variants are cached by.
    keyword_set: u32,
    keyword_sets: rustc_hash::FxHashMap<Vec<String>, u32>,
    /// The `LightMode` tags of tagged-pass draws (renderer lists).
    pass_tags: Vec<String>,
    state: State,
    presented: Arc<Mutex<Option<Presented>>>,
    started: Instant,
    warned: HashSet<String>,
    traced_frames: u64,
    compiled: compiled::CompiledPrograms,
    /// `GrabPass` textures by name, and the named ones grabbed in this camera's render.
    grabs: HashMap<String, u64>,
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
        let pipelines = Pipelines::new(&device);
        let defaults = resources::default_textures(&device, &queue);
        UnityRenderer {
            plugins: Default::default(),
            #[cfg(target_os = "linux")]
            vulkan_plugins: (unbound_runtime::graphics_device::get()
                == unbound_compat::GraphicsDevice::Vulkan)
                .then(|| plugins_vulkan::VulkanPlugins::new(&device, &queue))
                .flatten(),
            frames_completed: Default::default(),
            plugin_pass_open: false,
            conventions,
            adapter,
            device,
            queue,
            resolver,
            pipelines,
            fence_issued: 0,
            fence_completed: Arc::new(std::sync::atomic::AtomicU64::new(0)),
            mipmaps: Default::default(),
            pending_mips: Default::default(),
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
            depth_copy_pipelines: Default::default(),
            depth_blit_pipelines: HashMap::default(),
            bit_views: HashMap::default(),
            depth_main_groups: HashMap::default(),
            pre_draw: Vec::new(),
            pre_copies: Vec::new(),
            feedback_copies: HashMap::default(),
            depth_reads: Vec::new(),
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
            unaligned_count: None,
            generation: 1,
            draw_buffer: None,
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
            keywords: Vec::new(),
            keyword_set: 0,
            keyword_sets: Default::default(),
            pass_tags: Vec::new(),
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
                depth_bias: [0.0; 2],
                gizmo_draw: false,
                state_override: None,
                draw_setup: None,
                instance_multiplier: 1,
                mesh_instance: None,
                draw_indices: None,
            },
            presented,
            started: Instant::now(),
            warned: HashSet::default(),
            traced_frames: 0,
            grabs: HashMap::default(),
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
        gpu: GpuTexture,
        previous: Option<(wgpu::Texture, SamplerKey)>,
        packet: &owned::Texture,
    ) {
        let device = self.device.clone();
        if let Some((previous, sampler)) = &previous {
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
        self.textures.insert(handle, gpu);
    }

    fn warn_once(&mut self, key: String, message: impl FnOnce() -> String) {
        if self.warned.insert(key) {
            tracing::warn!("{}", message());
        }
    }

    /// The buffer a draw's external indices are read from (`DrawIndices`, render protocols 8 and
    /// 10): `count` indices of `size` bytes from `first`, drawn with base vertex 0 (or the
    /// arguments' of an indirect draw). None, warned once, when the planned draws cannot read them
    /// safely ([`external_index_refusal`]): the draw is skipped.
    fn external_indices(
        &mut self,
        draw: &commands::Draw,
        handle: u64,
        size: u8,
        first: u32,
        count: u32,
        planned: &[Planned],
    ) -> Option<wgpu::Buffer> {
        let buffer = self
            .buffers
            .get(&handle)
            .map(|b| (b.buffer.clone(), b.size));
        let strip = self
            .meshes
            .get(&draw.mesh)
            .and_then(|m| m.submeshes.get(draw.submesh.max(0) as usize))
            .is_some_and(|s| s.topology == resources::Topology::LineStrip);
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
        if let Some(why) = external_index_refusal(source, size, first, count, strip, &draws) {
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
            Upload::UploadTexture(handle, texture) => {
                self.generation += 1;
                let old = self.textures.remove(&handle);
                let previous = old
                    .as_ref()
                    .map(|gpu| (gpu.texture.clone(), gpu.sampler_key));
                let gpu =
                    resources::upload_texture(&device, &self.queue, old, &texture, self.generation);
                self.install_texture(handle, gpu, previous, &texture);
            }
            Upload::ReleaseTexture(handle) => {
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
            }
            Upload::TextureMipBias(handle, bias) => {
                self.texture_mip_biases.insert(handle, bias);
                if let Some(texture) = self.textures.get_mut(&handle) {
                    self.generation += 1;
                    let bias = if bias == 0.0 { 0 } else { bias.to_bits() };
                    if texture.sampler_key.lod_bias_bits != bias {
                        self.compute_bindings.release_sampler(&texture.texture);
                        texture.sampler_key.lod_bias_bits = bias;
                    }
                    texture.generation = self.generation;
                    texture.bind_groups = [None, None];
                }
            }
            Upload::TextureSampler(handle, [filter, wrap_u, wrap_v, wrap_w, aniso]) => {
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
                let pass_textures = vec![None; resolved.passes.len() + resolved.all.len()];
                self.materials.insert(
                    handle,
                    GpuMaterial {
                        resolved: Arc::new(resolved),
                        pass_textures,
                        variants: Default::default(),
                        blocks: Default::default(),
                    },
                );
            }
            Upload::ReleaseMaterial(handle) => {
                self.materials.remove(&handle);
            }
        }
    }

    /// A texture's anisotropic level under the global setting.
    fn effective_aniso(&self, level: i32) -> i32 {
        effective_aniso(self.anisotropy, level)
    }

    /// A texture's mip bias (`Texture.mipMapBias`) as the bits of its sampler key: the
    /// sampler's own LOD bias, which keeps implicit and explicit gradients. Each device's
    /// backend has its Editor's anisotropic transition as it is (tests/projects/samplerbias,
    /// both devices).
    fn sampler_lod_bias(&self, handle: u64) -> u32 {
        let bias = self.texture_mip_biases.get(&handle).copied().unwrap_or(0.0);
        if bias == 0.0 { 0 } else { bias.to_bits() }
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

    /// A texture by handle, or the named default.
    /// Whether the current target draws into a texture (its color or a further color).
    fn drawn_into(&self, handle: u64) -> bool {
        let (Some(t), Some(target)) = (self.textures.get(&handle), &self.state.target) else {
            return false;
        };
        std::iter::once(&target.texture)
            .chain(target.extra.iter().map(|e| &e.texture))
            .any(|x| {
                x.as_ref()
                    .is_some_and(|(texture, ..)| *texture == t.texture)
            })
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
        let fits = self.feedback_copies.get(&handle).is_some_and(|c| {
            c.size() == source.size()
                && c.format() == source.format()
                && c.mip_level_count() == source.mip_level_count()
        });
        if !fits {
            let copy = self.device.create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound feedback copy"),
                size: source.size(),
                mip_level_count: source.mip_level_count(),
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: source.format(),
                usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST,
                view_formats: if srgb {
                    &[wgpu::TextureFormat::Rgba8UnormSrgb]
                } else {
                    &[]
                },
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
            format: (srgb && space == 1).then_some(wgpu::TextureFormat::Rgba8UnormSrgb),
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
        if depth_element {
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
            if !t.render_target {
                key.aniso = self.effective_aniso(key.aniso);
            }
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
        let mut key = {
            let t = self.texture_or_default(handle, default);
            if let Some(bg) = &t.bind_groups[slot] {
                return (bg.clone(), t.alpha_only);
            }
            t.sampler_key
        };
        // The legacy main-texture binding follows the same global policy as a named 2D
        // texture binding. Render targets keep their own anisotropic level.
        if !self.texture_or_default(handle, default).render_target {
            key.aniso = self.effective_aniso(key.aniso);
        }
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
    /// nothing set. Err (warned once) when a hidden counter's buffer is not set or has no counter:
    /// the pass is not drawn, as such a dispatch is not run.
    fn buffer_bind_group(
        &mut self,
        id: u32,
        shader: &str,
        overrides: &[(String, owned::PropertyValue)],
        material: &owned::Material,
        globals: &[(String, owned::PropertyValue)],
    ) -> Result<Option<wgpu::BindGroup>, ()> {
        if self.compiled.buffer_layout(id).is_none() {
            return Ok(None);
        }
        if self.empty_storage.is_none() {
            self.empty_storage = Some(self.device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound empty storage"),
                size: 256,
                usage: wgpu::BufferUsages::STORAGE,
                mapped_at_creation: false,
            }));
        }
        let program = self.compiled.program(id).clone();
        // The buffer bound to a slot: a random write target at its UAV register (a global the
        // managed Graphics.SetRandomWriteTarget sets; cleared to no buffer, 0) first, a released
        // one binding nothing; else one of its name.
        let handle_of = |b: &unbound_shaders::BufferSlot| {
            let target = b.register.and_then(|r| {
                let key = format!("{RANDOM_WRITE_TARGET}{r}");
                globals.iter().find_map(|(n, v)| match v {
                    owned::PropertyValue::Buffer(h) if *n == key && *h != 0 => Some(*h),
                    _ => None,
                })
            });
            target.or_else(|| {
                overrides
                    .iter()
                    .rev()
                    .chain(material.properties.iter())
                    .chain(globals.iter())
                    .find_map(|(n, v)| match v {
                        owned::PropertyValue::Buffer(h) if *n == b.name => Some(*h),
                        _ => None,
                    })
            })
        };
        let mut missing = Vec::new();
        let mut bound: Vec<wgpu::Buffer> = Vec::with_capacity(program.buffers.len());
        for b in &program.buffers {
            if !b.read_only {
                if let Some(buffer) = handle_of(b).and_then(|h| self.buffers.get_mut(&h)) {
                    buffer.revision = buffer.revision.wrapping_add(1);
                }
            }
            // glslang's hidden counter of an Append or Counter buffer (`<name>@count`) binds
            // that buffer's counter.
            let owner = b.name.strip_suffix(compute::COUNTER_SUFFIX);
            let buffer = match owner {
                Some(owner) => program
                    .buffers
                    .iter()
                    .find(|o| o.name == owner)
                    .and_then(&handle_of)
                    .and_then(|h| self.buffers.get_mut(&h))
                    .and_then(|g| {
                        g.counter_used |= g.counter.is_some();
                        g.counter.clone()
                    }),
                None => handle_of(b)
                    .and_then(|h| self.buffers.get(&h))
                    .map(|g| g.buffer.clone()),
            };
            if let (Some(owner), None) = (owner, &buffer) {
                self.warn_once(format!("drawcount{shader}{owner}"), || {
                    format!(
                        "shader `{shader}` uses the counter of buffer `{owner}`, which is not set or not an Append or Counter buffer: the draw is skipped"
                    )
                });
                return Err(());
            }
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
            self.warn_once(format!("drawbuf{shader}{name}"), || {
                format!(
                    "shader `{shader}` reads buffer `{name}`, which nothing set: it reads zeros"
                )
            });
        }
        Ok(Some(group))
    }

    fn program_bind_group(
        &mut self,
        handle: u64,
        pass: usize,
        id: u32,
        space: i32,
        globals: &[(String, owned::PropertyValue)],
        overrides: &[(String, owned::PropertyValue)],
        main_override: u64,
    ) -> Option<wgpu::BindGroup> {
        let program = self.compiled.program(id).clone();
        let texture_override = |name: &str| {
            overrides.iter().find_map(|(n, v)| match v {
                owned::PropertyValue::Texture { handle, .. } if n == name => Some(*handle),
                _ => None,
            })
        };
        // Per-draw textures make a bind group of their own, not cached on the material.
        let mut cached = !overrides
            .iter()
            .any(|(_, v)| matches!(v, owned::PropertyValue::Texture { .. }));
        let slots: Vec<(
            u64,
            String,
            unbound_shaders::TextureDim,
            unbound_shaders::TextureSlot,
        )> = {
            let resolved = &self.materials.get(&handle)?.resolved;
            program
                .textures
                .iter()
                .enumerate()
                .map(|(k, name)| {
                    let (mut texture, default) = resolved.texture(name);
                    if let Some(h) = texture_override(name) {
                        texture = h;
                    }
                    if texture == 0
                        && let Some((_, owned::PropertyValue::Texture { handle, .. })) =
                            globals.iter().find(|(n, _)| n == name)
                    {
                        texture = *handle;
                    }
                    let dim = program
                        .texture_dims
                        .get(k)
                        .copied()
                        .unwrap_or(unbound_shaders::TextureDim::D2);
                    // Typed 2D _MainTex slots use the program layout, but still receive a
                    // renderer's main texture (a sprite's, for example).
                    if name == "_MainTex"
                        && dim == unbound_shaders::TextureDim::D2
                        && main_override != 0
                    {
                        texture = main_override;
                    }
                    let slot = program.texture_slots.get(k).copied().unwrap_or_default();
                    (texture, default, dim, slot)
                })
                .collect()
        };
        let is_2d = |dim: unbound_shaders::TextureDim| dim == unbound_shaders::TextureDim::D2;
        // A handle may name the texture's depth element (render protocol 6): its view comes from
        // the depth copy, brought up to date for this draw even when the bind group is cached.
        let raw: Vec<u64> = slots.iter().map(|(h, ..)| *h).collect();
        let mut element_views: Vec<Option<wgpu::TextureView>> = Vec::with_capacity(slots.len());
        for (k, (h, _, dim, slot)) in slots.iter().enumerate() {
            let (base, depth) = handle::split(*h);
            element_views.push(if !depth {
                None
            } else if slot.sample == SampleKind::Float && is_2d(*dim) {
                self.depth_element_view(base)
            } else if slot.depth() && is_2d(*dim) {
                None
            } else {
                let name = program.textures[k].clone();
                self.warn_once(format!("depth-element-slot{id}{name}"), || {
                    format!("texture `{name}` samples a render texture's depth element, which its slot ({:?}, {dim:?}) cannot read; the slot reads its default", slot.sample)
                });
                None
            });
        }
        let mut slots: Vec<_> = slots
            .into_iter()
            .map(|(h, d, dim, slot)| (handle::split(h).0, d, dim, slot))
            .collect();
        for k in 0..slots.len() {
            let (h, slot) = (slots[k].0, slots[k].3);
            if !slot.integer() && self.unreadable_as_float(h, &program.textures[k]) {
                slots[k].0 = 0;
            }
        }
        // Integer slots read a float texture's values as their bits, within its size, and zeros
        // where nothing is bound (as Unity does on OpenGL Core: tests/projects/rtdepth
        // fmt.*.stencil.uint, fmt.*.globalStencil.uint). The bits are copied before every draw.
        let mut integer_views: Vec<Option<wgpu::TextureView>> = Vec::with_capacity(slots.len());
        for (k, (h, _, dim, slot)) in slots.iter().enumerate() {
            if !slot.integer() {
                integer_views.push(None);
                continue;
            }
            let name = &program.textures[k];
            let texture = self.textures.get(h);
            // An array slot with nothing bound reads Unity's default array texture's bits.
            if *dim == unbound_shaders::TextureDim::D2Array
                && texture.is_none()
                && !handle::split(raw[k]).1
            {
                let signed = slot.sample == SampleKind::Sint;
                integer_views.push(Some(self.integer_array_default(signed, space)));
                continue;
            }
            // Integer slots read 2D textures otherwise (their default is a 2D texture of zeros):
            // any other slot shape skips the draw, bound or not, as does a layered texture or a
            // depth element.
            if !is_2d(*dim)
                || handle::split(raw[k]).1
                || texture.is_some_and(|t| t.layers > 1 || t.volume)
            {
                let name = name.clone();
                let h = *h;
                self.warn_once(format!("integer-slot{id}{name}{h}"), || {
                    format!("integer texture `{name}` ({dim:?}) of a shader is bound to texture {h}, whose shape or element it cannot read: the draw is skipped")
                });
                return None;
            }
            let Some(t) = texture else {
                integer_views.push(None);
                continue;
            };
            let signed = slot.sample == SampleKind::Sint;
            // An integer texture is read as it is, by a slot of its signedness (a slot of the
            // other one reads zeros).
            let format = t.texture.format();
            if resources::is_integer(format) {
                let same = format.sample_type(None, None)
                    == Some(if signed {
                        wgpu::TextureSampleType::Sint
                    } else {
                        wgpu::TextureSampleType::Uint
                    });
                let view = (same && !self.drawn_into(*h)).then(|| t.view.clone());
                let view = match view {
                    Some(view) => Some(view),
                    None if same => self.feedback_view(*h, space),
                    None => {
                        let (name, h) = (name.clone(), *h);
                        self.warn_once(format!("int-sign{id}{name}{h}"), || {
                            format!("integer texture `{name}` reads the {format:?} render texture {h} of the other signedness: it reads zeros")
                        });
                        None
                    }
                };
                integer_views.push(view);
                continue;
            }
            let (width, height, source) = (t.width, t.height, self.sampled_texture_view(t, space));
            let fits = self
                .bit_views
                .get(&(*h, signed))
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
                self.bit_views.insert((*h, signed), (width, height, view));
            }
            let target = self.bit_views[&(*h, signed)].2.clone();
            self.pre_draw.push(DepthCopy {
                depth: source,
                target: target.clone(),
                kind: if signed {
                    CopyKind::SintBits
                } else {
                    CopyKind::UintBits
                },
            });
            integer_views.push(Some(target));
        }
        let generations: Vec<(u64, u64)> = slots
            .iter()
            .zip(&raw)
            .map(|((h, d, dim, _), raw)| {
                let generation = if is_2d(*dim) {
                    self.texture_or_default(*h, d).generation
                } else {
                    self.textures.get(h).map_or(0, |t| t.generation)
                };
                // The color and the depth element of one texture are different bindings.
                (*raw, generation)
            })
            .collect();
        let stamp = (id, space, generations);
        // A texture the target draws into is read through a copy made for this draw.
        if slots.iter().any(|(h, ..)| self.drawn_into(*h)) {
            cached = false;
        }
        if cached
            && let Some(Some((bg, built))) = self.materials[&handle].pass_textures.get(pass)
            && *built == stamp
        {
            return Some(bg.clone());
        }
        let mut views = Vec::new();
        let mut samplers = Vec::new();
        for (k, (h, d, dim, slot)) in slots.iter().enumerate() {
            let depth_element = handle::split(raw[k]).1;
            // A sampler is the texture's, or the state its name spells where the program could
            // not pair it with one texture (samples through inline samplers use their texture's
            // own sampler: `unbound_shaders`' samplers module); comparison samplers compare
            // less-or-equal (Unity's shadow maps, without reversed depth).
            let mut key = match slot.inline {
                Some(inline) => resources::SamplerKey::inline(inline),
                None if is_2d(*dim) => self.texture_or_default(*h, d).sampler_key,
                None => self
                    .textures
                    .get(h)
                    .map_or(resources::SamplerKey::LINEAR_CLAMP, |t| t.sampler_key),
            };
            // The global setting is the 2D texture assets' (render targets, shadow maps and
            // cubemaps keep their own level: measured, tests/projects/urp's reflection under
            // ForceEnable).
            let render_target = self.textures.get(h).is_some_and(|t| t.render_target);
            if slot.inline.is_none() && !slot.comparison && !render_target && is_2d(*dim) {
                key.aniso = self.effective_aniso(key.aniso);
            }
            if slot.comparison {
                key.compare = 4;
            }
            if slot.inline.is_none() {
                key.lod_bias_bits = self.sampler_lod_bias(*h);
            }

            if slot.integer() {
                // Integers are read unfiltered.
                samplers.push(self.sampler(resources::SamplerKey {
                    filter: 0,
                    wrap_u: 1,
                    wrap_v: 1,
                    wrap_w: 0,
                    aniso: 1,
                    compare: 0,
                    lod_bias_bits: 0,
                }));
                let view = match integer_views[k].take() {
                    Some(view) => view,
                    None => self.integer_default(slot.sample == SampleKind::Sint),
                };
                views.push(view);
            } else if let Some(view) = element_views[k].take() {
                samplers.push(self.sampler(key));
                views.push(view);
            } else if depth_element && !slot.depth() {
                // A depth element the slot or the texture cannot provide: the slot's default.
                samplers.push(self.sampler(key));
                views.push(match is_2d(*dim) {
                    true => self.texture_or_default(0, d).view_for(space).clone(),
                    false => self.dim_default(*dim, d),
                });
            } else if slot.depth() {
                samplers.push(self.sampler(key));
                // The texture's depth when its shape is the slot's, else a far-plane default.
                let cube_slot = *dim == unbound_shaders::TextureDim::Cube;
                let view = self
                    .textures
                    .get(h)
                    .filter(|t| t.cube == cube_slot)
                    .and_then(|t| t.depth_sample.clone())
                    .unwrap_or_else(|| self.depth_default(cube_slot));
                views.push(view);
            } else if is_2d(*dim) && self.drawn_into(*h) {
                samplers.push(self.sampler(key));
                let view = match self.feedback_view(*h, space) {
                    Some(view) => view,
                    None => self.texture_or_default(0, d).view_for(space).clone(),
                };
                views.push(view);
            } else if is_2d(*dim) {
                samplers.push(self.sampler(key));
                views.push(self.sampled_texture_view(self.texture_or_default(*h, d), space));
            } else if self.drawn_into(*h) {
                samplers.push(self.sampler(key));
                let (name, h) = (program.textures[k].clone(), *h);
                self.warn_once(format!("feedback-dim{id}{name}{h}"), || {
                    format!("texture `{name}` ({dim:?}) samples render texture {h} while drawing into it: it reads its default")
                });
                views.push(self.dim_default(*dim, d));
            } else {
                samplers.push(self.sampler(key));
                // A cubemap bound to a cube slot, a 3D texture to a 3D slot, a 2D texture (of
                // any layers) to an array slot; anything else reads the slot's default.
                use unbound_shaders::TextureDim;
                let bound = self.textures.get(h).and_then(|t| match dim {
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
                        let format = srgb.then_some(wgpu::TextureFormat::Rgba8UnormSrgb);
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
                    None => views.push(self.dim_default(*dim, d)),
                }
            }
        }
        let mut entries = Vec::new();
        for (k, (view, sampler)) in views.iter().zip(&samplers).enumerate() {
            entries.push(wgpu::BindGroupEntry {
                binding: 2 * k as u32,
                resource: wgpu::BindingResource::TextureView(view),
            });
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
        if cached
            && let Some(slot) = self
                .materials
                .get_mut(&handle)
                .unwrap()
                .pass_textures
                .get_mut(pass)
        {
            *slot = Some((bg.clone(), stamp));
        }
        Some(bg)
    }

    /// Queues the copy of the depth target's depth into its color copy, when passes since
    /// `mark` drew into it.
    fn copy_depth(&mut self, passes: &mut Vec<Op>, mark: usize) {
        let Some(handle) = self.depth_target else {
            return;
        };
        if passes.len() <= mark {
            return;
        }
        if let Some(copy) = self.depth_copy_op(handle) {
            passes.push(Op::DepthCopy(copy));
        }
    }

    /// The copy of a render target's depth into its float color copy.
    fn depth_copy_op(&self, handle: u64) -> Option<DepthCopy> {
        let t = self.textures.get(&handle)?;
        let target = t.depth_copy.clone()?;
        match (&t.depth_sample, &t.depth_msaa) {
            (Some(depth), _) => Some(DepthCopy {
                depth: depth.clone(),
                target,
                kind: CopyKind::Depth,
            }),
            (None, Some(depth)) => Some(DepthCopy {
                depth: depth.clone(),
                target,
                kind: CopyKind::DepthMultisampled,
            }),
            _ => None,
        }
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
            self.depth_reads.push((handle, current));
            return Some(view);
        }
        let (width, height) = (t.width, t.height);
        let view = resources::depth_copy_view(&self.device, width, height);
        self.generation += 1;
        let generation = self.generation;
        let t = self.textures.get_mut(&handle).unwrap();
        t.depth_copy = Some(view.clone());
        t.generation = generation;
        t.bind_groups = [None, None];
        if current && self.depth_target.is_none() {
            self.depth_target = Some(handle);
        }
        self.depth_reads.push((handle, true));
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

    /// The pipeline that writes a depth buffer's depth into a float color texture.
    fn depth_copy_pipeline(
        &mut self,
        kind: CopyKind,
    ) -> (wgpu::RenderPipeline, wgpu::BindGroupLayout) {
        if let Some(p) = &self.depth_copy_pipelines[kind.index()] {
            return p.clone();
        }
        let device = &self.device;
        let source = match kind {
            CopyKind::Depth => DEPTH_COPY_WGSL.to_string(),
            CopyKind::DepthMultisampled => DEPTH_COPY_WGSL
                .replace("texture_2d<f32>", "texture_multisampled_2d<f32>")
                .replace(
                    "vec2<i32>(p.xy), 0)",
                    &format!(
                        "vec2<i32>(p.xy), {})",
                        self.conventions.depth_resolve_sample
                    ),
                ),
            CopyKind::UintBits => BITS_COPY_WGSL.replace("SCALAR", "u32"),
            CopyKind::SintBits => BITS_COPY_WGSL.replace("SCALAR", "i32"),
            CopyKind::ToR8 | CopyKind::ToRgba8 => CHANNEL_COPY_WGSL.into(),
        };
        let (sample_type, target_format) = match kind {
            CopyKind::Depth | CopyKind::DepthMultisampled => (
                wgpu::TextureSampleType::Float { filterable: false },
                resources::depth_copy_format(device),
            ),
            CopyKind::UintBits => (
                wgpu::TextureSampleType::Float { filterable: false },
                wgpu::TextureFormat::Rgba32Uint,
            ),
            CopyKind::SintBits => (
                wgpu::TextureSampleType::Float { filterable: false },
                wgpu::TextureFormat::Rgba32Sint,
            ),
            CopyKind::ToR8 => (
                wgpu::TextureSampleType::Float { filterable: false },
                wgpu::TextureFormat::R8Unorm,
            ),
            CopyKind::ToRgba8 => (
                wgpu::TextureSampleType::Float { filterable: false },
                wgpu::TextureFormat::Rgba8Unorm,
            ),
        };
        let multisampled = kind == CopyKind::DepthMultisampled;
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("unbound depth copy"),
            source: wgpu::ShaderSource::Wgsl(source.into()),
        });
        let layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound depth copy"),
            entries: &[wgpu::BindGroupLayoutEntry {
                binding: 0,
                visibility: wgpu::ShaderStages::FRAGMENT,
                ty: wgpu::BindingType::Texture {
                    sample_type,
                    view_dimension: wgpu::TextureViewDimension::D2,
                    multisampled,
                },
                count: None,
            }],
        });
        let pipeline_layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("unbound depth copy"),
            bind_group_layouts: &[Some(&layout)],
            immediate_size: 0,
        });
        let pipeline = device.create_render_pipeline(&wgpu::RenderPipelineDescriptor {
            label: Some("unbound depth copy"),
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
        });
        self.depth_copy_pipelines[kind.index()] = Some((pipeline, layout));
        self.depth_copy_pipelines[kind.index()].clone().unwrap()
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
            Some(self.ensure_screen(self.screen_slot).depth.clone())
        } else {
            self.textures
                .get(&destination)
                .and_then(|t| t.depth.clone())
        };
        let to = end(self, destination);
        match (from, to, to_view) {
            (
                Some((source, source_samples, size)),
                Some((_, samples, to_size)),
                Some(destination),
            ) if size == to_size => Some(DepthBlit {
                source,
                source_samples,
                destination,
                samples,
            }),
            _ => {
                self.warn_once(format!("copy-depth{source}/{destination}"), || {
                    format!("CopyDepth from {source} to {destination}: both need a depth buffer of one size; skipped")
                });
                None
            }
        }
    }

    /// The pipeline that draws a depth buffer into another (`CopyDepth`).
    fn depth_blit_pipeline(
        &mut self,
        multisampled: bool,
        samples: u32,
        per_sample: bool,
    ) -> (wgpu::RenderPipeline, wgpu::BindGroupLayout) {
        let key = (multisampled, samples, per_sample);
        if let Some(p) = self.depth_blit_pipelines.get(&key) {
            return p.clone();
        }
        let source = if per_sample {
            DEPTH_BLIT_PER_SAMPLE_WGSL.to_string()
        } else if multisampled {
            DEPTH_BLIT_WGSL.replace("texture_2d<f32>", "texture_multisampled_2d<f32>")
        } else {
            DEPTH_BLIT_WGSL.to_string()
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
                format: DEPTH_FORMAT,
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
            let texture = self.device.create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound screen"),
                size,
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: SCREEN_FORMAT,
                usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                    | wgpu::TextureUsages::TEXTURE_BINDING
                    | wgpu::TextureUsages::COPY_SRC
                    | wgpu::TextureUsages::COPY_DST,
                view_formats: &[wgpu::TextureFormat::Rgba8UnormSrgb],
            });
            let color = texture.create_view(&Default::default());
            let srgb = texture.create_view(&wgpu::TextureViewDescriptor {
                format: Some(wgpu::TextureFormat::Rgba8UnormSrgb),
                ..Default::default()
            });
            // The other slots' depth when it has this size: the screen keeps its depth.
            let shared = self
                .screens
                .iter()
                .flatten()
                .find(|s| s.width == width && s.height == height)
                .map(|s| (s.depth.clone(), s.depth_sample.clone()));
            let (depth, depth_sample) = shared.unwrap_or_else(|| {
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
                (texture.create_view(&Default::default()), sample)
            });
            self.screens[slot] = Some(ScreenTarget {
                width,
                height,
                texture,
                color,
                srgb,
                depth,
                depth_sample,
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
            color: color.clone(),
            depth_slice: None,
            resolve: None,
            samples: 1,
            depth: Some(s.depth.clone()),
            format,
            width: s.width,
            height: s.height,
            screen: true,
            screen_like: true,
            extra: Vec::new(),
            texture: Some((s.texture.clone(), 0, 0)),
            auto_mips: false,
            defer_mips: None,
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
        let mut target = if color == 0 {
            self.screen_target(space)
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
            let generate_mips = t.auto_generate_mips
                && mip == 0
                && t.mip_count > 1
                && !resources::is_integer(t.texture.format());
            Target {
                color: color_view,
                depth_slice: t.volume.then_some(layer),
                resolve,
                samples,
                depth: depth_view,
                format: t.target_format(space),
                width: (t.width >> mip).max(1),
                height: (t.height >> mip).max(1),
                screen: false,
                screen_like,
                extra: Vec::new(),
                texture: Some((t.texture.clone(), layer, mip)),
                auto_mips: generate_mips && !t.defer_mips,
                defer_mips: (generate_mips && t.defer_mips)
                    .then_some(t.target_format(self.color_space)),
            }
        };
        if depth == commands::NO_TEXTURE {
            target.depth = None;
        } else if depth != 0 && depth != color {
            target.depth = self.textures.get(&depth).and_then(|d| d.depth.clone());
        }
        Some(target)
    }

    /// Whether a target command keeps the same framebuffer attachments.
    fn same_target(&self, color: u64, depth: u64, mip: i32, slice: i32, extra: &[u64]) -> bool {
        self.target_handles == (color, depth)
            && self.state.target.as_ref().is_some_and(|target| {
                target.texture.as_ref().is_some_and(|(_, layer, level)| {
                    *layer == slice.max(0) as u32 && *level == mip.max(0) as u32
                }) && target.extra.len() == extra.len()
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

    fn prepare_framebuffer(&mut self, operations: &mut Vec<Op>) {
        if self.framebuffer_dirty {
            self.process_mips(operations);
            self.defer_target_mips();
            self.framebuffer_dirty = false;
        }
    }

    fn new_pass(&mut self, target: Target) -> Pass {
        let mut pass = Pass {
            target,
            seed: None,
            color_load: wgpu::LoadOp::Load,
            depth_clear: None,
            items: Vec::new(),
            args: Vec::new(),
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
        self.uniforms.clear();
        self.compute_bindings.begin();
        self.compiled.begin_frame();
        let time = self.started.elapsed().as_secs_f32();
        let device = self.device.clone();
        let mut passes: Vec<Op> = Vec::new();
        // Whether the last pass is still open for the current target.
        let mut open = false;
        let mut end_frame = false;
        let mut draws = 0usize;
        // Where the passes into the depth-only target being drawn start (this submission's).
        let mut depth_mark = 0usize;
        for command in commands {
            if !matches!(command, Command::Plugin(_)) {
                self.plugin_pass_open = false;
            }
            match command {
                Command::BeginFrame {
                    screen_width,
                    screen_height,
                    color_space,
                } => {
                    self.screen_size = (screen_width.max(1) as u32, screen_height.max(1) as u32);
                    self.color_space = color_space.clamp(0, 1);
                    self.screen_slot = (self.screen_slot + 1) % self.screens.len();
                    self.screen_touched = false;
                    self.frame_number += 1;
                    self.readback.frame(self.frame_number);
                    self.compute_bindings.frame(self.frame_number);
                    self.state.target = None;
                    self.target_handles = (0, 0);
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
                        if let Some(copies) = self.resolve_msaa(texture) {
                            passes.extend(copies.map(Op::Copy));
                        }
                    } else if let Some(copy) = self.plan_copy(
                        texture,
                        [0, 0],
                        [0, 0, -1, -1],
                        destination,
                        [0, 0],
                        [0, 0],
                        false,
                    ) {
                        passes.push(Op::Copy(copy));
                    }
                    open = false;
                }
                Command::GenerateMips { texture } => {
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
                    self.encode(std::mem::take(&mut passes));
                    depth_mark = 0;
                    // Whether the current target's pass was drawing: a plugin event that does not
                    // care where it runs runs inside it on the Vulkan device.
                    self.plugin_pass_open |= open;
                    self.run_plugin(&call);
                    open = false;
                }
                Command::EndFrame => {
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
                    self.copy_depth(&mut passes, depth_mark);
                    // The texture whose depth buffer is attached (the color's own, or another's:
                    // a G-buffer's) is copied where shaders sample it when it has a depth copy.
                    self.depth_target = depth_owner(color, depth)
                        .filter(|h| self.textures.get(h).is_some_and(|t| t.depth_copy.is_some()));
                    depth_mark = passes.len();
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
                            let Some(t) = self.resolve_target(*e, commands::NO_TEXTURE, mip, slice)
                            else {
                                continue;
                            };
                            if t.width != width || t.height != height || t.samples != samples {
                                self.warn_once(format!("mrt-size{e}"), || {
                                    format!("render target {e} differs in size or samples from the first color: not bound")
                                });
                                continue;
                            }
                            further.push(ExtraColor {
                                color: t.color,
                                depth_slice: t.depth_slice,
                                resolve: t.resolve,
                                format: t.format,
                                texture: t.texture,
                                auto_mips: t.auto_mips,
                                defer_mips: t.defer_mips,
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
                Command::MeshInstance(index) => self.state.mesh_instance = Some(index),
                Command::InstanceMultiplier(multiplier) => {
                    self.state.instance_multiplier = multiplier.max(1)
                }
                Command::ScreenStandIn(texture) => self.screen_stand_in = texture,
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
                    self.state.gizmo_draw = flags & commands::camera::GIZMO != 0;
                    if flags & commands::camera::BEGIN != 0 {
                        self.grabbed.clear();
                    }
                }
                Command::Global(name, value) => {
                    let globals = Arc::make_mut(&mut self.globals);
                    match self.globals_index.position(globals, &name) {
                        Some(i) => globals[i].1 = value,
                        None => globals.push((name, value)),
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
                            let pass_textures =
                                vec![None; resolved.passes.len() + resolved.all.len()];
                            self.materials.insert(
                                handle,
                                GpuMaterial {
                                    resolved: Arc::new(resolved),
                                    pass_textures,
                                    variants: Default::default(),
                                    blocks: Default::default(),
                                },
                            );
                        }
                    }
                }
                Command::PassTags(tags) => self.pass_tags = tags,
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
                        // The materials' bind groups hold samplers of the old levels.
                        for material in self.materials.values_mut() {
                            material.pass_textures.iter_mut().for_each(|p| *p = None);
                        }
                        for texture in self.textures.values_mut().chain(self.defaults.values_mut())
                        {
                            texture.bind_groups = [None, None];
                        }
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
                    let clear_color = flags & commands::clear::COLOR != 0;
                    if clear_color {
                        self.mark_msaa_written(&target);
                    }
                    let color = resources::unorm8_clear(target.format, color);
                    let clear_depth =
                        flags & (commands::clear::DEPTH | commands::clear::STENCIL) != 0;
                    let viewport = self.state.viewport;
                    let full = viewport[0] <= 0.0
                        && viewport[1] <= 0.0
                        && viewport[0] + viewport[2] >= target.width as f32
                        && viewport[1] + viewport[3] >= target.height as f32;
                    // Integer targets are cleared by a pass's load (the clear pipeline writes
                    // floats): a whole-target clear after draws starts a pass of its own.
                    let integer = resources::is_integer(target.format);
                    if full
                        && integer
                        && (clear_color || clear_depth)
                        && !last_pass(&mut passes).items.is_empty()
                    {
                        let pass = self.new_pass(target.clone());
                        passes.push(Op::Pass(pass));
                    }
                    let pass = last_pass(&mut passes);
                    if full && pass.items.is_empty() {
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
                    } else if integer && (clear_color || clear_depth) {
                        self.warn_once(format!("int-clear{:?}", target.format), || {
                            format!("a clear of part of an integer target ({:?}, viewport {viewport:?}) is not supported: its color and depth are left as they are", target.format)
                        });
                    } else if clear_color || clear_depth {
                        let pipeline = self.pipelines.clear(
                            &device,
                            ClearKey {
                                format: target.format,
                                extra: target.extra_formats(),
                                depth: target.depth.is_some(),
                                samples: target.samples,
                                color: clear_color,
                                clear_depth,
                            },
                        );
                        // The clear draws at the far depth of the device.
                        let offset = self.push_uniform(DrawUniform {
                            color,
                            misc: [self.conventions.depth(1.0), 0.0, 0.0, 0.0],
                            ..Zeroable::zeroed()
                        });
                        last_pass(&mut passes)
                            .items
                            .push(PassItem::Clear(pipeline, offset, viewport));
                    }
                }
                Command::Draw(draw) => {
                    if self.sync_mesh_buffers(draw.mesh, &mut passes) {
                        open = false;
                    }
                    let mesh_instance = self.state.mesh_instance.take();
                    let first_instance = mesh_instance.unwrap_or(0);
                    // Taken first: a draw's indices never pass to a later draw.
                    let indices = self.state.draw_indices.take();
                    let Some(target) = self.state.target.clone() else {
                        self.warn_once("draw-no-target".into(), || {
                            "a draw came before any render target was set: skipped".into()
                        });
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
                    let mut planned = self.plan_draw(&draw, &target, time, mesh_instance);
                    // Depth elements the draw samples whose copies lag (made just now, or the
                    // current target's): copied here, between what was drawn and this draw.
                    let reads = std::mem::take(&mut self.depth_reads);
                    let mut copied = Vec::new();
                    for (texture, now) in reads {
                        if !now || copied.contains(&texture) {
                            continue;
                        }
                        if let Some(copy) = self.depth_copy_op(texture) {
                            open = false;
                            passes.push(Op::DepthCopy(copy));
                            copied.push(texture);
                        }
                    }
                    if let Some((instances, args, offset, indexed)) = self.state.draw_setup.take() {
                        let mut buffer = (args != 0)
                            .then(|| self.buffers.get(&args).map(|b| b.buffer.clone()))
                            .flatten();
                        if args != 0 && buffer.is_none() {
                            self.warn_once(format!("args{args}"), || {
                                format!("an indirect draw's arguments buffer {args} does not exist: skipped")
                            });
                            continue;
                        }
                        let mut draw_offset = u64::from(offset);
                        if let Some(source) = buffer.take() {
                            let size = if indexed { INDIRECT_ARGS_SLOT } else { 16 };
                            if draw_offset % 4 != 0 || draw_offset + size > source.size() {
                                self.warn_once(format!("args-range{args}-{offset}"), || {
                                    format!("an indirect draw's arguments at byte {offset} of buffer {args} are not 4-byte aligned or lie past its end: skipped")
                                });
                                continue;
                            }
                            let (destination, at) = self.indirect_args.slot(&self.device);
                            let copy = ArgsCopy {
                                source,
                                source_offset: draw_offset,
                                destination: destination.clone(),
                                destination_offset: at,
                                indexed,
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
                        let Some(index) =
                            self.external_indices(&draw, handle, size, first, count, &planned)
                        else {
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
                                p.base_vertex = 0;
                            }
                        }
                    }
                    for item in planned {
                        match item {
                            Planned::Draw(mut d) => {
                                d.first_instance = first_instance;
                                if d.indirect.is_none() {
                                    d.instances =
                                        d.instances.wrapping_mul(self.state.instance_multiplier);
                                    d.first_instance = d
                                        .first_instance
                                        .wrapping_mul(self.state.instance_multiplier);
                                }
                                if let Some(owned::PropertyValue::Vector(limits)) = draw
                                    .properties
                                    .iter()
                                    .find(|(name, _)| name == "<MeshInstanceBatchLimits>")
                                    .map(|(_, value)| value)
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
                                    d.first_instance %= capacity;
                                }
                                if !open {
                                    let pass = self.new_pass(target.clone());
                                    passes.push(Op::Pass(pass));
                                    open = true;
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
                    open = false;
                    if let Some(op) = self.plan_dispatch(&d) {
                        passes.push(op);
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
                    if (source == 0 || destination == 0) && !self.screen_touched {
                        // The frame's screen starts at its first use, a copy too: the slot still
                        // holds an older frame's image.
                        let target = self.screen_target(self.color_space);
                        let pass = self.new_pass(target);
                        passes.push(Op::Pass(pass));
                    }
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
                    depth_mark = passes.len();
                    if (source == 0 || destination == 0) && !self.screen_touched {
                        let target = self.screen_target(self.color_space);
                        let pass = self.new_pass(target);
                        passes.push(Op::Pass(pass));
                    }
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
        self.encode(passes);
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
        let id = self.compiled.variant(
            &device,
            shader,
            source,
            keywords,
            linear,
            layout,
            compiled::VariantUse::Draw,
        );
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
                compiled::VariantUse::Draw,
            ),
            true,
        )
    }

    /// The pipelines, bind groups and uniforms of a draw: one per pass it draws.
    fn plan_draw(
        &mut self,
        draw: &commands::Draw,
        target: &Target,
        time: f32,
        mesh_instance: Option<u32>,
    ) -> Vec<Planned> {
        let mut planned = Vec::new();
        let device = self.device.clone();
        let space = self.color_space;
        let (tw, th) = (target.width, target.height);
        let Some(material) = self.materials.get(&draw.material) else {
            self.warn_once(format!("mat{}", draw.material), || {
                format!("a draw uses unknown material {}; skipped", draw.material)
            });
            return planned;
        };
        let resolved = material.resolved.clone();
        let Some(mesh) = self.meshes.get(&draw.mesh) else {
            self.warn_once(format!("mesh{}", draw.mesh), || {
                format!("a draw uses unknown mesh {}; skipped", draw.mesh)
            });
            return planned;
        };
        let Some(sub) = mesh.submeshes.get(draw.submesh.max(0) as usize).cloned() else {
            self.warn_once(format!("sub{}/{}", draw.mesh, draw.submesh), || {
                format!(
                    "a draw uses missing submesh {} of mesh {}; skipped",
                    draw.submesh, draw.mesh
                )
            });
            return planned;
        };
        if sub.count == 0 {
            return planned;
        }
        let (vertex, index) = (mesh.vertex.clone(), mesh.index.clone());
        let mesh_extra = mesh.extra.clone();
        let extra_size = vertex.size() / size_of::<resources::Vertex>() as u64
            * size_of::<resources::ExtraVertex>() as u64;
        let viewport = clamp_viewport(self.state.viewport, tw, th);
        if viewport[2] <= 0.0 || viewport[3] <= 0.0 {
            return planned;
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
            return planned;
        }
        let (view, projection) = (self.state.view, self.state.projection);
        // The projection as the device draws with it into this target (Unity's GPU projection:
        // flipped into render textures where UV starts at the top, depth in its range).
        let flipped = self.conventions.flips(target.screen_like);
        let gpu_projection = self.conventions.gpu_projection(&projection, flipped);
        // Rows stored top-down turn the winding (as a mirrored transform does).
        let top_down = self.conventions.top_down(target.screen_like);
        // The passes drawn: the forward ones (a renderer's), every one, or one by index.
        let forward = resolved.passes.len();
        let passes: Vec<(usize, &crate::materials::ResolvedPass)> = match draw.pass {
            -1 => resolved.passes.iter().enumerate().collect(),
            // A renderer list's: per tag in order, the first pass with that LightMode.
            -3 => {
                let mut chosen = Vec::new();
                let tags = self.pass_tags.clone();
                for tag in &tags {
                    let Some(i) = resolved.lab.tagged_pass(tag) else {
                        continue;
                    };
                    if chosen.iter().any(|(c, _)| *c == forward + i) {
                        continue;
                    }
                    match &resolved.all[i] {
                        Ok(p) => chosen.push((forward + i, p)),
                        Err(reason) => {
                            let reason = reason.clone();
                            self.warn_once(format!("{}#{i}", resolved.shader), || {
                                format!(
                                    "shader `{}`: pass {i} is not drawn: {reason}",
                                    resolved.shader
                                )
                            });
                        }
                    }
                }
                chosen
            }
            -2 => resolved
                .all
                .iter()
                .enumerate()
                .filter_map(|(i, p)| p.as_ref().ok().map(|p| (forward + i, p)))
                .collect(),
            n => match resolved.all.get(n as usize) {
                Some(Ok(p)) => vec![(forward + n as usize, p)],
                Some(Err(reason)) => {
                    let reason = reason.clone();
                    self.warn_once(format!("{}#{n}", resolved.shader), || {
                        format!(
                            "shader `{}`: pass {n} is not drawn: {reason}",
                            resolved.shader
                        )
                    });
                    return planned;
                }
                None => {
                    self.warn_once(format!("{}#{n}?", resolved.shader), || {
                        format!(
                            "shader `{}` has no pass {n} ({} passes): nothing drawn",
                            resolved.shader,
                            resolved.all.len()
                        )
                    });
                    return planned;
                }
            },
        };
        // The material's properties under the draw's own (a MaterialPropertyBlock): looked up in
        // layers, not merged (a copy per draw cost more than drawing).
        let material_props: &owned::Material = &resolved.material;
        let overrides: &[(String, owned::PropertyValue)] = &draw.properties;
        let layered = |name: &str| {
            overrides
                .iter()
                .rev()
                .find(|(n, _)| n == name)
                .map(|(_, v)| v)
                .or_else(|| material_props.get(name))
        };
        let texture_of = |name: &str| match layered(name) {
            Some(owned::PropertyValue::Texture { handle, .. }) => *handle,
            _ => 0,
        };
        // `_MainTex`: the renderer's (a sprite's texture), else the material's.
        let (_, main_default) = resolved.texture("_MainTex");
        let main_handle = if draw.main_texture != 0 {
            draw.main_texture
        } else {
            texture_of("_MainTex")
        };
        let (texture_bg, alpha_only) = self.main_bind_group(main_handle, &main_default, space);
        let screen = [
            tw as f32,
            th as f32,
            1.0 + 1.0 / tw as f32,
            1.0 + 1.0 / th as f32,
        ];
        let offset = self.push_uniform(DrawUniform {
            model: draw.model,
            view,
            proj: gpu_projection,
            color: draw.color,
            screen,
            misc: [time, space as f32, if alpha_only { 1.0 } else { 0.0 }, 0.0],
            pad: [0.0; 4],
        });
        let linear = space == 1;
        let mesh_replay = mesh_instance.is_some()
            || overrides
                .iter()
                .any(|(name, _)| name == "<MeshInstanceBatchLimits>");
        let mut globals = self.globals.clone();
        self.globals_index.refresh(&globals);
        for (pass_index, pass) in passes {
            if let Some(name) = &pass.grab {
                if let Some(copy) = self.grab(name, target) {
                    planned.push(Planned::Grab(copy));
                }
                globals = self.globals.clone();
                self.globals_index.refresh(&globals);
                continue;
            }
            let layout = self.pipelines.texture_layout.clone();
            // The pass's variant for the material's and the global keywords, chosen once per
            // keyword set (the material's cache goes with it when it changes).
            let key = (pass_index, self.keyword_set, linear, mesh_replay);
            let cached = self
                .materials
                .get(&draw.material)
                .and_then(|m| m.variants.get(&key).copied());
            let (id, errored) = match cached {
                Some(v) => v,
                None => {
                    let mut keywords = resolved.material.keywords.clone();
                    keywords.extend(self.keywords.iter().cloned());
                    if mesh_replay {
                        keywords.push("INSTANCING_ON".into());
                        keywords.push(unbound_shaders::MESH_INSTANCE_REPLAY.into());
                    }
                    let (id, errored) = self.variant_or_error(
                        &resolved.shader,
                        &pass.program,
                        &keywords,
                        linear,
                        &layout,
                    );
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
            (state, stencil_ref) = self.draw_state(
                state,
                stencil_ref,
                (determinant3(&draw.model) < 0.0) != top_down,
            );
            let key = PipelineKey {
                state,
                topology: sub.topology,
                format: target.format,
                depth: target.depth.is_some(),
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
            let Some(material_bg) = self.program_bind_group(
                draw.material,
                pass_index,
                id,
                space,
                &globals,
                &draw.properties,
                draw.main_texture,
            ) else {
                self.pre_draw.clear();
                continue;
            };
            // The copies of textures it draws into and the conversions this pass reads run right
            // before it, after the grabs before it.
            for copy in self.pre_copies.drain(..) {
                planned.push(Planned::Grab(copy));
            }
            for copy in self.pre_draw.drain(..) {
                planned.push(Planned::Convert(copy));
            }
            let Ok(buffers_bg) =
                self.buffer_bind_group(id, &resolved.shader, overrides, material_props, &globals)
            else {
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
                // `<name>_TexelSize` of a global texture too, as Unity provides it.
                if handle == 0
                    && let Some(owned::PropertyValue::Texture { handle: h, .. }) =
                        globals_index.find(&globals, name)
                {
                    handle = *h;
                }
                // A depth element is the size of its texture.
                textures
                    .get(&handle::split(handle).0)
                    .map(|t| (t.width, t.height))
            };
            // A buffer bound by a cbuffer's name: the draw's, the material's, else a global one.
            let buffers = &self.buffers;
            let constant_buffer = |name: &str| -> Option<&[u8]> {
                let buffer = |(n, v): &(String, owned::PropertyValue)| match v {
                    owned::PropertyValue::Buffer(h) if n == name => Some(*h),
                    _ => None,
                };
                // The material and the globals are searched only when they have a property of
                // the name (most cbuffers are bound to none).
                let handle = draw.properties.iter().find_map(buffer).or_else(|| {
                    (material_props.get(name).is_some()
                        || globals_index.find(&globals, name).is_some())
                    .then(|| {
                        material_props
                            .properties
                            .iter()
                            .chain(globals.iter())
                            .find_map(buffer)
                    })
                    .flatten()
                })?;
                buffers.get(&handle)?.shadow.as_deref()
            };
            let (camera_view, camera_proj) = self.state.camera.unwrap_or((view, projection));
            let values = compiled::DrawValues {
                gizmo_draw: self.state.gizmo_draw,
                color: draw.color,
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
                lab: &resolved.lab,
                globals: &globals,
                globals_index,
                main_alpha_only: alpha_only,
                texture_size: &size,
                constant_buffer: &constant_buffer,
                instance: mesh_instance,
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
            let program_offset = self.compiled.push(id, &values, Some(&block));
            planned.push(Planned::Draw(PlannedDraw {
                pipeline,
                vertex: vertex.clone(),
                extra,
                index: index.clone(),
                index_format: wgpu::IndexFormat::Uint32,
                range: sub.start..sub.start + sub.count,
                base_vertex: sub.base_vertex,
                material: material_bg,
                texture: texture_bg.clone(),
                buffers: buffers_bg,
                offset,
                stencil_ref,
                viewport,
                scissor,
                program: (id, program_offset),
                instances: 1,
                first_instance: 0,
                indirect: None,
            }));
        }
        planned
    }

    /// A `GrabPass` into `name`: the texture (the target's size and format, made or remade as
    /// needed) and its `_TexelSize` set as globals, and the copy of the target into it; none
    /// when a named texture was grabbed in this camera's render already (measured: Unity grabs
    /// those once per camera render, `_GrabTexture` for every object).
    fn grab(&mut self, name: &str, target: &Target) -> Option<Copy> {
        if name != "_GrabTexture" && !self.grabbed.insert(name.to_string()) {
            return None;
        }
        let Some((source, layer, mip)) = target.texture.clone() else {
            return None;
        };
        let (width, height) = (target.width, target.height);
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
            wgpu::TextureFormat::R8Snorm => texture_format::R8_SNORM,
            wgpu::TextureFormat::Rg8Snorm => texture_format::RG8_SNORM,
            wgpu::TextureFormat::Rgba8Snorm => texture_format::RGBA8_SNORM,
            wgpu::TextureFormat::R8Unorm => texture_format::R8_UNORM,
            wgpu::TextureFormat::Rg8Unorm => texture_format::RG8_UNORM,
            f => resources::integer_code(f).unwrap_or(0),
        };
        let next = handle::PLAIN_MAX - self.grabs.len() as u64;
        let handle = *self.grabs.entry(name.to_string()).or_insert(next);
        let fits = self.textures.get(&handle).is_some_and(|t| {
            t.width == width
                && t.height == height
                && t.texture.format().remove_srgb_suffix() == source.format().remove_srgb_suffix()
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
                render_target: false,
                random_write: false,
                auto_generate_mips: false,
                defer_mips: false,
                retain_resolve: false,
                samples: 1,
                depth_bits: 0,
                layers: 1,
                dimension: 2,
            };
            let old = self.textures.remove(&handle);
            let gpu = resources::upload_texture(
                &self.device,
                &self.queue,
                old,
                &texture,
                self.generation,
            );
            self.textures.insert(handle, gpu);
        }
        let destination = self.textures[&handle].texture.clone();
        let globals = Arc::make_mut(&mut self.globals);
        let texel = [
            1.0 / width as f32,
            1.0 / height as f32,
            width as f32,
            height as f32,
        ];
        for (key, value) in [
            (
                name.to_string(),
                owned::PropertyValue::Texture {
                    handle,
                    st: [1.0, 1.0, 0.0, 0.0],
                },
            ),
            (
                format!("{name}_TexelSize"),
                owned::PropertyValue::Vector(texel),
            ),
        ] {
            match globals.iter_mut().find(|(n, _)| *n == key) {
                Some(slot) => slot.1 = value,
                None => globals.push((key, value)),
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
        let channel_kind = channel_copy_kind(src.format(), dst.format()).filter(|_| {
            src.dimension() == wgpu::TextureDimension::D2
                && dst.dimension() == wgpu::TextureDimension::D2
        });
        if src.format().remove_srgb_suffix() != dst.format().remove_srgb_suffix()
            && channel_kind.is_none()
        {
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
        if w < 0 {
            [x, y, w, h] = [0, 0, sw, sh];
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
            kind,
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
            size: wgpu::Extent3d {
                width: w as u32,
                height: h as u32,
                depth_or_array_layers: 1,
            },
        })
    }

    /// Uploads the planned uniforms, encodes the passes and queues them.
    fn encode(&mut self, passes: Vec<Op>) {
        if passes.is_empty() {
            return;
        }
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
        }
        if !self.uniforms.is_empty() {
            let mut data = vec![0u8; self.uniforms.len() * DRAW_SLOT as usize];
            for (i, u) in self.uniforms.iter().enumerate() {
                let at = i * DRAW_SLOT as usize;
                data[at..at + size_of::<DrawUniform>()].copy_from_slice(bytemuck::bytes_of(u));
            }
            self.queue
                .write_buffer(self.draw_buffer.as_ref().unwrap(), 0, &data);
        }
        let programs = self.compiled.upload(
            &device,
            &self.queue,
            self.draw_buffer.as_ref().unwrap(),
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
                Op::BufferCopy(c) => {
                    compute::encode_copy(&mut encoder, c);
                    continue;
                }
                Op::DepthBlit(b) => {
                    let (pipeline, layout) = self.depth_blit_pipeline(
                        b.source_samples > 1,
                        b.samples,
                        b.samples > 1 && b.samples == b.source_samples,
                    );
                    let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
                        label: Some("unbound depth blit"),
                        layout: &layout,
                        entries: &[wgpu::BindGroupEntry {
                            binding: 0,
                            resource: wgpu::BindingResource::TextureView(&b.source),
                        }],
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
                            stencil_ops: Some(wgpu::Operations {
                                load: wgpu::LoadOp::Clear(0),
                                store: wgpu::StoreOp::Store,
                            }),
                        }),
                        timestamp_writes: None,
                        occlusion_query_set: None,
                        multiview_mask: None,
                    });
                    rp.set_pipeline(&pipeline);
                    rp.set_bind_group(0, &group, &[]);
                    rp.draw(0..3, 0..1);
                    continue;
                }
                Op::DepthCopy(c) => {
                    let (pipeline, layout) = self.depth_copy_pipeline(c.kind);
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
                        let (pipeline, layout) = self.depth_copy_pipeline(channels.kind);
                        let texture = device.create_texture(&wgpu::TextureDescriptor {
                            label: Some("unbound channel copy"),
                            size: channels.size,
                            mip_level_count: 1,
                            sample_count: 1,
                            dimension: wgpu::TextureDimension::D2,
                            format: c.destination.format().remove_srgb_suffix(),
                            usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                                | wgpu::TextureUsages::COPY_SRC,
                            view_formats: &[],
                        });
                        let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
                            label: Some("unbound channel copy"),
                            layout: &layout,
                            entries: &[wgpu::BindGroupEntry {
                                binding: 0,
                                resource: wgpu::BindingResource::TextureView(&channels.source),
                            }],
                        });
                        let target = texture.create_view(&Default::default());
                        let mut rp = encoder.begin_render_pass(&wgpu::RenderPassDescriptor {
                            label: Some("unbound channel copy"),
                            color_attachments: &[Some(wgpu::RenderPassColorAttachment {
                                view: &target,
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
                        drop(rp);
                        texture
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
                    encoder.copy_texture_to_texture(
                        wgpu::TexelCopyTextureInfo {
                            texture: source,
                            mip_level: source_mip,
                            origin: source_origin,
                            aspect: wgpu::TextureAspect::All,
                        },
                        wgpu::TexelCopyTextureInfo {
                            texture: &c.destination,
                            mip_level: c.destination_mip,
                            origin: c.destination_origin,
                            aspect: wgpu::TextureAspect::All,
                        },
                        c.size,
                    );
                    continue;
                }
            };
            for copy in &pass.args {
                copy.encode(&mut encoder);
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
            // store the clear color's bits).
            let load = |format| match pass.color_load {
                wgpu::LoadOp::Clear(c) => wgpu::LoadOp::Clear(resources::integer_clear(
                    format,
                    resources::unorm8_clear_color(format, c),
                )),
                load => load,
            };
            let colors: Vec<_> = std::iter::once(Some(wgpu::RenderPassColorAttachment {
                view: &pass.target.color,
                depth_slice: pass.target.depth_slice,
                resolve_target: pass.target.resolve.as_ref(),
                ops: wgpu::Operations {
                    load: load(pass.target.format),
                    store: wgpu::StoreOp::Store,
                },
            }))
            .chain(pass.target.extra.iter().map(|e| {
                Some(wgpu::RenderPassColorAttachment {
                    view: &e.color,
                    depth_slice: e.depth_slice,
                    resolve_target: e.resolve.as_ref(),
                    ops: wgpu::Operations {
                        load: load(e.format),
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
                        stencil_ops: Some(wgpu::Operations {
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
                        rp.set_pipeline(pipeline);
                        rp.set_bind_group(0, &draw_bg, &[*offset]);
                        rp.set_stencil_reference(0);
                        rp.set_scissor_rect(s[0], s[1], s[2], s[3]);
                        rp.draw(0..3, 0..1);
                    }
                    PassItem::Draw(d) => {
                        let (id, offset) = d.program;
                        let Some(group) = programs.get(&id) else {
                            continue;
                        };
                        rp.set_pipeline(&d.pipeline);
                        rp.set_bind_group(0, group, &[offset, d.offset]);
                        rp.set_bind_group(1, &d.material, &[]);
                        rp.set_bind_group(2, &d.texture, &[]);
                        if let Some(buffers) = &d.buffers {
                            rp.set_bind_group(3, buffers, &[]);
                        }
                        rp.set_vertex_buffer(0, d.vertex.slice(..));
                        if let Some(extra) = &d.extra {
                            rp.set_vertex_buffer(1, extra.slice(..));
                        }
                        rp.set_index_buffer(d.index.slice(..), d.index_format);
                        rp.set_stencil_reference(d.stencil_ref);
                        let s = stored_rect(d.scissor);
                        rp.set_scissor_rect(s[0], s[1], s[2], s[3]);
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
            if pass.target.auto_mips {
                if let Some((texture, layer, _)) = &pass.target.texture {
                    self.mipmaps
                        .encode(&mut encoder, texture, pass.target.format, *layer, 1);
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
        self.read_texture_impl(texture, 0, 0, rect, float, false)
    }

    /// As [`Self::read_pixels`], from a mip level of a layer (an array slice, a cube face or a
    /// volume's depth slice at that level); `None` when the texture has no such level or layer.
    pub fn read_texture(
        &mut self,
        texture: u64,
        mip: u32,
        layer: u32,
        rect: [i32; 4],
        float: bool,
        current_attachment: bool,
    ) -> Option<Vec<u8>> {
        self.read_texture_impl(texture, mip, layer, rect, float, !current_attachment)
    }

    /// A read of the part of `rect` inside a screen of `size` that no pass has started:
    /// transparent black, four bytes or floats per pixel.
    fn unstarted_screen(size: (u32, u32), rect: [i32; 4], float: bool) -> Vec<u8> {
        let (tw, th) = (size.0 as i32, size.1 as i32);
        let w = ((rect[0] + rect[2]).clamp(0, tw) - rect[0].clamp(0, tw)) as usize;
        let h = ((rect[1] + rect[3]).clamp(0, th) - rect[1].clamp(0, th)) as usize;
        vec![0; w * h * if float { 16 } else { 4 }]
    }

    fn read_texture_impl(
        &mut self,
        texture: u64,
        mip: u32,
        layer: u32,
        rect: [i32; 4],
        float: bool,
        retained_resolve: bool,
    ) -> Option<Vec<u8>> {
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
            let current = &self.textures.get(&texture)?.texture;
            if retained_resolve {
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
                        | wgpu::TextureFormat::Rgba8Snorm => {
                            for pixel in line.chunks_exact(texel as usize) {
                                let rgba = resources::norm_texel_to_f32(format, pixel)?;
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
    return vec4<f32>(textureLoad(depth, vec2<i32>(p.xy), 0).x, 0.0, 0.0, 1.0);
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

/// The vertices a draw of 16-bit indices with base vertex 0 can fetch: one per index value,
/// 0..=0xFFFF.
const INDEXED_VERTICES: u64 = 1 << 16;

/// Why draws cannot read `count` indices of `size` bytes from index `first` of a buffer (its size
/// in bytes and whether it can be read as indices; none: it does not exist), if they cannot. They
/// can when the buffer holds the range, the topology is a list one (the pipelines set no strip
/// index format, which an indexed strip draw needs), and each draw either reads no vertex inputs
/// (its pipeline binds no vertex buffer: "a draw call on the GPU, without a vertex buffer", the
/// indexed DrawProcedural 6000.0, so any index, base vertex or indirect count is safe) or is a
/// direct draw of 16-bit indices whose every vertex buffer holds a vertex for each of the 2^16
/// index values (the procedural stand-in's 65536): with base vertex 0, no index fetches past
/// vertex 0xFFFF.
fn external_index_refusal(
    buffer: Option<(u64, bool)>,
    size: u8,
    first: u32,
    count: u32,
    strip: bool,
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
    if strip {
        return Some("strip topologies are not drawn from a buffer's indices");
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

    /// Copies between R8 storages repack their texels; others (RG8 with RGBA8) do not.
    #[test]
    fn channel_copies_join_r8_and_rgba8_storages() {
        use wgpu::TextureFormat as F;
        assert_eq!(
            channel_copy_kind(F::Rgba8UnormSrgb, F::R8Unorm),
            Some(CopyKind::ToR8)
        );
        assert_eq!(
            channel_copy_kind(F::R8Unorm, F::Rgba8Unorm),
            Some(CopyKind::ToRgba8)
        );
        assert_eq!(channel_copy_kind(F::Rg8Unorm, F::Rgba8Unorm), None);
        assert_eq!(channel_copy_kind(F::Rgba8Unorm, F::Rg8Unorm), None);
        assert_eq!(channel_copy_kind(F::R8Unorm, F::Rg8Unorm), None);
        assert_eq!(channel_copy_kind(F::Rgba16Float, F::R8Unorm), None);
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
            external_index_refusal(index, 2, 0, 6, false, &[stand_in(false)]),
            None
        );
        assert_eq!(
            external_index_refusal(index, 2, 3, 3, false, &[stand_in(true)]),
            None
        );
        assert_eq!(
            external_index_refusal(index, 2, 0, 6, false, &[stand_in(false), stand_in(true)]),
            None
        );
    }

    /// Anything else is refused: a vertex short in either buffer, an indirect draw, a strip, a
    /// missing buffer or one without index usage, a range past its end.
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
            assert!(external_index_refusal(index, 2, 0, 6, false, &draws).is_some());
        }
        let covered = [stand_in(false)];
        assert!(external_index_refusal(index, 2, 0, 6, true, &covered).is_some());
        assert!(external_index_refusal(None, 2, 0, 6, false, &covered).is_some());
        assert!(external_index_refusal(Some((12, false)), 2, 0, 6, false, &covered).is_some());
        assert!(external_index_refusal(index, 2, 4, 3, false, &covered).is_some());
        assert!(external_index_refusal(index, 2, 0, 7, false, &covered).is_some());
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
                assert_eq!(
                    external_index_refusal(index, size, 0, 6, false, &draws),
                    None
                );
            }
        }
        let mut procedural = stand_in(false);
        procedural.vertex_inputs = false;
        assert!(external_index_refusal(index, 4, 1, 6, false, &[procedural]).is_some());
        assert!(external_index_refusal(index, 4, 0, 6, true, &[stand_in(false)]).is_some());
        assert!(external_index_refusal(index, 4, 0, 6, false, &[stand_in(false)]).is_some());
    }
}
