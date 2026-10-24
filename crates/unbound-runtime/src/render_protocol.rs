//! The structures the managed engine hands the renderer; see docs/RENDER_PROTOCOL.md. The layouts
//! are shared with `managed/UnityEngine/Graphics/RenderProtocol.cs`.
#![allow(missing_docs)]

pub use crate::render_commands as commands;

#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Vector3 {
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Vector4 {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub w: f32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Color {
    pub r: f32,
    pub g: f32,
    pub b: f32,
    pub a: f32,
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Color32 {
    pub r: u8,
    pub g: u8,
    pub b: u8,
    pub a: u8,
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Rect {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}

/// Unity's column-major matrix: `m[col * 4 + row]` (m00, m10, m20, m30, m01, ...).
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Matrix4x4 {
    pub m: [f32; 16],
}

impl Default for Matrix4x4 {
    fn default() -> Self {
        let mut m = [0.0; 16];
        m[0] = 1.0;
        m[5] = 1.0;
        m[10] = 1.0;
        m[15] = 1.0;
        Self { m }
    }
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct SubMeshPacket {
    pub index_start: i32,
    pub index_count: i32,
    pub topology: i32,
    pub base_vertex: i32,
}

#[repr(C)]
#[derive(Debug)]
pub struct MeshPacket {
    pub vertex_count: i32,
    pub positions: *const Vector3,
    pub normals: *const Vector3,
    pub tangents: *const Vector4,
    pub colors: *const Color32,
    pub uv: [*const Vector4; 4],
    pub index_count: i32,
    pub indices: *const u32,
    pub submesh_count: i32,
    pub submeshes: *const SubMeshPacket,
}

#[repr(C)]
#[derive(Debug)]
pub struct TexturePacket {
    pub width: i32,
    pub height: i32,
    /// 0 sRGB RGBA8, 1 linear RGBA8, 2 RGBA floats (kept as half floats), 3 Alpha8, 4 packed
    /// 11/11/10 floats (render targets only, no data).
    pub format: i32,
    pub mip_count: i32,
    pub data: *const u8,
    pub data_length: i64,
    pub filter_mode: i32,
    pub wrap_u: i32,
    pub wrap_v: i32,
    pub aniso_level: i32,
    pub render_target: i32,
    pub depth_bits: i32,
    /// Images (6 for a cubemap); the data holds each image's mip chain in turn.
    pub layers: i32,
    /// Unity's TextureDimension (2 2D, 3 3D, 4 cube, 5 2D array).
    pub dimension: i32,
}

#[repr(C)]
#[derive(Debug, Default, Clone, Copy)]
pub struct ImportedTextureInfo {
    pub width: i32,
    pub height: i32,
    pub mip_count: i32,
    pub srgb: i32,
    pub filter_mode: i32,
    pub wrap_u: i32,
    pub wrap_v: i32,
    pub aniso_level: i32,
    pub readable: i32,
    pub sprite_count: i32,
}

#[repr(C)]
#[derive(Debug)]
pub struct MaterialProperty {
    pub name: *const u8,
    pub name_length: i32,
    pub kind: i32,
    pub value: Vector4,
    pub texture: u64,
    pub texture_st: Vector4,
}

#[repr(C)]
#[derive(Debug)]
pub struct MaterialPacket {
    pub shader_name: *const u8,
    pub shader_name_length: i32,
    pub render_queue: i32,
    pub keywords: *const u8,
    pub keywords_length: i32,
    pub property_count: i32,
    pub properties: *const MaterialProperty,
}

/// A slice from a pointer and a count the managed side gave (empty when null).
///
/// # Safety
/// The pointer must be valid for `count` elements for the duration of the call.
pub unsafe fn slice<'a, T>(ptr: *const T, count: i32) -> &'a [T] {
    if ptr.is_null() || count <= 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(ptr, count as usize) }
    }
}

/// A UTF-8 string from a pointer and a length.
///
/// # Safety
/// As [`slice`].
pub unsafe fn string<'a>(ptr: *const u8, length: i32) -> &'a str {
    std::str::from_utf8(unsafe { slice(ptr, length) }).unwrap_or("")
}

/// Owned copies of the packets, for renderers that keep them past the call.
pub mod owned {
    use super::*;

    #[derive(Debug, Clone, Default)]
    pub struct Mesh {
        pub positions: Vec<Vector3>,
        pub normals: Vec<Vector3>,
        pub tangents: Vec<Vector4>,
        pub colors: Vec<Color32>,
        pub uv: [Vec<Vector4>; 4],
        pub indices: Vec<u32>,
        pub submeshes: Vec<SubMeshPacket>,
    }

    impl Mesh {
        /// # Safety
        /// The packet's pointers must be valid as the protocol describes.
        pub unsafe fn from_packet(p: &MeshPacket) -> Self {
            let n = p.vertex_count;
            unsafe {
                Self {
                    positions: slice(p.positions, n).to_vec(),
                    normals: slice(p.normals, n).to_vec(),
                    tangents: slice(p.tangents, n).to_vec(),
                    colors: slice(p.colors, n).to_vec(),
                    uv: [
                        slice(p.uv[0], n).to_vec(),
                        slice(p.uv[1], n).to_vec(),
                        slice(p.uv[2], n).to_vec(),
                        slice(p.uv[3], n).to_vec(),
                    ],
                    indices: slice(p.indices, p.index_count).to_vec(),
                    submeshes: slice(p.submeshes, p.submesh_count).to_vec(),
                }
            }
        }
    }

    #[derive(Debug, Clone, Default)]
    pub struct Texture {
        pub width: i32,
        pub height: i32,
        pub format: i32,
        pub mip_count: i32,
        pub data: Vec<u8>,
        pub filter_mode: i32,
        pub wrap_u: i32,
        pub wrap_v: i32,
        pub aniso_level: i32,
        pub render_target: bool,
        /// A render target kernels write (RenderTexture.enableRandomWrite).
        pub random_write: bool,
        /// A render target's MSAA samples (1: none).
        pub samples: u32,
        pub depth_bits: i32,
        /// Images, each with its mip chain (6 for a cubemap).
        pub layers: i32,
        /// Unity's TextureDimension (2 2D, 4 cube).
        pub dimension: i32,
    }

    impl Texture {
        /// # Safety
        /// As [`Mesh::from_packet`].
        pub unsafe fn from_packet(p: &TexturePacket) -> Self {
            Self {
                width: p.width,
                height: p.height,
                format: p.format,
                mip_count: p.mip_count,
                data: unsafe { slice(p.data, p.data_length as i32) }.to_vec(),
                filter_mode: p.filter_mode,
                wrap_u: p.wrap_u,
                wrap_v: p.wrap_v,
                aniso_level: p.aniso_level,
                render_target: p.render_target & 1 != 0,
                random_write: p.render_target & 2 != 0,
                // Bits 8..15: the MSAA sample count.
                samples: ((p.render_target >> 8) & 0xff).max(1) as u32,
                depth_bits: p.depth_bits,
                layers: p.layers.max(1),
                dimension: if p.dimension == 0 { 2 } else { p.dimension },
            }
        }
    }

    #[derive(Debug, Clone, PartialEq)]
    pub enum PropertyValue {
        Float(f32),
        Vector([f32; 4]),
        Texture {
            handle: u64,
            st: [f32; 4],
        },
        Int(i32),
        Matrix([f32; 16]),
        /// A graphics or compute buffer's handle.
        Buffer(u64),
        /// An array uniform: elements of `components` floats each (4 vectors, 16 matrices); 0:
        /// consecutive values copied from the uniform's offset (ComputeShader.SetFloats).
        Array {
            components: u32,
            data: Vec<f32>,
        },
        /// A texture mip level bound for random writes (a compute kernel's RW texture).
        Image {
            handle: u64,
            mip: i32,
        },
    }

    /// A material (or global) property's name and value.
    ///
    /// # Safety
    /// As [`Mesh::from_packet`].
    unsafe fn property(prop: &MaterialProperty) -> (String, PropertyValue) {
        let name = unsafe { string(prop.name, prop.name_length) }.to_string();
        let v = prop.value;
        let value = match prop.kind {
            0 => PropertyValue::Float(v.x),
            2 => PropertyValue::Texture {
                handle: prop.texture,
                st: [
                    prop.texture_st.x,
                    prop.texture_st.y,
                    prop.texture_st.z,
                    prop.texture_st.w,
                ],
            },
            3 => PropertyValue::Int(v.x as i32),
            _ => PropertyValue::Vector([v.x, v.y, v.z, v.w]),
        };
        (name, value)
    }

    #[derive(Debug, Clone, Default)]
    pub struct Material {
        pub shader: String,
        pub render_queue: i32,
        pub keywords: Vec<String>,
        pub properties: Vec<(String, PropertyValue)>,
    }

    impl Material {
        /// # Safety
        /// As [`Mesh::from_packet`].
        pub unsafe fn from_packet(p: &MaterialPacket) -> Self {
            unsafe {
                let properties = slice(p.properties, p.property_count)
                    .iter()
                    .map(|prop| property(prop))
                    .collect();
                Self {
                    shader: string(p.shader_name, p.shader_name_length).to_string(),
                    render_queue: p.render_queue,
                    keywords: string(p.keywords, p.keywords_length)
                        .split_whitespace()
                        .map(String::from)
                        .collect(),
                    properties,
                }
            }
        }

        pub fn get(&self, name: &str) -> Option<&PropertyValue> {
            self.properties
                .iter()
                .find(|(n, _)| n == name)
                .map(|(_, v)| v)
        }
    }
}

/// What executes the protocol. The windowed app installs a GPU renderer; headless runs keep the
/// default, which only records what it was given.
pub trait RenderBackend: Send + Sync {
    fn upload_mesh(&self, handle: u64, mesh: owned::Mesh);
    fn release_mesh(&self, handle: u64);
    fn upload_texture(&self, handle: u64, texture: owned::Texture);
    fn release_texture(&self, handle: u64);
    fn update_material(&self, handle: u64, material: owned::Material);
    fn release_material(&self, handle: u64);
    /// Executes rendering commands in order (see [`commands`]).
    fn submit_commands(&self, commands: Vec<commands::Command>);
    /// The pixels of a rectangle of a texture (0: the screen), after every command submitted so
    /// far: RGBA rows bottom first, 4 bytes per pixel, or 16 (f32) when `float`.
    fn read_pixels(&self, _texture: u64, _rect: [i32; 4], _float: bool) -> Option<Vec<u8>> {
        None
    }
    /// Bytes of a buffer after every command submitted so far.
    fn read_buffer(&self, _buffer: u64, _offset: u64, _size: u64) -> Option<Vec<u8>> {
        None
    }
    /// A shader's SubShader for the active render pipeline (after its fallbacks) as JSON:
    /// `{ shader, tags, passes: [{ name, tags }] }`.
    fn shader_passes(&self, _shader: &str) -> Result<String, String> {
        Err("no renderer".into())
    }
    /// A compute kernel's thread group size: (file, kernel, keywords).
    fn kernel_threads(
        &self,
        _file: &str,
        _kernel: &str,
        _keywords: &[String],
    ) -> Result<[u32; 3], String> {
        Err("no renderer".into())
    }
}

/// Accepts everything and draws nothing.
#[derive(Default)]
pub struct NullBackend;

impl RenderBackend for NullBackend {
    fn upload_mesh(&self, _: u64, _: owned::Mesh) {}
    fn release_mesh(&self, _: u64) {}
    fn upload_texture(&self, _: u64, _: owned::Texture) {}
    fn release_texture(&self, _: u64) {}
    fn update_material(&self, _: u64, _: owned::Material) {}
    fn release_material(&self, _: u64) {}
    fn submit_commands(&self, _: Vec<commands::Command>) {}
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::mem::{offset_of, size_of};

    /// The managed side's layouts (checked by a matching test there).
    #[test]
    fn layouts() {
        assert_eq!(size_of::<Matrix4x4>(), 64);
        assert_eq!(size_of::<MaterialProperty>(), 56);
        assert_eq!(size_of::<MeshPacket>(), 104);
        assert_eq!(offset_of!(MeshPacket, uv), 40);
    }
}
