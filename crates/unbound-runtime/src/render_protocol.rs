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
    pub colors: *const Vector4,
    /// Texture coordinate channels 0..7 (`TEXCOORD0..7`); null when a channel is unset.
    pub uv: [*const Vector4; 8],
    pub index_count: i32,
    pub indices: *const u32,
    pub submesh_count: i32,
    pub submeshes: *const SubMeshPacket,
}

/// Texture handle bits (render protocol 6, `NativeApi` version 13). The handles the managed side
/// allocates never set bits 62 and 63. A sampled binding (a material's texture property, a draw's
/// per-draw texture property, a global texture) may set [`handle::DEPTH_ELEMENT`]; every other use
/// of a handle (uploads, releases, targets, copies, main textures, storage images, kernels' and
/// read backs) must be plain. Anything else is rejected with an error.
pub mod handle {
    /// The render texture's depth buffer, sampled as `(d, 0, 0, 1)` (`RenderTextureSubElement.Depth`).
    pub const DEPTH_ELEMENT: u64 = 1 << 62;
    /// The bits no allocated handle uses.
    pub const RESERVED: u64 = 3 << 62;
    /// The largest plain handle. Textures the renderer makes itself (`GrabPass` textures) count
    /// down from it, clear of the element bits and of the "no texture" `u64::MAX`.
    pub const PLAIN_MAX: u64 = !RESERVED;

    /// A handle with no element bits (`what` names its use in the error).
    pub fn plain(h: u64, what: &str) -> Result<u64, String> {
        if h & RESERVED != 0 {
            Err(format!(
                "{what}: texture handle {h:#x} carries reserved bits"
            ))
        } else {
            Ok(h)
        }
    }

    /// A sampled binding's handle: [`DEPTH_ELEMENT`] is allowed, other reserved bits are not.
    pub fn sampled(h: u64, what: &str) -> Result<u64, String> {
        if h & RESERVED & !DEPTH_ELEMENT != 0 {
            Err(format!(
                "{what}: texture handle {h:#x} carries unknown element bits"
            ))
        } else if h == DEPTH_ELEMENT {
            Err(format!("{what}: a depth element names no texture"))
        } else {
            Ok(h)
        }
    }

    /// A sampled binding's texture and whether it samples the depth element.
    pub fn split(h: u64) -> (u64, bool) {
        (h & !RESERVED, h & DEPTH_ELEMENT != 0)
    }
}

/// The codes of [`TexturePacket::format`].
pub mod texture_format {
    /// sRGB RGBA8 (sampled through an sRGB view in linear projects).
    pub const SRGB_RGBA8: i32 = 0;
    /// Linear RGBA8: also the default color render target.
    pub const LINEAR_RGBA8: i32 = 1;
    /// RGBA floats, kept as half floats.
    pub const RGBA_HALF: i32 = 2;
    /// Alpha8, sampled as (0, 0, 0, a).
    pub const ALPHA8: i32 = 3;
    /// Packed 11/11/10 floats (render targets only, no data).
    pub const RG11B10_FLOAT: i32 = 4;
    /// A depth-only render target.
    pub const DEPTH_ONLY: i32 = 5;
    pub const R32_FLOAT: i32 = 6;
    /// BC7 color, from native producers only.
    pub const BC7_COLOR: i32 = 7;
    /// BC7 linear data, from native producers only.
    pub const BC7_DATA: i32 = 8;
    pub const RGBA32_FLOAT: i32 = 9;
    pub const RG32_FLOAT: i32 = 10;
    pub const R16_FLOAT: i32 = 11;
    pub const RG16_FLOAT: i32 = 12;
    /// Two normalized 16-bit channels (Unity's `R16G16_UNorm`: RenderTextureFormat.RG32,
    /// TextureFormat.RG32). Needs the device's 16-bit normalized formats; data: four f32 per
    /// texel, of which the first two are kept.
    pub const RG16_UNORM: i32 = 13;
    /// Four normalized 16-bit channels (Unity's `R16G16B16A16_UNorm`: RenderTextureFormat.ARGB64,
    /// TextureFormat.RGBA64). Needs the device's 16-bit normalized formats; data: four f32 per
    /// texel.
    pub const RGBA16_UNORM: i32 = 14;
    /// Normalized 10-bit RGB and 2-bit alpha packed in 32 bits, red in the low bits (Unity's
    /// `A2B10G10R10_UNormPack32`: RenderTextureFormat.ARGB2101010); data: four f32 per texel.
    pub const RGB10A2_UNORM: i32 = 15;
    /// One normalized 16-bit channel (Unity's `R16_UNorm`: RenderTextureFormat.R16,
    /// TextureFormat.R16). Needs the device's 16-bit normalized formats; data: four f32 per
    /// texel, of which the first is kept.
    pub const R16_UNORM: i32 = 16;
    /// The first integer code ([`integer`]): render targets only, no upload data.
    pub const INTEGER_FIRST: i32 = 17;
    pub const INTEGER_LAST: i32 = 34;
    /// One signed normalized 8-bit channel (Unity's `R8_SNorm`); data: four f32 per texel, of
    /// which the first is kept.
    pub const R8_SNORM: i32 = 35;
    /// Two signed normalized 8-bit channels (Unity's `R8G8_SNorm`); data: four f32 per texel,
    /// of which the first two are kept.
    pub const RG8_SNORM: i32 = 36;
    /// Four signed normalized 8-bit channels (Unity's `R8G8B8A8_SNorm`: what URP's depth-normals
    /// prepass draws into); data: four f32 per texel.
    pub const RGBA8_SNORM: i32 = 37;

    /// The integer code of `channels` (1, 2 or 4) channels of `bits` (8, 16 or 32) each, signed
    /// or unsigned (Unity's R/RG/RGBA 8/16/32 SInt/UInt): 17 + (channel index × 3 + bit index)
    /// × 2 + signed.
    pub const fn integer(channels: u32, bits: u32, signed: bool) -> i32 {
        let c = match channels {
            1 => 0,
            2 => 1,
            _ => 2,
        };
        let b = match bits {
            8 => 0,
            16 => 1,
            _ => 2,
        };
        INTEGER_FIRST + (c * 3 + b) * 2 + signed as i32
    }

    /// The packet's code, or why the renderer cannot take it: codes outside the table are
    /// rejected rather than drawn as some other format.
    pub fn validate(code: i32) -> Result<i32, String> {
        if (SRGB_RGBA8..=RGBA8_SNORM).contains(&code) {
            Ok(code)
        } else {
            Err(format!(
                "texture packet format {code} is not a known format code"
            ))
        }
    }

    /// Codes whose upload data is four f32 per texel.
    pub fn float_data(code: i32) -> bool {
        matches!(
            code,
            RGBA_HALF
                | R32_FLOAT
                | RGBA32_FLOAT..=RG16_FLOAT
                | RG16_UNORM..=R16_UNORM
                | R8_SNORM..=RGBA8_SNORM
        )
    }
}

#[repr(C)]
#[derive(Debug)]
pub struct TexturePacket {
    pub width: i32,
    pub height: i32,
    /// 0 sRGB RGBA8, 1 linear RGBA8, 2 RGBA floats (kept as half floats), 3 Alpha8, 4 packed
    /// 11/11/10 floats (render targets only, no data), 5 none: a depth-only render target, which
    /// reads as its depth when sampled; 6 R32F, 9 RGBA32F, 10 RG32F, 11 R16F, 12 RG16F,
    /// 13 RG16 unorm, 14 RGBA16 unorm, 15 RGB10A2 unorm, 16 R16 unorm, 17..=34 integer render
    /// targets, 35 R8 snorm, 36 RG8 snorm, 37 RGBA8 snorm (see [`texture_format`]).
    /// Float upload data is always four f32 channels (13..=16 and 35..=37 included). 7/8 are BC7 color/data
    /// imported textures, passed only by native producers.
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
    /// Unity's TextureWrapMode along W, the depth of a Texture3D (`NativeApi` 14).
    pub wrap_w: i32,
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
    /// TexturePacket format: 0 sRGB RGBA8, 1 linear RGBA8, 2 RGBAHalf, 9 RGBAFloat.
    pub format: i32,
    /// TextureDimension: 2 a 2D texture, 3 a Texture3D, 4 a cubemap, 5 a Texture2DArray (an
    /// array's slices and a volume's depth come from the `texture-layers` JSON call).
    pub dimension: i32,
    /// The importer's `wrapW` (`NativeApi` 14).
    pub wrap_w: i32,
    /// Unity's `TextureFormat` the Editor reports for the import (`Texture2D.format`), 0 when
    /// it is the one `format`'s data gives (`NativeApi` 31).
    pub texture_format: i32,
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
    /// A matrix's (16) or an array's floats; `components` floats per array element.
    pub data: *const f32,
    pub data_length: i32,
    pub components: i32,
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
        pub colors: Vec<Vector4>,
        pub uv: [Vec<Vector4>; 8],
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
                    uv: std::array::from_fn(|c| slice(p.uv[c], n).to_vec()),
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
        /// TextureWrapMode along W: only a Texture3D's samples use it.
        pub wrap_w: i32,
        pub aniso_level: i32,
        pub render_target: bool,
        /// A render target kernels write (RenderTexture.enableRandomWrite).
        pub random_write: bool,
        /// Automatically filter render-target mips after rendering.
        pub auto_generate_mips: bool,
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
                wrap_w: p.wrap_w,
                aniso_level: p.aniso_level,
                render_target: p.render_target & 1 != 0,
                random_write: p.render_target & 2 != 0,
                auto_generate_mips: p.render_target & 4 != 0,
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
        /// consecutive values copied from the uniform's offset (ComputeShader.SetFloats). The
        /// data is shared: the draws of one instanced call reference one array (render
        /// protocol 12).
        Array {
            components: u32,
            data: std::sync::Arc<[f32]>,
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
            // A buffer's handle travels in the texture field.
            5 => PropertyValue::Buffer(prop.texture),
            4 | 6 => {
                let data = if prop.data.is_null() || prop.data_length <= 0 {
                    &[][..]
                } else {
                    unsafe { std::slice::from_raw_parts(prop.data, prop.data_length as usize) }
                };
                if prop.kind == 4 {
                    let mut m = [0.0; 16];
                    for (o, v) in m.iter_mut().zip(data) {
                        *o = *v;
                    }
                    PropertyValue::Matrix(m)
                } else {
                    PropertyValue::Array {
                        components: prop.components.max(1) as u32,
                        data: data.into(),
                    }
                }
            }
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
        /// Where each property is, built on the first lookup (renderers look properties up per
        /// uniform per draw).
        pub index: PropertyIndex,
    }

    /// Positions of properties by name for a list that changes (global properties): `refresh`
    /// rebuilds it when the list's length changed; lookups check the name they find.
    #[derive(Default, Debug)]
    pub struct NameIndex {
        len: usize,
        map: rustc_hash::FxHashMap<String, u32>,
    }

    impl NameIndex {
        pub fn refresh(&mut self, properties: &[(String, PropertyValue)]) {
            if self.len == properties.len() && !(self.len > 0 && self.map.is_empty()) {
                return;
            }
            self.map.clear();
            for (i, (n, _)) in properties.iter().enumerate() {
                self.map.entry(n.clone()).or_insert(i as u32);
            }
            self.len = properties.len();
        }

        /// Where `name` is in `properties`, the index refreshed first.
        pub fn position(
            &mut self,
            properties: &[(String, PropertyValue)],
            name: &str,
        ) -> Option<usize> {
            self.refresh(properties);
            match self.map.get(name) {
                Some(&i) if properties.get(i as usize).is_some_and(|(n, _)| n == name) => {
                    Some(i as usize)
                }
                Some(_) => properties.iter().position(|(n, _)| n == name),
                None => None,
            }
        }

        pub fn find<'p>(
            &self,
            properties: &'p [(String, PropertyValue)],
            name: &str,
        ) -> Option<&'p PropertyValue> {
            if self.len == properties.len() {
                match self.map.get(name) {
                    Some(&i) => {
                        if let Some((n, v)) = properties.get(i as usize)
                            && n == name
                        {
                            return Some(v);
                        }
                    }
                    None => return None,
                }
            }
            properties.iter().find(|(n, _)| n == name).map(|(_, v)| v)
        }
    }

    /// Positions of properties by name. A clone starts empty; an index that no longer fits the
    /// properties (they changed after it was built) is not trusted.
    #[derive(Default)]
    pub struct PropertyIndex(std::sync::OnceLock<(usize, rustc_hash::FxHashMap<String, u32>)>);

    impl Clone for PropertyIndex {
        fn clone(&self) -> Self {
            Self::default()
        }
    }

    impl std::fmt::Debug for PropertyIndex {
        fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
            f.write_str("PropertyIndex")
        }
    }

    impl PropertyIndex {
        /// The value of `name` in `properties` (the first one of that name).
        pub fn find<'p>(
            &self,
            properties: &'p [(String, PropertyValue)],
            name: &str,
        ) -> Option<&'p PropertyValue> {
            let (len, map) = self.0.get_or_init(|| {
                let mut map = rustc_hash::FxHashMap::default();
                for (i, (n, _)) in properties.iter().enumerate() {
                    map.entry(n.clone()).or_insert(i as u32);
                }
                (properties.len(), map)
            });
            if *len == properties.len() {
                return match map.get(name) {
                    Some(&i) => match properties.get(i as usize) {
                        Some((n, v)) if n == name => Some(v),
                        _ => properties.iter().find(|(n, _)| n == name).map(|(_, v)| v),
                    },
                    None => None,
                };
            }
            properties.iter().find(|(n, _)| n == name).map(|(_, v)| v)
        }
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
                    index: Default::default(),
                }
            }
        }

        pub fn get(&self, name: &str) -> Option<&PropertyValue> {
            self.index.find(&self.properties, name)
        }
    }
}

/// One explicit shader warm-up request, shared by immediate and queued startup execution.
#[derive(Clone, Debug, Default, serde::Deserialize)]
pub struct ShaderWarmupRequest {
    #[serde(default)]
    pub stats: bool,
    #[serde(default)]
    pub shader: String,
    #[serde(default)]
    pub passes: Vec<usize>,
    #[serde(default)]
    pub keywords: Vec<String>,
}

/// What executes the protocol. The windowed app installs a GPU renderer; headless runs keep the
/// default, which only records what it was given.
pub trait RenderBackend: Send + Sync {
    fn upload_mesh(&self, handle: u64, mesh: owned::Mesh);
    fn release_mesh(&self, handle: u64);
    fn upload_texture(&self, handle: u64, texture: owned::Texture);
    /// Sampler metadata delivered separately from the ABI-stable texture packet.
    fn texture_mip_bias(&self, _handle: u64, _bias: f32) {}
    fn texture_sampler(&self, _handle: u64, _properties: [i32; 5]) {}
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
    /// A rectangle of a mip level of a texture's layer (an array slice, a cube face, a volume's
    /// depth slice), as [`Backend::read_pixels`] gives it (NativeApi 15).
    fn read_texture(
        &self,
        _texture: u64,
        _mip: u32,
        _layer: u32,
        _rect: [i32; 4],
        _float: bool,
    ) -> Option<Vec<u8>> {
        None
    }
    /// Bytes of a buffer after every command submitted so far.
    fn read_buffer(&self, _buffer: u64, _offset: u64, _size: u64) -> Option<Vec<u8>> {
        None
    }
    /// Insert a marker after submitted GPU work, or poll the completed marker sequence.
    fn graphics_fence(&self, _insert: bool) -> Result<u64, String> {
        Err("no renderer".into())
    }
    /// A shader's SubShader for the active render pipeline (after its fallbacks) as JSON:
    /// `{ shader, tags, passes: [{ name, tags }] }`.
    fn shader_passes(&self, _shader: &str) -> Result<String, String> {
        Err("no renderer".into())
    }
    /// Prepare shader programs and pipelines, or query their prepared counts (JSON).
    fn shader_warmup(&self, _request: ShaderWarmupRequest) -> Result<String, String> {
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
        assert_eq!(size_of::<MaterialProperty>(), 72);
        assert_eq!(size_of::<MeshPacket>(), 136);
        assert_eq!(offset_of!(MeshPacket, uv), 40);
    }

    /// The normalized 16-bit, 10-10-10-2, integer and signed normalized 8-bit codes extend the
    /// table by value: the packet keeps its layout, and codes past the table are rejected.
    #[test]
    fn texture_format_codes() {
        use texture_format::*;
        assert_eq!(
            (RG16_UNORM, RGBA16_UNORM, RGB10A2_UNORM, R16_UNORM),
            (13, 14, 15, 16)
        );
        assert_eq!((R8_SNORM, RG8_SNORM, RGBA8_SNORM), (35, 36, 37));
        for code in 0..=RGBA8_SNORM {
            assert_eq!(validate(code), Ok(code));
        }
        // The integer codes follow the normalized ones, without upload data.
        assert_eq!((integer(1, 8, false), integer(4, 32, true)), (17, 34));
        assert!((INTEGER_FIRST..=INTEGER_LAST).all(|c| !float_data(c)));
        for code in [-1, 38, 39, i32::MAX, i32::MIN] {
            assert!(validate(code).is_err(), "{code} is accepted");
        }
        for code in [
            RG16_UNORM,
            RGBA16_UNORM,
            RGB10A2_UNORM,
            R16_UNORM,
            R8_SNORM,
            RG8_SNORM,
            RGBA8_SNORM,
        ] {
            assert!(float_data(code), "{code} data is not four floats");
        }
        let floats: Vec<i32> = (0..=16).filter(|&c| float_data(c)).collect();
        assert_eq!(floats, [2, 6, 9, 10, 11, 12, 13, 14, 15, 16]);
        // The packet itself does not change (NativeApi layout).
        assert_eq!(offset_of!(TexturePacket, format), 8);
        assert_eq!(offset_of!(TexturePacket, wrap_w), 64);
        assert_eq!(size_of::<TexturePacket>(), 72);
    }
}
