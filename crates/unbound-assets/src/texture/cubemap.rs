//! Cubemaps from images (`textureShape: Cube`): the six faces cut from a 6:1 or 1:6 strip or a
//! 4:3 or 3:4 cross, or projected from a 2:1 latitude-longitude panorama, as `generateCubemap`
//! says (AutoCubemap picks by aspect). The glossy convolution of their mips is in `specular`.
//!
//! Faces are in Unity's order (+X, -X, +Y, -Y, +Z, -Z) and orientation: a face of a strip or
//! cross is the square as the image shows it, top-down (the importer then flips rows as for any
//! texture); in the vertical cross -Z is upside down. Directions of a face texel follow the
//! D3D/Vulkan cube convention (assumed for the image layouts, not measured).
use super::{Image, TextureFormat, read_f32};
use anyhow::{Result, bail};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum Layout {
    HorizontalStrip,
    VerticalStrip,
    HorizontalCross,
    VerticalCross,
    LatLong,
}

/// The layout `generateCubemap` gives an image: AutoCubemap (6) and FullCubemap (5) by aspect,
/// Cylindrical (2) a panorama; Spheremap (1) is not supported.
pub(super) fn layout(width: u32, height: u32, mode: i32) -> Result<Layout> {
    let (w, h) = (width as u64, height as u64);
    let by_aspect = if w == 6 * h {
        Layout::HorizontalStrip
    } else if h == 6 * w {
        Layout::VerticalStrip
    } else if 3 * w == 4 * h {
        Layout::HorizontalCross
    } else if 4 * w == 3 * h {
        Layout::VerticalCross
    } else if w == 2 * h {
        Layout::LatLong
    } else {
        bail!(
            "a {width}x{height} image is no cubemap layout Unbound knows (6:1, 1:6, 4:3, 3:4, 2:1)"
        )
    };
    match mode {
        1 => bail!("spheremap cubemaps are not supported yet"),
        2 => Ok(Layout::LatLong),
        _ => Ok(by_aspect),
    }
}

/// The face size of a layout.
pub(super) fn face_size(width: u32, height: u32, layout: Layout) -> u32 {
    match layout {
        Layout::HorizontalStrip => height,
        Layout::VerticalStrip => width,
        Layout::HorizontalCross => width / 4,
        Layout::VerticalCross => width / 3,
        Layout::LatLong => width / 4,
    }
}

/// The six faces of an image (top-down rows), each `size` square before any resizing.
pub(super) fn faces(image: &Image, layout: Layout) -> Vec<Image> {
    let size = face_size(image.width, image.height, layout);
    let cut = |col: u32, row: u32, rotate: bool| -> Image {
        let bpp = image.format.bytes_per_pixel();
        let mut data = Vec::with_capacity((size * size) as usize * bpp);
        for y in 0..size {
            let sy = if rotate { size - 1 - y } else { y };
            let start = (((row * size + sy) * image.width + col * size) as usize) * bpp;
            let line = &image.data[start..start + size as usize * bpp];
            if rotate {
                for px in line.chunks(bpp).rev() {
                    data.extend_from_slice(px);
                }
            } else {
                data.extend_from_slice(line);
            }
        }
        Image {
            width: size,
            height: size,
            format: image.format,
            data,
        }
    };
    match layout {
        Layout::HorizontalStrip => (0..6).map(|i| cut(i, 0, false)).collect(),
        Layout::VerticalStrip => (0..6).map(|i| cut(0, i, false)).collect(),
        Layout::HorizontalCross => vec![
            cut(2, 1, false),
            cut(0, 1, false),
            cut(1, 0, false),
            cut(1, 2, false),
            cut(1, 1, false),
            cut(3, 1, false),
        ],
        Layout::VerticalCross => vec![
            cut(2, 1, false),
            cut(0, 1, false),
            cut(1, 0, false),
            cut(1, 2, false),
            cut(1, 1, false),
            cut(1, 3, true),
        ],
        Layout::LatLong => (0..6)
            .map(|face| from_panorama(image, face, size))
            .collect(),
    }
}

/// The direction through a face texel's center, `s`, `t` from the face's top-left.
pub(super) fn direction(face: usize, s: f32, t: f32) -> [f32; 3] {
    let (sc, tc) = (2.0 * s - 1.0, 2.0 * t - 1.0);
    let d = match face {
        0 => [1.0, -tc, -sc],
        1 => [-1.0, -tc, sc],
        2 => [sc, 1.0, tc],
        3 => [sc, -1.0, -tc],
        4 => [sc, -tc, 1.0],
        _ => [-sc, -tc, -1.0],
    };
    normalize(d)
}

fn normalize(d: [f32; 3]) -> [f32; 3] {
    let l = (d[0] * d[0] + d[1] * d[1] + d[2] * d[2]).sqrt().max(1e-20);
    [d[0] / l, d[1] / l, d[2] / l]
}

/// A pixel as linear RGBA floats.
fn pixel(image: &Image, x: usize, y: usize) -> [f32; 4] {
    let i = y * image.width as usize + x;
    match image.format {
        TextureFormat::Rgba8 => {
            let p = &image.data[i * 4..i * 4 + 4];
            [p[0], p[1], p[2], p[3]].map(|c| c as f32 / 255.0)
        }
        TextureFormat::Rgba32F => {
            let p = &image.data[i * 16..i * 16 + 16];
            std::array::from_fn(|c| read_f32(&p[c * 4..c * 4 + 4]))
        }
    }
}

fn store(format: TextureFormat, value: [f32; 4], out: &mut Vec<u8>) {
    match format {
        TextureFormat::Rgba8 => out.extend(value.map(|c| (c.clamp(0.0, 1.0) * 255.0 + 0.5) as u8)),
        TextureFormat::Rgba32F => value
            .iter()
            .for_each(|c| out.extend_from_slice(&c.to_le_bytes())),
    }
}

/// A face of a panorama: u = 0.75 − longitude/2π (longitude = atan2(z, x)), v = latitude/π from
/// the top, as the built-in Skybox/Panoramic shader maps it (assumed for the importer), bilinear.
fn from_panorama(image: &Image, face: usize, size: u32) -> Image {
    let (w, h) = (image.width as f32, image.height as f32);
    let mut data = Vec::with_capacity((size * size) as usize * image.format.bytes_per_pixel());
    for y in 0..size {
        for x in 0..size {
            let d = direction(
                face,
                (x as f32 + 0.5) / size as f32,
                (y as f32 + 0.5) / size as f32,
            );
            let longitude = d[2].atan2(d[0]);
            let latitude = d[1].clamp(-1.0, 1.0).acos();
            let u = (0.75 - longitude / std::f32::consts::TAU).rem_euclid(1.0);
            let v = latitude / std::f32::consts::PI;
            let (fx, fy) = (u * w - 0.5, (v * h - 0.5).clamp(0.0, h - 1.0));
            let (x0, y0) = (fx.floor(), fy.floor());
            let (ax, ay) = (fx - x0, fy - y0);
            let xi = |k: f32| ((x0 + k).rem_euclid(w)) as usize;
            let yi = |k: f32| ((y0 + k).min(h - 1.0)) as usize;
            let p00 = pixel(image, xi(0.0), yi(0.0));
            let p10 = pixel(image, xi(1.0), yi(0.0));
            let p01 = pixel(image, xi(0.0), yi(1.0));
            let p11 = pixel(image, xi(1.0), yi(1.0));
            let value: [f32; 4] = std::array::from_fn(|c| {
                (p00[c] * (1.0 - ax) + p10[c] * ax) * (1.0 - ay)
                    + (p01[c] * (1.0 - ax) + p11[c] * ax) * ay
            });
            store(image.format, value, &mut data);
        }
    }
    Image {
        width: size,
        height: size,
        format: image.format,
        data,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn layouts_by_aspect() {
        assert_eq!(layout(768, 128, 6).unwrap(), Layout::HorizontalStrip);
        assert_eq!(layout(128, 768, 6).unwrap(), Layout::VerticalStrip);
        assert_eq!(layout(512, 384, 6).unwrap(), Layout::HorizontalCross);
        assert_eq!(layout(384, 512, 6).unwrap(), Layout::VerticalCross);
        assert_eq!(layout(512, 256, 6).unwrap(), Layout::LatLong);
        assert!(layout(500, 300, 6).is_err());
    }

    /// The vertical cross's -Z is upside down: its top-left texel is the source's bottom-right.
    #[test]
    fn the_vertical_cross_turns_negative_z() {
        let (w, h) = (6u32, 8u32);
        let data = (0..w * h).flat_map(|i| [i as u8, 0, 0, 255]).collect();
        let image = Image {
            width: w,
            height: h,
            format: TextureFormat::Rgba8,
            data,
        };
        let faces = faces(&image, Layout::VerticalCross);
        // -Z is cells x 2..4, y 6..8; its bottom-right source texel (3, 7) comes first.
        assert_eq!(faces[5].data[0], (7 * w + 3) as u8);
        // +Z (x 2..4, y 2..4) as is.
        assert_eq!(faces[4].data[0], (2 * w + 2) as u8);
    }
}
