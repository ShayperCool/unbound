//! Fast LDR ASTC through the MIT-licensed ISPCTextureCompressor C interface packaged by
//! intel_tex_2. The dependency's Rust ASTC encoder is unfinished; its C interface is complete.
use super::BlockFormat;

#[repr(C)]
struct Surface {
    pixels: *const u8,
    width: i32,
    height: i32,
    stride: i32,
}

#[repr(C)]
struct Settings {
    block_width: i32,
    block_height: i32,
    channels: i32,
    fast_skip_threshold: i32,
    refine_iterations: i32,
}

unsafe extern "C" {
    #[link_name = "CompressBlocksASTC"]
    fn compress_blocks(surface: *const Surface, output: *mut u8, settings: *const Settings);
}

pub(super) fn compress(rgba: &[u8], width: u32, height: u32, format: BlockFormat) -> Vec<u8> {
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let side = format.footprint() as usize;
    let (pw, ph) = (w.next_multiple_of(side), h.next_multiple_of(side));
    // ASTC clamps padding, including sub-block mip levels (texturecompression q0 controls).
    let mut padded = vec![0; pw * ph * 4];
    for y in 0..ph {
        for x in 0..pw {
            let from = (y.min(h - 1) * w + x.min(w - 1)) * 4;
            padded[(y * pw + x) * 4..][..4].copy_from_slice(&rgba[from..][..4]);
        }
    }
    let opaque = rgba[..w * h * 4].chunks_exact(4).all(|p| p[3] == 255);
    let preset = if opaque {
        intel_tex_2::astc::opaque_fast_settings(side as u32, side as u32)
    } else {
        intel_tex_2::astc::alpha_fast_settings(side as u32, side as u32)
    };
    let settings = Settings {
        block_width: preset.block_width as i32,
        block_height: preset.block_height as i32,
        channels: preset.channels as i32,
        fast_skip_threshold: preset.fast_skip_threshold as i32,
        refine_iterations: preset.refine_iterations as i32,
    };
    let surface = Surface {
        pixels: padded.as_ptr(),
        width: pw as i32,
        height: ph as i32,
        stride: (pw * 4) as i32,
    };
    let mut output = vec![0; format.level_size(width, height)];
    // The padded surface contains whole blocks. The C interface writes exactly 16 bytes per
    // block; all working buffers and settings are local to this call.
    unsafe {
        compress_blocks(&surface, output.as_mut_ptr(), &settings);
    }
    output
}

// Validate the 2D ASTC bit budget before the byte decoder reads variable-length fields.
// The format allows at most 64 weights in 24..96 bits, and at most 18 endpoint values.
// Void-extent blocks have a separate constant-colour payload.
fn valid_header(block: &[u8]) -> bool {
    const MAX_WEIGHTS: u32 = 64;
    const MIN_WEIGHT_BITS: u32 = 24;
    const MAX_WEIGHT_BITS: u32 = 96;
    const MAX_ENDPOINT_VALUES: u32 = 18;
    const BLOCK_BITS: u32 = 128;
    let bits = u128::from_le_bytes(block.try_into().unwrap());
    let field = |at: u32, mask: u32| ((bits >> at) as u32) & mask;
    let mode = field(0, 2047);
    if mode & 511 == 508 {
        return true;
    }
    let mut high = field(9, 1);
    let mut dual = field(10, 1);
    let a = field(5, 3);
    let base;
    let (x, y);
    if mode & 3 != 0 {
        base = field(4, 1) | (mode & 3) * 2;
        let b = field(7, 3);
        (x, y) = match field(2, 3) {
            0 => (b + 4, a + 2),
            1 => (b + 8, a + 2),
            2 => (a + 2, b + 8),
            _ if mode & 256 != 0 => ((b & 1) + 2, a + 2),
            _ => (a + 2, (b & 1) + 6),
        };
    } else {
        base = field(4, 1) | field(2, 3) * 2;
        (x, y) = match field(7, 3) {
            0 => (12, a + 2),
            1 => (a + 2, 12),
            2 => {
                high = 0;
                dual = 0;
                (a + 6, field(9, 3) + 6)
            }
            _ if a == 0 => (6, 10),
            _ if a == 1 => (10, 6),
            _ => return false,
        };
    }
    if base < 2 {
        return false;
    }
    let count = x * y * (dual + 1);
    if count > MAX_WEIGHTS {
        return false;
    }
    let quant = base - 2 + high * 6;
    // Integer sequence sizes for 2,3,4,5,6,8,10,12,16,20,24,32 weight levels.
    let (binary, trits, quints) = match quant {
        0 => (1, 0, 0),
        1 => (0, 1, 0),
        2 => (2, 0, 0),
        3 => (0, 0, 1),
        4 => (1, 1, 0),
        5 => (3, 0, 0),
        6 => (1, 0, 1),
        7 => (2, 1, 0),
        8 => (4, 0, 0),
        9 => (2, 0, 1),
        10 => (3, 1, 0),
        11 => (5, 0, 0),
        _ => return false,
    };
    let weight_bits =
        count * binary + (count * 8).div_ceil(5) * trits + (count * 7).div_ceil(3) * quints;
    if !(MIN_WEIGHT_BITS..=MAX_WEIGHT_BITS).contains(&weight_bits) {
        return false;
    }
    let partitions = field(11, 3) + 1;
    if partitions == 4 && dual != 0 {
        return false;
    }
    let (config, endpoints) = if partitions == 1 {
        (17, 2 + 2 * field(15, 3))
    } else {
        let class = field(23, 3);
        if class == 0 {
            (29, partitions * (2 + 2 * field(27, 3)))
        } else {
            let endpoints = (0..partitions)
                .map(|i| 2 + 2 * (class - 1 + field(25 + i, 1)))
                .sum();
            (25 + partitions * 3, endpoints)
        }
    };
    let config = config + dual * 2;
    let min_endpoint_bits = endpoints + (endpoints * 8).div_ceil(5);
    endpoints <= MAX_ENDPOINT_VALUES && config + weight_bits + min_endpoint_bits <= BLOCK_BITS
}

pub(super) fn decode(blocks: &[u8], width: u32, height: u32, format: BlockFormat) -> Vec<u8> {
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let side = format.footprint() as usize;
    let across = w.div_ceil(side);
    let mut output = vec![0; w * h * 4];
    for (i, block) in blocks
        .chunks_exact(16)
        .take(across * h.div_ceil(side))
        .enumerate()
    {
        let mut image = [0u32; 64];
        if valid_header(block) {
            texture2ddecoder::decode_astc_block(block, side, side, &mut image);
        } else {
            image.fill(0xffff00ff); // The ASTC error colour: opaque magenta.
        }
        let (bx, by) = (i % across * side, i / across * side);
        for y in 0..side.min(h - by) {
            for x in 0..side.min(w - bx) {
                let [b, g, r, a] = image[y * side + x].to_le_bytes();
                output[((by + y) * w + bx + x) * 4..][..4].copy_from_slice(&[r, g, b, a]);
            }
        }
    }
    output
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn malformed_payloads_do_not_abort_raw_block_operations() {
        for format in [
            BlockFormat::Astc4x4,
            BlockFormat::Astc5x5,
            BlockFormat::Astc6x6,
            BlockFormat::Astc8x8,
        ] {
            let side = format.footprint();
            assert_eq!(
                decode(&[167; 16], side, side, format),
                [255, 0, 255, 255].repeat((side * side) as usize)
            );
            let mut seed = 31u64;
            for _ in 0..4096 {
                let mut block = [0u8; 16];
                for byte in &mut block {
                    seed = seed.wrapping_mul(6364136223846793005).wrapping_add(1);
                    *byte = (seed >> 32) as u8;
                }
                assert_eq!(
                    decode(&block, side, side, format).len(),
                    (side * side * 4) as usize
                );
            }
        }
    }
    #[test]
    fn encoded_rectangles_pass_the_block_budget_checks() {
        let rgba: Vec<u8> = (0..9)
            .flat_map(|y| {
                (0..13).flat_map(move |x| {
                    [
                        (x * 17) as u8,
                        (y * 23) as u8,
                        ((x ^ y) * 11) as u8,
                        (x * y * 39) as u8,
                    ]
                })
            })
            .collect();
        for format in [
            BlockFormat::Astc4x4,
            BlockFormat::Astc5x5,
            BlockFormat::Astc6x6,
            BlockFormat::Astc8x8,
        ] {
            let encoded = compress(&rgba, 13, 9, format);
            assert!(encoded.chunks_exact(16).all(valid_header));
        }
    }
    #[test]
    fn fast_rgba_block_matches_the_reference() {
        let rgba = [
            81, 234, 124, 196, 213, 15, 76, 226, 113, 35, 178, 174, 17, 227, 79, 39, 134, 230, 98,
            198, 92, 118, 21, 216, 31, 168, 187, 47, 11, 70, 196, 92, 198, 253, 52, 69, 174, 186,
            137, 9, 89, 201, 47, 172, 82, 5, 102, 77, 19, 49, 242, 64, 205, 218, 170, 118, 31, 134,
            16, 37, 228, 32, 51, 251,
        ];
        assert_eq!(
            compress(&rgba, 4, 4, BlockFormat::Astc4x4),
            [
                67, 132, 99, 156, 201, 37, 190, 35, 144, 187, 172, 40, 169, 29, 98, 51
            ]
        );
    }
    #[test]
    fn tiny_reference_uses_clamped_six_by_six_padding() {
        let rgba = [
            49, 161, 205, 19, 190, 18, 237, 66, 108, 78, 116, 146, 19, 37, 34, 46,
        ];
        assert_eq!(
            compress(&rgba, 2, 2, BlockFormat::Astc6x6),
            [
                98, 132, 249, 128, 12, 250, 32, 144, 32, 176, 34, 144, 32, 176, 238, 78
            ]
        );
    }
    #[test]
    fn small_and_rectangular_levels_keep_whole_blocks() {
        for format in [
            BlockFormat::Astc4x4,
            BlockFormat::Astc5x5,
            BlockFormat::Astc6x6,
            BlockFormat::Astc8x8,
        ] {
            assert!(!format.is_bc());
            assert_eq!(
                BlockFormat::from_unity_format(format.unity_format()),
                Some(format)
            );
            let rgba = [70, 130, 90, 61].repeat(3 * 7);
            let blocks = compress(&rgba, 3, 7, format);
            assert_eq!(blocks.len(), format.level_size(3, 7));
            assert_eq!(decode(&blocks, 3, 7, format).len(), rgba.len());
            assert_eq!(decode(&[], 3, 7, format), vec![0; rgba.len()]);
        }
    }
}
