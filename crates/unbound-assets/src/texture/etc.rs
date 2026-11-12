//! ETC1 and ETC2 blocks of imports for targets that store them (Android).
//!
//! Colour blocks are ETC1 blocks ([`encode_etc1`]), which are also valid ETC2 RGB blocks;
//! ETC2 RGBA8 adds an EAC block for the alpha ([`encode_alpha`]). The formats, sizes and layout are the Unity 6000.0.71f1 Editor's
//! (tests/projects/texturecompression Android profiles); the Editor's encoders search more
//! modes, so the texels differ slightly from its blocks.

use super::BlockFormat;
use rayon::prelude::*;

/// EAC modifier tables, each the negative half of its eight modifiers (the positive half is
/// `-m - 1` of each), as the ETC2 specification lists them.
const EAC_TABLES: [[i32; 4]; 16] = [
    [-3, -6, -9, -15],
    [-3, -7, -10, -13],
    [-2, -5, -8, -13],
    [-2, -4, -6, -13],
    [-3, -6, -8, -12],
    [-3, -7, -9, -11],
    [-4, -7, -8, -11],
    [-3, -5, -8, -11],
    [-2, -6, -8, -10],
    [-2, -5, -8, -10],
    [-2, -4, -8, -10],
    [-2, -5, -7, -10],
    [-3, -4, -7, -10],
    [-1, -2, -3, -10],
    [-4, -6, -8, -9],
    [-3, -5, -7, -9],
];

/// The eight modifiers of an EAC table in index order: the negative ones, then the positive.
fn eac_modifiers(table: usize) -> [i32; 8] {
    let n = EAC_TABLES[table];
    [
        n[0],
        n[1],
        n[2],
        n[3],
        -n[0] - 1,
        -n[1] - 1,
        -n[2] - 1,
        -n[3] - 1,
    ]
}

/// An EAC block of 8-bit alpha (texels row by row): the table, multiplier and base value that
/// reproduce the texels with the least squared error, searched around the multiplier and base
/// that make each table span the block's alpha range.
pub fn encode_alpha(alpha: &[u8; 16]) -> [u8; 8] {
    let (lo, hi) = alpha.iter().fold((255i32, 0i32), |(lo, hi), &a| {
        (lo.min(a.into()), hi.max(a.into()))
    });
    let error = |base: i32, mods: &[i32; 8], mul: i32, bound: i64| -> i64 {
        let mut sum = 0i64;
        for &a in alpha {
            let a = i32::from(a);
            let mut best = i32::MAX;
            for m in mods {
                let d = (base + m * mul).clamp(0, 255) - a;
                best = best.min(d * d);
            }
            sum += i64::from(best);
            if sum >= bound {
                break;
            }
        }
        sum
    };
    let mut best = (i64::MAX, 0usize, 1i32, lo);
    'search: for table in 0..16 {
        let mods = eac_modifiers(table);
        let (low, high) = (mods[3], mods[7]);
        let span = high - low;
        let fit = ((hi - lo + span - 1) / span).clamp(1, 15);
        for mul in (fit - 1).max(1)..=(fit + 1).min(15) {
            let centre = ((lo - low * mul) + (hi - high * mul)) / 2;
            for base in centre - 1..=centre + 1 {
                let base = base.clamp(0, 255);
                let e = error(base, &mods, mul, best.0);
                if e < best.0 {
                    best = (e, table, mul, base);
                    if e == 0 {
                        break 'search;
                    }
                }
            }
        }
    }
    let (_, table, mul, base) = best;
    let mods = eac_modifiers(table);
    // Indices of the texels column by column, three bits each, the first texel highest.
    let mut bits = 0u64;
    for x in 0..4 {
        for y in 0..4 {
            let a = i32::from(alpha[y * 4 + x]);
            let index = (0..8)
                .min_by_key(|&i| {
                    let d = (base + mods[i] * mul).clamp(0, 255) - a;
                    d * d
                })
                .unwrap();
            bits = (bits << 3) | index as u64;
        }
    }
    let mut out = [0u8; 8];
    out[0] = base as u8;
    out[1] = ((mul as u8) << 4) | table as u8;
    out[2..].copy_from_slice(&bits.to_be_bytes()[2..]);
    out
}

/// ETC1 luminance modifiers per table, for pixel indices 0..3 (the specification's order:
/// +a, +b, -a, -b).
const ETC1_MODIFIERS: [[i32; 4]; 8] = [
    [2, 8, -2, -8],
    [5, 17, -5, -17],
    [9, 29, -9, -29],
    [13, 42, -13, -42],
    [18, 60, -18, -60],
    [24, 80, -24, -80],
    [33, 106, -33, -106],
    [47, 183, -47, -183],
];

/// The texels (x, y) of one half of a block: columns 0-1 or 2-3, or rows 0-1 or 2-3 (`flip`).
fn half(flip: bool, second: bool) -> impl Iterator<Item = (usize, usize)> {
    let o = if second { 2 } else { 0 };
    (0..8).map(move |i| {
        if flip {
            (i % 4, o + i / 4)
        } else {
            (o + i % 2, i / 2)
        }
    })
}

/// The best table of a half around `base` (expanded 8-bit colour): (squared error, table,
/// per-texel indices in [`half`] order).
fn fit_half(
    block: &[[u8; 4]; 16],
    flip: bool,
    second: bool,
    base: [i32; 3],
) -> (i64, u32, [u32; 8]) {
    let mut best = (i64::MAX, 0, [0; 8]);
    for (table, mods) in ETC1_MODIFIERS.iter().enumerate() {
        let mut sum = 0i64;
        let mut indices = [0u32; 8];
        for (i, (x, y)) in half(flip, second).enumerate() {
            let t = block[y * 4 + x];
            let (mut e, mut pick) = (i64::MAX, 0);
            for (k, m) in mods.iter().enumerate() {
                let err: i64 = (0..3)
                    .map(|c| {
                        let d = i64::from((base[c] + m).clamp(0, 255)) - i64::from(t[c]);
                        d * d
                    })
                    .sum();
                if err < e {
                    (e, pick) = (err, k as u32);
                }
            }
            indices[i] = pick;
            sum += e;
        }
        if sum < best.0 {
            best = (sum, table as u32, indices);
        }
    }
    best
}

/// An ETC1 block (also a valid ETC2 RGB block) of RGBA8 texels row by row: each half takes its
/// average colour, differential when the two 5-bit averages are close enough, else individual
/// 4-bit, and the modifier table that fits its texels best; the split (columns or rows) with
/// the smaller error wins.
pub fn encode_etc1(block: &[[u8; 4]; 16]) -> [u8; 8] {
    let mut best = (i64::MAX, 0u64);
    for flip in [false, true] {
        let average = |second: bool| {
            let mut sum = [0u32; 3];
            for (x, y) in half(flip, second) {
                for c in 0..3 {
                    sum[c] += u32::from(block[y * 4 + x][c]);
                }
            }
            sum.map(|v| v as f32 / 8.0)
        };
        let (a1, a2) = (average(false), average(true));
        let quantize = |a: [f32; 3], max: f32| a.map(|v| (v * max / 255.0).round() as i32);
        let (c1, c2) = (quantize(a1, 31.0), quantize(a2, 31.0));
        let diff = [0, 1, 2].map(|c| c2[c] - c1[c]);
        let mut word = 0u64;
        let (b1, b2) = if diff.iter().all(|d| (-4..=3).contains(d)) {
            word |= 1 << 33;
            for c in 0..3 {
                word |= (c1[c] as u64) << (59 - 8 * c);
                word |= ((diff[c] & 7) as u64) << (56 - 8 * c);
            }
            let e = |v: i32| (v << 3) | (v >> 2);
            (c1.map(e), c2.map(e))
        } else {
            let (c1, c2) = (quantize(a1, 15.0), quantize(a2, 15.0));
            for c in 0..3 {
                word |= (c1[c] as u64) << (60 - 8 * c);
                word |= (c2[c] as u64) << (56 - 8 * c);
            }
            let e = |v: i32| (v << 4) | v;
            (c1.map(e), c2.map(e))
        };
        let (e1, t1, i1) = fit_half(block, flip, false, b1);
        let (e2, t2, i2) = fit_half(block, flip, true, b2);
        word |= u64::from(t1) << 37 | u64::from(t2) << 34 | u64::from(flip) << 32;
        for (second, indices) in [(false, i1), (true, i2)] {
            for ((x, y), index) in half(flip, second).zip(indices) {
                let bit = x * 4 + y;
                word |= u64::from(index >> 1) << (16 + bit);
                word |= u64::from(index & 1) << bit;
            }
        }
        if e1 + e2 < best.0 {
            best = (e1 + e2, word);
        }
    }
    best.1.to_be_bytes()
}

/// Compresses one RGBA8 level (rows as stored; a level smaller than a block repeats as tiles in
/// it, as the Editor's blocks do) to ETC1/ETC2 RGB blocks, or ETC2 RGBA8 blocks with `alpha`.
pub fn compress_level(rgba: &[u8], width: u32, height: u32, format: BlockFormat) -> Vec<u8> {
    compress(rgba, width, height, format, false)
}

pub(super) fn compress_runtime_eac(
    rgba: &[u8],
    width: u32,
    height: u32,
    format: BlockFormat,
) -> Vec<u8> {
    assert!(format.is_eac());
    compress(rgba, width, height, format, true)
}

fn compress(rgba: &[u8], width: u32, height: u32, format: BlockFormat, runtime: bool) -> Vec<u8> {
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let (bw, bh) = (w.div_ceil(4), h.div_ceil(4));
    let size = format.block_bytes();
    let mut out = vec![0u8; bw * bh * size];
    out.par_chunks_mut(bw * size)
        .enumerate()
        .for_each(|(by, row)| {
            for bx in 0..bw {
                let mut block = [[0u8; 4]; 16];
                for (i, texel) in block.iter_mut().enumerate() {
                    let (x, y) = if runtime {
                        (
                            bx * 4 + (i % 4) % (w - bx * 4).min(4),
                            by * 4 + (i / 4) % (h - by * 4).min(4),
                        )
                    } else {
                        ((bx * 4 + i % 4) % w, (by * 4 + i / 4) % h)
                    };
                    texel.copy_from_slice(&rgba[(y * w + x) * 4..][..4]);
                }
                let dst = &mut row[bx * size..][..size];
                if format.is_eac() {
                    // The independent eight-bit source fit is shared with EAC alpha. Its
                    // output is also a valid unsigned R11/RG11 block; fitting remains an
                    // approximation, with exact block counts in texturecompression.
                    for channel in 0..if format == BlockFormat::EacR { 1 } else { 2 } {
                        dst[channel * 8..channel * 8 + 8]
                            .copy_from_slice(&encode_alpha(&block.map(|t| t[channel])));
                    }
                } else {
                    if format == BlockFormat::Etc2Rgba8 {
                        dst[..8].copy_from_slice(&encode_alpha(&block.map(|t| t[3])));
                    }
                    dst[size - 8..].copy_from_slice(&encode_etc1(&block));
                }
            }
        });
    out
}

/// CPU reads use the eight-bit EAC interpretation measured in texturecompression. GPU
/// sampling uses the unsigned eleven-bit definition, including its multiplier-zero case.
pub(super) fn eac_texel(block: &[u8], x: usize, y: usize, eleven: bool) -> u16 {
    let table = (block[1] & 15) as usize;
    let multiplier = i32::from(block[1] >> 4);
    let bits = u64::from_be_bytes([
        0, 0, block[2], block[3], block[4], block[5], block[6], block[7],
    ]);
    let index = ((bits >> ((15 - (x * 4 + y)) * 3)) & 7) as usize;
    let modifier = eac_modifiers(table)[index];
    let base = i32::from(block[0]);
    if eleven {
        (base * 8 + 4 + modifier * if multiplier == 0 { 1 } else { multiplier * 8 }).clamp(0, 2047)
            as u16
    } else {
        (base + modifier * multiplier).clamp(0, 255) as u16
    }
}

pub(super) fn decode_eac_texel(format: BlockFormat, block: &[u8], index: usize) -> [u8; 4] {
    let (x, y) = (index % 4, index / 4);
    let mut output = [0, 0, 0, 255];
    output[0] = eac_texel(block, x, y, false) as u8;
    if format == BlockFormat::EacRg {
        output[1] = eac_texel(&block[8..], x, y, false) as u8;
    }
    output
}

/// Fixed-point approximation of the measured OpenGL channel conversion. The complete
/// eac.values control retains one-UNorm16-step residuals for multiplier-zero blocks;
/// other profiles use ideal UNorm11. No byte-precision reduction is applied to GPU data.
fn eac_gpu_value(value: u16, measured: bool) -> u16 {
    if measured {
        const SCALE_NUMERATOR: u32 = 65567;
        const SCALE_DENOMINATOR: u32 = 2048;
        let expanded =
            (u32::from(value) * SCALE_NUMERATOR + SCALE_DENOMINATOR / 2) / SCALE_DENOMINATOR;
        expanded as u16
    } else {
        ((u32::from(value) * 65535 + 1023) / 2047) as u16
    }
}

/// Tightly packed unsigned normalized 16-bit red or red/green channels for GPU upload.
pub fn decode_eac_gpu(
    format: BlockFormat,
    blocks: &[u8],
    width: u32,
    height: u32,
    measured: bool,
) -> Vec<u8> {
    assert!(format.is_eac());
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let channels = if format == BlockFormat::EacR { 1 } else { 2 };
    let mut output = vec![0; w * h * channels * 2];
    let columns = w.div_ceil(4);
    for (i, block) in blocks.chunks_exact(format.block_bytes()).enumerate() {
        let (bx, by) = (i % columns * 4, i / columns * 4);
        if by >= h {
            break;
        }
        for y in 0..4.min(h - by) {
            for x in 0..4.min(w - bx) {
                for channel in 0..channels {
                    let value = eac_texel(&block[channel * 8..], x, y, true);
                    let at = (((by + y) * w + bx + x) * channels + channel) * 2;
                    output[at..at + 2]
                        .copy_from_slice(&eac_gpu_value(value, measured).to_le_bytes());
                }
            }
        }
    }
    output
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn eac_cpu_bytes_and_gpu_channels_use_distinct_precision() {
        let red = [128, 13, 255, 255, 255, 255, 255, 255];
        let green = [64, 48, 0, 0, 0, 0, 0, 0];
        let block: Vec<_> = red.into_iter().chain(green).collect();
        for format in [BlockFormat::EacR, BlockFormat::EacRg] {
            let bytes = &block[..format.block_bytes()];
            for index in 0..16 {
                assert_eq!(
                    decode_eac_texel(format, bytes, index),
                    [
                        128,
                        if format == BlockFormat::EacRg { 55 } else { 0 },
                        0,
                        255
                    ]
                );
            }
            let blocks = bytes.repeat(format.level_size(3, 7) / format.block_bytes());
            let gpu = decode_eac_gpu(format, &blocks, 3, 7, true);
            let channels = if format == BlockFormat::EacR { 1 } else { 2 };
            assert_eq!(gpu.len(), 3 * 7 * channels * 2);
            for pixel in gpu.chunks_exact(channels * 2) {
                // Unity 6000.0.71f1, texturecompression eac.manual controls.
                assert_eq!(u16::from_le_bytes(pixel[..2].try_into().unwrap()), 33200);
                if channels == 2 {
                    assert_eq!(u16::from_le_bytes(pixel[2..].try_into().unwrap()), 14215);
                }
            }
            assert_eq!(
                super::super::decode_block_texel(format, bytes, 0).unwrap()[0],
                128.0 / 255.0
            );
            assert!(super::super::decode_block_texel(format, bytes, 16).is_none());
            assert!(
                super::super::decode_block_texel(format, &bytes[..bytes.len() - 1], 0).is_none()
            );
        }
    }
    #[test]
    fn runtime_eac_partial_blocks_repeat_the_remaining_rows_and_columns() {
        let rgba: Vec<_> = (0..5)
            .flat_map(|y| (0..6).flat_map(move |x| [(x * 39) as u8, (y * 51) as u8, 0, 255]))
            .collect();
        let encoded = compress_runtime_eac(&rgba, 6, 5, BlockFormat::EacRg);
        let mut edge = [[0; 4]; 16];
        for (i, pixel) in edge.iter_mut().enumerate() {
            let x = 4 + (i % 4) % 2;
            pixel.copy_from_slice(&rgba[(4 * 6 + x) * 4..][..4]);
        }
        assert_eq!(&encoded[48..56], &encode_alpha(&edge.map(|p| p[0])));
        assert_eq!(&encoded[56..64], &encode_alpha(&edge.map(|p| p[1])));
    }
}
