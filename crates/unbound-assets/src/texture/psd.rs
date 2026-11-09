//! Photoshop files (PSD, and PSB with its wider lengths): the flattened image a file stores after
//! its layers, which is what Unity imports. Grayscale and RGB, 8 and 16 bits per channel, raw or
//! PackBits-compressed. A 16-bit channel v becomes the byte nearest v / 257 and a gray texel
//! (g, g, g, alpha), as in the Unity 6000.0.71f1 Editor (tests/projects/textureimport Psd16*).
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
        let len = if wide { self.u64()? } else { u64::from(self.u32()?) };
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
                out.extend_from_slice(packed.get(i..i + count).context("psd: truncated PackBits row")?);
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

/// The flattened image as RGBA8 rows top-down: (width, height, texels).
pub(super) fn decode(bytes: &[u8]) -> Result<(u32, u32, Vec<u8>)> {
    let mut r = Reader { data: bytes, at: 0 };
    ensure!(r.take(4)? == b"8BPS", "psd: not a Photoshop file");
    let version = r.u16()?;
    ensure!(version == 1 || version == 2, "psd: unknown version {version}");
    let wide = version == 2;
    r.take(6)?;
    let channels = usize::from(r.u16()?);
    let height = r.u32()?;
    let width = r.u32()?;
    let depth = r.u16()?;
    let mode = r.u16()?;
    // Color mode data, image resources, layers and masks: the flattened image follows them.
    r.skip_section(false)?;
    r.skip_section(false)?;
    r.skip_section(wide)?;
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
    ensure!(w > 0 && h > 0, "psd: empty image");
    let row = w * sample;
    let plane = row * h;
    // Only the colour channels and the first extra one (alpha) are read.
    let used = (colour + 1).min(channels);
    let compression = r.u16()?;
    let planes: Vec<u8> = match compression {
        0 => r.take(plane * used)?.to_vec(),
        1 => {
            let rows = h * channels;
            let mut lengths = Vec::with_capacity(rows);
            for _ in 0..rows {
                lengths.push(if wide { r.u32()? as usize } else { usize::from(r.u16()?) });
            }
            let mut out = Vec::with_capacity(plane * used);
            for &len in &lengths[..h * used] {
                unpack_bits(r.take(len)?, row, &mut out)?;
            }
            out
        }
        _ => bail!("psd: image data compression {compression} is not supported"),
    };
    let value = |channel: usize, texel: usize| -> u8 {
        let at = channel * plane + texel * sample;
        if sample == 1 {
            planes[at]
        } else {
            let v = u32::from(u16::from_be_bytes([planes[at], planes[at + 1]]));
            ((v + 128) / 257) as u8
        }
    };
    let mut rgba = Vec::with_capacity(w * h * 4);
    for texel in 0..w * h {
        let alpha = if used > colour { value(colour, texel) } else { 255 };
        if colour == 1 {
            let g = value(0, texel);
            rgba.extend_from_slice(&[g, g, g, alpha]);
        } else {
            rgba.extend_from_slice(&[value(0, texel), value(1, texel), value(2, texel), alpha]);
        }
    }
    Ok((width, height, rgba))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn file(channels: u16, depth: u16, mode: u16, rle: bool, planes: &[Vec<u8>], w: u32) -> Vec<u8> {
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

    #[test]
    fn sixteen_bit_channels_round_to_the_nearest_byte() {
        // 0x807f / 257 = 127.9 -> 128; 0x8080 -> 128.0 -> 128; 0x7f80 = 32640 -> 127.0 -> 127;
        // 0x00ff -> 0.99 -> 1 (a shift would give 0).
        let r = vec![0x80, 0x7f, 0x80, 0x80];
        let g = vec![0x7f, 0x80, 0x00, 0xff];
        let b = vec![0xff, 0xff, 0x00, 0x00];
        let (w, h, rgba) = decode(&file(3, 16, 3, false, &[r, g, b], 2)).unwrap();
        assert_eq!((w, h), (2, 1));
        assert_eq!(rgba, [128, 127, 255, 255, 128, 1, 0, 255]);
    }

    #[test]
    fn gray_and_alpha_planes_with_packbits() {
        let gray = vec![10, 20, 30, 40];
        let alpha = vec![255, 0, 128, 7];
        let (_, h, rgba) = decode(&file(2, 8, 1, true, &[gray, alpha], 2)).unwrap();
        assert_eq!(h, 2);
        assert_eq!(rgba, [10, 10, 10, 255, 20, 20, 20, 0, 30, 30, 30, 128, 40, 40, 40, 7]);
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
        assert!(decode(&cmyk).unwrap_err().to_string().contains("colour mode 4"));
        let deep = file(3, 32, 3, false, &vec![vec![0; 8]; 3], 2);
        assert!(decode(&deep).unwrap_err().to_string().contains("32-bit"));
        assert!(decode(&cmyk[..30]).is_err());
    }
}
