//! Crunch archives and their ordinary GPU blocks, using the public zlib-licensed fork.
use super::{BlockFormat, Blocks, MipLevel};
use anyhow::{Result, ensure};
use serde::{Deserialize, Serialize};
use std::ffi::c_void;

/// The public Crunch library's maximum level resolution.
pub const MAX_RESOLUTION: u32 = 4096;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum CrunchFormat {
    Dxt1,
    Dxt5,
    Etc1,
    Etc2Rgba8,
}
impl CrunchFormat {
    pub fn from_unity(format: i32) -> Option<Self> {
        Some(match format {
            28 => Self::Dxt1,
            29 => Self::Dxt5,
            64 => Self::Etc1,
            65 => Self::Etc2Rgba8,
            _ => return None,
        })
    }
    pub fn unity_format(self) -> i32 {
        match self {
            Self::Dxt1 => 28,
            Self::Dxt5 => 29,
            Self::Etc1 => 64,
            Self::Etc2Rgba8 => 65,
        }
    }
    pub fn blocks(self) -> BlockFormat {
        match self {
            Self::Dxt1 => BlockFormat::Bc1,
            Self::Dxt5 => BlockFormat::Bc3,
            Self::Etc1 => BlockFormat::Etc1,
            Self::Etc2Rgba8 => BlockFormat::Etc2Rgba8,
        }
    }
}

unsafe extern "C" {
    fn unbound_crunch_encode(
        pixels: *const u32,
        count: usize,
        width: u32,
        height: u32,
        levels: u32,
        format: u32,
        quality: u32,
        perceptual: bool,
        output: *mut *mut c_void,
        length: *mut usize,
    ) -> i32;
    fn unbound_crunch_free(archive: *mut c_void);
    fn unbound_crunch_info(archive: *const u8, size: usize, metadata: *mut u32) -> i32;
    fn unbound_crunch_unpack(
        archive: *const u8,
        size: usize,
        output: *mut u8,
        capacity: usize,
    ) -> i32;
}

struct Allocation(*mut c_void);
impl Drop for Allocation {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe { unbound_crunch_free(self.0) };
        }
    }
}

pub fn encode(
    mips: &[MipLevel],
    format: CrunchFormat,
    quality: i32,
    perceptual: bool,
) -> Result<Vec<u8>> {
    let first = mips
        .first()
        .ok_or_else(|| anyhow::anyhow!("Crunch requires a mip level"))?;
    ensure!(
        first.width > 0
            && first.height > 0
            && first.width <= MAX_RESOLUTION
            && first.height <= MAX_RESOLUTION,
        "Crunch supports dimensions from 1 through 4096"
    );
    let maximum_levels = u32::BITS - first.width.max(first.height).leading_zeros();
    ensure!(
        mips.len() <= maximum_levels as usize,
        "too many Crunch mip levels for these dimensions"
    );
    let mut pixels = Vec::new();
    for (mip, image) in mips.iter().enumerate() {
        let width = (first.width >> mip).max(1);
        let height = (first.height >> mip).max(1);
        ensure!(
            image.width == width
                && image.height == height
                && image.data.len() == width as usize * height as usize * 4,
            "Crunch mip {mip} does not contain the expected RGBA8 texels"
        );
        pixels.extend(
            image
                .data
                .chunks_exact(4)
                .map(|p| u32::from_le_bytes(p.try_into().unwrap())),
        );
    }
    let (mut pointer, mut length) = (std::ptr::null_mut(), 0);
    // A typed input guarantees the public codec's pixel alignment. The codec owns its output
    // until copied below; Allocation releases it on every success or error path.
    let status = unsafe {
        unbound_crunch_encode(
            pixels.as_ptr(),
            pixels.len(),
            first.width,
            first.height,
            mips.len() as u32,
            format.unity_format() as u32,
            quality.clamp(0, 100) as u32,
            perceptual,
            &mut pointer,
            &mut length,
        )
    };
    let allocation = Allocation(pointer);
    ensure!(
        status != 0 && !allocation.0.is_null() && length != 0,
        "Crunch encoding failed"
    );
    Ok(unsafe { std::slice::from_raw_parts(allocation.0.cast::<u8>(), length) }.to_vec())
}

#[derive(Debug)]
pub struct Transcoded {
    pub format: CrunchFormat,
    pub width: u32,
    pub height: u32,
    pub blocks: Blocks,
}

/// Validates the complete archive and returns its GPU blocks, not archive chunks.
pub fn transcode(archive: &[u8]) -> Result<Transcoded> {
    let mut info = [0u32; 4];
    let valid = unsafe { unbound_crunch_info(archive.as_ptr(), archive.len(), info.as_mut_ptr()) };
    ensure!(valid != 0, "invalid or unsupported Crunch archive");
    let format = CrunchFormat::from_unity(info[0] as i32)
        .ok_or_else(|| anyhow::anyhow!("unsupported Crunch texture format {}", info[0]))?;
    let (width, height, levels) = (info[1], info[2], info[3]);
    let block_format = format.blocks();
    let sizes: Vec<_> = (0..levels)
        .map(|m| block_format.level_size((width >> m).max(1), (height >> m).max(1)))
        .collect();
    let mut output = vec![0; sizes.iter().sum()];
    // Metadata was validated by the codec. The destination contains every block of each mip.
    let valid = unsafe {
        unbound_crunch_unpack(
            archive.as_ptr(),
            archive.len(),
            output.as_mut_ptr(),
            output.len(),
        )
    };
    ensure!(valid != 0, "Crunch block transcoding failed");
    let mut at = 0;
    let mips = sizes
        .into_iter()
        .map(|size| {
            let bytes = serde_bytes::ByteBuf::from(output[at..at + size].to_vec());
            at += size;
            bytes
        })
        .collect();
    Ok(Transcoded {
        format,
        width,
        height,
        blocks: Blocks {
            format: block_format,
            mips,
        },
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn archives_keep_formats_dimensions_mips_and_channel_layouts() {
        let mips: Vec<_> = [(16, 8), (8, 4), (4, 2), (2, 1), (1, 1)]
            .into_iter()
            .map(|(width, height)| MipLevel {
                width,
                height,
                data: (0..width * height)
                    .flat_map(|i| [73, 123, 211, if i % 2 == 0 { 0 } else { 255 }])
                    .collect(),
            })
            .collect();
        for format in [
            CrunchFormat::Dxt1,
            CrunchFormat::Dxt5,
            CrunchFormat::Etc1,
            CrunchFormat::Etc2Rgba8,
        ] {
            for quality in [0, 50, 100] {
                let archive = encode(&mips, format, quality, true).unwrap();
                let image = transcode(&archive).unwrap();
                assert_eq!((image.format, image.width, image.height), (format, 16, 8));
                assert_eq!(image.blocks.mips.len(), mips.len());
                for (level, source) in image.blocks.mips.iter().zip(&mips) {
                    assert_eq!(
                        level.len(),
                        format.blocks().level_size(source.width, source.height)
                    );
                }
                let pixels =
                    super::super::decode_blocks(format.blocks(), &image.blocks.mips[0], 16, 8);
                if matches!(format, CrunchFormat::Dxt1 | CrunchFormat::Etc1) {
                    assert!(pixels.chunks_exact(4).all(|p| p[3] == 255));
                } else {
                    assert!(pixels.chunks_exact(4).any(|p| p[3] < 128));
                    assert!(pixels.chunks_exact(4).any(|p| p[3] > 128));
                }
            }
        }
    }

    #[test]
    fn incomplete_and_corrupt_archives_are_rejected_before_decoding() {
        let source = MipLevel {
            width: 4,
            height: 4,
            data: [73, 123, 211, 255].repeat(16),
        };
        let archive = encode(&[source], CrunchFormat::Dxt1, 50, true).unwrap();
        for length in [0, 1, 61, 73, archive.len() - 1] {
            assert!(transcode(&archive[..length]).is_err());
        }
        for (at, value) in [(2, 255), (6, 255), (16, 255), (17, 6), (19, 1), (25, 42)] {
            let mut corrupt = archive.clone();
            corrupt[at] = value;
            assert!(transcode(&corrupt).is_err());
        }
    }
}
