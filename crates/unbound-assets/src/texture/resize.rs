//! Resizing with the Mitchell resize algorithm, matching the Unity 6000.0.71f1 Editor's output:
//! stb_image_resize 0.97 on the stored values, four channels, edges clamped, no alpha weighting
//! (as with STBIR_FLAG_ALPHA_PREMULTIPLIED) and no colour space conversion. Each axis that
//! shrinks uses the Mitchell-Netravali filter (B = C = 1/3; for float images the variant
//! `(27 m(x) + 2) / 35`), every other axis the cubic B-spline. Bytes decode as v / 255 and
//! encode as `(int)(saturate(v) * 255 + 0.5)`; all filtering is float, in stb's order. The Editor
//! splits the output rows among `min(height / 16, job workers)` jobs; each resizes its band as an
//! stb region, which recomputes the vertical mapping in float, so a few bytes depend on the worker
//! count.
//! Every resized case of tests/projects/textureimport matches byte for byte.
use super::{Image, TextureFormat, read_f32};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Kernel {
    CubicBSpline,
    Mitchell,
    /// The Mitchell variant used for HDR images.
    MitchellModified,
}

impl Kernel {
    fn at(self, x: f32) -> f32 {
        let x = x.abs();
        match self {
            Kernel::CubicBSpline => {
                if x < 1.0 {
                    (4.0 + x * x * (3.0 * x - 6.0)) / 6.0
                } else if x < 2.0 {
                    (8.0 + x * (-12.0 + x * (6.0 - x))) / 6.0
                } else {
                    0.0
                }
            }
            Kernel::Mitchell | Kernel::MitchellModified => {
                let m = if x < 1.0 {
                    (16.0 + x * x * (21.0 * x - 36.0)) / 18.0
                } else if x < 2.0 {
                    (32.0 + x * (-60.0 + x * (36.0 - 7.0 * x))) / 18.0
                } else {
                    return 0.0;
                };
                if self == Kernel::MitchellModified {
                    (m + 0.074_074_075) * 27.0 / 35.0
                } else {
                    m
                }
            }
        }
    }
}

/// Every kernel reaches two texels out.
const SUPPORT: f32 = 2.0;

/// The output texels one input texel scatters to (shrinking) or the input texels one output
/// texel gathers (enlarging), with their weights.
struct Contributor {
    n0: i32,
    n1: i32,
    weights: Vec<f32>,
}

fn upsampling(scale: f32) -> bool {
    scale > 1.0
}

/// `stbir__get_filter_pixel_margin`.
fn margin(scale: f32) -> i32 {
    let width = if upsampling(scale) {
        (SUPPORT * 2.0).ceil()
    } else {
        (SUPPORT * 2.0 / scale).ceil()
    };
    width as i32 / 2
}

/// `stbir__calculate_filters` (with `stbir__normalize_downsample_coefficients`).
fn contributors(
    kernel: Kernel,
    scale: f32,
    shift: f32,
    input: usize,
    output: usize,
) -> Vec<Contributor> {
    if upsampling(scale) {
        let radius = SUPPORT * scale;
        return (0..output)
            .map(|n| {
                let centre = n as f32 + 0.5;
                let lower = (centre - radius + shift) / scale;
                let upper = (centre + radius + shift) / scale;
                let in_centre = (centre + shift) / scale;
                let mut first = (lower as f64 + 0.5).floor() as i32;
                let last = (upper as f64 - 0.5).floor() as i32;
                let mut weights = Vec::new();
                let mut total = 0f32;
                let mut i = 0;
                while i <= last - first {
                    let w = kernel.at(in_centre - ((i + first) as f32 + 0.5));
                    if i == 0 && w == 0.0 {
                        first += 1;
                        continue;
                    }
                    weights.push(w);
                    total += w;
                    i += 1;
                }
                let normalize = 1.0 / total;
                weights.iter_mut().for_each(|w| *w *= normalize);
                let mut n1 = last;
                for (i, &w) in weights.iter().enumerate().rev() {
                    if w != 0.0 {
                        break;
                    }
                    n1 = first + i as i32 - 1;
                }
                Contributor {
                    n0: first,
                    n1,
                    weights,
                }
            })
            .collect();
    }
    let radius = SUPPORT / scale;
    let margin = margin(scale);
    let mut contributors: Vec<Contributor> = (0..input as i32 + 2 * margin)
        .map(|n| {
            let centre = (n - margin) as f32 + 0.5;
            let lower = (centre - radius) * scale - shift;
            let upper = (centre + radius) * scale - shift;
            let out_centre = centre * scale - shift;
            let first = (lower as f64 + 0.5).floor() as i32;
            let last = (upper as f64 - 0.5).floor() as i32;
            let weights: Vec<f32> = (0..=last - first)
                .map(|i| kernel.at(((i + first) as f32 + 0.5) - out_centre) * scale)
                .collect();
            let mut n1 = last;
            for (i, &w) in weights.iter().enumerate().rev() {
                if w != 0.0 {
                    break;
                }
                n1 = first + i as i32 - 1;
            }
            Contributor {
                n0: first,
                n1,
                weights,
            }
        })
        .collect();
    // Every output texel's weights sum to 1.
    for i in 0..output as i32 {
        let mut total = 0f32;
        for c in &contributors {
            if (c.n0..=c.n1).contains(&i) {
                total += c.weights[(i - c.n0) as usize];
            } else if i < c.n0 {
                break;
            }
        }
        let normalize = 1.0 / total;
        for c in &mut contributors {
            if (c.n0..=c.n1).contains(&i) {
                c.weights[(i - c.n0) as usize] *= normalize;
            } else if i < c.n0 {
                break;
            }
        }
    }
    // Leading zero weights and texels before the image are skipped.
    let width = (SUPPORT * 2.0).ceil() as usize;
    for c in &mut contributors {
        let mut skip = c.weights.iter().take_while(|&&w| w == 0.0).count();
        c.n0 += skip as i32;
        while c.n0 < 0 {
            c.n0 += 1;
            skip += 1;
        }
        let range = (c.n1 - c.n0 + 1).max(0) as usize;
        let mut shifted = c.weights.clone();
        for i in 0..width.min(range) {
            if i + skip >= width {
                break;
            }
            let w = c.weights.get(i + skip).copied().unwrap_or(0.0);
            match shifted.get_mut(i) {
                Some(slot) => *slot = w,
                None => shifted.push(w),
            }
        }
        c.weights = shifted;
        c.n1 = c.n1.min(output as i32 - 1);
    }
    contributors
}

/// Resamples `lines` of `input` texels each (`line(i, k)` is texel k of line i, k clamped into
/// the line) to `output` texels per line.
fn resample(
    lines: usize,
    line: impl Fn(usize, usize) -> [f32; 4],
    input: usize,
    output: usize,
    kernel: Kernel,
    scale: f32,
    shift: f32,
) -> Vec<[f32; 4]> {
    let filters = contributors(kernel, scale, shift, input, output);
    let clamp = |k: i32| k.clamp(0, input as i32 - 1) as usize;
    let mut out = vec![[0f32; 4]; lines * output];
    for l in 0..lines {
        let row = &mut out[l * output..(l + 1) * output];
        if upsampling(scale) {
            for (x, c) in filters.iter().enumerate() {
                for k in c.n0..=c.n1 {
                    let (v, w) = (line(l, clamp(k)), c.weights[(k - c.n0) as usize]);
                    (0..4).for_each(|ch| row[x][ch] += v[ch] * w);
                }
            }
        } else {
            let margin = margin(scale);
            for (n, c) in filters.iter().enumerate() {
                let v = line(l, clamp(n as i32 - margin));
                for k in c.n0..=c.n1 {
                    let w = c.weights[(k - c.n0) as usize];
                    (0..4).for_each(|ch| row[k as usize][ch] += v[ch] * w);
                }
            }
        }
    }
    out
}

/// The Editor's job worker count without `-job-worker-count`: the online
/// logical processors (`sysconf(_SC_NPROCESSORS_ONLN)`, not the affinity mask) less one, at least
/// 1 and at most 112. Resized bytes depend on it (measured with tests/projects/textureimport at
/// 1, 3 and the default 15 workers), so it is part of the texture cache key.
pub(crate) fn job_workers() -> usize {
    // SAFETY: sysconf only reads a system value.
    let online = unsafe { libc::sysconf(libc::_SC_NPROCESSORS_ONLN) }.max(1) as usize;
    (online.max(2) - 1).min(112)
}

/// `image` (rows top-down) resized to `width` x `height`. The Editor resizes the bottom-up
/// image; float rounding is not symmetric, so the rows are taken bottom-up here too.
pub(super) fn mitchell(image: &Image, width: u32, height: u32) -> Image {
    let (w, h) = (image.width as usize, image.height as usize);
    let (dw, dh) = (width as usize, height as usize);
    let texels: Vec<[f32; 4]> = match image.format {
        TextureFormat::Rgba8 => image
            .data
            .as_chunks::<4>()
            .0
            .iter()
            .map(|p| p.map(|v| v as f32 / 255.0))
            .collect(),
        TextureFormat::Rgba32F => image
            .data
            .as_chunks::<16>()
            .0
            .iter()
            .map(|p| std::array::from_fn(|c| read_f32(&p[c * 4..c * 4 + 4])))
            .collect(),
    };
    let texels: Vec<[f32; 4]> = texels.chunks(w).rev().flatten().copied().collect();
    let shrink = if image.format == TextureFormat::Rgba32F {
        Kernel::MitchellModified
    } else {
        Kernel::Mitchell
    };
    let kernel = |n: usize, dn: usize| {
        if dn < n { shrink } else { Kernel::CubicBSpline }
    };
    // Horizontally first, every input row.
    let x_scale = dw as f32 / w as f32;
    let rows = resample(
        h,
        |y, x| texels[y * w + x],
        w,
        dw,
        kernel(w, dw),
        x_scale,
        0.0,
    );
    // Then each band of output rows as its own region.
    let jobs = (dh / 16).min(job_workers());
    let bands: Vec<(usize, usize)> = if jobs < 2 {
        vec![(0, dh)]
    } else {
        let per = dh / jobs;
        (0..jobs)
            .map(|j| (j * per, if j + 1 == jobs { dh - j * per } else { per }))
            .collect()
    };
    let mut out = Vec::with_capacity(dw * dh);
    for (start, count) in bands {
        let (scale, shift) = if jobs < 2 {
            (count as f32 / h as f32, 0.0)
        } else {
            let t0 = start as f32 / dh as f32;
            let t1 = (start + count) as f32 / dh as f32;
            (
                (count as f32 / h as f32) / (t1 - t0),
                t0 * count as f32 / (t1 - t0),
            )
        };
        let columns = resample(
            dw,
            |x, y| rows[y * dw + x],
            h,
            count,
            kernel(h, dh),
            scale,
            shift,
        );
        for y in 0..count {
            out.extend((0..dw).map(|x| columns[x * count + y]));
        }
    }
    let out: Vec<[f32; 4]> = out.chunks(dw).rev().flatten().copied().collect();
    let data = match image.format {
        TextureFormat::Rgba8 => out
            .iter()
            .flat_map(|t| t.map(|v| ((v.clamp(0.0, 1.0) * 255.0) as f64 + 0.5) as u8))
            .collect(),
        TextureFormat::Rgba32F => out
            .iter()
            .flat_map(|t| t.iter().flat_map(|v| v.to_le_bytes()).collect::<Vec<_>>())
            .collect(),
    };
    Image {
        width,
        height,
        format: image.format,
        data,
    }
}
