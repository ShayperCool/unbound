//! Cubemaps from images (`textureShape: Cube`): the six faces cut from a 6:1 or 1:6 strip or a
//! horizontal or vertical cross (4:3, 3:4, or cells that are not square), projected from a
//! latitude-longitude panorama or a sphere map, or the image's corner square on every face, as
//! `generateCubemap` says (AutoCubemap picks by aspect). The glossy convolution of their mips is
//! in `specular`.
//!
//! Faces are in Unity's order (+X, -X, +Y, -Y, +Z, -Z) and orientation: a face of a strip or
//! cross is the square as the image shows it, top-down (the importer then flips rows as for any
//! texture); in the vertical cross -Z is upside down. Directions of a face texel follow the
//! D3D/Vulkan cube convention. The layouts other than the exact strips and crosses match the
//! Unity 6000.0.71f1 Editor (tests/projects/cubemapimport).
use super::{Image, TextureFormat, read_f32};
use anyhow::Result;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum Layout {
    HorizontalStrip,
    VerticalStrip,
    HorizontalCross,
    VerticalCross,
    LatLong,
    SphereMap,
    /// Every face is the image's bottom-left square (the image cannot be laid out as a cube).
    Corner,
}

/// The strip or cross an image's exact aspect makes.
fn exact(w: u64, h: u64) -> Option<Layout> {
    if w == 6 * h {
        Some(Layout::HorizontalStrip)
    } else if h == 6 * w {
        Some(Layout::VerticalStrip)
    } else if 3 * w == 4 * h {
        Some(Layout::HorizontalCross)
    } else if 4 * w == 3 * h {
        Some(Layout::VerticalCross)
    } else {
        None
    }
}

/// What AutoCubemap makes of an image of no exact layout, by its aspect (width / height,
/// inclusive bounds as fractions): each layout takes images near its own aspect, within the
/// bands every measured image of the Unity 6000.0.71f1 Editor falls in (tests/projects/
/// cubemapimport Aspect*, Wide*, Square*, Tall*); between a layout's last measured image and the
/// next layout's first, the choice is not measured, so it is not taken there.
const AUTO_BANDS: &[((u64, u64), (u64, u64), Layout)] = &[
    ((1, 2), (2, 3), Layout::Corner),
    ((100, 143), (4, 5), Layout::VerticalCross),
    ((50, 59), (50, 59), Layout::Corner),
    ((9, 10), (28, 25), Layout::SphereMap),
    ((23, 20), (6, 5), Layout::Corner),
    ((5, 4), (4, 3), Layout::HorizontalCross),
    ((29, 20), (38, 25), Layout::Corner),
    ((31, 20), (14, 5), Layout::LatLong),
    ((29, 10), (5, 1), Layout::Corner),
];

/// The layout `generateCubemap` gives an image: Spheremap (1) and Cylindrical (2) whatever its
/// aspect; FullCubemap (5) a strip or cross by exact aspect, else the corner square;
/// AutoCubemap (6, and any other value) a strip or cross by exact aspect, else by
/// [`AUTO_BANDS`].
pub(super) fn layout(width: u32, height: u32, mode: i32) -> Result<Layout> {
    let (w, h) = (width as u64, height as u64);
    match mode {
        1 => return Ok(Layout::SphereMap),
        2 => return Ok(Layout::LatLong),
        _ => {}
    }
    if let Some(layout) = exact(w, h) {
        return Ok(layout);
    }
    if mode == 5 {
        return Ok(Layout::Corner);
    }
    AUTO_BANDS
        .iter()
        .find(|((ln, ld), (hn, hd), _)| ln * h <= w * ld && w * hd <= hn * h)
        .map(|&(_, _, layout)| layout)
        .ok_or_else(|| {
            anyhow::anyhow!(
                "AutoCubemap's mapping of a {width}x{height} image is not measured (aspects \
                 measured: 0.5-0.67, 50:59, 1.15-1.2, 1.45-1.52 and 2.9-5 the corner square, 0.7-0.8 a \
                 vertical cross, 0.9-1.12 a sphere map, 1.25-1.33 a horizontal cross, 1.55-2.8 a \
                 panorama, and the exact strips); set the texture's Mapping"
            )
        })
}

/// A cross's cells: whole texels across and down (its columns and rows of the image).
fn cells(width: u32, height: u32, layout: Layout) -> (u32, u32) {
    match layout {
        Layout::HorizontalCross => (width / 4, height / 3),
        _ => (width / 3, height / 4),
    }
}

/// The face size a layout cuts or projects from the image, before it is made a power of two
/// (a cross's larger cell side: its faces are squares).
pub(super) fn face_size(width: u32, height: u32, layout: Layout) -> u32 {
    match layout {
        Layout::HorizontalStrip => height,
        Layout::VerticalStrip => width,
        Layout::HorizontalCross | Layout::VerticalCross => {
            let (w, h) = cells(width, height, layout);
            w.max(h)
        }
        Layout::LatLong | Layout::SphereMap => width / 2,
        Layout::Corner => width.min(height),
    }
}

/// The six faces of an image (top-down rows): cut at the layout's face size, projected at
/// `size` (the faces' final size).
pub(super) fn faces(image: &Image, layout: Layout, size: u32) -> Vec<Image> {
    let cell = face_size(image.width, image.height, layout);
    let bpp = image.format.bytes_per_pixel();
    let cut = |col: u32, row: u32, rotate: bool| -> Image {
        let size = cell;
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
        Layout::HorizontalCross | Layout::VerticalCross => {
            // Cells of whole texels counted from the image's bottom-left (Unity's texture
            // order); each face is the square of the larger cell side from its cell's corner,
            // reaching right and up (tests/projects/cubemapimport Aspect125Auto, Aspect080Auto,
            // Aspect070Auto). Past the image's edge the edge texels are repeated: unmeasured (the
            // probe reads no texel there).
            let (cw, ch) = cells(image.width, image.height, layout);
            let rows = if layout == Layout::HorizontalCross {
                3
            } else {
                4
            };
            let square = |col: u32, row: u32, rotate: bool| -> Image {
                let x0 = col * cw;
                let bottom = (rows - 1 - row) * ch;
                let top = image.height as i64 - bottom as i64 - cell as i64;
                let mut data = Vec::with_capacity((cell * cell) as usize * bpp);
                for y in 0..cell {
                    let sy = if rotate { cell - 1 - y } else { y };
                    let row = (top + sy as i64).clamp(0, image.height as i64 - 1) as u32;
                    let line = &image.data[(row * image.width) as usize * bpp..]
                        [..image.width as usize * bpp];
                    for x in 0..cell {
                        let sx = if rotate { cell - 1 - x } else { x };
                        let column = (x0 + sx).min(image.width - 1) as usize;
                        data.extend_from_slice(&line[column * bpp..column * bpp + bpp]);
                    }
                }
                Image {
                    width: cell,
                    height: cell,
                    format: image.format,
                    data,
                }
            };
            let minus_z = if layout == Layout::HorizontalCross {
                square(3, 1, false)
            } else {
                square(1, 3, true)
            };
            vec![
                square(2, 1, false),
                square(0, 1, false),
                square(1, 0, false),
                square(1, 2, false),
                square(1, 1, false),
                minus_z,
            ]
        }
        Layout::Corner => {
            // The image's last rows, the first in Unity's bottom-up texture order, read upward.
            let mut data = Vec::with_capacity((cell * cell) as usize * bpp);
            for y in 0..cell {
                let start = (((image.height - 1 - y) * image.width) as usize) * bpp;
                data.extend_from_slice(&image.data[start..start + cell as usize * bpp]);
            }
            let face = Image {
                width: cell,
                height: cell,
                format: image.format,
                data,
            };
            vec![face; 6]
        }
        Layout::LatLong => (0..6)
            .map(|face| project(image, face, size, panorama))
            .collect(),
        Layout::SphereMap => (0..6)
            .map(|face| project(image, face, size, sphere_map))
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

/// Where in the image (u right, v down, 0..1) a direction is taken from, and whether the image
/// wraps around horizontally.
type Mapping = fn([f32; 3]) -> (f32, f32, bool);

/// A latitude-longitude panorama: u = 0.75 − longitude/2π (longitude = atan2(z, x)), v =
/// latitude/π from the top, wrapping around.
fn panorama(d: [f32; 3]) -> (f32, f32, bool) {
    let longitude = d[2].atan2(d[0]);
    let latitude = d[1].clamp(-1.0, 1.0).acos();
    let u = (0.75 - longitude / std::f32::consts::TAU).rem_euclid(1.0);
    (u, latitude / std::f32::consts::PI, true)
}

/// A sphere map (a mirrored ball seen along -Z): +Z at the image's centre, -Z at its rim, u =
/// 0.5 + x/m and v = 0.5 − y/m with m = 2·√(x² + y² + (z + 1)²).
fn sphere_map(d: [f32; 3]) -> (f32, f32, bool) {
    let m = 2.0 * (d[0] * d[0] + d[1] * d[1] + (d[2] + 1.0) * (d[2] + 1.0)).sqrt();
    if m <= f32::EPSILON {
        return (0.5, 0.5, false);
    }
    (0.5 + d[0] / m, 0.5 - d[1] / m, false)
}

/// A face of `size` texels projected out of the image through a mapping, bilinear. As the
/// Unity 6000.0.71f1 Editor projects (tests/projects/cubemapimport: every panorama and sphere map
/// texel of 30 images exact): a face texel looks through its top-left corner (x / size), and the
/// image is sampled from the bottom up with texel centres at whole coordinates (u·w, and v·h − 1
/// counted from the top).
fn project(image: &Image, face: usize, size: u32, mapping: Mapping) -> Image {
    let (w, h) = (image.width as f32, image.height as f32);
    let mut data = Vec::with_capacity((size * size) as usize * image.format.bytes_per_pixel());
    for y in 0..size {
        for x in 0..size {
            let d = direction(face, x as f32 / size as f32, y as f32 / size as f32);
            let (u, v, wraps) = mapping(d);
            let fx = u * w;
            let fx = if wraps { fx } else { fx.clamp(0.0, w - 1.0) };
            let fy = (v * h - 1.0).clamp(0.0, h - 1.0);
            let (x0, y0) = (fx.floor(), fy.floor());
            let (ax, ay) = (fx - x0, fy - y0);
            let xi = |k: f32| {
                if wraps {
                    (x0 + k).rem_euclid(w) as usize
                } else {
                    (x0 + k).min(w - 1.0) as usize
                }
            };
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
        // tests/projects/cubemapimport: 16:9 and 2:1 panoramas, 1:1 sphere maps, 3:2 and 3:1
        // corner squares; a 5:3 image (between measured bands) is refused.
        assert_eq!(layout(3840, 2160, 6).unwrap(), Layout::LatLong);
        assert_eq!(layout(200, 100, 6).unwrap(), Layout::LatLong);
        assert_eq!(layout(128, 128, 6).unwrap(), Layout::SphereMap);
        assert_eq!(layout(192, 128, 6).unwrap(), Layout::Corner);
        assert_eq!(layout(240, 80, 6).unwrap(), Layout::Corner);
        assert_eq!(layout(64, 128, 6).unwrap(), Layout::Corner);
        assert_eq!(layout(125, 100, 6).unwrap(), Layout::HorizontalCross);
        assert_eq!(layout(100, 143, 6).unwrap(), Layout::VerticalCross);
        assert!(layout(124, 100, 6).is_err());
        assert!(layout(100, 117, 6).is_err());
        assert!(layout(500, 300, 6).is_ok());
        assert!(layout(600, 100, 6).is_ok());
        assert!(layout(700, 100, 6).is_err());
        // Cylindrical and Spheremap whatever the aspect; FullCubemap the corner square.
        assert_eq!(layout(128, 128, 2).unwrap(), Layout::LatLong);
        assert_eq!(layout(192, 32, 2).unwrap(), Layout::LatLong);
        assert_eq!(layout(256, 128, 1).unwrap(), Layout::SphereMap);
        assert_eq!(layout(192, 108, 5).unwrap(), Layout::Corner);
        assert_eq!(layout(768, 128, 5).unwrap(), Layout::HorizontalStrip);
    }

    /// A cross whose cells are not square: whole cells from the bottom-left, each face the
    /// larger side's square reaching right and up, repeated past the image's edge
    /// (tests/projects/cubemapimport Aspect080Auto: 100x125, cells 33x31, faces 33).
    #[test]
    fn an_uneven_cross_cuts_squares_of_its_larger_cell_side() {
        let (w, h) = (100u32, 125u32);
        let image = Image {
            width: w,
            height: h,
            format: TextureFormat::Rgba8,
            data: (0..h)
                .flat_map(|y| (0..w).flat_map(move |x| [x as u8, y as u8, 255, 255]))
                .collect(),
        };
        let faces = faces(&image, Layout::VerticalCross, 64);
        assert_eq!(faces[4].width, 33);
        let at = |face: &Image, x: u32, y: u32| {
            let i = ((y * face.width + x) * 4) as usize;
            (face.data[i], face.data[i + 1])
        };
        // +Y (column 1, top row): rows from 125 - 33 - 3 * 31 = -1, the first repeated.
        assert_eq!(at(&faces[2], 0, 0), (33, 0));
        assert_eq!(at(&faces[2], 0, 1), (33, 0));
        assert_eq!(at(&faces[2], 0, 2), (33, 1));
        // +Z (column 1, second row) from row 30.
        assert_eq!(at(&faces[4], 0, 0), (33, 30));
    }

    /// The corner square is the image's bottom rows read upward, on every face.
    #[test]
    fn the_corner_square_is_the_bottom_left_square_read_upward() {
        let (w, h) = (3u32, 2u32);
        let data = (0..w * h).flat_map(|i| [i as u8, 0, 0, 255]).collect();
        let image = Image {
            width: w,
            height: h,
            format: TextureFormat::Rgba8,
            data,
        };
        let faces = faces(&image, Layout::Corner, 2);
        assert_eq!(faces.len(), 6);
        // Rows: the source's last row (3, 4), then its first (0, 1).
        let red: Vec<u8> = faces[3].data.chunks(4).map(|p| p[0]).collect();
        assert_eq!(red, [3, 4, 0, 1]);
    }

    /// +Z looks at a sphere map's centre, and the projected faces read back as the Editor's
    /// (tests/projects/cubemapimport: Square11Spheremap face4 8,8 is 46,45; Wide21Cylindrical
    /// face0 16,16 is 166,41), from an image holding each texel's position.
    #[test]
    fn projections_read_back_as_the_editors() {
        assert_eq!(sphere_map(direction(4, 0.5, 0.5)), (0.5, 0.5, false));
        let positions = |w: u32, h: u32| Image {
            width: w,
            height: h,
            format: TextureFormat::Rgba8,
            data: (0..h)
                .flat_map(|y| (0..w).flat_map(move |x| [x as u8, y as u8, 255, 255]))
                .collect(),
        };
        let at = |face: &Image, x: u32, y: u32| {
            let i = ((y * face.width + x) * 4) as usize;
            (face.data[i], face.data[i + 1])
        };
        let sphere = faces(&positions(128, 128), Layout::SphereMap, 64);
        assert_eq!(at(&sphere[4], 8, 8), (46, 45));
        let panorama = faces(&positions(256, 128), Layout::LatLong, 128);
        assert_eq!(at(&panorama[0], 16, 16), (166, 41));
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
        let faces = faces(&image, Layout::VerticalCross, 2);
        // -Z is cells x 2..4, y 6..8; its bottom-right source texel (3, 7) comes first.
        assert_eq!(faces[5].data[0], (7 * w + 3) as u8);
        // +Z (x 2..4, y 2..4) as is.
        assert_eq!(faces[4].data[0], (2 * w + 2) as u8);
    }
}
