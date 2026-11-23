//! The other images a bake writes beside its lightmaps (matches the Unity 6000.0.71f1 Editor's
//! Progressive CPU lightmapper, tests/projects/bakedbuffers `Directional*`, `Masks*`): the
//! directions light comes from (directional lightmaps), and in the Shadowmask Lighting Mode
//! the shadowmask, each as 8-bit texels.
//!
//! The directional image blends each texel's direct and indirect directions by how much light
//! each brings; both images then fill the texels whose light does not stand (back faces) and the
//! padding rings around the charts from their neighbours, a mean weighed by which neighbours
//! hold their own; the directional image then fills every other texel from a chain of halved
//! images. The seams are stitched afterwards ([`super::seams`]).

/// A texel's luminance by its red, green and blue.
const LUMINANCE: [f32; 4] = [0.22, 0.707, 0.071, 0.0];
/// The factor a neighbour mean's approximate reciprocal is corrected by.
const RECIPROCAL_CORRECTION: f32 = 1.000_244_259_834_289_6;
/// The neighbours a texel is filled from, in the order their light is summed: the row above,
/// the row below, then left and right.
const NEIGHBOURS: [(isize, isize); 8] = [
    (-1, -1),
    (0, -1),
    (1, -1),
    (-1, 1),
    (0, 1),
    (1, 1),
    (-1, 0),
    (1, 0),
];

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

/// Whether a texel's light stands: a used texel whose rays met back faces no more than the
/// tolerance allows, and whose four values are numbers (1), else 0.
fn markers(b: &Buffers, backface_tolerance: f32, image: &[[f32; 4]]) -> Vec<f32> {
    let limit = 1.0 - backface_tolerance;
    (0..b.width * b.height)
        .map(|k| {
            let stands = b.used[k]
                && (backface_tolerance <= 0.0 || b.validity.is_none_or(|v| !(v[k] > limit)))
                && image[k].iter().all(|v| v.is_finite());
            if stands { 1.0 } else { 0.0 }
        })
        .collect()
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

/// The directional image of a lightmap: per texel the direct and indirect directions (as
/// [`super::directional::Toward::encode`] gives them) blended by each one's length times its
/// light's luminance; filled where the light does not stand and `padding` rings out, then
/// every other texel from halved images. 32-bit floats, before stitching and rounding.
pub fn directional(
    b: &Buffers,
    toward_direct: &[[f32; 4]],
    toward_indirect: Option<&[[f32; 4]]>,
    backface_tolerance: f32,
    padding: u32,
) -> Vec<[f32; 4]> {
    let texels = b.width * b.height;
    let none = vec![[0.0f32; 4]; texels];
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
    let toward_indirect = toward_indirect.unwrap_or(&none);
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
    out = dilate(&out, &marks, b.used, b.width, b.height, padding);
    fill_background(&mut out, b.width, b.height);
    out
}

/// The shadowmask image of a lightmap (the direct pass's per-channel visibility of the mixed
/// lights): its fourth channel kept within [0, 1]; filled where the light does not stand and
/// `padding` rings out (no further). 32-bit floats, before stitching and rounding.
pub fn shadowmask(
    b: &Buffers,
    mask: &[[f32; 4]],
    backface_tolerance: f32,
    padding: u32,
) -> Vec<[f32; 4]> {
    let out: Vec<[f32; 4]> = mask
        .iter()
        .map(|t| [t[0], t[1], t[2], t[3].clamp(0.0, 1.0)])
        .collect();
    let marks = markers(b, backface_tolerance, &out);
    dilate(&out, &marks, b.used, b.width, b.height, padding)
}

/// The approximate reciprocal the Editor's fill takes (the processor's own estimate, whose bits
/// differ between processors; exact elsewhere).
fn reciprocal(x: f32) -> f32 {
    #[cfg(target_arch = "x86_64")]
    {
        // SAFETY: SSE is part of every x86_64 processor.
        unsafe {
            use std::arch::x86_64::{_mm_cvtss_f32, _mm_rcp_ss, _mm_set_ss};
            _mm_cvtss_f32(_mm_rcp_ss(_mm_set_ss(x)))
        }
    }
    #[cfg(not(target_arch = "x86_64"))]
    {
        1.0 / x
    }
}

/// One fill pass: a texel whose marker is above 0 is kept; the others (only those `within`, if
/// given) take the mean of their neighbours weighed by the neighbours' markers, their marker
/// the weights' sum within [0, 1]. Whether any texel came to hold light.
fn fill_pass(
    src: &[[f32; 4]],
    within: Option<&[bool]>,
    width: usize,
    height: usize,
) -> (Vec<[f32; 4]>, bool) {
    let mut dst = src.to_vec();
    let mut filled = false;
    for y in 0..height {
        for x in 0..width {
            let k = y * width + x;
            if src[k][3] > 0.0 || within.is_some_and(|w| !w[k]) {
                continue;
            }
            let mut acc = [0.0f32; 4];
            let mut weights = 0.0f32;
            for (dx, dy) in NEIGHBOURS {
                let (nx, ny) = (x as isize + dx, y as isize + dy);
                if nx < 0 || ny < 0 || nx >= width as isize || ny >= height as isize {
                    continue;
                }
                let n = src[ny as usize * width + nx as usize];
                for c in 0..4 {
                    acc[c] = n[c] * n[3] + acc[c];
                }
                weights += n[3];
            }
            let r = reciprocal(weights.max(1.0)) * RECIPROCAL_CORRECTION;
            let mut out: [f32; 4] = std::array::from_fn(|c| r * acc[c]);
            out[3] = (weights.clamp(0.0, 1.0) - out[3]) * 1.0 + out[3];
            filled |= out[3] > 0.0;
            dst[k] = out;
        }
    }
    (dst, filled)
}

/// The fill of one image (its fourth value the marker): passes over the used texels while one
/// fills any, then `padding` passes over all.
fn fill(
    image: Vec<[f32; 4]>,
    used: &[bool],
    width: usize,
    height: usize,
    padding: u32,
) -> Vec<[f32; 4]> {
    let mut current = image;
    loop {
        let (next, filled) = fill_pass(&current, Some(used), width, height);
        current = next;
        if !filled {
            break;
        }
    }
    for _ in 0..padding {
        let (next, filled) = fill_pass(&current, None, width, height);
        current = next;
        if !filled {
            break;
        }
    }
    current
}

/// The fill of a four-channel image: its first three channels and its fourth filled apart
/// (each with the markers as its weights).
fn dilate(
    image: &[[f32; 4]],
    marks: &[f32],
    used: &[bool],
    width: usize,
    height: usize,
    padding: u32,
) -> Vec<[f32; 4]> {
    let first: Vec<[f32; 4]> = image
        .iter()
        .zip(marks)
        .map(|(t, &m)| [t[0], t[1], t[2], m])
        .collect();
    let fourth: Vec<[f32; 4]> = image
        .iter()
        .zip(marks)
        .map(|(t, &m)| [t[3], t[3], t[3], m])
        .collect();
    let first = fill(first, used, width, height, padding);
    let fourth = fill(fourth, used, width, height, padding);
    first
        .iter()
        .zip(&fourth)
        .map(|(a, b)| [a[0], a[1], a[2], b[0]])
        .collect()
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
