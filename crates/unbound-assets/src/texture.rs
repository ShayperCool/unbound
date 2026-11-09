//! The texture importer: decodes an image and applies `TextureImporter` settings the way Unity's
//! importer does, producing the mip chain the GPU texture is created from.
//!
//! Processing order (as in Unity): decode → alpha source → alpha-is-transparency dilation →
//! non-power-of-two scaling → max-size clamp → vertical flip → mipmaps.
//!
//! **Row order.** Unity stores textures bottom-up: row 0 of every mip level is the *bottom* row of
//! the image, so `GetPixel(0, 0)` and UV `(0, 0)` are the bottom-left corner, and sprite rects are
//! measured from the bottom-left. [`ImportedTexture`] keeps that order; a renderer that expects
//! top-down rows either flips the data or samples with `v' = 1 - v`.
//!
//! Block compression ([`bcn`]): on the Standalone target a 2D texture of power-of-two size is
//! stored in the format the Editor picks ([`ImportedTexture::blocks`]), and its pixels are what
//! those blocks decode to. Other targets' formats (ETC, ASTC), crunch and unmeasured cases keep
//! their pixels uncompressed. Cubemaps are imported as their six faces ([`ImportedTexture::layers`]);
//! Texture2DArray and Texture3D shapes as the cells of their flipbook (`flipbookColumns` x
//! `flipbookRows`), each slice processed like a 2D texture of the cell's size.
use crate::{
    Importer, asset_name,
    meta::{boolean, float, importer, int},
    sprite::{self, SpriteDef, SpriteSettings},
};
use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::{path::Path, sync::OnceLock};
use unbound_yaml::Value;

/// File extensions Unity's TextureImporter handles that this importer decodes.
pub const EXTENSIONS: &[&str] = &[
    "png", "jpg", "jpeg", "tga", "bmp", "gif", "psd", "tif", "tiff", "hdr", "exr",
];

/// The fileID of the Texture2D object of an imported texture.
pub const TEXTURE_FILE_ID: i64 = 2_800_000;

/// `textureType`. Pre-5.5 types without a modern equivalent (Advanced, Reflection, `-1`) read as
/// [`TextureType::Default`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum TextureType {
    Default,
    NormalMap,
    /// "Editor GUI and Legacy GUI".
    Gui,
    Sprite,
    Cursor,
    Cookie,
    Lightmap,
    SingleChannel,
    Shadowmask,
    DirectionalLightmap,
}

impl TextureType {
    pub fn from_meta(value: i64) -> Self {
        match value {
            1 => Self::NormalMap,
            2 => Self::Gui,
            4 => Self::Cookie,
            6 => Self::Lightmap,
            7 => Self::Cursor,
            8 => Self::Sprite,
            10 => Self::SingleChannel,
            11 => Self::Shadowmask,
            12 => Self::DirectionalLightmap,
            _ => Self::Default,
        }
    }

    /// Types whose sampler defaults to clamping and that are never rescaled to a power of two.
    fn is_ui(self) -> bool {
        matches!(self, Self::Sprite | Self::Gui | Self::Cursor)
    }
}

/// `textureShape`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum TextureShape {
    Texture2D,
    Cube,
    Texture2DArray,
    Texture3D,
}

/// `alphaUsage` (`TextureImporter.alphaSource`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum AlphaSource {
    None,
    FromInput,
    FromGrayScale,
}

/// `nPOTScale`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum NpotScale {
    None,
    ToNearest,
    ToLarger,
    ToSmaller,
}

/// `resizeAlgorithm` of a platform setting: the filter used to fit `maxTextureSize`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum ResizeAlgorithm {
    Mitchell,
    Bilinear,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum WrapMode {
    Repeat,
    Clamp,
    Mirror,
    MirrorOnce,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum FilterMode {
    Point,
    Bilinear,
    Trilinear,
}

/// The pixel format of the imported data.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum TextureFormat {
    /// 8-bit RGBA; colour channels are sRGB-encoded when [`ImportedTexture::srgb`].
    Rgba8,
    /// 32-bit float RGBA, little-endian (HDR/EXR sources); always linear.
    Rgba32F,
}

impl TextureFormat {
    pub fn bytes_per_pixel(self) -> usize {
        match self {
            Self::Rgba8 => 4,
            Self::Rgba32F => 16,
        }
    }
}

/// The `TextureImporter` settings that affect the imported data, read from a `.meta`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TextureSettings {
    pub texture_type: TextureType,
    pub shape: TextureShape,
    /// For [`TextureType::SingleChannel`]: 0 alpha (Unity makes an Alpha8 texture), 1 red (R8,
    /// or BC4 compressed). The imported data stays RGBA ([`ImportedTexture::reported_format`]
    /// names the format, [`ImportedTexture::gpu_channel`] what the GPU samples).
    pub single_channel_component: i32,
    /// Colour data is sRGB (`sRGBTexture`, formerly `!linearTexture`). Normal maps never are.
    pub srgb: bool,
    /// Set by the importer in a gamma-space project: HDR colour is stored gamma-encoded.
    #[serde(default)]
    pub gamma_hdr: bool,
    /// Set by the importer for a build target whose normal maps are encoded XYZ (what
    /// `UNITY_NO_DXT5nm` shaders read) instead of DXT5nm (1, y, y, x).
    #[serde(default)]
    pub normal_maps_xyz: bool,
    pub alpha_source: AlphaSource,
    pub alpha_is_transparency: bool,
    /// The effective maximum size for the build target.
    pub max_size: u32,
    pub resize_algorithm: ResizeAlgorithm,
    /// Effective NPOT scaling: always `None` for sprites and GUI textures.
    pub npot_scale: NpotScale,
    pub mipmaps: bool,
    /// `mipMapMode`: 0 box, 1 Kaiser.
    pub mip_filter: i32,
    /// `borderMipMap`.
    #[serde(default)]
    pub border_mipmap: bool,
    /// `fadeOut`: the mip levels where fading to gray starts and ends.
    #[serde(default)]
    pub mip_fade: Option<(i32, i32)>,
    /// `mipMapsPreserveCoverage`: the `alphaTestReferenceValue` whose coverage the mips keep.
    #[serde(default)]
    pub preserve_coverage: Option<f32>,
    /// A normal map made from the image's height ("Create from Grayscale"): `heightScale` and
    /// `normalMapFilter` (0 standard, 1 Sobel).
    #[serde(default)]
    pub normal_from_height: Option<(f32, i32)>,
    /// `flipGreenChannel` of a normal map.
    #[serde(default)]
    pub flip_green: bool,
    /// Whether the build target's explicit format stores alpha (`None`: automatic). With an
    /// alpha-less "Alpha Source: None", only an automatic format drops the input alpha.
    #[serde(default)]
    pub format_alpha: Option<bool>,
    pub is_readable: bool,
    pub wrap_u: WrapMode,
    pub wrap_v: WrapMode,
    pub wrap_w: WrapMode,
    pub filter: FilterMode,
    pub aniso: i32,
    pub mip_bias: f32,
    pub sprite: SpriteSettings,
    /// `generateCubemap` (a Cube shape's layout): 6 auto, 5 full cubemap, 2 cylindrical, 1 sphere.
    pub cubemap_layout: i32,
    /// `cubemapConvolution`: 0 none, 1 specular (glossy reflection), 2 diffuse.
    pub cubemap_convolution: i32,
    /// `flipbookColumns`, `flipbookRows`: the cells of a Texture2DArray or Texture3D shape.
    #[serde(default = "one_by_one")]
    pub flipbook: (u32, u32),
    /// The build target keeps the pixels as they are: an uncompressed format (RGBA32, ...), or
    /// compression off. Such a texture is never block-compressed.
    #[serde(default)]
    pub uncompressed: bool,
    /// The build target's explicit TextureImporterFormat (`None`: automatic).
    #[serde(default)]
    pub explicit_format: Option<i64>,
    /// The build target's `textureCompression` for an automatic format: 0 none, 1 normal, 2 high,
    /// 3 low quality.
    #[serde(default)]
    pub compression: i32,
    /// `crunchedCompression` of the build target.
    #[serde(default)]
    pub crunched: bool,
    /// `pSDRemoveMatte`: take Photoshop's white matte out of a flattened image with transparency.
    #[serde(default)]
    pub remove_matte: bool,
}

fn one_by_one() -> (u32, u32) {
    (1, 1)
}

impl Default for TextureSettings {
    fn default() -> Self {
        Self::read(&Value::Null, "Standalone")
    }
}

impl TextureSettings {
    /// Reads the settings of a `.meta` (any layout from Unity 5.x on), with Unity's defaults for
    /// missing fields. `build_target` selects the platform override (`Standalone`, `Android`,
    /// `iPhone`, ...) applied when that platform is `overridden`.
    pub fn read(meta: &Value, build_target: &str) -> Self {
        let imp = importer(meta, "TextureImporter");
        let texture_type = TextureType::from_meta(int(imp, "textureType").unwrap_or(0));
        let shape = match int(imp, "textureShape").unwrap_or(1) {
            2 => TextureShape::Cube,
            4 => TextureShape::Texture2DArray,
            8 => TextureShape::Texture3D,
            _ => TextureShape::Texture2D,
        };
        let srgb = match boolean(imp, "mipmaps.sRGBTexture") {
            Some(srgb) => srgb,
            None => !boolean(imp, "mipmaps.linearTexture").unwrap_or(false),
        } && texture_type != TextureType::NormalMap
            // A cookie is one (alpha) channel, which no sRGB decode applies to; a Single Channel
            // texture of an automatic format stays linear even when marked sRGB (R8_UNorm in the
            // Unity 6000.0.71f1 Editor, tests/projects/urp texture-samples.jsonl SingleRedSrgb;
            // explicit formats are unmeasured and keep the setting).
            && texture_type != TextureType::Cookie
            && !(texture_type == TextureType::SingleChannel
                && platform_format(imp, build_target).is_none());
        let alpha_source = match int(imp, "alphaUsage") {
            Some(0) => AlphaSource::None,
            Some(2) => AlphaSource::FromGrayScale,
            Some(_) => AlphaSource::FromInput,
            None if boolean(imp, "grayScaleToAlpha") == Some(true) => AlphaSource::FromGrayScale,
            None => AlphaSource::FromInput,
        };
        let npot_scale = if texture_type.is_ui() {
            NpotScale::None
        } else {
            match int(imp, "nPOTScale").unwrap_or(1) {
                0 => NpotScale::None,
                2 => NpotScale::ToLarger,
                3 => NpotScale::ToSmaller,
                _ => NpotScale::ToNearest,
            }
        };
        let (max_size, resize_algorithm) = platform_max_size(imp, build_target);
        let uncompressed = platform_uncompressed(imp, build_target);
        let entry = platform_entry(imp, build_target);
        let compression = entry
            .and_then(|e| int(e, "textureCompression"))
            .or_else(|| int(imp, "textureCompression"))
            .unwrap_or(1) as i32;
        let crunched = entry
            .and_then(|e| boolean(e, "crunchedCompression"))
            .or_else(|| boolean(imp, "crunchedCompression"))
            .unwrap_or(false);
        let format_alpha = platform_format(imp, build_target).map(format_has_alpha);
        let normal_map = texture_type == TextureType::NormalMap;

        // Sampler state. `-1` (and `-100` for the mip bias) mean "the type's default".
        let wrap = |key: &str| -> WrapMode {
            let value = int(imp, &format!("textureSettings.{key}"))
                .or_else(|| int(imp, "textureSettings.wrapMode"))
                .unwrap_or(-1);
            match value {
                0 => WrapMode::Repeat,
                1 => WrapMode::Clamp,
                2 => WrapMode::Mirror,
                3 => WrapMode::MirrorOnce,
                _ if texture_type.is_ui() => WrapMode::Clamp,
                _ => WrapMode::Repeat,
            }
        };
        // A Cube shape clamps on every axis, whatever its wrap settings say (a Repeat meta
        // imports as Clamp, Clamp, Clamp in the 6000.0 Editor: tests/projects/texturearrays).
        let wrap = |key: &str| {
            if shape == TextureShape::Cube {
                WrapMode::Clamp
            } else {
                wrap(key)
            }
        };
        let filter = match int(imp, "textureSettings.filterMode").unwrap_or(-1) {
            0 => FilterMode::Point,
            2 => FilterMode::Trilinear,
            _ => FilterMode::Bilinear,
        };
        let aniso = match int(imp, "textureSettings.aniso").unwrap_or(-1) {
            a if a < 0 => 1,
            a => a.min(16) as i32,
        };
        let mip_bias = match float(imp, "textureSettings.mipBias") {
            Some(b) if b != -1.0 && b != -100.0 => b,
            _ => 0.0,
        };

        TextureSettings {
            texture_type,
            shape,
            single_channel_component: int(imp, "singleChannelComponent").unwrap_or(0) as i32,
            srgb,
            gamma_hdr: false,
            normal_maps_xyz: false,
            alpha_source,
            alpha_is_transparency: boolean(imp, "alphaIsTransparency").unwrap_or(false),
            max_size,
            resize_algorithm,
            npot_scale,
            mipmaps: boolean(imp, "mipmaps.enableMipMap").unwrap_or(true),
            mip_filter: int(imp, "mipmaps.mipMapMode").unwrap_or(0) as i32,
            border_mipmap: boolean(imp, "mipmaps.borderMipMap").unwrap_or(false),
            mip_fade: boolean(imp, "mipmaps.fadeOut").unwrap_or(false).then(|| {
                (
                    int(imp, "mipmaps.mipMapFadeDistanceStart").unwrap_or(1) as i32,
                    int(imp, "mipmaps.mipMapFadeDistanceEnd").unwrap_or(3) as i32,
                )
            }),
            preserve_coverage: boolean(imp, "mipmaps.mipMapsPreserveCoverage")
                .unwrap_or(false)
                .then(|| float(imp, "mipmaps.alphaTestReferenceValue").unwrap_or(0.5)),
            normal_from_height: (normal_map
                && boolean(imp, "bumpmap.convertToNormalMap").unwrap_or(false))
            .then(|| {
                (
                    float(imp, "bumpmap.heightScale").unwrap_or(0.25),
                    int(imp, "bumpmap.normalMapFilter").unwrap_or(0) as i32,
                )
            }),
            flip_green: normal_map && boolean(imp, "bumpmap.flipGreenChannel").unwrap_or(false),
            format_alpha,
            is_readable: boolean(imp, "isReadable").unwrap_or(false),
            wrap_u: wrap("wrapU"),
            wrap_v: wrap("wrapV"),
            wrap_w: wrap("wrapW"),
            filter,
            aniso,
            mip_bias,
            sprite: SpriteSettings::read(imp, texture_type == TextureType::Sprite),
            cubemap_layout: int(imp, "generateCubemap").unwrap_or(6) as i32,
            cubemap_convolution: int(imp, "cubemapConvolution").unwrap_or(0) as i32,
            flipbook: (
                int(imp, "flipbookColumns").unwrap_or(1).max(1) as u32,
                int(imp, "flipbookRows").unwrap_or(1).max(1) as u32,
            ),
            uncompressed,
            explicit_format: platform_format(imp, build_target),
            compression,
            crunched,
            remove_matte: boolean(imp, "pSDRemoveMatte").unwrap_or(false),
        }
    }

    /// A stable hash of the settings, for callers that key derived data on them.
    pub fn hash(&self) -> u64 {
        let bytes = postcard::to_allocvec(self).expect("settings serialize");
        let hash = blake3::hash(&bytes);
        u64::from_le_bytes(hash.as_bytes()[..8].try_into().unwrap())
    }
}

/// The max size and resize filter for `build_target`: the target's entry when overridden, else the
/// `DefaultTexturePlatform` entry, else the top-level `maxTextureSize` (pre-5.5 metas).
///
/// Modern Unity keeps the top-level `maxTextureSize` only as a legacy mirror that is not updated
/// when the default platform entry changes (most metas of the test project have 2048 there and
/// 4096 in the default entry), so it is not combined with the platform value.
/// The platform settings that apply to a build target: its own when overridden, else the
/// default platform's.
fn platform_entry<'a>(imp: &'a Value, build_target: &str) -> Option<&'a Value> {
    let entries = imp
        .get("platformSettings")
        .or_else(|| imp.get("buildTargetSettings"))
        .and_then(Value::as_seq)
        .unwrap_or_default();
    let entry = |target: &str, require_override: bool| {
        entries.iter().find(|e| {
            e.get("buildTarget").and_then(Value::as_str) == Some(target)
                // `buildTargetSettings` (pre-5.5) lists only overridden targets.
                && (!require_override || boolean(e, "overridden").unwrap_or(true))
        })
    };
    entry(build_target, true).or_else(|| entry("DefaultTexturePlatform", false))
}

fn platform_max_size(imp: &Value, build_target: &str) -> (u32, ResizeAlgorithm) {
    let global = int(imp, "maxTextureSize").unwrap_or(2048);
    let chosen = platform_entry(imp, build_target);
    let max = chosen
        .and_then(|e| int(e, "maxTextureSize"))
        .unwrap_or(global)
        .clamp(32, 16384) as u32;
    let algorithm = match chosen.and_then(|e| int(e, "resizeAlgorithm")) {
        Some(1) => ResizeAlgorithm::Bilinear,
        _ => ResizeAlgorithm::Mitchell,
    };
    (max, algorithm)
}

/// The build target's explicit TextureImporterFormat; `None` for `Automatic`.
pub fn platform_format(imp: &Value, build_target: &str) -> Option<i64> {
    platform_entry(imp, build_target)
        .and_then(|e| int(e, "textureFormat"))
        .or_else(|| int(imp, "textureFormat"))
        .filter(|&f| f >= 0)
}

/// Whether a TextureImporterFormat has an alpha channel.
fn format_has_alpha(format: i64) -> bool {
    // Alpha8, ARGB16, RGBA32, ARGB32, DXT5, RGBA16, RGBAHalf, RGBAFloat, BC7, DXT5 crunched,
    // PVRTC RGBA, ETC2 with alpha, ASTC (LDR and HDR), ETC2 RGBA crunched, RGBA64.
    matches!(
        format,
        1 | 2 | 4 | 5 | 12 | 13 | 17 | 20 | 25 | 29 | 31 | 33 | 46 | 47 | 48..=53 | 65..=71 | 74
    )
}

/// Whether the build target stores the texture without loss: a TextureImporterFormat that is no
/// block compression (Alpha8, RGB24, RGBA32, RGBAHalf, R8, ...), or `Automatic` with compression
/// None.
fn platform_uncompressed(imp: &Value, build_target: &str) -> bool {
    let chosen = platform_entry(imp, build_target);
    if let Some(format) = platform_format(imp, build_target) {
        // DXT, BC4-7, PVRTC, ETC/EAC, ASTC (and their crunched forms).
        return !matches!(format, 10 | 12 | 24..=61 | 64..=71);
    }
    chosen
        .and_then(|e| int(e, "textureCompression"))
        .or_else(|| int(imp, "textureCompression"))
        .unwrap_or(1)
        == 0
}

/// One mip level, rows bottom-up.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct MipLevel {
    pub width: u32,
    pub height: u32,
    #[serde(with = "serde_bytes")]
    pub data: Vec<u8>,
}

/// An imported texture: its mip chain, sampler state and the sprites defined on it.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ImportedTexture {
    /// The Texture2D's name: the file name without extension.
    pub name: String,
    /// Size of the imported texture (mip 0).
    pub width: u32,
    pub height: u32,
    /// Size of the source image, before NPOT scaling and the max-size clamp. Sprite rects in the
    /// `.meta` are in this space.
    pub original_width: u32,
    pub original_height: u32,
    pub format: TextureFormat,
    /// Sample with an sRGB view (only meaningful in a linear-colour-space project).
    pub srgb: bool,
    /// Mip 0 first; a single level when mipmaps are disabled. Rows are bottom-up (see the module
    /// documentation). A cubemap holds each face's chain in turn (+X, -X, +Y, -Y, +Z, -Z), a
    /// Texture2DArray each slice's chain in turn, a Texture3D one level after another, each level
    /// all its depth slices (which halve with the level, at least 1).
    pub mips: Vec<MipLevel>,
    /// Images: 6 for a cubemap, the slices of a Texture2DArray, the depth of a Texture3D, else 1.
    pub layers: u32,
    pub wrap_u: WrapMode,
    pub wrap_v: WrapMode,
    pub wrap_w: WrapMode,
    pub filter: FilterMode,
    pub aniso: i32,
    pub mip_bias: f32,
    /// `Texture2D.isReadable`: CPU access (`GetPixels`) is allowed.
    pub is_readable: bool,
    /// Some pixel is not fully opaque.
    pub has_alpha: bool,
    /// Sprite sub-assets (empty unless the texture type is Sprite).
    pub sprites: Vec<SpriteDef>,
    pub settings: TextureSettings,
    /// [`TextureSettings::hash`].
    pub import_settings_hash: u64,
    /// The texture as the build target stores it, block-compressed ([`storage`]); then
    /// [`ImportedTexture::mips`] holds what the blocks decode to (BC6H: the float pixels before
    /// compression).
    pub blocks: Option<Blocks>,
    /// The build target stores the pixels uncompressed ([`Storage::Uncompressed`]).
    #[serde(default)]
    pub uncompressed: bool,
    /// Why the texture is not stored the way the build target stores it (a compressed format
    /// Unbound does not produce, [`Storage::Unsupported`]; blocks of Unbound's own encoder), for
    /// the warning given when it is loaded.
    #[serde(default)]
    pub approximation: Option<String>,
    /// The mips block-compressed to BC7 for the GPU alone (4x smaller than RGBA8), where the
    /// build target compresses in a format Unbound does not produce (ETC, ASTC, crunch,
    /// unmeasured cases) and the GPU samples BC7; a 2D RGBA8 texture of a size divisible by 4.
    /// Scripts see the uncompressed [`ImportedTexture::mips`].
    pub bc7: Option<Vec<serde_bytes::ByteBuf>>,
}

/// An import's block-compressed mip chain.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Blocks {
    pub format: BlockFormat,
    /// Mip 0 first, each level whole 4x4 blocks.
    pub mips: Vec<serde_bytes::ByteBuf>,
}

/// How the build target stores a texture's data.
#[derive(Debug, Clone, PartialEq)]
pub enum Storage {
    /// Uncompressed: its pixels as they are.
    Uncompressed,
    /// Block-compressed in this format.
    Blocks(BlockFormat),
    /// A compressed format Unbound does not produce (the reason): the pixels stay uncompressed.
    Unsupported(String),
}

/// How the Standalone target of the Unity 6000.0.71f1 Editor stores an import
/// (tests/projects/texturecompression): only 2D textures whose sides are multiples of 4 are
/// compressed, and with mipmaps only when they are also powers of two (20x12 and 30x18 with mips
/// and 2x2 stay uncompressed, automatic or an explicit DXT5; 64x16, and 20x12 without mips, are
/// compressed). An explicit
/// DXT1, DXT5, BC4, BC5, BC6H or BC7 is taken as it is. An automatic format with compression
/// on is BC6H for HDR data, BC4 for a Single Channel texture's red (its alpha stays Alpha8),
/// BC7 at High Quality and otherwise DXT1, or DXT5 when a texel is not opaque (a normal map:
/// DXT5 at Normal and Low Quality, BC7 at High; Sprite and GUI textures as Default ones).
/// Compressor quality does not change the DXT and BC4 blocks. Other targets, crunch, cubemaps,
/// arrays, volumes and the other texture types are unmeasured.
pub fn storage(texture: &ImportedTexture, build_target: &str) -> Storage {
    let s = &texture.settings;
    let unsupported = |why: &str| Storage::Unsupported(why.to_string());
    let hdr = texture.format == TextureFormat::Rgba32F;
    if s.uncompressed && s.explicit_format.is_none() {
        return Storage::Uncompressed;
    }
    let format = match s.explicit_format {
        Some(10) => BlockFormat::Bc1,
        Some(12) => BlockFormat::Bc3,
        Some(26) => BlockFormat::Bc4,
        Some(27) => BlockFormat::Bc5,
        Some(24) => BlockFormat::Bc6h,
        Some(25) => BlockFormat::Bc7,
        Some(28 | 29 | 64 | 65) => return unsupported("crunched formats"),
        Some(_) if s.uncompressed => return Storage::Uncompressed,
        Some(f) => return unsupported(&format!("TextureImporterFormat {f}")),
        None if s.crunched => return unsupported("crunched formats"),
        None if hdr => BlockFormat::Bc6h,
        None => match s.texture_type {
            TextureType::SingleChannel if s.single_channel_component == 0 => {
                return Storage::Uncompressed;
            }
            TextureType::SingleChannel => BlockFormat::Bc4,
            TextureType::Default
            | TextureType::NormalMap
            | TextureType::Sprite
            | TextureType::Gui => {
                if s.compression == 2 {
                    BlockFormat::Bc7
                } else if texture.has_alpha || s.texture_type == TextureType::NormalMap {
                    BlockFormat::Bc3
                } else {
                    BlockFormat::Bc1
                }
            }
            other => return unsupported(&format!("automatic compression of {other:?} textures")),
        },
    };
    if build_target != "Standalone" {
        return unsupported(&format!("the {build_target} target's compressed formats"));
    }
    if s.shape != TextureShape::Texture2D || texture.layers != 1 {
        return unsupported(&format!("compressed {:?} textures", s.shape));
    }
    if (format == BlockFormat::Bc6h) != hdr {
        return unsupported(&format!("{format:?} from {:?} data", texture.format));
    }
    let mipmapped = texture.mip_count() > 1;
    let compressible = |side: u32| side % 4 == 0 && (side.is_power_of_two() || !mipmapped);
    if !compressible(texture.width) || !compressible(texture.height) {
        return Storage::Uncompressed;
    }
    Storage::Blocks(format)
}

/// Compresses `texture` to `format` and replaces its pixels by what the blocks decode to (BC6H
/// keeps its float colour, Unbound has no float decoding of the blocks, and reads alpha 1).
fn compress_blocks(texture: &mut ImportedTexture, format: BlockFormat) {
    let mips: Vec<serde_bytes::ByteBuf> = texture
        .mips
        .iter()
        .map(|m| {
            serde_bytes::ByteBuf::from(if format == BlockFormat::Bc6h {
                bcn::compress_level_f32(&m.data, m.width, m.height)
            } else {
                bcn::compress_level(format, &m.data, m.width, m.height)
            })
        })
        .collect();
    if format == BlockFormat::Bc6h {
        for mip in &mut texture.mips {
            for texel in mip.data.chunks_exact_mut(16) {
                texel[12..].copy_from_slice(&1.0f32.to_le_bytes());
            }
        }
    } else {
        for (mip, blocks) in texture.mips.iter_mut().zip(&mips) {
            mip.data = bcn::decode_level(format, blocks, mip.width, mip.height);
        }
    }
    if format.approximates_editor() {
        texture.approximation = Some(format!(
            "{format:?} textures are compressed by Unbound's own encoder: their format and sizes \
             are the Editor's, their texels differ slightly from the Editor's blocks"
        ));
    }
    texture.blocks = Some(Blocks { format, mips });
}

/// Unity's `TextureFormat` values an import reports ([`ImportedTexture::reported_format`]).
pub mod unity_format {
    pub const ALPHA8: i32 = 1;
    pub const RGB24: i32 = 3;
    pub const RGBA32: i32 = 4;
    pub const R8: i32 = 63;
}

/// The channel a one-channel GPU format carries ([`ImportedTexture::gpu_channel`]).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum GpuChannel {
    /// Alpha8: samples (0, 0, 0, a).
    Alpha,
    /// R8 or BC4: samples (r, 0, 0, 1).
    Red,
}

impl ImportedTexture {
    /// The Texture2D's fileID within the asset.
    pub const FILE_ID: i64 = TEXTURE_FILE_ID;

    /// The `format` the Editor reports for this import: its block format when block-compressed
    /// ([`storage`]); for an 8-bit import the target stores uncompressed with an automatic
    /// format (Unity 6000.0.71f1 Editor, tests/projects/urp texture-formats.jsonl, and
    /// tests/projects/texturecompression NpotOpaqueNormal, NpotNormal: a compressed setting on a
    /// size that is not compressed) a Cookie keeps its alpha alone (Alpha8), a Single Channel
    /// texture its channel (Alpha8 or R8), other textures RGB24 without transparent texels and
    /// RGBA32 with them. A Single Channel Alpha texture is Alpha8 compressed too
    /// (texture-samples.jsonl SingleAlphaCompressed). None where Unbound keeps its RGBA32 or float
    /// answer: explicit uncompressed formats, compressed formats Unbound does not produce, HDR,
    /// normal maps and lightmaps stored uncompressed (unmeasured).
    pub fn reported_format(&self) -> Option<i32> {
        if let Some(blocks) = &self.blocks {
            return Some(blocks.format.unity_format());
        }
        let s = &self.settings;
        if self.format != TextureFormat::Rgba8 || s.format_alpha.is_some() {
            return None;
        }
        if s.texture_type == TextureType::SingleChannel && s.single_channel_component == 0 {
            return Some(unity_format::ALPHA8);
        }
        if !self.uncompressed {
            return None;
        }
        match s.texture_type {
            TextureType::Cookie => Some(unity_format::ALPHA8),
            TextureType::SingleChannel => Some(unity_format::R8),
            TextureType::NormalMap
            | TextureType::Lightmap
            | TextureType::Shadowmask
            | TextureType::DirectionalLightmap => None,
            _ if self.has_alpha => Some(unity_format::RGBA32),
            _ => Some(unity_format::RGB24),
        }
    }

    /// The one channel the GPU samples of an 8-bit import that keeps a single channel, as the
    /// Unity 6000.0.71f1 Editor samples it (tests/projects/urp texture-samples.jsonl): a Single
    /// Channel texture its alpha (Alpha8, compressed or not) or its red (R8, or BC4 compressed),
    /// a Cookie its alpha where it imports as Alpha8 (uncompressed; compressed cookies are
    /// unmeasured and keep their alpha in every channel). None for other textures and explicit
    /// formats.
    pub fn gpu_channel(&self) -> Option<GpuChannel> {
        let s = &self.settings;
        if self.format != TextureFormat::Rgba8 || s.format_alpha.is_some() {
            return None;
        }
        match s.texture_type {
            TextureType::SingleChannel if s.single_channel_component == 0 => {
                Some(GpuChannel::Alpha)
            }
            TextureType::SingleChannel => Some(GpuChannel::Red),
            TextureType::Cookie if self.reported_format() == Some(unity_format::ALPHA8) => {
                Some(GpuChannel::Alpha)
            }
            _ => None,
        }
    }

    /// The sprite with `file_id`.
    pub fn sprite(&self, file_id: i64) -> Option<&SpriteDef> {
        self.sprites.iter().find(|s| s.file_id == file_id)
    }

    /// Unity's TextureDimension: 2 a 2D texture, 3 a Texture3D, 4 a cubemap, 5 a Texture2DArray.
    pub fn dimension(&self) -> i32 {
        match self.settings.shape {
            TextureShape::Texture2D => 2,
            TextureShape::Texture3D => 3,
            TextureShape::Cube => 4,
            TextureShape::Texture2DArray => 5,
        }
    }

    /// Mip levels of each image (of the volume, for a Texture3D).
    pub fn mip_count(&self) -> usize {
        if self.settings.shape == TextureShape::Texture3D {
            self.mips.len()
        } else {
            self.mips.len() / self.layers.max(1) as usize
        }
    }

    /// The mip `mip` of image `layer` (a Texture3D's level holds every depth slice; its only
    /// image is 0).
    pub fn level(&self, layer: usize, mip: usize) -> Option<&MipLevel> {
        let count = self.mip_count();
        if mip >= count || layer >= self.layers.max(1) as usize {
            return None;
        }
        if self.settings.shape == TextureShape::Texture3D {
            return (layer == 0).then(|| &self.mips[mip]);
        }
        self.mips.get(layer * count + mip)
    }
}

/// The texture importer. Its option is the build target whose platform overrides apply.
#[derive(Debug, Clone)]
pub struct TextureImporter {
    pub build_target: String,
    /// Also compress textures whose target format Unbound does not produce to BC7 for the GPU
    /// ([`ImportedTexture::bc7`]).
    pub compress: bool,
    /// The project works in gamma color space: HDR colour textures are stored gamma-encoded.
    pub gamma: bool,
    /// The build target encodes normal maps XYZ (its shaders define `UNITY_NO_DXT5nm`), not
    /// DXT5nm.
    pub normal_maps_xyz: bool,
}

impl Default for TextureImporter {
    fn default() -> Self {
        Self {
            build_target: "Standalone".into(),
            compress: false,
            gamma: false,
            normal_maps_xyz: false,
        }
    }
}

impl Importer for TextureImporter {
    type Output = ImportedTexture;
    const KIND: &'static str = "textures";
    // 12: Texture2DArray and Texture3D shapes import their flipbook cells (were a 2D texture).
    // 13: single-cell arrays and volumes are no longer BC7-compressed.
    // 14: Cookie textures keep their alpha in every channel and are not sRGB.
    // 15: Lightmap and Shadowmask textures keep their input alpha under "Alpha Source: None".
    // 16: Single Channel textures are linear and, as one-channel imports, not BC7-compressed.
    // 17: a Single Channel texture of an explicit format keeps its sRGB setting.
    // 18: glossy-convolved cubemaps follow the Editor's specular convolution; byte faces stay
    //     gamma-encoded bytes.
    // 19: Standalone imports are block-compressed in the Editor's format (`blocks`); BC7 for the
    //     GPU only where the target's format is not produced.
    // 20: mipless textures of multiple-of-4 sizes are compressed; Cursor textures keep their
    //     pixels; the import records why it is not stored as the target stores it.
    // 21: Photoshop files are read by Unbound's own reader (16-bit channels round to bytes).
    // 22: Photoshop gray reads opaque, Remove Matte applies.
    const VERSION: u32 = 22;

    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedTexture> {
        let mut settings = TextureSettings::read(meta, &self.build_target);
        settings.gamma_hdr = self.gamma;
        settings.normal_maps_xyz = self.normal_maps_xyz;
        let mut texture = import_texture(source, bytes, meta, settings)?;
        match storage(&texture, &self.build_target) {
            Storage::Uncompressed => texture.uncompressed = true,
            Storage::Blocks(format) => compress_blocks(&mut texture, format),
            Storage::Unsupported(why) => {
                texture.approximation = Some(format!(
                    "{why} are not produced: such textures keep their pixels uncompressed (their \
                     format reads RGBA32 or RGB24)"
                ));
                if self.compress {
                    texture.bc7 = compress_bc7(&texture);
                }
            }
        }
        Ok(texture)
    }

    fn options_key(&self) -> String {
        // Resized textures depend on the Editor's job worker count, which follows the machine.
        format!(
            "{}{}{}{}+workers{}",
            self.build_target,
            if self.compress { "+bc7" } else { "" },
            if self.gamma { "+gamma" } else { "" },
            if self.normal_maps_xyz { "+xyz" } else { "" },
            resize::job_workers()
        )
    }
}

/// The mips of a 2D RGBA8 texture whose size is divisible by 4, compressed to BC7 (levels below 4
/// pixels padded with their edge, as the GPU's 4x4 blocks need); `None` for any other texture.
/// Arrays and volumes stay RGBA8 even with a single slice: the renderer binds them through
/// RGBA8 array and volume views. One-channel imports ([`ImportedTexture::gpu_channel`]) upload
/// that channel alone instead.
pub fn compress_bc7(texture: &ImportedTexture) -> Option<Vec<serde_bytes::ByteBuf>> {
    use rayon::prelude::*;
    if texture.format != TextureFormat::Rgba8
        || texture.settings.shape != TextureShape::Texture2D
        || texture.layers != 1
        || texture.width % 4 != 0
        || texture.height % 4 != 0
        || texture.gpu_channel().is_some()
    {
        return None;
    }
    let settings = if texture.has_alpha {
        intel_tex_2::bc7::alpha_fast_settings()
    } else {
        intel_tex_2::bc7::opaque_fast_settings()
    };
    Some(
        texture
            .mips
            .iter()
            .map(|mip| {
                let (w, h) = (mip.width.max(1) as usize, mip.height.max(1) as usize);
                let (pw, ph) = (w.next_multiple_of(4), h.next_multiple_of(4));
                let padded;
                let data: &[u8] = if (pw, ph) == (w, h) {
                    &mip.data
                } else {
                    let mut out = vec![0u8; pw * ph * 4];
                    for y in 0..ph {
                        for x in 0..pw {
                            let from = (y.min(h - 1) * w + x.min(w - 1)) * 4;
                            out[(y * pw + x) * 4..][..4].copy_from_slice(&mip.data[from..from + 4]);
                        }
                    }
                    padded = out;
                    &padded
                };
                // Strips of 16 block rows compress in parallel; their blocks follow each other.
                let strip = 64.min(ph);
                let blocks: Vec<Vec<u8>> = data
                    .par_chunks(pw * 4 * strip)
                    .map(|rows| {
                        let surface = intel_tex_2::RgbaSurface {
                            data: rows,
                            width: pw as u32,
                            height: (rows.len() / (pw * 4)) as u32,
                            stride: (pw * 4) as u32,
                        };
                        intel_tex_2::bc7::compress_blocks(&settings, &surface)
                    })
                    .collect();
                serde_bytes::ByteBuf::from(blocks.concat())
            })
            .collect(),
    )
}

/// Imports a texture with already-read `settings` (`meta` supplies the sprite sheet).
pub fn import_texture(
    source: &Path,
    bytes: &[u8],
    meta: &Value,
    settings: TextureSettings,
) -> Result<ImportedTexture> {
    let mut image = decode(source, bytes, settings.remove_matte).context("decoding")?;
    let (original_width, original_height) = (image.width, image.height);
    // HDR colour in a gamma project is gamma-encoded: a 2D texture's per level after resizing and
    // filtering (measured with tests/projects/textureimport), a cubemap's here before its faces
    // are cut (unmeasured order), a glossy-convolved cubemap's mips after the convolution
    // (import_cubemap; unmeasured).
    if settings.gamma_hdr
        && settings.srgb
        && image.format == TextureFormat::Rgba32F
        && settings.shape == TextureShape::Cube
        && !cube_convolves(&image, &settings)?
    {
        image.linear_to_gamma();
    }

    match settings.alpha_source {
        // Only an automatic format leaves the alpha out; an explicit one with alpha keeps it.
        // Baked lightmaps and shadowmasks keep theirs under "None" (the Editor's lightmap alpha
        // is 0 in unused texels, a shadowmask's unused fourth channel 0: Unity 6000.0.71f1,
        // tests/projects/urp texture-formats.jsonl).
        AlphaSource::None
            if settings.format_alpha != Some(true)
                && !matches!(
                    settings.texture_type,
                    TextureType::Lightmap | TextureType::Shadowmask
                ) =>
        {
            image.set_opaque()
        }
        AlphaSource::FromGrayScale => image.alpha_from_gray(),
        AlphaSource::None | AlphaSource::FromInput => {}
    }
    if settings.texture_type == TextureType::Cookie {
        image.alpha_to_all_channels();
    }
    // The Editor dilates whenever the texture's format stores alpha (an automatic one does
    // whenever the image has transparent texels to fill).
    let dilate = settings.alpha_is_transparency && settings.format_alpha != Some(false);
    if matches!(
        settings.shape,
        TextureShape::Texture2DArray | TextureShape::Texture3D
    ) {
        return import_flipbook(source, image, settings, dilate);
    }
    if dilate {
        image.dilate_transparent();
    }

    if settings.shape == TextureShape::Cube {
        return import_cubemap(source, image, settings, (original_width, original_height));
    }

    let (w, h) = npot_size(image.width, image.height, settings.npot_scale);
    let (w, h) = fit_max_size(w, h, settings.max_size);
    if (w, h) != (image.width, image.height) {
        image = image.resize(w, h, settings.resize_algorithm)?;
    }
    image.flip_rows();
    let format = image.format;
    let mips = mip_chain(image, &settings);
    let has_alpha = match format {
        TextureFormat::Rgba8 => mips[0].data.as_chunks::<4>().0.iter().any(|p| p[3] < 255),
        TextureFormat::Rgba32F => mips[0]
            .data
            .as_chunks::<16>()
            .0
            .iter()
            .any(|p| read_f32(&p[12..]) < 1.0),
    };

    let name = asset_name(source);
    let mut sprites = sprite::read_sprites(
        importer(meta, "TextureImporter"),
        &settings.sprite,
        &name,
        (original_width, original_height),
        (mips[0].width, mips[0].height),
    );
    if !sprites.is_empty() {
        let rgba = (format == TextureFormat::Rgba8).then_some(&mips[0].data[..]);
        if rgba.is_none() {
            tracing::warn!(
                "{}: the alpha of a floating-point texture is not traced: its sprites get no \
                 Tight mesh or default physics shape from it (Unbound traces 8-bit alpha only)",
                source.display()
            );
        }
        let polygon = settings.sprite.mode == sprite::SpriteMode::Polygon;
        for s in &mut sprites {
            sprite::build_geometry(s, mips[0].width, mips[0].height, rgba, polygon);
        }
    }
    Ok(ImportedTexture {
        name,
        width: mips[0].width,
        height: mips[0].height,
        original_width,
        original_height,
        format,
        srgb: settings.srgb,
        mips,
        wrap_u: settings.wrap_u,
        wrap_v: settings.wrap_v,
        wrap_w: settings.wrap_w,
        filter: settings.filter,
        aniso: settings.aniso,
        mip_bias: settings.mip_bias,
        is_readable: settings.is_readable,
        has_alpha,
        sprites,
        import_settings_hash: settings.hash(),
        settings,
        layers: 1,
        blocks: None,
        uncompressed: false,
        approximation: None,
        bc7: None,
    })
}

/// The imported mip chain of a 2D texture (just its top level without mipmaps).
fn mip_chain(image: Image, settings: &TextureSettings) -> Vec<MipLevel> {
    mip_chain_with_columns(image, settings, None)
}

/// [`mip_chain`] whose first border mip reads its edge columns from `first_columns` (bottom-up
/// texels of the image's size and format).
fn mip_chain_with_columns(
    image: Image,
    settings: &TextureSettings,
    first_columns: Option<&[u8]>,
) -> Vec<MipLevel> {
    let format = image.format;
    let height = settings
        .normal_from_height
        .filter(|_| format == TextureFormat::Rgba8)
        .map(|(scale, filter)| (scale, filter == 1));
    let levels = settings.mipmaps && (image.width > 1 || image.height > 1);
    // The Editor filters sRGB colour in linear light (only when there are mips to make) and
    // makes normals from float heights; other 8-bit textures keep their top level in bytes.
    let srgb = settings.srgb && format == TextureFormat::Rgba8 && levels && height.is_none();
    let top = if height.is_some() {
        mipmaps::Top::Unorm
    } else if srgb {
        mipmaps::Top::Srgb
    } else {
        mipmaps::Top::Bytes
    };
    let columns = first_columns
        .map(|data| mipmaps::Level::decode(image.width, image.height, data, format, top));
    let top = mipmaps::Level::decode(image.width, image.height, &image.data, format, top);
    let options = mipmaps::Options {
        filter: if settings.mip_filter == 1 {
            mipmaps::Filter::Kaiser
        } else {
            mipmaps::Filter::Box
        },
        border: settings.border_mipmap,
        coverage: settings.preserve_coverage,
    };
    let hdr_gamma = settings.gamma_hdr && settings.srgb && format == TextureFormat::Rgba32F;
    let encoding = mipmaps::Encoding {
        height,
        normal_map: settings.texture_type == TextureType::NormalMap,
        dxt5nm: !settings.normal_maps_xyz,
        flip_green: settings.flip_green,
        srgb: srgb || hdr_gamma,
        fade: settings.mip_fade,
    };
    let chain = if levels {
        mipmaps::chain_with_columns(top, &options, columns.as_ref())
    } else {
        vec![top]
    };
    chain
        .iter()
        .enumerate()
        .map(|(i, level)| MipLevel {
            width: level.width,
            height: level.height,
            data: level.encode(i, format, &encoding),
        })
        .collect()
}

/// A Texture2DArray or Texture3D: the image cut into `flipbookColumns` x `flipbookRows` cells of
/// floor(width / columns) x floor(height / rows) texels, the top-left cell first, then left to
/// right and top to bottom (measured with tests/projects/texturearrays). Each cell is processed
/// like a 2D texture of its size: alpha-is-transparency dilation, NPOT scaling, the max size and,
/// for an array, the 2D mip chain with every mip setting. A volume's mips are its own (see
/// [`volume_chain`]).
fn import_flipbook(
    source: &Path,
    mut image: Image,
    settings: TextureSettings,
    dilate: bool,
) -> Result<ImportedTexture> {
    let (columns, rows) = settings.flipbook;
    let (cell_w, cell_h) = (image.width / columns.max(1), image.height / rows.max(1));
    if cell_w == 0 || cell_h == 0 {
        bail!(
            "a {columns}x{rows} flipbook does not fit the {}x{} image",
            image.width,
            image.height
        );
    }
    if dilate {
        image.dilate_flipbook_prefix(cell_w, cell_h);
    }
    let mut cells = Vec::with_capacity((columns * rows) as usize);
    for row in 0..rows {
        for column in 0..columns {
            let mut cell = image.crop(column * cell_w, row * cell_h, cell_w, cell_h);
            let (w, h) = npot_size(cell_w, cell_h, settings.npot_scale);
            let (w, h) = fit_max_size(w, h, settings.max_size);
            if (w, h) != (cell_w, cell_h) {
                cell = cell.resize(w, h, settings.resize_algorithm)?;
            }
            // An array's HDR colour is gamma-encoded per level by its 2D chain; a volume's here.
            if settings.shape == TextureShape::Texture3D
                && settings.gamma_hdr
                && settings.srgb
                && cell.format == TextureFormat::Rgba32F
            {
                cell.linear_to_gamma();
            }
            cell.flip_rows();
            cells.push(cell);
        }
    }
    let format = image.format;
    let has_alpha = cells.iter().any(Image::has_alpha);
    let (width, height) = (cells[0].width, cells[0].height);
    let layers = cells.len() as u32;
    let mips = if settings.shape == TextureShape::Texture3D {
        if format == TextureFormat::Rgba32F && settings.mipmaps {
            tracing::warn!(
                "{}: the mips of a floating-point Texture3D are not measured; Unbound halves it \
                 like an 8-bit volume without the byte truncation",
                source.display()
            );
        }
        volume_chain(cells, settings.mipmaps)
    } else {
        let views = (settings.border_mipmap && settings.mipmaps)
            .then(|| flipbook_views(&cells, columns, rows));
        cells
            .into_iter()
            .enumerate()
            .flat_map(|(i, cell)| {
                mip_chain_with_columns(cell, &settings, views.as_ref().map(|v| &v[i][..]))
            })
            .collect()
    };
    Ok(ImportedTexture {
        name: asset_name(source),
        width,
        height,
        original_width: image.width,
        original_height: image.height,
        format,
        srgb: settings.srgb,
        mips,
        wrap_u: settings.wrap_u,
        wrap_v: settings.wrap_v,
        wrap_w: settings.wrap_w,
        filter: settings.filter,
        aniso: settings.aniso,
        mip_bias: settings.mip_bias,
        is_readable: settings.is_readable,
        has_alpha,
        sprites: Vec::new(),
        import_settings_hash: settings.hash(),
        settings,
        layers,
        blocks: None,
        uncompressed: false,
        approximation: None,
        bc7: None,
    })
}

/// What each slice's first border mip reads its edge columns from, as the 6000.0 Editor does
/// it: the whole flipbook image (bottom-up rows), read from the slice's bottom-left texel on as
/// if its rows were a cell wide, so a column steps by the cell's width instead of the image's.
/// Measured texel for texel at the first level of tests/projects/texturearrays's border case;
/// lower levels read their own slice. The cells here are the processed (possibly resized) ones
/// put back in their grid; with resized cells this is unmeasured. Texels past the image's end
/// (only the top row's cells can reach there) repeat its last texel.
fn flipbook_views(cells: &[Image], columns: u32, rows: u32) -> Vec<Vec<u8>> {
    let (cw, ch) = (cells[0].width as usize, cells[0].height as usize);
    let bpp = cells[0].format.bytes_per_pixel();
    let (columns, rows) = (columns as usize, rows as usize);
    let width = cw * columns;
    let mut whole = vec![0u8; width * ch * rows * bpp];
    for (i, cell) in cells.iter().enumerate() {
        // Slice i is column i % columns of row i / columns from the top; rows are bottom-up.
        let (cx, cy) = (i % columns, rows - 1 - i / columns);
        for y in 0..ch {
            let to = ((cy * ch + y) * width + cx * cw) * bpp;
            whole[to..to + cw * bpp].copy_from_slice(&cell.data[y * cw * bpp..(y + 1) * cw * bpp]);
        }
    }
    let texels = whole.len() / bpp;
    (0..cells.len())
        .map(|i| {
            let (cx, cy) = (i % columns, rows - 1 - i / columns);
            let origin = cy * ch * width + cx * cw;
            (0..cw * ch)
                .flat_map(|t| {
                    let at = (origin + t).min(texels - 1) * bpp;
                    whole[at..at + bpp].to_vec()
                })
                .collect()
        })
        .collect()
}

/// A Texture3D's mip chain from its (bottom-up) depth slices: each level is the one above halved
/// along x, then y, then z, every pass the truncating mean `(a + b) >> 1` of a pair of bytes (an
/// axis of size 1 is kept; an odd last texel is dropped). The colour space and the mip filter do
/// not change it. Measured exactly at every level of tests/projects/texturearrays's volumes.
fn volume_chain(slices: Vec<Image>, mipmaps: bool) -> Vec<MipLevel> {
    let (mut w, mut h, mut d) = (slices[0].width, slices[0].height, slices.len() as u32);
    let format = slices[0].format;
    let data: Vec<u8> = slices.into_iter().flat_map(|s| s.data).collect();
    let count = if mipmaps {
        32 - w.max(h).max(d).leading_zeros()
    } else {
        1
    };
    let mut levels = vec![MipLevel {
        width: w,
        height: h,
        data,
    }];
    for _ in 1..count {
        let mut level = levels.last().unwrap().data.clone();
        for axis in 0..3 {
            (level, w, h, d) = halve_axis(&level, (w, h, d), axis, format);
        }
        levels.push(MipLevel {
            width: w,
            height: h,
            data: level,
        });
    }
    levels
}

/// One pass of [`volume_chain`]: `data` (w x h x d texels) halved along `axis` (0 x, 1 y, 2 z).
fn halve_axis(
    data: &[u8],
    (w, h, d): (u32, u32, u32),
    axis: usize,
    format: TextureFormat,
) -> (Vec<u8>, u32, u32, u32) {
    let size = [w, h, d];
    if size[axis] == 1 {
        return (data.to_vec(), w, h, d);
    }
    let mut out_size = size;
    out_size[axis] = size[axis] / 2;
    let [ow, oh, od] = out_size;
    let bpp = format.bytes_per_pixel();
    let index = |x: u32, y: u32, z: u32| ((z * h + y) * w + x) as usize * bpp;
    let mut out = Vec::with_capacity((ow * oh * od) as usize * bpp);
    for z in 0..od {
        for y in 0..oh {
            for x in 0..ow {
                let mut first = [x, y, z];
                first[axis] *= 2;
                let mut second = first;
                second[axis] += 1;
                let a = &data[index(first[0], first[1], first[2])..][..bpp];
                let b = &data[index(second[0], second[1], second[2])..][..bpp];
                match format {
                    TextureFormat::Rgba8 => out.extend(
                        a.iter()
                            .zip(b)
                            .map(|(&a, &b)| ((a as u16 + b as u16) >> 1) as u8),
                    ),
                    TextureFormat::Rgba32F => {
                        for c in 0..4 {
                            let at = c * 4..c * 4 + 4;
                            let v = (read_f32(&a[at.clone()]) + read_f32(&b[at])) * 0.5;
                            out.extend_from_slice(&v.to_le_bytes());
                        }
                    }
                }
            }
        }
    }
    (out, ow, oh, od)
}

/// Whether a cubemap's mips are glossy-convolved ("Convolution Type: Specular").
fn convolves(settings: &TextureSettings) -> bool {
    settings.shape == TextureShape::Cube && settings.cubemap_convolution == 1 && settings.mipmaps
}

/// A cubemap image's face size as cut, its size after scaling, and its mip count.
fn cube_size(
    image: &Image,
    settings: &TextureSettings,
    layout: cubemap::Layout,
) -> (u32, u32, u32) {
    let face = cubemap::face_size(image.width, image.height, layout);
    let (size, _) = npot_size(face, face, settings.npot_scale);
    let (size, _) = fit_max_size(size, size, settings.max_size);
    let levels = if settings.mipmaps {
        32 - size.leading_zeros()
    } else {
        1
    };
    (face, size, levels)
}

/// Whether a cubemap image will be glossy-convolved (it has mips to convolve).
fn cube_convolves(image: &Image, settings: &TextureSettings) -> Result<bool> {
    if !convolves(settings) {
        return Ok(false);
    }
    let layout = cubemap::layout(image.width, image.height, settings.cubemap_layout)?;
    Ok(cube_size(image, settings, layout).2 > 1)
}

/// A cubemap: its faces cut from (or projected out of) the image, scaled as a texture of the
/// face size would be, each with its mips (glossy-convolved when `cubemapConvolution` says so).
fn import_cubemap(
    source: &Path,
    image: Image,
    settings: TextureSettings,
    original: (u32, u32),
) -> Result<ImportedTexture> {
    let layout = cubemap::layout(image.width, image.height, settings.cubemap_layout)?;
    let (face, size, levels) = cube_size(&image, &settings, layout);
    let convolve = convolves(&settings) && levels > 1;
    // A gamma project's convolved HDR cube is gamma-encoded after the convolution (the others
    // were before their faces were cut, import_texture).
    let gamma = settings.gamma_hdr && settings.srgb && image.format == TextureFormat::Rgba32F;
    let mut faces = cubemap::faces(&image, layout);
    if size != face {
        faces = faces
            .into_iter()
            .map(|f| f.resize(size, size, settings.resize_algorithm))
            .collect::<Result<_>>()?;
    }
    let has_alpha = faces.iter().any(Image::has_alpha);
    let srgb = settings.srgb;
    let chains: Vec<Vec<Image>> = if convolve {
        // Byte faces are convolved as linear light whatever their sRGB setting and stored
        // gamma-encoded again; float faces stay linear (measured: tests/projects/reflectionprobes
        // import.*).
        let bytes = faces[0].format == TextureFormat::Rgba8;
        let linear: Vec<Image> = faces.into_iter().map(Image::into_linear_float).collect();
        let mut chains = specular::convolve(&linear);
        if bytes || gamma {
            chains.iter_mut().flatten().for_each(Image::linear_to_gamma);
        }
        if bytes {
            chains = chains
                .into_iter()
                .map(|chain| chain.into_iter().map(Image::into_bytes).collect())
                .collect();
        }
        chains
    } else {
        faces
            .into_iter()
            .map(|f| {
                let face_srgb = srgb && f.format == TextureFormat::Rgba8 && levels > 1;
                let top = if face_srgb {
                    mipmaps::Top::Srgb
                } else {
                    mipmaps::Top::Bytes
                };
                let top = mipmaps::Level::decode(f.width, f.height, &f.data, f.format, top);
                let encoding = mipmaps::Encoding {
                    srgb: face_srgb,
                    ..Default::default()
                };
                let options = mipmaps::Options {
                    filter: mipmaps::Filter::Box,
                    border: false,
                    coverage: None,
                };
                let format = f.format;
                let mut chain = vec![f];
                for (i, level) in mipmaps::chain(top, &options)
                    .iter()
                    .enumerate()
                    .skip(1)
                    .take(levels as usize - 1)
                {
                    chain.push(Image {
                        width: level.width,
                        height: level.height,
                        format,
                        data: level.encode(i, format, &encoding),
                    });
                }
                chain
            })
            .collect()
    };
    let format = chains[0][0].format;
    let mips = chains
        .into_iter()
        .flatten()
        .map(|mut level| {
            level.flip_rows();
            MipLevel {
                width: level.width,
                height: level.height,
                data: level.data,
            }
        })
        .collect();
    Ok(ImportedTexture {
        name: asset_name(source),
        width: size,
        height: size,
        original_width: original.0,
        original_height: original.1,
        format,
        srgb: srgb && format == TextureFormat::Rgba8,
        mips,
        wrap_u: settings.wrap_u,
        wrap_v: settings.wrap_v,
        wrap_w: settings.wrap_w,
        filter: settings.filter,
        aniso: settings.aniso,
        mip_bias: settings.mip_bias,
        is_readable: settings.is_readable,
        has_alpha,
        sprites: Vec::new(),
        import_settings_hash: settings.hash(),
        settings,
        layers: 6,
        blocks: None,
        uncompressed: false,
        approximation: None,
        bc7: None,
    })
}

/// The size after non-power-of-two scaling. Power-of-two sizes are kept.
pub fn npot_size(width: u32, height: u32, mode: NpotScale) -> (u32, u32) {
    if mode == NpotScale::None || (width.is_power_of_two() && height.is_power_of_two()) {
        return (width, height);
    }
    let scale = |v: u32| match mode {
        NpotScale::None => v,
        NpotScale::ToLarger => v.next_power_of_two(),
        NpotScale::ToSmaller => prev_power_of_two(v),
        // Mathf.ClosestPowerOfTwo: ties go to the larger.
        NpotScale::ToNearest => {
            let (lo, hi) = (prev_power_of_two(v), v.next_power_of_two());
            if v - lo < hi - v { lo } else { hi }
        }
    };
    (scale(width), scale(height))
}

fn prev_power_of_two(v: u32) -> u32 {
    if v == 0 {
        1
    } else {
        1 << (31 - v.leading_zeros())
    }
}

/// The size after the max-size clamp: when the larger side exceeds `max`, the image is scaled so
/// that side is exactly `max`, keeping the aspect ratio (each side at least 1).
pub fn fit_max_size(width: u32, height: u32, max: u32) -> (u32, u32) {
    let largest = width.max(height);
    if largest <= max {
        return (width, height);
    }
    let scale = max as f64 / largest as f64;
    let fit = |v: u32| ((v as f64 * scale).round() as u32).clamp(1, max);
    (fit(width), fit(height))
}

/// A decoded image: RGBA rows, top-down until [`Image::flip_rows`].
#[derive(Clone)]
struct Image {
    width: u32,
    height: u32,
    format: TextureFormat,
    data: Vec<u8>,
}

/// Decodes an image file; `remove_matte` applies to Photoshop files ([`psd::decode`]).
fn decode(source: &Path, bytes: &[u8], remove_matte: bool) -> Result<Image> {
    let ext = source
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    if bytes.starts_with(b"8BPS") || (ext == "psd" && !bytes.is_empty()) {
        // Photoshop: Unity imports the flattened image.
        let (width, height, data) = psd::decode(bytes, remove_matte)?;
        return Ok(Image {
            width,
            height,
            format: TextureFormat::Rgba8,
            data,
        });
    }
    // The content decides (some projects have JPEGs named `.png`), the extension is the fallback.
    let format = match image::guess_format(bytes) {
        Ok(format) => format,
        Err(_) => match image::ImageFormat::from_extension(&ext) {
            Some(format) => format,
            None => bail!("unrecognized image format"),
        },
    };
    let image = match image::load_from_memory_with_format(bytes, format) {
        Ok(image) => image,
        // The image crate reads only RGB(A) EXR layers; Unity also imports single-channel and
        // luminance ones.
        Err(_) if format == image::ImageFormat::OpenExr => return decode_exr_channels(bytes),
        // Nor colour-mapped TGA.
        Err(e) if format == image::ImageFormat::Tga => {
            return decode_color_mapped_tga(bytes).ok_or(e).map_err(Into::into);
        }
        Err(e) => return Err(e.into()),
    };
    let (width, height) = (image.width(), image.height());
    Ok(match image {
        image::DynamicImage::ImageRgb32F(_) | image::DynamicImage::ImageRgba32F(_) => {
            let rgba = image.into_rgba32f();
            let mut data = Vec::with_capacity(rgba.len() * 4);
            for v in rgba.into_raw() {
                data.extend_from_slice(&v.to_le_bytes());
            }
            Image {
                width,
                height,
                format: TextureFormat::Rgba32F,
                data,
            }
        }
        _ => Image {
            width,
            height,
            format: TextureFormat::Rgba8,
            data: image.into_rgba8().into_raw(),
        },
    })
}

/// An EXR of any channel layout: R, G, B, A by name (`Y` for luminance, else the first channel
/// stands for red); missing colour channels are 0 (grey when only `Y`), missing alpha is 1.
fn decode_exr_channels(bytes: &[u8]) -> Result<Image> {
    use exr::prelude::{ReadChannels, ReadLayers};
    let image = exr::prelude::read()
        .no_deep_data()
        .largest_resolution_level()
        .all_channels()
        .first_valid_layer()
        .all_attributes()
        .from_buffered(std::io::Cursor::new(bytes))?;
    let layer = &image.layer_data;
    let (width, height) = (layer.size.width(), layer.size.height());
    let channels = &layer.channel_data.list;
    let find = |name: &str| channels.iter().find(|c| c.name.eq_case_insensitive(name));
    let luma = find("Y");
    let red = find("R").or(luma).or(channels.first());
    let (green, blue) = match (find("G"), find("B")) {
        (None, None) if luma.is_some() => (luma, luma),
        found => found,
    };
    let alpha = find("A");
    let mut data = Vec::with_capacity(width * height * 16);
    for i in 0..width * height {
        let sample = |c: Option<&exr::prelude::AnyChannel<exr::prelude::FlatSamples>>, default| {
            c.map_or(default, |c| c.sample_data.value_by_flat_index(i).to_f32())
        };
        for v in [
            sample(red, 0.0),
            sample(green, 0.0),
            sample(blue, 0.0),
            sample(alpha, 1.0),
        ] {
            data.extend_from_slice(&v.to_le_bytes());
        }
    }
    Ok(Image {
        width: width as u32,
        height: height as u32,
        format: TextureFormat::Rgba32F,
        data,
    })
}

/// A colour-mapped TGA (image type 1, or 9 when RLE-compressed) with 8- or 16-bit indices into a
/// 15/16/24/32-bit palette. `None` when the file is not one (or is truncated).
fn decode_color_mapped_tga(bytes: &[u8]) -> Option<Image> {
    let header = bytes.get(..18)?;
    let u16_at = |i: usize| u16::from_le_bytes([header[i], header[i + 1]]) as usize;
    let (id_len, map_type, image_type) = (header[0] as usize, header[1], header[2]);
    let (map_first, map_len, entry_bits) = (u16_at(3), u16_at(5), header[7] as usize);
    let (width, height, index_bits, descriptor) = (u16_at(12), u16_at(14), header[16], header[17]);
    if map_type != 1
        || !matches!(image_type, 1 | 9)
        || !matches!(index_bits, 8 | 16)
        || width == 0
        || height == 0
    {
        return None;
    }
    let entry_bytes = entry_bits.div_ceil(8);
    let palette_bytes = bytes.get(18 + id_len..18 + id_len + map_len * entry_bytes)?;
    let palette: Vec<[u8; 4]> = palette_bytes
        .chunks_exact(entry_bytes)
        .map(|e| match entry_bytes {
            4 => [e[2], e[1], e[0], e[3]],
            3 => [e[2], e[1], e[0], 255],
            _ => {
                // ARRRRRGG GGGBBBBB, little-endian.
                let v = u16::from_le_bytes([e[0], e[1]]);
                let c = |shift: u16| (((v >> shift) & 31) * 255 / 31) as u8;
                [c(10), c(5), c(0), 255]
            }
        })
        .collect();
    let mut data = &bytes[18 + id_len + map_len * entry_bytes..];
    let index_bytes = index_bits as usize / 8;
    let read_index = |data: &mut &[u8]| -> Option<usize> {
        let (index, rest) = data.split_at_checked(index_bytes)?;
        *data = rest;
        let index = if index_bytes == 1 {
            index[0] as usize
        } else {
            u16::from_le_bytes([index[0], index[1]]) as usize
        };
        index.checked_sub(map_first)
    };
    let mut pixels: Vec<[u8; 4]> = Vec::with_capacity(width * height);
    while pixels.len() < width * height {
        let (count, repeat) = if image_type == 9 {
            let (&packet, rest) = data.split_first()?;
            data = rest;
            ((packet & 0x7f) as usize + 1, packet & 0x80 != 0)
        } else {
            (width * height, false)
        };
        if repeat {
            let color = *palette.get(read_index(&mut data)?)?;
            pixels.extend(std::iter::repeat_n(color, count));
        } else {
            for _ in 0..count {
                pixels.push(*palette.get(read_index(&mut data)?)?);
            }
        }
    }
    pixels.truncate(width * height);
    // Rows are bottom-up unless descriptor bit 5 says top-down.
    if descriptor & 0x20 == 0 {
        pixels = pixels.chunks(width).rev().flatten().copied().collect();
    }
    Some(Image {
        width: width as u32,
        height: height as u32,
        format: TextureFormat::Rgba8,
        data: pixels.into_iter().flatten().collect(),
    })
}

fn read_f32(bytes: &[u8]) -> f32 {
    f32::from_le_bytes(bytes.try_into().unwrap())
}

impl Image {
    /// HDR colour in a gamma-space project: each colour channel through Unity's
    /// LinearToGammaSpace (the sRGB curve, values above 1 raised to 1/2.2). Measured on the
    /// 6000.0 Editor: an EXR cubemap's 0.18, 0.10 and 1.19 import as 0.46, 0.35 and 1.08.
    fn linear_to_gamma(&mut self) {
        fn encode(v: f32) -> f32 {
            if v <= 0.0 {
                0.0
            } else if v <= 0.0031308 {
                v * 12.92
            } else if v < 1.0 {
                1.055 * v.powf(0.416_666_67) - 0.055
            } else if v == 1.0 {
                1.0
            } else {
                v.powf(0.454_545_45)
            }
        }
        for texel in self.data.chunks_exact_mut(16) {
            for c in 0..3 {
                let at = c * 4;
                let v = f32::from_le_bytes(texel[at..at + 4].try_into().unwrap());
                texel[at..at + 4].copy_from_slice(&encode(v).to_le_bytes());
            }
        }
    }

    /// Float texels as bytes (each channel clamped to 0..1 and rounded).
    fn into_bytes(self) -> Image {
        if self.format != TextureFormat::Rgba32F {
            return self;
        }
        let data = self
            .data
            .as_chunks::<4>()
            .0
            .iter()
            .map(|c| (f32::from_le_bytes(*c).clamp(0.0, 1.0) * 255.0 + 0.5) as u8)
            .collect();
        Image {
            format: TextureFormat::Rgba8,
            data,
            ..self
        }
    }

    fn set_opaque(&mut self) {
        match self.format {
            TextureFormat::Rgba8 => self
                .data
                .as_chunks_mut::<4>()
                .0
                .iter_mut()
                .for_each(|p| p[3] = 255),
            TextureFormat::Rgba32F => self
                .data
                .as_chunks_mut::<16>()
                .0
                .iter_mut()
                .for_each(|p| p[12..].copy_from_slice(&1f32.to_le_bytes())),
        }
    }

    /// Alpha from the mean of the colour channels, rounded down (measured on the 6000.0 Editor).
    fn alpha_from_gray(&mut self) {
        match self.format {
            TextureFormat::Rgba8 => self.data.as_chunks_mut::<4>().0.iter_mut().for_each(|p| {
                p[3] = ((p[0] as u32 + p[1] as u32 + p[2] as u32) / 3) as u8;
            }),
            TextureFormat::Rgba32F => self.data.as_chunks_mut::<16>().0.iter_mut().for_each(|p| {
                let c = |i: usize| read_f32(&p[i * 4..i * 4 + 4]);
                let mean = (c(0) + c(1) + c(2)) / 3.0;
                p[12..].copy_from_slice(&mean.to_le_bytes());
            }),
        }
    }

    /// A Cookie texture: the Editor keeps only its alpha (an Alpha8 texture: what the alpha
    /// source gives, so an opaque image's cookie is white; measured with the URP probe's
    /// cookie_directional scenario, tests/projects/urp). Every channel holds it here, so a
    /// shader reading the colour (as URP does from an RGBA texture) or the alpha sees the same
    /// value.
    fn alpha_to_all_channels(&mut self) {
        match self.format {
            TextureFormat::Rgba8 => self.data.as_chunks_mut::<4>().0.iter_mut().for_each(|p| {
                *p = [p[3]; 4];
            }),
            TextureFormat::Rgba32F => self.data.as_chunks_mut::<16>().0.iter_mut().for_each(|p| {
                let alpha: [u8; 4] = p[12..].try_into().unwrap();
                for c in 0..3 {
                    p[c * 4..c * 4 + 4].copy_from_slice(&alpha);
                }
            }),
        }
    }

    fn has_alpha(&self) -> bool {
        match self.format {
            TextureFormat::Rgba8 => self.data.as_chunks::<4>().0.iter().any(|p| p[3] < 255),
            TextureFormat::Rgba32F => self
                .data
                .as_chunks::<16>()
                .0
                .iter()
                .any(|p| read_f32(&p[12..]) < 1.0),
        }
    }

    /// "Alpha is transparency" (matching the Unity 6000.0.71f1 Editor's output in
    /// tests/projects/textureimport byte for byte): every fully transparent texel takes
    /// the colour of one visible texel (alpha > 0) and keeps alpha 0, so filtering does not
    /// bleed the colour hidden under transparent areas into the edges. The visible texel is
    /// found by jump flooding on the bottom-up image: each texel starts with itself if visible,
    /// then for steps k = min(max(w, h) / 2, 16), k / 2, ..., 1 looks at its own candidate and
    /// those of the texels k left, right, below and above (in that order), keeping the first
    /// one nearest in Manhattan distance. Texels the flood never reaches keep their colour.
    fn dilate_transparent(&mut self) {
        const MAX_STEP: usize = 16;
        const NONE: (u16, u16) = (u16::MAX, u16::MAX);
        if self.format != TextureFormat::Rgba8 {
            return;
        }
        let (w, h) = (self.width as usize, self.height as usize);
        // Coordinates are 16-bit with 0xffff for none; larger images are left as they are.
        if w == 0 || h == 0 || w > 0xfffe || h > 0xfffe {
            return;
        }
        // Rows are stored top-down here; the Editor floods bottom-up (y = 0 is the last row).
        let index = |x: usize, y: usize| (h - 1 - y) * w + x;
        let data = &mut self.data;
        let visible = |data: &[u8], i: usize| data[i * 4 + 3] != 0;
        let mut seeds: Vec<(u16, u16)> = vec![NONE; w * h];
        for y in 0..h {
            for x in 0..w {
                if visible(data, index(x, y)) {
                    seeds[y * w + x] = (x as u16, y as u16);
                }
            }
        }
        let distance = |(sx, sy): (u16, u16), x: usize, y: usize| {
            (sx as isize - x as isize).unsigned_abs() + (sy as isize - y as isize).unsigned_abs()
        };
        let mut next = seeds.clone();
        let mut step = (w.max(h) / 2).min(MAX_STEP);
        while step > 0 {
            let k = step as isize;
            for y in 0..h {
                for x in 0..w {
                    let mut best = seeds[y * w + x];
                    if best != (x as u16, y as u16) {
                        let mut best_distance = if best == NONE {
                            usize::MAX
                        } else {
                            distance(best, x, y)
                        };
                        for (dx, dy) in [(-k, 0), (k, 0), (0, -k), (0, k)] {
                            let (nx, ny) = (x as isize + dx, y as isize + dy);
                            if nx < 0 || ny < 0 || nx >= w as isize || ny >= h as isize {
                                continue;
                            }
                            let seed = seeds[ny as usize * w + nx as usize];
                            if seed == NONE {
                                continue;
                            }
                            let d = distance(seed, x, y);
                            if d < best_distance {
                                (best, best_distance) = (seed, d);
                            }
                        }
                    }
                    next[y * w + x] = best;
                }
            }
            std::mem::swap(&mut seeds, &mut next);
            step /= 2;
        }
        for y in 0..h {
            for x in 0..w {
                let (sx, sy) = seeds[y * w + x];
                let i = index(x, y);
                if (sx, sy) == NONE || visible(data, i) {
                    continue;
                }
                let s = index(sx as usize, sy as usize);
                let color: [u8; 3] = data[s * 4..s * 4 + 3].try_into().unwrap();
                data[i * 4..i * 4 + 3].copy_from_slice(&color);
                data[i * 4 + 3] = 0;
            }
        }
    }

    fn resize(self, width: u32, height: u32, algorithm: ResizeAlgorithm) -> Result<Image> {
        Ok(match algorithm {
            ResizeAlgorithm::Mitchell => resize::mitchell(&self, width, height),
            ResizeAlgorithm::Bilinear => self.resample_bilinear(width, height),
        })
    }

    /// The Bilinear resize algorithm, measured on the 6000.0 Editor: each axis apart, x first,
    /// interpolating the two texels around each texel centre, bytes rounded down after each pass.
    fn resample_bilinear(&self, width: u32, height: u32) -> Image {
        let taps = |n: u32, dn: u32| -> Vec<(usize, usize, f64)> {
            (0..dn)
                .map(|x| {
                    let u = (x as f64 + 0.5) * n as f64 / dn as f64 - 0.5;
                    let i = u.floor();
                    let clamp = |v: f64| v.clamp(0.0, n as f64 - 1.0) as usize;
                    (clamp(i), clamp(i + 1.0), u - i)
                })
                .collect()
        };
        let bpp = self.format.bytes_per_pixel();
        let lerp = |a: &[u8], b: &[u8], t: f64, out: &mut Vec<u8>| match self.format {
            TextureFormat::Rgba8 => (0..4)
                .for_each(|c| out.push((a[c] as f64 * (1.0 - t) + b[c] as f64 * t).floor() as u8)),
            TextureFormat::Rgba32F => (0..4).for_each(|c| {
                let (a, b) = (
                    read_f32(&a[c * 4..c * 4 + 4]),
                    read_f32(&b[c * 4..c * 4 + 4]),
                );
                out.extend_from_slice(&(a * (1.0 - t as f32) + b * t as f32).to_le_bytes());
            }),
        };
        let (w, h) = (self.width as usize, self.height as usize);
        let dw = width as usize;
        let mut rows = Vec::with_capacity(h * dw * bpp);
        for y in 0..h {
            for &(a, b, t) in &taps(self.width, width) {
                let at = |x: usize| &self.data[(y * w + x) * bpp..(y * w + x + 1) * bpp];
                lerp(at(a), at(b), t, &mut rows);
            }
        }
        let mut data = Vec::with_capacity(dw * height as usize * bpp);
        for &(a, b, t) in &taps(self.height, height) {
            for x in 0..dw {
                let at = |y: usize| &rows[(y * dw + x) * bpp..(y * dw + x + 1) * bpp];
                lerp(at(a), at(b), t, &mut data);
            }
        }
        Image {
            width,
            height,
            format: self.format,
            data,
        }
    }

    /// sRGB bytes as linear float.
    fn into_linear_float(self) -> Image {
        if self.format != TextureFormat::Rgba8 {
            return self;
        }
        let table = srgb_to_linear_table();
        let data = self
            .data
            .as_chunks::<4>()
            .0
            .iter()
            .flat_map(|p| {
                [
                    table[p[0] as usize],
                    table[p[1] as usize],
                    table[p[2] as usize],
                    p[3] as f32 / 255.0,
                ]
            })
            .flat_map(f32::to_le_bytes)
            .collect();
        Image {
            width: self.width,
            height: self.height,
            format: TextureFormat::Rgba32F,
            data,
        }
    }

    /// Alpha-is-transparency of a flipbook as the 6000.0 Editor applies it: the jump flood of
    /// [`Image::dilate_transparent`] runs over the first `cell_w` x `cell_h` texels of the whole
    /// image in bottom-up row order, read as one cell-sized image; the texels after them keep
    /// their colour. (Each 64x64 cell of a 128x64 two-column flipbook is filled in its bottom 32
    /// rows only; measured texel for texel in tests/projects/texturearrays.)
    fn dilate_flipbook_prefix(&mut self, cell_w: u32, cell_h: u32) {
        let bpp = self.format.bytes_per_pixel();
        let len = (cell_w * cell_h) as usize * bpp;
        self.flip_rows();
        let mut prefix = Image {
            width: cell_w,
            height: cell_h,
            format: self.format,
            data: self.data[..len].to_vec(),
        };
        // The prefix is bottom-up; the flood takes top-down rows.
        prefix.flip_rows();
        prefix.dilate_transparent();
        prefix.flip_rows();
        self.data[..len].copy_from_slice(&prefix.data);
        self.flip_rows();
    }

    /// The `width` x `height` texels at (`x`, `y`) (top-down rows, as the image).
    fn crop(&self, x: u32, y: u32, width: u32, height: u32) -> Image {
        let bpp = self.format.bytes_per_pixel();
        let mut data = Vec::with_capacity((width * height) as usize * bpp);
        for row in y..y + height {
            let start = ((row * self.width + x) as usize) * bpp;
            data.extend_from_slice(&self.data[start..start + width as usize * bpp]);
        }
        Image {
            width,
            height,
            format: self.format,
            data,
        }
    }

    /// Top-down to Unity's bottom-up row order.
    fn flip_rows(&mut self) {
        let row = self.width as usize * self.format.bytes_per_pixel();
        let rows = self.height as usize;
        for y in 0..rows / 2 {
            let (top, bottom) = self.data.split_at_mut((rows - 1 - y) * row);
            top[y * row..(y + 1) * row].swap_with_slice(&mut bottom[..row]);
        }
    }
}

/// sRGB-encoded byte to linear float.
fn srgb_to_linear_table() -> &'static [f32; 256] {
    static TABLE: OnceLock<[f32; 256]> = OnceLock::new();
    TABLE.get_or_init(|| {
        std::array::from_fn(|i| {
            let c = i as f32 / 255.0;
            if c <= 0.04045 {
                c / 12.92
            } else {
                ((c + 0.055) / 1.055).powf(2.4)
            }
        })
    })
}

mod bcn;
pub use bcn::{BlockFormat, decode_level as decode_blocks};
mod cubemap;
mod mipmaps;
mod psd;
mod resize;
mod specular;
#[cfg(test)]
mod tests;
