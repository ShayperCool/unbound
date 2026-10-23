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
//! stay uncompressed RGBA. Cubemap/array/3D shapes are imported as their 2D source image and
//! reported through [`TextureSettings::shape`] so the caller can refuse them.
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
    pub alpha_source: AlphaSource,
    pub alpha_is_transparency: bool,
    /// The effective maximum size for the build target.
    pub max_size: u32,
    pub resize_algorithm: ResizeAlgorithm,
    /// Effective NPOT scaling: always `None` for sprites and GUI textures.
    pub npot_scale: NpotScale,
    pub mipmaps: bool,
    /// `mipMapMode`: 0 box, 1 Kaiser (approximated with box).
    pub mip_filter: i32,
    pub is_readable: bool,
    pub wrap_u: WrapMode,
    pub wrap_v: WrapMode,
    pub wrap_w: WrapMode,
    pub filter: FilterMode,
    pub aniso: i32,
    pub mip_bias: f32,
    pub sprite: SpriteSettings,
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
            alpha_source,
            alpha_is_transparency: boolean(imp, "alphaIsTransparency").unwrap_or(false),
            max_size,
            resize_algorithm,
            npot_scale,
            mipmaps: boolean(imp, "mipmaps.enableMipMap").unwrap_or(true),
            mip_filter: int(imp, "mipmaps.mipMapMode").unwrap_or(0) as i32,
            is_readable: boolean(imp, "isReadable").unwrap_or(false),
            wrap_u: wrap("wrapU"),
            wrap_v: wrap("wrapV"),
            wrap_w: wrap("wrapW"),
            filter,
            aniso,
            mip_bias,
            sprite: SpriteSettings::read(imp, texture_type == TextureType::Sprite),
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
fn platform_max_size(imp: &Value, build_target: &str) -> (u32, ResizeAlgorithm) {
    let global = int(imp, "maxTextureSize").unwrap_or(2048);
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
    let chosen = entry(build_target, true).or_else(|| entry("DefaultTexturePlatform", false));
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
    /// documentation).
    pub mips: Vec<MipLevel>,
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
}

impl Default for TextureImporter {
    fn default() -> Self {
        Self {
            build_target: "Standalone".into(),
        }
    }
}

impl Importer for TextureImporter {
    type Output = ImportedTexture;
    const KIND: &'static str = "textures";
    const VERSION: u32 = 2;

    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedTexture> {
        let settings = TextureSettings::read(meta, &self.build_target);
        import_texture(source, bytes, meta, settings)
    }

    fn options_key(&self) -> String {
        self.build_target.clone()
    }
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

    match settings.alpha_source {
        AlphaSource::None => image.set_opaque(),
        AlphaSource::FromGrayScale => image.alpha_from_gray(),
        AlphaSource::FromInput => {}
    }
    if settings.alpha_is_transparency && settings.alpha_source != AlphaSource::None {
        image.dilate_transparent();
    }

    let (w, h) = npot_size(image.width, image.height, settings.npot_scale);
    let (w, h) = fit_max_size(w, h, settings.max_size);
    if (w, h) != (image.width, image.height) {
        image = image.resize(w, h, settings.resize_algorithm)?;
    }
    image.flip_rows();
    let has_alpha = image.has_alpha();

    let format = image.format;
    let mut mips = vec![MipLevel {
        width: image.width,
        height: image.height,
        data: image.data,
    }];
    if settings.mipmaps {
        let srgb = settings.srgb && format == TextureFormat::Rgba8;
        while let Some(last) = mips.last().filter(|m| m.width > 1 || m.height > 1) {
            let (width, height, data) =
                downsample(&last.data, last.width, last.height, format, srgb);
            mips.push(MipLevel {
                width,
                height,
                data,
            });
        }
    }

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

    /// Alpha from the Rec. 601 luma of the colour.
    fn alpha_from_gray(&mut self) {
        match self.format {
            TextureFormat::Rgba8 => self.data.as_chunks_mut::<4>().0.iter_mut().for_each(|p| {
                let luma = 299 * p[0] as u32 + 587 * p[1] as u32 + 114 * p[2] as u32;
                p[3] = ((luma + 500) / 1000) as u8;
            }),
            TextureFormat::Rgba32F => self.data.as_chunks_mut::<16>().0.iter_mut().for_each(|p| {
                let c = |i: usize| read_f32(&p[i * 4..i * 4 + 4]);
                let luma = 0.299 * c(0) + 0.587 * c(1) + 0.114 * c(2);
                p[12..].copy_from_slice(&luma.to_le_bytes());
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
        use fast_image_resize::{FilterType, PixelType, ResizeAlg, ResizeOptions, Resizer, images};
        let pixel_type = match self.format {
            TextureFormat::Rgba8 => PixelType::U8x4,
            TextureFormat::Rgba32F => PixelType::F32x4,
        };
        let src = images::Image::from_vec_u8(self.width, self.height, self.data, pixel_type)?;
        let mut dst = images::Image::new(width, height, pixel_type);
        let filter = match algorithm {
            ResizeAlgorithm::Mitchell => FilterType::Mitchell,
            ResizeAlgorithm::Bilinear => FilterType::Bilinear,
        };
        // Alpha-weighted (the default): transparent pixels do not darken their neighbours.
        let options = ResizeOptions::new().resize_alg(ResizeAlg::Convolution(filter));
        Resizer::new().resize(&src, &mut dst, &options)?;
        Ok(Image {
            width,
            height,
            format: self.format,
            data: dst.into_vec(),
        })
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

/// Linear float (quantized to 1/4095) to sRGB-encoded byte.
fn linear_to_srgb_table() -> &'static [u8; 4096] {
    static TABLE: OnceLock<[u8; 4096]> = OnceLock::new();
    TABLE.get_or_init(|| {
        std::array::from_fn(|i| {
            let l = i as f32 / 4095.0;
            let c = if l <= 0.003_130_8 {
                l * 12.92
            } else {
                1.055 * l.powf(1.0 / 2.4) - 0.055
            };
            (c * 255.0).round().clamp(0.0, 255.0) as u8
        })
    })
}

/// The next mip level with a 2×2 box filter (edge texels repeat for odd sizes). For sRGB data the
/// colour channels are averaged in linear space; alpha is always linear.
fn downsample(
    src: &[u8],
    width: u32,
    height: u32,
    format: TextureFormat,
    srgb: bool,
) -> (u32, u32, Vec<u8>) {
    let (w, h) = (width as usize, height as usize);
    let (dw, dh) = ((w / 2).max(1), (h / 2).max(1));
    let bpp = format.bytes_per_pixel();
    let mut out = vec![0u8; dw * dh * bpp];
    let to_linear = srgb_to_linear_table();
    let to_srgb = linear_to_srgb_table();
    for y in 0..dh {
        let (y0, y1) = ((2 * y).min(h - 1), (2 * y + 1).min(h - 1));
        for x in 0..dw {
            let (x0, x1) = ((2 * x).min(w - 1), (2 * x + 1).min(w - 1));
            let taps = [(x0, y0), (x1, y0), (x0, y1), (x1, y1)].map(|(sx, sy)| (sy * w + sx) * bpp);
            let dst = &mut out[(y * dw + x) * bpp..][..bpp];
            match format {
                TextureFormat::Rgba8 => {
                    for c in 0..4 {
                        if srgb && c < 3 {
                            let sum: f32 =
                                taps.iter().map(|&t| to_linear[src[t + c] as usize]).sum();
                            dst[c] = to_srgb[((sum * 0.25) * 4095.0 + 0.5) as usize];
                        } else {
                            let sum: u32 = taps.iter().map(|&t| src[t + c] as u32).sum();
                            dst[c] = ((sum + 2) / 4) as u8;
                        }
                    }
                }
                TextureFormat::Rgba32F => {
                    for c in 0..4 {
                        let sum: f32 = taps
                            .iter()
                            .map(|&t| read_f32(&src[t + c * 4..t + c * 4 + 4]))
                            .sum();
                        dst[c * 4..c * 4 + 4].copy_from_slice(&(sum * 0.25).to_le_bytes());
                    }
                }
            }
        }
    }
    (dw as u32, dh as u32, out)
}

#[cfg(test)]
mod tests;
