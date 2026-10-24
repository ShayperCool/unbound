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
    pub index: wgpu::Buffer,
    pub submeshes: Vec<SubMesh>,
}

/// Interleaves a mesh and rewrites its submeshes' indices into one list (quads become triangles).
pub fn build_mesh(mesh: &owned::Mesh) -> (Vec<Vertex>, Vec<u32>, Vec<SubMesh>) {
    let n = mesh.positions.len();
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
            if let Some(v) = mesh.uv[channel].get(i) {
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
    // Anisotropy needs linear filtering everywhere.
    let aniso = if key.filter >= 2 {
        key.aniso.clamp(1, 16) as u16
    } else {
        1
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
        ..Default::default()
    })
}

pub struct GpuTexture {
    pub texture: wgpu::Texture,
    /// Raw view (gamma projects; data textures).
    pub view: wgpu::TextureView,
    /// sRGB view of color textures, sampled in linear projects.
    pub srgb_view: Option<wgpu::TextureView>,
    pub sampler_key: SamplerKey,
    pub depth: Option<wgpu::TextureView>,
    pub width: u32,
    pub height: u32,
    pub format: i32,
    pub mip_count: u32,
    pub render_target: bool,
    pub alpha_only: bool,
    /// Bumped on every change: bind groups that use the texture compare it.
    pub generation: u64,
    /// The main-texture bind group per color space.
    pub bind_groups: [Option<wgpu::BindGroup>; 2],
}

impl GpuTexture {
    pub fn view_for(&self, color_space: i32) -> &wgpu::TextureView {
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
    let width = t.width.max(1) as u32;
    let height = t.height.max(1) as u32;
    let max_mips = 32 - width.max(height).leading_zeros();
    let mip_count = (t.mip_count.max(1) as u32).min(max_mips);
    let render_target = t.render_target;
    // Float textures are stored as half floats: filterable and blendable everywhere.
    let (format, bytes_per_pixel) = match t.format {
        2 => (wgpu::TextureFormat::Rgba16Float, 8),
        _ => (wgpu::TextureFormat::Rgba8Unorm, 4),
    };
    let color = t.format == 0;
    let sampler_key = SamplerKey {
        filter: t.filter_mode,
        wrap_u: t.wrap_u,
        wrap_v: t.wrap_v,
        aniso: t.aniso_level,
    };
    let reuse = existing.filter(|e| {
        e.width == width
            && e.height == height
            && e.mip_count == mip_count
            && e.format == t.format
            && e.render_target == render_target
            && (e.depth.is_some() == (t.depth_bits > 0))
    });
    let mut gpu = match reuse {
        Some(mut e) => {
            e.sampler_key = sampler_key;
            e.bind_groups = [None, None];
            e
        }
        None => {
            let mut usage = wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_DST;
            if render_target {
                usage |= wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC;
            }
            let view_formats: &[wgpu::TextureFormat] = if color {
                &[wgpu::TextureFormat::Rgba8UnormSrgb]
            } else {
                &[]
            };
            let texture = device.create_texture(&wgpu::TextureDescriptor {
                label: Some("unbound texture"),
                size: wgpu::Extent3d {
                    width,
                    height,
                    depth_or_array_layers: 1,
                },
                mip_level_count: mip_count,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format,
                usage,
                view_formats,
            });
            let view = texture.create_view(&wgpu::TextureViewDescriptor::default());
            let srgb_view = color.then(|| {
                texture.create_view(&wgpu::TextureViewDescriptor {
                    format: Some(wgpu::TextureFormat::Rgba8UnormSrgb),
                    ..Default::default()
                })
            });
            let depth = (render_target && t.depth_bits > 0).then(|| {
                device
                    .create_texture(&wgpu::TextureDescriptor {
                        label: Some("unbound render texture depth"),
                        size: wgpu::Extent3d {
                            width,
                            height,
                            depth_or_array_layers: 1,
                        },
                        mip_level_count: 1,
                        sample_count: 1,
                        dimension: wgpu::TextureDimension::D2,
                        format: wgpu::TextureFormat::Depth24PlusStencil8,
                        usage: wgpu::TextureUsages::RENDER_ATTACHMENT,
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
                width,
                height,
                format: t.format,
                mip_count,
                render_target,
                alpha_only: t.format == 3,
                generation,
                bind_groups: [None, None],
            }
        }
    };
    gpu.generation = generation;

    if !t.data.is_empty() {
        let source_bpp = match t.format {
            2 => 16,
            3 => 1,
            _ => 4,
        };
        let mut offset = 0usize;
        for level in 0..mip_count {
            let w = (width >> level).max(1);
            let h = (height >> level).max(1);
            let len = (w * h) as usize * source_bpp;
            if offset + len > t.data.len() {
                tracing::warn!(
                    "texture data is shorter than its {mip_count} mip levels ({} bytes)",
                    t.data.len()
                );
                break;
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
                2 => {
                    converted = src
                        .as_chunks::<4>()
                        .0
                        .iter()
                        .flat_map(|c| {
                            f32_to_f16(f32::from_le_bytes([c[0], c[1], c[2], c[3]])).to_le_bytes()
                        })
                        .collect::<Vec<u8>>();
                    &converted
                }
                _ => src,
            };
            queue.write_texture(
                wgpu::TexelCopyTextureInfo {
                    texture: &gpu.texture,
                    mip_level: level,
                    origin: wgpu::Origin3d::ZERO,
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
                    depth_or_array_layers: 1,
                },
            );
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
        depth_bits: 0,
    };
    let mut map = HashMap::new();
    for (name, rgba) in [
        ("white", [255, 255, 255, 255]),
        ("black", [0, 0, 0, 0]),
        ("gray", [128, 128, 128, 128]),
        ("grey", [128, 128, 128, 128]),
        ("linearGray", [188, 188, 188, 188]),
        ("bump", [128, 128, 255, 128]),
        ("red", [255, 0, 0, 0]),
    ] {
        map.insert(name, upload_texture(device, queue, None, &pixel(rgba), 0));
    }
    map
}

#[cfg(test)]
mod tests {
    use super::*;
    use unbound_runtime::render_protocol::{SubMeshPacket, Vector3};

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
