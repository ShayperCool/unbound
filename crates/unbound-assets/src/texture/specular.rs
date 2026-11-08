//! The "Specular (Glossy Reflection)" convolution of an imported cubemap (`cubemapConvolution: 1`),
//! as the Unity 6000.0.71f1 Editor computes it (tests/projects/reflectionprobes `import.*`).
//!
//! Mip 0 is the image. Mip `i` of the first `n = min(mip count, 7)` levels is the image filtered
//! with a GGX lobe of perceptual roughness `i / (n - 1)` around each texel's direction (the view
//! along the normal, so the lobe is centred on it); the levels past `n` are box-filtered from the
//! last convolved one. Each convolved texel either
//!
//! * averages `N = min(256·2^i, 4096)` directions drawn from the lobe (filtered importance
//!   sampling): stratified in azimuth and, through a fixed permutation, in the lobe's polar
//!   coordinate, each stratum jittered by fixed pseudo-random tables and the whole set turned per
//!   texel; every direction reads a box-filtered level of the image chosen from the lobe's density
//!   there (trilinear, bilinear within a face, faces joined through a one-texel border), or
//! * when a small enough level resolves the lobe and this is not the roughest level, sums that
//!   whole level weighted by the lobe and each texel's solid angle.
//!
//! The pseudo-random tables come from a fixed xorshift128 sequence, so the result is the same on
//! every run. Only the solid-angle weights of the second path differ by a fraction of a percent:
//! they use an exact reciprocal square root.
use super::{Image, TextureFormat, read_f32};
use rayon::prelude::*;
use std::sync::OnceLock;

/// Levels convolved at most: their roughness runs from 0 to 1 in equal steps.
const MAX_CONVOLVED_LEVELS: u32 = 7;
/// Directions averaged for level 1; each further level doubles them up to [`MAX_SAMPLES`].
const BASE_SAMPLES: u32 = 256;
const MAX_SAMPLES: u32 = 4096;
/// Entries of each pseudo-random table (indices wrap at this power of two).
const TABLE_LEN: usize = 4096;
const TABLE_MASK: u32 = TABLE_LEN as u32 - 1;
/// The xorshift128 state the tables start from.
const XORSHIFT_SEED: [u32; 4] = [0x8707_42b3, 0x7f74_1ba0, 0xacf7_8621, 0xadb8_e840];
/// A table entry as a fraction of one stratum.
const UNIT_FRACTION: f32 = 1.0 / 65536.0;
const TAU: f32 = std::f32::consts::TAU;
const INV_TAU: f32 = 1.0 / std::f32::consts::TAU;
const PI: f32 = std::f32::consts::PI;
/// The smallest density/extent a logarithm or a division sees (one float epsilon).
const EPSILON: f32 = f32::EPSILON;
/// Levels below this many (`mip count - 6`) are never summed whole.
const WHOLE_LEVEL_FLOOR: i32 = 6;

/// The fixed pseudo-random tables: the first 4096 outputs of the generator (its seed's last word
/// first) and the next 4096, each as its low 16 bits.
struct Tables {
    first: [u16; TABLE_LEN],
    second: [u16; TABLE_LEN],
}

fn tables() -> &'static Tables {
    static TABLES: OnceLock<Tables> = OnceLock::new();
    TABLES.get_or_init(|| {
        let [mut x, mut y, mut z, mut w] = XORSHIFT_SEED;
        let mut next = || {
            let t = x ^ (x << 11);
            (x, y, z) = (y, z, w);
            w = w ^ (w >> 19) ^ t ^ (t >> 8);
            w as u16
        };
        let mut first = [0u16; TABLE_LEN];
        first[0] = XORSHIFT_SEED[3] as u16;
        first[1..].iter_mut().for_each(|v| *v = next());
        let second = std::array::from_fn(|_| next());
        Tables { first, second }
    })
}

fn sample_count(level: u32) -> u32 {
    (BASE_SAMPLES << level).min(MAX_SAMPLES)
}

/// The two stratum permutations of a sample count (0..n shuffled by each table).
struct Strata {
    polar: Vec<u16>,
    order: Vec<u16>,
}

fn strata(level: u32) -> &'static Strata {
    static STRATA: OnceLock<Vec<Strata>> = OnceLock::new();
    let all = STRATA.get_or_init(|| {
        let t = tables();
        let shuffled = |n: usize, table: &[u16; TABLE_LEN]| {
            let mut p: Vec<u16> = (0..n as u16).collect();
            for j in 0..n {
                let k = j + table[j] as usize % (n - j);
                p.swap(j, k);
            }
            p
        };
        (0..MAX_CONVOLVED_LEVELS)
            .map(|level| {
                let n = sample_count(level) as usize;
                Strata {
                    polar: shuffled(n, &t.first),
                    order: shuffled(n, &t.second),
                }
            })
            .collect()
    });
    &all[level as usize]
}

/// The unnormalised direction of a face position (`sc`, `tc` in -1..1 from the face's top-left),
/// the major axis ±1 (Unity's cube layout).
fn face_vector<T: Copy + std::ops::Neg<Output = T> + From<i8>>(
    face: usize,
    sc: T,
    tc: T,
) -> [T; 3] {
    let one = T::from(1);
    match face {
        0 => [one, -tc, -sc],
        1 => [-one, -tc, sc],
        2 => [sc, one, tc],
        3 => [sc, -one, -tc],
        4 => [sc, -tc, one],
        _ => [-sc, -tc, -one],
    }
}

/// A level of every face with a one-texel border taken from the neighbouring faces, so that
/// bilinear reads at a face's edge see across it.
struct Level {
    size: usize,
    faces: [Vec<[f32; 4]>; 6],
}

impl Level {
    fn stride(&self) -> usize {
        self.size + 2
    }

    fn at(&self, face: usize, x: usize, y: usize) -> [f32; 4] {
        self.faces[face][y * self.stride() + x]
    }

    fn bordered(texels: &[Vec<[f32; 4]>; 6], size: usize) -> Level {
        let stride = size + 2;
        let faces = std::array::from_fn(|face| {
            let mut out = vec![[0.0f32; 4]; stride * stride];
            for y in 0..size {
                out[(y + 1) * stride + 1..(y + 1) * stride + 1 + size]
                    .copy_from_slice(&texels[face][y * size..(y + 1) * size]);
            }
            let border = |i: isize| -> f64 {
                if i < 0 {
                    -1.0 - 1e-3 / size as f64
                } else if i >= size as isize {
                    1.0 + 1e-3 / size as f64
                } else {
                    (i as f64 + 0.5) / size as f64 * 2.0 - 1.0
                }
            };
            let edge = (0..size as isize)
                .flat_map(|i| [(i, -1), (i, size as isize), (-1, i), (size as isize, i)]);
            for (x, y) in edge {
                let (f, nx, ny) = texel_of(face_vector(face, border(x), border(y)), size);
                out[((y + 1) as usize) * stride + (x + 1) as usize] = texels[f][ny * size + nx];
            }
            // A corner: the mean of the two border texels beside it.
            for (cx, cy) in [(0, 0), (size + 1, 0), (0, size + 1), (size + 1, size + 1)] {
                let ax = if cx == 0 { 1 } else { size };
                let ay = if cy == 0 { 1 } else { size };
                let (a, b) = (out[cy * stride + ax], out[ay * stride + cx]);
                out[cy * stride + cx] = std::array::from_fn(|c| (a[c] + b[c]) * 0.5);
            }
            out
        });
        Level { size, faces }
    }
}

/// The face texel a direction lands on.
fn texel_of(d: [f64; 3], size: usize) -> (usize, usize, usize) {
    let [x, y, z] = d;
    let (ax, ay, az) = (x.abs(), y.abs(), z.abs());
    let (face, sc, tc, ma) = if ay >= ax && ay >= az {
        if y > 0.0 {
            (2, x, z, ay)
        } else {
            (3, x, -z, ay)
        }
    } else if az >= ax {
        if z > 0.0 {
            (4, x, -y, az)
        } else {
            (5, -x, -y, az)
        }
    } else if x > 0.0 {
        (0, -z, -y, ax)
    } else {
        (1, z, -y, ax)
    };
    let index = |c: f64| (((c / ma + 1.0) * 0.5 * size as f64) as usize).min(size - 1);
    (face, index(sc), index(tc))
}

/// The box-filtered chain of the faces, mip 0 first (each level the mean of 2x2 texels).
fn box_chain(faces: [Vec<[f32; 4]>; 6], size: usize) -> Vec<([Vec<[f32; 4]>; 6], usize)> {
    let mut chain = vec![(faces, size)];
    while chain.last().unwrap().1 > 1 {
        let (prev, s) = chain.last().unwrap();
        let half = s / 2;
        let next = std::array::from_fn(|f| downsample(&prev[f], *s));
        chain.push((next, half));
    }
    chain
}

fn downsample(texels: &[[f32; 4]], size: usize) -> Vec<[f32; 4]> {
    let half = size / 2;
    let mut out = Vec::with_capacity(half * half);
    for y in 0..half {
        for x in 0..half {
            let mut sum = [0.0f32; 4];
            for (dx, dy) in [(0, 0), (1, 0), (0, 1), (1, 1)] {
                let p = texels[(2 * y + dy) * size + 2 * x + dx];
                (0..4).for_each(|c| sum[c] += p[c] * 0.25);
            }
            out.push(sum);
        }
    }
    out
}

/// What every texel of one convolved level shares.
struct LevelParameters {
    /// α² of the GGX lobe.
    a2: f32,
    samples: u32,
    /// Half the log₂ of a texel's share of the samples' solid angle at mip 0.
    lod_bias: f32,
    /// The finest mip a sample reads (where the lobe's core spans about one texel).
    min_lod: f32,
    /// The level summed whole instead of sampled, if any.
    whole_level: Option<usize>,
}

impl LevelParameters {
    fn new(level: u32, convolved: u32, size: u32, mip_count: u32) -> Self {
        let samples = sample_count(level);
        let t = level as f32 / (convolved - 1) as f32;
        let alpha = t * t;
        let a2 = alpha * alpha;
        // The polar angle holding all but the outermost of the sqrt(N)/2 polar strata.
        let k = ((samples as f32).sqrt() * 0.5) as i32 as f32;
        let c = (k - 0.5) / k;
        let cos_core = (c / (c * (1.0 - a2) + a2)).sqrt();
        let sin_core = (1.0 - cos_core * cos_core).max(0.0).sqrt();
        let extent = (2.0 * (sin_core / cos_core)).max(EPSILON);
        let min_lod = ((1.0 / extent).ln() * -std::f32::consts::LOG2_E + mip_count as f32).max(0.0);
        // Integer division, as the Editor computes it (a large sample count gives -inf: every
        // sample then reads the finest level the lobe allows).
        let share = (size * size) / (samples * 4);
        let lod_bias = 0.5 * ((share as f32).ln() * std::f32::consts::LOG2_E);
        let finest = mip_count as i32 - WHOLE_LEVEL_FLOOR;
        let pick = (min_lod.floor() as i32 - 1).clamp(0, finest.max(0));
        let whole_texels = 6 * (size as i64 >> pick).pow(2);
        let whole_level =
            (finest >= 0 && whole_texels <= 8 * samples as i64 && level < convolved - 1)
                .then_some(pick as usize);
        Self {
            a2,
            samples,
            lod_bias,
            min_lod,
            whole_level,
        }
    }
}

/// `log2(v)` as the convolution approximates it (a quadratic over the mantissa).
fn approx_log2(v: f32) -> f32 {
    let v = v.max(EPSILON);
    let bits = v.to_bits() as i32;
    let m = f32::from_bits(((bits & 0x807f_ffff_u32 as i32) | 0x3f80_0000) as u32) - 1.0;
    m * 1.437_907_1 + (bits >> 23) as f32 - 127.0 + (m * 0.194_381_13 - 0.632_288_2) * m * m
}

/// `sin(2πx)` for |x| ≤ 1/4 as the convolution approximates it (an odd polynomial).
fn approx_sin_turns(x: f32) -> f32 {
    let x2 = x * x;
    let x4 = x2 * x2;
    (x4 * x4 * 39.657_032 + (x2 * -76.568_62 + 81.601_82) * x4 + x2 * -41.341_66 + 6.283_185) * x
}

/// The face, and the position on it in texels of a level of `size`, a direction reads.
fn locate(d: [f32; 3], size: f32) -> (usize, f32, f32) {
    let [x, y, z] = d;
    let (ax, ay, az) = (x.abs(), y.abs(), z.abs());
    let z_major = ax <= az && ay <= az;
    let y_major = az <= ay && ax <= ay;
    let (major, base) = if y_major {
        (y, 2)
    } else if z_major {
        (z, 4)
    } else {
        (x, 0)
    };
    let face = base + (major < 0.0) as usize;
    let sign = if major >= 0.0 { -1.0 } else { 1.0 };
    let (u, v) = if y_major {
        (x, sign * z)
    } else if z_major {
        (-(x * sign), y)
    } else {
        (sign * z, y)
    };
    let m = major.abs().max(EPSILON);
    let u = ((u / m) * 0.5 + 0.5).clamp(0.0, 1.0);
    let v = ((v / m) * -0.5 + 0.5).clamp(0.0, 1.0);
    (face, u * size - 0.5, v * size - 0.5)
}

fn bilinear(level: &Level, face: usize, px: f32, py: f32) -> [f32; 4] {
    // Bordered coordinates: texel i sits at i + 1.
    let ix = (px + 1.0) as usize;
    let iy = (py + 1.0) as usize;
    let fx = px - (ix as f32 - 1.0);
    let fy = py - (iy as f32 - 1.0);
    let (p00, p10) = (level.at(face, ix, iy), level.at(face, ix + 1, iy));
    let (p01, p11) = (level.at(face, ix, iy + 1), level.at(face, ix + 1, iy + 1));
    let (w00, w10) = ((1.0 - fx) * (1.0 - fy), fx * (1.0 - fy));
    let (w01, w11) = ((1.0 - fx) * fy, fx * fy);
    // A texel of zero weight is left out (an infinite one would make the read NaN).
    let mut out = [0.0f32; 4];
    for (p, w) in [(p00, w00), (p10, w10), (p01, w01), (p11, w11)] {
        if w != 0.0 {
            (0..4).for_each(|c| out[c] += p[c] * w);
        }
    }
    out
}

/// A unit vector orthogonal to `n`, from the axis `n` leans on least.
fn tangent(n: [f32; 3]) -> [f32; 3] {
    let (ax, ay, az) = (n[0].abs(), n[1].abs(), n[2].abs());
    let t = if ay >= ax && az >= ax {
        [0.0, -n[2], n[1]]
    } else if az >= ay {
        [n[2], 0.0, -n[0]]
    } else {
        [-n[1], n[0], 0.0]
    };
    let l = (t[0] * t[0] + t[1] * t[1] + t[2] * t[2]).sqrt();
    t.map(|c| c / l)
}

fn cross(a: [f32; 3], b: [f32; 3]) -> [f32; 3] {
    [
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    ]
}

/// The lobe's average around `n` from importance-sampled directions (texel `x`, `y` of its face).
fn sampled(
    chain: &[Level],
    p: &LevelParameters,
    level: u32,
    n: [f32; 3],
    x: u32,
    y: u32,
) -> [f32; 4] {
    let t = tables();
    let strata = strata(level);
    let samples = p.samples;
    let top = (chain.len() - 1) as f32;
    let tan = tangent(n);
    let bit = cross(tan, n);
    // The texel's turn of the whole set, within one azimuth stratum.
    let turn = t.second[((t.first[(y & TABLE_MASK) as usize] as u32 + x) & TABLE_MASK) as usize]
        as f32
        * UNIT_FRACTION
        * (TAU / samples as f32);
    let (sr, cr) = (turn.sin(), turn.cos());
    let rotated: [f32; 3] = std::array::from_fn(|c| tan[c] * cr - bit[c] * sr);
    let across: [f32; 3] = std::array::from_fn(|c| tan[c] * sr + bit[c] * cr);
    let offset =
        t.second[((t.first[(x & TABLE_MASK) as usize] as u32 + y) & TABLE_MASK) as usize] as u32;
    let mut sum = [0.0f32; 4];
    for k in 0..samples {
        let index = strata.order[k as usize] as u32 + offset;
        let jitter = (index & TABLE_MASK) as usize;
        let azimuth = ((t.first[jitter] as f32 * UNIT_FRACTION + k as f32)
            * (TAU / samples as f32))
            * INV_TAU;
        let polar = (t.second[jitter] as f32 * UNIT_FRACTION
            + strata.polar[(index & (samples - 1)) as usize] as f32)
            * (1.0 / samples as f32);
        let cos2 = polar / ((1.0 - p.a2) * polar + p.a2);
        let cos_t = cos2.sqrt();
        let sin_t = (1.0 - cos_t * cos_t).sqrt();
        let along = approx_sin_turns(0.25 - (azimuth - azimuth.round_ties_even()).abs()) * sin_t;
        let shifted = azimuth - 0.25;
        let side = approx_sin_turns(0.25 - (shifted - shifted.round_ties_even()).abs()) * sin_t;
        let d: [f32; 3] =
            std::array::from_fn(|c| n[c] * cos_t + rotated[c] * side + across[c] * along);
        let denom = cos_t * cos_t * (p.a2 - 1.0) + 1.0;
        let density = (cos_t * p.a2) / (denom * PI * denom);
        let major = d[0].abs().max(d[1].abs()).max(d[2].abs());
        let lod = p.lod_bias - approx_log2(density * major * major * major) * 0.5;
        let lod = lod.max(p.min_lod).max(0.0).min(top);
        let l0 = lod as usize;
        let l1 = (l0 + 1).min(chain.len() - 1);
        let f = lod - l0 as f32;
        let read = |l: usize| {
            let (face, px, py) = locate(d, chain[l].size as f32);
            bilinear(&chain[l], face, px, py)
        };
        // A level of zero weight is not read (an infinite texel would make it NaN).
        let a = read(l0);
        if f > 0.0 {
            let b = read(l1);
            (0..4).for_each(|c| sum[c] += a[c] * (1.0 - f) + b[c] * f);
        } else {
            (0..4).for_each(|c| sum[c] += a[c]);
        }
    }
    sum.map(|c| c / samples as f32)
}

/// The lobe's average around `n` over every texel of one level, weighted by the lobe and the
/// texel's solid angle.
fn whole(level: &Level, a2: f32, n: [f32; 3]) -> [f32; 4] {
    let s = level.size;
    let inv = 1.0 / s as f32;
    let mut sum = [0.0f32; 4];
    let mut weights = 0.0f32;
    for face in 0..6 {
        for y in 0..s {
            let tc = -(1.0 - ((y as f32 + 0.5) * inv) * 2.0);
            for x in 0..s {
                let sc = ((x as f32 + 0.5) * inv) * 2.0 - 1.0;
                let v = face_vector(face, sc, tc);
                let r = 1.0 / (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt();
                let c = (n[0] * v[0] + n[1] * v[1] + n[2] * v[2]) * r;
                if c <= 0.0 {
                    continue;
                }
                let denom = c * c * (a2 - 1.0) + 1.0;
                let w = (a2 * c) / (denom * PI * denom) * (r * r * r);
                let p = level.at(face, x + 1, y + 1);
                (0..4).for_each(|i| sum[i] += p[i] * w);
                weights += w;
            }
        }
    }
    sum.map(|c| c / weights)
}

/// Each face's mip chain, glossy-convolved. `faces`: six square faces of linear RGBA floats
/// (Unity's order and orientation, top-down). Every level down to 1x1 is returned.
pub(super) fn convolve(faces: &[Image]) -> Vec<Vec<Image>> {
    let size = faces[0].width;
    let texels: [Vec<[f32; 4]>; 6] = std::array::from_fn(|f| {
        faces[f]
            .data
            .as_chunks::<16>()
            .0
            .iter()
            .map(|p| std::array::from_fn(|c| read_f32(&p[c * 4..c * 4 + 4])))
            .collect()
    });
    let mip_count = 32 - size.leading_zeros();
    let convolved = mip_count.min(MAX_CONVOLVED_LEVELS);
    let chain: Vec<Level> = box_chain(texels.clone(), size as usize)
        .iter()
        .map(|(t, s)| Level::bordered(t, *s))
        .collect();
    let mut out: Vec<Vec<Vec<[f32; 4]>>> = texels.into_iter().map(|t| vec![t]).collect();
    for level in 1..mip_count {
        let s = (size >> level) as usize;
        let next: Vec<Vec<[f32; 4]>> = if level < convolved {
            let p = LevelParameters::new(level, convolved, size, mip_count);
            (0..6)
                .map(|face| {
                    (0..s * s)
                        .into_par_iter()
                        .map(|i| {
                            let (x, y) = ((i % s) as u32, (i / s) as u32);
                            let sc = ((x as f32 + 0.5) / s as f32) * 2.0 - 1.0;
                            let tc = -(1.0 - ((y as f32 + 0.5) / s as f32) * 2.0);
                            let v = face_vector(face, sc, tc);
                            let inv = 1.0 / (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt();
                            let n = v.map(|c| c * inv);
                            match p.whole_level {
                                Some(l) => whole(&chain[l], p.a2, n),
                                None => sampled(&chain, &p, level, n, x, y),
                            }
                        })
                        .collect()
                })
                .collect()
        } else {
            let above = (size >> (level - 1)) as usize;
            out.iter()
                .map(|chain| downsample(chain.last().unwrap(), above))
                .collect()
        };
        for (f, texels) in next.into_iter().enumerate() {
            out[f].push(texels);
        }
    }
    out.into_iter()
        .map(|levels| {
            levels
                .into_iter()
                .map(|texels| {
                    let s = (texels.len() as f64).sqrt() as u32;
                    Image {
                        width: s,
                        height: s,
                        format: TextureFormat::Rgba32F,
                        data: texels
                            .iter()
                            .flatten()
                            .flat_map(|c| c.to_le_bytes())
                            .collect(),
                    }
                })
                .collect()
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_tables_follow_the_generator() {
        let t = tables();
        assert_eq!(t.first[0], 0xe840);
        // The generator's next word, by hand: t = x ^ x << 11, w ^ w >> 19 ^ t ^ t >> 8.
        let (x, w) = (XORSHIFT_SEED[0], XORSHIFT_SEED[3]);
        let tt = x ^ (x << 11);
        assert_eq!(t.first[1], (w ^ (w >> 19) ^ tt ^ (tt >> 8)) as u16);
        for level in 1..MAX_CONVOLVED_LEVELS {
            let s = strata(level);
            let n = sample_count(level) as usize;
            let mut sorted = s.polar.clone();
            sorted.sort_unstable();
            assert_eq!(sorted, (0..n as u16).collect::<Vec<_>>(), "a permutation");
            assert_ne!(s.polar, s.order);
        }
    }

    /// A face texel's centre direction lands on that texel (the cube layout both ways).
    #[test]
    fn a_face_texels_direction_lands_on_it() {
        let size = 8;
        for face in 0..6 {
            for (x, y) in [(0, 0), (7, 0), (3, 5), (7, 7)] {
                let c = |i: usize| (i as f64 + 0.5) / size as f64 * 2.0 - 1.0;
                let d = face_vector(face, c(x), c(y));
                assert_eq!(texel_of(d, size), (face, x, y), "face {face} texel {x},{y}");
            }
        }
    }

    /// A border texel is the neighbouring face's edge texel: +Z's left column continues on -X's
    /// right column, row for row.
    #[test]
    fn borders_continue_on_the_neighbouring_face() {
        let size = 4;
        let texels: [Vec<[f32; 4]>; 6] = std::array::from_fn(|f| {
            (0..size * size)
                .map(|i| [f as f32, (i % size) as f32, (i / size) as f32, 1.0])
                .collect()
        });
        let level = Level::bordered(&texels, size);
        for y in 0..size {
            assert_eq!(
                level.at(4, 0, y + 1),
                [1.0, (size - 1) as f32, y as f32, 1.0]
            );
        }
    }

    /// An odd level's box mean reads the top-left 2x2 texels of each pair (NPOT faces).
    #[test]
    fn an_odd_level_downsamples_from_its_top_left() {
        let texels: Vec<[f32; 4]> = (0..9).map(|i| [i as f32; 4]).collect();
        assert_eq!(
            downsample(&texels, 3),
            vec![[(0.0 + 1.0 + 3.0 + 4.0) / 4.0; 4]]
        );
    }

    #[test]
    fn sample_counts_double_up_to_4096() {
        let counts: Vec<u32> = (1..7).map(sample_count).collect();
        assert_eq!(counts, [512, 1024, 2048, 4096, 4096, 4096]);
    }

    #[test]
    fn the_approximations_stay_close() {
        for i in 0..=100 {
            let x = -0.25 + 0.5 * i as f32 / 100.0;
            assert!(
                (approx_sin_turns(x) - (TAU * x).sin()).abs() < 2e-4,
                "sin {x}"
            );
            let v = 0.01 + i as f32 * 3.7;
            assert!((approx_log2(v) - v.log2()).abs() < 1e-2, "log2 {v}");
        }
    }

    fn uniform(size: u32, value: [f32; 4]) -> Vec<Image> {
        let data: Vec<u8> = (0..size * size)
            .flat_map(|_| value)
            .flat_map(f32::to_le_bytes)
            .collect();
        (0..6)
            .map(|_| Image {
                width: size,
                height: size,
                format: TextureFormat::Rgba32F,
                data: data.clone(),
            })
            .collect()
    }

    /// A constant environment stays constant at every level (sampled, whole-level and boxed),
    /// for a size whose roughness steps are not sixths (16: five convolved levels).
    #[test]
    fn a_uniform_cube_stays_uniform() {
        for size in [16, 64] {
            let chains = convolve(&uniform(size, [0.25, 0.5, 2.0, 1.0]));
            assert_eq!(chains.len(), 6);
            for chain in &chains {
                assert_eq!(chain.len(), (32 - size.leading_zeros()) as usize);
                for level in chain {
                    for p in level.data.as_chunks::<16>().0 {
                        let v: [f32; 4] = std::array::from_fn(|c| read_f32(&p[c * 4..c * 4 + 4]));
                        for (got, want) in v.iter().zip([0.25, 0.5, 2.0, 1.0]) {
                            assert!((got - want).abs() <= want * 1e-5, "{size}: {v:?}");
                        }
                    }
                }
            }
        }
    }

    #[test]
    fn levels_pick_sampling_or_the_whole_level() {
        // 64: mip 1 samples, mips 2-5 sum level 1 whole, the roughest samples again.
        let picks: Vec<Option<usize>> = (1..7)
            .map(|level| LevelParameters::new(level, 7, 64, 7).whole_level)
            .collect();
        assert_eq!(picks, [None, Some(1), Some(1), Some(1), Some(1), None]);
        // Small cubes (fewer than six mips) always sample.
        assert!((1..4).all(|l| LevelParameters::new(l, 4, 8, 4).whole_level.is_none()));
    }
}
