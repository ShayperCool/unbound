//! Mip chain generation as Unity's TextureImporter does it: halving, border mips, coverage
//! preservation, per-level processing and the mip fade, every operation in 32-bit float in the
//! order that reproduces the Unity 6000.0.71f1 Editor's output (tests/projects/textureimport
//! matches every level byte for byte).
//!
//! * The chain is computed first, then every level is processed and rounded on its own, so
//!   rounding never accumulates down the chain and the fade does not feed the next level.
//! * The top level of a linear 8-bit texture stays in bytes: the first mip filters the bytes and
//!   scales by 1/255. An sRGB texture's top level is decoded to linear float through an
//!   sRGB-to-linear table first; a normal map made from height is converted to float (bytes
//!   times 1/255).
//! * Levels with an even width and height are halved by a box (the 2x2 sum times 1/4) or a
//!   Kaiser filter (two separable passes); a level with an odd side (1 included) is resampled with
//!   16.16 fixed-point bilinear filtering.
//! * Border mip maps replace the edge rows, then the edge columns, with pair averages.
//! * Preserve coverage bisects the alpha threshold until the level passes the alpha test about as
//!   often as the level above, then scales alpha by reference / threshold.
//! * Per level: normals from height, flip green, linear to gamma, the DXT5nm layout, the fade,
//!   then rounding to the nearest byte (halves up).
use super::{TextureFormat, read_f32};
use std::sync::OnceLock;

/// 1/255 as bytes are multiplied by it (a float, not a division).
const INV_255: f32 = 1.0 / 255.0;

/// One level of the chain: RGBA texels, rows bottom-up. A level `scale`d by 1/255 holds bytes
/// (integer values 0..255); every other level holds its values as they are.
#[derive(Clone)]
pub(super) struct Level {
    pub width: u32,
    pub height: u32,
    pub texels: Vec<[f32; 4]>,
    scale: f32,
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

/// How the top level enters the chain.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(super) enum Top {
    /// 8-bit texels kept as bytes (a linear texture, or any texture without mips).
    Bytes,
    /// 8-bit sRGB colour decoded to linear float; alpha times 1/255.
    Srgb,
    /// 8-bit texels converted to float (times 1/255).
    Unorm,
}

/// How a level becomes the texture's data.
#[derive(Debug, Clone, Copy, Default)]
pub(super) struct Encoding {
    /// Normals made from height: `(heightScale, sobel)`.
    pub height: Option<(f32, bool)>,
    /// A normal map (from height or not).
    pub normal_map: bool,
    /// The target stores normal maps as DXT5nm (1, y, y, x) rather than (x, y, z, 1).
    pub dxt5nm: bool,
    /// `flipGreenChannel` of a normal map.
    pub flip_green: bool,
    /// Linear colour back to gamma (an sRGB texture).
    pub srgb: bool,
    /// `mipmapFadeDistanceStart` and `End`.
    pub fade: Option<(i32, i32)>,
}

impl Level {
    pub fn decode(width: u32, height: u32, data: &[u8], format: TextureFormat, top: Top) -> Self {
        let (texels, scale) = match format {
            TextureFormat::Rgba8 => {
                let bytes = data.as_chunks::<4>().0.iter();
                match top {
                    Top::Bytes => (bytes.map(|p| p.map(|v| v as f32)).collect(), INV_255),
                    Top::Srgb => {
                        let table = srgb_to_linear();
                        let texels = bytes
                            .map(|p| {
                                std::array::from_fn(|c| {
                                    if c < 3 {
                                        table[p[c] as usize]
                                    } else {
                                        p[c] as f32 * INV_255
                                    }
                                })
                            })
                            .collect();
                        (texels, 1.0)
                    }
                    Top::Unorm => (bytes.map(|p| p.map(|v| v as f32 * INV_255)).collect(), 1.0),
                }
            }
            TextureFormat::Rgba32F => (
                data.as_chunks::<16>()
                    .0
                    .iter()
                    .map(|p| std::array::from_fn(|c| read_f32(&p[c * 4..c * 4 + 4])))
                    .collect(),
                1.0,
            ),
        };
        Level {
            width,
            height,
            texels,
            scale,
        }
    }

    fn bytes(&self) -> bool {
        self.scale != 1.0
    }

    /// The texels as floats (a byte level times 1/255).
    fn values(&self) -> Vec<[f32; 4]> {
        if self.bytes() {
            self.texels
                .iter()
                .map(|t| t.map(|v| v * self.scale))
                .collect()
        } else {
            self.texels.clone()
        }
    }

    /// Level `index` of the chain in `format`.
    pub fn encode(&self, index: usize, format: TextureFormat, encoding: &Encoding) -> Vec<u8> {
        if self.bytes() {
            return self.encode_bytes(encoding);
        }
        let mut texels = match encoding.height {
            Some((scale, sobel)) => normals_from_height(self, scale, sobel, encoding.dxt5nm),
            None => self.texels.clone(),
        };
        if encoding.normal_map && encoding.flip_green {
            texels.iter_mut().for_each(|t| t[1] = 1.0 - t[1]);
        }
        if encoding.srgb {
            texels
                .iter_mut()
                .for_each(|t| (0..3).for_each(|c| t[c] = linear_to_gamma(t[c])));
        }
        if encoding.normal_map && encoding.dxt5nm && encoding.height.is_none() {
            texels.iter_mut().for_each(|t| *t = [1.0, t[1], t[1], t[0]]);
        }
        if let Some((start, end)) = encoding.fade
            && index > 0
            && index as i32 >= start
        {
            fade(&mut texels, index as i32, start, end, encoding);
        }
        let mut out = Vec::with_capacity(texels.len() * format.bytes_per_pixel());
        for texel in &texels {
            match format {
                TextureFormat::Rgba8 => out.extend(texel.map(|v| quantize((v * 255.0) as f64))),
                TextureFormat::Rgba32F => texel
                    .iter()
                    .for_each(|v| out.extend_from_slice(&v.to_le_bytes())),
            }
        }
        out
    }

    /// A byte level is stored as it is: flip green is 255 - green and DXT5nm a swizzle.
    fn encode_bytes(&self, encoding: &Encoding) -> Vec<u8> {
        self.texels
            .iter()
            .flat_map(|t| {
                let mut p = t.map(|v| v as u8);
                if encoding.normal_map && encoding.flip_green {
                    p[1] = 255 - p[1];
                }
                if encoding.normal_map && encoding.dxt5nm {
                    p = [255, p[1], p[1], p[0]];
                }
                p
            })
            .collect()
    }
}

/// A 0..255 value rounded to the nearest byte (halves up).
pub(super) fn quantize(v: f64) -> u8 {
    ((v + 0.5) as f32).clamp(0.0, 255.0) as u8
}

/// The sRGB-to-linear decoding table of the 256 byte values, in float.
fn srgb_to_linear() -> &'static [f32; 256] {
    static TABLE: OnceLock<[f32; 256]> = OnceLock::new();
    TABLE.get_or_init(|| {
        std::array::from_fn(|i| {
            let v = i as f32 * INV_255;
            if v <= 0.04045 {
                v / 12.92
            } else {
                ((v + 0.055) / 1.055).powf(2.4)
            }
        })
    })
}

/// Linear to sRGB gamma of one channel, in float.
fn linear_to_gamma(v: f32) -> f32 {
    // The exponents are one float step above the nearest to 1/2.4 and 1/2.2 (as measured).
    const LDR: f32 = f32::from_bits(0x3ed5_5556);
    const HDR: f32 = f32::from_bits(0x3ee8_ba2f);
    if v <= 0.0 {
        0.0
    } else if v <= 0.003_130_8 {
        v * 12.92
    } else if v < 1.0 {
        v.powf(LDR) * 1.055 - 0.055
    } else if v == 1.0 {
        1.0
    } else {
        v.powf(HDR)
    }
}

/// The mip fade: level `index` moves `(index - start) / (end - start)` of the way to
/// mid-gray, or to the flat normal of the target's layout. DXT5nm keeps red (always 1) and fades
/// alpha (x); other layouts keep alpha.
fn fade(texels: &mut [[f32; 4]], index: i32, start: i32, end: i32, encoding: &Encoding) {
    let t = if end == start {
        1.0
    } else {
        let t = (index - start) as f32 / (end - start) as f32;
        if t < 0.0 { 0.0 } else { t.min(1.0) }
    };
    let dxt5nm = encoding.normal_map && encoding.dxt5nm;
    let flat = !encoding.normal_map || encoding.dxt5nm;
    let (blue, alpha) = if flat { (0.5, 0.5) } else { (1.0, 0.0) };
    let (keep, half, blue, alpha) = (1.0 - t, 0.5 * t, blue * t, alpha * t);
    for texel in texels {
        if !dxt5nm {
            texel[0] = texel[0] * keep + half;
        }
        texel[1] = texel[1] * keep + half;
        texel[2] = texel[2] * keep + blue;
        if dxt5nm {
            texel[3] = texel[3] * keep + alpha;
        }
    }
}

/// `top` and the levels below it down to 1x1 (each side halves, rounding down, at least 1).
pub(super) fn chain(top: Level, options: &Options) -> Vec<Level> {
    let mut levels = vec![top];
    while let Some(last) = levels.last().filter(|l| l.width > 1 || l.height > 1) {
        let mut next = if last.width % 2 == 1 || last.height % 2 == 1 {
            bilinear(last)
        } else if options.filter == Filter::Kaiser {
            kaiser(last)
        } else {
            halve_box(last)
        };
        if options.border {
            border(last, &mut next);
        }
        if let Some(reference) = options.coverage {
            preserve_coverage(last, &mut next, reference);
        }
        levels.push(next);
    }
    levels
}

fn half_size(level: &Level) -> (usize, usize) {
    (
        (level.width as usize / 2).max(1),
        (level.height as usize / 2).max(1),
    )
}

fn float_level(width: usize, height: usize, texels: Vec<[f32; 4]>) -> Level {
    Level {
        width: width as u32,
        height: height as u32,
        texels,
        scale: 1.0,
    }
}

/// Box halving: ((p00 + p10) + p01) + p11, times 1/4 (and 1/255 for bytes).
fn halve_box(src: &Level) -> Level {
    let (w, (dw, dh)) = (src.width as usize, half_size(src));
    let scale = 0.25 * src.scale;
    let mut texels = Vec::with_capacity(dw * dh);
    for y in 0..dh {
        for x in 0..dw {
            let at = |dx: usize, dy: usize| src.texels[(2 * y + dy) * w + 2 * x + dx];
            let (p00, p10, p01, p11) = (at(0, 0), at(1, 0), at(0, 1), at(1, 1));
            texels.push(std::array::from_fn(|c| {
                (((p00[c] + p10[c]) + p01[c]) + p11[c]) * scale
            }));
        }
    }
    float_level(dw, dh, texels)
}

/// Kaiser halving: the Kaiser kernel over the 8 texels around each pair (edges mirrored),
/// x then y, each pass accumulating in float.
fn kaiser(src: &Level) -> Level {
    let (w, h) = (src.width as usize, src.height as usize);
    let (dw, dh) = half_size(src);
    let weights = kaiser_weights();
    let taps = |n: usize, x: usize| -> [usize; 8] {
        std::array::from_fn(|k| mirror(2 * x as isize - 3 + k as isize, n))
    };
    // x: bytes times 1/255 after each product (the 8-bit pass).
    let mut rows = Vec::with_capacity(dw * h);
    for y in 0..h {
        for x in 0..dw {
            let mut acc = [0f32; 4];
            for (k, i) in taps(w, x).into_iter().enumerate() {
                let v = src.texels[y * w + i];
                (0..4).for_each(|c| acc[c] += weights[k] * v[c] * src.scale);
            }
            rows.push(acc);
        }
    }
    let mut texels = Vec::with_capacity(dw * dh);
    for y in 0..dh {
        let taps = taps(h, y);
        for x in 0..dw {
            let mut acc = [0f32; 4];
            for (k, &j) in taps.iter().enumerate() {
                let v = rows[j * dw + x];
                (0..4).for_each(|c| acc[c] += weights[k] * v[c]);
            }
            texels.push(acc);
        }
    }
    float_level(dw, dh, texels)
}

/// A texel index mirrored into 0..n (the edge texel repeats).
fn mirror(mut i: isize, n: usize) -> usize {
    let n = n as isize;
    while i < 0 || i >= n {
        i = if i < 0 {
            let j = -i - 1;
            if j < n { j } else { 2 * n - 1 - j }
        } else {
            2 * n - 1 - i
        };
    }
    i as usize
}

/// Kaiser-windowed sinc (width 2, alpha 4) at source offsets -3.5 .. 3.5, computed in double,
/// rounded to float and normalized by their float sum.
fn kaiser_weights() -> &'static [f32; 8] {
    static WEIGHTS: OnceLock<[f32; 8]> = OnceLock::new();
    WEIGHTS.get_or_init(|| {
        fn bessel0(half: f64) -> f64 {
            let (mut sum, mut term, mut i) = (1.0, 1.0, 1.0);
            loop {
                term *= half / i;
                let square = term * term;
                sum += square;
                i += 1.0;
                if square <= sum * 1e-16 {
                    return sum;
                }
            }
        }
        // pi is the float one, widened to double.
        const PI: f64 = std::f32::consts::PI as f64;
        let raw: [f32; 8] = std::array::from_fn(|k| {
            let x = (k as f32 - 4.0) + 0.5;
            let half = x * 0.5;
            let sinc = if half != 0.0 {
                let d = half as f64 * PI;
                d.sin() / d
            } else {
                1.0
            };
            let t = x as f64 * 0.25;
            let window = bessel0((1.0 - t * t).sqrt() * 4.0 * 0.5) / bessel0(2.0);
            (sinc * window) as f32
        });
        let sum = raw.iter().fold(0f32, |s, &w| s + w);
        raw.map(|w| w / sum)
    })
}

/// 16.16 fixed-point positions of the destination texels along one axis:
/// the first position and the step.
fn fixed_steps(n: usize, dn: usize) -> (u32, u32) {
    if dn < n {
        let step = ((n as u32) << 16) / dn as u32;
        ((step >> 1).wrapping_sub(0x8000), step)
    } else {
        let divisor = if dn == 1 { 1 } else { dn as u32 - 1 };
        (0, (((n as u32) << 16) - 0x10000) / divisor)
    }
}

/// A level with an odd side halved by resampling: bilinear in 16.16 fixed point, the next row
/// and column clamped at the last, interpolated in float.
fn bilinear(src: &Level) -> Level {
    let (w, h) = (src.width as usize, src.height as usize);
    let (dw, dh) = half_size(src);
    let values = src.values();
    let (x0, x_step) = fixed_steps(w, dw);
    let (y0, y_step) = fixed_steps(h, dh);
    const FRACTION: f32 = 1.0 / 65536.0;
    let mut texels = Vec::with_capacity(dw * dh);
    let mut y_pos = y0;
    for _ in 0..dh {
        let row = (y_pos >> 16) as usize;
        let next_row = if row == h - 1 { row } else { row + 1 };
        let ty = (y_pos & 0xffff) as f32 * FRACTION;
        let mut x_pos = x0;
        for x in 0..dw {
            let i = (x_pos >> 16) as usize;
            // Only the last texel of a row is clamped.
            let j = if w <= 1 {
                i
            } else if x + 1 < dw || i + 1 < w {
                i + 1
            } else {
                i
            };
            let tx = (x_pos & 0xffff) as f32 * FRACTION;
            let lerp = |r: usize| -> [f32; 4] {
                let (a, b) = (values[r * w + i], values[r * w + j]);
                std::array::from_fn(|c| a[c] * (1.0 - tx) + b[c] * tx)
            };
            let (top, bottom) = (lerp(row), lerp(next_row));
            texels.push(std::array::from_fn(|c| {
                top[c] * (1.0 - ty) + bottom[c] * ty
            }));
            x_pos = x_pos.wrapping_add(x_step);
        }
        y_pos = y_pos.wrapping_add(y_step);
    }
    float_level(dw, dh, texels)
}

/// Border mip maps: the edge rows, then the edge columns (which win at the corners), become the
/// mean of texels 2i and 2i + 1 of the level above's edge.
fn border(src: &Level, dst: &mut Level) {
    let (w, h) = (src.width as usize, src.height as usize);
    let (dw, dh) = (dst.width as usize, dst.height as usize);
    let pair = |a: [f32; 4], b: [f32; 4]| -> [f32; 4] {
        std::array::from_fn(|c| (a[c] + b[c]) * 0.5 * src.scale)
    };
    for (dy, sy) in [(0, 0), (dh - 1, h - 1)] {
        for x in 0..dw {
            let (a, b) = ((2 * x).min(w - 1), (2 * x + 1).min(w - 1));
            dst.texels[dy * dw + x] = pair(src.texels[sy * w + a], src.texels[sy * w + b]);
        }
    }
    for (dx, sx) in [(0, 0), (dw - 1, w - 1)] {
        for y in 0..dh {
            let (a, b) = ((2 * y).min(h - 1), (2 * y + 1).min(h - 1));
            dst.texels[y * dw + dx] = pair(src.texels[a * w + sx], src.texels[b * w + sx]);
        }
    }
}

/// The preserve-coverage step: when the level passes the alpha test (alpha above the
/// reference) less often than the level above, the alpha threshold is bisected between 0 and the
/// reference until the coverages agree within 0.5% or the bounds meet, and alpha is scaled by
/// reference / threshold (at most 1). A level that cannot reach the coverage keeps its alpha.
fn preserve_coverage(src: &Level, dst: &mut Level, reference: f32) {
    if !(reference > 0.0 && reference < 1.0) {
        return;
    }
    const CLOSE: f32 = 1.005;
    let count = |texels: &[[f32; 4]], scale: f32, threshold: f32| {
        texels.iter().filter(|t| t[3] * scale > threshold).count() as f32
    };
    let ratio = |a: f32, b: f32| a.max(b) / a.min(b);
    let desired = (1.0 / src.texels.len() as f32) * count(&src.texels, src.scale, reference);
    let per_texel = 1.0 / dst.texels.len() as f32;
    let coverage = |threshold: f32| count(&dst.texels, 1.0, threshold) * per_texel;
    let current = coverage(reference);
    if current >= desired {
        return;
    }
    let most = coverage(0.0);
    if desired >= most || !(ratio(current, most) >= CLOSE) {
        return;
    }
    // `high` passes too few texels, `low` enough; the coverages at the bounds.
    let (mut high, mut low) = (reference, 0f32);
    let (mut at_high, mut at_low) = (current, most);
    loop {
        if !(ratio(high, low) >= CLOSE) {
            break;
        }
        let mid = (high + low) * 0.5;
        let at_mid = coverage(mid);
        if CLOSE > ratio(at_mid, desired) {
            low = mid;
            break;
        }
        if desired < at_mid {
            at_low = at_mid;
        }
        let spread = if at_mid <= desired {
            high = mid;
            at_high = at_mid;
            ratio(at_mid, at_low)
        } else {
            low = mid;
            ratio(at_high, at_low)
        };
        if !(spread >= CLOSE) {
            break;
        }
    }
    if low == 0.0 {
        return;
    }
    let scale = reference / low;
    dst.texels.iter_mut().for_each(|t| {
        let a = t[3] * scale;
        t[3] = if a > 1.0 { 1.0 } else { a };
    });
}

/// Normal map from height: height is the sum of red, green and blue times
/// `(10^heightScale * 0.01 - 0.01) * (width + height) / 3`; the slope is the central difference
/// (times 4) or the Sobel sum, wrapping at the edges. The normal is (-dx, -dy, 1) normalized,
/// stored as DXT5nm (1, y, y, x) or as (x, y, z, 1), each component times 0.5 plus 0.5.
fn normals_from_height(
    level: &Level,
    height_scale: f32,
    sobel: bool,
    dxt5nm: bool,
) -> Vec<[f32; 4]> {
    let (w, h) = (level.width as usize, level.height as usize);
    let k = 10f32.powf(height_scale) * 0.01 + -0.01;
    let k = (w + h) as f32 * k / 3.0;
    let heights: Vec<f32> = level
        .values()
        .iter()
        .map(|t| ((t[0] + t[1]) + t[2]) * k)
        .collect();
    let at = |x: isize, y: isize| {
        heights[y.rem_euclid(h as isize) as usize * w + x.rem_euclid(w as isize) as usize]
    };
    let mut out = Vec::with_capacity(w * h);
    for y in 0..h as isize {
        for x in 0..w as isize {
            let (dx, dy) = if sobel {
                let (a, c) = (at(x - 1, y - 1), at(x - 1, y + 1));
                let (d, e) = (at(x + 1, y - 1), at(x + 1, y + 1));
                let left = ((at(x - 1, y) * -2.0 - a) - c) + d;
                let dx = ((at(x + 1, y) + at(x + 1, y)) + left) + e;
                let below = ((at(x, y - 1) * -2.0 - a) - d) + c;
                let dy = ((at(x, y + 1) + at(x, y + 1)) + below) + e;
                (dx, dy)
            } else {
                (
                    at(x + 1, y) * 4.0 - at(x - 1, y) * 4.0,
                    at(x, y + 1) * 4.0 - at(x, y - 1) * 4.0,
                )
            };
            let length = ((dy * dy + dx * dx) + 1.0).sqrt();
            let (nx, ny) = (-dx / length, -dy / length);
            let encode = |v: f32| (v + 1.0) * 0.5;
            out.push(if dxt5nm {
                [1.0, encode(ny), encode(ny), encode(nx)]
            } else {
                [encode(nx), encode(ny), encode(1.0 / length), 1.0]
            });
        }
    }
    out
}
