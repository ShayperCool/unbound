//! TIFF files with extra samples (an alpha channel, a channel the file leaves unspecified, as
//! Photoshop writes RGBA files, or more than one): the first extra sample is the texture's alpha
//! whatever the file calls it, the others are dropped, and a gray file's gray fills red, green and
//! blue. A gray file keeps one alpha per gray value: that of the last pixel of that value in file
//! order (rows top first). Matches the Unity 6000.0.71f1 Editor (tests/projects/textureimport
//! `Tiff*`; gray measured with 8-bit samples and an unspecified extra sample, 16-bit gray with
//! extra samples is refused). Layouts not read here (tiles, planes, float or other sample depths,
//! other compressions or predictors) go to the image crate for an RGB file with one alpha sample
//! (or four samples without ExtraSamples), which it reads correctly, and are refused otherwise.
//! The tiff crate copies such rows with the stride of the colour samples alone, which shears every row, so
//! their strips are read here. Other TIFF files return `None` and go through the image crate.
use super::narrow_u16;
use anyhow::{Context, Result, bail};
use std::io::{Cursor, Read};
use tiff::decoder::Decoder;
use tiff::tags::Tag;

/// `PhotometricInterpretation` BlackIsZero (gray) and RGB.
const PHOTOMETRIC_GRAY: u16 = 1;
const PHOTOMETRIC_RGB: u16 = 2;
/// `ExtraSamples` values that name an alpha channel (associated, unassociated).
const ALPHA_EXTRA_SAMPLES: [u16; 2] = [1, 2];
/// `PlanarConfiguration` chunky: the samples of a pixel are stored together.
const PLANAR_CHUNKY: u16 = 1;
/// `Compression` values read here.
const COMPRESSION_NONE: u16 = 1;
const COMPRESSION_LZW: u16 = 5;
const COMPRESSION_DEFLATE: u16 = 8;
const COMPRESSION_DEFLATE_OLD: u16 = 32946;
const COMPRESSION_PACKBITS: u16 = 32773;
/// The compressions read here.
const READ_COMPRESSIONS: [u16; 5] = [
    COMPRESSION_NONE,
    COMPRESSION_LZW,
    COMPRESSION_DEFLATE,
    COMPRESSION_DEFLATE_OLD,
    COMPRESSION_PACKBITS,
];
/// `Predictor` horizontal differencing.
const PREDICTOR_HORIZONTAL: u16 = 2;

/// RGBA8 rows (top row first) of a gray or RGB TIFF with extra samples; `None` for every other
/// TIFF.
pub(super) fn decode(bytes: &[u8]) -> Result<Option<(u32, u32, Vec<u8>)>> {
    let mut decoder = Decoder::new(Cursor::new(bytes))?;
    let samples = usize::from(
        decoder
            .find_tag_unsigned::<u16>(Tag::SamplesPerPixel)?
            .unwrap_or(1),
    );
    let colour = match decoder.find_tag_unsigned::<u16>(Tag::PhotometricInterpretation)? {
        Some(PHOTOMETRIC_RGB) => 3,
        Some(PHOTOMETRIC_GRAY) => 1,
        _ => return Ok(None),
    };
    if samples <= colour {
        return Ok(None);
    }
    let what = "TIFF with extra samples";
    let extra: Vec<u16> = decoder
        .find_tag_unsigned_vec(Tag::ExtraSamples)?
        .unwrap_or_default();
    // The image crate reads an RGB file with one extra sample marked as alpha (or without the
    // ExtraSamples tag) correctly, in every layout; the other files with extra samples it shears.
    let crate_reads_it = colour == 3
        && samples == 4
        && extra
            .first()
            .is_none_or(|e| ALPHA_EXTRA_SAMPLES.contains(e));
    let planar = decoder
        .find_tag_unsigned::<u16>(Tag::PlanarConfiguration)?
        .unwrap_or(PLANAR_CHUNKY);
    let depths: Vec<u16> = decoder
        .find_tag_unsigned_vec(Tag::BitsPerSample)?
        .unwrap_or_else(|| vec![1]);
    let depth = depths[0];
    let format = decoder
        .find_tag_unsigned::<u16>(Tag::SampleFormat)?
        .unwrap_or(1);
    let strips: Option<Vec<u64>> = decoder.find_tag_unsigned_vec(Tag::StripOffsets)?;
    let compression = decoder
        .find_tag_unsigned::<u16>(Tag::Compression)?
        .unwrap_or(COMPRESSION_NONE);
    let predictor = decoder
        .find_tag_unsigned::<u16>(Tag::Predictor)?
        .unwrap_or(1);
    let unsupported = if planar != PLANAR_CHUNKY {
        Some(format!("planar configuration {planar}"))
    } else if !matches!(depth, 8 | 16) || depths.iter().any(|&d| d != depth) {
        Some(format!(
            "{depths:?} bits per sample (8 or 16 for every sample are read)"
        ))
    } else if format != 1 {
        Some(format!(
            "sample format {format} (unsigned integers are read)"
        ))
    } else if colour == 1 && depth != 8 {
        Some(format!(
            "gray with {depth}-bit samples (8-bit gray is read)"
        ))
    } else if strips.is_none() {
        Some("tiles (strips are read)".to_string())
    } else if !READ_COMPRESSIONS.contains(&compression) {
        Some(format!("compression {compression}"))
    } else if !matches!(predictor, 1 | PREDICTOR_HORIZONTAL) {
        Some(format!("predictor {predictor}"))
    } else {
        None
    };
    if let Some(unsupported) = unsupported {
        if crate_reads_it {
            return Ok(None);
        }
        bail!("{what}: {unsupported} is not supported");
    }
    let offsets = strips.unwrap_or_default();
    let (width, height) = decoder.dimensions()?;
    let counts: Vec<u64> = decoder
        .find_tag_unsigned_vec(Tag::StripByteCounts)?
        .with_context(|| format!("{what}: no strip byte counts"))?;
    let rows_per_strip = decoder
        .find_tag_unsigned::<u32>(Tag::RowsPerStrip)?
        .unwrap_or(height)
        .clamp(1, height.max(1));
    let needed = height.div_ceil(rows_per_strip) as usize;
    if offsets.len() != counts.len() || offsets.len() < needed {
        bail!(
            "{what}: {} strip offsets and {} byte counts for {needed} strips",
            offsets.len(),
            counts.len()
        );
    }
    let big_endian = decoder.byte_order() == tiff::tags::ByteOrder::BigEndian;

    let bytes_per_sample = usize::from(depth / 8);
    let row_len = width as usize * samples * bytes_per_sample;
    let mut raw = vec![0u8; row_len * height as usize];
    for (strip, (&offset, &count)) in offsets.iter().zip(&counts).enumerate() {
        let first_row = strip * rows_per_strip as usize;
        if first_row >= height as usize {
            break;
        }
        let rows = (rows_per_strip as usize).min(height as usize - first_row);
        let source = usize::try_from(offset)
            .ok()
            .zip(usize::try_from(count).ok())
            .and_then(|(o, c)| bytes.get(o..o.checked_add(c)?))
            .with_context(|| format!("{what}: strip {strip} lies outside the file"))?;
        let target = &mut raw[first_row * row_len..(first_row + rows) * row_len];
        inflate(compression, source, target).with_context(|| format!("{what}: strip {strip}"))?;
    }
    if predictor == PREDICTOR_HORIZONTAL {
        for row in raw.chunks_exact_mut(row_len) {
            undo_horizontal_differencing(row, samples, bytes_per_sample, big_endian);
        }
    }

    let sample = |pixel: &[u8], index: usize| -> u8 {
        if bytes_per_sample == 1 {
            pixel[index]
        } else {
            let pair = [pixel[index * 2], pixel[index * 2 + 1]];
            narrow_u16(if big_endian {
                u16::from_be_bytes(pair)
            } else {
                u16::from_le_bytes(pair)
            })
        }
    };
    let pixels = raw.chunks_exact(samples * bytes_per_sample);
    let mut rgba = Vec::with_capacity(width as usize * height as usize * 4);
    if colour == 3 {
        for pixel in pixels {
            rgba.extend_from_slice(&[
                sample(pixel, 0),
                sample(pixel, 1),
                sample(pixel, 2),
                sample(pixel, 3),
            ]);
        }
    } else {
        let mut alpha_of_gray = [u8::MAX; 256];
        for pixel in pixels.clone() {
            alpha_of_gray[usize::from(pixel[0])] = pixel[1];
        }
        for pixel in pixels {
            rgba.extend_from_slice(&[
                pixel[0],
                pixel[0],
                pixel[0],
                alpha_of_gray[usize::from(pixel[0])],
            ]);
        }
    }
    Ok(Some((width, height, rgba)))
}

/// Decompresses one strip into `target`, which it must fill.
fn inflate(compression: u16, source: &[u8], target: &mut [u8]) -> Result<()> {
    let data = match compression {
        COMPRESSION_NONE => source.to_vec(),
        COMPRESSION_LZW => weezl::decode::Decoder::with_tiff_size_switch(weezl::BitOrder::Msb, 8)
            .decode(source)
            .context("LZW")?,
        COMPRESSION_DEFLATE | COMPRESSION_DEFLATE_OLD => {
            let mut out = Vec::with_capacity(target.len());
            flate2::read::ZlibDecoder::new(source)
                .read_to_end(&mut out)
                .context("Deflate")?;
            out
        }
        COMPRESSION_PACKBITS => unpack_bits(source, target.len()),
        _ => bail!("compression {compression} is not supported"),
    };
    if data.len() < target.len() {
        bail!(
            "{} bytes decoded where {} are needed",
            data.len(),
            target.len()
        );
    }
    target.copy_from_slice(&data[..target.len()]);
    Ok(())
}

/// PackBits runs up to `len` bytes.
fn unpack_bits(mut source: &[u8], len: usize) -> Vec<u8> {
    let mut out = Vec::with_capacity(len);
    while let Some((&header, rest)) = source.split_first() {
        source = rest;
        let n = header as i8;
        if n >= 0 {
            let take = (n as usize + 1).min(source.len());
            out.extend_from_slice(&source[..take]);
            source = &source[take..];
        } else if n != -128
            && let Some((&value, rest)) = source.split_first()
        {
            out.extend(std::iter::repeat_n(value, 1 - n as isize as usize));
            source = rest;
        }
        if out.len() >= len {
            break;
        }
    }
    out
}

/// Each sample of a row was stored as its difference from the same sample of the pixel before.
fn undo_horizontal_differencing(row: &mut [u8], samples: usize, bytes: usize, big_endian: bool) {
    let stride = samples * bytes;
    if bytes == 1 {
        for i in stride..row.len() {
            row[i] = row[i].wrapping_add(row[i - stride]);
        }
        return;
    }
    let read = |b: &[u8]| {
        if big_endian {
            u16::from_be_bytes([b[0], b[1]])
        } else {
            u16::from_le_bytes([b[0], b[1]])
        }
    };
    for i in (stride..row.len()).step_by(2) {
        let v = read(&row[i..i + 2]).wrapping_add(read(&row[i - stride..i - stride + 2]));
        let b = if big_endian {
            v.to_be_bytes()
        } else {
            v.to_le_bytes()
        };
        row[i..i + 2].copy_from_slice(&b);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    /// How a test file stores its pixels.
    #[derive(Clone, Copy)]
    struct Layout {
        big_endian: bool,
        rows_per_strip: u16,
        compression: u16,
        predictor: u16,
        tiled: bool,
    }

    const PLAIN: Layout = Layout {
        big_endian: false,
        rows_per_strip: u16::MAX,
        compression: COMPRESSION_NONE,
        predictor: 1,
        tiled: false,
    };

    /// A TIFF of `values` (samples of each pixel in order, rows top first) stored as `layout`
    /// says; with `tiled` the strip tags are replaced by tile tags of the whole image.
    fn tiff(
        (width, height): (u16, u16),
        photometric: u16,
        depth: u16,
        extra: &[u16],
        values: &[u16],
        layout: Layout,
    ) -> Vec<u8> {
        let colour = if photometric == PHOTOMETRIC_RGB { 3 } else { 1 };
        let samples = colour + extra.len();
        let u16b = |v: u16| {
            if layout.big_endian {
                v.to_be_bytes()
            } else {
                v.to_le_bytes()
            }
        };
        let u32b = |v: u32| {
            if layout.big_endian {
                v.to_be_bytes()
            } else {
                v.to_le_bytes()
            }
        };
        let rows_per_strip = layout.rows_per_strip.min(height);
        let row_values = usize::from(width) * samples;
        let mut strips = Vec::new();
        for rows in values.chunks(row_values * usize::from(rows_per_strip)) {
            let mut rows = rows.to_vec();
            if layout.predictor == PREDICTOR_HORIZONTAL {
                for row in rows.chunks_mut(row_values) {
                    for i in (samples..row.len()).rev() {
                        row[i] = row[i].wrapping_sub(row[i - samples]);
                    }
                }
            }
            let raw: Vec<u8> = if depth == 8 {
                rows.iter().map(|&v| v as u8).collect()
            } else {
                rows.iter().flat_map(|&v| u16b(v)).collect()
            };
            strips.push(match layout.compression {
                COMPRESSION_NONE => raw,
                COMPRESSION_LZW => {
                    weezl::encode::Encoder::with_tiff_size_switch(weezl::BitOrder::Msb, 8)
                        .encode(&raw)
                        .unwrap()
                }
                COMPRESSION_DEFLATE => {
                    let mut e =
                        flate2::write::ZlibEncoder::new(Vec::new(), flate2::Compression::default());
                    e.write_all(&raw).unwrap();
                    e.finish().unwrap()
                }
                COMPRESSION_PACKBITS => raw
                    .chunks(100)
                    .flat_map(|c| std::iter::once(c.len() as u8 - 1).chain(c.iter().copied()))
                    .collect(),
                other => panic!("compression {other}"),
            });
        }
        let mut out = if layout.big_endian {
            b"MM\0*".to_vec()
        } else {
            b"II*\0".to_vec()
        };
        out.extend_from_slice(&[0; 4]);
        let mut offsets = Vec::new();
        for strip in &strips {
            offsets.push(out.len() as u32);
            out.extend_from_slice(strip);
        }
        let lens: Vec<u32> = strips.iter().map(|s| s.len() as u32).collect();
        let mut tags: Vec<(u16, u16, Vec<u32>)> = vec![
            (256, 3, vec![u32::from(width)]),
            (257, 3, vec![u32::from(height)]),
            (258, 3, vec![u32::from(depth); samples]),
            (259, 3, vec![u32::from(layout.compression)]),
            (262, 3, vec![u32::from(photometric)]),
            (277, 3, vec![samples as u32]),
            (317, 3, vec![u32::from(layout.predictor)]),
        ];
        if layout.tiled {
            tags.extend([
                (322, 3, vec![u32::from(width)]),
                (323, 3, vec![u32::from(height)]),
                (324, 4, offsets),
                (325, 4, lens),
            ]);
        } else {
            tags.extend([
                (273, 4, offsets),
                (278, 3, vec![u32::from(rows_per_strip)]),
                (279, 4, lens),
            ]);
        }
        if !extra.is_empty() {
            tags.push((338, 3, extra.iter().map(|&e| u32::from(e)).collect()));
        }
        tags.sort();
        out.extend_from_slice(&[0; 1][..out.len() & 1]);
        let ifd = out.len();
        let blob_base = ifd + 2 + 12 * tags.len() + 4;
        let mut blobs = Vec::new();
        let mut entries = Vec::new();
        entries.extend_from_slice(&u16b(tags.len() as u16));
        for (tag, kind, values) in &tags {
            let packed: Vec<u8> = values
                .iter()
                .flat_map(|&v| {
                    if *kind == 3 {
                        u16b(v as u16).to_vec()
                    } else {
                        u32b(v).to_vec()
                    }
                })
                .collect();
            entries.extend_from_slice(&u16b(*tag));
            entries.extend_from_slice(&u16b(*kind));
            entries.extend_from_slice(&u32b(values.len() as u32));
            if packed.len() <= 4 {
                let mut inline = packed.clone();
                inline.resize(4, 0);
                entries.extend_from_slice(&inline);
            } else {
                entries.extend_from_slice(&u32b((blob_base + blobs.len()) as u32));
                blobs.extend_from_slice(&packed);
            }
        }
        entries.extend_from_slice(&[0; 4]);
        out.extend_from_slice(&entries);
        out.extend_from_slice(&blobs);
        let ifd_bytes = u32b(ifd as u32);
        out[4..8].copy_from_slice(&ifd_bytes);
        out
    }

    /// The entry of `tag` in a little-endian file's directory.
    fn entry(file: &[u8], tag: u16) -> usize {
        let ifd = u32::from_le_bytes(file[4..8].try_into().unwrap()) as usize;
        let count = u16::from_le_bytes([file[ifd], file[ifd + 1]]) as usize;
        (0..count)
            .map(|e| ifd + 2 + e * 12)
            .find(|&at| u16::from_le_bytes([file[at], file[at + 1]]) == tag)
            .unwrap()
    }

    /// The file with its Compression tag set to `value` (data untouched: only the choice of
    /// reader is tested).
    fn with_compression(file: Vec<u8>, value: u16) -> Vec<u8> {
        with_tag_value(file, 259, value)
    }

    /// The file with the inline value of `tag` set to `value`.
    fn with_tag_value(mut file: Vec<u8>, tag: u16, value: u16) -> Vec<u8> {
        let at = entry(&file, tag);
        file[at + 8..at + 10].copy_from_slice(&value.to_le_bytes());
        file
    }

    /// The file without its ExtraSamples tag (renamed to InkNames, 333, which readers ignore
    /// here and which keeps the directory sorted).
    fn without_extra_samples_tag(mut file: Vec<u8>) -> Vec<u8> {
        let at = entry(&file, 338);
        file[at..at + 2].copy_from_slice(&333u16.to_le_bytes());
        file
    }

    fn rgba(file: &[u8]) -> Vec<u8> {
        decode(file).unwrap().unwrap().2
    }

    /// 5x7 pixels of RGB and one extra sample, distinct values.
    fn values(depth: u16) -> Vec<u16> {
        (0..5 * 7 * 4u32)
            .map(|i| {
                if depth == 8 {
                    (i * 37 % 251) as u16
                } else {
                    (i * 4099 % 65521) as u16
                }
            })
            .collect()
    }

    #[test]
    fn the_first_extra_sample_is_alpha_whatever_it_is_called() {
        let v = values(8);
        let expected: Vec<u8> = v.iter().map(|&x| x as u8).collect();
        for extra in [0, 1, 2] {
            assert_eq!(
                rgba(&tiff((5, 7), PHOTOMETRIC_RGB, 8, &[extra], &v, PLAIN)),
                expected
            );
        }
        // Two extra samples: the second is dropped.
        let five: Vec<u16> = v
            .chunks(4)
            .flat_map(|p| p.iter().copied().chain([7]))
            .collect();
        assert_eq!(
            rgba(&tiff((5, 7), PHOTOMETRIC_RGB, 8, &[0, 0], &five, PLAIN)),
            expected
        );
        // Plain RGB goes to the image crate.
        let three: Vec<u16> = v.chunks(4).flat_map(|p| p[..3].to_vec()).collect();
        assert!(
            decode(&tiff((5, 7), PHOTOMETRIC_RGB, 8, &[], &three, PLAIN))
                .unwrap()
                .is_none()
        );
    }

    #[test]
    fn gray_keeps_one_alpha_per_gray_value_the_last_pixels() {
        let file = tiff(
            (3, 1),
            PHOTOMETRIC_GRAY,
            8,
            &[0],
            &[10, 20, 30, 40, 10, 50],
            PLAIN,
        );
        assert_eq!(
            rgba(&file),
            [10, 10, 10, 50, 30, 30, 30, 40, 10, 10, 10, 50]
        );
        let deep = tiff((1, 1), PHOTOMETRIC_GRAY, 16, &[0], &[0, 0], PLAIN);
        assert!(decode(&deep).is_err());
    }

    #[test]
    fn every_layout_reads_the_same_pixels() {
        for depth in [8, 16] {
            let v = values(depth);
            let expected = rgba(&tiff((5, 7), PHOTOMETRIC_RGB, depth, &[0], &v, PLAIN));
            if depth == 16 {
                assert_eq!(
                    expected[..4],
                    v[..4].iter().map(|&x| narrow_u16(x)).collect::<Vec<_>>()[..]
                );
            }
            for big_endian in [false, true] {
                for rows_per_strip in [1, 3, 7] {
                    for compression in [
                        COMPRESSION_NONE,
                        COMPRESSION_LZW,
                        COMPRESSION_DEFLATE,
                        COMPRESSION_PACKBITS,
                    ] {
                        for predictor in [1, PREDICTOR_HORIZONTAL] {
                            let layout = Layout {
                                big_endian,
                                rows_per_strip,
                                compression,
                                predictor,
                                tiled: false,
                            };
                            let file = tiff((5, 7), PHOTOMETRIC_RGB, depth, &[0], &v, layout);
                            assert_eq!(
                                rgba(&file),
                                expected,
                                "{depth} bits, {big_endian}, {rows_per_strip} rows, {compression}, {predictor}"
                            );
                        }
                    }
                }
            }
        }
    }

    #[test]
    fn layouts_not_read_here_fall_back_only_where_the_image_crate_reads_them() {
        let v = values(8);
        let tiled = Layout {
            tiled: true,
            ..PLAIN
        };
        let no_tag = without_extra_samples_tag(tiff((5, 7), PHOTOMETRIC_RGB, 8, &[2], &v, tiled));
        assert!(decode(&no_tag).unwrap().is_none());
        let jpeg = tiff((5, 7), PHOTOMETRIC_RGB, 8, &[2], &v, PLAIN);
        let jpeg = with_compression(jpeg, 7);
        assert!(decode(&jpeg).unwrap().is_none());
        let float_predictor =
            with_tag_value(tiff((5, 7), PHOTOMETRIC_RGB, 8, &[2], &v, PLAIN), 317, 3);
        assert!(decode(&float_predictor).unwrap().is_none());
        let float_predictor =
            with_tag_value(tiff((5, 7), PHOTOMETRIC_RGB, 8, &[0], &v, PLAIN), 317, 3);
        assert!(decode(&float_predictor).is_err());
        let jpeg = with_compression(tiff((5, 7), PHOTOMETRIC_RGB, 8, &[0], &v, PLAIN), 7);
        assert!(decode(&jpeg).is_err());
        assert!(
            decode(&tiff((5, 7), PHOTOMETRIC_RGB, 8, &[2], &v, tiled))
                .unwrap()
                .is_none()
        );
        assert!(decode(&tiff((5, 7), PHOTOMETRIC_RGB, 8, &[0], &v, tiled)).is_err());
    }

    #[test]
    fn horizontal_differencing_and_packbits_runs_are_undone() {
        let mut row = [10u8, 20, 30, 40, 1, 2, 3, 4];
        undo_horizontal_differencing(&mut row, 4, 1, false);
        assert_eq!(row, [10, 20, 30, 40, 11, 22, 33, 44]);
        let mut deep = [0xff, 0xff, 0, 2];
        undo_horizontal_differencing(&mut deep, 1, 2, true);
        assert_eq!(deep, [0xff, 0xff, 0, 1]);
        assert_eq!(unpack_bits(&[2, 1, 2, 3, 0xfe, 9], 6), [1, 2, 3, 9, 9, 9]);
    }
}
