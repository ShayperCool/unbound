//! Block formats Unbound keeps only as a layered texture's storage (Texture2DArray,
//! CubemapArray, Texture3D blocks): ETC2 RGBA1 (punch-through alpha), signed EAC R11 and RG11,
//! and PVRTC at 2 and 4 bits a texel. They are decoded for reading and for the renderer, which
//! samples none of them. ETC2 RGBA1 and PVRTC decode with `texture2ddecoder` (MIT OR
//! Apache-2.0); signed EAC with the EAC definition below.

use anyhow::{Context, Result, bail};

const PVRTC_RGB2: i32 = 30;
const PVRTC_RGBA2: i32 = 31;
const PVRTC_RGB4: i32 = 32;
const PVRTC_RGBA4: i32 = 33;
const EAC_R_SIGNED: i32 = 42;
const EAC_RG_SIGNED: i32 = 44;
const ETC2_RGBA1: i32 = 46;

/// Whether Unity's `TextureFormat` `format` is one of this module's.
pub fn is_layered_block_format(format: i32) -> bool {
    matches!(
        format,
        PVRTC_RGB2
            | PVRTC_RGBA2
            | PVRTC_RGB4
            | PVRTC_RGBA4
            | EAC_R_SIGNED
            | EAC_RG_SIGNED
            | ETC2_RGBA1
    )
}

fn is_pvrtc(format: i32) -> bool {
    (PVRTC_RGB2..=PVRTC_RGBA4).contains(&format)
}

/// One `width` x `height` level as RGBA8 texels, rows in the blocks' order (as GetPixels32 reads
/// it): ETC2 RGBA1 and PVRTC. Signed EAC has no such reading (the Editor refuses it).
pub fn decode_rgba(format: i32, data: &[u8], width: u32, height: u32) -> Result<Vec<u8>> {
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let mut image = vec![0u32; w * h];
    if format == ETC2_RGBA1 {
        let columns = w.div_ceil(4);
        for (i, block) in data.chunks_exact(8).enumerate() {
            let (bx, by) = (i % columns * 4, i / columns * 4);
            if by >= h {
                break;
            }
            let mut texels = [0u32; 16];
            texture2ddecoder::decode_etc2_rgba1_block(block, &mut texels);
            for y in 0..4.min(h - by) {
                for x in 0..4.min(w - bx) {
                    image[(by + y) * w + bx + x] = texels[y * 4 + x];
                }
            }
        }
    } else if is_pvrtc(format) {
        // PVRTC levels hold at least 8x8 texels at 4 bits (16x8 at 2): a smaller level decodes
        // at that size and keeps its corner.
        let two = matches!(format, PVRTC_RGB2 | PVRTC_RGBA2);
        let (pw, ph) = (w.max(if two { 16 } else { 8 }), h.max(8));
        let mut padded = vec![0u32; pw * ph];
        texture2ddecoder::decode_pvrtc(data, pw, ph, &mut padded, two)
            .map_err(anyhow::Error::msg)
            .with_context(|| format!("PVRTC level {width}x{height}"))?;
        for y in 0..h {
            image[y * w..][..w].copy_from_slice(&padded[y * pw..][..w]);
        }
    } else {
        bail!("texture format {format} has no RGBA8 reading");
    }
    Ok(image
        .into_iter()
        .flat_map(|p| {
            let [b, g, r, a] = p.to_le_bytes();
            [r, g, b, a]
        })
        .collect())
}

/// The bytes of a block whose texels decode one by one ([`decode_texel`]): ETC2 RGBA1's.
pub fn texel_block_bytes(format: i32) -> Option<usize> {
    (format == ETC2_RGBA1).then_some(8)
}

/// Texel `index` (row-major in the 4x4 block) of an ETC2 RGBA1 block as floats.
pub fn decode_texel(format: i32, block: &[u8], index: usize) -> Option<[f32; 4]> {
    if format != ETC2_RGBA1 || block.len() < 8 || index >= 16 {
        return None;
    }
    let mut texels = [0u32; 16];
    texture2ddecoder::decode_etc2_rgba1_block(block, &mut texels);
    let [b, g, r, a] = texels[index].to_le_bytes();
    Some([r, g, b, a].map(|c| f32::from(c) / 255.0))
}

/// A signed EAC R11 texel of the block at (`x`, `y`), from -1023 to 1023: the base codeword is
/// a signed byte (-128 taken as -127), the modifier scaled by the multiplier times 8 (by 1 when
/// the multiplier is 0).
fn signed_eac_texel(block: &[u8], x: usize, y: usize) -> i32 {
    let table = (block[1] & 15) as usize;
    let multiplier = i32::from(block[1] >> 4);
    let bits = u64::from_be_bytes([
        0, 0, block[2], block[3], block[4], block[5], block[6], block[7],
    ]);
    let index = ((bits >> ((15 - (x * 4 + y)) * 3)) & 7) as usize;
    let modifier = super::etc::eac_modifiers(table)[index];
    let base = i32::from(block[0] as i8).max(-127);
    (base * 8 + modifier * if multiplier == 0 { 1 } else { multiplier * 8 }).clamp(-1023, 1023)
}

/// One level of signed EAC as tightly packed signed normalized 16-bit red (EAC_R_SIGNED) or
/// red/green (EAC_RG_SIGNED) channels, for the renderer.
pub fn decode_signed_eac_gpu(
    format: i32,
    blocks: &[u8],
    width: u32,
    height: u32,
) -> Result<Vec<u8>> {
    let channels = match format {
        EAC_R_SIGNED => 1,
        EAC_RG_SIGNED => 2,
        _ => bail!("texture format {format} is not signed EAC"),
    };
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let mut output = vec![0; w * h * channels * 2];
    let columns = w.div_ceil(4);
    for (i, block) in blocks.chunks_exact(8 * channels).enumerate() {
        let (bx, by) = (i % columns * 4, i / columns * 4);
        if by >= h {
            break;
        }
        for y in 0..4.min(h - by) {
            for x in 0..4.min(w - bx) {
                for channel in 0..channels {
                    let value = signed_eac_texel(&block[channel * 8..], x, y);
                    let snorm = ((value * 32767) as f32 / 1023.0).round() as i16;
                    let at = (((by + y) * w + bx + x) * channels + channel) * 2;
                    output[at..at + 2].copy_from_slice(&snorm.to_le_bytes());
                }
            }
        }
    }
    Ok(output)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A signed EAC block's extremes and its multiplier-0 step: base -128 reads as -127, the
    /// largest modifier saturates at 1023, and the channels pack as 16-bit snorm.
    #[test]
    fn signed_eac_follows_the_definition() {
        // Table 0's index 0 is -3; all indices 0.
        let mut block = [0u8; 8];
        block[0] = 0x80; // -128, taken as -127
        block[1] = 0x10; // multiplier 1, table 0
        assert_eq!(signed_eac_texel(&block, 0, 0), -1023);
        block[0] = 0x9c; // -100
        assert_eq!(signed_eac_texel(&block, 0, 0), -100 * 8 - 3 * 8);
        block[0] = 0x81; // -127: the same as -128
        let least = signed_eac_texel(&block, 0, 0);
        block[0] = 0x80;
        block[1] = 0x00;
        assert_eq!(signed_eac_texel(&block, 0, 0), -127 * 8 - 3);
        assert_eq!(least, -1023);
        block[1] = 0x10;
        block[0] = 127;
        // Index 7 everywhere (table 0's largest modifier): 1016 plus it saturates.
        block[2..].copy_from_slice(&[0xff; 6]);
        assert_eq!(signed_eac_texel(&block, 1, 2), 1023);
        block[0] = 100;
        block[1] = 0x00; // multiplier 0: the modifier unscaled
        assert_eq!(signed_eac_texel(&block, 3, 3), 800 + 14);
        let packed = decode_signed_eac_gpu(EAC_R_SIGNED, &block, 4, 4).unwrap();
        assert_eq!(packed.len(), 32);
        assert_eq!(
            i16::from_le_bytes([packed[0], packed[1]]),
            (814.0f32 * 32767.0 / 1023.0).round() as i16
        );
        assert!(decode_rgba(EAC_R_SIGNED, &block, 4, 4).is_err());
    }

    /// PVRTC levels below their least size decode at it and keep the corner; ETC2 RGBA1 decodes
    /// through its punch-through alpha.
    #[test]
    fn small_pvrtc_levels_and_etc2_rgba1_decode() {
        let data: Vec<u8> = (0..32).map(|i| (i * 37 + 11) as u8).collect();
        for format in [PVRTC_RGB2, PVRTC_RGBA4] {
            let full = decode_rgba(format, &data, 8, 8).unwrap();
            let corner = decode_rgba(format, &data, 4, 4).unwrap();
            assert_eq!(corner.len(), 64);
            for y in 0..4 {
                assert_eq!(corner[y * 16..][..16], full[y * 32..][..16]);
            }
        }
        let rgba1 = decode_rgba(ETC2_RGBA1, &data[..8], 4, 4).unwrap();
        assert_eq!(
            decode_texel(ETC2_RGBA1, &data[..8], 5).unwrap()[3] * 255.0,
            f32::from(rgba1[5 * 4 + 3])
        );
        assert!(is_layered_block_format(ETC2_RGBA1) && !is_layered_block_format(41));
    }
}
