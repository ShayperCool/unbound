//! The filters a bake applies to a lightmap's direct light and to its indirect and sky's light
//! before they are summed, where the Lighting Settings' Filtering is Advanced (matches the Unity
//! 6000.0.71f1 Editor's Progressive CPU lightmapper, tests/projects/bakedbuffers `Filter*`). Both
//! mix a texel's light only with that of texels of its own chart whose light stands (weight
//! above 0) and leave the others as they are: the Gaussian filter, a separable kernel of the
//! Gaussian's integral over each texel, rows then columns, weighed by the texels' weights; the
//! A-Trous filter, four passes over a 5 × 5 grid of taps spread twice as far each pass, weighed by
//! how alike the taps' light, surface normals and places are.

/// The scale the kernel's texel boundaries are divided by (the Gaussian's spread, 0.7 × √2, in
/// units of the kernel's width over the radius's).
const SPREAD: f32 = 0.494_974_73;
/// The coefficient of the approximation of the error function the kernel integrates with
/// (`erf x ≈ sign x · √(1 − exp(−x² (4/π + A x²) / (1 + A x²)))`).
const ERF_A: f32 = 0.140_012_4;
const FOUR_OVER_PI: f32 = 1.273_239_5;
/// The first and second Newton steps' constants of the reciprocal of a texel's weights.
const NEWTON_FIRST: f32 = 2.000_000_5;
const NEWTON_SECOND: f32 = 2.0;

/// The approximation of the error function at `x`.
fn erf(x: f32) -> f32 {
    let ax2 = x * ERF_A * x;
    let e = (-((ax2 + FOUR_OVER_PI) * x * x) / (ax2 + 1.0)).exp();
    // (A negative difference is NaN, as the square root of it is.)
    (1.0f32).copysign(x) * (1.0 - e).sqrt()
}

/// The kernel of a Gaussian filter of `radius` texels: `2 ⌈radius⌉ + 1` weights, each the
/// Gaussian's integral over its texel, summing to 1.
pub fn kernel(radius: f32) -> Vec<f32> {
    let n = radius.ceil() as i32 * 2 + 1;
    let count = n as f32;
    let scale = count / (radius + radius + 1.0);
    let boundary = |j: i32| {
        let t = j as f32 / count;
        let d = f64::from(t) * 2.0 - 1.0;
        (d as f32 * scale) / SPREAD
    };
    let mut weights = vec![0.0f32; n as usize];
    for i in 0..=n / 2 {
        let upper = (erf(boundary(i + 1)) + 1.0) * 0.5;
        let w = upper + (erf(boundary(i)) + 1.0) * -0.5;
        weights[i as usize] = w;
        weights[(n - 1 - i) as usize] = w;
    }
    // Summed as the Editor sums: the first n mod 8 one by one, then eight at a time.
    let head = weights.len() % 8;
    let mut sum = weights[..head].iter().fold(0.0f32, |s, w| s + w);
    for block in weights[head..].chunks(8) {
        // ((w7 + w6) + … + w0) + sum.
        let (last, rest) = block.split_last().expect("a block of eight");
        sum = rest.iter().rev().fold(*last, |b, w| b + w) + sum;
    }
    weights.iter().map(|w| w / sum).collect()
}

/// The reciprocal of a texel's weights: the processor's estimate refined by two Newton steps
/// (its estimate where they give no number). On other than x86-64 the exact reciprocal refined
/// (not measured).
fn reciprocal(weights: f32) -> f32 {
    #[cfg(target_arch = "x86_64")]
    // SAFETY: SSE is part of every x86_64 processor.
    let estimate = unsafe {
        use std::arch::x86_64::{_mm_cvtss_f32, _mm_rcp_ss, _mm_set_ss};
        _mm_cvtss_f32(_mm_rcp_ss(_mm_set_ss(weights)))
    };
    #[cfg(not(target_arch = "x86_64"))]
    let estimate = 1.0 / weights;
    let first = (NEWTON_FIRST - weights * estimate) * estimate;
    let second = (NEWTON_SECOND - weights * first) * first;
    if second.is_nan() { estimate } else { second }
}

/// One pass of `kernel` along rows (`along_rows`) or columns, from `src` into `dst`.
fn pass(
    src: &[[f32; 4]],
    dst: &mut [[f32; 4]],
    charts: &[i32],
    width: usize,
    height: usize,
    kernel: &[f32],
    along_rows: bool,
) {
    let half = (kernel.len() / 2) as isize;
    for y in 0..height {
        for x in 0..width {
            let k = y * width + x;
            let chart = charts[k];
            if chart == -1 || !(src[k][3] > 0.0) {
                dst[k] = src[k];
                continue;
            }
            let mut sum = [0.0f32; 4];
            let mut weights = 0.0f32;
            for (j, &w) in kernel.iter().enumerate() {
                let offset = j as isize - half;
                let (nx, ny) = if along_rows {
                    (x as isize + offset, y as isize)
                } else {
                    (x as isize, y as isize + offset)
                };
                if nx < 0 || ny < 0 || nx >= width as isize || ny >= height as isize {
                    continue;
                }
                let q = ny as usize * width + nx as usize;
                if charts[q] != chart {
                    continue;
                }
                let v = src[q];
                for c in 0..4 {
                    sum[c] += v[c] * w * v[3];
                }
                weights += w * v[3];
            }
            let r = reciprocal(weights);
            dst[k] = [sum[0] * r, sum[1] * r, sum[2] * r, sum[3] * r];
        }
    }
}

/// Filters `buffer` (per texel red, green, blue and its weight) with a Gaussian of `radius`
/// texels, rows then columns; each texel's `charts` entry is its chart (-1: none). A radius of 0
/// leaves it as it is.
pub fn gaussian(buffer: &mut [[f32; 4]], charts: &[i32], width: usize, height: usize, radius: f32) {
    if radius == 0.0 {
        return;
    }
    let kernel = kernel(radius);
    let mut rows = vec![[0.0f32; 4]; buffer.len()];
    pass(buffer, &mut rows, charts, width, height, &kernel, true);
    pass(&rows, buffer, charts, width, height, &kernel, false);
}

/// The A-Trous filter's taps, as (x, y) offsets on its 5 × 5 grid, in the order they are summed.
/// The Editor's grid has (0, -2) twice and no (0, -1); so has this one.
const ATROUS_TAPS: [(i32, i32); 25] = [
    (-2, -2),
    (-1, -2),
    (0, -2),
    (1, -2),
    (2, -2),
    (-2, -1),
    (-1, -1),
    (0, -2),
    (1, -1),
    (2, -1),
    (-2, 0),
    (-1, 0),
    (0, 0),
    (1, 0),
    (2, 0),
    (-2, 1),
    (-1, 1),
    (0, 1),
    (1, 1),
    (2, 1),
    (-2, 2),
    (-1, 2),
    (0, 2),
    (1, 2),
    (2, 2),
];
/// The A-Trous passes; the taps of pass `i` (from 0) lie `2^i` texels apart.
const ATROUS_PASSES: u32 = 4;
/// The spread of the light term in the first pass (halved each pass) and of the normal term.
const ATROUS_LIGHT_SPREAD: f32 = 0.1;
const ATROUS_NORMAL_SPREAD: f32 = 0.01;

/// The factor of a squared distance in the exponent of an A-Trous term of `spread` σ: what
/// `1 / (2σ²)` is for a σ of 1 and above; the Editor blends towards 1 below, by σ.
fn atrous_factor(spread: f32) -> f32 {
    let m = spread.clamp(0.0, 1.0);
    m * reciprocal(1.0 + m * (2.0 * spread * spread - 1.0))
}

/// An A-Trous term of a squared distance: `exp(-factor · distance)`, which the Editor blends, by
/// a factor below 1, with 1 for a distance of 0 (and less for those up to 1).
fn atrous_term(factor: f32, distance: f32) -> f32 {
    let base = 1.0 - distance.clamp(0.0, 1.0);
    ((-(factor * distance)).exp() - base) * factor.clamp(0.0, 1.0) + base
}

/// Filters `buffer` (per texel red, green, blue and its weight) with the A-Trous filter of
/// `spread` (the Lighting Settings' position sigma, in texels): `normals` are the lightmap's
/// samples' normals (`supersampling` × `supersampling` a texel, rows of `width × supersampling`),
/// of which a texel's first is its own. A spread of 0 leaves it as it is.
pub fn atrous(
    buffer: &mut [[f32; 4]],
    charts: &[i32],
    normals: &[[f32; 4]],
    width: usize,
    height: usize,
    supersampling: usize,
    spread: f32,
) {
    if spread == 0.0 {
        return;
    }
    let normal = |x: usize, y: usize| {
        normals[(y * supersampling) * (width * supersampling) + x * supersampling]
    };
    let place_factor = atrous_factor(spread);
    let normal_factor = atrous_factor(ATROUS_NORMAL_SPREAD);
    let mut src = buffer.to_vec();
    let mut dst = vec![[0.0f32; 4]; buffer.len()];
    for pass in 0..ATROUS_PASSES {
        let step = 1i32 << pass;
        let light_factor = atrous_factor(ATROUS_LIGHT_SPREAD / step as f32);
        for y in 0..height {
            for x in 0..width {
                let k = y * width + x;
                let chart = charts[k];
                let p = src[k];
                if chart < 0 || !(p[3] > 0.0) {
                    dst[k] = p;
                    continue;
                }
                let np = normal(x, y);
                let mut sum = [0.0f32; 4];
                let mut weights = 0.0f32;
                for &(dx, dy) in &ATROUS_TAPS {
                    let (qx, qy) = (x as i32 + dx * step, y as i32 + dy * step);
                    if qx < 0 || qy < 0 || qx >= width as i32 || qy >= height as i32 {
                        continue;
                    }
                    let (qx, qy) = (qx as usize, qy as usize);
                    let q = qy * width + qx;
                    let v = src[q];
                    if charts[q] != chart || !(v[3] > 0.0) {
                        continue;
                    }
                    let d: [f32; 4] = std::array::from_fn(|c| (v[c] - p[c]) * (v[c] - p[c]));
                    let light = (d[0] + d[1]) + (d[2] + d[3]);
                    let nq = normal(qx, qy);
                    let n: [f32; 3] = std::array::from_fn(|c| (nq[c] - np[c]) * (nq[c] - np[c]));
                    let surface = (n[0] + n[1]) + n[2];
                    let (ox, oy) = (x as f32 - qx as f32, y as f32 - qy as f32);
                    let place = ox * ox + oy * oy;
                    let w = atrous_term(normal_factor, surface)
                        * atrous_term(light_factor, light)
                        * atrous_term(place_factor, place);
                    for c in 0..4 {
                        sum[c] += v[c] * w;
                    }
                    weights += w;
                }
                let r = reciprocal(weights);
                dst[k] = [sum[0] * r, sum[1] * r, sum[2] * r, sum[3] * r];
            }
        }
        std::mem::swap(&mut src, &mut dst);
    }
    buffer.copy_from_slice(&src);
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The kernels the Editor's filter uses for whole and fractional radii (its taps sum to 1).
    #[test]
    fn kernels_integrate_the_gaussian_over_each_texel() {
        let one = kernel(1.0);
        assert_eq!(one.len(), 3);
        assert!(
            (one[0] - 0.169_042_66).abs() < 1e-6 && (one[1] - 0.661_914_7).abs() < 1e-6,
            "{one:?}"
        );
        let half = kernel(2.5);
        assert_eq!(half.len(), 7);
        assert!((half[3] - 0.366_366_27).abs() < 1e-6, "{half:?}");
        assert!((kernel(0.5)[1] - 0.847_116_95).abs() < 1e-6);
        for r in [0.25f32, 1.0, 2.5, 5.0, 20.0] {
            let k = kernel(r);
            assert!((k.iter().sum::<f32>() - 1.0).abs() < 1e-5, "{r}");
            assert!(
                k.windows(2).take(k.len() / 2).all(|w| w[0] <= w[1]),
                "{r}: {k:?}"
            );
        }
    }

    /// Light mixes only within a chart and with texels whose light stands; a texel whose light
    /// does not stand, or of no chart, is left as it is.
    #[test]
    fn the_filter_keeps_to_charts_and_standing_light() {
        // One row: chart 0 for x 0..3, chart 1 for x 3..5, none at 5.
        let charts = [0, 0, 0, 1, 1, -1];
        let mut buffer = [
            [1.0, 0.0, 0.0, 1.0],
            [0.0, 0.0, 0.0, 1.0],
            [9.0, 0.0, 0.0, 0.0],
            [4.0, 0.0, 0.0, 1.0],
            [2.0, 0.0, 0.0, 1.0],
            [7.0, 0.0, 0.0, 1.0],
        ];
        gaussian(&mut buffer, &charts, 6, 1, 1.0);
        let k = kernel(1.0);
        // x 0: itself and x 1 (x 2 does not stand).
        let expected0 = (1.0 * k[1]) / (k[1] + k[2]);
        assert!((buffer[0][0] - expected0).abs() < 1e-6, "{buffer:?}");
        assert!(
            (buffer[1][0] - k[0] / (k[0] + k[1])).abs() < 1e-6,
            "{buffer:?}"
        );
        // Not standing, or of no chart: kept.
        assert_eq!(buffer[2], [9.0, 0.0, 0.0, 0.0]);
        assert_eq!(buffer[5], [7.0, 0.0, 0.0, 1.0]);
        // Chart 1 mixes only its own two texels.
        let expected3 = (4.0 * k[1] + 2.0 * k[2]) / (k[1] + k[2]);
        assert!((buffer[3][0] - expected3).abs() < 1e-5, "{buffer:?}");
        assert!((buffer[3][3] - 1.0).abs() < 1e-6, "{buffer:?}");
        // A radius of 0 leaves the buffer.
        let mut same = [[3.0, 0.0, 0.0, 1.0]; 4];
        gaussian(&mut same, &[0; 4], 2, 2, 0.0);
        assert_eq!(same, [[3.0, 0.0, 0.0, 1.0]; 4]);
    }

    /// The A-Trous spread factor is 1 / (2σ²) from a σ of 1 on and the Editor's blend below it;
    /// a term is exp(-k d) for a factor of 1 and above, blended with 1 - min(d, 1) below.
    #[test]
    fn atrous_factors_and_terms_follow_the_editor() {
        assert!((atrous_factor(1.5) - 1.0 / 4.5).abs() < 1e-6);
        assert!(
            (atrous_factor(0.1) - 0.1 / 0.902).abs() < 1e-5,
            "{}",
            atrous_factor(0.1)
        );
        assert!((atrous_factor(0.5) - 0.5 / 0.75).abs() < 1e-5);
        assert_eq!(atrous_factor(0.0), 0.0);
        assert!((atrous_term(2.0, 0.3) - (-0.6f32).exp()).abs() < 1e-6);
        let k = 0.25f32;
        let expected = ((-k * 0.5f32).exp() - 0.5) * k + 0.5;
        assert!((atrous_term(k, 0.5) - expected).abs() < 1e-6);
        // A factor of 0 keeps only equal values.
        assert_eq!(atrous_term(0.0, 0.0), 1.0);
        assert_eq!(atrous_term(0.0, 2.0), 0.0);
    }

    /// The A-Trous filter keeps uniform light, keeps to charts and standing light, and leaves
    /// texels of no chart or no weight; a spread of 0 leaves the buffer.
    #[test]
    fn the_atrous_filter_keeps_to_charts_and_standing_light() {
        let (w, h) = (6, 1);
        let normals = vec![[0.0, 1.0, 0.0, 0.0]; w * h];
        let charts = [0, 0, 0, 1, 1, -1];
        let mut uniform = vec![[2.0, 2.0, 2.0, 1.0]; w * h];
        atrous(&mut uniform, &[0; 6], &normals, w, h, 1, 1.5);
        assert!(
            uniform.iter().all(|t| (t[0] - 2.0).abs() < 1e-5),
            "{uniform:?}"
        );
        let mut buffer = vec![
            [1.0, 0.0, 0.0, 1.0],
            [1.05, 0.0, 0.0, 1.0],
            [9.0, 0.0, 0.0, 0.0],
            [4.0, 0.0, 0.0, 1.0],
            [4.02, 0.0, 0.0, 1.0],
            [7.0, 0.0, 0.0, 1.0],
        ];
        atrous(&mut buffer, &charts, &normals, w, h, 1, 1.5);
        assert_eq!(buffer[2], [9.0, 0.0, 0.0, 0.0]);
        assert_eq!(buffer[5], [7.0, 0.0, 0.0, 1.0]);
        // Chart 0 mixes its two standing texels only, chart 1 its own.
        assert!(
            buffer[0][0] > 1.0 && buffer[0][0] < 1.05 && buffer[1][0] > 1.0 && buffer[1][0] < 1.05,
            "{buffer:?}"
        );
        assert!(buffer[3][0] > 4.0 && buffer[3][0] < 4.02, "{buffer:?}");
        let mut same = buffer.clone();
        atrous(&mut same, &charts, &normals, w, h, 1, 0.0);
        assert_eq!(same, buffer);
    }
}
