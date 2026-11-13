//! Photoshop files (PSD, and PSB with its wider lengths): the flattened image a file stores after
//! its layers, which is what Unity imports. Grayscale and RGB, 8 and 16 bits per channel, raw or
//! PackBits-compressed. A 16-bit channel v becomes the byte nearest v / 257 and a gray texel
//! (g, g, g, 1), as in the Unity 6000.0.71f1 Editor (tests/projects/textureimport Psd16*,
//! PsdGrayAlpha*).
use anyhow::{Context, Result, bail, ensure};

/// A cursor over big-endian file data.
struct Reader<'a> {
    data: &'a [u8],
    at: usize,
}

impl<'a> Reader<'a> {
    fn take(&mut self, n: usize) -> Result<&'a [u8]> {
        let end = self.at.checked_add(n).context("psd: length overflow")?;
        let bytes = self.data.get(self.at..end).context("psd: truncated file")?;
        self.at = end;
        Ok(bytes)
    }

    fn u16(&mut self) -> Result<u16> {
        Ok(u16::from_be_bytes(self.take(2)?.try_into().unwrap()))
    }

    fn u32(&mut self) -> Result<u32> {
        Ok(u32::from_be_bytes(self.take(4)?.try_into().unwrap()))
    }

    fn u64(&mut self) -> Result<u64> {
        Ok(u64::from_be_bytes(self.take(8)?.try_into().unwrap()))
    }

    /// Skips a section that starts with its length (4 bytes; 8 in a PSB when `wide`).
    fn skip_section(&mut self, wide: bool) -> Result<()> {
        let len = if wide {
            self.u64()?
        } else {
            u64::from(self.u32()?)
        };
        self.take(usize::try_from(len).context("psd: section too large")?)?;
        Ok(())
    }
}

/// Unpacks one PackBits row of `len` bytes.
fn unpack_bits(packed: &[u8], len: usize, out: &mut Vec<u8>) -> Result<()> {
    let start = out.len();
    let mut i = 0;
    while out.len() - start < len {
        let n = *packed.get(i).context("psd: truncated PackBits row")? as i8;
        i += 1;
        match n {
            0..=127 => {
                let count = n as usize + 1;
                out.extend_from_slice(
                    packed
                        .get(i..i + count)
                        .context("psd: truncated PackBits row")?,
                );
                i += count;
            }
            -127..=-1 => {
                let byte = *packed.get(i).context("psd: truncated PackBits row")?;
                i += 1;
                out.extend(std::iter::repeat_n(byte, (1 - n as isize) as usize));
            }
            // -128 is a no-op.
            _ => {}
        }
    }
    // A run past the row's end belongs to no texel.
    out.truncate(start + len);
    Ok(())
}

/// The largest side of a PSD and of a PSB (the file format's limits).
const MAX_SIDE: [u32; 2] = [30_000, 300_000];
/// The most channels a file may have.
const MAX_CHANNELS: usize = 56;

/// The flattened image as RGBA8 rows top-down: (width, height, texels). With `remove_matte`
/// (`TextureImporter`'s Remove Matte) the white Photoshop blends into the flattened colour of a
/// file whose first extra channel is the flattened transparency (a negative layer count) is taken
/// out again.
pub(super) fn decode(bytes: &[u8], remove_matte: bool) -> Result<(u32, u32, Vec<u8>)> {
    let mut r = Reader { data: bytes, at: 0 };
    ensure!(r.take(4)? == b"8BPS", "psd: not a Photoshop file");
    let version = r.u16()?;
    ensure!(
        version == 1 || version == 2,
        "psd: unknown version {version}"
    );
    let wide = version == 2;
    r.take(6)?;
    let channels = usize::from(r.u16()?);
    let height = r.u32()?;
    let width = r.u32()?;
    let depth = r.u16()?;
    let mode = r.u16()?;
    let max_side = MAX_SIDE[usize::from(wide)];
    ensure!(
        (1..=max_side).contains(&width) && (1..=max_side).contains(&height),
        "psd: invalid size {width}x{height}"
    );
    ensure!(
        (1..=MAX_CHANNELS).contains(&channels),
        "psd: invalid channel count {channels}"
    );
    // Color mode data, image resources, then layers and masks, whose layer count is negative
    // when the first extra channel holds the flattened image's transparency.
    r.skip_section(false)?;
    r.skip_section(false)?;
    let layers_len = if wide { r.u64()? } else { u64::from(r.u32()?) };
    let layers_at = r.at;
    let merged_alpha = layers_len >= if wide { 10 } else { 6 } && {
        let layer_info_len = if wide { r.u64()? } else { u64::from(r.u32()?) };
        layer_info_len >= 2 && (r.u16()? as i16) < 0
    };
    r.at = layers_at;
    r.take(usize::try_from(layers_len).context("psd: section too large")?)?;
    let colour = match (mode, channels) {
        (1, 1..) => 1,
        (3, 3..) => 3,
        (1 | 3, _) => bail!("psd: {channels} channels for colour mode {mode}"),
        // Bitmap, indexed, CMYK, multichannel, duotone and Lab images.
        _ => bail!("psd: colour mode {mode} is not supported (grayscale and RGB are)"),
    };
    let sample = match depth {
        8 => 1,
        16 => 2,
        _ => bail!("psd: {depth}-bit channels are not supported (8 and 16 bits are)"),
    };
    let (w, h) = (width as usize, height as usize);
    let row = w * sample;
    let plane = row * h;
    // RGB reads its colour and the first extra channel as alpha (whatever it holds); grayscale
    // reads its gray alone, opaque (the Unity 6000.0.71f1 Editor, tests/projects/textureimport
    // PsdMatte5, PsdGrayAlpha).
    let used = if colour == 3 { channels.min(4) } else { 1 };
    let compression = r.u16()?;
    let total = plane.checked_mul(used).context("psd: image too large")?;
    let planes: Vec<u8> = match compression {
        0 => r.take(total)?.to_vec(),
        1 => {
            // Row lengths of every channel; only those of the channels read are kept.
            let mut lengths = Vec::with_capacity(h * used);
            for i in 0..h * channels {
                let len = if wide {
                    r.u32()? as usize
                } else {
                    usize::from(r.u16()?)
                };
                if i < h * used {
                    lengths.push(len);
                }
            }
            let mut out = Vec::with_capacity(total.min(bytes.len().saturating_mul(128)));
            for &len in &lengths {
                unpack_bits(r.take(len)?, row, &mut out)?;
            }
            out
        }
        _ => bail!("psd: image data compression {compression} is not supported"),
    };
    let max = if sample == 1 { 255 } else { 65535 };
    let value = |channel: usize, texel: usize| -> u32 {
        let at = channel * plane + texel * sample;
        if sample == 1 {
            u32::from(planes[at])
        } else {
            u32::from(u16::from_be_bytes([planes[at], planes[at + 1]]))
        }
    };
    let unmatte = remove_matte && merged_alpha && used == 4;
    // A 16-bit channel becomes the byte nearest v / 257 (tests/projects/textureimport Psd16*).
    let narrow = |v: u32| {
        if sample == 1 {
            v as u8
        } else {
            super::narrow_u16(v as u16)
        }
    };
    let mut rgba = Vec::with_capacity(w * h * 4);
    for texel in 0..w * h {
        let alpha = if used == 4 { value(3, texel) } else { max };
        let mut c = [0u32; 3];
        for (k, v) in c.iter_mut().enumerate() {
            *v = value(if colour == 1 { 0 } else { k }, texel);
        }
        if unmatte && alpha != 0 && alpha != max {
            c = c.map(|v| remove_white(v, alpha, max));
        }
        rgba.extend_from_slice(&[narrow(c[0]), narrow(c[1]), narrow(c[2]), narrow(alpha)]);
    }
    Ok((width, height, rgba))
}

/// A channel of colour blended over white at `alpha` (both of `max` steps) back to the colour:
/// v / a + max (1 - 1 / a), a = alpha / max, in single precision, truncated and kept as its low
/// bits (matches the Unity 6000.0.71f1 Editor, tests/projects/textureimport PsdMatteOn).
fn remove_white(v: u32, alpha: u32, max: u32) -> u32 {
    let max_f = max as f32;
    let inverse = 1.0 / (alpha as f32 / max_f);
    let white = (1.0 - inverse) * max_f;
    (((v as f32 * inverse + white) as i32) as u32) & max
}

#[cfg(test)]
mod tests {
    use super::*;

    fn file(
        channels: u16,
        depth: u16,
        mode: u16,
        rle: bool,
        planes: &[Vec<u8>],
        w: u32,
    ) -> Vec<u8> {
        let h = (planes[0].len() / (w as usize * usize::from(depth / 8))) as u32;
        let mut out = b"8BPS".to_vec();
        out.extend_from_slice(&1u16.to_be_bytes());
        out.extend_from_slice(&[0; 6]);
        out.extend_from_slice(&channels.to_be_bytes());
        out.extend_from_slice(&h.to_be_bytes());
        out.extend_from_slice(&w.to_be_bytes());
        out.extend_from_slice(&depth.to_be_bytes());
        out.extend_from_slice(&mode.to_be_bytes());
        // Empty colour mode data and image resources; a layer section of 4 bytes.
        out.extend_from_slice(&[0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 9, 9, 9, 9]);
        out.extend_from_slice(&u16::from(rle).to_be_bytes());
        if rle {
            let row = planes[0].len() / h as usize;
            let packed: Vec<Vec<u8>> = planes
                .iter()
                .flat_map(|p| p.chunks(row))
                .map(|r| {
                    let mut v = vec![(r.len() - 1) as u8];
                    v.extend_from_slice(r);
                    v
                })
                .collect();
            for p in &packed {
                out.extend_from_slice(&(p.len() as u16).to_be_bytes());
            }
            for p in &packed {
                out.extend_from_slice(p);
            }
        } else {
            for p in planes {
                out.extend_from_slice(p);
            }
        }
        out
    }

    fn decode_plain(bytes: &[u8]) -> Result<(u32, u32, Vec<u8>)> {
        decode(bytes, false)
    }

    #[test]
    fn sixteen_bit_channels_round_to_the_nearest_byte() {
        // 0x807f / 257 = 127.9 -> 128; 0x8080 -> 128.0 -> 128; 0x7f80 = 32640 -> 127.0 -> 127;
        // 0x00ff -> 0.99 -> 1 (a shift would give 0).
        let r = vec![0x80, 0x7f, 0x80, 0x80];
        let g = vec![0x7f, 0x80, 0x00, 0xff];
        let b = vec![0xff, 0xff, 0x00, 0x00];
        let (w, h, rgba) = decode_plain(&file(3, 16, 3, false, &[r, g, b], 2)).unwrap();
        assert_eq!((w, h), (2, 1));
        assert_eq!(rgba, [128, 127, 255, 255, 128, 1, 0, 255]);
    }

    #[test]
    fn gray_reads_opaque_and_ignores_extra_channels() {
        let gray = vec![10, 20, 30, 40];
        let alpha = vec![255, 0, 128, 7];
        let (_, h, rgba) = decode_plain(&file(2, 8, 1, true, &[gray, alpha], 2)).unwrap();
        assert_eq!(h, 2);
        assert_eq!(
            rgba,
            [
                10, 10, 10, 255, 20, 20, 20, 255, 30, 30, 30, 255, 40, 40, 40, 255
            ]
        );
    }

    /// `file` with a layer section whose layer count is `count`.
    fn with_layer_count(mut bytes: Vec<u8>, count: i16) -> Vec<u8> {
        // The layer section (4 bytes of 9) starts after the header (26) and two empty sections.
        let at = 26 + 8;
        let section = [&[0u8, 0, 0, 6][..], &[0, 0, 0, 2], &count.to_be_bytes()].concat();
        bytes.splice(at..at + 8, section);
        bytes
    }

    #[test]
    fn remove_matte_needs_the_setting_and_the_merged_transparency_mark() {
        // Colour 200 at alpha 128 over white: 200 * 255 / 128 + 255 * (1 - 255 / 128) = 145.4.
        let planes = vec![vec![200, 7], vec![200, 7], vec![200, 7], vec![128, 255]];
        let flat = file(4, 8, 3, false, &planes, 2);
        let marked = with_layer_count(flat.clone(), -1);
        let texel = |bytes: &[u8], matte| decode(bytes, matte).unwrap().2[..4].to_vec();
        assert_eq!(texel(&marked, true), [145, 145, 145, 128]);
        assert_eq!(texel(&marked, false), [200, 200, 200, 128]);
        assert_eq!(texel(&flat, true), [200, 200, 200, 128]);
        assert_eq!(
            texel(&with_layer_count(flat, 1), true),
            [200, 200, 200, 128]
        );
        // Opaque texels keep their colour.
        assert_eq!(decode(&marked, true).unwrap().2[4..], [7, 7, 7, 255]);
        // Below the white, the result keeps its low bits (as the Editor's does).
        // 10 * 1.992 - 252.99 = -233.07: -233 keeps its low byte, 23.
        assert_eq!(remove_white(10, 128, 255), 23);
    }

    #[test]
    fn psb_files_have_wide_lengths() {
        // Version 2: an 8-byte layer section length, 4-byte PackBits row lengths.
        let mut out = b"8BPS".to_vec();
        out.extend_from_slice(&2u16.to_be_bytes());
        out.extend_from_slice(&[0; 6]);
        out.extend_from_slice(&3u16.to_be_bytes());
        out.extend_from_slice(&1u32.to_be_bytes());
        out.extend_from_slice(&2u32.to_be_bytes());
        out.extend_from_slice(&8u16.to_be_bytes());
        out.extend_from_slice(&3u16.to_be_bytes());
        out.extend_from_slice(&[0; 8]);
        out.extend_from_slice(&0u64.to_be_bytes());
        out.extend_from_slice(&1u16.to_be_bytes());
        for _ in 0..3 {
            out.extend_from_slice(&3u32.to_be_bytes());
        }
        for v in [1u8, 2, 3] {
            out.extend_from_slice(&[1, v, v]);
        }
        let (w, h, rgba) = decode_plain(&out).unwrap();
        assert_eq!((w, h), (2, 1));
        assert_eq!(rgba, [1, 2, 3, 255, 1, 2, 3, 255]);
    }

    #[test]
    fn sizes_past_the_format_limits_fail_before_reading() {
        let mut huge = file(4, 16, 3, false, &vec![vec![0; 4]; 4], 1);
        huge[14..18].copy_from_slice(&0x8000_0000u32.to_be_bytes());
        huge[18..22].copy_from_slice(&0x8000_0000u32.to_be_bytes());
        assert!(
            decode_plain(&huge)
                .unwrap_err()
                .to_string()
                .contains("invalid size")
        );
        let mut tall = file(4, 8, 3, true, &vec![vec![0; 2]; 4], 2);
        tall[14..18].copy_from_slice(&u32::MAX.to_be_bytes());
        assert!(decode_plain(&tall).is_err());
        let mut channels = file(3, 8, 3, false, &vec![vec![0; 2]; 3], 2);
        channels[12..14].copy_from_slice(&57u16.to_be_bytes());
        assert!(decode_plain(&channels).is_err());
    }

    #[test]
    fn packbits_runs_repeat_and_stop_at_the_row() {
        let mut out = Vec::new();
        // A run of 3 x 5, then a literal of 2 bytes of which only one fits the row.
        unpack_bits(&[0xfe, 5, 1, 6, 7], 4, &mut out).unwrap();
        assert_eq!(out, [5, 5, 5, 6]);
        assert!(unpack_bits(&[0x05, 1], 6, &mut Vec::new()).is_err());
    }

    #[test]
    fn unsupported_files_fail_with_a_reason() {
        let cmyk = file(4, 8, 4, false, &vec![vec![0; 4]; 4], 2);
        assert!(
            decode_plain(&cmyk)
                .unwrap_err()
                .to_string()
                .contains("colour mode 4")
        );
        let deep = file(3, 32, 3, false, &vec![vec![0; 8]; 3], 2);
        assert!(
            decode_plain(&deep)
                .unwrap_err()
                .to_string()
                .contains("32-bit")
        );
        assert!(decode_plain(&cmyk[..30]).is_err());
    }
}
