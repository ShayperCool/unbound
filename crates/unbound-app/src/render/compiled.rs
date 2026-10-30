//! Compiled programs (by `unbound-shaders`): their modules and pipelines, and each draw's uniform
//! block, filled by name with Unity's built-in variables, the material's properties and global
//! shader properties.
use super::pipelines::{DRAW_SLOT, PipelineKey, create_pipeline};
use crate::shaderlab::{PropertyDefault, ShaderLab};
use rustc_hash::{FxHashMap as HashMap, FxHashSet as HashSet};
use std::sync::Arc;
use unbound_runtime::render_protocol::owned::{Material, PropertyValue};
use unbound_shaders::{Compiled, Program, Scalar, Settings, ShaderCompiler, TextureDim};

struct ProgramGpu {
    program: Arc<Compiled>,
    /// Where each of the program's uniforms comes from, decided once (see [`Source`]).
    plan: Vec<Source>,
    /// The uniforms by name.
    by_name: rustc_hash::FxHashMap<String, u32>,
    vertex: wgpu::ShaderModule,
    fragment: wgpu::ShaderModule,
    uniform_layout: wgpu::BindGroupLayout,
    pub texture_layout: wgpu::BindGroupLayout,
    /// Group 3: the program's buffers (none without any).
    buffer_layout: Option<wgpu::BindGroupLayout>,
    layout: wgpu::PipelineLayout,
    pipelines: HashMap<PipelineKey, wgpu::RenderPipeline>,
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
}

/// What a draw's uniform block is filled from.
pub struct DrawValues<'a> {
    pub model: [f32; 16],
    pub view: [f32; 16],
    pub proj: [f32; 16],
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
    pub lab: &'a ShaderLab,
    /// Global shader properties (`Shader.SetGlobal*`, lights, ambient).
    pub globals: &'a [(String, PropertyValue)],
    /// Where each global property is in `globals`.
    pub globals_index: &'a unbound_runtime::render_protocol::owned::NameIndex,
    /// Whether the main texture is alpha-only (UI text): `_TextureSampleAdd` defaults to white.
    pub main_alpha_only: bool,
    /// A texture property's texture size (width, height), when one is bound.
    pub texture_size: &'a dyn Fn(&str) -> Option<(u32, u32)>,
    /// The bytes of the constant buffer bound to a cbuffer's name, when one is.
    pub constant_buffer: &'a dyn Fn(&str) -> Option<&'a [u8]>,
}

impl DrawValues<'_> {
    /// A property of the draw, else of its material.
    pub fn get(&self, name: &str) -> Option<&PropertyValue> {
        self.overrides
            .iter()
            .rev()
            .find(|(n, _)| n == name)
            .map(|(_, v)| v)
            .or_else(|| self.material.get(name))
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
        }
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
        let uniform_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound program uniforms"),
            entries: &[
                layout_entry(0, dynamic_uniform()),
                layout_entry(1, dynamic_uniform()),
            ],
        });
        let mut texture_entries = Vec::new();
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
            texture_entries.push(layout_entry(
                2 * k,
                wgpu::BindingType::Texture {
                    sample_type: if slot.depth {
                        wgpu::TextureSampleType::Depth
                    } else {
                        wgpu::TextureSampleType::Float { filterable: true }
                    },
                    view_dimension: view_dimension(dim),
                    multisampled: false,
                },
            ));
            texture_entries.push(layout_entry(
                2 * k + 1,
                wgpu::BindingType::Sampler(if slot.comparison {
                    wgpu::SamplerBindingType::Comparison
                } else {
                    wgpu::SamplerBindingType::Filtering
                }),
            ));
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
                Some(main_texture_layout),
                buffer_layout.as_ref(),
            ],
            immediate_size: 0,
        });
        let gpu = ProgramGpu {
            plan: program
                .uniforms
                .iter()
                .map(|u| Source::of(&u.name))
                .collect(),
            by_name: program
                .uniforms
                .iter()
                .enumerate()
                .map(|(i, u)| (u.name.clone(), i as u32))
                .collect(),
            vertex: module(&format!("{shader} (vertex)"), &program.vertex),
            fragment: module(&format!("{shader} (fragment)"), &program.fragment),
            program,
            uniform_layout,
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

    pub fn texture_layout(&self, id: u32) -> &wgpu::BindGroupLayout {
        &self.programs[id as usize].texture_layout
    }

    /// The layout of a program's buffers (group 3), when it has any.
    pub fn buffer_layout(&self, id: u32) -> Option<&wgpu::BindGroupLayout> {
        self.programs[id as usize].buffer_layout.as_ref()
    }

    pub fn pipeline(
        &mut self,
        device: &wgpu::Device,
        id: u32,
        key: PipelineKey,
    ) -> wgpu::RenderPipeline {
        let gpu = &mut self.programs[id as usize];
        if let Some(p) = gpu.pipelines.get(&key) {
            return p.clone();
        }
        let pipeline = create_pipeline(
            device,
            &gpu.layout,
            (&gpu.vertex, &gpu.program.vertex_entry),
            (&gpu.fragment, &gpu.program.fragment_entry),
            key,
            gpu.program.extra_vertex_inputs,
            super::pipelines::fragment_outputs(&gpu.program.fragment),
        );
        gpu.pipelines.insert(key, pipeline.clone());
        pipeline
    }

    pub fn begin_frame(&mut self) {
        self.data.clear();
        self.used.clear();
    }

    /// A draw's uniform block; returns its offset in this frame's buffer.
    pub fn push(&mut self, id: u32, values: &DrawValues, block: Option<&MaterialBlock>) -> u32 {
        self.used.insert(id);
        let gpu = &self.programs[id as usize];
        let offset = self.data.len();
        let size = (gpu.program.uniform_size.max(16) as u64).div_ceil(DRAW_SLOT) * DRAW_SLOT;
        self.data.resize(offset + size as usize, 0);
        let out = &mut self.data[offset..offset + gpu.program.uniform_size as usize];
        match block {
            Some(block) => {
                let n = block.bytes.len().min(out.len());
                out[..n].copy_from_slice(&block.bytes[..n]);
                fill(&gpu.program, &gpu.plan, values, out, Some(&block.dynamic));
                // The draw's own properties over the material's.
                for (name, value) in values.overrides {
                    let mut redo = |uniform: &str| {
                        if let Some(&i) = gpu.by_name.get(uniform) {
                            write_uniform(&gpu.program, &gpu.plan, i as usize, values, out);
                        }
                    };
                    redo(name);
                    if matches!(value, PropertyValue::Texture { .. }) {
                        redo(&format!("{name}_ST"));
                        redo(&format!("{name}_TexelSize"));
                    }
                }
            }
            None => fill(&gpu.program, &gpu.plan, values, out, None),
        }
        offset as u32
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
        let program = &gpu.program;
        let mut bytes = vec![0u8; program.uniform_size as usize];
        let mut dynamic = Vec::new();
        for (i, (u, source)) in program.uniforms.iter().zip(&gpu.plan).enumerate() {
            if u.count != 1 {
                continue;
            }
            match static_value(&u.name, *source, material, lab, linear) {
                Some(value) => write_value(u, &value, &mut bytes),
                None => dynamic.push(i as u32),
            }
        }
        MaterialBlock { bytes, dynamic }
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

    /// Uploads the frame's uniform blocks; the group-0 bind group of each program used.
    pub fn upload(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        draws: &wgpu::Buffer,
        draw_size: u64,
    ) -> HashMap<u32, wgpu::BindGroup> {
        let mut groups = HashMap::default();
        if self.used.is_empty() {
            return groups;
        }
        let bytes = self.data.len() as u64;
        if self.buffer.as_ref().is_none_or(|b| b.size() < bytes) {
            self.buffer = Some(device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound program uniforms"),
                size: bytes.next_power_of_two().max(DRAW_SLOT * 64),
                usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            }));
        }
        let buffer = self.buffer.as_ref().unwrap();
        queue.write_buffer(buffer, 0, &self.data);
        for &id in &self.used {
            let gpu = &self.programs[id as usize];
            let size = gpu.program.uniform_size.max(16) as u64;
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("unbound program uniforms"),
                layout: &gpu.uniform_layout,
                entries: &[
                    wgpu::BindGroupEntry {
                        binding: 0,
                        resource: wgpu::BindingResource::Buffer(wgpu::BufferBinding {
                            buffer,
                            offset: 0,
                            size: wgpu::BufferSize::new(size),
                        }),
                    },
                    wgpu::BindGroupEntry {
                        binding: 1,
                        resource: wgpu::BindingResource::Buffer(wgpu::BufferBinding {
                            buffer: draws,
                            offset: 0,
                            size: wgpu::BufferSize::new(draw_size),
                        }),
                    },
                ],
            });
            groups.insert(id, group);
        }
        groups
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

fn gamma_to_linear(c: f32) -> f32 {
    if c <= 0.04045 {
        c / 12.92
    } else {
        ((c + 0.055) / 1.055).powf(2.4)
    }
}

/// A material property's value: the material's, else the shader's default.
fn property(v: &DrawValues, lab: &ShaderLab, name: &str, linear: bool) -> Option<[f32; 4]> {
    property_of(v.get(name), lab, name, linear)
}

/// A property's vector from its value (`None`: the shader's default), colours made linear.
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
    if linear && lab.colors.contains(name) {
        return Some([
            gamma_to_linear(value[0]),
            gamma_to_linear(value[1]),
            gamma_to_linear(value[2]),
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
    ObjectToWorld,
    WorldToObject,
    MatrixV,
    MatrixInvV,
    MatrixVP,
    Projection,
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

impl Source {
    fn of(name: &str) -> Source {
        use Builtin::*;
        let which = match name {
            "unity_ObjectToWorld" => ObjectToWorld,
            "unity_WorldToObject" => WorldToObject,
            "unity_MatrixV" => MatrixV,
            "unity_MatrixInvV" => MatrixInvV,
            "unity_MatrixVP" => MatrixVP,
            "glstate_matrix_projection" | "unity_CameraProjection" => Projection,
            "unity_CameraInvProjection" => InvProjection,
            "unity_WorldToCamera" => WorldToCamera,
            "unity_CameraToWorld" => CameraToWorld,
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
            properties_first: name.starts_with("unity_"),
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
    let t = v.time;
    let view_inverse = || inverse(&v.view);
    let camera_view_inverse = || inverse(&v.camera_view);
    // Unity's camera matrices: worldToCameraMatrix has OpenGL's negated z; the transform's
    // world-to-local does not.
    let flip_z = {
        let mut m = [0.0; 16];
        m[0] = 1.0;
        m[5] = 1.0;
        m[10] = -1.0;
        m[15] = 1.0;
        m
    };
    use Builtin::*;
    match which {
        ObjectToWorld => mat(v.model),
        WorldToObject => mat(inverse(&v.model)),
        MatrixV => mat(v.view),
        MatrixInvV => mat(view_inverse()),
        MatrixVP => mat(mul(&v.proj, &v.view)),
        Projection => mat(v.proj),
        InvProjection => mat(inverse(&v.proj)),
        WorldToCamera => mat(mul(&flip_z, &v.camera_view)),
        CameraToWorld => mat(inverse(&mul(&flip_z, &v.camera_view))),
        TransposeModelView => mat(transpose(&mul(&v.view, &v.model))),
        InvTransModelView => mat(transpose(&inverse(&mul(&v.view, &v.model)))),
        ModelView => mat(mul(&v.view, &v.model)),
        Time => vec([t / 20.0, t, t * 2.0, t * 3.0]),
        SinTime => vec([(t / 8.0).sin(), (t / 4.0).sin(), (t / 2.0).sin(), t.sin()]),
        CosTime => vec([(t / 8.0).cos(), (t / 4.0).cos(), (t / 2.0).cos(), t.cos()]),
        DeltaTime => vec([1.0 / 60.0, 60.0, 1.0 / 60.0, 60.0]),
        CameraPos => {
            let i = camera_view_inverse();
            vec([i[12], i[13], i[14], 1.0])
        }
        ProjectionParams => {
            let (near, far, _) = clip_planes(&v.camera_proj);
            vec([1.0, near, far, 1.0 / far])
        }
        ScreenParams => {
            let [w, h] = v.screen;
            vec([w, h, 1.0 + 1.0 / w, 1.0 + 1.0 / h])
        }
        ZBufferParams => {
            let (near, far, _) = clip_planes(&v.camera_proj);
            let x = 1.0 - far / near;
            let y = far / near;
            vec([x, y, x / far, y / far])
        }
        OrthoParams => {
            let (_, _, ortho) = clip_planes(&v.camera_proj);
            vec([
                1.0 / v.camera_proj[0],
                1.0 / v.camera_proj[5],
                0.0,
                if ortho { 1.0 } else { 0.0 },
            ])
        }
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
    if let Some(PropertyValue::Matrix(m)) = v.get(name) {
        return mat(*m);
    }
    if let Some(value) = property(v, v.lab, name, v.linear) {
        return vec(value);
    }
    if let Some(value) = v.globals_index.find(v.globals, name) {
        match value {
            PropertyValue::Float(f) => return vec([*f; 4]),
            PropertyValue::Int(i) => return vec([*i as f32; 4]),
            PropertyValue::Vector(x) => return vec(*x),
            PropertyValue::Matrix(m) => return mat(*m),
            PropertyValue::Texture { .. }
            | PropertyValue::Buffer(_)
            | PropertyValue::Array { .. }
            | PropertyValue::Image { .. } => {}
        }
    }
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
        // A sprite's renderer color and flips, which URP's sprite shaders apply in the
        // vertex program (positions scaled by the flips): Unbound's sprite meshes carry
        // both already, so they are neutral (no skinning offset: z -1).
        "unity_SpriteColor" => vec([1.0, 1.0, 1.0, 1.0]),
        "unity_SpriteProps" => vec([1.0, 1.0, -1.0, 0.0]),
        _ => None,
    }
}

/// A program's uniform block as a material fills it (see [`CompiledPrograms::material_block`]).
pub struct MaterialBlock {
    bytes: Vec<u8>,
    /// The uniforms the draw fills (built-in variables, global properties, values derived from
    /// bound textures).
    dynamic: Vec<u32>,
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

/// Writes one uniform (not an array) for a draw.
fn write_uniform(program: &Compiled, plan: &[Source], i: usize, v: &DrawValues, out: &mut [u8]) {
    let u = &program.uniforms[i];
    if u.count != 1 {
        return;
    }
    if let Some(value) = value(&u.name, plan[i], v, &program.textures) {
        write_value(u, &value, out);
    }
}

/// A value's components at a uniform's place: a matrix's columns are 8 bytes apart with 2 rows,
/// 16 otherwise; values are 4x4.
fn write_value(u: &unbound_shaders::Uniform, value: &Value, out: &mut [u8]) {
    let values = value.as_slice();
    let column_stride = if u.rows == 2 { 8 } else { 16 };
    for c in 0..u.columns as usize {
        for r in 0..u.rows as usize {
            let x = values.get(c * 4 + r).copied().unwrap_or(0.0);
            let at = u.offset as usize + c * column_stride + r * 4;
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

/// Writes a program's uniform block for a draw. Members nothing provides stay zero: arrays (no
/// material property or built-in Unbound fills is one) and unknown names.
/// With `only`, just those uniforms (and the arrays and constant buffers).
fn fill(program: &Compiled, plan: &[Source], v: &DrawValues, out: &mut [u8], only: Option<&[u32]>) {
    // Arrays: the draw's or material's array property, else the global one.
    for u in program.uniforms.iter().filter(|u| u.count > 1) {
        let value = match v.get(&u.name) {
            Some(value @ PropertyValue::Array { .. }) => Some(value),
            _ => v
                .globals_index
                .find(v.globals, &u.name)
                .filter(|value| matches!(value, PropertyValue::Array { .. })),
        };
        if let Some(PropertyValue::Array { components, data }) = value {
            write_array(u, *components, data, out);
        }
    }
    match only {
        Some(indices) => {
            for &i in indices {
                write_uniform(program, plan, i as usize, v, out);
            }
        }
        None => {
            for i in 0..program.uniforms.len() {
                write_uniform(program, plan, i, v, out);
            }
        }
    }
    // A constant buffer bound to a cbuffer's name fills the cbuffer's range.
    for block in &program.blocks {
        let Some(bytes) = (v.constant_buffer)(&block.name) else {
            continue;
        };
        let start = block.offset as usize;
        let end = (start + block.size as usize).min(out.len());
        if end > start {
            let n = (end - start).min(bytes.len());
            out[start..start + n].copy_from_slice(&bytes[..n]);
        }
    }
}

/// An array uniform's elements: `components` floats each (0: consecutive values from the
/// uniform's offset), as many as the array holds.
fn write_array(u: &unbound_shaders::Uniform, components: u32, data: &[f32], out: &mut [u8]) {
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
    let offset = u.offset as usize;
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

#[cfg(test)]
mod tests {
    use super::*;
    use unbound_shaders::Uniform;

    fn uniform(name: &str, offset: u32, rows: u32, columns: u32) -> Uniform {
        Uniform {
            name: name.into(),
            offset,
            scalar: Scalar::Float,
            rows,
            columns,
            count: 1,
            stride: 0,
        }
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
            textures: Vec::new(),
            texture_dims: Vec::new(),
            main_texture: true,
            keywords: Vec::new(),
            texture_slots: Vec::new(),
            buffers: Vec::new(),
            blocks: vec![unbound_shaders::BlockRange {
                name: "Bound".into(),
                offset: 160,
                size: 16,
            }],
            extra_vertex_inputs: false,
        };
        let size = |name: &str| (name == "_MainTex").then_some((64, 32));
        let globals_index = Default::default();
        let values = DrawValues {
            globals_index: &globals_index,
            overrides: &[],
            model,
            view: model,
            proj: model,
            camera_view: model,
            camera_proj: model,
            screen: [960.0, 540.0],
            time: 1.0,
            linear: true,
            material: &material,
            lab: &lab,
            globals: &[("_Global".to_string(), PropertyValue::Float(3.0))],
            main_alpha_only: false,
            texture_size: &size,
            constant_buffer: &|name| (name == "Bound").then_some(&BOUND[..]),
        };
        let mut out = vec![0u8; 176];
        let plan: Vec<Source> = program
            .uniforms
            .iter()
            .map(|u| Source::of(&u.name))
            .collect();
        fill(&program, &plan, &values, &mut out, None);
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
    }

    const BOUND: [u8; 16] = {
        let (a, b) = (7.0f32.to_le_bytes(), 8.0f32.to_le_bytes());
        [
            a[0], a[1], a[2], a[3], 0, 0, 0, 0, 0, 0, 0, 0, b[0], b[1], b[2], b[3],
        ]
    };
}
