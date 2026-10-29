//! Image bytes in and out at runtime (`ImageConversion.LoadImage`, `EncodeToPNG`, ...): decoding
//! to 8-bit RGBA and encoding RGBA pixels. Rows are bottom first, as textures store them.
use anyhow::{Context, Result, bail};
use image::{DynamicImage, ImageFormat, Rgba32FImage, RgbaImage};
use std::io::Cursor;

/// A decoded image: size, RGBA8 rows bottom first, and whether it came from a JPEG (which Unity
/// loads as RGB24; everything else as a 32-bit format).
pub struct Decoded {
    pub width: u32,
    pub height: u32,
    pub rgba: Vec<u8>,
    pub jpeg: bool,
}

pub fn decode(bytes: &[u8]) -> Result<Decoded> {
    let format = image::guess_format(bytes).context("not a known image format")?;
    let image = image::load_from_memory_with_format(bytes, format).context("decoding the image")?;
    let mut rgba = image.to_rgba8();
    image::imageops::flip_vertical_in_place(&mut rgba);
    Ok(Decoded {
        width: rgba.width(),
        height: rgba.height(),
        rgba: rgba.into_raw(),
        jpeg: format == ImageFormat::Jpeg,
    })
}

/// What to encode to.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Encoding {
    Png,
    /// With a quality from 1 to 100.
    Jpg(u8),
    Tga,
    /// 32-bit float channels.
    Exr,
}

/// Encodes RGBA8 pixels (bottom row first).
pub fn encode_rgba8(encoding: Encoding, width: u32, height: u32, rgba: &[u8]) -> Result<Vec<u8>> {
    let mut image = RgbaImage::from_raw(width, height, rgba.to_vec())
        .context("pixel data does not match the size")?;
    image::imageops::flip_vertical_in_place(&mut image);
    let image = DynamicImage::ImageRgba8(image);
    let mut out = Cursor::new(Vec::new());
    match encoding {
        Encoding::Png => image.write_to(&mut out, ImageFormat::Png)?,
        Encoding::Tga => image.write_to(&mut out, ImageFormat::Tga)?,
        Encoding::Jpg(quality) => {
            // JPEG has no alpha: Unity drops it.
            let rgb = image.to_rgb8();
            let encoder =
                image::codecs::jpeg::JpegEncoder::new_with_quality(&mut out, quality.clamp(1, 100));
            rgb.write_with_encoder(encoder)?;
        }
        Encoding::Exr => {
            return encode_rgba_f32(width, height, &image.to_rgba32f().into_raw(), true);
        }
    }
    Ok(out.into_inner())
}

/// Encodes float RGBA pixels as EXR (bottom row first unless `flipped` says they are already top first).
pub fn encode_rgba_f32(width: u32, height: u32, rgba: &[f32], flipped: bool) -> Result<Vec<u8>> {
    let mut image = Rgba32FImage::from_raw(width, height, rgba.to_vec())
        .context("pixel data does not match the size")?;
    if !flipped {
        image::imageops::flip_vertical_in_place(&mut image);
    }
    if width == 0 || height == 0 {
        bail!("an empty image cannot be encoded");
    }
    let mut out = Cursor::new(Vec::new());
    DynamicImage::ImageRgba32F(image).write_to(&mut out, ImageFormat::OpenExr)?;
    Ok(out.into_inner())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn png_round_trips_with_rows_bottom_first() {
        // 1x2: a red bottom row, a blue top row.
        let pixels = [255, 0, 0, 255, 0, 0, 255, 128];
        let png = encode_rgba8(Encoding::Png, 1, 2, &pixels).unwrap();
        let decoded = decode(&png).unwrap();
        assert_eq!((decoded.width, decoded.height, decoded.jpeg), (1, 2, false));
        assert_eq!(decoded.rgba, pixels);
        // The file stores the top row first.
        let file = image::load_from_memory(&png).unwrap().to_rgba8();
        assert_eq!(file.get_pixel(0, 0).0, [0, 0, 255, 128]);
    }

    #[test]
    fn jpegs_are_marked_and_exr_encodes() {
        let pixels = vec![200u8; 4 * 8 * 8];
        let jpg = encode_rgba8(Encoding::Jpg(90), 8, 8, &pixels).unwrap();
        assert!(decode(&jpg).unwrap().jpeg);
        let exr = encode_rgba_f32(2, 2, &[0.5; 16], false).unwrap();
        assert!(exr.starts_with(&[0x76, 0x2f, 0x31, 0x01]));
    }
}
