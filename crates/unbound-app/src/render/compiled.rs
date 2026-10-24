//! Compiled programs (by `unbound-shaders`): their modules and pipelines, and each draw's uniform
//! block, filled by name with Unity's built-in variables, the material's properties and global
//! shader properties.
use super::pipelines::{DRAW_SLOT, PipelineKey, create_pipeline};
use crate::shaderlab::{PropertyDefault, ShaderLab};
use std::{
    collections::{HashMap, HashSet},
    sync::Arc,
};
use unbound_runtime::render_protocol::owned::{Material, PropertyValue};
use unbound_shaders::{Compiled, Program, Scalar, Settings, ShaderCompiler, TextureDim};

struct ProgramGpu {
    program: Arc<Compiled>,
    vertex: wgpu::ShaderModule,
    fragment: wgpu::ShaderModule,
    uniform_layout: wgpu::BindGroupLayout,
    pub texture_layout: wgpu::BindGroupLayout,
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
    /// The target's size in pixels.
    pub screen: [f32; 2],
    pub time: f32,
    pub linear: bool,
    pub material: &'a Material,
    pub lab: &'a ShaderLab,
    /// Global shader properties (`Shader.SetGlobal*`, lights, ambient).
    pub globals: &'a [(String, PropertyValue)],
    /// Whether the main texture is alpha-only (UI text): `_TextureSampleAdd` defaults to white.
    pub main_alpha_only: bool,
    /// A texture property's texture size (width, height), when one is bound.
    pub texture_size: &'a dyn Fn(&str) -> Option<(u32, u32)>,
}

pub fn view_dimension(dim: TextureDim) -> wgpu::TextureViewDimension {
    match dim {
        TextureDim::D2 => wgpu::TextureViewDimension::D2,
        TextureDim::D2Array => wgpu::TextureViewDimension::D2Array,
        TextureDim::Cube => wgpu::TextureViewDimension::Cube,
        TextureDim::D3 => wgpu::TextureViewDimension::D3,
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
            ids: HashMap::new(),
            failed: HashSet::new(),
            data: Vec::new(),
            used: HashSet::new(),
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
            texture_entries.push(layout_entry(
                2 * k,
                wgpu::BindingType::Texture {
                    sample_type: wgpu::TextureSampleType::Float { filterable: true },
                    view_dimension: view_dimension(dim),
                    multisampled: false,
                },
            ));
            texture_entries.push(layout_entry(
                2 * k + 1,
                wgpu::BindingType::Sampler(wgpu::SamplerBindingType::Filtering),
            ));
        }
        let texture_layout = device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("unbound program textures"),
            entries: &texture_entries,
        });
        let layout = device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("unbound program"),
            bind_group_layouts: &[
                Some(&uniform_layout),
                Some(&texture_layout),
                Some(main_texture_layout),
            ],
            immediate_size: 0,
        });
        let gpu = ProgramGpu {
            vertex: module(&format!("{shader} (vertex)"), &program.vertex),
            fragment: module(&format!("{shader} (fragment)"), &program.fragment),
            program,
            uniform_layout,
            texture_layout,
            layout,
            pipelines: HashMap::new(),
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
        );
        gpu.pipelines.insert(key, pipeline.clone());
        pipeline
    }

    pub fn begin_frame(&mut self) {
        self.data.clear();
        self.used.clear();
    }

    /// A draw's uniform block; returns its offset in this frame's buffer.
    pub fn push(&mut self, id: u32, values: &DrawValues) -> u32 {
        self.used.insert(id);
        let program = self.programs[id as usize].program.clone();
        let offset = self.data.len();
        let size = (program.uniform_size.max(16) as u64).div_ceil(DRAW_SLOT) * DRAW_SLOT;
        self.data.resize(offset + size as usize, 0);
        fill(
            &program,
            values,
            &mut self.data[offset..offset + program.uniform_size as usize],
        );
        offset as u32
    }

    /// Uploads the frame's uniform blocks; the group-0 bind group of each program used.
    pub fn upload(
        &mut self,
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        draws: &wgpu::Buffer,
        draw_size: u64,
    ) -> HashMap<u32, wgpu::BindGroup> {
        let mut groups = HashMap::new();
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
fn property(material: &Material, lab: &ShaderLab, name: &str, linear: bool) -> Option<[f32; 4]> {
    let value = match material.get(name) {
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
fn texture_st(material: &Material, name: &str) -> [f32; 4] {
    match material.get(name) {
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
fn value(name: &str, v: &DrawValues) -> Option<Vec<f32>> {
    let t = v.time;
    let view_inverse = || inverse(&v.view);
    let (near, far, ortho) = clip_planes(&v.proj);
    let vec = |a: [f32; 4]| Some(a.to_vec());
    let mat = |m: Mat| Some(m.to_vec());
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
    match name {
        "unity_ObjectToWorld" => mat(v.model),
        "unity_WorldToObject" => mat(inverse(&v.model)),
        "unity_MatrixV" => mat(v.view),
        "unity_MatrixInvV" => mat(view_inverse()),
        "unity_MatrixVP" => mat(mul(&v.proj, &v.view)),
        "glstate_matrix_projection" | "unity_CameraProjection" => mat(v.proj),
        "unity_CameraInvProjection" => mat(inverse(&v.proj)),
        "unity_WorldToCamera" => mat(mul(&flip_z, &v.view)),
        "unity_CameraToWorld" => mat(inverse(&mul(&flip_z, &v.view))),
        "glstate_matrix_transpose_modelview0" => mat(transpose(&mul(&v.view, &v.model))),
        "glstate_matrix_invtrans_modelview0" => mat(transpose(&inverse(&mul(&v.view, &v.model)))),
        "glstate_matrix_modelview0" => mat(mul(&v.view, &v.model)),
        "_Time" => vec([t / 20.0, t, t * 2.0, t * 3.0]),
        "_SinTime" => vec([(t / 8.0).sin(), (t / 4.0).sin(), (t / 2.0).sin(), t.sin()]),
        "_CosTime" => vec([(t / 8.0).cos(), (t / 4.0).cos(), (t / 2.0).cos(), t.cos()]),
        "unity_DeltaTime" => vec([1.0 / 60.0, 60.0, 1.0 / 60.0, 60.0]),
        "_WorldSpaceCameraPos" => {
            let i = view_inverse();
            vec([i[12], i[13], i[14], 1.0])
        }
        "_ProjectionParams" => vec([1.0, near, far, 1.0 / far]),
        "_ScreenParams" => {
            let [w, h] = v.screen;
            vec([w, h, 1.0 + 1.0 / w, 1.0 + 1.0 / h])
        }
        "_ZBufferParams" => {
            let x = 1.0 - far / near;
            let y = far / near;
            vec([x, y, x / far, y / far])
        }
        "unity_OrthoParams" => vec([
            1.0 / v.proj[0],
            1.0 / v.proj[5],
            0.0,
            if ortho { 1.0 } else { 0.0 },
        ]),
        "unity_WorldTransformParams" => vec([0.0, 0.0, 0.0, 1.0]),
        "unity_LODFade" => vec([1.0, 1.0, 0.0, 0.0]),
        _ => {
            if let Some(value) = property(v.material, v.lab, name, v.linear) {
                return vec(value);
            }
            if let Some((_, value)) = v.globals.iter().find(|(n, _)| n == name) {
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
            let material = v.material;
            if let Some(texture) = name.strip_suffix("_ST") {
                return vec(texture_st(material, texture));
            }
            if let Some(texture) = name.strip_suffix("_TexelSize") {
                let (w, h) = (v.texture_size)(texture).unwrap_or((4, 4));
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
                _ => None,
            }
        }
    }
}

/// Writes a program's uniform block for a draw. Members nothing provides stay zero: arrays (no
/// material property or built-in Unbound fills is one) and unknown names.
fn fill(program: &Compiled, v: &DrawValues, out: &mut [u8]) {
    for u in program.uniforms.iter().filter(|u| u.count == 1) {
        let Some(values) = value(&u.name, v) else {
            continue;
        };
        // A matrix's columns are 8 bytes apart with 2 rows, 16 otherwise; values are 4x4.
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
            uniform_size: 160,
            textures: Vec::new(),
            texture_dims: Vec::new(),
            main_texture: true,
            keywords: Vec::new(),
        };
        let size = |name: &str| (name == "_MainTex").then_some((64, 32));
        let values = DrawValues {
            model,
            view: model,
            proj: model,
            screen: [960.0, 540.0],
            time: 1.0,
            linear: true,
            material: &material,
            lab: &lab,
            globals: &[("_Global".to_string(), PropertyValue::Float(3.0))],
            main_alpha_only: false,
            texture_size: &size,
        };
        let mut out = vec![0u8; 160];
        fill(&program, &values, &mut out);
        let f = |at: usize| f32::from_le_bytes(out[at..at + 4].try_into().unwrap());
        assert_eq!(f(48), 5.0, "the model's translation");
        // A Color property in linear space: converted (0.5 → 0.214); alpha is not.
        assert!((f(64) - 0.214).abs() < 0.001 && f(76) == 1.0);
        assert_eq!(f(80), 0.25);
        assert_eq!([f(96), f(100), f(104)], [2.0, 3.0, 0.5]);
        assert_eq!([f(112), f(120), f(124)], [1.0 / 64.0, 64.0, 32.0]);
        assert_eq!(f(128), -2e10);
        assert_eq!(f(144), 3.0, "a global property");
    }
}
