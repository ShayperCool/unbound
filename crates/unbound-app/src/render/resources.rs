//! GPU copies of the protocol's meshes and textures.
use bytemuck::{Pod, Zeroable};
use std::collections::{BTreeSet, HashMap};
use std::sync::{Mutex, RwLock};
use unbound_runtime::render_protocol::{owned, texture_format};

/// The one vertex layout every mesh is uploaded with (`VertexIn` in unity.wgsl).
#[repr(C)]
#[derive(Debug, Clone, Copy, Pod, Zeroable)]
pub struct Vertex {
    pub position: [f32; 3],
    pub normal: [f32; 3],
    pub tangent: [f32; 4],
    pub color: [f32; 4],
    pub uv: [[f32; 4]; 4],
}

pub const VERTEX_ATTRIBUTES: [wgpu::VertexAttribute; 8] = wgpu::vertex_attr_array![
    0 => Float32x3,
    1 => Float32x3,
    2 => Float32x4,
    3 => Float32x4,
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

/// `MeshTopology.Quads`: drawn as two triangles per quad.
const MESH_TOPOLOGY_QUADS: i32 = 2;

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
            .map_or([1.0; 4], |c| [c.x, c.y, c.z, c.w]);
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
        // Unity's MeshTopology: 0 triangles, 2 quads, 3 lines, 4 line strip, 5 points.
        let topology = match sm.topology {
            3 => Topology::Lines,
            4 => Topology::LineStrip,
            5 => Topology::Points,
            _ => Topology::Triangles,
        };
        if sm.topology == MESH_TOPOLOGY_QUADS {
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
    /// Along W (a Texture3D's depth); samplers that are not a texture's own repeat there.
    pub wrap_w: i32,
    pub aniso: i32,
    /// A comparison sampler's function (Unity's CompareFunction; 0: none).
    pub compare: i32,
    /// Native sampler LOD bias, as bits so cache identity includes its exact value.
    pub lod_bias_bits: u32,
}

impl SamplerKey {
    /// The bilinear, clamped state of samplers that are no texture's (Unity's grab textures and
    /// render targets' defaults).
    pub const LINEAR_CLAMP: SamplerKey = SamplerKey {
        filter: 1,
        wrap_u: 1,
        wrap_v: 1,
        wrap_w: 0,
        aniso: 1,
        compare: 0,
        lod_bias_bits: 0,
    };

    /// The state an inline sampler's name spells.
    pub fn inline(state: unbound_shaders::InlineSampler) -> SamplerKey {
        SamplerKey {
            filter: state.filter,
            wrap_u: state.wrap,
            wrap_v: state.wrap,
            ..SamplerKey::LINEAR_CLAMP
        }
    }
}

/// A sampler of the key's state. A comparison compares in the device's depth direction: Unity
/// mirrors the (shadow) compare function with a reversed depth buffer.
pub fn create_sampler(device: &wgpu::Device, key: SamplerKey, reversed_z: bool) -> wgpu::Sampler {
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
    let aniso = if key.filter >= 1 {
        key.aniso.clamp(1, 16) as u16
    } else {
        1
    };
    let native_nearest = aniso > 1
        && mip == wgpu::MipmapFilterMode::Nearest
        && device.native_sampler_lod_bias_limit().is_some();
    // Vulkan can preserve Native's bilinear nearest-mip choice with anisotropy. Keep the
    // previous zero-bias policy on backends where that native extension is unsupported.
    let mip = if aniso > 1 && !native_nearest {
        wgpu::MipmapFilterMode::Linear
    } else {
        mip
    };
    let descriptor = wgpu::SamplerDescriptor {
        label: Some("unbound texture sampler"),
        address_mode_u: wrap(key.wrap_u),
        address_mode_v: wrap(key.wrap_v),
        address_mode_w: wrap(key.wrap_w),
        mag_filter: mag,
        min_filter: mag,
        mipmap_filter: mip,
        anisotropy_clamp: aniso,
        compare: compare_function(if reversed_z {
            i32::from(super::device::mirror_compare(key.compare as u8))
        } else {
            key.compare
        }),
        ..Default::default()
    };
    let bias = f32::from_bits(key.lod_bias_bits);
    if bias == 0.0 && !native_nearest {
        device.create_sampler(&descriptor)
    } else {
        device
            .create_sampler_with_lod_bias(&descriptor, bias)
            .expect("nonzero texture mipMapBias requires native Vulkan sampler LOD bias support")
    }
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

/// A float color texture of a depth buffer's size, which the depth copy writes.
pub fn depth_copy_view(device: &wgpu::Device, width: u32, height: u32) -> wgpu::TextureView {
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
            format: depth_copy_format(device),
            usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::TEXTURE_BINDING,
            view_formats: &[],
        })
        .create_view(&wgpu::TextureViewDescriptor::default())
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
    /// A render target's depth as a float color texture, copied after the passes that draw into
    /// it: what sampling a depth-only target reads (Unity's depth textures read as their depth),
    /// and what its depth element reads (`render_protocol::handle::DEPTH_ELEMENT`; made the first
    /// time a draw samples it for a target with color).
    pub depth_copy: Option<wgpu::TextureView>,
    /// A multisampled target's depth buffer, as the depth copy reads it.
    pub depth_msaa: Option<wgpu::TextureView>,
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
    /// Writable views, prepared once for each mip.
    pub storage_views: Vec<wgpu::TextureView>,
    /// One attachment view per volume mip, made with the resource and reused across draws.
    pub volume_targets: Vec<(wgpu::TextureView, Option<wgpu::TextureView>)>,
    pub width: u32,
    pub height: u32,
    pub format: i32,
    pub mip_count: u32,
    pub render_target: bool,
    /// Kernels may write it (a storage binding).
    pub random_write: bool,
    pub auto_generate_mips: bool,
    pub defer_mips: bool,
    pub alpha_only: bool,
    /// Bumped on every change: bind groups that use the texture compare it.
    pub generation: u64,
    /// The main-texture bind group per color space.
    pub bind_groups: [Option<wgpu::BindGroup>; 2],
}

impl GpuTexture {
    pub fn view_for(&self, color_space: i32) -> &wgpu::TextureView {
        if let Some(copy) = self.depth_copy.as_ref().filter(|_| self.format == 5) {
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

/// The usages of a view of `texture` in `format`: an sRGB view of a random-write texture (stored
/// linear, written by kernels) cannot be a storage binding, which wgpu refuses for sRGB.
pub fn view_usage(
    texture: &wgpu::Texture,
    format: Option<wgpu::TextureFormat>,
) -> Option<wgpu::TextureUsages> {
    format
        .filter(|f| f.is_srgb())
        .map(|_| texture.usage() - wgpu::TextureUsages::STORAGE_BINDING)
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

/// A float as a half float, rounding half up. A mantissa that rounds up past its last step
/// carries into the exponent (0.49999997 is 0.5, the largest finite values round to infinity).
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
    sign | (((e as u32) << 10) + ((mantissa + 0x1000) >> 13)) as u16
}

/// What the device does with the formats only some devices have: those of the normalized 16-bit
/// and packed 10-10-10-2 codes ([`texture_format::RG16_UNORM`] ..= [`texture_format::R16_UNORM`]). Each is the format's features
/// as wgpu checks them (the adapter's when the device has
/// `TEXTURE_ADAPTER_SPECIFIC_FORMAT_FEATURES`, else WebGPU's guaranteed ones), or none where the
/// device lacks the feature the format needs (`TEXTURE_FORMAT_16BIT_NORM`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct NormFormatCaps {
    pub r16: Option<wgpu::TextureFormatFeatures>,
    pub rg16: Option<wgpu::TextureFormatFeatures>,
    pub rgba16: Option<wgpu::TextureFormatFeatures>,
    pub rgb10a2: Option<wgpu::TextureFormatFeatures>,
    pub r8snorm: Option<wgpu::TextureFormatFeatures>,
    pub rg8snorm: Option<wgpu::TextureFormatFeatures>,
    pub rgba8snorm: Option<wgpu::TextureFormatFeatures>,
    pub r8: Option<wgpu::TextureFormatFeatures>,
    pub rg8: Option<wgpu::TextureFormatFeatures>,
    /// The [`INTEGER_FORMATS`] kernels may read and write (bit `i` for the `i`th).
    pub integer_storage: u32,
    /// The device samples, filters, copies, draws into and blends packed 11/11/10 floats
    /// (`Rg11b10Ufloat`), by the format's own features: with WebGPU's
    /// `RG11B10UFLOAT_RENDERABLE` feature, or on a backend that draws into them without it
    /// (wgpu's OpenGL backend).
    pub packed_float_target: bool,
}

/// The formats of the integer codes, in code order ([`texture_format::integer`]).
const INTEGER_FORMATS: [wgpu::TextureFormat; 18] = {
    use wgpu::TextureFormat as F;
    [
        F::R8Uint,
        F::R8Sint,
        F::R16Uint,
        F::R16Sint,
        F::R32Uint,
        F::R32Sint,
        F::Rg8Uint,
        F::Rg8Sint,
        F::Rg16Uint,
        F::Rg16Sint,
        F::Rg32Uint,
        F::Rg32Sint,
        F::Rgba8Uint,
        F::Rgba8Sint,
        F::Rgba16Uint,
        F::Rgba16Sint,
        F::Rgba32Uint,
        F::Rgba32Sint,
    ]
};

/// The format of an integer code; none for other codes.
pub fn integer_format(code: i32) -> Option<wgpu::TextureFormat> {
    let i = usize::try_from(code.checked_sub(texture_format::INTEGER_FIRST)?).ok()?;
    INTEGER_FORMATS.get(i).copied()
}

/// The integer code of a format; none for other formats.
pub fn integer_code(format: wgpu::TextureFormat) -> Option<i32> {
    let i = INTEGER_FORMATS.iter().position(|f| *f == format)?;
    Some(texture_format::INTEGER_FIRST + i as i32)
}

/// Whether a format's texels are integers (signed or unsigned).
pub fn is_integer(format: wgpu::TextureFormat) -> bool {
    matches!(
        format.sample_type(None, None),
        Some(wgpu::TextureSampleType::Sint | wgpu::TextureSampleType::Uint)
    )
}

/// A clear color for a one- or two-channel 8-bit normalized target: each channel at its nearest
/// step, halves down (0.5 stores 127 and 0.25 stores 64, as the Editor's R8G8_UNorm does,
/// tests/projects/renderapi `volumeReadback.*.RG16`, and as Unbound's RGBA8 targets do; this
/// device's R8 and RG8 clears round halves up). Other formats keep the color.
pub fn unorm8_clear(format: wgpu::TextureFormat, color: [f32; 4]) -> [f32; 4] {
    if !matches!(
        format,
        wgpu::TextureFormat::R8Unorm | wgpu::TextureFormat::Rg8Unorm
    ) {
        return color;
    }
    color.map(|v| (v.clamp(0.0, 1.0) * 255.0 - 0.5).ceil().max(0.0) / 255.0)
}

/// [`unorm8_clear`] of a pass's clear color (each attachment's own format).
pub fn unorm8_clear_color(format: wgpu::TextureFormat, color: wgpu::Color) -> wgpu::Color {
    if !matches!(
        format,
        wgpu::TextureFormat::R8Unorm | wgpu::TextureFormat::Rg8Unorm
    ) {
        return color;
    }
    let [r, g, b, a] = unorm8_clear(
        format,
        [color.r, color.g, color.b, color.a].map(|v| v as f32),
    )
    .map(f64::from);
    wgpu::Color { r, g, b, a }
}

/// A clear color as an integer target stores it: each channel's float bits as a signed or
/// unsigned integer, clamped to the channel's range (measured on the Editor's OpenGLCore device:
/// 3.0 clears R32_SInt to 0x40400000 and R8_SInt to 127, tests/projects/textureformats
/// `rt.*.clear.load`). Other formats keep the color.
pub fn integer_clear(format: wgpu::TextureFormat, color: wgpu::Color) -> wgpu::Color {
    let signed = match format.sample_type(None, None) {
        Some(wgpu::TextureSampleType::Sint) => true,
        Some(wgpu::TextureSampleType::Uint) => false,
        _ => return color,
    };
    let channels = format.components() as u32;
    let bits = format.block_copy_size(None).unwrap_or(4) * 8 / channels.max(1);
    let store = |v: f64| {
        let raw = (v as f32).to_bits();
        if signed {
            let max = (1i64 << (bits - 1)) - 1;
            (raw as i32 as i64).clamp(-max - 1, max) as f64
        } else {
            (raw as u64).min((1u64 << bits) - 1) as f64
        }
    };
    wgpu::Color {
        r: store(color.r),
        g: store(color.g),
        b: store(color.b),
        a: store(color.a),
    }
}

impl NormFormatCaps {
    /// Nothing known: every such texture falls back.
    pub const NONE: Self = Self {
        r16: None,
        rg16: None,
        rgba16: None,
        rgb10a2: None,
        r8snorm: None,
        rg8snorm: None,
        rgba8snorm: None,
        r8: None,
        rg8: None,
        integer_storage: 0,
        packed_float_target: false,
    };

    /// The device's, from its adapter. The device asks for the adapter's features when it is
    /// made, so it has the 16-bit normalized formats exactly where the adapter does.
    pub fn query(adapter: &wgpu::Adapter, device_features: wgpu::Features) -> Self {
        let features = |format: wgpu::TextureFormat| {
            if device_features.contains(wgpu::Features::TEXTURE_ADAPTER_SPECIFIC_FORMAT_FEATURES) {
                adapter.get_texture_format_features(format)
            } else {
                format.guaranteed_format_features(device_features)
            }
        };
        let norm16 = device_features.contains(wgpu::Features::TEXTURE_FORMAT_16BIT_NORM);
        Self {
            r16: norm16.then(|| features(wgpu::TextureFormat::R16Unorm)),
            rg16: norm16.then(|| features(wgpu::TextureFormat::Rg16Unorm)),
            rgba16: norm16.then(|| features(wgpu::TextureFormat::Rgba16Unorm)),
            rgb10a2: Some(features(wgpu::TextureFormat::Rgb10a2Unorm)),
            r8snorm: Some(features(wgpu::TextureFormat::R8Snorm)),
            rg8snorm: Some(features(wgpu::TextureFormat::Rg8Snorm)),
            rgba8snorm: Some(features(wgpu::TextureFormat::Rgba8Snorm)),
            r8: Some(features(wgpu::TextureFormat::R8Unorm)),
            rg8: Some(features(wgpu::TextureFormat::Rg8Unorm)),
            integer_storage: INTEGER_FORMATS
                .iter()
                .enumerate()
                .filter(|(_, f)| {
                    let f = features(**f);
                    f.allowed_usages
                        .contains(wgpu::TextureUsages::STORAGE_BINDING)
                        && f.flags.contains(
                            wgpu::TextureFormatFeatureFlags::STORAGE_WRITE_ONLY
                                | wgpu::TextureFormatFeatureFlags::STORAGE_READ_WRITE,
                        )
                })
                .fold(0, |bits, (i, _)| bits | 1 << i),
            packed_float_target: {
                use wgpu::{TextureFormatFeatureFlags as Flags, TextureUsages as U};
                let f = features(wgpu::TextureFormat::Rg11b10Ufloat);
                f.allowed_usages
                    .contains(U::TEXTURE_BINDING | U::COPY_SRC | U::COPY_DST | U::RENDER_ATTACHMENT)
                    && f.flags.contains(Flags::FILTERABLE | Flags::BLENDABLE)
            },
        }
    }
}

/// The renderer's device's [`NormFormatCaps`] (none until the renderer reads them).
static NORM_FORMAT_CAPS: RwLock<NormFormatCaps> = RwLock::new(NormFormatCaps::NONE);

/// Records the device's [`NormFormatCaps`], read when the renderer starts.
pub fn set_norm_format_caps(caps: NormFormatCaps) {
    *NORM_FORMAT_CAPS.write().unwrap_or_else(|e| e.into_inner()) = caps;
}

fn norm_format_caps() -> NormFormatCaps {
    *NORM_FORMAT_CAPS.read().unwrap_or_else(|e| e.into_inner())
}

/// The format a normalized 16-bit, 10-10-10-2 or signed normalized 8-bit code is stored in, or
/// why this device cannot keep it. Every such texture must be one the device samples, filters, copies, draws into and
/// blends, whatever its own use, so render textures and textures of one format stay
/// copy-compatible (Graphics.CopyTexture). A multisampled target also needs the sample count
/// and resolving, and one kernels write (enableRandomWrite) storage access.
pub fn norm_format(
    code: i32,
    caps: &NormFormatCaps,
    render_target: bool,
    random_write: bool,
    samples: u32,
) -> Result<wgpu::TextureFormat, String> {
    use wgpu::{TextureFormat as F, TextureFormatFeatureFlags as Flags, TextureUsages as U};
    let (format, features) = match code {
        texture_format::RG16_UNORM => (F::Rg16Unorm, caps.rg16),
        texture_format::RGBA16_UNORM => (F::Rgba16Unorm, caps.rgba16),
        texture_format::RGB10A2_UNORM => (F::Rgb10a2Unorm, caps.rgb10a2),
        texture_format::R16_UNORM => (F::R16Unorm, caps.r16),
        texture_format::R8_SNORM => (F::R8Snorm, caps.r8snorm),
        texture_format::RG8_SNORM => (F::Rg8Snorm, caps.rg8snorm),
        texture_format::RGBA8_SNORM => (F::Rgba8Snorm, caps.rgba8snorm),
        texture_format::R8_UNORM => (F::R8Unorm, caps.r8),
        texture_format::RG8_UNORM => (F::Rg8Unorm, caps.rg8),
        _ => {
            return Err(format!(
                "format code {code} is not a normalized 16-bit, 10-10-10-2, signed normalized 8-bit or one- or two-channel 8-bit code"
            ));
        }
    };
    let Some(features) = features else {
        return Err(match format {
            F::R8Unorm | F::Rg8Unorm => {
                format!("{format:?}: the device's formats are not known yet")
            }
            _ => format!(
                "{format:?} needs the TEXTURE_FORMAT_16BIT_NORM device feature, which this device does not have"
            ),
        });
    };
    let usages = U::TEXTURE_BINDING | U::COPY_SRC | U::COPY_DST | U::RENDER_ATTACHMENT;
    if !features.allowed_usages.contains(usages)
        || !features
            .flags
            .contains(Flags::FILTERABLE | Flags::BLENDABLE)
    {
        return Err(format!(
            "this device cannot sample, filter, copy, draw into and blend {format:?}"
        ));
    }
    if render_target
        && samples > 1
        && !(features.flags.sample_count_supported(samples)
            && features.flags.contains(Flags::MULTISAMPLE_RESOLVE))
    {
        return Err(format!(
            "this device cannot draw into {format:?} with {samples} samples"
        ));
    }
    // Kernels' 16-bit normalized storage also needs TEXTURE_FORMAT_16BIT_NORM (wgpu validates
    // their shaders with it), which a format without features above lacks.
    if random_write {
        if !features.allowed_usages.contains(U::STORAGE_BINDING)
            || !features
                .flags
                .contains(Flags::STORAGE_WRITE_ONLY | Flags::STORAGE_READ_WRITE)
        {
            return Err(format!(
                "this device cannot let kernels read and write {format:?}"
            ));
        }
    }
    Ok(format)
}

/// Why a texture is not stored in its code's own format.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum FormatFallback {
    /// A code outside the protocol's table (an error: no producer sends one).
    Unknown(String),
    /// A format this device cannot keep for the texture's use.
    Unsupported(String),
}

/// The format a packet's texture is stored in and its bytes per texel on the GPU.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TextureStorage {
    pub format: wgpu::TextureFormat,
    pub bytes_per_pixel: u32,
    /// Set when the texture is stored as linear RGBA8 instead of its code's format.
    pub fallback: Option<FormatFallback>,
}

/// The storage of a packet's format code. `samples` is a render target's MSAA sample count.
pub fn texture_storage(t: &owned::Texture, samples: u32, caps: &NormFormatCaps) -> TextureStorage {
    let compressed = block_format(t.format).filter(|_| !t.render_target);
    let stored = |format: wgpu::TextureFormat, bytes_per_pixel: u32| TextureStorage {
        format,
        bytes_per_pixel,
        fallback: None,
    };
    let rgba8 = |fallback: FormatFallback| TextureStorage {
        format: wgpu::TextureFormat::Rgba8Unorm,
        bytes_per_pixel: 4,
        fallback: Some(fallback),
    };
    if let Err(why) = texture_format::validate(t.format) {
        // As the default color target, so the handle still names a texture.
        return rgba8(FormatFallback::Unknown(why));
    }
    match t.format {
        2 => stored(wgpu::TextureFormat::Rgba16Float, 8),
        6 => stored(wgpu::TextureFormat::R32Float, 4),
        9 => stored(wgpu::TextureFormat::Rgba32Float, 16),
        10 => stored(wgpu::TextureFormat::Rg32Float, 8),
        11 => stored(wgpu::TextureFormat::R16Float, 2),
        12 => stored(wgpu::TextureFormat::Rg16Float, 4),
        4 if caps.packed_float_target && !t.random_write => {
            stored(wgpu::TextureFormat::Rg11b10Ufloat, 4)
        }
        4 => stored(wgpu::TextureFormat::Rgba16Float, 8),
        texture_format::RG16_UNORM..=texture_format::R16_UNORM
        | texture_format::R8_SNORM..=texture_format::RGBA8_SNORM
        | texture_format::R8_UNORM
        | texture_format::RG8_UNORM => {
            // Where the device cannot keep them, linear RGBA8: what they were before they had
            // codes of their own.
            match norm_format(t.format, caps, t.render_target, t.random_write, samples) {
                Ok(format) => stored(format, format.block_copy_size(None).unwrap_or(4)),
                Err(why) => rgba8(FormatFallback::Unsupported(why)),
            }
        }
        texture_format::INTEGER_FIRST..=texture_format::INTEGER_LAST => {
            let i = (t.format - texture_format::INTEGER_FIRST) as usize;
            let format = INTEGER_FORMATS[i];
            if !t.random_write || caps.integer_storage & 1 << i != 0 {
                stored(format, format.block_copy_size(None).unwrap_or(4))
            } else {
                // Four 32-bit channels of the same signedness, which kernels read and write on
                // every device Unbound runs on (Vulkan; Metal's read-write tier 2).
                let wide = INTEGER_FORMATS[16 + i % 2];
                TextureStorage {
                    format: wide,
                    bytes_per_pixel: 16,
                    fallback: Some(FormatFallback::Unsupported(format!(
                        "this device cannot let kernels read and write {format:?}: kept as {wide:?}"
                    ))),
                }
            }
        }
        _ if compressed.is_some() => stored(compressed.unwrap(), 0),
        _ => stored(wgpu::TextureFormat::Rgba8Unorm, 4),
    }
}

/// A block-compressed packet as RGBA8, decoded as a GPU samples it (BC3 colour always in
/// four-colour mode; colour codes sampled through an sRGB view in linear projects, the others
/// linear; BC6H clamped to 0..1).
fn decode_blocks(t: &owned::Texture) -> owned::Texture {
    use unbound_assets::texture::{BlockFormat, decode_level_as_gpu};
    let format = match t.format {
        texture_format::BC1_COLOR | texture_format::BC1_DATA => BlockFormat::Bc1,
        texture_format::BC3_COLOR | texture_format::BC3_DATA => BlockFormat::Bc3,
        texture_format::BC4 => BlockFormat::Bc4,
        texture_format::BC5 => BlockFormat::Bc5,
        texture_format::BC6H => BlockFormat::Bc6h,
        _ => BlockFormat::Bc7,
    };
    report_fallback(
        t.format,
        &FormatFallback::Unsupported(if format == BlockFormat::Bc6h {
            "the device has no BC formats: the blocks are decoded, HDR colour clamped to 0..1"
                .to_string()
        } else {
            "the device has no BC formats: the blocks are decoded".to_string()
        }),
    );
    let (width, height) = (t.width.max(1) as u32, t.height.max(1) as u32);
    let mut data = Vec::new();
    let mut offset = 0;
    for level in 0..t.mip_count.max(1) as u32 {
        let (w, h) = ((width >> level).max(1), (height >> level).max(1));
        let len = format.level_size(w, h);
        let Some(blocks) = t.data.get(offset..offset + len) else {
            break;
        };
        offset += len;
        data.extend(decode_level_as_gpu(format, blocks, w, h));
    }
    owned::Texture {
        format: if texture_format::block_color(t.format) {
            texture_format::SRGB_RGBA8
        } else {
            texture_format::LINEAR_RGBA8
        },
        data,
        ..t.clone()
    }
}

/// The GPU format of a block-compressed packet code (linear; a colour code samples through
/// [`wgpu::TextureFormat::add_srgb_suffix`]).
pub fn block_format(code: i32) -> Option<wgpu::TextureFormat> {
    use wgpu::TextureFormat as F;
    Some(match code {
        texture_format::BC7_COLOR | texture_format::BC7_DATA => F::Bc7RgbaUnorm,
        texture_format::BC1_COLOR | texture_format::BC1_DATA => F::Bc1RgbaUnorm,
        texture_format::BC3_COLOR | texture_format::BC3_DATA => F::Bc3RgbaUnorm,
        texture_format::BC4 => F::Bc4RUnorm,
        texture_format::BC5 => F::Bc5RgUnorm,
        texture_format::BC6H => F::Bc6hRgbUfloat,
        _ => return None,
    })
}

/// Logs a texture's format fallback, once per code and reason.
fn report_fallback(code: i32, fallback: &FormatFallback) {
    static REPORTED: Mutex<BTreeSet<(i32, String)>> = Mutex::new(BTreeSet::new());
    let (FormatFallback::Unknown(why) | FormatFallback::Unsupported(why)) = fallback;
    if !REPORTED
        .lock()
        .unwrap_or_else(|e| e.into_inner())
        .insert((code, why.clone()))
    {
        return;
    }
    match fallback {
        FormatFallback::Unknown(why) => tracing::error!(
            "texture upload rejected ({why}): the texture is made as linear RGBA8 instead"
        ),
        FormatFallback::Unsupported(why) if integer_format(code).is_some() => {
            tracing::warn!("Unbound: a render texture of format code {code}: {why}")
        }
        FormatFallback::Unsupported(why) => tracing::warn!(
            "Unbound: a texture of format code {code} is kept as linear RGBA8 (8 bits per channel, not its own precision): {why}"
        ),
    }
}

/// A float as an unsigned normalized integer of `max` steps: clamped to 0..=1, rounded to the
/// nearest step; NaN is 0.
fn unorm(v: f32, max: u32) -> u32 {
    if v.is_nan() {
        0
    } else {
        (v.clamp(0.0, 1.0) * max as f32).round() as u32
    }
}

/// Signed normalized: `v` clamped to [-1, 1] in steps of 1 / `max` (round to nearest).
fn snorm(v: f32, max: u32) -> i32 {
    if v.is_nan() {
        0
    } else {
        (v.clamp(-1.0, 1.0) * max as f32).round() as i32
    }
}

/// Upload data (four f32 per texel) of a normalized 16-bit, 10-10-10-2 or signed normalized
/// 8-bit code as `format` stores it: its own format, or RGBA8 where it falls back.
pub fn norm_texels(format: wgpu::TextureFormat, data: &[u8]) -> Vec<u8> {
    let texel = format.block_copy_size(None).unwrap_or(4) as usize;
    let mut out = Vec::with_capacity(data.len() / 16 * texel);
    for chunk in data.chunks_exact(16) {
        let c: [f32; 4] = std::array::from_fn(|i| {
            f32::from_le_bytes(chunk[i * 4..i * 4 + 4].try_into().unwrap())
        });
        match format {
            wgpu::TextureFormat::R16Unorm => {
                out.extend_from_slice(&(unorm(c[0], 0xffff) as u16).to_le_bytes());
            }
            wgpu::TextureFormat::Rg16Unorm => {
                for v in &c[..2] {
                    out.extend_from_slice(&(unorm(*v, 0xffff) as u16).to_le_bytes());
                }
            }
            wgpu::TextureFormat::Rgba16Unorm => {
                for v in c {
                    out.extend_from_slice(&(unorm(v, 0xffff) as u16).to_le_bytes());
                }
            }
            // Red in the low bits, alpha in the top two.
            wgpu::TextureFormat::Rgb10a2Unorm => {
                let packed = unorm(c[0], 0x3ff)
                    | unorm(c[1], 0x3ff) << 10
                    | unorm(c[2], 0x3ff) << 20
                    | unorm(c[3], 3) << 30;
                out.extend_from_slice(&packed.to_le_bytes());
            }
            wgpu::TextureFormat::R8Snorm
            | wgpu::TextureFormat::Rg8Snorm
            | wgpu::TextureFormat::Rgba8Snorm => {
                out.extend(c[..texel].iter().map(|v| snorm(*v, 0x7f) as i8 as u8));
            }
            _ => out.extend(c.map(|v| unorm(v, 0xff) as u8)),
        }
    }
    out
}

/// A texel of a normalized 16-bit, 10-10-10-2 or signed normalized 8-bit format as RGBA floats
/// (a one- or two-channel one reads (r, 0, 0, 1) or (r, g, 0, 1), as the float formats do); none
/// for other formats or a short texel.
pub fn norm_texel_to_f32(format: wgpu::TextureFormat, texel: &[u8]) -> Option<[f32; 4]> {
    let u16_at =
        |i: usize| f32::from(u16::from_le_bytes([texel[i * 2], texel[i * 2 + 1]])) / 65535.0;
    // -128 and -127 both read -1.
    let i8_at = |i: usize| (f32::from(texel[i] as i8) / 127.0).max(-1.0);
    Some(match format {
        wgpu::TextureFormat::R8Snorm if !texel.is_empty() => [i8_at(0), 0.0, 0.0, 1.0],
        wgpu::TextureFormat::Rg8Snorm if texel.len() >= 2 => [i8_at(0), i8_at(1), 0.0, 1.0],
        wgpu::TextureFormat::Rgba8Snorm if texel.len() >= 4 => {
            [i8_at(0), i8_at(1), i8_at(2), i8_at(3)]
        }
        wgpu::TextureFormat::R16Unorm if texel.len() >= 2 => [u16_at(0), 0.0, 0.0, 1.0],
        wgpu::TextureFormat::Rg16Unorm if texel.len() >= 4 => [u16_at(0), u16_at(1), 0.0, 1.0],
        wgpu::TextureFormat::Rgba16Unorm if texel.len() >= 8 => {
            [u16_at(0), u16_at(1), u16_at(2), u16_at(3)]
        }
        wgpu::TextureFormat::Rgb10a2Unorm if texel.len() >= 4 => {
            let p = u32::from_le_bytes(texel[..4].try_into().unwrap());
            let ten = |shift: u32| ((p >> shift) & 0x3ff) as f32 / 1023.0;
            [ten(0), ten(10), ten(20), (p >> 30) as f32 / 3.0]
        }
        _ => return None,
    })
}

/// Creates (or rewrites in place, when the shape is unchanged) a texture from a packet.
pub fn upload_texture(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
    existing: Option<GpuTexture>,
    t: &owned::Texture,
    generation: u64,
) -> GpuTexture {
    upload_texture_with(device, queue, existing, t, generation, &mut |desc| {
        device.create_texture(desc)
    })
}

/// [`upload_texture`] whose texture and depth texture come from `allocate` (one whose memory
/// native plugins share, `executor::plugins`). Multisampled colors and depth copies are the
/// device's own.
pub fn upload_texture_with(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
    existing: Option<GpuTexture>,
    t: &owned::Texture,
    generation: u64,
    allocate: &mut dyn FnMut(&wgpu::TextureDescriptor) -> wgpu::Texture,
) -> GpuTexture {
    // A packet without pixels only changes the sampler state: a block-compressed import (whose
    // managed side knows it by its pixels' format) keeps its format and blocks.
    let keep_compressed;
    let decoded;
    let t = match &existing {
        Some(e)
            if t.data.is_empty()
                && texture_format::block_compressed(e.format)
                && !texture_format::block_compressed(t.format) =>
        {
            keep_compressed = owned::Texture {
                format: e.format,
                ..t.clone()
            };
            &keep_compressed
        }
        // A device without BC formats takes what the blocks decode to, as Unity does; so does
        // a size the GPU cannot keep in blocks (not a multiple of 4: malformed data).
        _ if block_format(t.format).is_some()
            && !t.render_target
            && (t.width % 4 != 0
                || t.height % 4 != 0
                || !device
                    .features()
                    .contains(wgpu::Features::TEXTURE_COMPRESSION_BC)) =>
        {
            decoded = decode_blocks(t);
            &decoded
        }
        _ => t,
    };
    let width = t.width.max(1) as u32;
    let height = t.height.max(1) as u32;
    let render_target = t.render_target;
    let layers = t.layers.max(1) as u32;
    // A Texture3D: its layers are depth slices (mips shrink them too).
    let volume = t.dimension == 3;
    let mip_count = gpu_mip_count(width, height, layers, volume, t.mip_count);
    let samples = if render_target && !volume {
        t.samples.clamp(1, 8).next_power_of_two().min(8)
    } else {
        1
    };
    // Integer targets cannot be resolved: they are drawn with one sample.
    let samples = if samples > 1 && integer_format(t.format).is_some() {
        report_fallback(
            t.format,
            &FormatFallback::Unsupported(format!(
                "integer targets cannot be resolved, so its {samples} samples are drawn as one"
            )),
        );
        1
    } else {
        samples
    };
    let cube = t.dimension == 4 && layers == 6;
    // A CubemapArray: six faces per cube, sampled as a cube array.
    let cube_array = t.dimension == 6 && layers >= 6 && layers % 6 == 0;
    // Float textures retain their component count and precision. Packed
    // 11/11/10 float targets (URP's HDR camera and bloom format) keep their own precision,
    // which shows in the rounding of every pass, where the device can draw into them (not
    // as storage textures).
    // Imported textures may come block-compressed (see `texture_storage`).
    let compressed = block_format(t.format).filter(|_| !render_target);
    let storage = texture_storage(t, samples, &norm_format_caps());
    if let Some(fallback) = &storage.fallback {
        report_fallback(t.format, fallback);
    }
    let (format, bytes_per_pixel) = (storage.format, storage.bytes_per_pixel);
    let color = t.format == 0 || texture_format::block_color(t.format);
    let srgb_format = match compressed {
        Some(format) => format.add_srgb_suffix(),
        None => wgpu::TextureFormat::Rgba8UnormSrgb,
    };
    let sampler_key = SamplerKey {
        filter: t.filter_mode,
        wrap_u: t.wrap_u,
        wrap_v: t.wrap_v,
        wrap_w: t.wrap_w,
        aniso: t.aniso_level,
        compare: 0,
        lod_bias_bits: 0,
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
            let texture = allocate(&wgpu::TextureDescriptor {
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
                    usage: view_usage(&texture, Some(srgb_format)),
                    ..Default::default()
                })
            });
            let storage_views = if t.random_write {
                (0..mip_count)
                    .map(|mip| {
                        texture.create_view(&wgpu::TextureViewDescriptor {
                            format: Some(format.remove_srgb_suffix()),
                            usage: Some(wgpu::TextureUsages::STORAGE_BINDING),
                            base_mip_level: mip,
                            mip_level_count: Some(1),
                            ..Default::default()
                        })
                    })
                    .collect()
            } else {
                Vec::new()
            };
            let volume_targets = if volume && render_target {
                (0..mip_count)
                    .map(|mip| {
                        let descriptor = wgpu::TextureViewDescriptor {
                            dimension: Some(wgpu::TextureViewDimension::D3),
                            base_mip_level: mip,
                            mip_level_count: Some(1),
                            ..Default::default()
                        };
                        let raw = texture.create_view(&descriptor);
                        let srgb = color.then(|| {
                            texture.create_view(&wgpu::TextureViewDescriptor {
                                format: Some(srgb_format),
                                usage: view_usage(&texture, Some(srgb_format)),
                                ..descriptor
                            })
                        });
                        (raw, srgb)
                    })
                    .collect()
            } else {
                Vec::new()
            };
            let depth_texture = (render_target && t.depth_bits > 0).then(|| {
                allocate(&wgpu::TextureDescriptor {
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
                    // A multisampled one is read by the depth copy only.
                    usage: wgpu::TextureUsages::RENDER_ATTACHMENT
                        | wgpu::TextureUsages::TEXTURE_BINDING,
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
            let depth_msaa = depth_texture.as_ref().filter(|_| samples > 1).map(|t| {
                t.create_view(&wgpu::TextureViewDescriptor {
                    aspect: wgpu::TextureAspect::DepthOnly,
                    dimension: Some(wgpu::TextureViewDimension::D2),
                    mip_level_count: Some(1),
                    array_layer_count: Some(1),
                    ..Default::default()
                })
            });
            let depth_copy = (t.format == 5 && layers == 1 && depth_sample.is_some())
                .then(|| depth_copy_view(device, width, height));
            GpuTexture {
                texture,
                view,
                srgb_view,
                sampler_key,
                depth,
                depth_sample,
                depth_copy,
                depth_msaa,
                depth_texture,
                msaa,
                samples,
                layers,
                cube,
                cube_array,
                volume,
                volume_targets,
                storage_views,
                width,
                height,
                format: t.format,
                mip_count,
                render_target,
                random_write: t.random_write,
                auto_generate_mips: t.auto_generate_mips,
                defer_mips: t.defer_mips,
                alpha_only: t.format == 3,
                generation,
                bind_groups: [None, None],
            }
        }
    };
    gpu.generation = generation;
    gpu.auto_generate_mips = t.auto_generate_mips;
    gpu.defer_mips = t.defer_mips;

    if let (false, Some(block)) = (t.data.is_empty(), compressed) {
        // Whole 4x4 blocks; levels below 4 pixels are a whole (padded) block.
        let block_bytes = block.block_copy_size(None).unwrap_or(16);
        let mut offset = 0usize;
        for level in 0..mip_count {
            let bw = (width >> level).max(1).div_ceil(4);
            let bh = (height >> level).max(1).div_ceil(4);
            let len = (bw * bh * block_bytes) as usize;
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
                    bytes_per_row: Some(bw * block_bytes),
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
            c if texture_format::float_data(c) => 16,
            c => texture_format::byte_texel(c),
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
                    // Kept as RGBA8 where the device has no R8/RG8 storage for this use.
                    texture_format::R8_UNORM | texture_format::RG8_UNORM
                        if format == wgpu::TextureFormat::Rgba8Unorm =>
                    {
                        let n = texture_format::byte_texel(t.format);
                        converted = src
                            .chunks_exact(n)
                            .flat_map(|p| [p[0], if n == 2 { p[1] } else { 0 }, 0, 255])
                            .collect::<Vec<u8>>();
                        &converted
                    }
                    // Normalized integers from floats, in the format the texture is stored in.
                    texture_format::RG16_UNORM..=texture_format::R16_UNORM
                    | texture_format::R8_SNORM..=texture_format::RGBA8_SNORM => {
                        converted = norm_texels(format, src);
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
        wrap_w: 0,
        aniso_level: 1,
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

/// The mip levels a texture gets on the GPU: the requested count, at most a full chain. A
/// volume's chain follows its largest side including the depth (a 4x4x16 Texture3D has 5
/// levels, as in Unity); an array's or cube's layers never add levels.
fn gpu_mip_count(width: u32, height: u32, layers: u32, volume: bool, requested: i32) -> u32 {
    let largest = if volume {
        width.max(height).max(layers)
    } else {
        width.max(height)
    };
    (requested.max(1) as u32).min(32 - largest.max(1).leading_zeros())
}

#[cfg(test)]
mod tests {
    use super::*;
    use unbound_runtime::render_protocol::{SubMeshPacket, Vector3, Vector4};

    #[test]
    fn volumes_deeper_than_wide_keep_every_level() {
        // tests/projects/texturearrays: a 4x4x16 Texture3D has 5 levels; a 4x4 array of 16
        // slices has 3.
        assert_eq!(gpu_mip_count(4, 4, 16, true, 5), 5);
        assert_eq!(gpu_mip_count(4, 4, 16, false, 5), 3);
        assert_eq!(gpu_mip_count(16, 8, 12, true, 5), 5);
        assert_eq!(gpu_mip_count(64, 64, 2, true, 99), 7);
        assert_eq!(gpu_mip_count(8, 8, 1, false, 0), 1);
    }

    #[test]
    fn half_floats() {
        assert_eq!(f32_to_f16(0.0), 0);
        assert_eq!(f32_to_f16(1.0), 0x3c00);
        assert_eq!(f32_to_f16(-2.0), 0xc000);
        assert_eq!(f32_to_f16(0.5), 0x3800);
        assert_eq!(f32_to_f16(65504.0), 0x7bff);
        assert_eq!(f32_to_f16(1e9), 0x7c00);
        // Rounding up past the mantissa's last step carries into the exponent (a baked lightmap
        // texel just below 0.5 read 0.25, tests/projects/urp lightmap_shadowmask).
        let below = |v: f32| f32::from_bits(v.to_bits() - 1);
        assert_eq!(f32_to_f16(below(0.5)), 0x3800);
        assert_eq!(f32_to_f16(below(2.0)), 0x4000);
        assert_eq!(f32_to_f16(-below(1.0)), 0xbc00);
        assert_eq!(f32_to_f16(65520.0), 0x7c00);
        assert_eq!(f32_to_f16(below(6.1035156e-5)), 0x0400);
        for bits in (0u16..0x7c00).step_by(7) {
            assert_eq!(f32_to_f16(f16_to_f32(bits)), bits, "{bits:#x}");
        }
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
                topology: 2,
                base_vertex: 0,
            }],
            ..Default::default()
        };
        let (vertices, indices, submeshes) = build_mesh(&mesh);
        assert_eq!(vertices.len(), 4);
        assert_eq!(vertices[0].color, [1.0; 4]);
        assert_eq!(indices, vec![0, 1, 2, 0, 2, 3]);
        assert_eq!(submeshes[0].count, 6);
        assert_eq!(submeshes[0].topology, Topology::Triangles);
        assert_eq!(size_of::<Vertex>(), 120);
    }

    #[test]
    fn submesh_topologies_are_unitys_mesh_topology() {
        // MeshTopology: Triangles 0, Quads 2, Lines 3, LineStrip 4, Points 5.
        let sub = |topology| SubMeshPacket {
            index_start: 0,
            index_count: 4,
            topology,
            base_vertex: 0,
        };
        let mesh = owned::Mesh {
            positions: vec![Vector3::default(); 4],
            indices: vec![0, 1, 2, 3],
            submeshes: [0, 3, 4, 5].map(sub).to_vec(),
            ..Default::default()
        };
        let (_, indices, submeshes) = build_mesh(&mesh);
        let topologies: Vec<_> = submeshes.iter().map(|s| s.topology).collect();
        assert_eq!(
            topologies,
            [
                Topology::Triangles,
                Topology::Lines,
                Topology::LineStrip,
                Topology::Points
            ]
        );
        // Only quads rewrite their indices.
        assert_eq!(indices.len(), 16);
    }

    /// Every usage and flag: a device that does everything with the format.
    fn full() -> Option<wgpu::TextureFormatFeatures> {
        Some(wgpu::TextureFormatFeatures {
            allowed_usages: wgpu::TextureUsages::all(),
            flags: wgpu::TextureFormatFeatureFlags::all(),
        })
    }

    fn full_caps() -> NormFormatCaps {
        NormFormatCaps {
            r16: full(),
            rg16: full(),
            rgba16: full(),
            rgb10a2: full(),
            r8snorm: full(),
            rg8snorm: full(),
            rgba8snorm: full(),
            r8: full(),
            rg8: full(),
            integer_storage: (1 << 18) - 1,
            packed_float_target: true,
        }
    }

    fn packet(format: i32, render_target: bool, random_write: bool) -> owned::Texture {
        owned::Texture {
            width: 4,
            height: 4,
            format,
            mip_count: 1,
            render_target,
            random_write,
            samples: 1,
            layers: 1,
            dimension: 2,
            ..Default::default()
        }
    }

    fn stored(
        code: i32,
        rt: bool,
        rw: bool,
        samples: u32,
        caps: &NormFormatCaps,
    ) -> TextureStorage {
        texture_storage(&packet(code, rt, rw), samples, caps)
    }

    #[test]
    fn norm_codes_keep_their_precision() {
        use wgpu::TextureFormat as F;
        let caps = full_caps();
        for rt in [false, true] {
            for (code, format, bytes) in [
                (13, F::Rg16Unorm, 4),
                (14, F::Rgba16Unorm, 8),
                (15, F::Rgb10a2Unorm, 4),
                (16, F::R16Unorm, 2),
                (35, F::R8Snorm, 1),
                (36, F::Rg8Snorm, 2),
                (37, F::Rgba8Snorm, 4),
            ] {
                assert_eq!(
                    stored(code, rt, false, if rt { 4 } else { 1 }, &caps),
                    TextureStorage {
                        format,
                        bytes_per_pixel: bytes,
                        fallback: None
                    },
                    "code {code}, render target {rt}"
                );
            }
        }
        // The codes before them are stored as they were.
        for (code, format) in [
            (0, F::Rgba8Unorm),
            (1, F::Rgba8Unorm),
            (2, F::Rgba16Float),
            (4, F::Rg11b10Ufloat),
            (6, F::R32Float),
            (9, F::Rgba32Float),
            (10, F::Rg32Float),
            (11, F::R16Float),
            (12, F::Rg16Float),
        ] {
            let s = stored(code, true, false, 1, &caps);
            assert_eq!((s.format, s.fallback), (format, None), "code {code}");
        }
        // Packed 11/11/10 floats where the device draws into them, by the format's features;
        // else, and for a texture kernels write, half floats.
        let unpacked = NormFormatCaps {
            packed_float_target: false,
            ..caps
        };
        assert_eq!(stored(4, true, false, 1, &unpacked).format, F::Rgba16Float);
        assert_eq!(stored(4, true, true, 1, &caps).format, F::Rgba16Float);
    }

    /// Block-compressed imports keep their format (render targets never are), and a device
    /// without BC formats gets what the blocks decode to.
    #[test]
    fn block_codes_are_stored_compressed_or_decoded() {
        use wgpu::TextureFormat as F;
        for (code, format) in [
            (texture_format::BC1_COLOR, F::Bc1RgbaUnorm),
            (texture_format::BC3_DATA, F::Bc3RgbaUnorm),
            (texture_format::BC4, F::Bc4RUnorm),
            (texture_format::BC5, F::Bc5RgUnorm),
            (texture_format::BC6H, F::Bc6hRgbUfloat),
            (texture_format::BC7_COLOR, F::Bc7RgbaUnorm),
        ] {
            let s = stored(code, false, false, 1, &full_caps());
            assert_eq!((s.format, s.fallback), (format, None), "code {code}");
        }
        // One BC1 block: c0 = white, c1 = black, every texel index 1 (c1), as a 2x2 level.
        let t = owned::Texture {
            width: 2,
            height: 2,
            format: texture_format::BC1_COLOR,
            mip_count: 1,
            data: vec![0xff, 0xff, 0, 0, 0x55, 0x55, 0x55, 0x55],
            ..owned::Texture::default()
        };
        let decoded = decode_blocks(&t);
        assert_eq!(decoded.format, texture_format::SRGB_RGBA8);
        assert_eq!(decoded.data, [0, 0, 0, 255].repeat(4));
        let bc4 = decode_blocks(&owned::Texture {
            format: texture_format::BC4,
            ..t
        });
        assert_eq!(bc4.format, texture_format::LINEAR_RGBA8);
    }

    /// One- and two-channel 8-bit uploads keep their channels.
    #[test]
    fn one_and_two_channel_codes_keep_their_channels() {
        for (code, format, bytes) in [
            (texture_format::R8_UNORM, wgpu::TextureFormat::R8Unorm, 1),
            (texture_format::RG8_UNORM, wgpu::TextureFormat::Rg8Unorm, 2),
        ] {
            let s = stored(code, false, false, 1, &full_caps());
            assert_eq!((s.format, s.bytes_per_pixel), (format, bytes));
            assert!(s.fallback.is_none());
            assert_eq!(texture_format::byte_texel(code), bytes as usize);
            // Render textures of the format share it (copies between them and textures).
            assert_eq!(stored(code, true, false, 4, &full_caps()).format, format);
            let none = NormFormatCaps {
                r8: None,
                rg8: None,
                ..full_caps()
            };
            let s = stored(code, false, false, 1, &none);
            assert_eq!(
                (s.format, s.bytes_per_pixel),
                (wgpu::TextureFormat::Rgba8Unorm, 4)
            );
            assert!(matches!(s.fallback, Some(FormatFallback::Unsupported(_))));
        }
    }

    #[test]
    fn unknown_codes_are_rejected() {
        for code in [-1, texture_format::RG8_UNORM + 1, 99, i32::MAX] {
            let s = stored(code, true, false, 1, &full_caps());
            assert_eq!(s.format, wgpu::TextureFormat::Rgba8Unorm);
            assert!(
                matches!(s.fallback, Some(FormatFallback::Unknown(_))),
                "code {code}: {:?}",
                s.fallback
            );
        }
        assert!(norm_format(12, &full_caps(), true, false, 1).is_err());
    }

    #[test]
    fn norm16_without_the_device_feature_falls_back_to_rgba8() {
        let caps = NormFormatCaps {
            r16: None,
            rg16: None,
            rgba16: None,
            rgb10a2: full(),
            r8snorm: full(),
            rg8snorm: full(),
            rgba8snorm: full(),
            r8: full(),
            rg8: full(),
            integer_storage: 0,
            packed_float_target: false,
        };
        for code in [13, 14, 16] {
            let s = stored(code, true, false, 1, &caps);
            assert_eq!(s.format, wgpu::TextureFormat::Rgba8Unorm, "code {code}");
            assert_eq!(s.bytes_per_pixel, 4);
            assert!(
                matches!(&s.fallback, Some(FormatFallback::Unsupported(why)) if why.contains("TEXTURE_FORMAT_16BIT_NORM")),
                "code {code}: {:?}",
                s.fallback
            );
        }
        assert_eq!(
            stored(15, true, false, 1, &caps).format,
            wgpu::TextureFormat::Rgb10a2Unorm
        );
        // Before the renderer reads the device, every one of them falls back.
        for code in [13, 14, 15, 16, 35, 36, 37] {
            assert!(
                stored(code, false, false, 1, &NormFormatCaps::NONE)
                    .fallback
                    .is_some()
            );
        }
    }

    /// WebGPU's guaranteed features (no adapter-specific ones) do not let 16-bit normalized
    /// formats be drawn into: plain textures fall back too, so copies between them and render
    /// textures of the format keep working.
    #[test]
    fn norm_formats_follow_the_devices_format_features() {
        use wgpu::{Features, TextureFormat as F};
        let guaranteed = |f: F, features: Features| Some(f.guaranteed_format_features(features));
        let caps = NormFormatCaps {
            r16: guaranteed(F::R16Unorm, Features::TEXTURE_FORMAT_16BIT_NORM),
            rg16: guaranteed(F::Rg16Unorm, Features::TEXTURE_FORMAT_16BIT_NORM),
            rgba16: guaranteed(F::Rgba16Unorm, Features::TEXTURE_FORMAT_16BIT_NORM),
            rgb10a2: guaranteed(F::Rgb10a2Unorm, Features::empty()),
            r8snorm: guaranteed(F::R8Snorm, Features::empty()),
            rg8snorm: guaranteed(F::Rg8Snorm, Features::empty()),
            rgba8snorm: guaranteed(F::Rgba8Snorm, Features::empty()),
            r8: full(),
            rg8: full(),
            integer_storage: 0,
            packed_float_target: false,
        };
        for code in [13, 14, 16] {
            for rt in [false, true] {
                assert_eq!(stored(code, rt, false, 1, &caps).format, F::Rgba8Unorm);
            }
        }
        // 10-10-10-2 is drawn into, blended and resolved at 4 samples everywhere, but not at 2
        // or as storage unless the adapter says so.
        assert_eq!(stored(15, true, false, 4, &caps).format, F::Rgb10a2Unorm);
        assert_eq!(stored(15, true, false, 2, &caps).format, F::Rgba8Unorm);
        assert_eq!(stored(15, true, true, 1, &caps).format, F::Rgba8Unorm);
    }

    /// Kernels write every one of the formats where the device lets them read and write it.
    #[test]
    fn kernels_write_the_formats_the_device_stores_for_them() {
        use wgpu::TextureFormat as F;
        let caps = full_caps();
        for (code, format, bytes) in [
            (13, F::Rg16Unorm, 4),
            (14, F::Rgba16Unorm, 8),
            (15, F::Rgb10a2Unorm, 4),
            (16, F::R16Unorm, 2),
        ] {
            assert_eq!(
                stored(code, true, true, 1, &caps),
                TextureStorage {
                    format,
                    bytes_per_pixel: bytes,
                    fallback: None
                },
                "code {code}"
            );
        }
    }

    /// Where the device cannot let kernels read and write a format (no storage binding, or
    /// read-only/write-only access only), its RW render textures fall back, loudly; the same
    /// format without kernel writes is kept.
    #[test]
    fn kernel_writes_follow_the_devices_storage_features() {
        use wgpu::{TextureFormatFeatureFlags as Flags, TextureUsages as U};
        let without = |usages: U, flags: Flags| {
            Some(wgpu::TextureFormatFeatures {
                allowed_usages: U::all() - usages,
                flags: Flags::all() - flags,
            })
        };
        for features in [
            without(U::STORAGE_BINDING, Flags::empty()),
            without(U::empty(), Flags::STORAGE_READ_WRITE),
            without(U::empty(), Flags::STORAGE_WRITE_ONLY),
        ] {
            let caps = NormFormatCaps {
                r16: features,
                rg16: features,
                rgba16: features,
                rgb10a2: features,
                r8snorm: features,
                rg8snorm: features,
                rgba8snorm: features,
                r8: full(),
                rg8: full(),
                integer_storage: 0,
                packed_float_target: false,
            };
            for code in [13, 14, 15, 16] {
                let s = stored(code, true, true, 1, &caps);
                assert_eq!(s.format, wgpu::TextureFormat::Rgba8Unorm, "code {code}");
                assert!(
                    matches!(&s.fallback, Some(FormatFallback::Unsupported(why)) if why.contains("kernels read and write")),
                    "code {code}: {:?}",
                    s.fallback
                );
                assert_eq!(stored(code, true, false, 1, &caps).fallback, None);
            }
        }
        // Without TEXTURE_FORMAT_16BIT_NORM, kernels cannot write the 16-bit formats either.
        let caps = NormFormatCaps {
            r16: None,
            rg16: None,
            rgba16: None,
            rgb10a2: full(),
            r8snorm: full(),
            rg8snorm: full(),
            rgba8snorm: full(),
            r8: full(),
            rg8: full(),
            integer_storage: 0,
            packed_float_target: false,
        };
        for code in [13, 14, 16] {
            assert!(
                matches!(&stored(code, true, true, 1, &caps).fallback, Some(FormatFallback::Unsupported(why)) if why.contains("TEXTURE_FORMAT_16BIT_NORM")),
                "code {code}"
            );
        }
        // WebGPU's guaranteed features give the 16-bit formats read-only/write-only storage but
        // no drawing into them: they fall back, kernel writes or not.
        let guaranteed = |f: wgpu::TextureFormat| {
            Some(f.guaranteed_format_features(wgpu::Features::TEXTURE_FORMAT_16BIT_NORM))
        };
        let caps = NormFormatCaps {
            r16: guaranteed(wgpu::TextureFormat::R16Unorm),
            rg16: guaranteed(wgpu::TextureFormat::Rg16Unorm),
            rgba16: guaranteed(wgpu::TextureFormat::Rgba16Unorm),
            rgb10a2: full(),
            r8snorm: full(),
            rg8snorm: full(),
            rgba8snorm: full(),
            r8: full(),
            rg8: full(),
            integer_storage: 0,
            packed_float_target: false,
        };
        for code in [13, 14, 16] {
            assert_eq!(
                stored(code, true, true, 1, &caps).format,
                wgpu::TextureFormat::Rgba8Unorm
            );
        }
    }

    fn floats(texels: &[[f32; 4]]) -> Vec<u8> {
        texels
            .iter()
            .flatten()
            .flat_map(|v| v.to_le_bytes())
            .collect()
    }

    #[test]
    fn snorm_texels_keep_their_sign() {
        use wgpu::TextureFormat as F;
        let floats = |v: [f32; 4]| v.iter().flat_map(|f| f.to_le_bytes()).collect::<Vec<u8>>();
        // Clamped to [-1, 1], nearest of 255 steps; NaN stores 0.
        let data = floats([-1.5, -0.5, 0.25, f32::NAN]);
        assert_eq!(norm_texels(F::Rgba8Snorm, &data), [0x81, 0xc0, 0x20, 0]);
        assert_eq!(norm_texels(F::Rg8Snorm, &data), [0x81, 0xc0]);
        assert_eq!(norm_texels(F::R8Snorm, &data), [0x81]);
        // Read back: -128 reads -1 as -127 does; one-channel formats read (r, 0, 0, 1).
        assert_eq!(
            norm_texel_to_f32(F::Rgba8Snorm, &[0x80, 0x81, 0x7f, 0]),
            Some([-1.0, -1.0, 1.0, 0.0])
        );
        assert_eq!(
            norm_texel_to_f32(F::R8Snorm, &[0xc0]),
            Some([-64.0 / 127.0, 0.0, 0.0, 1.0])
        );
        assert_eq!(norm_texel_to_f32(F::Rg8Snorm, &[0x7f]), None);
    }

    #[test]
    fn norm_texels_round_to_the_nearest_step() {
        use wgpu::TextureFormat as F;
        let data = floats(&[[0.0, 0.5, 1.0, 1.0], [-1.0, 2.0, f32::NAN, 0.25]]);
        let u16s = |bytes: Vec<u8>| -> Vec<u16> {
            bytes
                .chunks_exact(2)
                .map(|b| u16::from_le_bytes([b[0], b[1]]))
                .collect()
        };
        assert_eq!(
            u16s(norm_texels(F::Rgba16Unorm, &data)),
            [0, 32768, 65535, 65535, 0, 65535, 0, 16384]
        );
        assert_eq!(u16s(norm_texels(F::Rg16Unorm, &data)), [0, 32768, 0, 65535]);
        assert_eq!(u16s(norm_texels(F::R16Unorm, &data)), [0, 0]);
        let packed: Vec<u32> = norm_texels(F::Rgb10a2Unorm, &data)
            .chunks_exact(4)
            .map(|b| u32::from_le_bytes(b.try_into().unwrap()))
            .collect();
        assert_eq!(
            packed,
            [512 << 10 | 1023 << 20 | 3 << 30, 1023 << 10 | 1 << 30]
        );
        // The RGBA8 fallback: the precision these formats had before.
        assert_eq!(
            norm_texels(F::Rgba8Unorm, &data),
            [0, 128, 255, 255, 0, 255, 0, 64]
        );
    }

    #[test]
    fn norm_texels_read_back_as_floats() {
        use wgpu::TextureFormat as F;
        let read = |format: F, texel: &[[f32; 4]]| {
            norm_texel_to_f32(format, &norm_texels(format, &floats(texel))).unwrap()
        };
        assert_eq!(
            read(F::Rg16Unorm, &[[1.0, 0.0, 0.7, 0.2]]),
            [1.0, 0.0, 0.0, 1.0]
        );
        assert_eq!(
            read(F::R16Unorm, &[[0.5, 0.7, 0.7, 0.2]]),
            [32768.0 / 65535.0, 0.0, 0.0, 1.0]
        );
        assert_eq!(
            read(F::Rgba16Unorm, &[[1.0, 0.0, 1.0, 0.0]]),
            [1.0, 0.0, 1.0, 0.0]
        );
        // A 16-bit step, finer than 8 bits can hold.
        let v = read(F::Rgba16Unorm, &[[1.0 / 65535.0; 4]])[0];
        assert_eq!(v, 1.0 / 65535.0);
        assert_eq!(
            read(F::Rgb10a2Unorm, &[[1.0, 0.0, 1.0, 1.0]]),
            [1.0, 0.0, 1.0, 1.0]
        );
        assert_eq!(
            read(F::Rgb10a2Unorm, &[[0.0, 0.0, 0.0, 2.0 / 3.0]])[3],
            2.0 / 3.0
        );
        assert_eq!(norm_texel_to_f32(F::Rgba8Unorm, &[0; 4]), None);
        assert_eq!(norm_texel_to_f32(F::Rgba16Unorm, &[0; 4]), None);
    }

    #[test]
    fn integer_codes_are_stored_in_their_own_format() {
        use wgpu::TextureFormat as F;
        for (channels, bits, signed, format) in [
            (1, 8, false, F::R8Uint),
            (1, 8, true, F::R8Sint),
            (1, 32, true, F::R32Sint),
            (2, 16, true, F::Rg16Sint),
            (4, 16, false, F::Rgba16Uint),
            (4, 32, false, F::Rgba32Uint),
            (4, 32, true, F::Rgba32Sint),
        ] {
            let code = texture_format::integer(channels, bits, signed);
            assert_eq!(integer_format(code), Some(format));
            assert_eq!(integer_code(format), Some(code));
            assert!(is_integer(format));
            let s = stored(code, true, true, 1, &full_caps());
            assert_eq!((s.format, s.fallback), (format, None), "code {code}");
            assert_eq!(s.bytes_per_pixel, format.block_copy_size(None).unwrap());
        }
        assert_eq!(
            texture_format::integer(4, 32, true),
            texture_format::INTEGER_LAST
        );
        assert!(!is_integer(F::Rgba8Unorm) && integer_code(F::Rgba8Unorm).is_none());
        // Kernels write a format the device cannot store into as four 32-bit channels.
        let code = texture_format::integer(1, 8, true);
        let s = stored(code, true, true, 1, &NormFormatCaps::NONE);
        assert_eq!(s.format, F::Rgba32Sint);
        assert!(matches!(s.fallback, Some(FormatFallback::Unsupported(_))));
        assert_eq!(
            stored(code, true, false, 1, &NormFormatCaps::NONE).format,
            F::R8Sint
        );
    }

    #[test]
    fn integer_clears_store_the_floats_bits_clamped() {
        use wgpu::TextureFormat as F;
        let color = wgpu::Color {
            r: 3.0,
            g: -2.0,
            b: 1000.5,
            a: 0.5,
        };
        let c = integer_clear(F::R32Sint, color);
        assert_eq!(
            (c.r, c.g),
            (0x4040_0000 as f64, (0xc000_0000u32 as i32) as f64)
        );
        assert_eq!(integer_clear(F::R8Sint, color).r, 127.0);
        assert_eq!(integer_clear(F::Rg16Sint, color).g, -32768.0);
        let c = integer_clear(F::Rgba32Uint, color);
        assert_eq!((c.g, c.b), (0xc000_0000u32 as f64, 0x447a_2000 as f64));
        assert_eq!(integer_clear(F::Rgba8Uint, color).a, 255.0);
        assert_eq!(integer_clear(F::Rgba8Unorm, color), color);
        let half = [0.25, 0.5, 2.0, -1.0];
        assert_eq!(unorm8_clear(F::Rgba8Unorm, half), half);
        let c = unorm8_clear_color(
            F::R8Unorm,
            wgpu::Color {
                r: 0.5,
                g: 0.25,
                b: 0.0,
                a: 1.0,
            },
        );
        assert_eq!((c.r * 255.0).round(), 127.0);
        assert_eq!((c.g * 255.0).round(), 64.0);
        assert_eq!(
            unorm8_clear(F::Rg8Unorm, half),
            [64.0 / 255.0, 127.0 / 255.0, 1.0, 0.0]
        );
    }
}
