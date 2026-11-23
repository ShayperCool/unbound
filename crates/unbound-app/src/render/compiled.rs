//! Compiled programs (by `unbound-shaders`): their modules and pipelines, and each draw's uniform
//! block, filled by name with Unity's built-in variables, the material's properties and global
//! shader properties.
use super::pipelines::{DRAW_SLOT, PipelineKey, create_pipeline};
use crate::shaderlab::{PropertyDefault, ShaderLab};
use rustc_hash::{FxHashMap as HashMap, FxHashSet as HashSet};
use std::sync::Arc;
use unbound_assets::color::gamma_to_linear;
use unbound_runtime::render_protocol::owned::{Material, PropertyValue};
use unbound_shaders::{Compiled, Program, Scalar, Settings, ShaderCompiler, TextureDim};

/// Per-binding metadata precomputed once per compiled program for packing draw uniforms and
/// sharing both standalone bound constant buffers and pass-wide/global bindings across draws.
struct BindingPlan {
    /// Byte offset of this binding in the program's logical uniform layout.
    offset: u32,
    /// Byte size of this binding.
    size: u32,
    /// If this binding holds exactly one cbuffer starting at `offset` and is not written per draw
    /// by DOTS instancing, its name so a constant buffer bound to it can be shared across draws.
    standalone_block: Option<String>,
    /// Indices into `Compiled::blocks` lying in this binding.
    blocks: Vec<u32>,
    /// Indices into `Compiled::uniforms` that are arrays (`stride > 0`) lying in this binding.
    arrays: Vec<u32>,
    /// True if any uniform in this binding varies per draw (per-draw builtins, instanced
    /// properties, per-draw derived values, or DOTS instancing blocks).
    has_per_draw_builtin: bool,
    /// True if any uniform in this binding depends on camera/projection/screen/time state.
    has_camera_builtin: bool,
    /// True if any uniform in this binding derives `_TexelSize` (depends on texture sizes).
    has_texel_size: bool,
    /// True if any uniform in this binding derives `_MainTex_ST` or `_MainTex_TexelSize`.
    has_main_tex_derived: bool,
    /// Unique stripped texture names for `_ST` / `_TexelSize` uniforms in this binding.
    tex_derived_bases: Vec<Box<str>>,
}

/// Per-property override metadata precomputed once per compiled program so each draw override
/// resolves its affected binding mask, direct uniform target, and `_ST`/`_TexelSize` derivation
/// flag in a single hash lookup.
#[derive(Clone, Copy, Debug)]
struct OverrideTarget {
    /// Bitmask of uniform bindings affected by an override of this property name.
    binding_mask: u8,
    /// Index into `Compiled::uniforms` for a direct read non-array uniform (`u32::MAX` if none).
    direct_uniform: u32,
    /// True if `<name>_ST` or `<name>_TexelSize` is a read uniform in this program.
    is_tex_derived_base: bool,
}

/// How a program's uniforms and cbuffers map onto its uniform bindings.
struct ProgramBindings {
    bindings: Vec<BindingPlan>,
    /// By uniform index: which binding it lies in.
    uniform_binding: Vec<u8>,
    /// By uniform index: byte offset relative to its binding's start.
    uniform_local_offset: Vec<u32>,
    /// Single-lookup override resolution table keyed by read uniform name and by stripped
    /// `_ST`/`_TexelSize` texture base name.
    override_targets: rustc_hash::FxHashMap<String, OverrideTarget>,
}

impl ProgramBindings {
    fn of(program: &Compiled, plan: &[Source]) -> ProgramBindings {
        let mut bindings: Vec<BindingPlan> = if !program.uniform_bindings.is_empty() {
            program
                .uniform_bindings
                .iter()
                .map(|b| BindingPlan {
                    offset: b.offset,
                    size: b.size,
                    standalone_block: None,
                    blocks: Vec::new(),
                    arrays: Vec::new(),
                    has_per_draw_builtin: false,
                    has_camera_builtin: false,
                    has_texel_size: false,
                    has_main_tex_derived: false,
                    tex_derived_bases: Vec::new(),
                })
                .collect()
        } else if program.uniform_size > 0 {
            vec![BindingPlan {
                offset: 0,
                size: program.uniform_size,
                standalone_block: None,
                blocks: Vec::new(),
                arrays: Vec::new(),
                has_per_draw_builtin: false,
                has_camera_builtin: false,
                has_texel_size: false,
                has_main_tex_derived: false,
                tex_derived_bases: Vec::new(),
            }]
        } else {
            Vec::new()
        };

        let binding_for = |bindings: &[BindingPlan], offset: u32| -> Option<usize> {
            bindings
                .iter()
                .position(|b| offset >= b.offset && offset < b.offset + b.size)
        };

        for (i, block) in program.blocks.iter().enumerate() {
            if let Some(k) = binding_for(&bindings, block.offset) {
                bindings[k].blocks.push(i as u32);
                if super::dots::DotsLayout::uses_block(&block.name) {
                    bindings[k].has_per_draw_builtin = true;
                }
            }
        }
        for b in &mut bindings {
            if b.blocks.len() == 1 {
                let block = &program.blocks[b.blocks[0] as usize];
                if block.offset == b.offset && !super::dots::DotsLayout::uses_block(&block.name) {
                    b.standalone_block = Some(block.name.clone());
                }
            }
        }

        let mut uniform_binding = Vec::with_capacity(program.uniforms.len());
        let mut uniform_local_offset = Vec::with_capacity(program.uniforms.len());
        let mut override_targets: rustc_hash::FxHashMap<String, OverrideTarget> =
            Default::default();
        for (i, u) in program.uniforms.iter().enumerate() {
            let k = binding_for(&bindings, u.offset).unwrap_or(0);
            let local = bindings
                .get(k)
                .map_or(u.offset, |b| u.offset.saturating_sub(b.offset));
            uniform_binding.push(k as u8);
            uniform_local_offset.push(local);
            if u.unread {
                if let Some(entry) = override_targets.get_mut(&u.name) {
                    entry.direct_uniform = u32::MAX;
                }
                continue;
            }
            let bit = if k < MAX_UNIFORM_BINDINGS {
                1u8 << k
            } else {
                0
            };
            let direct = if u.count == 1 && u.stride == 0 {
                i as u32
            } else {
                u32::MAX
            };
            let entry = override_targets
                .entry(u.name.clone())
                .or_insert(OverrideTarget {
                    binding_mask: 0,
                    direct_uniform: u32::MAX,
                    is_tex_derived_base: false,
                });
            entry.binding_mask |= bit;
            entry.direct_uniform = direct;
            if let Some(b) = bindings.get_mut(k) {
                if u.stride > 0 {
                    b.arrays.push(i as u32);
                }
                if let Some(source) = plan.get(i) {
                    match source {
                        Source::Builtin { which, .. } => {
                            if which.is_per_draw() {
                                b.has_per_draw_builtin = true;
                            } else {
                                b.has_camera_builtin = true;
                            }
                        }
                        Source::Named => {}
                    }
                }
                if u.name == SPRITE_COLOR
                    || u.name == "_TextureSampleAdd"
                    || program.instanced_properties.contains(&u.name)
                {
                    b.has_per_draw_builtin = true;
                }
                if let Some(base) = u
                    .name
                    .strip_suffix("_ST")
                    .or_else(|| u.name.strip_suffix("_TexelSize"))
                {
                    if u.name.ends_with("_TexelSize") {
                        b.has_texel_size = true;
                    }
                    if base == "_MainTex" {
                        b.has_main_tex_derived = true;
                    }
                    if !b
                        .tex_derived_bases
                        .iter()
                        .any(|existing| &**existing == base)
                    {
                        b.tex_derived_bases.push(base.into());
                    }
                    let base_entry =
                        override_targets
                            .entry(base.to_string())
                            .or_insert(OverrideTarget {
                                binding_mask: 0,
                                direct_uniform: u32::MAX,
                                is_tex_derived_base: false,
                            });
                    base_entry.binding_mask |= bit;
                    base_entry.is_tex_derived_base = true;
                }
            }
        }

        ProgramBindings {
            bindings,
            uniform_binding,
            uniform_local_offset,
            override_targets,
        }
    }

    fn map_offset(&self, offsets: &DrawOffsets, logical: u32) -> Option<usize> {
        let (k, b) = self
            .bindings
            .iter()
            .enumerate()
            .find(|(_, b)| logical >= b.offset && logical < b.offset + b.size)?;
        Some(offsets.offsets[k] as usize + (logical - b.offset) as usize)
    }
}

struct ProgramGpu {
    shader: String,
    program: Arc<Compiled>,
    /// Where each of the program's uniforms comes from, decided once (see [`Source`]).
    plan: Vec<Source>,
    /// The uniforms by name.
    by_name: rustc_hash::FxHashMap<String, u32>,
    /// Precomputed per-binding layout for packing and sharing constant buffers.
    bindings: ProgramBindings,
    vertex: wgpu::ShaderModule,
    fragment: wgpu::ShaderModule,
    /// The fragment program without its depth output ([`Depthless`]), made the first time a
    /// target without a depth buffer is drawn with a program that writes the depth.
    depthless: Option<Result<Depthless, String>>,
    uniform_layout: wgpu::BindGroupLayout,
    /// Cached group-0 bind group keyed by `(self.buffer_gen, draws_gen)` so steady-state frames
    /// reuse the bind group without recreating it every submission.
    uniform_group: Option<(u64, u64, wgpu::BindGroup)>,
    pub texture_layout: wgpu::BindGroupLayout,
    /// Group 3: the program's buffers (none without any).
    buffer_layout: Option<wgpu::BindGroupLayout>,
    layout: wgpu::PipelineLayout,
    pipelines: HashMap<PipelineKey, wgpu::RenderPipeline>,
    /// Whether the vertex program reads vertex inputs: one that reads none (built from its
    /// vertex and instance ids) draws without vertex buffers.
    vertex_inputs: bool,
    /// Whether the program declares `unity_SpriteColor` (a renderer's own color reaches it there,
    /// read or not: URP's shader library declares it for every program).
    sprite_color: bool,
    /// Where it takes a BatchRendererGroup draw's DOTS instancing data.
    dots: super::dots::DotsLayout,
}

/// A fragment program without its depth output: its module, entry point and WGSL.
struct Depthless {
    module: wgpu::ShaderModule,
    entry: String,
    wgsl: String,
}

/// The most uniform bindings of a program's cbuffers the renderer binds ([`Compiled::uniform_bindings`]):
/// eight bindings of 64 KB, more than any device's per-stage count leaves room for beside the
/// per-draw uniform on the measured drivers (14 per stage on OpenGL Core, 15 dynamic ones on
/// Vulkan, NVIDIA).
pub const MAX_UNIFORM_BINDINGS: usize = 8;

/// Where each uniform binding of a draw lies in [`CompiledPrograms`]'s frame buffer.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct DrawOffsets {
    pub offsets: [u32; MAX_UNIFORM_BINDINGS],
    pub count: u8,
}

/// A constant buffer bound to a cbuffer's name: its handle, mutation revision and CPU shadow bytes.
#[derive(Clone, Copy)]
pub struct ConstantBufferRef<'a> {
    pub handle: u64,
    pub revision: u64,
    pub bytes: &'a [u8],
}

impl DrawOffsets {
    /// The dynamic offsets of a draw whose uniform bindings lie at `self` and per-draw
    /// uniform at `draw`, in binding order (the first uniform binding, the per-draw uniform,
    /// the others), and how many there are.
    pub fn dynamic_offsets(&self, draw: u32) -> ([u32; MAX_UNIFORM_BINDINGS + 1], usize) {
        dynamic_offsets(&self.offsets[..self.count as usize], draw)
    }
}

/// [`ProgramUniforms::offsets`] of uniform bindings at `bindings` (none: one at 0).
fn dynamic_offsets(bindings: &[u32], draw: u32) -> ([u32; MAX_UNIFORM_BINDINGS + 1], usize) {
    let mut out = [0; MAX_UNIFORM_BINDINGS + 1];
    out[0] = bindings.first().copied().unwrap_or(0);
    out[1] = draw;
    for (k, &base) in bindings.iter().enumerate().skip(1) {
        out[k + 1] = base;
    }
    (out, bindings.len().max(1) + 1)
}

/// Why a device cannot bind a program's uniform bindings: one is larger than a uniform binding
/// may be (naming its cbuffers), or there are more than the device binds beside the per-draw
/// uniform.
fn uniform_bindings_refused(program: &Compiled, limits: &wgpu::Limits) -> Option<String> {
    let max_size = limits.max_uniform_buffer_binding_size;
    for binding in &program.uniform_bindings {
        if u64::from(binding.size) > u64::from(max_size) {
            let end = binding.offset + binding.size;
            let names: Vec<&str> = program
                .blocks
                .iter()
                .filter(|b| b.offset >= binding.offset && b.offset < end)
                .map(|b| b.name.as_str())
                .collect();
            return Some(format!(
                "its cbuffer{} `{}` take{} {} bytes in one uniform buffer, over the device's {max_size}",
                if names.len() == 1 { "" } else { "s" },
                names.join("`, `"),
                if names.len() == 1 { "s" } else { "" },
                binding.size,
            ));
        }
    }
    let count = program.uniform_bindings.len().max(1) as u32 + 1;
    let room = limits
        .max_uniform_buffers_per_shader_stage
        .min(limits.max_dynamic_uniform_buffers_per_pipeline_layout)
        .min(MAX_UNIFORM_BINDINGS as u32 + 1);
    (count > room).then(|| {
        format!(
            "its cbuffers take {} uniform buffers of up to {} bytes with the renderer's per-draw one, over the {room} the device binds",
            count,
            unbound_shaders::UNIFORM_BINDING_BYTES
        )
    })
}

/// The textures (and samplers) of the renderer's main texture group (`main_texture_layout`: one
/// of each, both stages), which the pipeline layout of a program that reads the main texture
/// there (`Compiled::main_texture`) carries beside its own; another program's leaves it out.
const MAIN_TEXTURE_BINDINGS: u32 = 1;
/// The uniform a renderer's own color reaches URP's sprite shaders by (see `DrawValues::sprite_color`).
const SPRITE_COLOR: &str = "unity_SpriteColor";

/// The bindings of each kind the main texture group adds to a program's layout.
fn main_texture_bindings(program: &Compiled) -> u32 {
    match program.main_texture {
        true => MAIN_TEXTURE_BINDINGS,
        false => 0,
    }
}

/// Why a device cannot bind a program's textures: each texture slot, each further variable of a
/// texture (`texture_aliases`), each sampler the program samples through (one per sampler state
/// it uses, however many textures share it: `TextureSlot::sampler`) and the main texture group
/// of a program that reads it are bindings of both stages, over the device's sampled textures
/// or samplers of a stage. A program reads the main group only where it samples the main
/// texture through its own sampler (else the main texture is a slot of its own).
fn texture_bindings_refused(program: &Compiled, limits: &wgpu::Limits) -> Option<String> {
    let textures = (program.textures.len() + program.texture_aliases.len()) as u32
        + main_texture_bindings(program);
    let samplers = program.texture_slots.iter().filter(|s| s.sampler).count() as u32
        + main_texture_bindings(program);
    if textures > limits.max_sampled_textures_per_shader_stage {
        return Some(format!(
            "it binds {textures} textures, over the {} the device binds in a stage",
            limits.max_sampled_textures_per_shader_stage
        ));
    }
    (samplers > limits.max_samplers_per_shader_stage).then(|| {
        format!(
            "it binds {samplers} samplers, over the {} the device binds in a stage",
            limits.max_samplers_per_shader_stage
        )
    })
}

/// Bitwise camera/target/time inputs for caching derived camera built-ins across draws.
#[derive(Clone, Copy, PartialEq, Eq)]
struct CameraInput {
    view: [u32; 16],
    proj: [u32; 16],
    gpu_proj: [u32; 16],
    camera_view: [u32; 16],
    camera_proj: [u32; 16],
    screen: [u32; 2],
    time: u32,
    flipped: bool,
    reversed_z: bool,
}

impl CameraInput {
    fn from_values(v: &DrawValues) -> Self {
        Self {
            view: v.view.map(f32::to_bits),
            proj: v.proj.map(f32::to_bits),
            gpu_proj: v.gpu_proj.map(f32::to_bits),
            camera_view: v.camera_view.map(f32::to_bits),
            camera_proj: v.camera_proj.map(f32::to_bits),
            screen: v.screen.map(f32::to_bits),
            time: v.time.to_bits(),
            flipped: v.flipped,
            reversed_z: v.reversed_z,
        }
    }
}

/// Camera/time/screen derived built-ins computed once per camera state change rather than per draw.
#[derive(Clone, Copy)]
struct CachedCamera {
    input: CameraInput,
    id: u64,
    view_inv: Mat,
    view_inv_t: Mat,
    matrix_vp: Mat,
    proj_inv: Mat,
    world_to_camera: Mat,
    camera_to_world: Mat,
    camera_pos: [f32; 4],
    projection_params: [f32; 4],
    screen_params: [f32; 4],
    zbuffer_params: [f32; 4],
    ortho_params: [f32; 4],
    time_vec: [f32; 4],
    sin_time: [f32; 4],
    cos_time: [f32; 4],
}

/// `_Time`, `_SinTime` and `_CosTime` of the time `t`. These functions serve the cached camera and
/// the uncached builtin path alike, so both give the same bits: the four sines written out in each
/// place were compiled differently on arm64 macOS and differed in the last bit.
#[inline(never)]
fn time_vector(t: f32) -> [f32; 4] {
    [t / 20.0, t, t * 2.0, t * 3.0]
}

#[inline(never)]
fn sin_time_vector(t: f32) -> [f32; 4] {
    [(t / 8.0).sin(), (t / 4.0).sin(), (t / 2.0).sin(), t.sin()]
}

#[inline(never)]
fn cos_time_vector(t: f32) -> [f32; 4] {
    [(t / 8.0).cos(), (t / 4.0).cos(), (t / 2.0).cos(), t.cos()]
}

impl CachedCamera {
    fn compute(id: u64, input: CameraInput, v: &DrawValues) -> Self {
        let flip_z = [
            1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, -1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
        ];
        let view_inv = inverse(&v.view);
        let view_inv_t = transpose(&view_inv);
        let matrix_vp = mul(&v.gpu_proj, &v.view);
        let proj_inv = inverse(&v.proj);
        let world_to_camera = mul(&flip_z, &v.camera_view);
        let camera_to_world = inverse(&world_to_camera);
        let cam_inv = inverse(&v.camera_view);
        let camera_pos = [cam_inv[12], cam_inv[13], cam_inv[14], 1.0];
        let (near, far, ortho) = clip_planes(&v.camera_proj);
        let projection_params = [if v.flipped { -1.0 } else { 1.0 }, near, far, 1.0 / far];
        let [w, h] = v.screen;
        let screen_params = [w, h, 1.0 + 1.0 / w, 1.0 + 1.0 / h];
        let (zx, zy) = if v.reversed_z {
            (far / near - 1.0, 1.0)
        } else {
            (1.0 - far / near, far / near)
        };
        let zbuffer_params = [zx, zy, zx / far, zy / far];
        let ortho_params = [
            1.0 / v.camera_proj[0],
            1.0 / v.camera_proj[5],
            0.0,
            if ortho { 1.0 } else { 0.0 },
        ];
        let (time_vec, sin_time, cos_time) = (
            time_vector(v.time),
            sin_time_vector(v.time),
            cos_time_vector(v.time),
        );
        Self {
            input,
            id,
            view_inv,
            view_inv_t,
            matrix_vp,
            proj_inv,
            world_to_camera,
            camera_to_world,
            camera_pos,
            projection_params,
            screen_params,
            zbuffer_params,
            ortho_params,
            time_vec,
            sin_time,
            cos_time,
        }
    }
}

/// Cache key for a non-per-draw uniform binding shared across draws within a frame.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
struct SharedBindingKey {
    program_id: u32,
    binding: u8,
    camera_id: u64,
    globals_revision: u64,
    textures_generation: u64,
    main_texture_handle: u64,
    material_key: u64,
    cbuffers_hash: u64,
}

/// Compiled programs and this frame's uniform blocks.
pub struct CompiledPrograms {
    compiler: Option<ShaderCompiler>,
    programs: Vec<ProgramGpu>,
    ids: HashMap<usize, u32>,
    /// Variants that failed (program, keywords, linear).
    failed: HashSet<(Program, Vec<String>, bool)>,
    data: Vec<u8>,
    used: HashSet<u32>,
    buffer: Option<wgpu::Buffer>,
    /// Monotonic generation incremented whenever `buffer` is allocated or grown.
    buffer_gen: u64,
    /// Last `draws_gen` seen by [`Self::upload`]; when it changes, cached group-0 bind groups are
    /// cleared so no program keeps a stale reference to a replaced per-draw uniform buffer.
    last_draws_gen: u64,
    /// A uniform name being built (an override's `_ST` / `_TexelSize`), reused from draw to draw.
    name: String,
    /// Reusable per-draw scratch of `(override_index, OverrideTarget)` populated in a single pass
    /// over `DrawValues::overrides`.
    override_scratch: Vec<(u32, OverrideTarget)>,
    /// Standalone constant buffers already copied into `data` this frame:
    /// `(buffer handle, buffer revision, binding size) -> byte offset in data`.
    shared_constants: HashMap<(u64, u64, u32), u32>,
    /// Shared non-per-draw bindings already packed into `data` this frame.
    shared_bindings: HashMap<SharedBindingKey, u32>,
    /// Cached derived built-ins for the most recent camera/target/time state.
    camera_cache: Option<CachedCamera>,
    next_camera_id: u64,
    /// Shared zeroed `DRAW_SLOT` in `data` for programs without uniform bindings, allocated once
    /// per frame.
    empty_slot: Option<u32>,
}

/// What a draw's uniform block is filled from.
#[derive(Clone, Copy)]
pub struct DrawValues<'a, 'buffers> {
    pub model: [f32; 16],
    pub gizmo_draw: bool,
    pub color: [f32; 4],
    /// `unity_SpriteColor`: a renderer's own color (white for other draws).
    pub sprite_color: [f32; 4],
    pub view: [f32; 16],
    /// The projection as Unity's API has it (OpenGL's convention).
    pub proj: [f32; 16],
    /// The projection the device draws with (`GL.GetGPUProjectionMatrix` for the target):
    /// `UNITY_MATRIX_P` and `UNITY_MATRIX_VP`.
    pub gpu_proj: [f32; 16],
    /// The projection is flipped for the target (`_ProjectionParams.x` -1).
    pub flipped: bool,
    /// The device's depth is reversed (`_ZBufferParams`).
    pub reversed_z: bool,
    /// The camera's matrices (its last setup): the camera built-ins read them, not the current
    /// ones a blit or command buffer set.
    pub camera_view: [f32; 16],
    pub camera_proj: [f32; 16],
    /// The target's size in pixels.
    pub screen: [f32; 2],
    pub time: f32,
    pub linear: bool,
    pub material: &'a Material,
    /// The draw's own properties (a MaterialPropertyBlock, per-renderer lights and probes), over
    /// the material's: the last of a name wins.
    pub overrides: &'a [(String, PropertyValue)],
    /// A property of the draw's own the program does not take (a renderer's constant it reads
    /// elsewhere): left out wherever `overrides` are read.
    pub hidden: Option<&'static str>,
    pub lab: &'a ShaderLab,
    /// Global shader properties (`Shader.SetGlobal*`, lights, ambient).
    pub globals: &'a [(String, PropertyValue)],
    /// Where each global property is in `globals`.
    pub globals_index: &'a unbound_runtime::render_protocol::owned::NameIndex,
    /// Monotonic revision incremented whenever `globals` changes.
    pub globals_revision: u64,
    /// Monotonic revision incremented whenever any GPU texture is created, resized or removed.
    pub textures_generation: u64,
    /// The draw's resolved `_MainTex` handle (for caching bindings with `_MainTex_ST`/`_TexelSize`).
    pub main_texture_handle: u64,
    /// Whether the main texture is alpha-only (UI text): `_TextureSampleAdd` defaults to white.
    pub main_alpha_only: bool,
    /// A texture property's texture size (width, height), when one is bound.
    pub texture_size: &'a dyn Fn(&str) -> Option<(u32, u32)>,
    /// The constant buffer bound to a cbuffer's name, when one is.
    pub constant_buffer: &'buffers dyn Fn(&str) -> Option<ConstantBufferRef<'buffers>>,
    /// The draw is this instance of a mesh-instancing call (`MeshInstance`): its scalar
    /// uniforms take this element of the draw's arrays of their name.
    pub instance: Option<u32>,
    /// The instance is one an instance multiplier adds past its call's count: its matrices
    /// and instanced properties read zero (no instance data was written for it).
    pub instance_empty: bool,
    /// One uniform's selected element, over the draw's other values (see [`write_uniform`]).
    pub element: Option<(&'a str, &'a PropertyValue)>,
}

impl DrawValues<'_, '_> {
    /// A property of the draw, else of its material.
    pub fn get(&self, name: &str) -> Option<&PropertyValue> {
        if let Some((n, value)) = self.element
            && n == name
        {
            return Some(value);
        }
        self.override_of(name).or_else(|| self.material.get(name))
    }

    /// The draw's own property of a name (the last one).
    fn override_of(&self, name: &str) -> Option<&PropertyValue> {
        if self.hidden == Some(name) {
            return None;
        }
        self.overrides
            .iter()
            .rev()
            .find(|(n, _)| n == name)
            .map(|(_, v)| v)
    }

    /// The draw's own properties the program takes (all but [`DrawValues::hidden`]).
    #[cfg(test)]
    fn taken_overrides(&self) -> impl Iterator<Item = &(String, PropertyValue)> {
        let hidden = self.hidden;
        self.overrides
            .iter()
            .filter(move |(n, _)| hidden != Some(n.as_str()))
    }
}

/// Element `index` of an array property as the value of one element: a float, vector or matrix
/// (1, 4 or 16 components); none past the array's end.
fn array_element(components: u32, data: &[f32], index: u32) -> Option<PropertyValue> {
    let n = components as usize;
    let start = (index as usize).checked_mul(n)?;
    let e = data.get(start..start.checked_add(n)?)?;
    match n {
        1 => Some(PropertyValue::Float(e[0])),
        4 => Some(PropertyValue::Vector(e.try_into().ok()?)),
        16 => Some(PropertyValue::Matrix(e.try_into().ok()?)),
        _ => None,
    }
}

pub fn view_dimension(dim: TextureDim) -> wgpu::TextureViewDimension {
    match dim {
        TextureDim::D2 => wgpu::TextureViewDimension::D2,
        TextureDim::D2Array => wgpu::TextureViewDimension::D2Array,
        TextureDim::Cube => wgpu::TextureViewDimension::Cube,
        TextureDim::D3 => wgpu::TextureViewDimension::D3,
        TextureDim::CubeArray => wgpu::TextureViewDimension::CubeArray,
    }
}

fn layout_entry(binding: u32, ty: wgpu::BindingType) -> wgpu::BindGroupLayoutEntry {
    wgpu::BindGroupLayoutEntry {
        binding,
        visibility: wgpu::ShaderStages::VERTEX_FRAGMENT,
        ty,
        count: None,
    }
}

fn dynamic_uniform() -> wgpu::BindingType {
    wgpu::BindingType::Buffer {
        ty: wgpu::BufferBindingType::Uniform,
        has_dynamic_offset: true,
        min_binding_size: None,
    }
}

impl CompiledPrograms {
    pub fn new(settings: Option<Settings>) -> CompiledPrograms {
        let compiler = settings.and_then(|s| match ShaderCompiler::new(s) {
            Ok(c) => Some(c),
            Err(e) => {
                tracing::warn!("project shaders cannot be compiled: {e:#}");
                None
            }
        });
        CompiledPrograms {
            compiler,
            programs: Vec::new(),
            ids: HashMap::default(),
            failed: HashSet::default(),
            data: Vec::new(),
            used: HashSet::default(),
            buffer: None,
            buffer_gen: 0,
            last_draws_gen: 0,
            name: String::new(),
            override_scratch: Vec::new(),
            shared_constants: HashMap::default(),
            shared_bindings: HashMap::default(),
            camera_cache: None,
            next_camera_id: 1,
            empty_slot: None,
        }
    }

    /// Enumerate source keyword choices through the same compiler used for ordinary draws.
    pub fn warmup_variants(
        &mut self,
        program: &Program,
        linear: bool,
    ) -> Result<unbound_shaders::KeywordVariants, String> {
        let compiler = self
            .compiler
            .as_mut()
            .ok_or("shader compiler is unavailable")?;
        compiler
            .pragmas(program, linear)
            .map(|p| p.variants())
            .map_err(|e| format!("{e:#}"))
    }

    /// Diagnostic counts, queried explicitly by the cold shader warm-up probe.
    pub fn prepared_counts(&self, shader: &str) -> (usize, usize) {
        let programs = self
            .programs
            .iter()
            .filter(|p| counted_for(&p.shader, shader));
        programs.fold((0, 0), |(count, pipelines), p| {
            (count + 1, pipelines + p.pipelines.len())
        })
    }

    /// The variant of a pass's program a material with `keywords` draws with (compiled on first
    /// use), or `None` when it does not compile (the reason is logged once).
    pub fn variant(
        &mut self,
        device: &wgpu::Device,
        shader: &str,
        source: &Program,
        keywords: &[String],
        linear: bool,
        main_texture_layout: &wgpu::BindGroupLayout,
    ) -> Option<u32> {
        let compiler = self.compiler.as_mut()?;
        let failure = (source.clone(), keywords.to_vec(), linear);
        if self.failed.contains(&failure) {
            return None;
        }
        let program = match compiler.compile(source, keywords, linear) {
            Ok(p) => p,
            Err(e) => {
                tracing::warn!(
                    "shader `{shader}` (keywords: {}): a pass does not compile for Unbound, so it draws with the error shader: {e:#}",
                    if keywords.is_empty() {
                        "none".to_string()
                    } else {
                        keywords.join(" ")
                    }
                );
                self.failed.insert(failure);
                return None;
            }
        };
        let key = Arc::as_ptr(&program) as usize;
        if let Some(id) = self.ids.get(&key) {
            return Some(*id);
        }
        let limits = device.limits();
        if let Some(why) = uniform_bindings_refused(&program, &limits)
            .or_else(|| texture_bindings_refused(&program, &limits))
        {
            tracing::warn!(
                "shader `{shader}` (keywords: {}): a pass cannot be bound on this device, so it draws with the error shader: {why}",
                if keywords.is_empty() {
                    "none".to_string()
                } else {
                    keywords.join(" ")
                }
            );
            self.failed.insert(failure);
            return None;
        }
        tracing::info!(
            "shader `{shader}`: compiled its program ({}{})",
            if program.keywords.is_empty() {
                "no keywords".to_string()
            } else {
                program.keywords.join(" ")
            },
            if linear { ", linear" } else { "" }
        );
        let module = |label: &str, wgsl: &str| {
            device.create_shader_module(wgpu::ShaderModuleDescriptor {
                label: Some(label),
                source: wgpu::ShaderSource::Wgsl(wgsl.into()),
            })
        };
        let further = program.uniform_bindings.len().saturating_sub(1);
        let uniform_entries: Vec<_> = (0..2 + further as u32)
            .map(|binding| layout_entry(binding, dynamic_uniform()))
            .collect();
        let uniform_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound program uniforms"),
            entries: &uniform_entries,
        });
        let mut texture_entries = Vec::new();
        // Each slot's texture entry (a further variable of the texture shows it too).
        let mut slot_textures = Vec::with_capacity(program.textures.len());
        for k in 0..program.textures.len() as u32 {
            let dim = program
                .texture_dims
                .get(k as usize)
                .copied()
                .unwrap_or(TextureDim::D2);
            let slot = program
                .texture_slots
                .get(k as usize)
                .copied()
                .unwrap_or_default();
            let texture = layout_entry(
                2 * k,
                wgpu::BindingType::Texture {
                    sample_type: if slot.multisampled
                        && slot.sample == unbound_shaders::SampleKind::Float
                    {
                        wgpu::TextureSampleType::Float { filterable: false }
                    } else {
                        sample_type(slot.sample)
                    },
                    view_dimension: view_dimension(dim),
                    multisampled: slot.multisampled,
                },
            );
            texture_entries.push(texture);
            slot_textures.push(texture);
            // A sampler the program never samples through is no binding: it would count against
            // the device's samplers of a stage ([`texture_bindings_refused`]).
            if !slot.sampler {
                continue;
            }
            // Integer textures are read unfiltered: their slot's sampler filters nothing.
            texture_entries.push(layout_entry(
                2 * k + 1,
                wgpu::BindingType::Sampler(if slot.comparison {
                    wgpu::SamplerBindingType::Comparison
                } else if slot.integer() || slot.multisampled {
                    wgpu::SamplerBindingType::NonFiltering
                } else {
                    wgpu::SamplerBindingType::Filtering
                }),
            ));
        }
        // A further variable of a texture (for another sampler than its own) shows its slot's
        // texture.
        let slots = program.textures.len() as u32;
        for (j, &slot) in program.texture_aliases.iter().enumerate() {
            texture_entries.push(wgpu::BindGroupLayoutEntry {
                binding: 2 * slots + j as u32,
                ..slot_textures[slot as usize]
            });
        }
        let texture_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound program textures"),
            entries: &texture_entries,
        });
        let buffer_layout = (!program.buffers.is_empty()).then(|| {
            let entries: Vec<_> = program
                .buffers
                .iter()
                .enumerate()
                .map(|(k, b)| wgpu::BindGroupLayoutEntry {
                    binding: k as u32,
                    // Written buffers are the fragment program's only.
                    visibility: if b.read_only {
                        wgpu::ShaderStages::VERTEX_FRAGMENT
                    } else {
                        wgpu::ShaderStages::FRAGMENT
                    },
                    ty: wgpu::BindingType::Buffer {
                        ty: wgpu::BufferBindingType::Storage {
                            read_only: b.read_only,
                        },
                        has_dynamic_offset: false,
                        min_binding_size: None,
                    },
                    count: None,
                })
                .collect();
            device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
                label: Some("unbound program buffers"),
                entries: &entries,
            })
        });
        let layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("unbound program"),
            bind_group_layouts: &[
                Some(&uniform_layout),
                Some(&texture_layout),
                // A group a pipeline layout leaves out takes whatever the draw binds there.
                program.main_texture.then_some(main_texture_layout),
                buffer_layout.as_ref(),
            ],
            immediate_size: 0,
        });
        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        let bindings = ProgramBindings::of(&program, &plan);
        let gpu = ProgramGpu {
            shader: shader.into(),
            plan,
            by_name: program
                .uniforms
                .iter()
                .enumerate()
                .map(|(i, u)| (u.name.clone(), i as u32))
                .collect(),
            bindings,
            vertex_inputs: program.vertex_inputs,
            sprite_color: program.uniforms.iter().any(|u| u.name == SPRITE_COLOR),
            dots: super::dots::DotsLayout::of(&program),
            vertex: module(&format!("{shader} (vertex)"), &program.vertex),
            fragment: module(&format!("{shader} (fragment)"), &program.fragment),
            depthless: None,
            program,
            uniform_layout,
            uniform_group: None,
            texture_layout,
            buffer_layout,
            layout,
            pipelines: HashMap::default(),
        };
        let id = self.programs.len() as u32;
        self.programs.push(gpu);
        self.ids.insert(key, id);
        Some(id)
    }

    /// The shader compiler (compute kernels compile through it too); none when shaders cannot
    /// compile on this machine.
    pub fn compiler(&mut self) -> Option<&mut ShaderCompiler> {
        self.compiler.as_mut()
    }

    pub fn program(&self, id: u32) -> &Arc<Compiled> {
        &self.programs[id as usize].program
    }

    /// Whether program `id`'s vertex program reads vertex inputs (its pipelines bind vertex
    /// buffers).
    pub fn reads_vertex_inputs(&self, id: u32) -> bool {
        self.programs[id as usize].vertex_inputs
    }

    /// Whether program `id` declares `unity_SpriteColor`.
    pub fn declares_sprite_color(&self, id: u32) -> bool {
        self.programs[id as usize].sprite_color
    }

    pub fn texture_layout(&self, id: u32) -> &wgpu::BindGroupLayout {
        &self.programs[id as usize].texture_layout
    }

    /// The layout of a program's buffers (group 3), when it has any.
    pub fn buffer_layout(&self, id: u32) -> Option<&wgpu::BindGroupLayout> {
        self.programs[id as usize].buffer_layout.as_ref()
    }

    /// Programs can prepare before their typed render targets are bound. Target-specific
    /// pipelines wait until a compatible attachment configuration is available.
    pub fn accepts_target(
        &self,
        id: u32,
        first: Option<wgpu::TextureFormat>,
        extra: &[Option<wgpu::TextureFormat>; 7],
    ) -> bool {
        super::pipelines::output_mismatch(
            first,
            extra,
            super::pipelines::fragment_output_kinds(&self.programs[id as usize].program.fragment),
        )
        .is_none()
    }

    pub fn pipeline(
        &mut self,
        device: &wgpu::Device,
        id: u32,
        key: PipelineKey,
    ) -> Result<wgpu::RenderPipeline, String> {
        let gpu = &mut self.programs[id as usize];
        if let Some(p) = gpu.pipelines.get(&key) {
            return Ok(p.clone());
        }
        // A target without a depth buffer has nowhere to write a program's depth output: it is
        // drawn with the program's colors alone, or not at all on a device that refuses it.
        if key.depth.is_none()
            && gpu.depthless.is_none()
            && super::pipelines::writes_depth(&gpu.program.fragment)
        {
            let device_type = unbound_runtime::graphics_device::get();
            gpu.depthless = Some(if device_type.draws_depth_output_without_depth_buffer() {
                unbound_shaders::without_depth_output(&gpu.program.fragment)
                    .map_err(|e| format!("its depth output cannot be left out: {e:#}"))
                    .and_then(|found| found.ok_or_else(|| "it has no depth output".to_string()))
                    .map(|(wgsl, entry)| Depthless {
                        module: device.create_shader_module(wgpu::ShaderModuleDescriptor {
                            label: Some(&format!("{} (fragment, no depth)", gpu.shader)),
                            source: wgpu::ShaderSource::Wgsl(wgsl.as_str().into()),
                        }),
                        entry,
                        wgsl,
                    })
            } else {
                Err(format!(
                    "it writes depth into a target without a depth buffer, for which the {device_type} device makes no pipeline"
                ))
            });
        }
        let (fragment, entry, wgsl) = match (&gpu.depthless, key.depth) {
            (Some(depthless), None) => {
                let depthless = depthless.as_ref().map_err(String::clone)?;
                (&depthless.module, &depthless.entry, &depthless.wgsl)
            }
            _ => (
                &gpu.fragment,
                &gpu.program.fragment_entry,
                &gpu.program.fragment,
            ),
        };
        // Each output a target takes must be of its scalar type (an integer target integers).
        let kinds = super::pipelines::fragment_output_kinds(wgsl);
        if let Some((location, kind, format)) =
            super::pipelines::output_mismatch(key.format, &key.extra, kinds)
        {
            return Err(format!(
                "its output {location} ({kind:?}) cannot be written into a {format:?} target"
            ));
        }
        let pipeline = create_pipeline(
            device,
            &gpu.layout,
            (&gpu.vertex, &gpu.program.vertex_entry),
            (fragment, entry),
            key,
            match (gpu.vertex_inputs, gpu.program.extra_vertex_inputs) {
                (false, _) => 0,
                (true, false) => 1,
                (true, true) => 2,
            },
            super::pipelines::fragment_outputs(wgsl),
        );
        gpu.pipelines.insert(key, pipeline.clone());
        Ok(pipeline)
    }

    pub fn begin_frame(&mut self) {
        self.data.clear();
        self.used.clear();
        self.shared_constants.clear();
        self.shared_bindings.clear();
        self.empty_slot = None;
    }

    fn cached_camera(&mut self, values: &DrawValues) -> CachedCamera {
        let input = CameraInput::from_values(values);
        if let Some(cam) = self.camera_cache
            && cam.input == input
        {
            return cam;
        }
        let id = self.next_camera_id;
        self.next_camera_id = self.next_camera_id.wrapping_add(1).max(1);
        let cam = CachedCamera::compute(id, input, values);
        self.camera_cache = Some(cam);
        cam
    }

    /// A draw's uniform bindings; returns each binding's offset in this frame's buffer.
    pub fn push(
        &mut self,
        id: u32,
        values: &DrawValues,
        block: Option<&MaterialBlock>,
    ) -> DrawOffsets {
        self.used.insert(id);
        let cam = self.cached_camera(values);
        let gpu = &self.programs[id as usize];
        push_program_uniforms(
            id,
            &gpu.program,
            &gpu.plan,
            &gpu.by_name,
            &gpu.bindings,
            block,
            values,
            &cam,
            &mut self.data,
            &mut self.shared_constants,
            &mut self.shared_bindings,
            &mut self.empty_slot,
            &mut self.name,
            &mut self.override_scratch,
        )
    }

    /// Whether a program reads DOTS instancing data ([`super::dots::DotsLayout::instanced`]).
    pub fn dots_instanced(&self, id: u32) -> bool {
        self.programs[id as usize].dots.instanced()
    }

    /// Writes a BatchRendererGroup draw's DOTS instancing data into the uniform bindings pushed at
    /// `offsets` ([`super::dots::write_mapped`]). Returns the batch buffer's window to copy into
    /// the frame's uniform buffer, in uniform buffer mode ([`super::dots::window_mapped`]): the
    /// destination byte offset, the buffer's byte offset and the bytes.
    pub fn write_dots(
        &mut self,
        id: u32,
        offsets: &DrawOffsets,
        draw: &unbound_runtime::render_commands::DotsDraw,
        overrides: &[(String, PropertyValue)],
        buffer_size: u64,
    ) -> Option<(u64, u64, u64)> {
        let gpu = &self.programs[id as usize];
        super::dots::write_mapped(&gpu.dots, draw, overrides, &mut self.data, |logical| {
            gpu.bindings.map_offset(offsets, logical)
        });
        super::dots::window_mapped(&gpu.dots, draw, buffer_size, |logical| {
            gpu.bindings.map_offset(offsets, logical)
        })
    }

    /// The frame's uniform buffer, once [`Self::upload`] has made it.
    pub fn uniform_buffer(&self) -> Option<&wgpu::Buffer> {
        self.buffer.as_ref()
    }

    /// The part of a program's uniform block a material decides, made once per material: the
    /// material's values and the shader's defaults; the rest is filled per draw.
    pub fn material_block(
        &self,
        id: u32,
        material: &Material,
        lab: &ShaderLab,
        linear: bool,
    ) -> MaterialBlock {
        let gpu = &self.programs[id as usize];
        material_block(
            &gpu.program,
            &gpu.plan,
            &gpu.bindings,
            material,
            lab,
            linear,
        )
    }

    /// The uniform values a draw gets, by name (`UNBOUND_TRACE_UNIFORMS` tracing).
    pub fn describe(&self, id: u32, values: &DrawValues) -> String {
        let gpu = &self.programs[id as usize];
        let program = &gpu.program;
        program
            .uniforms
            .iter()
            .zip(&gpu.plan)
            .map(
                |(u, source)| match value(&u.name, *source, values, &program.textures) {
                    Some(v) if u.count == 1 => format!(
                        "{}={:?}",
                        u.name,
                        &v.as_slice()[..(u.rows * u.columns).min(16) as usize]
                    ),
                    Some(_) => format!("{}=[array]", u.name),
                    None => format!("{}=-", u.name),
                },
            )
            .collect::<Vec<_>>()
            .join(" ")
    }

    /// Uploads the frame's uniform blocks and ensures every used program has a cached group-0
    /// bind group bound to the current uniform and per-draw buffers.
    pub fn upload(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        draws: &wgpu::Buffer,
        draws_gen: u64,
        draw_size: u64,
    ) {
        if self.used.is_empty() {
            return;
        }
        let bytes = (self.data.len() as u64).max(DRAW_SLOT);
        let mut invalidated = draws_gen != self.last_draws_gen;
        if self.buffer.as_ref().is_none_or(|b| b.size() < bytes) {
            self.buffer = Some(device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound program uniforms"),
                size: bytes.next_power_of_two().max(DRAW_SLOT * 64),
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            }));
            self.buffer_gen = self.buffer_gen.wrapping_add(1).max(1);
            invalidated = true;
        }
        if invalidated {
            self.last_draws_gen = draws_gen;
            for gpu in &mut self.programs {
                gpu.uniform_group = None;
            }
        }
        let buffer = self.buffer.as_ref().unwrap();
        if !self.data.is_empty() {
            queue.write_buffer(buffer, 0, &self.data);
        }
        let range = |size: u64| {
            wgpu::BindingResource::Buffer(wgpu::BufferBinding {
                buffer,
                offset: 0,
                size: wgpu::BufferSize::new(size),
            })
        };
        let buffer_gen = self.buffer_gen;
        let mut entries = Vec::with_capacity(MAX_UNIFORM_BINDINGS + 1);
        for &id in &self.used {
            let gpu = &mut self.programs[id as usize];
            if matches!(
                &gpu.uniform_group,
                Some((bg_gen, d_gen, _)) if *bg_gen == buffer_gen && *d_gen == draws_gen
            ) {
                continue;
            }
            let bindings = &gpu.program.uniform_bindings;
            // A program without cbuffers binds a minimal range.
            let first = bindings.first().map_or(16, |b| b.size.max(16));
            entries.clear();
            entries.push(wgpu::BindGroupEntry {
                binding: 0,
                resource: range(first as u64),
            });
            entries.push(wgpu::BindGroupEntry {
                binding: 1,
                resource: wgpu::BindingResource::Buffer(wgpu::BufferBinding {
                    buffer: draws,
                    offset: 0,
                    size: wgpu::BufferSize::new(draw_size),
                }),
            });
            for (k, b) in bindings.iter().enumerate().skip(1) {
                entries.push(wgpu::BindGroupEntry {
                    binding: k as u32 + 1,
                    resource: range(b.size as u64),
                });
            }
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound program uniforms"),
                layout: &gpu.uniform_layout,
                entries: &entries,
            });
            gpu.uniform_group = Some((buffer_gen, draws_gen, group));
        }
    }

    /// The cached group-0 uniform bind group of program `id` prepared by [`Self::upload`].
    pub fn uniform_group(&self, id: u32) -> Option<&wgpu::BindGroup> {
        self.programs
            .get(id as usize)
            .and_then(|gpu| gpu.uniform_group.as_ref())
            .map(|(_, _, group)| group)
    }
}

// ------------------------------------------------------------------ the uniform block

type Mat = [f32; 16];

fn mul(a: &Mat, b: &Mat) -> Mat {
    let mut c = [0.0; 16];
    for col in 0..4 {
        for row in 0..4 {
            c[col * 4 + row] = (0..4).map(|k| a[k * 4 + row] * b[col * 4 + k]).sum();
        }
    }
    c
}

fn transpose(m: &Mat) -> Mat {
    let mut t = [0.0; 16];
    for col in 0..4 {
        for row in 0..4 {
            t[row * 4 + col] = m[col * 4 + row];
        }
    }
    t
}

fn inverse(m: &Mat) -> Mat {
    // Fast exact 3D affine inverse for standard transform matrices whose bottom row is [0, 0, 0, 1].
    if m[3] == 0.0 && m[7] == 0.0 && m[11] == 0.0 && m[15] == 1.0 {
        let c0 = m[5] * m[10] - m[9] * m[6];
        let c4 = m[8] * m[6] - m[4] * m[10];
        let c8 = m[4] * m[9] - m[8] * m[5];
        let det = m[0] * c0 + m[1] * c4 + m[2] * c8;
        if det.abs() < 1e-20 {
            return [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ];
        }
        // Divided by the determinant as the general inverse does, so the 3x3 part is the same.
        let i0 = c0 / det;
        let i4 = c4 / det;
        let i8 = c8 / det;
        let i1 = (m[9] * m[2] - m[1] * m[10]) / det;
        let i5 = (m[0] * m[10] - m[8] * m[2]) / det;
        let i9 = (m[8] * m[1] - m[0] * m[9]) / det;
        let i2 = (m[1] * m[6] - m[5] * m[2]) / det;
        let i6 = (m[4] * m[2] - m[0] * m[6]) / det;
        let i10 = (m[0] * m[5] - m[4] * m[1]) / det;
        let (tx, ty, tz) = (m[12], m[13], m[14]);
        return [
            i0,
            i1,
            i2,
            0.0,
            i4,
            i5,
            i6,
            0.0,
            i8,
            i9,
            i10,
            0.0,
            -(i0 * tx + i4 * ty + i8 * tz),
            -(i1 * tx + i5 * ty + i9 * tz),
            -(i2 * tx + i6 * ty + i10 * tz),
            1.0,
        ];
    }
    inverse_general(m)
}

fn inverse_general(m: &Mat) -> Mat {
    let mut inv = [0.0f32; 16];
    inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15]
        + m[9] * m[7] * m[14]
        + m[13] * m[6] * m[11]
        - m[13] * m[7] * m[10];
    inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15]
        - m[8] * m[7] * m[14]
        - m[12] * m[6] * m[11]
        + m[12] * m[7] * m[10];
    inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15]
        + m[8] * m[7] * m[13]
        + m[12] * m[5] * m[11]
        - m[12] * m[7] * m[9];
    inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14]
        - m[8] * m[6] * m[13]
        - m[12] * m[5] * m[10]
        + m[12] * m[6] * m[9];
    inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15]
        - m[9] * m[3] * m[14]
        - m[13] * m[2] * m[11]
        + m[13] * m[3] * m[10];
    inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15]
        + m[8] * m[3] * m[14]
        + m[12] * m[2] * m[11]
        - m[12] * m[3] * m[10];
    inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15]
        - m[8] * m[3] * m[13]
        - m[12] * m[1] * m[11]
        + m[12] * m[3] * m[9];
    inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14]
        + m[8] * m[2] * m[13]
        + m[12] * m[1] * m[10]
        - m[12] * m[2] * m[9];
    inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15]
        + m[5] * m[3] * m[14]
        + m[13] * m[2] * m[7]
        - m[13] * m[3] * m[6];
    inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15]
        - m[4] * m[3] * m[14]
        - m[12] * m[2] * m[7]
        + m[12] * m[3] * m[6];
    inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15]
        + m[4] * m[3] * m[13]
        + m[12] * m[1] * m[7]
        - m[12] * m[3] * m[5];
    inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14]
        - m[4] * m[2] * m[13]
        - m[12] * m[1] * m[6]
        + m[12] * m[2] * m[5];
    inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11]
        - m[5] * m[3] * m[10]
        - m[9] * m[2] * m[7]
        + m[9] * m[3] * m[6];
    inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11]
        + m[4] * m[3] * m[10]
        + m[8] * m[2] * m[7]
        - m[8] * m[3] * m[6];
    inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11]
        - m[4] * m[3] * m[9]
        - m[8] * m[1] * m[7]
        + m[8] * m[3] * m[5];
    inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10]
        + m[4] * m[2] * m[9]
        + m[8] * m[1] * m[6]
        - m[8] * m[2] * m[5];
    let det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
    if det.abs() < 1e-20 {
        return [
            1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
        ];
    }
    inv.map(|v| v / det)
}

/// Assigned material/override values already use working space. Only shader defaults
/// need conversion here. Unity clamps their negative RGB before making the default linear.
fn property_of(
    found: Option<&PropertyValue>,
    lab: &ShaderLab,
    name: &str,
    linear: bool,
) -> Option<[f32; 4]> {
    let value = match found {
        Some(PropertyValue::Float(v)) => Some([*v, *v, *v, *v]),
        Some(PropertyValue::Int(v)) => Some([*v as f32; 4]),
        Some(PropertyValue::Vector(v)) => Some(*v),
        Some(
            PropertyValue::Texture { .. }
            | PropertyValue::Matrix(_)
            | PropertyValue::Buffer(_)
            | PropertyValue::Array { .. }
            | PropertyValue::Image { .. },
        ) => None,
        None => match lab.property(name) {
            Some(PropertyDefault::Float(v)) => Some([*v; 4]),
            Some(PropertyDefault::Vector(v)) => Some(*v),
            _ => None,
        },
    }?;
    if found.is_none() && linear && lab.colors.contains(name) {
        return Some([
            gamma_to_linear(value[0].max(0.0)),
            gamma_to_linear(value[1].max(0.0)),
            gamma_to_linear(value[2].max(0.0)),
            value[3],
        ]);
    }
    Some(value)
}

/// A texture property's scale and offset (`_X_ST`).
fn texture_st(v: &DrawValues, name: &str) -> [f32; 4] {
    match v.get(name) {
        Some(PropertyValue::Texture { st, .. }) if *st != [0.0; 4] => *st,
        _ => [1.0, 1.0, 0.0, 0.0],
    }
}

/// The near and far planes of an OpenGL projection, and whether it is orthographic.
fn clip_planes(p: &Mat) -> (f32, f32, bool) {
    let (m22, m23) = (p[10], p[14]);
    if p[15] == 1.0 {
        // Orthographic: m22 = -2/(f-n), m23 = -(f+n)/(f-n).
        ((m23 + 1.0) / m22, (m23 - 1.0) / m22, true)
    } else {
        (m23 / (m22 - 1.0), m23 / (m22 + 1.0), false)
    }
}

/// The value of a built-in variable or a material property, as floats (matrices column by column).
/// Where a uniform's value comes from: one of Unity's built-in variables (which a draw's or
/// material's property of the name overrides for the `unity_` ones), or properties by name.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Source {
    Builtin {
        which: Builtin,
        properties_first: bool,
    },
    Named,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Builtin {
    GizmoColor,
    ObjectToWorld,
    WorldToObject,
    MatrixV,
    MatrixInvV,
    MatrixVP,
    Projection,
    CameraProjection,
    InvProjection,
    WorldToCamera,
    CameraToWorld,
    TransposeModelView,
    InvTransModelView,
    ModelView,
    Time,
    SinTime,
    CosTime,
    DeltaTime,
    CameraPos,
    ProjectionParams,
    ScreenParams,
    ZBufferParams,
    OrthoParams,
    WorldTransformParams,
    LodFade,
    LightData,
    ProbesOcclusion,
    RenderingLayer,
}

impl Builtin {
    /// Whether this built-in varies per draw (object matrix, gizmo color, per-renderer flags)
    /// rather than per camera/target/frame.
    fn is_per_draw(self) -> bool {
        use Builtin::*;
        matches!(
            self,
            GizmoColor
                | ObjectToWorld
                | WorldToObject
                | TransposeModelView
                | InvTransModelView
                | ModelView
                | WorldTransformParams
                | LodFade
                | LightData
                | ProbesOcclusion
                | RenderingLayer
        )
    }
}

impl Source {
    fn of(name: &str) -> Source {
        use Builtin::*;
        let which = match name {
            "_GizmoBatchColor" => GizmoColor,
            "unity_ObjectToWorld" => ObjectToWorld,
            "unity_WorldToObject" => WorldToObject,
            "unity_MatrixV" => MatrixV,
            "unity_MatrixInvV" => MatrixInvV,
            "unity_MatrixVP" => MatrixVP,
            "glstate_matrix_projection" => Projection,
            "unity_CameraProjection" => CameraProjection,
            "unity_CameraInvProjection" => InvProjection,
            "unity_WorldToCamera" => WorldToCamera,
            "unity_CameraToWorld" => CameraToWorld,
            // The legacy tree shaders' camera-to-world (UnityBuiltin2xTreeLibrary.cginc).
            "_CameraToWorld" => CameraToWorld,
            "glstate_matrix_transpose_modelview0" => TransposeModelView,
            "glstate_matrix_invtrans_modelview0" => InvTransModelView,
            "glstate_matrix_modelview0" => ModelView,
            "_Time" => Time,
            "_SinTime" => SinTime,
            "_CosTime" => CosTime,
            "unity_DeltaTime" => DeltaTime,
            "_WorldSpaceCameraPos" => CameraPos,
            "_ProjectionParams" => ProjectionParams,
            "_ScreenParams" => ScreenParams,
            "_ZBufferParams" => ZBufferParams,
            "unity_OrthoParams" => OrthoParams,
            "unity_WorldTransformParams" => WorldTransformParams,
            "unity_LODFade" => LodFade,
            "unity_LightData" => LightData,
            "unity_ProbesOcclusion" => ProbesOcclusion,
            "unity_RenderingLayer" => RenderingLayer,
            _ => return Source::Named,
        };
        Source::Builtin {
            which,
            // A value set under the legacy trees' name wins over the camera's, as unity_ ones do.
            properties_first: name.starts_with("unity_") || name == "_CameraToWorld",
        }
    }
}

/// A uniform's value: a vector or a 4x4 matrix (column-major).
#[derive(Debug, Clone, Copy)]
enum Value {
    Vector([f32; 4]),
    Matrix([f32; 16]),
}

impl Value {
    fn as_slice(&self) -> &[f32] {
        match self {
            Value::Vector(v) => v,
            Value::Matrix(m) => m,
        }
    }
}

fn value(name: &str, source: Source, v: &DrawValues, textures: &[String]) -> Option<Value> {
    let vec = |a: [f32; 4]| Some(Value::Vector(a));
    let mat = |m: Mat| Some(Value::Matrix(m));
    let which = match source {
        Source::Named => return named_value(name, v, textures),
        Source::Builtin {
            which,
            properties_first,
        } => {
            // Per-renderer values the engine computed for this draw (lights, probes) come with
            // the draw's own properties and win over the defaults below.
            if properties_first {
                match v.get(name) {
                    Some(PropertyValue::Vector(x)) => return vec(*x),
                    Some(PropertyValue::Float(f)) => return vec([*f; 4]),
                    Some(PropertyValue::Matrix(m)) => return mat(*m),
                    _ => {}
                }
            }
            which
        }
    };
    builtin_value(which, name, v, textures, None)
}

/// A built-in variable's value (past the draw's and material's properties of its name).
fn builtin_value(
    which: Builtin,
    name: &str,
    v: &DrawValues,
    textures: &[String],
    cam: Option<&CachedCamera>,
) -> Option<Value> {
    let vec = |a: [f32; 4]| Some(Value::Vector(a));
    let mat = |m: Mat| Some(Value::Matrix(m));
    let t = v.time;
    let view_inverse = || cam.map_or_else(|| inverse(&v.view), |c| c.view_inv);
    let camera_view_inverse = || inverse(&v.camera_view);
    // Unity's camera matrices: worldToCameraMatrix has OpenGL's negated z; the transform's
    // world-to-local does not.
    let flip_z = [
        1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, -1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
    ];
    use Builtin::*;
    match which {
        GizmoColor => {
            if v.gizmo_draw {
                vec(v.color)
            } else {
                named_value(name, v, textures)
            }
        }
        ObjectToWorld => mat(v.model),
        WorldToObject => mat(if v.instance_empty {
            [0.0; 16]
        } else if v.gizmo_draw {
            // Gizmos transform normals as directions. Unity's shader sources factor their
            // normal matrix through this uniform and the inverse view matrix.
            transpose(&v.model)
        } else {
            inverse(&v.model)
        }),
        MatrixV => mat(v.view),
        MatrixInvV => mat(view_inverse()),
        MatrixVP => mat(cam.map_or_else(|| mul(&v.gpu_proj, &v.view), |c| c.matrix_vp)),
        Projection => mat(v.gpu_proj),
        // The camera's own projection and its inverse stay in the API's convention.
        CameraProjection => mat(v.proj),
        InvProjection => mat(cam.map_or_else(|| inverse(&v.proj), |c| c.proj_inv)),
        WorldToCamera => {
            mat(cam.map_or_else(|| mul(&flip_z, &v.camera_view), |c| c.world_to_camera))
        }
        CameraToWorld => mat(cam.map_or_else(
            || inverse(&mul(&flip_z, &v.camera_view)),
            |c| c.camera_to_world,
        )),
        TransposeModelView => mat(transpose(&mul(&v.view, &v.model))),
        InvTransModelView => mat(if v.gizmo_draw {
            let vit = cam.map_or_else(|| transpose(&inverse(&v.view)), |c| c.view_inv_t);
            mul(&vit, &v.model)
        } else {
            transpose(&inverse(&mul(&v.view, &v.model)))
        }),
        ModelView => mat(mul(&v.view, &v.model)),
        Time => vec(cam.map_or_else(|| time_vector(t), |c| c.time_vec)),
        SinTime => vec(cam.map_or_else(|| sin_time_vector(t), |c| c.sin_time)),
        CosTime => vec(cam.map_or_else(|| cos_time_vector(t), |c| c.cos_time)),
        DeltaTime => vec([1.0 / 60.0, 60.0, 1.0 / 60.0, 60.0]),
        CameraPos => vec(cam.map_or_else(
            || {
                let i = camera_view_inverse();
                [i[12], i[13], i[14], 1.0]
            },
            |c| c.camera_pos,
        )),
        ProjectionParams => vec(cam.map_or_else(
            || {
                let (near, far, _) = clip_planes(&v.camera_proj);
                [if v.flipped { -1.0 } else { 1.0 }, near, far, 1.0 / far]
            },
            |c| c.projection_params,
        )),
        ScreenParams => vec(cam.map_or_else(
            || {
                let [w, h] = v.screen;
                [w, h, 1.0 + 1.0 / w, 1.0 + 1.0 / h]
            },
            |c| c.screen_params,
        )),
        ZBufferParams => vec(cam.map_or_else(
            || {
                let (near, far, _) = clip_planes(&v.camera_proj);
                // Reversed depth: x = -1 + far/near, y = 1 (Unity's built-in shader variables).
                let (x, y) = if v.reversed_z {
                    (far / near - 1.0, 1.0)
                } else {
                    (1.0 - far / near, far / near)
                };
                [x, y, x / far, y / far]
            },
            |c| c.zbuffer_params,
        )),
        OrthoParams => vec(cam.map_or_else(
            || {
                let (_, _, ortho) = clip_planes(&v.camera_proj);
                [
                    1.0 / v.camera_proj[0],
                    1.0 / v.camera_proj[5],
                    0.0,
                    if ortho { 1.0 } else { 0.0 },
                ]
            },
            |c| c.ortho_params,
        )),
        WorldTransformParams => vec([0.0, 0.0, 0.0, 1.0]),
        LodFade => vec([1.0, 1.0, 0.0, 0.0]),
        // Per renderer, as Unity fills it for scriptable pipelines: no per-object additional
        // lights listed, the main light applies (z), no baked occlusion, rendering layer 1.
        LightData => vec([0.0, 0.0, 1.0, 0.0]),
        ProbesOcclusion => vec([1.0, 1.0, 1.0, 1.0]),
        RenderingLayer => vec([f32::from_bits(1), 0.0, 0.0, 0.0]),
    }
}

/// A uniform no built-in variable fills: the draw's or material's property, the shader's default,
/// a global property, or what Unity derives from a texture's name.
fn named_value(name: &str, v: &DrawValues, textures: &[String]) -> Option<Value> {
    let vec = |a: [f32; 4]| Some(Value::Vector(a));
    let mat = |m: Mat| Some(Value::Matrix(m));
    let found = v.get(name);
    if let Some(PropertyValue::Matrix(m)) = found {
        return mat(*m);
    }
    if let Some(value) = property_of(found, v.lab, name, v.linear) {
        return vec(value);
    }
    global_value(v.globals_index.find(v.globals, name)).or_else(|| derived_value(name, v, textures))
}

/// A global property's value as a uniform's: a number, vector or matrix.
fn global_value(found: Option<&PropertyValue>) -> Option<Value> {
    match found? {
        PropertyValue::Float(f) => Some(Value::Vector([*f; 4])),
        PropertyValue::Int(i) => Some(Value::Vector([*i as f32; 4])),
        PropertyValue::Vector(x) => Some(Value::Vector(*x)),
        PropertyValue::Matrix(m) => Some(Value::Matrix(*m)),
        PropertyValue::Texture { .. }
        | PropertyValue::Buffer(_)
        | PropertyValue::Array { .. }
        | PropertyValue::Image { .. } => None,
    }
}

/// What a uniform no property gives takes from its name: a texture's scale and offset or size,
/// or Unity's neutral values.
fn derived_value(name: &str, v: &DrawValues, textures: &[String]) -> Option<Value> {
    let vec = |a: [f32; 4]| Some(Value::Vector(a));
    if let Some(texture) = name.strip_suffix("_ST") {
        return vec(texture_st(v, texture));
    }
    if let Some(texture) = name.strip_suffix("_TexelSize") {
        // A texture nothing binds and the program doesn't sample has no size: Unity
        // leaves the vector zero (URP's UberPost reads `_BloomTexture_TexelSize`, which
        // nothing sets, to clamp nothing).
        let size = (v.texture_size)(texture).or_else(|| {
            (textures.iter().any(|t| t == texture) || v.get(texture).is_some()).then_some((4, 4))
        })?;
        let (w, h) = size;
        let (w, h) = (w.max(1) as f32, h.max(1) as f32);
        return vec([1.0 / w, 1.0 / h, w, h]);
    }
    if name.ends_with("_HDR") {
        return vec([1.0, 1.0, 0.0, 0.0]);
    }
    match name {
        // No RectMask2D clip: everything inside (clipping is the draw's scissor). Unity's
        // rect is unbounded (its UI shaders clamp it to ±2e10): shaders that clip
        // unconditionally (Spine's SkeletonGraphic) test object-space positions, which
        // can lie far past ±32767.
        "_ClipRect" => vec([-2e10, -2e10, 2e10, 2e10]),
        // Alpha-only textures (UI text) sample white with the texture's alpha.
        "_TextureSampleAdd" if v.main_alpha_only => vec([1.0, 1.0, 1.0, 0.0]),
        // A sprite's renderer color, which URP's sprite shaders apply in the vertex program
        // (the draw's own renderer color, white for other draws).
        SPRITE_COLOR => vec(v.sprite_color),
        // A sprite's flips, which URP's sprite shaders apply in the vertex program (positions
        // scaled by them): Unbound's sprite meshes carry them already, so they are neutral (no
        // skinning offset: z -1).
        "unity_SpriteProps" => vec([1.0, 1.0, -1.0, 0.0]),
        _ => None,
    }
}

static NEXT_MATERIAL_BLOCK_ID: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);

/// One dynamic uniform entry within a binding of a [`MaterialBlock`].
struct DynamicUniform {
    index: u32,
    local_offset: u32,
    shared: Shared,
    global_slot: GlobalSlot,
}

/// A program's uniform block as a material fills it (see [`CompiledPrograms::material_block`]).
pub struct MaterialBlock {
    /// Monotonic unique identifier for cache keying material-dependent bindings without ABA reuse.
    id: u64,
    /// Static material/default bytes per uniform binding; empty when that binding has no static
    /// values (avoids keeping large zeroed copies of Forward+ constant-buffer bindings per
    /// material).
    binding_bytes: Vec<Vec<u8>>,
    /// Per-binding list of dynamic uniforms (built-in variables, global properties, derived values)
    /// so shared/cached bindings are skipped without iterating their uniforms.
    binding_dynamic: Vec<Vec<DynamicUniform>>,
    /// By uniform index: whether it is dynamic (filled at draw time).
    is_dynamic: Vec<bool>,
    /// Per binding: true if this material contributes static values, material arrays, or declared
    /// material texture `_ST`/`_TexelSize` properties to the binding (requires keying by `id`).
    binding_material_dependent: Vec<bool>,
}

/// Where a uniform the draw fills takes its value from, its own properties left out.
#[derive(Clone, Copy, Debug)]
enum Shared {
    /// A built-in variable (the material has no number, vector or matrix of its name).
    Builtin(Builtin),
    /// The global property of its name, else what the name derives (the material and the
    /// shader give it nothing).
    Global,
    /// The global property of its name only (name cannot match [`derived_value`]).
    GlobalOnly,
    /// Decided per draw (`_GizmoBatchColor`: the gizmo's colour or a property).
    Draw,
}

/// Whether [`derived_value`] can ever produce a value for `name`.
fn may_derive(name: &str) -> bool {
    name.ends_with("_ST")
        || name.ends_with("_TexelSize")
        || name.ends_with("_HDR")
        || matches!(
            name,
            "_ClipRect" | "_TextureSampleAdd" | SPRITE_COLOR | "unity_SpriteProps"
        )
}

/// Where a global property is among the globals, learnt once per uniform: global properties are
/// only ever added or set in place, so a found position keeps the property (checked by name);
/// an absence holds while the list keeps its length. Packed as the list's length at the last
/// lookup (high half) and the position plus one, or zero when absent (low half).
#[derive(Default)]
struct GlobalSlot(std::sync::atomic::AtomicU64);

impl GlobalSlot {
    fn find<'g>(
        &self,
        globals: &'g [(String, PropertyValue)],
        index: &unbound_runtime::render_protocol::owned::NameIndex,
        name: &str,
    ) -> Option<&'g PropertyValue> {
        use std::sync::atomic::Ordering::Relaxed;
        let packed = self.0.load(Relaxed);
        let (length, at) = ((packed >> 32) as usize, (packed & 0xffff_ffff) as usize);
        if at > 0 {
            if let Some((n, value)) = globals.get(at - 1)
                && n == name
            {
                return Some(value);
            }
        } else if length == globals.len() {
            return None;
        }
        let found = index.find_position(globals, name);
        let at = found.map_or(0, |i| i as u64 + 1);
        self.0
            .store(((globals.len() as u64) << 32) | (at & 0xffff_ffff), Relaxed);
        found.map(|i| &globals[i].1)
    }
}

/// A uniform's value when the material alone decides it (the same answer `value` gives for a draw
/// without properties of its own); `None` when the draw does.
fn static_value(
    name: &str,
    source: Source,
    material: &Material,
    lab: &ShaderLab,
    linear: bool,
) -> Option<Value> {
    let found = material.get(name);
    match source {
        Source::Builtin {
            properties_first: true,
            ..
        } => match found {
            Some(PropertyValue::Vector(x)) => Some(Value::Vector(*x)),
            Some(PropertyValue::Float(f)) => Some(Value::Vector([*f; 4])),
            Some(PropertyValue::Matrix(m)) => Some(Value::Matrix(*m)),
            _ => None,
        },
        Source::Builtin { .. } => None,
        Source::Named => match found {
            Some(PropertyValue::Matrix(m)) => Some(Value::Matrix(*m)),
            Some(PropertyValue::Float(_) | PropertyValue::Int(_) | PropertyValue::Vector(_))
            | None => property_of(found, lab, name, linear).map(Value::Vector),
            Some(_) => None,
        },
    }
}

/// Writes one uniform (not an array) for a draw. In a mesh instance (`DrawValues::instance`),
/// a scalar uniform whose draw property is an array takes the instance's element of it, or the
/// material's value past the array's end; a declared instanced property's single value reaches
/// instance zero only, the others keep the material's. Outside one, a declared instanced
/// property's array keeps the material's value.
#[cfg(test)]
fn write_uniform(program: &Compiled, plan: &[Source], i: usize, v: &DrawValues, out: &mut [u8]) {
    write_uniform_at(
        program,
        plan,
        i,
        program.uniforms[i].offset as usize,
        v,
        out,
    );
}

fn write_uniform_at(
    program: &Compiled,
    plan: &[Source],
    i: usize,
    offset: usize,
    v: &DrawValues,
    out: &mut [u8],
) {
    let u = &program.uniforms[i];
    if u.count != 1 || u.stride > 0 {
        return;
    }
    let instanced = || program.instanced_properties.contains(&u.name);
    if v.instance_empty
        && (instanced() || matches!(v.override_of(&u.name), Some(PropertyValue::Array { .. })))
    {
        write_value_at(u, offset, &Value::Matrix([0.0; 16]), out);
        return;
    }
    let element;
    let material_only = match (v.instance, v.override_of(&u.name)) {
        (Some(index), Some(PropertyValue::Array { components, data })) => {
            element = array_element(*components, data, index);
            true
        }
        (Some(index), Some(_)) => {
            element = None;
            index > 0 && instanced()
        }
        (None, Some(PropertyValue::Array { .. })) => {
            element = None;
            instanced()
        }
        _ => {
            element = None;
            false
        }
    };
    // The material's values with the selected element, made only for the uniforms that take
    // them: a copy of the draw's values per uniform would cost more than the uniform itself.
    let value = if material_only {
        let selected = DrawValues {
            overrides: &[],
            element: element.as_ref().map(|e| (u.name.as_str(), e)),
            ..*v
        };
        value(&u.name, plan[i], &selected, &program.textures)
    } else {
        value(&u.name, plan[i], v, &program.textures)
    };
    if let Some(value) = value {
        write_value_at(u, offset, &value, out);
    }
}

/// A value's components at `offset`: a matrix's columns are 8 bytes apart with 2 rows, 16
/// otherwise; values are 4x4.
fn write_value_at(u: &unbound_shaders::Uniform, offset: usize, value: &Value, out: &mut [u8]) {
    let values = value.as_slice();
    let column_stride = if u.rows == 2 { 8 } else { 16 };
    for c in 0..u.columns as usize {
        for r in 0..u.rows as usize {
            let x = values.get(c * 4 + r).copied().unwrap_or(0.0);
            let at = offset + c * column_stride + r * 4;
            if at + 4 > out.len() {
                continue;
            }
            let bytes = match u.scalar {
                Scalar::Float => x.to_le_bytes(),
                Scalar::Int => (x as i32).to_le_bytes(),
                Scalar::Uint => (x as u32).to_le_bytes(),
            };
            out[at..at + 4].copy_from_slice(&bytes);
        }
    }
}

/// Byte size of one uniform binding's `DRAW_SLOT`-aligned allocation in `CompiledPrograms::data`.
#[inline]
fn binding_alloc_size(size: u32) -> usize {
    ((size.max(16) as u64).div_ceil(DRAW_SLOT) * DRAW_SLOT) as usize
}

/// A program's uniform block as a material fills it (see [`CompiledPrograms::material_block`]).
fn material_block(
    program: &Compiled,
    plan: &[Source],
    layout: &ProgramBindings,
    material: &Material,
    lab: &ShaderLab,
    linear: bool,
) -> MaterialBlock {
    let n_bindings = layout.bindings.len();
    let mut binding_bytes: Vec<Vec<u8>> = vec![Vec::new(); n_bindings];
    let mut binding_dynamic: Vec<Vec<DynamicUniform>> =
        (0..n_bindings).map(|_| Vec::new()).collect();
    let mut is_dynamic = vec![false; program.uniforms.len()];
    for (i, (u, source)) in program.uniforms.iter().zip(plan).enumerate() {
        if u.unread || u.count != 1 || u.stride > 0 {
            continue;
        }
        let k = layout.uniform_binding[i] as usize;
        let local = layout.uniform_local_offset[i];
        match static_value(&u.name, *source, material, lab, linear) {
            Some(value) => {
                if let Some(bytes) = binding_bytes.get_mut(k) {
                    if bytes.is_empty() {
                        // Pre-pad to `DRAW_SLOT` so draw-time packing copies with one
                        // `extend_from_slice` instead of zero-filling then overwriting.
                        bytes.resize(binding_alloc_size(layout.bindings[k].size), 0);
                    }
                    let size = layout.bindings[k].size as usize;
                    write_value_at(u, local as usize, &value, &mut bytes[..size]);
                }
            }
            None => {
                is_dynamic[i] = true;
                // Without a value of the material's (`static_value` found none), what `value`
                // goes on to.
                let shared = match *source {
                    Source::Builtin {
                        which: Builtin::GizmoColor,
                        ..
                    } => Shared::Draw,
                    Source::Builtin { which, .. } => Shared::Builtin(which),
                    Source::Named => {
                        if may_derive(&u.name) {
                            Shared::Global
                        } else {
                            Shared::GlobalOnly
                        }
                    }
                };
                if let Some(list) = binding_dynamic.get_mut(k) {
                    list.push(DynamicUniform {
                        index: i as u32,
                        local_offset: local,
                        shared,
                        global_slot: GlobalSlot::default(),
                    });
                }
            }
        }
    }
    let mut binding_material_dependent = Vec::with_capacity(n_bindings);
    for (k, bp) in layout.bindings.iter().enumerate() {
        let has_static = !binding_bytes[k].is_empty();
        let has_material_array = bp.arrays.iter().any(|&i| {
            matches!(
                material.get(&program.uniforms[i as usize].name),
                Some(PropertyValue::Array { .. })
            )
        });
        let has_material_tex = bp
            .tex_derived_bases
            .iter()
            .any(|tex| lab.property(tex).is_some() || material.get(tex).is_some());
        binding_material_dependent.push(has_static || has_material_array || has_material_tex);
    }
    MaterialBlock {
        id: NEXT_MATERIAL_BLOCK_ID.fetch_add(1, std::sync::atomic::Ordering::Relaxed),
        binding_bytes,
        binding_dynamic,
        is_dynamic,
        binding_material_dependent,
    }
}

/// Allocates and fills one draw's uniform bindings in `data`. Standalone cbuffer bindings covered
/// completely by a bound constant buffer (with no draw overrides targeting their uniforms) are
/// shared across all programs by `(handle, revision, size)`, and non-per-draw bindings without
/// draw overrides are cached per `(program, binding, camera, globals, textures, material, cbuffers)`
/// so pass-wide globals and arrays are packed once instead of every draw.
#[allow(clippy::too_many_arguments)]
fn push_program_uniforms(
    program_id: u32,
    program: &Compiled,
    plan: &[Source],
    by_name: &rustc_hash::FxHashMap<String, u32>,
    layout: &ProgramBindings,
    block: Option<&MaterialBlock>,
    values: &DrawValues,
    cam: &CachedCamera,
    data: &mut Vec<u8>,
    shared_constants: &mut HashMap<(u64, u64, u32), u32>,
    shared_bindings: &mut HashMap<SharedBindingKey, u32>,
    empty_slot: &mut Option<u32>,
    name: &mut String,
    override_scratch: &mut Vec<(u32, OverrideTarget)>,
) -> DrawOffsets {
    use std::hash::{Hash, Hasher};

    let mut offsets = [0u32; MAX_UNIFORM_BINDINGS];
    if layout.bindings.is_empty() {
        let slot = *empty_slot.get_or_insert_with(|| {
            let at = data.len() as u32;
            data.resize(data.len() + DRAW_SLOT as usize, 0);
            at
        });
        offsets[0] = slot;
        return DrawOffsets { offsets, count: 0 };
    }

    // Resolve all taken draw overrides against `layout.override_targets` in one pass so later
    // stages avoid re-hashing every override name.
    override_scratch.clear();
    let mut override_mask = 0u8;
    let mut simple_overrides =
        values.instance.is_none() && !values.instance_empty && values.element.is_none();
    let mut any_non_tex_derived = false;
    if block.is_some() && !values.overrides.is_empty() {
        let hidden = values.hidden;
        for (idx, (property, value)) in values.overrides.iter().enumerate() {
            if hidden == Some(property.as_str()) {
                continue;
            }
            if let Some(&target) = layout.override_targets.get(property.as_str()) {
                override_mask |= target.binding_mask;
                if !matches!(
                    value,
                    PropertyValue::Float(_)
                        | PropertyValue::Int(_)
                        | PropertyValue::Vector(_)
                        | PropertyValue::Matrix(_)
                ) {
                    simple_overrides = false;
                }
                if target.is_tex_derived_base && !matches!(value, PropertyValue::Texture { .. }) {
                    any_non_tex_derived = true;
                }
                override_scratch.push((idx as u32, target));
            }
        }
    }

    let can_cache_draw = block.is_some()
        && values.instance.is_none()
        && !values.instance_empty
        && values.element.is_none();

    let mut shared_mask = 0u8;
    let mut pending_cache: [(SharedBindingKey, u32); MAX_UNIFORM_BINDINGS] = [(
        SharedBindingKey {
            program_id: 0,
            binding: 0,
            camera_id: 0,
            globals_revision: 0,
            textures_generation: 0,
            main_texture_handle: 0,
            material_key: 0,
            cbuffers_hash: 0,
        },
        0,
    );
        MAX_UNIFORM_BINDINGS];
    let mut pending_count = 0usize;

    for (k, bp) in layout.bindings.iter().enumerate() {
        let bit = if k < MAX_UNIFORM_BINDINGS {
            1u8 << k
        } else {
            0
        };
        let alloc = binding_alloc_size(bp.size);

        // Fast path 1: standalone bound constant buffer (shared across programs by buffer revision).
        if (override_mask & bit) == 0
            && let Some(ref block_name) = bp.standalone_block
            && let Some(cb) = (values.constant_buffer)(block_name)
            && cb.bytes.len() >= bp.size as usize
        {
            shared_mask |= bit;
            let key = (cb.handle, cb.revision, bp.size);
            offsets[k] = *shared_constants.entry(key).or_insert_with(|| {
                let at = data.len();
                let n = bp.size as usize;
                data.extend_from_slice(&cb.bytes[..n]);
                if alloc > n {
                    data.resize(at + alloc, 0);
                }
                at as u32
            });
            continue;
        }

        // Fast path 2: general non-per-draw binding cache (pass-wide globals, light/probe arrays).
        if can_cache_draw
            && !bp.has_per_draw_builtin
            && (override_mask & bit) == 0
            && let Some(block) = block
        {
            let mut cbuffers_hash = 0u64;
            if !bp.blocks.is_empty() {
                let mut hasher: Option<rustc_hash::FxHasher> = None;
                for &bi in &bp.blocks {
                    let b = &program.blocks[bi as usize];
                    if let Some(cb) = (values.constant_buffer)(&b.name) {
                        let h = hasher.get_or_insert_with(Default::default);
                        bi.hash(h);
                        cb.handle.hash(h);
                        cb.revision.hash(h);
                        cb.bytes.len().hash(h);
                    }
                }
                if let Some(h) = hasher {
                    cbuffers_hash = h.finish() | 1;
                }
            }
            let key = SharedBindingKey {
                program_id,
                binding: k as u8,
                camera_id: if bp.has_camera_builtin { cam.id } else { 0 },
                globals_revision: values.globals_revision,
                textures_generation: if bp.has_texel_size {
                    values.textures_generation
                } else {
                    0
                },
                main_texture_handle: if bp.has_main_tex_derived {
                    values.main_texture_handle
                } else {
                    0
                },
                material_key: if block.binding_material_dependent[k] {
                    block.id
                } else {
                    0
                },
                cbuffers_hash,
            };
            if let Some(&cached_at) = shared_bindings.get(&key) {
                offsets[k] = cached_at;
                shared_mask |= bit;
                continue;
            }
            let at = data.len();
            if let Some(src) = block.binding_bytes.get(k)
                && !src.is_empty()
            {
                debug_assert_eq!(src.len(), alloc);
                data.extend_from_slice(src);
            } else {
                data.resize(at + alloc, 0);
            }
            offsets[k] = at as u32;
            if pending_count < MAX_UNIFORM_BINDINGS {
                pending_cache[pending_count] = (key, at as u32);
                pending_count += 1;
            }
            continue;
        }

        let at = data.len();
        if let Some(block) = block
            && let Some(src) = block.binding_bytes.get(k)
            && !src.is_empty()
        {
            debug_assert_eq!(src.len(), alloc);
            data.extend_from_slice(src);
        } else {
            data.resize(at + alloc, 0);
        }
        offsets[k] = at as u32;
    }

    let binding_range = |k: usize| -> std::ops::Range<usize> {
        let start = offsets[k] as usize;
        let len = layout.bindings[k].size as usize;
        start..start + len
    };

    // Arrays in non-shared bindings.
    for (k, bp) in layout.bindings.iter().enumerate() {
        if shared_mask & (1 << k) != 0 || bp.arrays.is_empty() {
            continue;
        }
        let out = &mut data[binding_range(k)];
        for &i in &bp.arrays {
            let u = &program.uniforms[i as usize];
            let value = match values.get(&u.name) {
                Some(value @ PropertyValue::Array { .. }) => Some(value),
                _ => values
                    .globals_index
                    .find(values.globals, &u.name)
                    .filter(|value| matches!(value, PropertyValue::Array { .. })),
            };
            if let Some(PropertyValue::Array { components, data }) = value {
                write_array_at(
                    u,
                    layout.uniform_local_offset[i as usize] as usize,
                    *components,
                    data,
                    out,
                );
            }
        }
    }

    match block {
        Some(block) => {
            // The uniforms the draw fills, from everything but its own properties: a uniform takes
            // the draw's properties only by its own name (or, for `_ST` and `_TexelSize`, its
            // texture's), so the overrides below give each the value it would get from all of
            // them, and the others need no search through the draw's list.
            let shared = DrawValues {
                overrides: &[],
                ..*values
            };
            for (k, dynamics) in block.binding_dynamic.iter().enumerate() {
                if shared_mask & (1 << k) != 0 || dynamics.is_empty() {
                    continue;
                }
                let out = &mut data[binding_range(k)];
                for entry in dynamics {
                    let i = entry.index as usize;
                    let u = &program.uniforms[i];
                    let local = entry.local_offset as usize;
                    // What `write_uniform` gives without the draw's properties, past the material's.
                    let value = match entry.shared {
                        Shared::Builtin(which) => {
                            builtin_value(which, &u.name, &shared, &program.textures, Some(cam))
                        }
                        Shared::Global => global_value(entry.global_slot.find(
                            values.globals,
                            values.globals_index,
                            &u.name,
                        ))
                        .or_else(|| derived_value(&u.name, &shared, &program.textures)),
                        Shared::GlobalOnly => global_value(entry.global_slot.find(
                            values.globals,
                            values.globals_index,
                            &u.name,
                        )),
                        Shared::Draw => {
                            write_uniform_at(program, plan, i, local, &shared, out);
                            continue;
                        }
                    };
                    if let Some(value) = value {
                        write_value_at(u, local, &value, out);
                    }
                }
            }
            // The draw's own properties over the material's, written when a full fill writes them:
            // the `_ST` / `_TexelSize` the draw fills of an override that is no texture (they
            // derive from the texture property's name) before the bound constant buffers; each
            // uniform of an override's name, and the `_ST` / `_TexelSize` of a texture override,
            // after them.
            let mut redo_suffix = |data: &mut [u8], property: &str, suffix: &str, texture: bool| {
                name.clear();
                name.push_str(property);
                name.push_str(suffix);
                if let Some(&i) = by_name.get(name.as_str())
                    && !program.uniforms[i as usize].unread
                    && (texture || block.is_dynamic[i as usize])
                {
                    let k = layout.uniform_binding[i as usize] as usize;
                    if shared_mask & (1 << k) == 0 {
                        let local = layout.uniform_local_offset[i as usize] as usize;
                        let out = &mut data[binding_range(k)];
                        write_uniform_at(program, plan, i as usize, local, values, out);
                    }
                }
            };
            if any_non_tex_derived {
                for &(idx, target) in override_scratch.iter() {
                    if target.is_tex_derived_base {
                        let (property, value) = &values.overrides[idx as usize];
                        if !matches!(value, PropertyValue::Texture { .. }) {
                            for suffix in TEXTURE_SUFFIXES {
                                redo_suffix(data, property, suffix, false);
                            }
                        }
                    }
                }
            }
            for (k, bp) in layout.bindings.iter().enumerate() {
                if shared_mask & (1 << k) != 0 {
                    continue;
                }
                let out = &mut data[binding_range(k)];
                for &bi in &bp.blocks {
                    let b = &program.blocks[bi as usize];
                    let Some(cb) = (values.constant_buffer)(&b.name) else {
                        continue;
                    };
                    let start = b.offset.saturating_sub(bp.offset) as usize;
                    let end = (start + b.size as usize).min(out.len());
                    if end > start {
                        let n = (end - start).min(cb.bytes.len());
                        out[start..start + n].copy_from_slice(&cb.bytes[..n]);
                    }
                }
            }
            if !override_scratch.is_empty() {
                if simple_overrides {
                    // Every matching taken override is a plain scalar/vector/matrix and this draw
                    // is not instanced: forward writes overwrite earlier duplicates identically to
                    // `override_of` without O(N^2) reverse scans or duplicate hash lookups.
                    for &(idx, target) in override_scratch.iter() {
                        if target.direct_uniform == u32::MAX {
                            continue;
                        }
                        let i = target.direct_uniform as usize;
                        let k = layout.uniform_binding[i] as usize;
                        if shared_mask & (1 << k) != 0 {
                            continue;
                        }
                        let u = &program.uniforms[i];
                        let local = layout.uniform_local_offset[i] as usize;
                        let out = &mut data[binding_range(k)];
                        let value = &values.overrides[idx as usize].1;
                        let direct = match (plan[i], value) {
                            (Source::Named, PropertyValue::Float(f)) => {
                                Some(Value::Vector([*f; 4]))
                            }
                            (Source::Named, PropertyValue::Int(v)) => {
                                Some(Value::Vector([*v as f32; 4]))
                            }
                            (Source::Named, PropertyValue::Vector(v)) => Some(Value::Vector(*v)),
                            (Source::Named, PropertyValue::Matrix(m)) => Some(Value::Matrix(*m)),
                            (
                                Source::Builtin {
                                    properties_first: true,
                                    ..
                                },
                                PropertyValue::Float(f),
                            ) => Some(Value::Vector([*f; 4])),
                            (
                                Source::Builtin {
                                    properties_first: true,
                                    ..
                                },
                                PropertyValue::Vector(v),
                            ) => Some(Value::Vector(*v)),
                            (
                                Source::Builtin {
                                    properties_first: true,
                                    ..
                                },
                                PropertyValue::Matrix(m),
                            ) => Some(Value::Matrix(*m)),
                            _ => None,
                        };
                        if let Some(val) = direct {
                            write_value_at(u, local, &val, out);
                        } else {
                            write_uniform_at(program, plan, i, local, values, out);
                        }
                    }
                } else {
                    for &(idx, target) in override_scratch.iter() {
                        if target.direct_uniform != u32::MAX {
                            let i = target.direct_uniform as usize;
                            let k = layout.uniform_binding[i] as usize;
                            if shared_mask & (1 << k) == 0 {
                                let local = layout.uniform_local_offset[i] as usize;
                                let out = &mut data[binding_range(k)];
                                write_uniform_at(program, plan, i, local, values, out);
                            }
                        }
                        if target.is_tex_derived_base {
                            let (property, value) = &values.overrides[idx as usize];
                            if matches!(value, PropertyValue::Texture { .. }) {
                                for suffix in TEXTURE_SUFFIXES {
                                    redo_suffix(data, property, suffix, true);
                                }
                            }
                        }
                    }
                }
            }
        }
        None => {
            for i in 0..program.uniforms.len() {
                let k = layout.uniform_binding[i] as usize;
                if shared_mask & (1 << k) != 0 {
                    continue;
                }
                let local = layout.uniform_local_offset[i] as usize;
                let out = &mut data[binding_range(k)];
                write_uniform_at(program, plan, i, local, values, out);
            }
            for (k, bp) in layout.bindings.iter().enumerate() {
                if shared_mask & (1 << k) != 0 {
                    continue;
                }
                let out = &mut data[binding_range(k)];
                for &bi in &bp.blocks {
                    let b = &program.blocks[bi as usize];
                    let Some(cb) = (values.constant_buffer)(&b.name) else {
                        continue;
                    };
                    let start = b.offset.saturating_sub(bp.offset) as usize;
                    let end = (start + b.size as usize).min(out.len());
                    if end > start {
                        let n = (end - start).min(cb.bytes.len());
                        out[start..start + n].copy_from_slice(&cb.bytes[..n]);
                    }
                }
            }
        }
    }

    for &(key, at) in &pending_cache[..pending_count] {
        shared_bindings.insert(key, at);
    }

    DrawOffsets {
        offsets,
        count: layout.bindings.len() as u8,
    }
}

/// The uniforms a texture property's name gives: its scale and offset, and its size.
const TEXTURE_SUFFIXES: [&str; 2] = ["_ST", "_TexelSize"];

/// Writes a program's uniform block for a draw into one contiguous buffer (tests).
#[cfg(test)]
fn fill(program: &Compiled, plan: &[Source], v: &DrawValues, out: &mut [u8]) {
    fill_arrays(program, v, out);
    for i in 0..program.uniforms.len() {
        write_uniform(program, plan, i, v, out);
    }
    fill_constant_buffers(program, v, out);
}

/// Arrays: the draw's or material's array property, else the global one.
#[cfg(test)]
fn fill_arrays(program: &Compiled, v: &DrawValues, out: &mut [u8]) {
    for u in program.uniforms.iter().filter(|u| u.stride > 0) {
        let value = match v.get(&u.name) {
            Some(value @ PropertyValue::Array { .. }) => Some(value),
            _ => v
                .globals_index
                .find(v.globals, &u.name)
                .filter(|value| matches!(value, PropertyValue::Array { .. })),
        };
        if let Some(PropertyValue::Array { components, data }) = value {
            write_array_at(u, u.offset as usize, *components, data, out);
        }
    }
}

/// A constant buffer bound to a cbuffer's name fills the cbuffer's range.
#[cfg(test)]
fn fill_constant_buffers(program: &Compiled, v: &DrawValues, out: &mut [u8]) {
    for block in &program.blocks {
        let Some(cb) = (v.constant_buffer)(&block.name) else {
            continue;
        };
        let start = block.offset as usize;
        let end = (start + block.size as usize).min(out.len());
        if end > start {
            let n = (end - start).min(cb.bytes.len());
            out[start..start + n].copy_from_slice(&cb.bytes[..n]);
        }
    }
}

/// An array uniform's elements: `components` floats each (0: consecutive values from `offset`),
/// as many as the array holds.
fn write_array_at(
    u: &unbound_shaders::Uniform,
    offset: usize,
    components: u32,
    data: &[f32],
    out: &mut [u8],
) {
    let mut put = |at: usize, x: f32| {
        if at + 4 > out.len() {
            return;
        }
        let bytes = match u.scalar {
            Scalar::Float => x.to_le_bytes(),
            Scalar::Int => (x as i32).to_le_bytes(),
            Scalar::Uint => (x as u32).to_le_bytes(),
        };
        out[at..at + 4].copy_from_slice(&bytes);
    };
    if components == 0 {
        for (i, x) in data.iter().enumerate() {
            put(offset + i * 4, *x);
        }
        return;
    }
    let n = components as usize;
    let (rows, columns) = (u.rows.max(1) as usize, u.columns.max(1) as usize);
    let count = (u.count as usize).min(data.len() / n);
    for i in 0..count {
        let element = &data[i * n..(i + 1) * n];
        let base = offset + i * u.stride as usize;
        for c in 0..columns {
            for r in 0..rows {
                if let Some(x) = element.get(c * 4 + r) {
                    put(base + c * 16 + r * 4, *x);
                }
            }
        }
    }
}

/// Whether a program compiled for the shader `compiled` counts for the shader a diagnostic
/// query names: every shader for an empty query, else that shader, by the name it was compiled
/// under or, for a project file known by its content key, by the name the file declares (which
/// is what a script's `Shader.name` gives).
fn counted_for(compiled: &str, queried: &str) -> bool {
    queried.is_empty()
        || compiled == queried
        || crate::shaderlab::generated_base(compiled) == Some(queried)
}

#[cfg(test)]
mod tests {
    use super::*;
    use unbound_shaders::Uniform;

    #[test]
    fn prepared_counts_answer_to_a_shader_file_by_its_declared_name() {
        let keyed = "Probe/Warmup#0123456789abcdef";
        assert!(counted_for(keyed, "Probe/Warmup"));
        assert!(counted_for(keyed, keyed));
        assert!(counted_for("Probe/Warmup", "Probe/Warmup"));
        assert!(counted_for(keyed, ""));
        // Another shader, another file of the name asked by its own key, or a name that only
        // starts alike, is not counted.
        assert!(!counted_for("Probe/Other#0123456789abcdef", "Probe/Warmup"));
        assert!(!counted_for(keyed, "Probe/Warmup#fedcba9876543210"));
        assert!(!counted_for(
            "Probe/WarmupFallback#0123456789abcdef",
            "Probe/Warmup"
        ));
    }

    /// The per-draw dynamic offsets in binding order: the first uniform binding, the per-draw
    /// uniform, then the further ones, each at its start in the draw's uniform data.
    #[test]
    fn dynamic_offsets_follow_the_binding_order() {
        assert_eq!(
            dynamic_offsets(&[], 768),
            ([0, 768, 0, 0, 0, 0, 0, 0, 0], 2)
        );
        let (offsets, count) = dynamic_offsets(&[4096, 5120, 70656], 256);
        assert_eq!(&offsets[..count], &[4096, 256, 5120, 70656]);
    }

    /// A program the device cannot bind is refused with its cbuffers named: one binding over the
    /// device's uniform buffer size, or more bindings than it binds beside the per-draw one.
    #[test]
    fn uniform_bindings_over_the_device_limits_are_refused_by_name() {
        let block = |name: &str, offset, size| unbound_shaders::BlockRange {
            name: name.into(),
            offset,
            size,
        };
        let binding = |offset, size| unbound_shaders::UniformBinding { offset, size };
        let mut program = Compiled {
            vertex: String::new(),
            fragment: String::new(),
            vertex_entry: String::new(),
            fragment_entry: String::new(),
            uniforms: Vec::new(),
            instanced_properties: Vec::new(),
            mesh_instance_stride: Some(0),
            mesh_instance_uniform_scaling: false,
            mesh_instance_max_count: None,
            uniform_size: 1024 + 65536 + 16384,
            uniform_bindings: vec![
                binding(0, 1024),
                binding(1024, 65536),
                binding(66560, 16384),
            ],
            textures: Vec::new(),
            texture_dims: Vec::new(),
            main_texture: false,
            keywords: Vec::new(),
            texture_slots: Vec::new(),
            texture_aliases: Vec::new(),
            blocks: vec![
                block("UnityPerDraw", 0, 1024),
                block("urp_TileBuffer", 1024, 65536),
                block("urp_ZBinBuffer", 66560, 8192),
                block("AdditionalLights", 74752, 8192),
            ],
            buffers: Vec::new(),
            extra_vertex_inputs: false,
            vertex_inputs: true,
            framebuffer_fetch: 0,
        };
        let desktop = wgpu::Limits {
            max_uniform_buffer_binding_size: 65536,
            max_uniform_buffers_per_shader_stage: 14,
            max_dynamic_uniform_buffers_per_pipeline_layout: 15,
            ..wgpu::Limits::default()
        };
        assert_eq!(uniform_bindings_refused(&program, &desktop), None);
        let small = wgpu::Limits {
            max_uniform_buffer_binding_size: 16384,
            ..desktop.clone()
        };
        let why = uniform_bindings_refused(&program, &small).unwrap();
        assert!(why.contains("`urp_TileBuffer` takes 65536 bytes"), "{why}");
        assert!(why.contains("16384"), "{why}");
        let few = wgpu::Limits {
            max_dynamic_uniform_buffers_per_pipeline_layout: 3,
            ..desktop.clone()
        };
        let why = uniform_bindings_refused(&program, &few).unwrap();
        assert!(why.contains("4 uniform buffers"), "{why}");
        // Past the renderer's own count, whatever the device takes.
        program.uniform_bindings = (0..MAX_UNIFORM_BINDINGS as u32 + 1)
            .map(|k| binding(k * 256, 16))
            .collect();
        assert!(uniform_bindings_refused(&program, &desktop).is_some());
        program.uniform_bindings.pop();
        assert_eq!(uniform_bindings_refused(&program, &desktop), None);

        // Textures: each with its sampler, aliases as further textures, and the main texture
        // group's one for a program that reads it (WebGPU's defaults bind 16 of each).
        program.main_texture = true;
        let sampled = unbound_shaders::TextureSlot {
            sampler: true,
            ..Default::default()
        };
        program.textures = (0..20).map(|k| format!("_T{k}")).collect();
        program.texture_slots = vec![sampled; 20];
        program.texture_aliases = vec![0];
        let gl = wgpu::Limits {
            max_sampled_textures_per_shader_stage: 32,
            max_samplers_per_shader_stage: 32,
            ..desktop.clone()
        };
        assert_eq!(texture_bindings_refused(&program, &gl), None);
        let webgpu = wgpu::Limits::default();
        let why = texture_bindings_refused(&program, &webgpu).unwrap();
        assert!(why.contains("22 textures, over the 16"), "{why}");
        let few_samplers = wgpu::Limits {
            max_sampled_textures_per_shader_stage: 32,
            ..webgpu.clone()
        };
        let why = texture_bindings_refused(&program, &few_samplers).unwrap();
        assert!(why.contains("21 samplers, over the 16"), "{why}");
        // Samplers are those the program samples through, however many textures share one:
        // 20 textures through 3 samplers bind 4 with the main one.
        let shared = unbound_shaders::TextureSlot::default();
        program.texture_slots = vec![shared; 20];
        for k in [0, 7, 19] {
            program.texture_slots[k] = sampled;
        }
        assert_eq!(texture_bindings_refused(&program, &few_samplers), None);
        // 15 samplers and the main one bind; one more does not.
        program.texture_slots = (0..20)
            .map(|k| if k < 15 { sampled } else { shared })
            .collect();
        assert_eq!(texture_bindings_refused(&program, &few_samplers), None);
        program.texture_slots[15] = sampled;
        let why = texture_bindings_refused(&program, &few_samplers).unwrap();
        assert!(why.contains("17 samplers, over the 16"), "{why}");
        // A program that does not read the main texture group binds 16 samplers of its own.
        program.main_texture = false;
        assert_eq!(texture_bindings_refused(&program, &few_samplers), None);
        program.texture_slots[16] = sampled;
        let why = texture_bindings_refused(&program, &few_samplers).unwrap();
        assert!(why.contains("17 samplers, over the 16"), "{why}");
        program.main_texture = true;
        // At the limit: 15 textures and the main one bind; one more does not.
        program.textures = (0..15).map(|k| format!("_T{k}")).collect();
        program.texture_slots = vec![sampled; 15];
        program.texture_aliases.clear();
        assert_eq!(texture_bindings_refused(&program, &webgpu), None);
        program.texture_aliases = vec![3];
        assert!(texture_bindings_refused(&program, &webgpu).is_some());
        program.texture_aliases.clear();
        program.textures.push("_T15".into());
        program.texture_slots.push(shared);
        assert!(texture_bindings_refused(&program, &webgpu).is_some());
    }

    fn uniform(name: &str, offset: u32, rows: u32, columns: u32) -> Uniform {
        Uniform {
            name: name.into(),
            offset,
            scalar: Scalar::Float,
            rows,
            columns,
            count: 1,
            stride: 0,
            unread: false,
        }
    }

    #[test]
    fn defaults_convert_but_assigned_packets_and_overrides_are_already_working_space() {
        let lab = ShaderLab::parse(
            r#"Shader "S" { Properties {
            _C("C",Color)=(2,0.5,-0.25,3)
            [HDR] _H("H",Color)=(2,0.5,-0.25,3)
            [Gamma] _F("F",Float)=2
        } SubShader { Pass {} } }"#,
        )
        .unwrap();
        let c = property_of(None, &lab, "_C", true).unwrap();
        assert!((c[0] - 4.594794).abs() < 0.00002);
        assert!((c[1] - 0.21404114).abs() < 0.00002);
        assert_eq!([c[2], c[3]], [0.0, 3.0]);
        assert_eq!(
            property_of(None, &lab, "_H", true),
            Some([2.0, 0.5, -0.25, 3.0])
        );
        assert!((property_of(None, &lab, "_F", true).unwrap()[0] - 4.594794).abs() < 0.00002);
        let value = PropertyValue::Vector([-0.5, 4.0, 8.0, -3.0]);
        assert_eq!(
            property_of(Some(&value), &lab, "_C", true),
            Some([-0.5, 4.0, 8.0, -3.0])
        );
        assert_eq!(
            property_of(None, &lab, "_C", false),
            Some([2.0, 0.5, -0.25, 3.0])
        );
    }

    /// The block path as it was before the draw's own properties were left out of the uniforms
    /// it fills: those from all of the draw's values, then the overrides by name.
    fn fill_over_block_reference(
        program: &Compiled,
        plan: &[Source],
        by_name: &rustc_hash::FxHashMap<String, u32>,
        layout: &ProgramBindings,
        block: &MaterialBlock,
        values: &DrawValues,
        out: &mut [u8],
    ) {
        for (k, bp) in layout.bindings.iter().enumerate() {
            let src = &block.binding_bytes[k];
            if !src.is_empty() {
                let start = bp.offset as usize;
                let end = (start + src.len()).min(out.len());
                if end > start {
                    out[start..end].copy_from_slice(&src[..end - start]);
                }
            }
        }
        fill_arrays(program, values, out);
        for dynamics in &block.binding_dynamic {
            for entry in dynamics {
                write_uniform(program, plan, entry.index as usize, values, out);
            }
        }
        fill_constant_buffers(program, values, out);
        for (name, value) in values.taken_overrides() {
            let mut redo = |uniform: &str| {
                if let Some(&i) = by_name.get(uniform) {
                    write_uniform(program, plan, i as usize, values, out);
                }
            };
            redo(name);
            if matches!(value, PropertyValue::Texture { .. }) {
                redo(&format!("{name}_ST"));
                redo(&format!("{name}_TexelSize"));
            }
        }
    }

    /// The block path (the material's part once, the draw's properties where they apply) writes
    /// what it wrote when every uniform the draw fills searched the draw's properties, and what a
    /// full fill writes for draws without array or texture properties (an array under a plain
    /// uniform's name outside instancing leaves a full fill's uniform unwritten, the block's the
    /// material's; a texture's `_ST` inside a bound constant buffer is the buffer's in a full
    /// fill, the texture's over a block).
    #[test]
    fn a_draw_over_its_material_block_fills_what_it_did() {
        let names = [
            ("unity_ObjectToWorld", 4),
            ("_Color", 1),
            ("_Cut", 1),
            ("_MainTex_ST", 1),
            ("_MainTex_TexelSize", 1),
            ("_Detail_ST", 1),
            ("_Mask_ST", 1),
            ("_Global", 1),
            ("unity_SHAr", 1),
            ("_ClipRect", 1),
        ];
        let mut uniforms = Vec::new();
        let mut offset = 0;
        for (name, columns) in names {
            uniforms.push(uniform(name, offset, 4, columns));
            offset += 16 * columns;
        }
        let program = Compiled {
            vertex: String::new(),
            fragment: String::new(),
            vertex_entry: String::new(),
            fragment_entry: String::new(),
            instanced_properties: vec!["_Cut".into()],
            mesh_instance_stride: Some(0),
            mesh_instance_uniform_scaling: false,
            mesh_instance_max_count: None,
            uniforms,
            uniform_size: offset,
            uniform_bindings: vec![unbound_shaders::UniformBinding {
                offset: 0,
                size: offset,
            }],
            textures: Vec::new(),
            texture_dims: Vec::new(),
            main_texture: true,
            keywords: Vec::new(),
            texture_slots: Vec::new(),
            texture_aliases: Vec::new(),
            buffers: Vec::new(),
            // A buffer bound over `_MainTex_ST` (offset 96).
            blocks: vec![unbound_shaders::BlockRange {
                name: "Bound".into(),
                offset: 96,
                size: 16,
            }],
            extra_vertex_inputs: false,
            vertex_inputs: true,
            framebuffer_fetch: 0,
        };
        fill_over_block_matches(&program, "Bound", &BOUND);
    }

    /// The same over a program of three bindings: a bound constant buffer that fills binding 1
    /// (shared across draws unless an override targets a uniform in it: a texture's `_ST` there),
    /// and the material's, global and instanced values in binding 2, at their offsets in it.
    #[test]
    fn a_draw_over_its_material_block_fills_what_it_did_across_bindings() {
        let at = |name: &str, offset| uniform(name, offset, 4, 1);
        let mut uniforms = vec![uniform("unity_ObjectToWorld", 0, 4, 4)];
        uniforms.extend([
            at("_Cut", 64),
            at("_ClipRect", 80),
            at("_MainTex_ST", 256),
            at("_MainTex_TexelSize", 272),
            at("_Detail_ST", 288),
            at("_Mask_ST", 304),
            at("_Color", 512),
            at("_Global", 528),
            at("unity_SHAr", 544),
        ]);
        // Arrays: one inside the bound block, one in binding 2.
        let array = |name: &str, offset| Uniform {
            count: 2,
            stride: 16,
            ..uniform(name, offset, 4, 1)
        };
        uniforms.extend([array("_TileArr", 320), array("_Palette", 560)]);
        let binding = |offset, size| unbound_shaders::UniformBinding { offset, size };
        let program = Compiled {
            vertex: String::new(),
            fragment: String::new(),
            vertex_entry: String::new(),
            fragment_entry: String::new(),
            instanced_properties: vec!["_Cut".into()],
            mesh_instance_stride: Some(0),
            mesh_instance_uniform_scaling: false,
            mesh_instance_max_count: None,
            uniforms,
            uniform_size: 592,
            uniform_bindings: vec![binding(0, 96), binding(256, 96), binding(512, 80)],
            textures: Vec::new(),
            texture_dims: Vec::new(),
            main_texture: true,
            keywords: Vec::new(),
            texture_slots: Vec::new(),
            texture_aliases: Vec::new(),
            buffers: Vec::new(),
            blocks: vec![unbound_shaders::BlockRange {
                name: "Tiles".into(),
                offset: 256,
                size: 96,
            }],
            extra_vertex_inputs: false,
            vertex_inputs: true,
            framebuffer_fetch: 0,
        };
        let tiles: Vec<u8> = (0..24)
            .flat_map(|i| (i as f32 + 0.5).to_le_bytes())
            .collect();
        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        let layout = ProgramBindings::of(&program, &plan);
        assert_eq!(
            layout.bindings[1].standalone_block.as_deref(),
            Some("Tiles")
        );
        assert_eq!(layout.bindings[2].arrays.len(), 1);
        fill_over_block_matches(&program, "Tiles", &tiles);
        // A bound buffer shorter than its binding is copied per draw over the material's values.
        fill_over_block_matches(&program, "Tiles", &tiles[..32]);
    }

    /// Runs draws with and without overrides, hidden overrides and instances over `program`'s
    /// material block, with `bound` bound to the cbuffer `bound_name`, and checks the per-binding
    /// fill against the reference and the full fill.
    fn fill_over_block_matches(program: &Compiled, bound_name: &str, bound: &[u8]) {
        let size_of_program = program.uniform_size as usize;
        let lab = ShaderLab::parse(r#"Shader "S" { Properties { _Color ("C", Color) = (0.5, 1, 1, 1) _Cut ("Cut", Range(0,1)) = 0.25 } SubShader { Pass { } } }"#).unwrap();
        let material = Material {
            shader: "S".into(),
            properties: vec![
                (
                    "_MainTex".into(),
                    PropertyValue::Texture {
                        handle: 7,
                        st: [2.0, 3.0, 0.5, 0.0],
                    },
                ),
                (
                    "_Detail_ST".into(),
                    PropertyValue::Vector([5.0, 6.0, 7.0, 8.0]),
                ),
            ],
            ..Default::default()
        };
        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        let by_name: rustc_hash::FxHashMap<String, u32> = program
            .uniforms
            .iter()
            .enumerate()
            .map(|(i, u)| (u.name.clone(), i as u32))
            .collect();
        let layout = ProgramBindings::of(program, &plan);
        let block = material_block(program, &plan, &layout, &material, &lab, true);
        let index = |name: &str| by_name[name] as usize;
        assert!(
            block.is_dynamic[index("_MainTex_ST")] && !block.is_dynamic[index("_Detail_ST")],
            "_MainTex_ST per draw, _Detail_ST the material's"
        );
        let mut model = [0.0; 16];
        for i in 0..4 {
            model[i * 5] = 1.0;
        }
        model[12] = 5.0;
        let size = |name: &str| (name == "_MainTex").then_some((64, 32));
        let globals_index = Default::default();
        let globals = [
            (
                "_Palette".to_string(),
                PropertyValue::Array {
                    components: 4,
                    data: vec![0.5; 8].into(),
                },
            ),
            ("_Global".to_string(), PropertyValue::Float(3.0)),
            (
                "unity_SHAr".to_string(),
                PropertyValue::Vector([0.1, 0.2, 0.3, 0.4]),
            ),
        ];
        let texture = |handle, st| PropertyValue::Texture { handle, st };
        let pair = |a: f32| PropertyValue::Array {
            components: 4,
            data: (0..8).map(|i| a + i as f32).collect::<Vec<_>>().into(),
        };
        let cases: Vec<Vec<(String, PropertyValue)>> = vec![
            vec![],
            // Arrays by override, in a bound block and in a binding of its own.
            vec![
                ("_TileArr".into(), pair(10.0)),
                ("_Palette".into(), pair(20.0)),
            ],
            vec![
                (
                    "_Color".into(),
                    PropertyValue::Vector([0.25, 0.5, 0.75, 1.0]),
                ),
                ("_Global".into(), PropertyValue::Float(9.0)),
                (
                    "unity_SHAr".into(),
                    PropertyValue::Vector([1.0, 2.0, 3.0, 4.0]),
                ),
            ],
            // A value of another kind under a texture's name hides the material's texture.
            vec![("_MainTex".into(), PropertyValue::Float(1.0))],
            vec![("_Detail".into(), PropertyValue::Float(1.0))],
            vec![
                ("_MainTex".into(), texture(9, [4.0, 4.0, 1.0, 1.0])),
                ("_Mask".into(), texture(3, [0.5, 0.5, 0.0, 0.0])),
                ("_Cut".into(), PropertyValue::Float(0.75)),
                ("_Cut".into(), PropertyValue::Float(0.5)),
            ],
            // Mesh instancing: per-instance arrays and an instanced property's single value.
            vec![
                (
                    "_Global".into(),
                    PropertyValue::Array {
                        components: 1,
                        data: vec![4.0, 5.0].into(),
                    },
                ),
                (
                    "_Color".into(),
                    PropertyValue::Array {
                        components: 4,
                        data: vec![0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8].into(),
                    },
                ),
                ("_Cut".into(), PropertyValue::Float(0.5)),
            ],
        ];
        let instances = [None, Some(0), Some(1), Some(2)];
        // A hidden override (a renderer constant the program reads elsewhere): every path fills
        // what it fills without that override, and the others still apply.
        let hiddens = [None, Some("_Color"), Some("_Cut")];
        for ((overrides, instance), hidden) in cases
            .iter()
            .flat_map(|o| instances.iter().map(move |i| (o, *i)))
            .flat_map(|c| hiddens.iter().map(move |h| (c, *h)))
        {
            let kept: Vec<(String, PropertyValue)> = overrides
                .iter()
                .filter(|(n, _)| Some(n.as_str()) != hidden)
                .cloned()
                .collect();
            let values = DrawValues {
                gizmo_draw: false,
                color: [1.0; 4],
                sprite_color: [1.0; 4],
                globals_index: &globals_index,
                overrides,
                hidden,
                model,
                view: model,
                proj: model,
                gpu_proj: model,
                flipped: false,
                reversed_z: false,
                camera_view: model,
                camera_proj: model,
                screen: [960.0, 540.0],
                time: 1.0,
                linear: true,
                material: &material,
                lab: &lab,
                globals: &globals,
                globals_revision: 0,
                textures_generation: 0,
                main_texture_handle: 0,
                main_alpha_only: false,
                texture_size: &size,
                constant_buffer: &|name| {
                    (name == bound_name).then_some(ConstantBufferRef {
                        handle: 1,
                        revision: 1,
                        bytes: bound,
                    })
                },
                instance,
                instance_empty: false,
                element: None,
            };
            let mut full = vec![0u8; size_of_program];
            fill(program, &plan, &values, &mut full);
            let mut without = vec![0u8; size_of_program];
            let unhidden = DrawValues {
                overrides: &kept,
                hidden: None,
                ..values
            };
            fill(program, &plan, &unhidden, &mut without);
            assert_eq!(full, without, "hidden {hidden:?}, overrides {overrides:?}");
            let mut data = Vec::new();
            let mut shared_constants = HashMap::default();
            let mut shared_bindings = HashMap::default();
            let mut empty_slot = None;
            let cam = CachedCamera::compute(1, CameraInput::from_values(&values), &values);
            let mut override_scratch = Vec::new();
            let draw_offsets = push_program_uniforms(
                0,
                program,
                &plan,
                &by_name,
                &layout,
                Some(&block),
                &values,
                &cam,
                &mut data,
                &mut shared_constants,
                &mut shared_bindings,
                &mut empty_slot,
                &mut String::new(),
                &mut override_scratch,
            );
            // Each binding's bytes at its place in the program's uniform block.
            let mut over = vec![0u8; size_of_program];
            for (k, bp) in layout.bindings.iter().enumerate() {
                let at = draw_offsets.offsets[k] as usize;
                let (start, len) = (bp.offset as usize, bp.size as usize);
                over[start..start + len].copy_from_slice(&data[at..at + len]);
            }
            // A bound buffer that fills its binding is shared unless an override targets a
            // uniform in it.
            let targets_bound = values.taken_overrides().any(|(n, v)| {
                let in_bound = |name: &str| {
                    by_name.get(name).is_some_and(|&i| {
                        layout.bindings[layout.uniform_binding[i as usize] as usize]
                            .standalone_block
                            .as_deref()
                            == Some(bound_name)
                    })
                };
                in_bound(n)
                    || matches!(v, PropertyValue::Texture { .. })
                        && TEXTURE_SUFFIXES
                            .iter()
                            .any(|s| in_bound(&format!("{n}{s}")))
            });
            let shares = bound.len() as u32
                >= layout
                    .bindings
                    .iter()
                    .find(|b| b.standalone_block.as_deref() == Some(bound_name))
                    .map_or(u32::MAX, |b| b.size);
            // Never shared under an override that reaches it; always without overrides (other
            // overrides may keep a per-draw copy: that costs a copy, not a value).
            let bound_bit = layout
                .bindings
                .iter()
                .position(|b| b.standalone_block.as_deref() == Some(bound_name))
                .map_or(0, |k| 1u8 << k);
            let reached = values.taken_overrides().any(|(n, _)| {
                layout
                    .override_targets
                    .get(n.as_str())
                    .is_some_and(|target| target.binding_mask & bound_bit != 0)
            });
            if targets_bound || !shares {
                assert_eq!(shared_constants.len(), 0, "overrides {overrides:?}");
            } else if !reached {
                assert_eq!(shared_constants.len(), 1, "overrides {overrides:?}");
            }
            let mut reference = vec![0u8; size_of_program];
            fill_over_block_reference(
                program,
                &plan,
                &by_name,
                &layout,
                &block,
                &values,
                &mut reference,
            );
            assert_eq!(
                reference, over,
                "overrides {overrides:?}, instance {instance:?}, hidden {hidden:?}"
            );
            if !overrides.iter().any(|(_, v)| {
                matches!(
                    v,
                    PropertyValue::Array { .. } | PropertyValue::Texture { .. }
                )
            }) {
                assert_eq!(full, over, "overrides {overrides:?}, instance {instance:?}");
            }
        }
    }

    /// A uniform's global property, looked up once: missing until it is added, then followed
    /// where it is as it is set again (and as other globals are added).
    #[test]
    fn a_global_slot_follows_the_globals_as_they_are_added_and_set() {
        use unbound_runtime::render_protocol::owned::NameIndex;
        let slot = GlobalSlot::default();
        let mut globals: Vec<(String, PropertyValue)> = Vec::new();
        let mut index = NameIndex::default();
        let float = |v: Option<&PropertyValue>| match v {
            Some(PropertyValue::Float(f)) => Some(*f),
            _ => None,
        };
        assert_eq!(float(slot.find(&globals, &index, "_G")), None);
        globals.push(("_Other".into(), PropertyValue::Float(1.0)));
        index.refresh(&globals);
        assert_eq!(float(slot.find(&globals, &index, "_G")), None);
        globals.push(("_G".into(), PropertyValue::Float(2.0)));
        index.refresh(&globals);
        assert_eq!(float(slot.find(&globals, &index, "_G")), Some(2.0));
        globals[1].1 = PropertyValue::Float(3.0);
        globals.push(("_More".into(), PropertyValue::Float(4.0)));
        index.refresh(&globals);
        assert_eq!(float(slot.find(&globals, &index, "_G")), Some(3.0));
        assert_eq!(float(slot.find(&globals, &index, "_G")), Some(3.0));
    }

    #[test]
    fn uniforms_are_filled_by_name() {
        let lab = ShaderLab::parse(r#"Shader "S" { Properties { _Color ("C", Color) = (0.5, 1, 1, 1) _Cut ("Cut", Range(0,1)) = 0.25 } SubShader { Pass { } } }"#).unwrap();
        let material = Material {
            shader: "S".into(),
            properties: vec![(
                "_MainTex".into(),
                PropertyValue::Texture {
                    handle: 7,
                    st: [2.0, 3.0, 0.5, 0.0],
                },
            )],
            ..Default::default()
        };
        let mut model = [0.0; 16];
        for i in 0..4 {
            model[i * 5] = 1.0;
        }
        model[12] = 5.0;
        let program = Compiled {
            vertex: String::new(),
            fragment: String::new(),
            vertex_entry: String::new(),
            fragment_entry: String::new(),
            instanced_properties: Vec::new(),
            mesh_instance_stride: Some(0),
            mesh_instance_uniform_scaling: false,
            mesh_instance_max_count: None,
            uniforms: vec![
                uniform("unity_ObjectToWorld", 0, 4, 4),
                uniform("_Color", 64, 4, 1),
                uniform("_Cut", 80, 1, 1),
                uniform("_MainTex_ST", 96, 4, 1),
                uniform("_MainTex_TexelSize", 112, 4, 1),
                uniform("_ClipRect", 128, 4, 1),
                uniform("_Global", 144, 1, 1),
            ],
            uniform_size: 176,
            uniform_bindings: vec![unbound_shaders::UniformBinding {
                offset: 0,
                size: 176,
            }],
            textures: Vec::new(),
            texture_dims: Vec::new(),
            main_texture: true,
            keywords: Vec::new(),
            texture_slots: Vec::new(),
            texture_aliases: Vec::new(),
            buffers: Vec::new(),
            blocks: vec![unbound_shaders::BlockRange {
                name: "Bound".into(),
                offset: 160,
                size: 16,
            }],
            extra_vertex_inputs: false,
            vertex_inputs: true,
            framebuffer_fetch: 0,
        };
        let size = |name: &str| (name == "_MainTex").then_some((64, 32));
        let globals_index = Default::default();
        let values = DrawValues {
            gizmo_draw: false,
            color: [1.0; 4],
            sprite_color: [1.0; 4],
            globals_index: &globals_index,
            overrides: &[],
            hidden: None,
            model,
            view: model,
            proj: model,
            gpu_proj: model,
            flipped: false,
            reversed_z: false,
            camera_view: model,
            camera_proj: model,
            screen: [960.0, 540.0],
            time: 1.0,
            linear: true,
            material: &material,
            lab: &lab,
            globals: &[("_Global".to_string(), PropertyValue::Float(3.0))],
            globals_revision: 0,
            textures_generation: 0,
            main_texture_handle: 0,
            main_alpha_only: false,
            texture_size: &size,
            constant_buffer: &|name| {
                (name == "Bound").then_some(ConstantBufferRef {
                    handle: 1,
                    revision: 1,
                    bytes: &BOUND[..],
                })
            },
            instance: None,
            instance_empty: false,
            element: None,
        };
        let mut out = vec![0u8; 176];
        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        fill(&program, &plan, &values, &mut out);
        let f = |at: usize| f32::from_le_bytes(out[at..at + 4].try_into().unwrap());
        assert_eq!(f(48), 5.0, "the model's translation");
        // A Color property in linear space: converted (0.5 → 0.214); alpha is not.
        assert!((f(64) - 0.214).abs() < 0.001 && f(76) == 1.0);
        assert_eq!(f(80), 0.25);
        assert_eq!([f(96), f(100), f(104)], [2.0, 3.0, 0.5]);
        assert_eq!([f(112), f(120), f(124)], [1.0 / 64.0, 64.0, 32.0]);
        assert_eq!(f(128), -2e10);
        assert_eq!(f(144), 3.0, "a global property");
        assert_eq!([f(160), f(172)], [7.0, 8.0], "a bound constant buffer");

        let mut instanced = program.clone();
        instanced.instanced_properties.push("_Color".into());
        let overrides = [
            ("_Color".into(), PropertyValue::Vector([1.0; 4])),
            ("_Cut".into(), PropertyValue::Float(0.75)),
        ];
        let later_instance = DrawValues {
            overrides: &overrides,
            instance: Some(1),
            ..values
        };
        fill(&instanced, &plan, &later_instance, &mut out);
        assert!(
            (f32::from_le_bytes(out[64..68].try_into().unwrap()) - 0.214).abs() < 0.001,
            "a scalar instanced property falls back to the material after instance zero"
        );
        assert_eq!(
            f32::from_le_bytes(out[80..84].try_into().unwrap()),
            0.75,
            "ordinary scalar uniforms apply to every instance"
        );
        let first_instance = DrawValues {
            instance: Some(0),
            ..later_instance
        };
        fill(&instanced, &plan, &first_instance, &mut out);
        assert_eq!(
            f32::from_le_bytes(out[64..68].try_into().unwrap()),
            1.0,
            "instance zero takes the scalar"
        );
        instanced.uniforms.push(Uniform {
            count: 2,
            stride: 16,
            ..uniform("_Palette", 176, 4, 1)
        });
        instanced.uniform_size = 208;
        let shared: std::sync::Arc<[f32]> = [0.25, 0.25, 0.25, 0.25, 0.75, 0.75, 0.75, 0.75].into();
        let array = PropertyValue::Array {
            components: 4,
            data: shared.clone(),
        };
        let overrides = [
            ("_Palette".into(), array.clone()),
            ("_Color".into(), array.clone()),
            (
                "_Cut".into(),
                PropertyValue::Array {
                    components: 1,
                    data: [0.125, 0.5].into(),
                },
            ),
        ];
        let instance = DrawValues {
            overrides: &overrides,
            instance: Some(1),
            ..values
        };
        let mut plan = plan.clone();
        plan.push(Source::Named);
        out.resize(208, 0);
        fill(&instanced, &plan, &instance, &mut out);
        let f = |out: &[u8], at: usize| f32::from_le_bytes(out[at..at + 4].try_into().unwrap());
        assert_eq!(
            f(&out, 64),
            0.75,
            "declared instanced arrays select their element"
        );
        assert_eq!(
            f(&out, 80),
            0.5,
            "so do ordinary scalar uniforms of an array"
        );
        assert_eq!(
            [f(&out, 176), f(&out, 192)],
            [0.25, 0.75],
            "ordinary uniform arrays retain every element"
        );
        let past = DrawValues {
            instance: Some(2),
            ..instance
        };
        fill(&instanced, &plan, &past, &mut out);
        assert!(
            (f(&out, 64) - 0.214).abs() < 0.001 && f(&out, 80) == 0.25,
            "past an array's end the material's value applies"
        );
        // An instance an instance multiplier adds past the call's count has no instance data.
        let added = DrawValues {
            model: [0.0; 16],
            instance: Some(2),
            instance_empty: true,
            ..instance
        };
        out.fill(0xff);
        fill(&instanced, &plan, &added, &mut out);
        assert_eq!(
            [f(&out, 64), f(&out, 76), f(&out, 80)],
            [0.0; 3],
            "its instanced properties and the call's arrays read zero"
        );
        assert_eq!(
            [f(&out, 96), f(&out, 144), f(&out, 176), f(&out, 192)],
            [2.0, 3.0, 0.25, 0.75],
            "ordinary uniforms, globals and uniform arrays keep their values"
        );
        // Its inverse matrix is zero too, where a zero matrix's inverse would be the identity.
        let mut inverted = instanced.clone();
        inverted
            .uniforms
            .push(uniform("unity_WorldToObject", 208, 4, 4));
        inverted.uniform_size = 272;
        let mut inverted_plan = plan.clone();
        inverted_plan.push(Source::of("unity_WorldToObject"));
        let mut block = vec![0xffu8; 272];
        let world_to_object =
            |block: &[u8]| -> Vec<f32> { (0..16).map(|k| f(block, 208 + 4 * k)).collect() };
        fill(&inverted, &inverted_plan, &past, &mut block);
        assert_eq!(
            world_to_object(&block),
            inverse(&model).to_vec(),
            "an instance's own inverse"
        );
        fill(&inverted, &inverted_plan, &added, &mut block);
        assert_eq!(
            world_to_object(&block),
            vec![0.0; 16],
            "an added instance's is zero"
        );
        let not_instance = DrawValues {
            instance: None,
            ..instance
        };
        fill(&instanced, &plan, &not_instance, &mut out);
        assert!(
            (f(&out, 64) - 0.214).abs() < 0.001,
            "outside mesh instancing an instanced property's array keeps the material's value"
        );
        instanced.uniforms.push(Uniform {
            count: 1,
            stride: 16,
            ..uniform("_Single", 208, 4, 1)
        });
        instanced.uniform_size = 224;
        plan.push(Source::Named);
        let overrides = [(
            "_Single".into(),
            PropertyValue::Array {
                components: 4,
                data: vec![0.5; 4].into(),
            },
        )];
        let instance = DrawValues {
            overrides: &overrides,
            instance: Some(0),
            ..values
        };
        out.resize(224, 0);
        fill(&instanced, &plan, &instance, &mut out);
        assert_eq!(
            f(&out, 208),
            0.5,
            "a one-element uniform array is still an array"
        );
    }

    /// Typed slots: only float slots are filterable; integer and depth slots bind their types.
    #[test]
    fn slot_texel_types_select_the_layout_sample_type() {
        use unbound_shaders::SampleKind;
        assert_eq!(
            sample_type(SampleKind::Float),
            wgpu::TextureSampleType::Float { filterable: true }
        );
        assert_eq!(sample_type(SampleKind::Sint), wgpu::TextureSampleType::Sint);
        assert_eq!(sample_type(SampleKind::Uint), wgpu::TextureSampleType::Uint);
        assert_eq!(
            sample_type(SampleKind::Depth),
            wgpu::TextureSampleType::Depth
        );
    }

    /// A standalone cbuffer bound by name (such as Forward+ tile/z-bin tables) is copied once per
    /// `(handle, revision, binding_size)` and shared across draws by offset; revision changes,
    /// short buffers and overrides inside the cbuffer stay isolated and byte-exact.
    #[test]
    fn standalone_constant_buffers_are_shared_across_draws() {
        let lab =
            ShaderLab::parse(r#"Shader "S" { Properties {} SubShader { Pass { } } }"#).unwrap();
        let material = Material {
            shader: "S".into(),
            ..Default::default()
        };
        let program = Compiled {
            vertex: String::new(),
            fragment: String::new(),
            vertex_entry: String::new(),
            fragment_entry: String::new(),
            uniforms: vec![
                uniform("unity_ObjectToWorld", 0, 4, 4),
                uniform("urp_Tiles", 256, 4, 1),
            ],
            instanced_properties: Vec::new(),
            mesh_instance_stride: Some(0),
            mesh_instance_uniform_scaling: false,
            mesh_instance_max_count: None,
            uniform_size: 256 + 16384,
            uniform_bindings: vec![
                unbound_shaders::UniformBinding {
                    offset: 0,
                    size: 64,
                },
                unbound_shaders::UniformBinding {
                    offset: 256,
                    size: 16384,
                },
            ],
            textures: Vec::new(),
            texture_dims: Vec::new(),
            main_texture: false,
            keywords: Vec::new(),
            texture_slots: Vec::new(),
            texture_aliases: Vec::new(),
            blocks: vec![
                unbound_shaders::BlockRange {
                    name: "UnityPerDraw".into(),
                    offset: 0,
                    size: 64,
                },
                unbound_shaders::BlockRange {
                    name: "urp_TileBuffer".into(),
                    offset: 256,
                    size: 16384,
                },
            ],
            buffers: Vec::new(),
            extra_vertex_inputs: false,
            vertex_inputs: true,
            framebuffer_fetch: 0,
        };
        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        let by_name: rustc_hash::FxHashMap<String, u32> = program
            .uniforms
            .iter()
            .enumerate()
            .map(|(i, u)| (u.name.clone(), i as u32))
            .collect();
        let layout = ProgramBindings::of(&program, &plan);
        assert_eq!(
            layout.bindings[0].standalone_block.as_deref(),
            Some("UnityPerDraw")
        );
        assert_eq!(
            layout.bindings[1].standalone_block.as_deref(),
            Some("urp_TileBuffer")
        );
        let block = material_block(&program, &plan, &layout, &material, &lab, true);
        assert!(
            block.binding_bytes[1].is_empty(),
            "no static bytes in tile binding"
        );

        let tiles_v1 = vec![0x3Cu8; 16384];
        let tiles_v2 = vec![0x5Au8; 16384];
        let short_tiles = vec![0x77u8; 64];
        let mut model1 = [0.0f32; 16];
        model1[12] = 1.0;
        let mut model2 = [0.0f32; 16];
        model2[12] = 2.0;
        let globals_index = Default::default();
        let size = |_: &str| None;
        let none_cb = |_: &str| None;
        let base = DrawValues {
            gizmo_draw: false,
            color: [1.0; 4],
            sprite_color: [1.0; 4],
            model: model1,
            view: model1,
            proj: model1,
            gpu_proj: model1,
            flipped: false,
            reversed_z: false,
            camera_view: model1,
            camera_proj: model1,
            screen: [1280.0, 720.0],
            time: 0.0,
            linear: true,
            material: &material,
            overrides: &[],
            hidden: None,
            lab: &lab,
            globals: &[],
            globals_index: &globals_index,
            globals_revision: 0,
            textures_generation: 0,
            main_texture_handle: 0,
            main_alpha_only: false,
            texture_size: &size,
            constant_buffer: &none_cb,
            instance: None,
            instance_empty: false,
            element: None,
        };

        let mut data = Vec::new();
        let mut shared = HashMap::default();
        let mut shared_bindings = HashMap::default();
        let mut empty_slot = None;
        let mut scratch = String::new();
        let mut override_scratch = Vec::new();
        let cam = CachedCamera::compute(1, CameraInput::from_values(&base), &base);

        let cb1 = |name: &str| {
            (name == "urp_TileBuffer").then_some(ConstantBufferRef {
                handle: 42,
                revision: 1,
                bytes: &tiles_v1,
            })
        };
        let v1 = DrawValues {
            model: model1,
            constant_buffer: &cb1,
            ..base
        };
        let d1 = push_program_uniforms(
            0,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block),
            &v1,
            &cam,
            &mut data,
            &mut shared,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        let len_after_d1 = data.len();
        let v2 = DrawValues {
            model: model2,
            constant_buffer: &cb1,
            ..base
        };
        let d2 = push_program_uniforms(
            0,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block),
            &v2,
            &cam,
            &mut data,
            &mut shared,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        assert_eq!(
            d1.offsets[1], d2.offsets[1],
            "second draw reuses shared tile buffer"
        );
        assert_ne!(
            d1.offsets[0], d2.offsets[0],
            "per-draw binding stays distinct"
        );
        assert_eq!(
            data.len() - len_after_d1,
            DRAW_SLOT as usize,
            "second draw only allocates its 256-byte per-draw binding"
        );
        assert_eq!(
            &data[d1.offsets[1] as usize..d1.offsets[1] as usize + 16384],
            &tiles_v1[..]
        );

        // Updated revision allocates a new shared copy without mutating d1/d2's bytes.
        let cb2 = |name: &str| {
            (name == "urp_TileBuffer").then_some(ConstantBufferRef {
                handle: 42,
                revision: 2,
                bytes: &tiles_v2,
            })
        };
        let v3 = DrawValues {
            model: model1,
            constant_buffer: &cb2,
            ..base
        };
        let d3 = push_program_uniforms(
            0,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block),
            &v3,
            &cam,
            &mut data,
            &mut shared,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        assert_ne!(d3.offsets[1], d1.offsets[1]);
        assert_eq!(
            &data[d1.offsets[1] as usize..d1.offsets[1] as usize + 16384],
            &tiles_v1[..]
        );
        assert_eq!(
            &data[d3.offsets[1] as usize..d3.offsets[1] as usize + 16384],
            &tiles_v2[..]
        );

        // Draw override targeting a uniform inside the standalone cbuffer falls back to per-draw
        // so the override applies without mutating the shared copy.
        let overrides = [(
            "urp_Tiles".to_string(),
            PropertyValue::Vector([9.0, 8.0, 7.0, 6.0]),
        )];
        let v4 = DrawValues {
            model: model1,
            constant_buffer: &cb1,
            overrides: &overrides,
            ..base
        };
        let d4 = push_program_uniforms(
            0,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block),
            &v4,
            &cam,
            &mut data,
            &mut shared,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        assert_ne!(d4.offsets[1], d1.offsets[1]);
        assert_eq!(
            f32::from_le_bytes(
                data[d4.offsets[1] as usize..d4.offsets[1] as usize + 4]
                    .try_into()
                    .unwrap()
            ),
            9.0
        );
        assert_eq!(
            &data[d1.offsets[1] as usize..d1.offsets[1] as usize + 16384],
            &tiles_v1[..]
        );

        // A buffer shorter than the binding falls back to per-draw so trailing bytes match.
        let cb_short = |name: &str| {
            (name == "urp_TileBuffer").then_some(ConstantBufferRef {
                handle: 99,
                revision: 1,
                bytes: &short_tiles,
            })
        };
        let v5 = DrawValues {
            model: model1,
            constant_buffer: &cb_short,
            ..base
        };
        let d5 = push_program_uniforms(
            0,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block),
            &v5,
            &cam,
            &mut data,
            &mut shared,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        assert_eq!(
            &data[d5.offsets[1] as usize..d5.offsets[1] as usize + 64],
            &short_tiles[..]
        );
        assert!(
            data[d5.offsets[1] as usize + 64..d5.offsets[1] as usize + 16384]
                .iter()
                .all(|&b| b == 0)
        );
    }

    /// The cached binding of a draw is reused only while what its values come from is the same:
    /// a material texture's `_ST` (another material), a `_TexelSize` (a texture resized), the
    /// main texture's (another main texture) and a bound constant buffer's bytes (a new revision).
    /// Each draw's binding is what a full fill of it writes.
    #[test]
    fn a_cached_binding_follows_materials_textures_and_bound_buffers() {
        let lab =
            ShaderLab::parse(r#"Shader "S" { Properties {} SubShader { Pass { } } }"#).unwrap();
        let material = |st: [f32; 4]| Material {
            shader: "S".into(),
            properties: vec![
                ("_Detail".into(), PropertyValue::Texture { handle: 5, st }),
                (
                    "_MainTex".into(),
                    PropertyValue::Texture {
                        handle: 6,
                        st: [1.0, 1.0, 0.0, 0.0],
                    },
                ),
            ],
            ..Default::default()
        };
        let (mat_a, mat_b) = (
            material([2.0, 2.0, 0.5, 0.0]),
            material([3.0, 1.0, 0.0, 0.25]),
        );
        // Binding 0: per draw. Binding 1: texture-derived values, no static bytes. Binding 2: a
        // bound cbuffer beside a global (two blocks: not a standalone binding).
        let program = Compiled {
            vertex: String::new(),
            fragment: String::new(),
            vertex_entry: String::new(),
            fragment_entry: String::new(),
            uniforms: vec![
                uniform("unity_ObjectToWorld", 0, 4, 4),
                uniform("_Detail_ST", 256, 4, 1),
                uniform("_Detail_TexelSize", 272, 4, 1),
                uniform("_MainTex_TexelSize", 288, 4, 1),
                uniform("_Bound", 512, 4, 1),
                uniform("_G", 528, 4, 1),
            ],
            instanced_properties: Vec::new(),
            mesh_instance_stride: None,
            mesh_instance_uniform_scaling: false,
            mesh_instance_max_count: None,
            uniform_size: 544,
            uniform_bindings: vec![
                unbound_shaders::UniformBinding {
                    offset: 0,
                    size: 64,
                },
                unbound_shaders::UniformBinding {
                    offset: 256,
                    size: 48,
                },
                unbound_shaders::UniformBinding {
                    offset: 512,
                    size: 32,
                },
            ],
            textures: vec!["_Detail".into(), "_MainTex".into()],
            texture_dims: Vec::new(),
            texture_slots: Vec::new(),
            texture_aliases: Vec::new(),
            main_texture: true,
            buffers: Vec::new(),
            keywords: Vec::new(),
            blocks: vec![
                unbound_shaders::BlockRange {
                    name: "Params".into(),
                    offset: 512,
                    size: 16,
                },
                unbound_shaders::BlockRange {
                    name: "$Global".into(),
                    offset: 528,
                    size: 16,
                },
            ],
            extra_vertex_inputs: false,
            vertex_inputs: true,
            framebuffer_fetch: 0,
        };
        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        let by_name: rustc_hash::FxHashMap<String, u32> = program
            .uniforms
            .iter()
            .enumerate()
            .map(|(i, u)| (u.name.clone(), i as u32))
            .collect();
        let layout = ProgramBindings::of(&program, &plan);
        assert!(
            !layout.bindings[1].has_per_draw_builtin && !layout.bindings[2].has_per_draw_builtin
        );
        assert!(layout.bindings[2].standalone_block.is_none());
        let block_a = material_block(&program, &plan, &layout, &mat_a, &lab, true);
        let block_b = material_block(&program, &plan, &layout, &mat_b, &lab, true);

        let globals = vec![(
            "_G".to_string(),
            PropertyValue::Vector([4.0, 3.0, 2.0, 1.0]),
        )];
        let mut globals_index = unbound_runtime::render_protocol::owned::NameIndex::default();
        globals_index.refresh(&globals);
        let small = |name: &str| match name {
            "_Detail" => Some((64, 32)),
            "_MainTex" => Some((128, 128)),
            _ => None,
        };
        let resized = |name: &str| match name {
            "_Detail" => Some((256, 16)),
            "_MainTex" => Some((128, 128)),
            _ => None,
        };
        let other_main = |name: &str| match name {
            "_Detail" => Some((64, 32)),
            "_MainTex" => Some((32, 8)),
            _ => None,
        };
        let params_1 = 1.0f32.to_le_bytes().repeat(4);
        let params_2 = 7.0f32.to_le_bytes().repeat(4);
        let bound_1 = |name: &str| {
            (name == "Params").then_some(ConstantBufferRef {
                handle: 3,
                revision: 1,
                bytes: &params_1,
            })
        };
        let bound_2 = |name: &str| {
            (name == "Params").then_some(ConstantBufferRef {
                handle: 3,
                revision: 2,
                bytes: &params_2,
            })
        };
        let ident = {
            let mut m = [0.0f32; 16];
            for i in 0..4 {
                m[i * 5] = 1.0;
            }
            m
        };
        let base = DrawValues {
            gizmo_draw: false,
            color: [1.0; 4],
            sprite_color: [1.0; 4],
            model: ident,
            view: ident,
            proj: ident,
            gpu_proj: ident,
            flipped: false,
            reversed_z: true,
            camera_view: ident,
            camera_proj: ident,
            screen: [1280.0, 720.0],
            time: 1.0,
            linear: true,
            material: &mat_a,
            overrides: &[],
            hidden: None,
            lab: &lab,
            globals: &globals,
            globals_index: &globals_index,
            globals_revision: 1,
            textures_generation: 1,
            main_texture_handle: 6,
            main_alpha_only: false,
            texture_size: &small,
            constant_buffer: &bound_1,
            instance: None,
            instance_empty: false,
            element: None,
        };
        let cam = CachedCamera::compute(1, CameraInput::from_values(&base), &base);
        let mut data = Vec::new();
        let mut shared_constants = HashMap::default();
        let mut shared_bindings = HashMap::default();
        let mut empty_slot = None;
        let mut scratch = String::new();
        let mut override_scratch = Vec::new();
        // Pushes a draw; returns its binding offsets after checking each binding's bytes.
        let mut draw = |values: &DrawValues, block: &MaterialBlock| {
            let offsets = push_program_uniforms(
                7,
                &program,
                &plan,
                &by_name,
                &layout,
                Some(block),
                values,
                &cam,
                &mut data,
                &mut shared_constants,
                &mut shared_bindings,
                &mut empty_slot,
                &mut scratch,
                &mut override_scratch,
            );
            let mut full = vec![0u8; program.uniform_size as usize];
            fill(&program, &plan, values, &mut full);
            for k in 1..3 {
                let b = &layout.bindings[k];
                let at = offsets.offsets[k] as usize;
                assert_eq!(
                    &data[at..at + b.size as usize],
                    &full[b.offset as usize..(b.offset + b.size) as usize],
                    "binding {k}"
                );
            }
            [offsets.offsets[1], offsets.offsets[2]]
        };
        let first = draw(&base, &block_a);
        // The same draw again: both bindings reused.
        assert_eq!(draw(&base, &block_a), first);
        // Another material: its texture's `_ST` differs.
        let other_material = draw(
            &DrawValues {
                material: &mat_b,
                ..base
            },
            &block_b,
        );
        assert_ne!(other_material[0], first[0]);
        // A texture resized (a new generation): its `_TexelSize` differs.
        let resized_texture = draw(
            &DrawValues {
                textures_generation: 2,
                texture_size: &resized,
                ..base
            },
            &block_a,
        );
        assert_ne!(resized_texture[0], first[0]);
        // Another main texture: `_MainTex_TexelSize` differs.
        let main = draw(
            &DrawValues {
                main_texture_handle: 9,
                texture_size: &other_main,
                ..base
            },
            &block_a,
        );
        assert_ne!(main[0], first[0]);
        // The bound buffer's next revision: binding 2's bytes differ.
        let rewritten = draw(
            &DrawValues {
                constant_buffer: &bound_2,
                ..base
            },
            &block_a,
        );
        assert_ne!(rewritten[1], first[1]);
    }

    #[test]
    fn shared_global_binding_and_camera_cache_reuse_and_invalidate() {
        use unbound_shaders::{BlockRange, Uniform, UniformBinding};

        let lab = ShaderLab::parse(
            r#"Shader "S" { Properties { _Color ("C", Color) = (1, 1, 1, 1) } SubShader { Pass { } } }"#,
        )
        .unwrap();
        let mat_a = Material {
            shader: "S".into(),
            properties: vec![("_Color".into(), PropertyValue::Vector([0.2, 0.4, 0.6, 1.0]))],
            ..Default::default()
        };
        let mat_b = Material {
            shader: "S".into(),
            properties: vec![("_Color".into(), PropertyValue::Vector([0.8, 0.1, 0.2, 1.0]))],
            ..Default::default()
        };
        let scalar = |name: &str, offset: u32, rows: u32, columns: u32| Uniform {
            name: name.into(),
            offset,
            rows,
            columns,
            count: 1,
            stride: 0,
            scalar: Scalar::Float,
            unread: false,
        };
        // Binding 0: UnityPerDraw + UnityPerMaterial (per-draw + per-material).
        // Binding 1: $Global (camera built-in + global scalar + unread per-draw built-in).
        // Binding 2: AdditionalLights (camera-free global array).
        let program = Compiled {
            vertex: String::new(),
            fragment: String::new(),
            vertex_entry: String::new(),
            fragment_entry: String::new(),
            uniforms: vec![
                scalar("unity_ObjectToWorld", 0, 4, 4),
                scalar("_Color", 64, 4, 1),
                scalar("unity_MatrixVP", 256, 4, 4),
                scalar("_WorldSpaceCameraPos", 320, 4, 1),
                scalar("_MainLightIntensity", 336, 1, 1),
                Uniform {
                    name: "glstate_matrix_transpose_modelview0".into(),
                    offset: 352,
                    rows: 4,
                    columns: 4,
                    count: 1,
                    stride: 0,
                    scalar: Scalar::Float,
                    unread: true,
                },
                Uniform {
                    name: "_AdditionalLightsColor".into(),
                    offset: 512,
                    rows: 4,
                    columns: 1,
                    count: 4,
                    stride: 16,
                    scalar: Scalar::Float,
                    unread: false,
                },
            ],
            instanced_properties: Vec::new(),
            mesh_instance_stride: None,
            mesh_instance_uniform_scaling: false,
            mesh_instance_max_count: None,
            uniform_size: 576,
            uniform_bindings: vec![
                UniformBinding {
                    offset: 0,
                    size: 80,
                },
                UniformBinding {
                    offset: 256,
                    size: 160,
                },
                UniformBinding {
                    offset: 512,
                    size: 64,
                },
            ],
            textures: Vec::new(),
            texture_dims: Vec::new(),
            texture_slots: Vec::new(),
            texture_aliases: Vec::new(),
            main_texture: false,
            buffers: Vec::new(),
            keywords: Vec::new(),
            blocks: vec![
                BlockRange {
                    name: "UnityPerDraw".into(),
                    offset: 0,
                    size: 64,
                },
                BlockRange {
                    name: "UnityPerMaterial".into(),
                    offset: 64,
                    size: 16,
                },
                BlockRange {
                    name: "$Global".into(),
                    offset: 256,
                    size: 160,
                },
                BlockRange {
                    name: "AdditionalLights".into(),
                    offset: 512,
                    size: 64,
                },
            ],
            extra_vertex_inputs: false,
            vertex_inputs: true,
            framebuffer_fetch: 0,
        };

        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        let by_name: rustc_hash::FxHashMap<String, u32> = program
            .uniforms
            .iter()
            .enumerate()
            .map(|(i, u)| (u.name.clone(), i as u32))
            .collect();
        let layout = ProgramBindings::of(&program, &plan);
        assert!(layout.bindings[0].has_per_draw_builtin);
        assert!(!layout.bindings[1].has_per_draw_builtin && layout.bindings[1].has_camera_builtin);
        assert!(!layout.bindings[2].has_per_draw_builtin && !layout.bindings[2].has_camera_builtin);

        let block_a = material_block(&program, &plan, &layout, &mat_a, &lab, true);
        let block_b = material_block(&program, &plan, &layout, &mat_b, &lab, true);
        assert!(block_a.binding_material_dependent[0]);
        assert!(!block_a.binding_material_dependent[1]);
        assert!(!block_a.binding_material_dependent[2]);

        let ident = [
            1.0f32, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
        ];
        let mut view1 = ident;
        view1[12] = 3.0;
        view1[13] = 4.0;
        view1[14] = -5.0;
        let mut view2 = ident;
        view2[12] = -9.0;
        let mut proj = ident;
        proj[0] = 1.5;
        proj[5] = 2.0;
        proj[10] = -1.02;
        proj[11] = -1.0;
        proj[14] = -0.2;
        proj[15] = 0.0;

        let mut model1 = ident;
        model1[12] = 10.0;
        let mut model2 = ident;
        model2[12] = 20.0;

        let globals1 = vec![
            ("_MainLightIntensity".to_string(), PropertyValue::Float(2.5)),
            (
                "_AdditionalLightsColor".to_string(),
                PropertyValue::Array {
                    components: 4,
                    data: vec![1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0].into(),
                },
            ),
        ];
        let mut globals_index1 = unbound_runtime::render_protocol::owned::NameIndex::default();
        globals_index1.refresh(&globals1);
        let size = |_: &str| None;
        let none_cb = |_: &str| None;

        let base = DrawValues {
            gizmo_draw: false,
            color: [1.0; 4],
            sprite_color: [1.0; 4],
            model: model1,
            view: view1,
            proj,
            gpu_proj: proj,
            flipped: false,
            reversed_z: true,
            camera_view: view1,
            camera_proj: proj,
            screen: [1280.0, 720.0],
            time: 1.5,
            linear: true,
            material: &mat_a,
            overrides: &[],
            hidden: None,
            lab: &lab,
            globals: &globals1,
            globals_index: &globals_index1,
            globals_revision: 1,
            textures_generation: 1,
            main_texture_handle: 0,
            main_alpha_only: false,
            texture_size: &size,
            constant_buffer: &none_cb,
            instance: None,
            instance_empty: false,
            element: None,
        };

        // CachedCamera must match uncached builtin_value bit-for-bit.
        let cam1 = CachedCamera::compute(1, CameraInput::from_values(&base), &base);
        for which in [
            Builtin::MatrixInvV,
            Builtin::MatrixVP,
            Builtin::InvProjection,
            Builtin::WorldToCamera,
            Builtin::CameraToWorld,
            Builtin::CameraPos,
            Builtin::ProjectionParams,
            Builtin::ScreenParams,
            Builtin::ZBufferParams,
            Builtin::OrthoParams,
            Builtin::Time,
            Builtin::SinTime,
            Builtin::CosTime,
        ] {
            let a = builtin_value(which, "", &base, &[], None).unwrap();
            let b = builtin_value(which, "", &base, &[], Some(&cam1)).unwrap();
            assert_eq!(
                a.as_slice().iter().map(|x| x.to_bits()).collect::<Vec<_>>(),
                b.as_slice().iter().map(|x| x.to_bits()).collect::<Vec<_>>(),
                "CachedCamera mismatch for {which:?}"
            );
        }

        let mut data = Vec::new();
        let mut shared_constants = HashMap::default();
        let mut shared_bindings = HashMap::default();
        let mut empty_slot = None;
        let mut scratch = String::new();
        let mut override_scratch = Vec::new();

        // Draw 1 (mat_a, model1) populates bindings 0, 1, 2.
        let d1 = push_program_uniforms(
            7,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block_a),
            &base,
            &cam1,
            &mut data,
            &mut shared_constants,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        let len_after_d1 = data.len();

        // Draw 2 (mat_b, model2, same camera & globals) MUST reuse BOTH binding 1 and binding 2!
        let v2 = DrawValues {
            model: model2,
            material: &mat_b,
            ..base
        };
        let d2 = push_program_uniforms(
            7,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block_b),
            &v2,
            &cam1,
            &mut data,
            &mut shared_constants,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        assert_ne!(
            d1.offsets[0], d2.offsets[0],
            "per-draw binding stays distinct"
        );
        assert_eq!(
            d1.offsets[1], d2.offsets[1],
            "shared $Global reused across materials"
        );
        assert_eq!(
            d1.offsets[2], d2.offsets[2],
            "shared AdditionalLights reused across materials"
        );
        assert_eq!(
            data.len() - len_after_d1,
            DRAW_SLOT as usize,
            "second draw only allocates per-draw binding 0"
        );

        // Verify bit-exact parity with uncached full fill for all read uniforms of draw 2.
        let mut full2 = vec![0u8; program.uniform_size as usize];
        fill(&program, &plan, &v2, &mut full2);
        for (k, bp) in layout.bindings.iter().enumerate() {
            let at = d2.offsets[k] as usize;
            let lo = bp.offset as usize;
            let len = if k == 1 { 96 } else { bp.size as usize };
            assert_eq!(
                &data[at..at + len],
                &full2[lo..lo + len],
                "binding {k} read bytes must match uncached fill"
            );
        }

        // Camera change invalidates binding 1 (has_camera_builtin) while binding 2 stays shared!
        let v3 = DrawValues {
            view: view2,
            camera_view: view2,
            ..base
        };
        let cam2 = CachedCamera::compute(2, CameraInput::from_values(&v3), &v3);
        let d3 = push_program_uniforms(
            7,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block_a),
            &v3,
            &cam2,
            &mut data,
            &mut shared_constants,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        assert_ne!(
            d3.offsets[1], d1.offsets[1],
            "camera change invalidates $Global"
        );
        assert_eq!(
            d3.offsets[2], d1.offsets[2],
            "camera-free AdditionalLights stays shared"
        );

        // Globals revision change invalidates both binding 1 and binding 2.
        let globals2 = vec![
            ("_MainLightIntensity".to_string(), PropertyValue::Float(9.0)),
            (
                "_AdditionalLightsColor".to_string(),
                PropertyValue::Array {
                    components: 4,
                    data: vec![9.0, 8.0, 7.0, 6.0].into(),
                },
            ),
        ];
        let mut globals_index2 = unbound_runtime::render_protocol::owned::NameIndex::default();
        globals_index2.refresh(&globals2);
        let v4 = DrawValues {
            globals: &globals2,
            globals_index: &globals_index2,
            globals_revision: 2,
            ..base
        };
        let d4 = push_program_uniforms(
            7,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block_a),
            &v4,
            &cam1,
            &mut data,
            &mut shared_constants,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        assert_ne!(d4.offsets[1], d1.offsets[1]);
        assert_ne!(d4.offsets[2], d1.offsets[2]);

        // Draw override targeting a uniform in binding 1 bypasses cache without poisoning d1.
        let overrides = [(
            "_MainLightIntensity".to_string(),
            PropertyValue::Float(42.0),
        )];
        let v5 = DrawValues {
            overrides: &overrides,
            ..base
        };
        let d5 = push_program_uniforms(
            7,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block_a),
            &v5,
            &cam1,
            &mut data,
            &mut shared_constants,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        assert_ne!(
            d5.offsets[1], d1.offsets[1],
            "override bypasses shared binding 1"
        );
        assert_eq!(
            d5.offsets[2], d1.offsets[2],
            "unaffected binding 2 stays shared"
        );
        assert_eq!(
            f32::from_le_bytes(
                data[d5.offsets[1] as usize + 80..d5.offsets[1] as usize + 84]
                    .try_into()
                    .unwrap()
            ),
            42.0
        );
        assert_eq!(
            f32::from_le_bytes(
                data[d1.offsets[1] as usize + 80..d1.offsets[1] as usize + 84]
                    .try_into()
                    .unwrap()
            ),
            2.5
        );
    }

    #[test]
    fn affine_inverse_matches_general_inverse_and_edge_cases() {
        let cases: [Mat; 6] = [
            // Identity
            [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ],
            // Non-uniform scale + translation
            [
                2.5, 0.0, 0.0, 0.0, 0.0, -0.5, 0.0, 0.0, 0.0, 0.0, 4.0, 0.0, 12.25, -7.5, 3.125,
                1.0,
            ],
            // Rotation + shear + mirrored scale + large translation
            [
                0.8, 0.6, -0.1, 0.0, -0.5, 0.7, 0.4, 0.0, 0.25, -0.35, -1.2, 0.0, 1024.5, -512.25,
                256.75, 1.0,
            ],
            // Singular affine matrix (zero scale axis) -> identity
            [
                1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 5.0, 6.0, 7.0, 1.0,
            ],
            // Perspective projection (non-affine bottom row) -> general fallback
            [
                1.5, 0.0, 0.0, 0.0, 0.0, 2.0, 0.0, 0.0, 0.0, 0.0, -1.02, -1.0, 0.0, 0.0, -0.2, 0.0,
            ],
            // General non-affine matrix with non-zero m[3]/m[7]/m[11]
            [
                1.2, 0.1, 0.0, 0.05, -0.2, 0.9, 0.3, -0.02, 0.0, -0.1, 1.1, 0.04, 3.0, -2.0, 1.5,
                1.3,
            ],
        ];
        for (idx, m) in cases.iter().enumerate() {
            let fast = inverse(m);
            let general = inverse_general(m);
            // The rotation and scale part of an affine matrix's inverse is the general one's,
            // bit for bit (the translation is computed differently).
            for k in [0, 1, 2, 4, 5, 6, 8, 9, 10] {
                assert_eq!(
                    fast[k].to_bits(),
                    general[k].to_bits(),
                    "case {idx} elem {k}"
                );
            }
            for (k, (&a, &b)) in fast.iter().zip(general.iter()).enumerate() {
                let tol = 1e-5 * b.abs().max(1.0);
                assert!(
                    (a - b).abs() <= tol,
                    "case {idx} elem {k}: fast={a} vs general={b}"
                );
            }
        }
    }

    #[test]
    fn prepadded_material_block_and_single_pass_overrides_match_full_fill() {
        let program = Compiled {
            vertex: String::new(),
            fragment: String::new(),
            vertex_entry: String::new(),
            fragment_entry: String::new(),
            uniforms: vec![
                uniform("_Color", 0, 4, 1),
                uniform("_Spec", 16, 4, 1),
                uniform("_MainTex_ST", 32, 4, 1),
            ],
            instanced_properties: Vec::new(),
            mesh_instance_stride: None,
            mesh_instance_uniform_scaling: false,
            mesh_instance_max_count: None,
            uniform_size: 48,
            uniform_bindings: vec![unbound_shaders::UniformBinding {
                offset: 0,
                size: 48,
            }],
            textures: Vec::new(),
            texture_dims: Vec::new(),
            texture_slots: Vec::new(),
            texture_aliases: Vec::new(),
            main_texture: false,
            buffers: Vec::new(),
            keywords: Vec::new(),
            blocks: Vec::new(),
            extra_vertex_inputs: false,
            vertex_inputs: true,
            framebuffer_fetch: 0,
        };
        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        let by_name: rustc_hash::FxHashMap<String, u32> = program
            .uniforms
            .iter()
            .enumerate()
            .map(|(i, u)| (u.name.clone(), i as u32))
            .collect();
        let layout = ProgramBindings::of(&program, &plan);
        let lab = ShaderLab::default();
        let mat = Material {
            shader: String::new(),
            render_queue: 2000,
            keywords: Vec::new(),
            properties: vec![(
                "_Color".to_string(),
                PropertyValue::Vector([0.2, 0.3, 0.4, 1.0]),
            )],
            index: Default::default(),
        };
        let block = material_block(&program, &plan, &layout, &mat, &lab, false);
        assert_eq!(
            block.binding_bytes[0].len(),
            DRAW_SLOT as usize,
            "non-empty static binding bytes are pre-padded to DRAW_SLOT"
        );
        assert!(
            block.binding_bytes[0][48..].iter().all(|&b| b == 0),
            "padding past binding size stays zero"
        );

        let overrides = vec![
            ("_Unused".to_string(), PropertyValue::Buffer(99)),
            (
                "_Color".to_string(),
                PropertyValue::Vector([1.0, 0.0, 0.0, 1.0]),
            ),
            (
                "_Color".to_string(),
                PropertyValue::Vector([0.0, 1.0, 0.0, 1.0]),
            ),
            (
                "_Spec".to_string(),
                PropertyValue::Vector([9.0, 9.0, 9.0, 9.0]),
            ),
            (
                "_MainTex".to_string(),
                PropertyValue::Texture {
                    handle: 5,
                    st: [2.0, 3.0, 0.5, 0.25],
                },
            ),
        ];
        let globals: Vec<(String, PropertyValue)> = Vec::new();
        let globals_index = unbound_runtime::render_protocol::owned::NameIndex::default();
        let size = |_: &str| None;
        let cb = |_: &str| None;
        let values = DrawValues {
            model: [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ],
            gizmo_draw: false,
            color: [1.0; 4],
            sprite_color: [1.0; 4],
            view: [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ],
            proj: [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ],
            gpu_proj: [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ],
            flipped: false,
            reversed_z: false,
            camera_view: [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ],
            camera_proj: [
                1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
            ],
            screen: [640.0, 480.0],
            time: 1.0,
            linear: false,
            material: &mat,
            overrides: &overrides,
            hidden: Some("_Spec"),
            lab: &lab,
            globals: &globals,
            globals_index: &globals_index,
            globals_revision: 1,
            textures_generation: 1,
            main_texture_handle: 5,
            main_alpha_only: false,
            texture_size: &size,
            constant_buffer: &cb,
            instance: None,
            instance_empty: false,
            element: None,
        };
        let cam = CachedCamera::compute(1, CameraInput::from_values(&values), &values);
        let mut data = Vec::new();
        let mut shared_constants = HashMap::default();
        let mut shared_bindings = HashMap::default();
        let mut empty_slot = None;
        let mut scratch = String::new();
        let mut override_scratch = Vec::new();
        let d = push_program_uniforms(
            0,
            &program,
            &plan,
            &by_name,
            &layout,
            Some(&block),
            &values,
            &cam,
            &mut data,
            &mut shared_constants,
            &mut shared_bindings,
            &mut empty_slot,
            &mut scratch,
            &mut override_scratch,
        );
        let mut full = vec![0u8; 48];
        fill(&program, &plan, &values, &mut full);
        let at = d.offsets[0] as usize;
        assert_eq!(&data[at..at + 48], &full[..]);
        assert!(
            data[at + 48..at + DRAW_SLOT as usize]
                .iter()
                .all(|&b| b == 0)
        );
    }

    const BOUND: [u8; 16] = {
        let (a, b) = (7.0f32.to_le_bytes(), 8.0f32.to_le_bytes());
        [
            a[0], a[1], a[2], a[3], 0, 0, 0, 0, 0, 0, 0, 0, b[0], b[1], b[2], b[3],
        ]
    };
}

/// The bind group layout's texel type of a slot's texture.
pub fn sample_type(kind: unbound_shaders::SampleKind) -> wgpu::TextureSampleType {
    use unbound_shaders::SampleKind;
    match kind {
        SampleKind::Float => wgpu::TextureSampleType::Float { filterable: true },
        SampleKind::Sint => wgpu::TextureSampleType::Sint,
        SampleKind::Uint => wgpu::TextureSampleType::Uint,
        SampleKind::Depth => wgpu::TextureSampleType::Depth,
    }
}
