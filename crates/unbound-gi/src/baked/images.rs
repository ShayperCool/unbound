//! The other images a bake writes beside its lightmaps (matches the Unity 6000.0.71f1 Editor's
//! Progressive CPU lightmapper, tests/projects/bakedbuffers `Directional*`, `Masks*`): the
//! directions light comes from (directional lightmaps), and in the Shadowmask Lighting Mode
//! the shadowmask, each as 8-bit texels.
//!
//! The directional image blends each texel's direct and indirect directions by how much light
//! each brings (as filtered, where the lightmap is); both images then fill the texels whose light
//! does not stand (back faces) and the padding rings around the charts from their neighbours, a
//! mean weighed by which neighbours hold their own (where the lightmap is filtered, after their
//! own filtering: the directional image's by a Gaussian filter of radius 5, the shadowmask's as
//! the direct light is denoised and filtered); the directional image then fills every other
//! texel from a chain of halved images. The seams are stitched afterwards ([`super::seams`]).

use super::composite::{self, PartFilter, PartFiltering, Post};
use super::{dilate, directional};
use anyhow::Result;

/// A texel's luminance by its red, green and blue.
const LUMINANCE: [f32; 4] = [0.22, 0.707, 0.071, 0.0];

/// A lightmap's buffers the images are made of, `width` × `height` texels from the first row.
pub struct Buffers<'a> {
    pub width: usize,
    pub height: usize,
    /// Whether each texel is used (a chart covers it).
    pub used: &'a [bool],
    /// The part of each texel's rays that met back faces first, where counted.
    pub validity: Option<&'a [f32]>,
    /// The direct light (red, green, blue, and its fourth number).
    pub direct: &'a [[f32; 4]],
    /// The indirect light and the sky's, where baked.
    pub indirect: Option<&'a [[f32; 4]]>,
    pub environment: Option<&'a [[f32; 4]]>,
}

/// Whether a texel's light stands ([`dilate::markers`]).
fn markers(b: &Buffers, backface_tolerance: f32, image: &[[f32; 4]]) -> Vec<f32> {
    dilate::markers(b.used, b.validity, backface_tolerance, image)
}

/// The direct light as the directions are weighed with it: zero for a group of four texels
/// (in the buffer's order) whose fourth numbers sum to no more than 0, the fourth number kept
/// within [0, 1] and then the marker.
fn direct_light(direct: &[[f32; 4]], marks: &[f32]) -> Vec<[f32; 4]> {
    let mut out = vec![[0.0f32; 4]; direct.len()];
    for group in 0..direct.len() / 4 {
        let at = 4 * group;
        let sum = ((direct[at][3] + direct[at + 1][3]) + direct[at + 2][3]) + direct[at + 3][3];
        if sum > 0.0 {
            out[at..at + 4].copy_from_slice(&direct[at..at + 4]);
        }
    }
    for (texel, &mark) in out.iter_mut().zip(marks) {
        texel[3] = mark;
    }
    out
}

/// The light of each lane's weight sum, as four lanes add up four values: the even lanes as
/// (c + d) + (a + b), the odd ones as (a + d) + (b + c).
fn lanes(v: [f32; 4]) -> [f32; 4] {
    let even = (v[2] + v[3]) + (v[0] + v[1]);
    let odd = (v[0] + v[3]) + (v[1] + v[2]);
    [even, odd, even, odd]
}

/// The length of a direction's first three values, per lane as [`lanes`] adds them.
fn lengths(v: [f32; 4]) -> [f32; 4] {
    let (x2, y2, z2) = (v[0] * v[0], v[1] * v[1], v[2] * v[2]);
    let even = (z2 + (x2 + y2)).sqrt();
    let odd = (x2 + (y2 + z2)).sqrt();
    [even, odd, even, odd]
}

/// The radius of the Gaussian filter of a filtered lightmap's directional image, whatever the
/// Lighting Settings' filters.
const DIRECTIONAL_RADIUS: f32 = 5.0;

/// The directional image of a lightmap: per texel the direct and indirect directions (as
/// [`super::directional::Toward::encode`] gives them) blended by each one's length times its
/// light's luminance; filled where the light does not stand and `padding` rings out, then
/// every other texel from halved images. Where the lightmap is filtered, `filtered` is its
/// direct light and indirect and sky's light as processed ([`composite::Composed::parts`])
/// and how its images are processed: the light weighs the directions as processed, and the
/// image is filtered before it is filled. 32-bit floats, before stitching and rounding.
pub fn directional(
    b: &Buffers,
    toward_direct: &[[f32; 4]],
    toward_indirect: Option<&[[f32; 4]]>,
    backface_tolerance: f32,
    padding: u32,
    filtered: Option<(&[Vec<[f32; 4]>; 2], &Post)>,
) -> Result<Vec<[f32; 4]>> {
    let texels = b.width * b.height;
    let none = vec![[0.0f32; 4]; texels];
    let (d, i) = match filtered {
        Some(([direct, indirect], _)) => (direct.clone(), indirect.clone()),
        None => {
            let direct_marks = markers(b, backface_tolerance, b.direct);
            let d = direct_light(b.direct, &direct_marks);
            let indirect_raw = b.indirect.unwrap_or(&none);
            let indirect_marks = markers(b, backface_tolerance, indirect_raw);
            let environment = b.environment.unwrap_or(&none);
            let i: Vec<[f32; 4]> = (0..texels)
                .map(|k| {
                    let mut t = indirect_raw[k];
                    for c in 0..3 {
                        t[c] = environment[k][c] + t[c];
                    }
                    t[3] = indirect_marks[k];
                    t
                })
                .collect();
            (d, i)
        }
    };
    let unlit = vec![directional::NONE; texels];
    let toward_indirect = toward_indirect.unwrap_or(&unlit);
    let mut out: Vec<[f32; 4]> = (0..texels)
        .map(|k| {
            let (dd, di) = (toward_direct[k], toward_indirect[k]);
            if !(i[k][3] > 0.0) {
                return dd;
            }
            let lum = |c: [f32; 4]| lanes(std::array::from_fn(|n| c[n] * LUMINANCE[n]));
            let (ld, li) = (lengths(dd), lengths(di));
            let (md, mi) = (lum(d[k]), lum(i[k]));
            std::array::from_fn(|n| {
                let wd = ld[n] * md[n];
                let wi = li[n] * mi[n];
                let total = wi + wd;
                let t = if total != 0.0 { wd / total } else { wd };
                (dd[n] - di[n]) * t + di[n]
            })
        })
        .collect();
    let marks = markers(b, backface_tolerance, &out);
    let process = filtered.map(|(_, post)| {
        let with = PartFiltering {
            denoise: false,
            filter: Some(PartFilter::Gaussian(DIRECTIONAL_RADIUS)),
        };
        (with, post)
    });
    out = dilate(&out, &marks, b.used, b.width, b.height, padding, process)?;
    fill_background(&mut out, b.width, b.height);
    Ok(out)
}

/// The shadowmask image of a lightmap (the direct pass's per-channel visibility of the mixed
/// lights): its fourth channel kept within [0, 1]; where the lightmap is filtered, processed as
/// its direct light is (`filtered`); filled where the light does not stand and `padding` rings
/// out (no further). 32-bit floats, before stitching and rounding.
pub fn shadowmask(
    b: &Buffers,
    mask: &[[f32; 4]],
    backface_tolerance: f32,
    padding: u32,
    filtered: Option<(PartFiltering, &Post)>,
) -> Result<Vec<[f32; 4]>> {
    let out: Vec<[f32; 4]> = mask
        .iter()
        .map(|t| [t[0], t[1], t[2], t[3].clamp(0.0, 1.0)])
        .collect();
    let marks = markers(b, backface_tolerance, &out);
    dilate(&out, &marks, b.used, b.width, b.height, padding, filtered)
}

/// The fill of a four-channel image: its first three channels and its fourth apart (each with
/// the markers as its weights), each first processed as `process` says where given.
fn dilate(
    image: &[[f32; 4]],
    marks: &[f32],
    used: &[bool],
    width: usize,
    height: usize,
    padding: u32,
    process: Option<(PartFiltering, &Post)>,
) -> Result<Vec<[f32; 4]>> {
    let mut first: Vec<[f32; 4]> = image
        .iter()
        .zip(marks)
        .map(|(t, &m)| [t[0], t[1], t[2], m])
        .collect();
    let mut fourth: Vec<[f32; 4]> = image
        .iter()
        .zip(marks)
        .map(|(t, &m)| [t[3], t[3], t[3], m])
        .collect();
    if let Some((with, post)) = process {
        composite::process(&mut first, with, post)?;
        composite::process(&mut fourth, with, post)?;
    }
    let first = dilate::fill(first, used, width, height, padding);
    let fourth = dilate::fill(fourth, used, width, height, padding);
    Ok(first
        .iter()
        .zip(&fourth)
        .map(|(a, b)| [a[0], a[1], a[2], b[0]])
        .collect())
}

/// Fills every texel of the directional image that holds nothing (all four values 0) from a
/// chain of halved images, each texel of which is the mean of the texels below it that hold
/// light (fourth value above 0), or nothing.
fn fill_background(image: &mut [[f32; 4]], width: usize, height: usize) {
    let count = (width.max(height) as f32).log2().floor() as usize + 1;
    let mut levels: Vec<(Vec<[f32; 4]>, usize, usize)> = vec![(image.to_vec(), width, height)];
    for _ in 1..count {
        let (src, w, h) = levels.last().unwrap();
        let (w2, h2) = ((w / 2).max(1), (h / 2).max(1));
        let (ox, oy) = (usize::from(*w != 1), usize::from(*h != 1));
        let mut dst = vec![[0.0f32; 4]; w2 * h2];
        for y in 0..h2 {
            for x in 0..w2 {
                let mut acc = [0.0f32; 4];
                let mut n = 0;
                for (dx, dy) in [(0, 0), (ox, 0), (0, oy), (ox, oy)] {
                    let t = src[(2 * y + dy) * w + 2 * x + dx];
                    if t[3] > 0.0 {
                        for c in 0..4 {
                            acc[c] += t[c];
                        }
                        n += 1;
                    }
                }
                if n > 0 {
                    let scale = 1.0 / n as f32;
                    dst[y * w2 + x] = acc.map(|v| v * scale);
                }
            }
        }
        levels.push((dst, w2, h2));
    }
    for l in (0..levels.len() - 1).rev() {
        let (coarse, cw, ch) = levels[l + 1].clone();
        let (fine, w, h) = &mut levels[l];
        for y in 0..*h {
            for x in 0..*w {
                let t = &mut fine[y * *w + x];
                if t.iter().all(|&v| v == 0.0) {
                    *t = coarse[(y / 2).min(ch - 1) * cw + (x / 2).min(cw - 1)];
                }
            }
        }
    }
    image.copy_from_slice(&levels[0].0);
}

/// An image's 8-bit texels as written (each value within [0, 1], rounded to the nearest of 255
/// steps, linear), rows from the last (the file's first row is the image's top).
pub fn to_rgba8(image: &[[f32; 4]], width: usize, height: usize) -> Vec<u8> {
    let mut out = Vec::with_capacity(width * height * 4);
    for y in (0..height).rev() {
        for t in &image[y * width..(y + 1) * width] {
            out.extend(t.map(|v| (v.clamp(0.0, 1.0) * 255.0 + 0.5).floor() as u8));
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_written_texels_are_rounded_and_the_last_row_comes_first() {
        let image = [[0.2, -1.0, 0.5, 2.0], [1.0, 0.0, 0.25, 0.75]];
        // 0.2 → 51.5 → 51; 0.5 → 128; 0.25 → 64.25 → 64; 0.75 → 191.75 → 191.
        assert_eq!(
            to_rgba8(&image, 1, 2),
            vec![255, 0, 64, 191, 51, 0, 128, 255]
        );
    }

    #[test]
    fn direct_light_of_a_group_of_four_without_any_is_none() {
        let mut direct = vec![[3.0, 3.0, 3.0, 0.0]; 8];
        direct[5][3] = 1.0;
        let marks = [1.0, 1.0, 1.0, 1.0, 0.0, 1.0, 1.0, 1.0];
        let out = direct_light(&direct, &marks);
        // (The light of the first group none; each texel's fourth value its marker.)
        assert!(out[..4].iter().all(|t| *t == [0.0, 0.0, 0.0, 1.0]));
        assert_eq!(out[4], [3.0, 3.0, 3.0, 0.0]);
        assert_eq!(out[5], [3.0, 3.0, 3.0, 1.0]);
    }

    #[test]
    fn the_directions_blend_by_each_lights_length_times_luminance() {
        // Four used texels: direct light alone, no light, as much of each, indirect alone.
        let used = [true; 4];
        let light = [1.0, 1.0, 1.0, 1.0];
        let dark = [0.0, 0.0, 0.0, 1.0];
        let direct = [light, dark, light, dark];
        let indirect = [dark, dark, light, light];
        let b = Buffers {
            width: 4,
            height: 1,
            used: &used,
            validity: None,
            direct: &direct,
            indirect: Some(&indirect),
            environment: None,
        };
        // Directions of equal lengths.
        let dd = [1.0, 0.5, 0.5, 1.0];
        let di = [0.5, 1.0, 0.5, 0.0];
        let out = directional(&b, &[dd; 4], Some(&[di; 4]), 0.9, 0, None).unwrap();
        assert_eq!(out[0], dd);
        // (No light either way: the indirect direction.)
        assert_eq!(out[1], di);
        assert_eq!(out[2], [0.75, 0.75, 0.5, 0.5]);
        assert_eq!(out[3], di);
    }

    #[test]
    fn a_texel_without_indirect_light_of_its_own_keeps_its_direct_direction() {
        // The second texel's indirect light is no number: it does not stand, so the texel
        // keeps its direct direction (its own still stands: no fill).
        let used = [true; 4];
        let light = [1.0, 1.0, 1.0, 1.0];
        let mut indirect = [light; 4];
        indirect[1] = [f32::NAN, 1.0, 1.0, 1.0];
        let b = Buffers {
            width: 4,
            height: 1,
            used: &used,
            validity: None,
            direct: &[light; 4],
            indirect: Some(&indirect),
            environment: None,
        };
        let dd = [1.0, 0.5, 0.5, 1.0];
        let di = [0.5, 1.0, 0.5, 0.0];
        let out = directional(&b, &[dd; 4], Some(&[di; 4]), 0.9, 0, None).unwrap();
        assert_eq!(out[0], [0.75, 0.75, 0.5, 0.5]);
        assert_eq!(out[1], dd);
    }

    #[test]
    fn the_directional_background_comes_from_halved_images_of_odd_sizes() {
        // 3 × 3 with light in the middle, 5 × 2 with light in one corner: every texel holding
        // nothing takes the light of the halved image above it.
        let lit = [0.25, 0.5, 0.75, 1.0];
        let mut image = vec![[0.0f32; 4]; 9];
        image[4] = lit;
        fill_background(&mut image, 3, 3);
        assert!(image.iter().all(|t| *t == lit), "{image:?}");
        let mut image = vec![[0.0f32; 4]; 10];
        image[0] = lit;
        fill_background(&mut image, 5, 2);
        assert!(image.iter().all(|t| *t == lit), "{image:?}");
        // One texel wide.
        let mut image = vec![[0.0f32; 4]; 3];
        image[0] = lit;
        fill_background(&mut image, 1, 3);
        assert!(image.iter().all(|t| *t == lit), "{image:?}");
    }

    #[test]
    fn the_shadowmasks_fourth_channel_is_kept_within_zero_and_one() {
        let used = [true; 4];
        let b = Buffers {
            width: 4,
            height: 1,
            used: &used,
            validity: None,
            direct: &[[0.0; 4]; 4],
            indirect: None,
            environment: None,
        };
        let mask = [
            [0.2, 0.4, 0.6, 1.5],
            [1.0, 1.0, 1.0, -0.5],
            [0.0, 0.0, 0.0, 0.5],
            [1.0, 0.0, 1.0, 1.0],
        ];
        let out = shadowmask(&b, &mask, 0.9, 0, None).unwrap();
        assert_eq!(out[0], [0.2, 0.4, 0.6, 1.0]);
        assert_eq!(out[1], [1.0, 1.0, 1.0, 0.0]);
        assert_eq!(out[2], [0.0, 0.0, 0.0, 0.5]);
        assert_eq!(out[3], [1.0, 0.0, 1.0, 1.0]);
    }

    /// Where the lightmap is filtered, the shadowmask's three channels and its fourth are each
    /// filtered as the direct light (here a Gaussian filter of radius 1), within their chart.
    #[test]
    fn a_filtered_lightmaps_shadowmask_is_filtered_as_its_direct_light() {
        let used = [true; 4];
        let b = Buffers {
            width: 4,
            height: 1,
            used: &used,
            validity: None,
            direct: &[[0.0; 4]; 4],
            indirect: None,
            environment: None,
        };
        let mask = [
            [1.0, 0.0, 0.0, 1.0],
            [0.0, 0.0, 0.0, 0.0],
            [0.0, 1.0, 0.0, 1.0],
            [0.0, 1.0, 0.0, 1.0],
        ];
        let charts = [0, 0, 1, 1];
        let post = Post {
            width: 4,
            height: 1,
            charts: &charts,
            normals: None,
            denoiser: None,
        };
        let with = PartFiltering {
            denoise: false,
            filter: Some(PartFilter::Gaussian(1.0)),
        };
        let out = shadowmask(&b, &mask, 0.9, 0, Some((with, &post))).unwrap();
        let k = super::super::filter::kernel(1.0);
        let near = |a: f32, b: f32| (a - b).abs() < 1e-5;
        // Chart 0: its two texels mix (first channel and fourth alike); chart 1 is uniform.
        assert!(near(out[0][0], k[1] / (k[1] + k[2])), "{out:?}");
        assert!(near(out[1][0], k[0] / (k[0] + k[1])), "{out:?}");
        assert!(near(out[0][3], k[1] / (k[1] + k[2])), "{out:?}");
        assert!(near(out[2][1], 1.0) && near(out[3][3], 1.0), "{out:?}");
        // Unfiltered, the texels keep their own.
        let plain = shadowmask(&b, &mask, 0.9, 0, None).unwrap();
        assert_eq!(plain[0], [1.0, 0.0, 0.0, 1.0]);
    }
}
