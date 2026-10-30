//! GPU copies of the protocol's meshes and textures.
use bytemuck::{Pod, Zeroable};
use std::collections::HashMap;
use unbound_runtime::render_protocol::owned;

/// The one vertex layout every mesh is uploaded with (`VertexIn` in unity.wgsl).
#[repr(C)]
#[derive(Debug, Clone, Copy, Pod, Zeroable)]
pub struct Vertex {
    pub position: [f32; 3],
    pub normal: [f32; 3],
    pub tangent: [f32; 4],
    pub color: [u8; 4],
    pub uv: [[f32; 4]; 4],
}

pub const VERTEX_ATTRIBUTES: [wgpu::VertexAttribute; 8] = wgpu::vertex_attr_array![
    0 => Float32x3,
    1 => Float32x3,
    2 => Float32x4,
    3 => Unorm8x4,
    4 => Float32x4,
    5 => Float32x4,
    6 => Float32x4,
    7 => Float32x4,
];

pub fn vertex_layout() -> wgpu::VertexBufferLayout<'static> {
    wgpu::VertexBufferLayout {
        array_stride: size_of::<Vertex>() as u64,
        step_mode: wgpu::VertexStepMode::Vertex,
        attributes: &VERTEX_ATTRIBUTES,
    }
}

/// The texture coordinate channels 4..7 (`TEXCOORD4..7`), in a second vertex buffer that only
/// meshes with them have (UI Toolkit's vertices use all eight channels).
#[repr(C)]
#[derive(Debug, Clone, Copy, Pod, Zeroable)]
pub struct ExtraVertex {
    pub uv: [[f32; 4]; 4],
}

pub const EXTRA_VERTEX_ATTRIBUTES: [wgpu::VertexAttribute; 4] = wgpu::vertex_attr_array![
    8 => Float32x4,
    9 => Float32x4,
    10 => Float32x4,
    11 => Float32x4,
];

pub fn extra_vertex_layout() -> wgpu::VertexBufferLayout<'static> {
    wgpu::VertexBufferLayout {
        array_stride: size_of::<ExtraVertex>() as u64,
        step_mode: wgpu::VertexStepMode::Vertex,
        attributes: &EXTRA_VERTEX_ATTRIBUTES,
    }
}

/// A mesh's channels 4..7, interleaved; none when it has none of them.
pub fn build_extra(mesh: &owned::Mesh) -> Option<Vec<ExtraVertex>> {
    if mesh.uv[4..].iter().all(Vec::is_empty) {
        return None;
    }
    let n = mesh.positions.len();
    let mut extra = vec![ExtraVertex::zeroed(); n];
    for (i, v) in extra.iter_mut().enumerate() {
        for (k, out) in v.uv.iter_mut().enumerate() {
            if let Some(u) = mesh.uv[4 + k].get(i) {
                *out = [u.x, u.y, u.z, u.w];
            }
        }
    }
    Some(extra)
}

/// Unity's `MeshTopology`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Topology {
    Triangles,
    Lines,
    LineStrip,
    Points,
}

impl Topology {
    pub fn wgpu(self) -> wgpu::PrimitiveTopology {
        match self {
            Topology::Triangles => wgpu::PrimitiveTopology::TriangleList,
            Topology::Lines => wgpu::PrimitiveTopology::LineList,
            Topology::LineStrip => wgpu::PrimitiveTopology::LineStrip,
            Topology::Points => wgpu::PrimitiveTopology::PointList,
        }
    }
}

#[derive(Debug, Clone)]
pub struct SubMesh {
    pub start: u32,
    pub count: u32,
    pub base_vertex: i32,
    pub topology: Topology,
}

pub struct GpuMesh {
    pub vertex: wgpu::Buffer,
    /// Channels 4..7 (`ExtraVertex`), when the mesh has them.
    pub extra: Option<wgpu::Buffer>,
    pub index: wgpu::Buffer,
    pub submeshes: Vec<SubMesh>,
}

/// Interleaves a mesh and rewrites its submeshes' indices into one list (quads become triangles).
pub fn build_mesh(mesh: &owned::Mesh) -> (Vec<Vertex>, Vec<u32>, Vec<SubMesh>) {
    let n = mesh.positions.len();
    // A channel the mesh lacks gives the nearest lower one it has, or zeros (measured on the
    // 6000.0 Editor: uv and uv3 only, TEXCOORD1 reads uv and TEXCOORD3 uv3; uv3 only, TEXCOORD0
    // and 1 read zeros). The built-in plane is lightmapped by its uv so (tests/projects/baked);
    // A mesh with `uv3` drawn by a shader that reads TEXCOORD3, say.
    let sources: [Option<usize>; 4] =
        std::array::from_fn(|channel| (0..=channel).rev().find(|&c| !mesh.uv[c].is_empty()));
    let mut vertices = Vec::with_capacity(n);
    for i in 0..n {
        let p = mesh.positions[i];
        let normal = mesh
            .normals
            .get(i)
            .map_or([0.0, 0.0, -1.0], |v| [v.x, v.y, v.z]);
        let tangent = mesh
            .tangents
            .get(i)
            .map_or([1.0, 0.0, 0.0, 1.0], |v| [v.x, v.y, v.z, v.w]);
        let color = mesh
            .colors
            .get(i)
            .map_or([255; 4], |c| [c.r, c.g, c.b, c.a]);
        let mut uv = [[0.0; 4]; 4];
        for (channel, out) in uv.iter_mut().enumerate() {
            if let Some(v) = sources[channel].and_then(|source| mesh.uv[source].get(i)) {
                *out = [v.x, v.y, v.z, v.w];
            }
        }
        vertices.push(Vertex {
            position: [p.x, p.y, p.z],
            normal,
            tangent,
            color,
            uv,
        });
    }
    let mut indices = Vec::with_capacity(mesh.indices.len());
    let mut submeshes = Vec::new();
    let all;
    let packets: &[unbound_runtime::render_protocol::SubMeshPacket] = if mesh.submeshes.is_empty() {
        all = [unbound_runtime::render_protocol::SubMeshPacket {
            index_start: 0,
            index_count: mesh.indices.len() as i32,
            topology: 0,
            base_vertex: 0,
        }];
        &all
    } else {
        &mesh.submeshes
    };
    for sm in packets {
        let start = sm.index_start.max(0) as usize;
        let end = (start + sm.index_count.max(0) as usize).min(mesh.indices.len());
        let source = &mesh.indices[start.min(end)..end];
        let first = indices.len() as u32;
        let topology = match sm.topology {
            2 => Topology::Lines,
            3 => Topology::LineStrip,
            5 => Topology::Points,
            _ => Topology::Triangles,
        };
        if sm.topology == 4 {
            for quad in source.as_chunks::<4>().0 {
                indices.extend_from_slice(&[quad[0], quad[1], quad[2], quad[0], quad[2], quad[3]]);
            }
        } else {
            indices.extend_from_slice(source);
        }
        submeshes.push(SubMesh {
            start: first,
            count: indices.len() as u32 - first,
            base_vertex: sm.base_vertex,
            topology,
        });
    }
    (vertices, indices, submeshes)
}

/// Grows or reuses a buffer for `data`.
pub fn write_buffer(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
    existing: Option<wgpu::Buffer>,
    data: &[u8],
    usage: wgpu::BufferUsages,
    label: &str,
) -> wgpu::Buffer {
    let size = (data.len() as u64).max(16).next_multiple_of(4);
    let buffer = match existing {
        Some(b) if b.size() >= size => b,
        _ => device.create_buffer(&wgpu::BufferDescriptor {
            label: Some(label),
            size: size.next_power_of_two(),
            usage: usage | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        }),
    };
    if !data.is_empty() {
        if data.len().is_multiple_of(4) {
            queue.write_buffer(&buffer, 0, data);
        } else {
            let mut padded = data.to_vec();
            padded.resize(data.len().next_multiple_of(4), 0);
            queue.write_buffer(&buffer, 0, &padded);
        }
    }
    buffer
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct SamplerKey {
    pub filter: i32,
    pub wrap_u: i32,
    pub wrap_v: i32,
    pub aniso: i32,
    /// A comparison sampler's function (Unity's CompareFunction; 0: none).
    pub compare: i32,
}

pub fn create_sampler(device: &wgpu::Device, key: SamplerKey) -> wgpu::Sampler {
    let wrap = |w: i32| match w {
        1 => wgpu::AddressMode::ClampToEdge,
        2 | 3 => wgpu::AddressMode::MirrorRepeat,
        _ => wgpu::AddressMode::Repeat,
    };
    let (mag, mip) = match key.filter {
        0 => (wgpu::FilterMode::Nearest, wgpu::MipmapFilterMode::Nearest),
        1 => (wgpu::FilterMode::Linear, wgpu::MipmapFilterMode::Nearest),
        _ => (wgpu::FilterMode::Linear, wgpu::MipmapFilterMode::Linear),
    };
    // Anisotropy needs linear filtering everywhere (wgpu): a bilinear texture with an
    // anisotropic level (which OpenGL and D3D filter anisotropically) blends its mips too.
    let aniso = if key.filter >= 1 {
        key.aniso.clamp(1, 16) as u16
    } else {
        1
    };
    let mip = if aniso > 1 {
        wgpu::MipmapFilterMode::Linear
    } else {
        mip
    };
    device.create_sampler(&wgpu::SamplerDescriptor {
        label: Some("unbound texture sampler"),
        address_mode_u: wrap(key.wrap_u),
        address_mode_v: wrap(key.wrap_v),
        address_mode_w: wgpu::AddressMode::Repeat,
        mag_filter: mag,
        min_filter: mag,
        mipmap_filter: mip,
        anisotropy_clamp: aniso,
        compare: compare_function(key.compare),
        ..Default::default()
    })
}

/// Unity's CompareFunction (0 disabled) as wgpu's.
pub fn compare_function(f: i32) -> Option<wgpu::CompareFunction> {
    Some(match f {
        1 => wgpu::CompareFunction::Never,
        2 => wgpu::CompareFunction::Less,
        3 => wgpu::CompareFunction::Equal,
        4 => wgpu::CompareFunction::LessEqual,
        5 => wgpu::CompareFunction::Greater,
        6 => wgpu::CompareFunction::NotEqual,
        7 => wgpu::CompareFunction::GreaterEqual,
        8 => wgpu::CompareFunction::Always,
        _ => return None,
    })
}

/// The format of a depth-only render target's depth copy: filterable 32-bit floats where the
/// device filters them, else half floats.
pub fn depth_copy_format(device: &wgpu::Device) -> wgpu::TextureFormat {
    if device
        .features()
        .contains(wgpu::Features::FLOAT32_FILTERABLE)
    {
        wgpu::TextureFormat::R32Float
    } else {
        wgpu::TextureFormat::R16Float
    }
}

pub struct GpuTexture {
    pub texture: wgpu::Texture,
    /// Raw view (gamma projects; data textures).
    pub view: wgpu::TextureView,
    /// sRGB view of color textures, sampled in linear projects.
    pub srgb_view: Option<wgpu::TextureView>,
    pub sampler_key: SamplerKey,
    pub depth: Option<wgpu::TextureView>,
    /// The depth buffer's depth, for shaders that sample it.
    pub depth_sample: Option<wgpu::TextureView>,
    /// A depth-only render target's depth as a float color texture, what sampling the texture
    /// reads (Unity's depth textures read as their depth): copied after the passes that draw
    /// into it.
    pub depth_copy: Option<wgpu::TextureView>,
    /// A render target's depth buffer (its layers' views are made as they are drawn into).
    pub depth_texture: Option<wgpu::Texture>,
    /// A render target's multisampled color (raw and sRGB views) that passes resolve into the
    /// texture, and its sample count (1: none).
    pub msaa: Option<(wgpu::TextureView, Option<wgpu::TextureView>)>,
    pub samples: u32,
    /// Images (6 for a cubemap) and whether the views are a cube's.
    pub layers: u32,
    pub cube: bool,
    /// A CubemapArray: its view is a cube array.
    pub cube_array: bool,
    /// A 3D texture (`layers` is its depth).
    pub volume: bool,
    pub width: u32,
    pub height: u32,
    pub format: i32,
    pub mip_count: u32,
    pub render_target: bool,
    /// Kernels may write it (a storage binding).
    pub random_write: bool,
    pub auto_generate_mips: bool,
    pub alpha_only: bool,
    /// Bumped on every change: bind groups that use the texture compare it.
    pub generation: u64,
    /// The main-texture bind group per color space.
    pub bind_groups: [Option<wgpu::BindGroup>; 2],
}

impl GpuTexture {
    pub fn view_for(&self, color_space: i32) -> &wgpu::TextureView {
        if let Some(copy) = &self.depth_copy {
            return copy;
        }
        match (&self.srgb_view, color_space) {
            (Some(srgb), 1) => srgb,
            _ => &self.view,
        }
    }

    /// The format a camera renders into this texture with.
    pub fn target_format(&self, color_space: i32) -> wgpu::TextureFormat {
        match (self.srgb_view.is_some(), color_space) {
            (true, 1) => wgpu::TextureFormat::Rgba8UnormSrgb,
            _ => self.texture.format(),
        }
    }
}

/// The RGB values of a packed 11/11/10 float texel (`Rg11b10Ufloat`).
pub fn rg11b10_to_f32(texel: u32) -> [f32; 3] {
    // Unsigned floats: 5 exponent bits (bias 15) over 6 (red, green) or 5 (blue) mantissa bits.
    fn unpack(bits: u32, mantissa_bits: u32) -> f32 {
        let exp = (bits >> mantissa_bits) as i32;
        let mantissa = (bits & ((1 << mantissa_bits) - 1)) as f32 / (1 << mantissa_bits) as f32;
        match exp {
            0 => mantissa * 2f32.powi(-14),
            31 if mantissa == 0.0 => f32::INFINITY,
            31 => f32::NAN,
            _ => (1.0 + mantissa) * 2f32.powi(exp - 15),
        }
    }
    [
        unpack(texel & 0x7ff, 6),
        unpack((texel >> 11) & 0x7ff, 6),
        unpack(texel >> 22, 5),
    ]
}

/// A half float's value.
pub fn f16_to_f32(h: u16) -> f32 {
    let sign = if h & 0x8000 != 0 { -1.0 } else { 1.0 };
    let exp = ((h >> 10) & 0x1f) as i32;
    let mantissa = (h & 0x3ff) as f32;
    match exp {
        0 => sign * mantissa * 2f32.powi(-24),
        0x1f if mantissa == 0.0 => sign * f32::INFINITY,
        0x1f => f32::NAN,
        _ => sign * (1.0 + mantissa / 1024.0) * 2f32.powi(exp - 15),
    }
}

fn f32_to_f16(value: f32) -> u16 {
    let bits = value.to_bits();
    let sign = ((bits >> 16) & 0x8000) as u16;
    let exp = ((bits >> 23) & 0xff) as i32;
    let mantissa = bits & 0x7f_ffff;
    if exp == 0xff {
        return sign | 0x7c00 | if mantissa != 0 { 0x200 } else { 0 };
    }
    let e = exp - 127 + 15;
    if e >= 0x1f {
        return sign | 0x7c00;
    }
    if e <= 0 {
        if e < -10 {
            return sign;
        }
        let m = (mantissa | 0x80_0000) >> (1 - e);
        return sign | ((m + 0x1000) >> 13) as u16;
    }
    sign | ((e as u32) << 10 | (mantissa + 0x1000) >> 13) as u16
}

/// Creates (or rewrites in place, when the shape is unchanged) a texture from a packet.
pub fn upload_texture(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
    existing: Option<GpuTexture>,
    t: &owned::Texture,
    generation: u64,
) -> GpuTexture {
    // A packet without pixels only changes the sampler state: a compressed texture (uploaded as
    // BC7 by the import, while the managed side knows it as RGBA8) keeps its format and blocks.
    let keep_compressed;
    let t = match &existing {
        Some(e) if t.data.is_empty() && matches!(e.format, 7 | 8) && t.format == e.format - 7 => {
            keep_compressed = owned::Texture {
                format: e.format,
                ..t.clone()
            };
            &keep_compressed
        }
        _ => t,
    };
    let width = t.width.max(1) as u32;
    let height = t.height.max(1) as u32;
    let max_mips = 32 - width.max(height).leading_zeros();
    let mip_count = (t.mip_count.max(1) as u32).min(max_mips);
    let render_target = t.render_target;
    let layers = t.layers.max(1) as u32;
    let samples = if render_target {
        t.samples.clamp(1, 8).next_power_of_two().min(8)
    } else {
        1
    };
    let cube = t.dimension == 4 && layers == 6;
    // A CubemapArray: six faces per cube, sampled as a cube array.
    let cube_array = t.dimension == 6 && layers >= 6 && layers % 6 == 0;
    // A Texture3D: its layers are depth slices (mips shrink them too).
    let volume = t.dimension == 3 && !render_target;
    // Float textures retain their component count and precision. Packed
    // 11/11/10 float targets (URP's HDR camera and bloom format) keep their own precision,
    // which shows in the rounding of every pass, where the device can draw into them (not
    // as storage textures).
    let packed = device
        .features()
        .contains(wgpu::Features::RG11B10UFLOAT_RENDERABLE);
    // Imported textures may come BC7-compressed (7: colour, sampled through an sRGB view in a
    // linear project like 0; 8: linear data like 1).
    let compressed = matches!(t.format, 7 | 8) && !render_target;
    let (format, bytes_per_pixel) = match t.format {
        2 => (wgpu::TextureFormat::Rgba16Float, 8),
        6 => (wgpu::TextureFormat::R32Float, 4),
        9 => (wgpu::TextureFormat::Rgba32Float, 16),
        10 => (wgpu::TextureFormat::Rg32Float, 8),
        11 => (wgpu::TextureFormat::R16Float, 2),
        12 => (wgpu::TextureFormat::Rg16Float, 4),
        4 if packed && !t.random_write => (wgpu::TextureFormat::Rg11b10Ufloat, 4),
        4 => (wgpu::TextureFormat::Rgba16Float, 8),
        _ if compressed => (wgpu::TextureFormat::Bc7RgbaUnorm, 0),
        _ => (wgpu::TextureFormat::Rgba8Unorm, 4),
    };
    let color = t.format == 0 || t.format == 7;
    let srgb_format = if compressed {
        wgpu::TextureFormat::Bc7RgbaUnormSrgb
    } else {
        wgpu::TextureFormat::Rgba8UnormSrgb
    };
    let sampler_key = SamplerKey {
        filter: t.filter_mode,
        wrap_u: t.wrap_u,
        wrap_v: t.wrap_v,
        aniso: t.aniso_level,
        compare: 0,
    };
    let reuse = existing.filter(|e| {
        e.width == width
            && e.height == height
            && e.mip_count == mip_count
            && e.format == t.format
            && e.layers == layers
            && e.volume == volume
            && e.samples == samples
            && e.render_target == render_target
            && e.random_write == t.random_write
            && (e.depth.is_some() == (t.depth_bits > 0))
    });
    let mut gpu = match reuse {
        Some(mut e) => {
            e.sampler_key = sampler_key;
            e.bind_groups = [None, None];
            e
        }
        None => {
            // Copies may read any texture (Graphics.CopyTexture).
            let mut usage = wgpu::TextureUsages::TEXTURE_BINDING
                | wgpu::TextureUsages::COPY_DST
                | wgpu::TextureUsages::COPY_SRC;
            if render_target {
                usage |= wgpu::TextureUsages::RENDER_ATTACHMENT;
            }
            if t.random_write {
                usage |= wgpu::TextureUsages::STORAGE_BINDING;
            }
            let srgb_formats = [srgb_format];
            let view_formats: &[wgpu::TextureFormat] = if color { &srgb_formats } else { &[] };
            let texture = device.create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound texture"),
                size: wgpu::Extent3d {
                    width,
                    height,
                    depth_or_array_layers: layers,
                },
                mip_level_count: mip_count,
                sample_count: 1,
                dimension: if volume {
                    wgpu::TextureDimension::D3
                } else {
                    wgpu::TextureDimension::D2
                },
                format,
                usage,
                view_formats,
            });
            let view_dimension = if cube {
                Some(wgpu::TextureViewDimension::Cube)
            } else if cube_array {
                Some(wgpu::TextureViewDimension::CubeArray)
            } else {
                None
            };
            let view = texture.create_view(&wgpu::TextureViewDescriptor {
                dimension: view_dimension,
                ..Default::default()
            });
            let srgb_view = color.then(|| {
                texture.create_view(&wgpu::TextureViewDescriptor {
                    format: Some(srgb_format),
                    dimension: view_dimension,
                    ..Default::default()
                })
            });
            let depth_texture = (render_target && t.depth_bits > 0).then(|| {
                device.create_texture(&wgpu::TextureDescriptor {
                    label: Some("unbound render texture depth"),
                    size: wgpu::Extent3d {
                        width,
                        height,
                        depth_or_array_layers: if volume { 1 } else { layers },
                    },
                    mip_level_count: if samples == 1 { mip_count } else { 1 },
                    sample_count: samples,
                    dimension: wgpu::TextureDimension::D2,
                    format: wgpu::TextureFormat::Depth24PlusStencil8,
                    usage: if samples > 1 {
                        wgpu::TextureUsages::RENDER_ATTACHMENT
                    } else {
                        wgpu::TextureUsages::RENDER_ATTACHMENT
                            | wgpu::TextureUsages::TEXTURE_BINDING
                    },
                    view_formats: &[],
                })
            });
            // A layered target (cube, array) is drawn into a layer at a time: this is the first's.
            let depth = depth_texture.as_ref().map(|t| {
                t.create_view(&wgpu::TextureViewDescriptor {
                    dimension: Some(wgpu::TextureViewDimension::D2),
                    mip_level_count: Some(1),
                    array_layer_count: Some(1),
                    ..Default::default()
                })
            });
            let msaa = (samples > 1).then(|| {
                let texture = device.create_texture(&wgpu::TextureDescriptor {
                    label: Some("unbound render texture msaa"),
                    size: wgpu::Extent3d {
                        width,
                        height,
                        depth_or_array_layers: 1,
                    },
                    mip_level_count: 1,
                    sample_count: samples,
                    dimension: wgpu::TextureDimension::D2,
                    format,
                    usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
                    view_formats,
                });
                let raw = texture.create_view(&wgpu::TextureViewDescriptor::default());
                let srgb = color.then(|| {
                    texture.create_view(&wgpu::TextureViewDescriptor {
                        format: Some(wgpu::TextureFormat::Rgba8UnormSrgb),
                        ..Default::default()
                    })
                });
                (raw, srgb)
            });
            // A multisampled depth buffer cannot be sampled as a texture.
            let depth_sample = depth_texture.as_ref().filter(|_| samples == 1).map(|t| {
                t.create_view(&wgpu::TextureViewDescriptor {
                    aspect: wgpu::TextureAspect::DepthOnly,
                    dimension: view_dimension,
                    ..Default::default()
                })
            });
            let depth_copy = (t.format == 5 && layers == 1 && depth_sample.is_some()).then(|| {
                let format = depth_copy_format(device);
                device
                    .create_texture(&wgpu::TextureDescriptor {
                        label: Some("unbound render texture depth copy"),
                        size: wgpu::Extent3d {
                            width,
                            height,
                            depth_or_array_layers: 1,
                        },
                        mip_level_count: 1,
                        sample_count: 1,
                        dimension: wgpu::TextureDimension::D2,
                        format,
                        usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                            | wgpu::TextureUsages::TEXTURE_BINDING,
                        view_formats: &[],
                    })
                    .create_view(&wgpu::TextureViewDescriptor::default())
            });
            GpuTexture {
                texture,
                view,
                srgb_view,
                sampler_key,
                depth,
                depth_sample,
                depth_copy,
                depth_texture,
                msaa,
                samples,
                layers,
                cube,
                cube_array,
                volume,
                width,
                height,
                format: t.format,
                mip_count,
                render_target,
                random_write: t.random_write,
                auto_generate_mips: t.auto_generate_mips,
                alpha_only: t.format == 3,
                generation,
                bind_groups: [None, None],
            }
        }
    };
    gpu.generation = generation;
    gpu.auto_generate_mips = t.auto_generate_mips;

    if !t.data.is_empty() && compressed {
        // BC7: 16 bytes per 4x4 block; levels below 4 pixels are a whole (padded) block.
        let mut offset = 0usize;
        for level in 0..mip_count {
            let bw = (width >> level).max(1).div_ceil(4);
            let bh = (height >> level).max(1).div_ceil(4);
            let len = (bw * bh * 16) as usize;
            if offset + len > t.data.len() {
                tracing::warn!(
                    "compressed texture data is shorter than its {mip_count} mip levels"
                );
                break;
            }
            queue.write_texture(
                wgpu::TexelCopyTextureInfo {
                    texture: &gpu.texture,
                    mip_level: level,
                    origin: wgpu::Origin3d::ZERO,
                    aspect: wgpu::TextureAspect::All,
                },
                &t.data[offset..offset + len],
                wgpu::TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(bw * 16),
                    rows_per_image: Some(bh),
                },
                wgpu::Extent3d {
                    width: bw * 4,
                    height: bh * 4,
                    depth_or_array_layers: 1,
                },
            );
            offset += len;
        }
    } else if !t.data.is_empty() {
        let source_bpp = match t.format {
            2 | 6 | 9..=12 => 16,
            3 => 1,
            _ => 4,
        };
        let mut offset = 0usize;
        // A 3D texture's data is level by level, each level all its slices.
        let images = if volume { 1 } else { layers };
        'layers: for layer in 0..images {
            for level in 0..mip_count {
                let w = (width >> level).max(1);
                let h = (height >> level).max(1);
                let d = if volume { (layers >> level).max(1) } else { 1 };
                let len = (w * h * d) as usize * source_bpp;
                if offset + len > t.data.len() {
                    tracing::warn!(
                        "texture data is shorter than its {mip_count} mip levels ({} bytes)",
                        t.data.len()
                    );
                    break 'layers;
                }
                let src = &t.data[offset..offset + len];
                offset += len;
                let converted;
                let bytes: &[u8] = match t.format {
                    // Alpha8 samples as (0, 0, 0, a), like Unity's.
                    3 => {
                        converted = src.iter().flat_map(|&a| [0, 0, 0, a]).collect::<Vec<u8>>();
                        &converted
                    }
                    2 | 6 | 9..=12 => {
                        let channels = match t.format {
                            6 | 11 => 1,
                            10 | 12 => 2,
                            _ => 4,
                        };
                        let half = matches!(t.format, 2 | 11 | 12);
                        let mut data = Vec::with_capacity(
                            src.len() / 16 * channels * if half { 2 } else { 4 },
                        );
                        for pixel in src.chunks_exact(16) {
                            for c in pixel[..channels * 4].chunks_exact(4) {
                                if half {
                                    data.extend_from_slice(
                                        &f32_to_f16(f32::from_le_bytes(c.try_into().unwrap()))
                                            .to_le_bytes(),
                                    );
                                } else {
                                    data.extend_from_slice(c);
                                }
                            }
                        }
                        converted = data;
                        &converted
                    }
                    _ => src,
                };
                queue.write_texture(
                    wgpu::TexelCopyTextureInfo {
                        texture: &gpu.texture,
                        mip_level: level,
                        origin: wgpu::Origin3d {
                            x: 0,
                            y: 0,
                            z: if volume { 0 } else { layer },
                        },
                        aspect: wgpu::TextureAspect::All,
                    },
                    bytes,
                    wgpu::TexelCopyBufferLayout {
                        offset: 0,
                        bytes_per_row: Some(w * bytes_per_pixel),
                        rows_per_image: Some(h),
                    },
                    wgpu::Extent3d {
                        width: w,
                        height: h,
                        depth_or_array_layers: d,
                    },
                );
            }
        }
    }
    gpu
}

/// Unity's default textures by name (`white`, `black`, ...), as 1x1 color textures.
pub fn default_textures(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
) -> HashMap<&'static str, GpuTexture> {
    let pixel = |rgba: [u8; 4]| owned::Texture {
        width: 1,
        height: 1,
        format: 0,
        mip_count: 1,
        data: rgba.to_vec(),
        filter_mode: 1,
        wrap_u: 0,
        wrap_v: 0,
        aniso_level: 1,
        render_target: false,
        random_write: false,
        auto_generate_mips: false,
        samples: 1,
        depth_bits: 0,
        layers: 1,
        dimension: 2,
    };
    let mut map = HashMap::new();
    for (name, rgba) in [
        ("white", [255, 255, 255, 255]),
        ("black", [0, 0, 0, 0]),
        ("unbound-unassigned", [128, 128, 128, 128]),
        // Measured: tests/projects/resources defaults.texels (gamma color space).
        ("gray", [127, 127, 127, 127]),
        ("grey", [127, 127, 127, 127]),
        ("linearGray", [127, 127, 127, 127]),
        ("bump", [127, 127, 255, 255]),
        ("red", [255, 0, 0, 0]),
    ] {
        map.insert(name, upload_texture(device, queue, None, &pixel(rgba), 0));
    }
    map
}

#[cfg(test)]
mod tests {
    use super::*;
    use unbound_runtime::render_protocol::{SubMeshPacket, Vector3, Vector4};

    #[test]
    fn half_floats() {
        assert_eq!(f32_to_f16(0.0), 0);
        assert_eq!(f32_to_f16(1.0), 0x3c00);
        assert_eq!(f32_to_f16(-2.0), 0xc000);
        assert_eq!(f32_to_f16(0.5), 0x3800);
        assert_eq!(f32_to_f16(65504.0), 0x7bff);
        assert_eq!(f32_to_f16(1e9), 0x7c00);
    }

    #[test]
    fn missing_uv_channels_read_the_nearest_lower_one() {
        // The 6000.0 Editor's reads (UvProbe, OpenGLCore): with uv and uv3, TEXCOORD0..3 are
        // uv, uv, uv3, uv3; with uv3 only, 0, 0, uv3, uv3.
        let at = |c: f32| {
            vec![Vector4 {
                x: c,
                y: c,
                z: 0.0,
                w: 0.0,
            }]
        };
        let mut mesh = owned::Mesh {
            positions: vec![Vector3::default()],
            indices: vec![0],
            ..Default::default()
        };
        mesh.uv[0] = at(0.1);
        mesh.uv[2] = at(0.5);
        let x = |m: &owned::Mesh| build_mesh(m).0[0].uv.map(|v| v[0]);
        assert_eq!(x(&mesh), [0.1, 0.1, 0.5, 0.5]);
        mesh.uv[0].clear();
        assert_eq!(x(&mesh), [0.0, 0.0, 0.5, 0.5]);
    }

    #[test]
    fn quads_become_triangles() {
        let mesh = owned::Mesh {
            positions: vec![Vector3::default(); 4],
            indices: vec![0, 1, 2, 3],
            submeshes: vec![SubMeshPacket {
                index_start: 0,
                index_count: 4,
                topology: 4,
                base_vertex: 0,
            }],
            ..Default::default()
        };
        let (vertices, indices, submeshes) = build_mesh(&mesh);
        assert_eq!(vertices.len(), 4);
        assert_eq!(vertices[0].color, [255; 4]);
        assert_eq!(indices, vec![0, 1, 2, 0, 2, 3]);
        assert_eq!(submeshes[0].count, 6);
        assert_eq!(size_of::<Vertex>(), 108);
    }
}
