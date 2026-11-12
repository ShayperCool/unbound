//! Block compression (BC1/DXT1, BC3/DXT5, BC4, BC5, BC6H, BC7) of imported textures, and the CPU
//! decoding `GetPixels` reads.
//!
//! BC1 and BC3 reproduce the blocks of the Unity 6000.0.71f1 Editor's importer bit for bit
//! (tests/projects/texturecompression): the colour endpoints are fitted along the principal axis
//! of the block's colours (a power iteration of the covariance), refined once by least squares,
//! and texels take the nearest of the four colours; a block of one colour takes the endpoint pair
//! whose interpolated colour is nearest ([`solid_tables`]); BC3's alpha spans the block's alpha
//! range in eight steps. The measured import profile uses bc7e for BC7, scalar endpoint fitting
//! for BC4/BC5 and quality-dependent ISPC BC6H settings. The measured runtime profile uses
//! its channel quantizer and a dithered colour fit. Other profiles retain approximations. Exact block counts and remaining differences are recorded by
//! tests/projects/texturecompression.
use serde::{Deserialize, Serialize};
use std::sync::OnceLock;

/// A block-compressed format of an import.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum BlockFormat {
    /// DXT1: opaque colour, 8 bytes a block.
    Bc1,
    /// DXT5: colour and alpha, 16 bytes a block.
    Bc3,
    /// One channel (red), 8 bytes a block.
    Bc4,
    /// Two channels (red, green), 16 bytes a block.
    Bc5,
    /// Unsigned half-float RGB, 16 bytes a block.
    Bc6h,
    /// RGBA, 16 bytes a block.
    Bc7,
    /// ETC1 (`ETC_RGB4`): opaque colour, 8 bytes a block.
    Etc1,
    /// ETC2 RGB: opaque colour, 8 bytes a block.
    Etc2Rgb,
    /// ETC2 RGBA8: an EAC alpha block and an ETC2 colour block, 16 bytes.
    Etc2Rgba8,
    /// LDR RGBA, 4x4 texels in 16 bytes.
    Astc4x4,
    /// LDR RGBA, 5x5 texels in 16 bytes.
    Astc5x5,
    /// LDR RGBA, 6x6 texels in 16 bytes.
    Astc6x6,
    /// LDR RGBA, 8x8 texels in 16 bytes.
    Astc8x8,
}

impl BlockFormat {
    /// The format of Unity's `TextureFormat` value, if it is one of these.
    pub fn from_unity_format(format: i32) -> Option<Self> {
        Some(match format {
            10 => Self::Bc1,
            12 => Self::Bc3,
            26 => Self::Bc4,
            27 => Self::Bc5,
            24 => Self::Bc6h,
            25 => Self::Bc7,
            34 => Self::Etc1,
            45 => Self::Etc2Rgb,
            47 => Self::Etc2Rgba8,
            48 => Self::Astc4x4,
            49 => Self::Astc5x5,
            50 => Self::Astc6x6,
            51 => Self::Astc8x8,
            _ => return None,
        })
    }

    /// Unity's `TextureFormat` value.
    pub fn unity_format(self) -> i32 {
        match self {
            Self::Bc1 => 10,
            Self::Bc3 => 12,
            Self::Bc4 => 26,
            Self::Bc5 => 27,
            Self::Bc6h => 24,
            Self::Bc7 => 25,
            Self::Etc1 => 34,
            Self::Etc2Rgb => 45,
            Self::Etc2Rgba8 => 47,
            Self::Astc4x4 => 48,
            Self::Astc5x5 => 49,
            Self::Astc6x6 => 50,
            Self::Astc8x8 => 51,
        }
    }

    pub fn block_bytes(self) -> usize {
        match self {
            Self::Bc1 | Self::Bc4 | Self::Etc1 | Self::Etc2Rgb => 8,
            _ => 16,
        }
    }

    /// Square block footprint in texels.
    pub fn footprint(self) -> u32 {
        match self {
            Self::Astc5x5 => 5,
            Self::Astc6x6 => 6,
            Self::Astc8x8 => 8,
            _ => 4,
        }
    }

    pub fn is_astc(self) -> bool {
        matches!(
            self,
            Self::Astc4x4 | Self::Astc5x5 | Self::Astc6x6 | Self::Astc8x8
        )
    }

    /// Bytes of a `width` x `height` level: whole blocks, at least one.
    pub fn level_size(self, width: u32, height: u32) -> usize {
        (width.max(1).div_ceil(self.footprint()) * height.max(1).div_ceil(self.footprint()))
            as usize
            * self.block_bytes()
    }

    /// Whether the blocks are this crate's own encoding rather than the Editor's.
    pub fn approximates_editor(self) -> bool {
        !matches!(self, Self::Bc1 | Self::Bc3)
    }

    /// Whether GPUs with BC formats sample the blocks; Unbound's renderer samples no ETC, so ETC
    /// blocks go up as BC7 blocks or pixels of what they decode to.
    pub fn is_bc(self) -> bool {
        !self.is_etc() && !self.is_astc()
    }

    /// ETC1 or ETC2.
    pub fn is_etc(self) -> bool {
        matches!(self, Self::Etc1 | Self::Etc2Rgb | Self::Etc2Rgba8)
    }
}

/// One 4x4 block of RGBA8 texels, row by row.
type Block = [[u8; 4]; 16];

/// The `level` RGBA8 texels (rows of `width`) of the 4x4 block at block coordinates (`bx`, `by`);
/// a level smaller than a block repeats as tiles (the 2x2 and 1x1 levels of the Unity 6000.0.71f1
/// Editor's blocks, tests/projects/texturecompression).
fn gather(rgba: &[u8], width: usize, height: usize, bx: usize, by: usize) -> Block {
    gather_tiled(rgba, width, height, bx, by, false)
}

fn gather_tiled(
    rgba: &[u8],
    width: usize,
    height: usize,
    bx: usize,
    by: usize,
    partial_tile: bool,
) -> Block {
    let mut block = [[0u8; 4]; 16];
    for (i, texel) in block.iter_mut().enumerate() {
        let (tw, th) = if partial_tile {
            ((width - bx * 4).min(4), (height - by * 4).min(4))
        } else {
            (width, height)
        };
        let x = (bx * 4 + (i % 4) % tw) % width;
        let y = (by * 4 + (i / 4) % th) % height;
        texel.copy_from_slice(&rgba[(y * width + x) * 4..][..4]);
    }
    block
}

/// Compresses one RGBA8 level (rows bottom-up, as stored) to `format`. BC6H takes
/// [`compress_level_f32`] instead.
pub fn compress_level(format: BlockFormat, rgba: &[u8], width: u32, height: u32) -> Vec<u8> {
    use rayon::prelude::*;
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let (bw, bh) = (w.div_ceil(4), h.div_ceil(4));
    match format {
        BlockFormat::Astc4x4
        | BlockFormat::Astc5x5
        | BlockFormat::Astc6x6
        | BlockFormat::Astc8x8 => super::astc::compress(rgba, width, height, format),
        BlockFormat::Etc1 | BlockFormat::Etc2Rgb | BlockFormat::Etc2Rgba8 => {
            super::etc::compress_level(rgba, width, height, format == BlockFormat::Etc2Rgba8)
        }
        BlockFormat::Bc1 | BlockFormat::Bc3 => {
            let size = format.block_bytes();
            let mut out = vec![0u8; bw * bh * size];
            out.par_chunks_mut(bw * size)
                .enumerate()
                .for_each(|(by, row)| {
                    for bx in 0..bw {
                        let block = gather(rgba, w, h, bx, by);
                        let dst = &mut row[bx * size..][..size];
                        if format == BlockFormat::Bc1 {
                            dst.copy_from_slice(&encode_bc1(&block, false));
                        } else {
                            dst[..8].copy_from_slice(&encode_alpha(&block, 3));
                            dst[8..].copy_from_slice(&encode_bc1(&block, true));
                        }
                    }
                });
            out
        }
        BlockFormat::Bc4 | BlockFormat::Bc5 | BlockFormat::Bc7 => {
            // The ISPC encoders take whole blocks: a level smaller than a block repeats as tiles.
            let (pw, ph) = (bw * 4, bh * 4);
            let mut padded = vec![0u8; pw * ph * 4];
            for y in 0..ph {
                for x in 0..pw {
                    let from = ((y % h) * w + x % w) * 4;
                    padded[(y * pw + x) * 4..][..4].copy_from_slice(&rgba[from..from + 4]);
                }
            }
            match format {
                BlockFormat::Bc4 => {
                    let red: Vec<u8> = padded.chunks_exact(4).map(|p| p[0]).collect();
                    intel_tex_2::bc4::compress_blocks(&intel_tex_2::RSurface {
                        data: &red,
                        width: pw as u32,
                        height: ph as u32,
                        stride: pw as u32,
                    })
                }
                BlockFormat::Bc5 => {
                    let rg: Vec<u8> = padded.chunks_exact(4).flat_map(|p| [p[0], p[1]]).collect();
                    intel_tex_2::bc5::compress_blocks(&intel_tex_2::RgSurface {
                        data: &rg,
                        width: pw as u32,
                        height: ph as u32,
                        stride: pw as u32 * 2,
                    })
                }
                _ => {
                    let opaque = padded.chunks_exact(4).all(|p| p[3] == 255);
                    let settings = if opaque {
                        intel_tex_2::bc7::opaque_fast_settings()
                    } else {
                        intel_tex_2::bc7::alpha_fast_settings()
                    };
                    // Strips of 16 block rows compress in parallel; their blocks follow each other.
                    let strip = 64.min(ph);
                    padded
                        .par_chunks(pw * 4 * strip)
                        .map(|rows| {
                            intel_tex_2::bc7::compress_blocks(
                                &settings,
                                &intel_tex_2::RgbaSurface {
                                    data: rows,
                                    width: pw as u32,
                                    height: (rows.len() / (pw * 4)) as u32,
                                    stride: (pw * 4) as u32,
                                },
                            )
                        })
                        .collect::<Vec<_>>()
                        .concat()
                }
            }
        }
        BlockFormat::Bc6h => panic!("BC6H compresses float levels (compress_level_f32)"),
    }
}

/// Texture2D.Compress's encoder in the measured compatibility profile. Channel bounds start
/// with texel zero's alpha, then use the remaining texels of the selected channel. Colour
/// fits use rounded thirds, with optional dithering (tests/projects/texturecompression).
pub fn compress_runtime_level(
    format: BlockFormat,
    rgba: &[u8],
    width: u32,
    height: u32,
    high_quality: bool,
) -> Vec<u8> {
    if !matches!(
        format,
        BlockFormat::Bc1 | BlockFormat::Bc3 | BlockFormat::Bc4 | BlockFormat::Bc5
    ) {
        return compress_level(format, rgba, width, height);
    }
    use rayon::prelude::*;
    unsafe extern "C" {
        fn unbound_runtime_dxt_init();
        fn unbound_runtime_dxt_colour(pixels: *const u8, block: *mut u8, high_quality: bool);
    }
    let colour = matches!(format, BlockFormat::Bc1 | BlockFormat::Bc3);
    if colour {
        static INIT: OnceLock<()> = OnceLock::new();
        INIT.get_or_init(|| unsafe { unbound_runtime_dxt_init() });
    }
    let channels: &[usize] = match format {
        BlockFormat::Bc3 => &[3],
        BlockFormat::Bc4 => &[0],
        BlockFormat::Bc5 => &[0, 1],
        _ => &[],
    };
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let bw = w.div_ceil(4);
    let size = format.block_bytes();
    let mut blocks = vec![0; bw * h.div_ceil(4) * size];
    blocks
        .par_chunks_mut(bw * size)
        .enumerate()
        .for_each(|(by, row)| {
            for bx in 0..bw {
                // Runtime edge blocks repeat their remaining rows/columns.
                let pixels = gather_tiled(rgba, w, h, bx, by, true);
                let dst = &mut row[bx * size..][..size];
                for (i, &channel) in channels.iter().enumerate() {
                    dst[i * 8..][..8].copy_from_slice(&encode_runtime_channel(&pixels, channel));
                }
                if colour {
                    let at = channels.len() * 8;
                    // Initialization has completed; the native function reads 64 bytes, writes 8,
                    // and uses only local working storage. Other block destinations are disjoint.
                    unsafe {
                        unbound_runtime_dxt_colour(
                            pixels.as_ptr().cast(),
                            dst[at..].as_mut_ptr(),
                            high_quality,
                        );
                    }
                }
            }
        });
    blocks
}

fn encode_runtime_channel(block: &Block, channel: usize) -> [u8; 8] {
    const INTERVALS: i32 = 7;
    let (mut lo, mut hi) = (block[0][3], block[0][3]);
    for texel in &block[1..] {
        lo = lo.min(texel[channel]);
        hi = hi.max(texel[channel]);
    }
    let distance = i32::from(hi) - i32::from(lo);
    let mut bits = u64::from(hi) | (u64::from(lo) << 8);
    for (i, texel) in block.iter().enumerate() {
        let mut position = (i32::from(texel[channel]) - i32::from(lo)) * INTERVALS + distance / 2;
        let mut level = 0;
        // A tie at an interval boundary takes its lower level.
        for step in [4, 2, 1] {
            if position > step * distance {
                level |= step;
                position -= step * distance;
            }
        }
        let mut index = (-level) & INTERVALS;
        index ^= i32::from(index < 2);
        bits |= (index as u64) << (16 + i * 3);
    }
    bits.to_le_bytes()
}

/// Import settings measured in Unity 6000.0.71f1, tests/projects/texturecompression. Runtime
/// compression and GPU-only conversions keep their separate encoder selection.
pub(super) fn compress_import_level(
    format: BlockFormat,
    data: &[u8],
    width: u32,
    height: u32,
    quality: i32,
    srgb: bool,
    bc7_compressor: i32,
) -> Vec<u8> {
    if matches!(format, BlockFormat::Bc4 | BlockFormat::Bc5) {
        use rayon::prelude::*;
        unsafe extern "C" {
            fn unbound_bc4_block(pixels: *const u8, block: *mut u8);
        }
        let (w, h) = (width.max(1) as usize, height.max(1) as usize);
        let size = format.block_bytes();
        let bw = w.div_ceil(4);
        let mut blocks = vec![0u8; bw * h.div_ceil(4) * size];
        blocks
            .par_chunks_mut(bw * size)
            .enumerate()
            .for_each(|(by, row)| {
                for bx in 0..bw {
                    let pixels = gather(data, w, h, bx, by);
                    for channel in 0..size / 8 {
                        let values = pixels.map(|p| p[channel]);
                        // The native call reads 16 bytes and writes 8, within this block's slice.
                        unsafe {
                            unbound_bc4_block(
                                values.as_ptr(),
                                row[bx * size + channel * 8..].as_mut_ptr(),
                            );
                        }
                    }
                }
            });
        return blocks;
    }
    if format == BlockFormat::Bc6h {
        let settings = match quality {
            0 => intel_tex_2::bc6h::very_fast_settings(),
            100 => intel_tex_2::bc6h::very_slow_settings(),
            _ => intel_tex_2::bc6h::slow_settings(),
        };
        return compress_f32_with_settings(data, width, height, &settings);
    }
    if format != BlockFormat::Bc7 || bc7_compressor == 1 {
        return compress_level(format, data, width, height);
    }
    use rayon::prelude::*;
    unsafe extern "C" {
        fn unbound_bc7e_init();
        fn unbound_bc7e_blocks(
            pixels: *const u32,
            blocks: *mut u64,
            count: u32,
            quality: i32,
            perceptual: bool,
        );
    }
    static INIT: OnceLock<()> = OnceLock::new();
    INIT.get_or_init(|| unsafe { unbound_bc7e_init() });
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let (bw, bh) = (w.div_ceil(4), h.div_ceil(4));
    // Native output is aligned; only convert it to bytes after the parallel writes finish.
    let mut blocks = vec![0u64; bw * bh * 2];
    blocks
        .par_chunks_mut(bw * 2)
        .enumerate()
        .for_each(|(by, row)| {
            // Fixed stack storage keeps image-sized temporary allocations out of the encoder.
            const BATCH: usize = 64;
            let mut pixels = [0u32; BATCH * 16];
            for start in (0..bw).step_by(BATCH) {
                let count = (bw - start).min(BATCH);
                for bx in 0..count {
                    let block = gather(data, w, h, start + bx, by);
                    for (i, texel) in block.iter().enumerate() {
                        pixels[bx * 16 + i] = u32::from_le_bytes(*texel);
                    }
                }
                // Both slices contain `count` whole blocks and do not alias; initialization is
                // complete, settings are local to the native call, and other rows are disjoint.
                unsafe {
                    unbound_bc7e_blocks(
                        pixels.as_ptr(),
                        row[start * 2..].as_mut_ptr(),
                        count as u32,
                        quality,
                        srgb,
                    );
                }
            }
        });
    blocks.into_iter().flat_map(u64::to_le_bytes).collect()
}

/// Compresses one RGBA32F level (little-endian floats, rows bottom-up) to BC6H.
pub fn compress_level_f32(rgba: &[u8], width: u32, height: u32) -> Vec<u8> {
    compress_f32_with_settings(rgba, width, height, &intel_tex_2::bc6h::fast_settings())
}

fn compress_f32_with_settings(
    rgba: &[u8],
    width: u32,
    height: u32,
    settings: &intel_tex_2::bc6h::EncodeSettings,
) -> Vec<u8> {
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let (pw, ph) = (w.next_multiple_of(4), h.next_multiple_of(4));
    // Half floats, a level smaller than a block tiled; negative values clamp to 0 (unsigned).
    let mut half = vec![0u8; pw * ph * 8];
    for y in 0..ph {
        for x in 0..pw {
            let from = ((y % h) * w + x % w) * 16;
            for c in 0..4 {
                let v = f32::from_le_bytes(rgba[from + c * 4..][..4].try_into().unwrap());
                let bits = f32_to_f16(if v.is_nan() { 0.0 } else { v.max(0.0) });
                half[(y * pw + x) * 8 + c * 2..][..2].copy_from_slice(&bits.to_le_bytes());
            }
        }
    }
    intel_tex_2::bc6h::compress_blocks(
        settings,
        &intel_tex_2::RgbaSurface {
            data: &half,
            width: pw as u32,
            height: ph as u32,
            stride: pw as u32 * 8,
        },
    )
}

/// An f32 as an IEEE half (round to nearest even; overflow to infinity).
fn f32_to_f16(v: f32) -> u16 {
    let bits = v.to_bits();
    let sign = ((bits >> 16) & 0x8000) as u16;
    let exp = ((bits >> 23) & 0xff) as i32;
    let mant = bits & 0x7f_ffff;
    if exp == 0xff {
        return sign | 0x7c00 | if mant != 0 { 0x200 } else { 0 };
    }
    let e = exp - 127 + 15;
    if e >= 0x1f {
        return sign | 0x7c00;
    }
    if e <= 0 {
        if e < -10 {
            return sign;
        }
        let m = mant | 0x80_0000;
        let shift = (14 - e) as u32;
        let half = 1u32 << (shift - 1);
        let rest = m & ((1 << shift) - 1);
        let mut out = m >> shift;
        if rest > half || (rest == half && out & 1 == 1) {
            out += 1;
        }
        return sign | out as u16;
    }
    let mut out = ((e as u32) << 10) | (mant >> 13);
    let rest = mant & 0x1fff;
    if rest > 0x1000 || (rest == 0x1000 && out & 1 == 1) {
        out += 1;
    }
    sign | out as u16
}

/// Decodes one level to RGBA8 (rows as stored) the way the Editor's `GetPixels32` reads it:
/// colours interpolate with truncating integer thirds (a half in BC1's three-colour mode, whose
/// fourth entry is transparent black), alpha, red and green steps round to nearest; BC4 reads
/// (r, 0, 0, 1) and BC5 (r, g, 0, 1). BC6H is clamped to 0..1 (Unbound has no float decoding of
/// its blocks). `blocks` shorter than the level leaves the missing blocks' texels zero.
pub fn decode_level(format: BlockFormat, blocks: &[u8], width: u32, height: u32) -> Vec<u8> {
    decode(format, blocks, width, height, false)
}

/// [`decode_level`] as GPUs sample the blocks: a BC3 block's colour half always has four
/// colours (for devices without BC formats).
pub fn decode_level_as_gpu(format: BlockFormat, blocks: &[u8], width: u32, height: u32) -> Vec<u8> {
    decode(format, blocks, width, height, true)
}

fn decode(format: BlockFormat, blocks: &[u8], width: u32, height: u32, gpu: bool) -> Vec<u8> {
    if format.is_astc() {
        return super::astc::decode(blocks, width, height, format);
    }
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let bw = w.div_ceil(4);
    let size = format.block_bytes();
    let mut out = vec![0u8; w * h * 4];
    let mut texels = [[0u8, 0, 0, 255]; 16];
    for (i, block) in blocks.chunks_exact(size).enumerate() {
        let (bx, by) = (i % bw, i / bw);
        match format {
            BlockFormat::Astc4x4
            | BlockFormat::Astc5x5
            | BlockFormat::Astc6x6
            | BlockFormat::Astc8x8 => unreachable!(),
            BlockFormat::Bc1 => decode_colour(block, &mut texels, false),
            BlockFormat::Bc3 => {
                decode_colour(&block[8..], &mut texels, gpu);
                decode_steps(&block[..8], &mut texels, 3);
            }
            BlockFormat::Bc4 => {
                texels = [[0, 0, 0, 255]; 16];
                decode_steps(block, &mut texels, 0);
            }
            BlockFormat::Bc5 => {
                texels = [[0, 0, 0, 255]; 16];
                decode_steps(&block[..8], &mut texels, 0);
                decode_steps(&block[8..], &mut texels, 1);
            }
            BlockFormat::Bc7
            | BlockFormat::Bc6h
            | BlockFormat::Etc1
            | BlockFormat::Etc2Rgb
            | BlockFormat::Etc2Rgba8 => {
                let mut image = [0u32; 16];
                match format {
                    BlockFormat::Bc7 => texture2ddecoder::decode_bc7_block(block, &mut image),
                    BlockFormat::Bc6h => {
                        texture2ddecoder::decode_bc6_block(block, &mut image, false)
                    }
                    // ETC1 blocks are ETC2 blocks without the overflowing modes.
                    BlockFormat::Etc1 | BlockFormat::Etc2Rgb => {
                        texture2ddecoder::decode_etc2_rgb_block(block, &mut image)
                    }
                    _ => texture2ddecoder::decode_etc2_rgba8_block(block, &mut image),
                }
                for (t, p) in texels.iter_mut().zip(image) {
                    let [b, g, r, a] = p.to_le_bytes();
                    *t = [r, g, b, a];
                }
            }
        }
        for (t, texel) in texels.iter().enumerate() {
            let (x, y) = (bx * 4 + t % 4, by * 4 + t / 4);
            if x < w && y < h {
                out[(y * w + x) * 4..][..4].copy_from_slice(texel);
            }
        }
    }
    out
}

fn expand565(c: u16) -> [u32; 3] {
    let (r, g, b) = (
        u32::from(c >> 11),
        u32::from((c >> 5) & 63),
        u32::from(c & 31),
    );
    [
        (r << 3) | (r >> 2),
        (g << 2) | (g >> 4),
        (b << 3) | (b >> 2),
    ]
}

/// A colour block. A BC3 block's colour half follows the same rule on the CPU, three colours
/// and black when c0 <= c1 (its alpha comes from the alpha half), as the Editor's GetPixels32
/// reads it (tests/projects/texturecompression new.DXT5, bytes of 205); `four` gives it four
/// colours always, as GPUs sample it.
fn decode_colour(block: &[u8], texels: &mut [[u8; 4]; 16], four: bool) {
    let c0 = u16::from_le_bytes([block[0], block[1]]);
    let c1 = u16::from_le_bytes([block[2], block[3]]);
    let (a, b) = (expand565(c0), expand565(c1));
    let mix = |wa: u32, wb: u32, d: u32| -> [u8; 4] {
        let m = |k: usize| ((a[k] * wa + b[k] * wb) / d) as u8;
        [m(0), m(1), m(2), 255]
    };
    let palette = if four || c0 > c1 {
        [mix(1, 0, 1), mix(0, 1, 1), mix(2, 1, 3), mix(1, 2, 3)]
    } else {
        [mix(1, 0, 1), mix(0, 1, 1), mix(1, 1, 2), [0, 0, 0, 0]]
    };
    let indices = u32::from_le_bytes(block[4..8].try_into().unwrap());
    for (i, t) in texels.iter_mut().enumerate() {
        *t = palette[(indices >> (2 * i)) as usize & 3];
    }
}

/// An eight-step block (BC3 alpha, BC4, a BC5 half) into channel `channel`.
fn decode_steps(block: &[u8], texels: &mut [[u8; 4]; 16], channel: usize) {
    let (a0, a1) = (u32::from(block[0]), u32::from(block[1]));
    let step = |i: u32, n: u32| ((a0 * (n - i) + a1 * i + n / 2) / n) as u8;
    let mut values = [0u8; 8];
    values[0] = a0 as u8;
    values[1] = a1 as u8;
    if a0 > a1 {
        for i in 1..7 {
            values[i as usize + 1] = step(i, 7);
        }
    } else {
        for i in 1..5 {
            values[i as usize + 1] = step(i, 5);
        }
        values[6] = 0;
        values[7] = 255;
    }
    let bits = u64::from_le_bytes(block[..8].try_into().unwrap()) >> 16;
    for (i, t) in texels.iter_mut().enumerate() {
        t[channel] = values[(bits >> (3 * i)) as usize & 7];
    }
}

/// The value of 5- and 6-bit endpoint codes expanded to 8 bits.
fn expand(code: u32, bits: u32) -> i32 {
    (if bits == 5 {
        (code << 3) | (code >> 2)
    } else {
        (code << 2) | (code >> 4)
    }) as i32
}

/// For every 8-bit value, the endpoint pair (`e0`, `e1`) a block of one colour uses and the value
/// the pair decodes to: four-colour mode (index 2: `(2 e0 + e1) / 3`) and three-colour mode (index
/// 2: `(e0 + e1) / 2`), integer division. The pair minimizes the decoding error plus 3% of the
/// endpoints' spread (the tolerance hardware interpolation is allowed); among equals the first in
/// order of `e1`, then `e0`, wins.
struct SolidTable {
    /// `[value] -> (e0, e1, decoded)`.
    entries: [(u8, u8, u8); 256],
}

fn solid_table(bits: u32, four: bool) -> SolidTable {
    let codes = 1u32 << bits;
    let mut entries = [(0u8, 0u8, 0u8); 256];
    for (value, entry) in entries.iter_mut().enumerate() {
        let mut best = f32::INFINITY;
        for e1 in 0..codes {
            for e0 in 0..codes {
                let (a, b) = (expand(e0, bits), expand(e1, bits));
                let decoded = if four { (2 * a + b) / 3 } else { (a + b) / 2 };
                let error = (decoded - value as i32).abs() as f32 + (a - b).abs() as f32 * 0.03;
                if error < best {
                    best = error;
                    *entry = (e0 as u8, e1 as u8, decoded as u8);
                }
            }
        }
    }
    SolidTable { entries }
}

/// The tables for 5- and 6-bit channels, four- then three-colour mode.
fn solid_tables() -> &'static [SolidTable; 4] {
    static TABLES: OnceLock<[SolidTable; 4]> = OnceLock::new();
    TABLES.get_or_init(|| {
        [
            solid_table(5, true),
            solid_table(6, true),
            solid_table(5, false),
            solid_table(6, false),
        ]
    })
}

/// The colour block of a block whose texels share one colour. A BC1 block may use three-colour
/// mode when its squared error is no larger; a BC3 block's colour is always four-colour.
fn encode_solid(rgb: [u8; 3], bc3: bool) -> [u8; 8] {
    let tables = solid_tables();
    let mode = |four: bool| {
        let t = if four { &tables[0..2] } else { &tables[2..4] };
        let (r, g, b) = (
            t[0].entries[rgb[0] as usize],
            t[1].entries[rgb[1] as usize],
            t[0].entries[rgb[2] as usize],
        );
        let error = [(r.2, rgb[0]), (g.2, rgb[1]), (b.2, rgb[2])]
            .iter()
            .map(|&(d, v)| (i32::from(d) - i32::from(v)).pow(2))
            .sum::<i32>();
        let pack = |e: (u8, u8)| -> u16 { (u16::from(e.0) << 11) | (u16::from(e.1) << 5) };
        let c0 = pack((r.0, g.0)) | u16::from(b.0);
        let c1 = pack((r.1, g.1)) | u16::from(b.1);
        (error, c0, c1)
    };
    let (four_error, f0, f1) = mode(true);
    let (three_error, t0, t1) = mode(false);
    let (c0, c1, indices) = if !bc3 && three_error <= four_error {
        // Three-colour mode needs c0 <= c1; index 2 is the midpoint either way.
        (t0.min(t1), t0.max(t1), 0xaaaa_aaaa_u32)
    } else if f0 >= f1 {
        (f0, f1, 0xaaaa_aaaa)
    } else {
        // Swapped to keep four-colour order: the 2/3 endpoint is now c1 (index 3).
        (f1, f0, 0xffff_ffff)
    };
    let mut out = [0u8; 8];
    out[..2].copy_from_slice(&c0.to_le_bytes());
    out[2..4].copy_from_slice(&c1.to_le_bytes());
    out[4..].copy_from_slice(&indices.to_le_bytes());
    out
}

/// An f32 to i32 as SSE's truncating conversion does it: NaN and out-of-range values give
/// `i32::MIN`.
fn truncate(v: f32) -> i32 {
    if v.is_nan() || v >= 2_147_483_648.0 || v < -2_147_483_648.0 {
        i32::MIN
    } else {
        v as i32
    }
}

/// An 8-bit value (0..=255, its fraction dropped) to an `n`-step code, rounding as integer
/// arithmetic does: `(v * n + 128 + ((v * n + 128) >> 8)) >> 8`.
fn quantize(v: f32, steps: i32) -> u16 {
    let t = truncate(v) * steps + 128;
    ((t + (t >> 8)) >> 8) as u16
}

fn rgb565(c: [f32; 3]) -> u16 {
    (quantize(c[0], 31) << 11) + (quantize(c[1], 63) << 5) + quantize(c[2], 31)
}

fn decode565(p: u16) -> [f32; 3] {
    expand565(p).map(|v| v as f32)
}

/// The two-bit indices (texel 0 lowest) of the nearest of the four colours along c0..c1,
/// before [`fix_indices`].
fn fit_indices(block: &[[f32; 3]; 16], p0: u16, p1: u16) -> u32 {
    let (c0, c1) = (decode565(p0), decode565(p1));
    let mut dir = [c1[0] - c0[0], c1[1] - c0[1], c1[2] - c0[2]];
    let norm = dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2];
    let scale = 1.0 / norm;
    for d in &mut dir {
        *d *= scale * 3.0;
    }
    let mut bias = 0.5f32;
    for p in 0..3 {
        bias -= c0[p] * dir[p];
    }
    let mut bits = 0u32;
    for (k, texel) in block.iter().enumerate() {
        let mut dot = 0.0f32;
        for p in 0..3 {
            dot += texel[p] * dir[p];
        }
        let q = truncate(dot + bias).clamp(0, 3) as u32;
        bits = bits.wrapping_add(q << (2 * k));
    }
    bits
}

/// Index order 0 (c0), 1 (2/3 c0), 2 (1/3 c0), 3 (c1) to BC1's 0, 2, 3, 1.
fn fix_indices(bits: u32) -> u32 {
    let low = bits & 0x5555_5555;
    let high = bits & 0xaaaa_aaaa;
    (high >> 1).wrapping_add(high ^ (low << 1))
}

/// Least-squares endpoints for the current indices (the block's mean for a block that uses one
/// index).
fn refine(block: &[[f32; 3]; 16], bits: u32, dc: [f32; 3]) -> (u16, u16) {
    let mut c0 = dc;
    let mut c1 = dc;
    if (bits ^ bits.wrapping_mul(4)) >= 4 {
        let mut atb1 = [0.0f32; 3];
        let (mut sum_q, mut sum_qq) = (0.0f32, 0.0f32);
        for (k, texel) in block.iter().enumerate() {
            let q = ((bits >> (2 * k)) & 3) as f32;
            let x = 3.0 - q;
            sum_q += q;
            sum_qq += q * q;
            for p in 0..3 {
                atb1[p] += x * texel[p];
            }
        }
        let mut atb2 = [0.0f32; 3];
        for p in 0..3 {
            atb2[p] = 3.0 * (dc[p] * 16.0) - atb1[p];
        }
        let cxx = 16.0 * 9.0 - 2.0 * 3.0 * sum_q + sum_qq;
        let cyy = sum_qq;
        let cxy = 3.0 * sum_q - sum_qq;
        let scale = 3.0 * (1.0 / (cxx * cyy - cxy * cxy));
        for p in 0..3 {
            c0[p] = ((atb1[p] * cyy - atb2[p] * cxy) * scale).clamp(0.0, 255.0);
            c1[p] = ((atb2[p] * cxx - atb1[p] * cxy) * scale).clamp(0.0, 255.0);
        }
    }
    (rgb565(c0), rgb565(c1))
}

/// The start vector of the power iteration: (1, e, π) as floats.
const AXIS_START: [f32; 3] = [1.0, std::f32::consts::E, std::f32::consts::PI];

/// A BC1 colour block (the colour half of a BC3 block when `bc3`).
fn encode_bc1(block: &Block, bc3: bool) -> [u8; 8] {
    let first = &block[0][..3];
    if block.iter().all(|t| &t[..3] == first) {
        return encode_solid([first[0], first[1], first[2]], bc3);
    }
    let texels: [[f32; 3]; 16] = std::array::from_fn(|k| [0, 1, 2].map(|p| f32::from(block[k][p])));
    // Mean and covariance.
    let mut dc = [0.0f32; 3];
    for (p, d) in dc.iter_mut().enumerate() {
        let mut acc = 0.0f32;
        for t in &texels {
            acc += t[p];
        }
        *d = acc / 16.0;
    }
    let mut covar = [0.0f32; 6];
    for t in &texels {
        let d = [t[0] - dc[0], t[1] - dc[1], t[2] - dc[2]];
        covar[0] += d[0] * d[0];
        covar[1] += d[0] * d[1];
        covar[2] += d[0] * d[2];
        covar[3] += d[1] * d[1];
        covar[4] += d[1] * d[2];
        covar[5] += d[2] * d[2];
    }
    for i in [0, 3, 5] {
        covar[i] += 0.001;
    }
    // Principal axis: four power iterations, normalized after every second.
    let mut axis = AXIS_START;
    for i in 0..4 {
        let a = [
            covar[0] * axis[0] + covar[1] * axis[1] + covar[2] * axis[2],
            covar[1] * axis[0] + covar[3] * axis[1] + covar[4] * axis[2],
            covar[2] * axis[0] + covar[4] * axis[1] + covar[5] * axis[2],
        ];
        axis = a;
        if i % 2 == 1 {
            let rnorm = 1.0 / (a[0] * a[0] + a[1] * a[1] + a[2] * a[2]).sqrt();
            for v in &mut axis {
                *v *= rnorm;
            }
        }
    }
    // Endpoints: the extreme projections onto the axis.
    let (mut min_dot, mut max_dot) = (256.0f32 * 256.0, 0.0f32);
    for t in &texels {
        let mut dot = 0.0f32;
        for p in 0..3 {
            dot += (t[p] - dc[p]) * axis[p];
        }
        min_dot = min_dot.min(dot);
        max_dot = max_dot.max(dot);
    }
    if max_dot - min_dot < 1.0 {
        min_dot -= 0.5;
        max_dot += 0.5;
    }
    let norm_sq = axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2];
    let rnorm_sq = 1.0 / norm_sq;
    let (mut c0, mut c1) = ([0.0f32; 3], [0.0f32; 3]);
    for p in 0..3 {
        c0[p] = (dc[p] + min_dot * rnorm_sq * axis[p]).clamp(0.0, 255.0);
        c1[p] = (dc[p] + max_dot * rnorm_sq * axis[p]).clamp(0.0, 255.0);
    }
    let ordered = |a: u16, b: u16| if a < b { (b, a) } else { (a, b) };
    let (mut p0, mut p1) = ordered(rgb565(c0), rgb565(c1));
    let mut bits = fit_indices(&texels, p0, p1);
    let (r0, r1) = refine(&texels, bits, dc);
    (p0, p1) = ordered(r0, r1);
    bits = fix_indices(fit_indices(&texels, p0, p1));
    let mut out = [0u8; 8];
    out[..2].copy_from_slice(&p0.to_le_bytes());
    out[2..4].copy_from_slice(&p1.to_le_bytes());
    out[4..].copy_from_slice(&bits.to_le_bytes());
    out
}

/// An eight-step block of channel `channel`: the block's range, each texel rounded to the
/// nearest step.
fn encode_alpha(block: &Block, channel: usize) -> [u8; 8] {
    let (mut lo, mut hi) = (255.0f32, 0.0f32);
    for t in block {
        let v = f32::from(t[channel]);
        lo = lo.min(v);
        hi = hi.max(v);
    }
    if lo == hi {
        hi = lo + 0.1;
    }
    let scale = 7.0 / (hi - lo);
    let mut indices = 0u64;
    for (k, t) in block.iter().enumerate() {
        let proj = (f32::from(t[channel]) - lo) * scale + 0.5;
        let mut q = 7 - truncate(proj).clamp(0, 7);
        if q > 0 {
            q += 1;
        }
        if q == 8 {
            q = 1;
        }
        indices |= (q as u64) << (3 * k);
    }
    // The maximum first: eight steps from it down to the minimum.
    let mut out = [0u8; 8];
    out[0] = truncate(hi).clamp(0, 255) as u8;
    out[1] = truncate(lo).clamp(0, 255) as u8;
    out[2..].copy_from_slice(&indices.to_le_bytes()[..6]);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn runtime_partial_edge_blocks_repeat_the_remaining_texels() {
        // The 10x6 level has a two-column and a two-row partial block.
        let rgba: Vec<u8> = (0..6)
            .flat_map(|y| {
                (0..10).flat_map(move |x| {
                    [
                        (x * 19 + y * 7 + 32) as u8,
                        (x * 11 + y * 31 + 18) as u8,
                        (x * 37 + y * 5 + 24) as u8,
                        if (x + y + 1) % 3 == 0 {
                            0
                        } else if (x + y) % 2 == 0 {
                            83
                        } else {
                            255
                        },
                    ]
                })
            })
            .collect();
        let blocks = compress_runtime_level(BlockFormat::Bc3, &rgba, 10, 6, false);
        assert_eq!(
            &blocks[32..48],
            &[
                255, 0, 65, 0, 195, 142, 19, 199, 141, 222, 8, 179, 221, 187, 170, 34
            ]
        );
    }

    #[test]
    fn runtime_channels_keep_the_reference_endpoint_seed() {
        // Unity 6000.0.71f1, texturecompression compress.seed.r8/rg16: texel zero is the
        // smallest component. Its alpha starts the bounds, before the other component values.
        let mut pixels = [[78, 130, 0, 255]; 16];
        pixels[0] = [0, 1, 0, 255];
        let rgba = pixels.concat();
        let red = [255, 78, 0x49, 0x92, 0x24, 0x49, 0x92, 0x24];
        let green = [255, 130, 0x49, 0x92, 0x24, 0x49, 0x92, 0x24];
        for high in [false, true] {
            assert_eq!(
                compress_runtime_level(BlockFormat::Bc4, &rgba, 4, 4, high),
                red
            );
            assert_eq!(
                compress_runtime_level(BlockFormat::Bc5, &rgba, 4, 4, high),
                [red, green].concat()
            );
        }
    }

    #[test]
    fn runtime_dxt_colour_dithering_and_alpha_match_the_reference() {
        // Unity 6000.0.71f1, texturecompression compress.rgba32, first block.
        let rgba = [
            81, 234, 124, 196, 213, 15, 76, 226, 113, 35, 178, 174, 17, 227, 79, 39, 134, 230, 98,
            198, 92, 118, 21, 216, 31, 168, 187, 47, 11, 70, 196, 92, 198, 253, 52, 69, 174, 186,
            137, 9, 89, 201, 47, 172, 82, 5, 102, 77, 19, 49, 242, 64, 205, 218, 170, 118, 31, 134,
            16, 37, 228, 32, 51, 251,
        ];
        let fast = [
            251, 9, 211, 62, 221, 206, 236, 30, 227, 190, 26, 17, 186, 122, 232, 169,
        ];
        let high = [
            251, 9, 211, 62, 221, 206, 236, 30, 226, 190, 26, 17, 186, 122, 232, 169,
        ];
        assert_eq!(
            compress_runtime_level(BlockFormat::Bc3, &rgba, 4, 4, false),
            fast
        );
        assert_eq!(
            compress_runtime_level(BlockFormat::Bc3, &rgba, 4, 4, true),
            high
        );
    }

    /// The endpoint tables of one-colour blocks (spot values worked out by hand from the rule).
    #[test]
    fn solid_tables_pick_the_nearest_pair_with_a_spread_penalty() {
        let t = solid_tables();
        // 5-bit four-colour: 2 is (2 * 0 + 8) / 3 exactly; 4 is (2 * 8 + 0) / 3 = 5.
        assert_eq!(t[0].entries[2], (0, 1, 2));
        assert_eq!(t[0].entries[4], (1, 0, 5));
        // 6-bit four-colour: 23 is exact for (0, 17) but that pair spans 69 (2.07 penalty);
        // (6, 6) decodes to 24 with no spread.
        assert_eq!(t[1].entries[23], (6, 6, 24));
        // 5-bit three-colour: 3 is nearest to (8 + 0) / 2 = 4.
        assert_eq!(t[2].entries[3], (1, 0, 4));
    }

    #[test]
    fn solid_blocks() {
        // Grey 128: four-colour error 0 with no spread wins.
        let grey = encode_solid([128, 128, 128], false);
        let c0 = u16::from_le_bytes([grey[0], grey[1]]);
        let c1 = u16::from_le_bytes([grey[2], grey[3]]);
        let decoded = decode_level(BlockFormat::Bc1, &grey, 4, 4);
        assert!(decoded.chunks(4).all(|t| t == &decoded[..4]));
        let d = &decoded[..4];
        assert!(
            d.iter().take(3).all(|&v| (v as i32 - 128).abs() <= 2),
            "{d:?}"
        );
        // A BC3 colour half never uses three-colour mode.
        let bc3 = encode_solid([239, 72, 42], true);
        assert!(u16::from_le_bytes([bc3[0], bc3[1]]) >= u16::from_le_bytes([bc3[2], bc3[3]]));
        assert!(c0 >= c1 || u32::from_le_bytes(grey[4..].try_into().unwrap()) == 0xaaaa_aaaa);
    }

    #[test]
    fn bc1_round_trips_two_colour_blocks() {
        let mut block = [[0u8, 0, 0, 255]; 16];
        for (i, t) in block.iter_mut().enumerate() {
            *t = if i % 2 == 0 {
                [255, 0, 0, 255]
            } else {
                [0, 0, 255, 255]
            };
        }
        let encoded = encode_bc1(&block, false);
        let mut rgba = Vec::new();
        for t in &block {
            rgba.extend_from_slice(t);
        }
        assert_eq!(decode_level(BlockFormat::Bc1, &encoded, 4, 4), rgba);
    }

    #[test]
    fn alpha_steps_round_to_nearest() {
        // a0 = 255 > a1 = 0: step 1 is (255 * 6 + 3) / 7 = 219.
        let mut block = [0u8; 8];
        block[0] = 255;
        block[1] = 0;
        // Texel 0 takes index 2.
        block[2] = 2;
        let decoded = decode_level(BlockFormat::Bc4, &block, 4, 4);
        assert_eq!(&decoded[..4], &[219, 0, 0, 255]);
    }

    #[test]
    fn alpha_blocks_span_the_range() {
        let mut block = [[0u8; 4]; 16];
        for (i, t) in block.iter_mut().enumerate() {
            t[3] = (i * 17) as u8;
        }
        let encoded = encode_alpha(&block, 3);
        assert_eq!((encoded[0], encoded[1]), (255, 0));
        let mut bc3 = [0u8; 16];
        bc3[..8].copy_from_slice(&encoded);
        bc3[8..].copy_from_slice(&encode_bc1(&block, true));
        let decoded = decode_level(BlockFormat::Bc3, &bc3, 4, 4);
        for (i, t) in decoded.chunks(4).enumerate() {
            assert!((t[3] as i32 - (i * 17) as i32).abs() <= 19, "{i}: {}", t[3]);
        }
    }

    #[test]
    fn levels_below_a_block_are_one_padded_block() {
        assert_eq!(BlockFormat::Bc1.level_size(2, 1), 8);
        assert_eq!(BlockFormat::Bc3.level_size(64, 64), 4096);
        let rgba = [10u8, 20, 30, 255, 40, 50, 60, 255];
        let blocks = compress_level(BlockFormat::Bc1, &rgba, 2, 1);
        assert_eq!(blocks.len(), 8);
        assert_eq!(decode_level(BlockFormat::Bc1, &blocks, 2, 1).len(), 8);
        assert_eq!(compress_level(BlockFormat::Bc7, &rgba, 2, 1).len(), 16);
    }
}
#[test]
fn measured_import_encoder_keeps_constant_channels_and_small_levels() {
    // Unity 6000.0.71f1, texturecompression: final levels of HeightBC4 and NormalsBC5.
    let rgba = [105, 127, 0, 255];
    let bc4 = compress_import_level(BlockFormat::Bc4, &rgba, 1, 1, 50, false, 0);
    assert_eq!(bc4, [105, 105, 0, 0, 0, 0, 0, 0]);
    let normal = [127, 127, 0, 255];
    let bc5 = compress_import_level(BlockFormat::Bc5, &normal, 1, 1, 50, false, 0);
    assert_eq!(
        bc5,
        [127, 127, 0, 0, 0, 0, 0, 0, 127, 127, 0, 0, 0, 0, 0, 0]
    );
    // Independent channels and rectangular sizes also use whole blocks.
    let data = [21, 194, 43, 99].repeat(3 * 7);
    let blocks = compress_import_level(BlockFormat::Bc5, &data, 3, 7, 50, false, 0);
    assert_eq!(blocks.len(), 32);
    assert_eq!(
        decode_level(BlockFormat::Bc5, &blocks, 3, 7),
        [21, 194, 0, 255].repeat(21)
    );
}

#[test]
fn bc7_import_tiles_and_encodes_alpha_in_every_quality() {
    // Last level of NoiseRGBA32, compared with NoiseBC7's Editor blocks.
    let rgba = [152, 152, 151, 127];
    let expected = [
        0x20, 0x33, 0xff, 0xcc, 0x2f, 0xfb, 0xff, 0xfd, 0xad, 0xaa, 0xaa, 0xaa, 0, 0, 0, 0,
    ];
    for quality in [0, 50, 100] {
        let block = compress_import_level(BlockFormat::Bc7, &rgba, 1, 1, quality, true, 0);
        assert_eq!(block, expected, "quality {quality}");
        assert_eq!(decode_level(BlockFormat::Bc7, &block, 1, 1), rgba);
    }
}

#[test]
fn hdr_special_values_and_edge_sizes_do_not_abort_import() {
    let data: Vec<_> = [f32::NAN, f32::INFINITY, -1.0, 1.0]
        .repeat(2 * 3)
        .into_iter()
        .flat_map(f32::to_le_bytes)
        .collect();
    for quality in [0, 50, 100] {
        assert_eq!(
            compress_import_level(BlockFormat::Bc6h, &data, 2, 3, quality, false, 0).len(),
            16
        );
    }
}
