//! Mip chain generation as Unity's TextureImporter does it (measured on the 6000.0 Editor with
//! tests/projects/textureimport, every level byte for byte unless noted):
//!
//! * Every level is filtered from the level above kept in 32-bit float (sRGB colour decoded to
//!   linear light, alpha and linear data as they are), and encoded on its own (rounded to
//!   nearest), so rounding never accumulates down the chain.
//! * Box: each texel samples the level above at its centre with bilinear weights, a 2x2 average
//!   for even sizes. Kaiser: a Kaiser-windowed sinc (width 2, alpha 4) over 8 texels with
//!   mirrored edges, used only while both sides are even (or 1); otherwise the box filter.
//! * Border mip maps: edge texels average two texels of the edge column (left/right, which wins
//!   at corners and in 1-wide levels) or the edge row of the level above, whatever the filter.
//! * Fade out: each level's encoded colour (alpha untouched) blends towards mid-gray, or a flat
//!   normal for normal maps, linearly between the start and end levels.
//! * Preserve coverage: alpha is scaled up until the level passes the alpha test as often as the
//!   top level (an approximation: a few texels differ by 1 from the Editor's).
//! * Normal maps made from height: the colour is filtered down the chain and every level's
//!   normals are made from its own height (the mean of red, green and blue).
use super::{TextureFormat, read_f32, srgb_to_linear_f64};

/// One level of the float chain: RGBA texels, rows bottom-up.
#[derive(Clone)]
pub(super) struct Level {
    pub width: u32,
    pub height: u32,
    pub texels: Vec<[f32; 4]>,
}

/// `mipmapFilter`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum Filter {
    Box,
    Kaiser,
}

/// The importer settings the chain depends on.
#[derive(Debug, Clone, Copy)]
pub(super) struct Options {
    pub filter: Filter,
    /// `borderMipmap`.
    pub border: bool,
    /// `mipMapsPreserveCoverage`: `alphaTestReferenceValue`.
    pub coverage: Option<f32>,
}

impl Level {
    pub fn decode(width: u32, height: u32, data: &[u8], format: TextureFormat, srgb: bool) -> Self {
        let texels = match format {
            TextureFormat::Rgba8 => data
                .as_chunks::<4>()
                .0
                .iter()
                .map(|p| {
                    std::array::from_fn(|c| {
                        if srgb && c < 3 {
                            srgb_to_linear_f64(p[c] as f64 / 255.0) as f32
                        } else {
                            p[c] as f32 / 255.0
                        }
                    })
                })
                .collect(),
            TextureFormat::Rgba32F => data
                .as_chunks::<16>()
                .0
                .iter()
                .map(|p| std::array::from_fn(|c| read_f32(&p[c * 4..c * 4 + 4])))
                .collect(),
        };
        Level {
            width,
            height,
            texels,
        }
    }

    /// The level in `format`. `fade` blends the encoded colour towards `fade_to` (0..1 per
    /// channel).
    pub fn encode(
        &self,
        format: TextureFormat,
        srgb: bool,
        fade: f32,
        fade_to: [f32; 3],
    ) -> Vec<u8> {
        let mut out = Vec::with_capacity(self.texels.len() * format.bytes_per_pixel());
        for texel in &self.texels {
            for (c, &v) in texel.iter().enumerate() {
                let fades = c < 3 && fade > 0.0;
                match format {
                    TextureFormat::Rgba8 => {
                        let mut e = if srgb && c < 3 {
                            linear_to_srgb_f64(v as f64) * 255.0
                        } else {
                            (v * 255.0) as f64
                        };
                        if fades {
                            e += (fade_to[c] as f64 * 255.0 - e) * fade as f64;
                        }
                        out.push(quantize(e));
                    }
                    TextureFormat::Rgba32F => {
                        let v = if fades {
                            v + (fade_to[c] - v) * fade
                        } else {
                            v
                        };
                        out.extend_from_slice(&v.to_le_bytes());
                    }
                }
            }
        }
        out
    }
}

/// A 0..255 value rounded to the nearest byte (halves up).
pub(super) fn quantize(v: f64) -> u8 {
    ((v + 0.5) as f32).clamp(0.0, 255.0) as u8
}

fn linear_to_srgb_f64(l: f64) -> f64 {
    if l <= 0.003_130_8 {
        l * 12.92
    } else {
        1.055 * l.powf(1.0 / 2.4) - 0.055
    }
}

/// How far level `level` fades: 0 up to the start level, 1 from the end level on.
pub(super) fn fade_amount(level: usize, (start, end): (i32, i32)) -> f32 {
    let level = level as i32;
    if level <= start {
        0.0
    } else if level >= end {
        1.0
    } else {
        (level - start) as f32 / (end - start) as f32
    }
}

/// `top` and the levels below it down to 1x1 (each side halves, rounding down, at least 1).
pub(super) fn chain(top: Level, options: &Options) -> Vec<Level> {
    let desired = options.coverage.map(|r| coverage(&top.texels, r, 1.0));
    let mut levels = vec![top];
    while let Some(last) = levels.last().filter(|l| l.width > 1 || l.height > 1) {
        let mut next = downsample(last, options.filter, options.border);
        if let (Some(reference), Some(desired)) = (options.coverage, desired) {
            scale_alpha_to_coverage(&mut next.texels, reference, desired);
        }
        levels.push(next);
    }
    levels
}

fn downsample(src: &Level, filter: Filter, border: bool) -> Level {
    let (w, h) = (src.width as usize, src.height as usize);
    let (dw, dh) = ((w / 2).max(1), (h / 2).max(1));
    let halves = |n: usize| n % 2 == 0 || n == 1;
    let kaiser = filter == Filter::Kaiser && halves(w) && halves(h);
    let axis = |n: usize, dn: usize| -> Vec<Taps> {
        (0..dn)
            .map(|x| {
                if kaiser {
                    Taps::Kernel(kaiser_taps(n, x))
                } else {
                    bilinear_taps(n, dn, x)
                }
            })
            .collect()
    };
    let (xs, ys) = (axis(w, dw), axis(h, dh));
    let mut texels = Vec::with_capacity(dw * dh);
    for (y, ty) in ys.iter().enumerate() {
        for (x, tx) in xs.iter().enumerate() {
            let edge_x = border && (x == 0 || x + 1 == dw);
            let edge_y = border && (y == 0 || y + 1 == dh);
            texels.push(if edge_x {
                let column = if x + 1 == dw { w - 1 } else { 0 };
                sample_axis(|row| src.texels[row * w + column], &pair_taps(h, y))
            } else if edge_y {
                let row = if y + 1 == dh { h - 1 } else { 0 };
                sample_axis(|col| src.texels[row * w + col], &pair_taps(w, x))
            } else {
                sample_axis(|row| sample_axis(|col| src.texels[row * w + col], tx), ty)
            });
        }
    }
    Level {
        width: dw as u32,
        height: dh as u32,
        texels,
    }
}

/// The source texels and weights of one destination texel along one axis.
enum Taps {
    /// Linear interpolation between two texels.
    Lerp(usize, usize, f32),
    /// A normalized kernel.
    Kernel(Vec<(usize, f64)>),
}

/// The mean of texels 2x and 2x + 1 (border texels filter this way whatever the filter or size).
fn pair_taps(n: usize, x: usize) -> Taps {
    Taps::Lerp((2 * x).min(n - 1), (2 * x + 1).min(n - 1), 0.5)
}

fn bilinear_taps(n: usize, dn: usize, x: usize) -> Taps {
    let u = (x as f32 + 0.5) * (n as f32 / dn as f32) - 0.5;
    let i = u.floor();
    let t = u - i;
    let i = i as isize;
    let clamp = |v: isize| v.clamp(0, n as isize - 1) as usize;
    Taps::Lerp(clamp(i), clamp(i + 1), t)
}

/// The 8 texels around texel pair (2x, 2x + 1) of a halving, edges mirrored.
fn kaiser_taps(n: usize, x: usize) -> Vec<(usize, f64)> {
    let mirror = |mut i: isize| {
        let n = n as isize;
        if n == 1 {
            return 0;
        }
        while i < 0 || i >= n {
            i = if i < 0 { -i - 1 } else { 2 * n - 1 - i };
        }
        i as usize
    };
    let weights = kaiser_weights();
    (-4..4)
        .zip(weights)
        .map(|(j, w)| (mirror(2 * x as isize + 1 + j), w))
        .collect()
}

/// Kaiser-windowed sinc (width 2, alpha 4) at texel offsets ±0.5 .. ±3.5 of the source, in
/// destination texels, normalized.
fn kaiser_weights() -> [f64; 8] {
    fn bessel0(x: f64) -> f64 {
        let (mut sum, mut term, mut k) = (1.0, 1.0, 0.0);
        loop {
            k += 2.0;
            term *= x * x / (k * k);
            sum += term;
            if term <= sum * 1e-16 {
                return sum;
            }
        }
    }
    fn kaiser(x: f64) -> f64 {
        const WIDTH: f64 = 2.0;
        const ALPHA: f64 = 4.0;
        let t = x / WIDTH;
        if t * t >= 1.0 {
            return 0.0;
        }
        let sinc = if x.abs() < 1e-9 {
            1.0
        } else {
            (std::f64::consts::PI * x).sin() / (std::f64::consts::PI * x)
        };
        sinc * bessel0(ALPHA * (1.0 - t * t).sqrt()) / bessel0(ALPHA)
    }
    let raw: [f64; 8] = std::array::from_fn(|i| kaiser((i as f64 - 4.0 + 0.5) * 0.5));
    let sum: f64 = raw.iter().sum();
    raw.map(|w| w / sum)
}

fn sample_axis(get: impl Fn(usize) -> [f32; 4], taps: &Taps) -> [f32; 4] {
    match taps {
        Taps::Lerp(a, b, t) => {
            let (a, b) = (get(*a), get(*b));
            std::array::from_fn(|c| a[c] * (1.0 - t) + b[c] * t)
        }
        Taps::Kernel(kernel) => {
            let mut sum = [0f64; 4];
            for &(i, w) in kernel {
                let v = get(i);
                for c in 0..4 {
                    sum[c] += v[c] as f64 * w;
                }
            }
            sum.map(|v| v as f32)
        }
    }
}

/// The fraction of texels whose alpha, scaled, passes the alpha test.
fn coverage(texels: &[[f32; 4]], reference: f32, scale: f32) -> f32 {
    let passing = texels.iter().filter(|t| t[3] * scale > reference).count();
    passing as f32 / texels.len() as f32
}

/// Scales alpha up until the level covers as much as the top level did (a level that covers
/// as much or more keeps its alpha): binary search over [0, 4], ten steps, keeping the smallest
/// scale found to cover enough.
fn scale_alpha_to_coverage(texels: &mut [[f32; 4]], reference: f32, desired: f32) {
    if coverage(texels, reference, 1.0) >= desired {
        return;
    }
    let (mut low, mut high, mut scale) = (0f32, 4f32, 1f32);
    for _ in 0..10 {
        let current = coverage(texels, reference, scale);
        if current < desired {
            low = scale;
        } else if current > desired {
            high = scale;
        } else {
            break;
        }
        scale = (low + high) * 0.5;
    }
    for t in texels.iter_mut() {
        t[3] = (t[3] * high).min(1.0);
    }
}

/// Tangent-space normals (x, y, z in -1..1) of a level whose height is the mean of red, green
/// and blue: central
/// differences (standard) or a Sobel filter over the four neighbours, wrapping at the edges,
/// with the slope scaled by `0.08 * (10^heightScale - 1)` times the level's mean side length.
pub(super) fn normals_from_height(level: &Level, height_scale: f32, sobel: bool) -> Vec<[f64; 3]> {
    let (w, h) = (level.width as isize, level.height as isize);
    let heights: Vec<f32> = level
        .texels
        .iter()
        .map(|t| (t[0] + t[1] + t[2]) / 3.0)
        .collect();
    let at = |x: isize, y: isize| heights[(y.rem_euclid(h) * w + x.rem_euclid(w)) as usize] as f64;
    let strength = 0.08 * (10f64.powf(height_scale as f64) - 1.0) * (w + h) as f64 / 2.0;
    let mut out = Vec::with_capacity((w * h) as usize);
    for y in 0..h {
        for x in 0..w {
            let (dx, dy) = if sobel {
                (
                    (at(x + 1, y - 1) + 2.0 * at(x + 1, y) + at(x + 1, y + 1)
                        - at(x - 1, y - 1)
                        - 2.0 * at(x - 1, y)
                        - at(x - 1, y + 1))
                        / 4.0,
                    (at(x - 1, y + 1) + 2.0 * at(x, y + 1) + at(x + 1, y + 1)
                        - at(x - 1, y - 1)
                        - 2.0 * at(x, y - 1)
                        - at(x + 1, y - 1))
                        / 4.0,
                )
            } else {
                (at(x + 1, y) - at(x - 1, y), at(x, y + 1) - at(x, y - 1))
            };
            let (nx, ny) = (-strength * dx, -strength * dy);
            let length = (nx * nx + ny * ny + 1.0).sqrt();
            out.push([nx / length, ny / length, 1.0 / length]);
        }
    }
    out
}
