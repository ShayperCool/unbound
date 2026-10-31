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
//! Compression formats (`textureFormat`, `textureCompression`, crunch) are not applied: textures
//! stay uncompressed RGBA. Cubemaps are imported as their six faces ([`ImportedTexture::layers`]);
//! array/3D shapes as their 2D source image, reported through [`TextureSettings::shape`] so the
//! caller can refuse them.
use crate::{
    Importer, asset_name,
    meta::{boolean, float, importer, int},
    sprite::{self, SpriteDef, SpriteSettings},
};
use anyhow::{Context, Result, anyhow, bail};
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
    /// For [`TextureType::SingleChannel`]: 0 alpha (Unity makes an Alpha8 texture), 1 red (R8).
    /// The imported data stays RGBA; the runtime picks the channel.
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
    /// The build target keeps the pixels as they are: an uncompressed format (RGBA32, ...), or
    /// compression off. Such a texture is never block-compressed.
    #[serde(default)]
    pub uncompressed: bool,
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
        } && texture_type != TextureType::NormalMap;
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
            uncompressed,
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
fn platform_format(imp: &Value, build_target: &str) -> Option<i64> {
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
    /// documentation). A cubemap holds each face's chain in turn (+X, -X, +Y, -Y, +Z, -Z).
    pub mips: Vec<MipLevel>,
    /// 6 for a cubemap, else 1.
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
    /// The mips block-compressed to BC7 for the GPU (4x smaller than RGBA8), when the importer
    /// compresses and the texture can be (a 2D RGBA8 texture of a size divisible by 4). The CPU
    /// keeps [`ImportedTexture::mips`] (pixels for `GetPixels`).
    pub bc7: Option<Vec<serde_bytes::ByteBuf>>,
}

impl ImportedTexture {
    /// The Texture2D's fileID within the asset.
    pub const FILE_ID: i64 = TEXTURE_FILE_ID;

    /// The sprite with `file_id`.
    pub fn sprite(&self, file_id: i64) -> Option<&SpriteDef> {
        self.sprites.iter().find(|s| s.file_id == file_id)
    }
}

/// The texture importer. Its option is the build target whose platform overrides apply.
#[derive(Debug, Clone)]
pub struct TextureImporter {
    pub build_target: String,
    /// Also compress eligible textures to BC7 ([`ImportedTexture::bc7`]).
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
    const VERSION: u32 = 8;

    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedTexture> {
        let mut settings = TextureSettings::read(meta, &self.build_target);
        settings.gamma_hdr = self.gamma;
        settings.normal_maps_xyz = self.normal_maps_xyz;
        let uncompressed = settings.uncompressed;
        let mut texture = import_texture(source, bytes, meta, settings)?;
        if self.compress && !uncompressed {
            texture.bc7 = compress_bc7(&texture);
        }
        Ok(texture)
    }

    fn options_key(&self) -> String {
        format!(
            "{}{}{}{}",
            self.build_target,
            if self.compress { "+bc7" } else { "" },
            if self.gamma { "+gamma" } else { "" },
            if self.normal_maps_xyz { "+xyz" } else { "" }
        )
    }
}

/// The mips of a 2D RGBA8 texture whose size is divisible by 4, compressed to BC7 (levels below 4
/// pixels padded with their edge, as the GPU's 4x4 blocks need); `None` for any other texture.
pub fn compress_bc7(texture: &ImportedTexture) -> Option<Vec<serde_bytes::ByteBuf>> {
    use rayon::prelude::*;
    if texture.format != TextureFormat::Rgba8
        || texture.layers != 1
        || texture.width % 4 != 0
        || texture.height % 4 != 0
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
    let mut image = decode(source, bytes).context("decoding")?;
    let (original_width, original_height) = (image.width, image.height);
    if settings.gamma_hdr && settings.srgb && image.format == TextureFormat::Rgba32F {
        image.linear_to_gamma();
    }

    match settings.alpha_source {
        // Only an automatic format leaves the alpha out; an explicit one with alpha keeps it.
        AlphaSource::None if settings.format_alpha != Some(true) => image.set_opaque(),
        AlphaSource::FromGrayScale => image.alpha_from_gray(),
        AlphaSource::None | AlphaSource::FromInput => {}
    }
    if settings.alpha_is_transparency && settings.alpha_source != AlphaSource::None {
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
    let sprites = sprite::read_sprites(
        importer(meta, "TextureImporter"),
        &settings.sprite,
        &name,
        (original_width, original_height),
        (mips[0].width, mips[0].height),
    );
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
        bc7: None,
    })
}

/// The imported mip chain of a 2D texture (just its top level without mipmaps).
fn mip_chain(image: Image, settings: &TextureSettings) -> Vec<MipLevel> {
    let normal_maps_xyz = settings.normal_maps_xyz;
    let format = image.format;
    let srgb = settings.srgb && format == TextureFormat::Rgba8;
    let normal_map = settings.texture_type == TextureType::NormalMap;
    let options = mipmaps::Options {
        filter: if settings.mip_filter == 1 {
            mipmaps::Filter::Kaiser
        } else {
            mipmaps::Filter::Box
        },
        border: settings.border_mipmap,
        coverage: settings.preserve_coverage,
    };
    let fade = |level: usize| {
        settings
            .mip_fade
            .map_or(0.0, |f| mipmaps::fade_amount(level, f))
    };
    let chain = |top: mipmaps::Level, options: &mipmaps::Options| {
        if settings.mipmaps {
            mipmaps::chain(top, options)
        } else {
            vec![top]
        }
    };

    if let Some((scale, filter)) = settings
        .normal_from_height
        .filter(|_| format == TextureFormat::Rgba8)
    {
        // The colour is filtered down the chain; each level's normals come from its height.
        let top = mipmaps::Level::decode(image.width, image.height, &image.data, format, false);
        let options = mipmaps::Options {
            coverage: None,
            ..options
        };
        return chain(top, &options)
            .iter()
            .enumerate()
            .map(|(i, level)| {
                let t = fade(i) as f64;
                let encode = |v: f64, to: f64| {
                    let e = (v * 0.5 + 0.5) * 255.0;
                    mipmaps::quantize(e + (to * 255.0 - e) * t)
                };
                let data = mipmaps::normals_from_height(level, scale, filter == 1)
                    .iter()
                    .flat_map(|n| {
                        let (x, y, z) = (encode(n[0], 0.5), encode(n[1], 0.5), encode(n[2], 1.0));
                        // Flipping green inverts only the green of the encoded texel.
                        let green = if settings.flip_green {
                            encode(-n[1], 0.5)
                        } else {
                            y
                        };
                        if normal_maps_xyz {
                            [x, green, z, 255]
                        } else {
                            [255, green, y, x]
                        }
                    })
                    .collect();
                MipLevel {
                    width: level.width,
                    height: level.height,
                    data,
                }
            })
            .collect();
    }

    let mut top = mipmaps::Level::decode(image.width, image.height, &image.data, format, srgb);
    if settings.flip_green {
        top.texels.iter_mut().for_each(|t| t[1] = 1.0 - t[1]);
    }
    let fade_to = if normal_map {
        [0.5, 0.5, 1.0]
    } else {
        [0.5; 3]
    };
    chain(top, &options)
        .iter()
        .enumerate()
        .map(|(i, level)| {
            let mut data = level.encode(format, srgb, fade(i), fade_to);
            if normal_map && !normal_maps_xyz {
                swizzle_dxt5nm(&mut data, format);
            }
            MipLevel {
                width: level.width,
                height: level.height,
                data,
            }
        })
        .collect()
}

/// A normal map as the DXT5nm layout stores it: (1, y, y, x).
fn swizzle_dxt5nm(data: &mut [u8], format: TextureFormat) {
    match format {
        TextureFormat::Rgba8 => data
            .as_chunks_mut::<4>()
            .0
            .iter_mut()
            .for_each(|p| *p = [255, p[1], p[1], p[0]]),
        TextureFormat::Rgba32F => data.as_chunks_mut::<16>().0.iter_mut().for_each(|p| {
            let (x, y) = (read_f32(&p[0..4]), read_f32(&p[4..8]));
            for (c, v) in [1.0, y, y, x].into_iter().enumerate() {
                p[c * 4..c * 4 + 4].copy_from_slice(&v.to_le_bytes());
            }
        }),
    }
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
    let mut faces = cubemap::faces(&image, layout);
    let face = faces[0].width;
    let (size, _) = npot_size(face, face, settings.npot_scale);
    let (size, _) = fit_max_size(size, size, settings.max_size);
    if size != face {
        faces = faces
            .into_iter()
            .map(|f| f.resize(size, size, settings.resize_algorithm))
            .collect::<Result<_>>()?;
    }
    let has_alpha = faces.iter().any(Image::has_alpha);
    let levels = if settings.mipmaps {
        32 - size.leading_zeros()
    } else {
        1
    };
    let mut srgb = settings.srgb;
    let chains: Vec<Vec<Image>> = if settings.cubemap_convolution == 1 && levels > 1 {
        // Convolved in linear light: sRGB bytes become linear floats.
        if faces[0].format == TextureFormat::Rgba8 && srgb {
            faces = faces.into_iter().map(Image::into_linear_float).collect();
            srgb = false;
        }
        cubemap::specular_mips(&faces, levels)
    } else {
        faces
            .into_iter()
            .map(|f| {
                let face_srgb = srgb && f.format == TextureFormat::Rgba8;
                let top = mipmaps::Level::decode(f.width, f.height, &f.data, f.format, face_srgb);
                let options = mipmaps::Options {
                    filter: mipmaps::Filter::Box,
                    border: false,
                    coverage: None,
                };
                let format = f.format;
                let mut chain = vec![f];
                for level in mipmaps::chain(top, &options)
                    .iter()
                    .skip(1)
                    .take(levels as usize - 1)
                {
                    chain.push(Image {
                        width: level.width,
                        height: level.height,
                        format,
                        data: level.encode(format, face_srgb, 0.0, [0.5; 3]),
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

fn decode(source: &Path, bytes: &[u8]) -> Result<Image> {
    let ext = source
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    if bytes.starts_with(b"8BPS") || (ext == "psd" && !bytes.is_empty()) {
        // Photoshop: Unity imports the merged (composite) image.
        let psd = psd::Psd::from_bytes(bytes).map_err(|e| anyhow!("psd: {e}"))?;
        return Ok(Image {
            width: psd.width(),
            height: psd.height(),
            format: TextureFormat::Rgba8,
            data: psd.rgba(),
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

    /// "Alpha is transparency": fully transparent pixels take the colour of their nearest visible
    /// neighbours (alpha stays 0), so bilinear filtering does not bleed the colour hidden under
    /// transparent areas (often black or white) into the edges. Filled in waves outwards from the
    /// visible pixels; each pixel averages its already-filled 8-neighbours.
    fn dilate_transparent(&mut self) {
        if self.format != TextureFormat::Rgba8 {
            return;
        }
        let (w, h) = (self.width as usize, self.height as usize);
        let data = &mut self.data;
        let mut filled: Vec<bool> = data.as_chunks::<4>().0.iter().map(|p| p[3] != 0).collect();
        if filled.iter().all(|&f| f) || !filled.iter().any(|&f| f) {
            return;
        }
        let neighbours = |i: usize| {
            let (x, y) = ((i % w) as isize, (i / w) as isize);
            (-1..=1)
                .flat_map(move |dy| (-1..=1).map(move |dx| (x + dx, y + dy)))
                .filter(move |&(nx, ny)| {
                    (nx, ny) != (x, y) && nx >= 0 && ny >= 0 && nx < w as isize && ny < h as isize
                })
                .map(move |(nx, ny)| ny as usize * w + nx as usize)
        };
        let mut wave: Vec<usize> = (0..w * h)
            .filter(|&i| !filled[i] && neighbours(i).any(|n| filled[n]))
            .collect();
        let mut queued = filled.clone();
        wave.iter().for_each(|&i| queued[i] = true);
        while !wave.is_empty() {
            let colors: Vec<[u8; 3]> = wave
                .iter()
                .map(|&i| {
                    let (mut sum, mut count) = ([0u32; 3], 0u32);
                    for n in neighbours(i).filter(|&n| filled[n]) {
                        for c in 0..3 {
                            sum[c] += data[n * 4 + c] as u32;
                        }
                        count += 1;
                    }
                    sum.map(|s| ((s + count / 2) / count.max(1)) as u8)
                })
                .collect();
            for (&i, color) in wave.iter().zip(&colors) {
                data[i * 4..i * 4 + 3].copy_from_slice(color);
                filled[i] = true;
            }
            let mut next = Vec::new();
            for &i in &wave {
                for n in neighbours(i) {
                    if !queued[n] {
                        queued[n] = true;
                        next.push(n);
                    }
                }
            }
            wave = next;
        }
    }

    fn resize(self, width: u32, height: u32, algorithm: ResizeAlgorithm) -> Result<Image> {
        Ok(match algorithm {
            ResizeAlgorithm::Mitchell => self.resample(width, height),
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

    /// Unity's resize (non-power-of-two scaling and the max size with the Mitchell algorithm),
    /// measured on the 6000.0 Editor: each axis apart, x first, with a Mitchell-Netravali filter
    /// (B = C = 1/3) widened by the scale when shrinking and a cubic B-spline when enlarging,
    /// centred on texel centres, edges clamped, on the stored values (no colour-space conversion
    /// or alpha weighting).
    fn resample(&self, width: u32, height: u32) -> Image {
        fn cubic(x: f64, b: f64, c: f64) -> f64 {
            let x = x.abs();
            if x < 1.0 {
                ((12.0 - 9.0 * b - 6.0 * c) * x * x * x
                    + (-18.0 + 12.0 * b + 6.0 * c) * x * x
                    + (6.0 - 2.0 * b))
                    / 6.0
            } else if x < 2.0 {
                ((-b - 6.0 * c) * x * x * x
                    + (6.0 * b + 30.0 * c) * x * x
                    + (-12.0 * b - 48.0 * c) * x
                    + (8.0 * b + 24.0 * c))
                    / 6.0
            } else {
                0.0
            }
        }
        let weights = |n: u32, dn: u32| -> Vec<Vec<(usize, f64)>> {
            let scale = n as f64 / dn as f64;
            let (b, c) = if dn < n {
                (1.0 / 3.0, 1.0 / 3.0)
            } else {
                (1.0, 0.0)
            };
            let widen = scale.max(1.0);
            (0..dn)
                .map(|x| {
                    let centre = (x as f64 + 0.5) * scale - 0.5;
                    let first = (centre - 2.0 * widen).floor() as i64 - 1;
                    let last = (centre + 2.0 * widen).ceil() as i64 + 1;
                    let taps: Vec<(usize, f64)> = (first..=last)
                        .map(|i| {
                            (
                                i.clamp(0, n as i64 - 1) as usize,
                                cubic((i as f64 - centre) / widen, b, c),
                            )
                        })
                        .filter(|&(_, w)| w != 0.0)
                        .collect();
                    let sum: f64 = taps.iter().map(|t| t.1).sum();
                    taps.into_iter().map(|(i, w)| (i, w / sum)).collect()
                })
                .collect()
        };
        let texels: Vec<[f64; 4]> = match self.format {
            TextureFormat::Rgba8 => self
                .data
                .as_chunks::<4>()
                .0
                .iter()
                .map(|p| p.map(|v| v as f64 / 255.0))
                .collect(),
            TextureFormat::Rgba32F => self
                .data
                .as_chunks::<16>()
                .0
                .iter()
                .map(|p| std::array::from_fn(|c| read_f32(&p[c * 4..c * 4 + 4]) as f64))
                .collect(),
        };
        let (w, h) = (self.width as usize, self.height as usize);
        let (dw, dh) = (width as usize, height as usize);
        let apply = |get: &dyn Fn(usize) -> [f64; 4], taps: &[(usize, f64)]| {
            let mut sum = [0f64; 4];
            for &(i, wt) in taps {
                let v = get(i);
                (0..4).for_each(|c| sum[c] += v[c] * wt);
            }
            sum
        };
        let (xs, ys) = (weights(self.width, width), weights(self.height, height));
        let mut rows = Vec::with_capacity(h * dw);
        for y in 0..h {
            for taps in &xs {
                rows.push(apply(&|i| texels[y * w + i], taps));
            }
        }
        let mut data = Vec::with_capacity(dw * dh * self.format.bytes_per_pixel());
        for taps in &ys {
            for x in 0..dw {
                let v = apply(&|j| rows[j * dw + x], taps);
                match self.format {
                    TextureFormat::Rgba8 => {
                        data.extend(v.map(|c| mipmaps::quantize(c.clamp(0.0, 1.0) * 255.0)))
                    }
                    TextureFormat::Rgba32F => v
                        .iter()
                        .for_each(|&c| data.extend_from_slice(&(c as f32).to_le_bytes())),
                }
            }
        }
        Image {
            width,
            height,
            format: self.format,
            data,
        }
    }

    /// sRGB bytes as linear floats.
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

/// sRGB-encoded value (0..1) to linear.
fn srgb_to_linear_f64(c: f64) -> f64 {
    if c <= 0.04045 {
        c / 12.92
    } else {
        ((c + 0.055) / 1.055).powf(2.4)
    }
}

mod cubemap;
mod mipmaps;
#[cfg(test)]
mod tests;
