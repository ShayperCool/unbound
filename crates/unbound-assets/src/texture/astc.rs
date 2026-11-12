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
        texture2ddecoder::decode_astc_block(block, side, side, &mut image);
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
