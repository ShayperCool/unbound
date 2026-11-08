//! The form in which the AssetBundle cache (`UnityWebRequestAssetBundle` with a version, hash or
//! `CachedAssetBundle`) keeps a downloaded archive, and the room it asks the cache for, as the
//! Unity 6000.0.71f1 Editor does (tests/projects/networkcaching).
//!
//! The cache keeps LZ4 archives while `Caching.compressionEnabled`, uncompressed ones otherwise.
//! An archive already in that form is kept as downloaded, byte for byte: with compression, one
//! whose blocks include an LZ4 or LZ4HC block; without, one whose blocks are all uncompressed and
//! not streamed with a compression. Any other archive (uncompressed or LZMA with compression,
//! compressed without) is rewritten: its content in 128 KiB blocks, LZ4 (the reference library's
//! default compression) or stored with the streamed flag; then the block and directory
//! information, LZ4HC at the highest level, after the blocks; the header keeps the source's format
//! version and version strings.
//!
//! The room asked for is the downloaded size when the archive is kept, else the size of the
//! source archive uncompressed: its header, its block information (16 hash bytes, a count and ten
//! bytes per block), its directory (a count and 21 bytes plus the path per file) and its content.

use anyhow::{Context, Result, ensure};

use crate::bundle::{Block, Layout};

/// The uncompressed size of each block of a rewritten archive.
const BLOCK_SIZE: usize = 128 * 1024;
/// Block flags: the compression (low six bits) and a streamed block.
const COMPRESSION_MASK: u16 = 0x3f;
const LZ4: u16 = 2;
const STREAMED: u16 = 0x40;
/// Header flags of a rewritten archive: block and directory information compressed with LZ4HC
/// (3), stored together (0x40), after the blocks (0x80).
const REWRITTEN_FLAGS: u32 = 0x3 | 0x40 | 0x80;
/// The LZ4HC level of a rewritten archive's information: the highest.
const INFO_LEVEL: i32 = 12;
/// The bytes the hash of the block information takes before its block count.
const INFO_HASH: usize = 16;

/// An archive as the cache keeps it.
pub struct CacheForm {
    /// The archive the cache stores.
    pub data: Vec<u8>,
    /// Whether it was rewritten (else it is the download itself).
    pub rewritten: bool,
    /// The room the cache must have free for it.
    pub required_space: u64,
}

/// Whether an archive with these blocks must be rewritten to be kept LZ4 (`compress`) or
/// uncompressed.
fn needs_rewrite(blocks: &[Block], compress: bool) -> bool {
    // LZ4 (2) and LZ4HC (3).
    let lz4 = |flags: u16| flags & (COMPRESSION_MASK & !1) == LZ4;
    if compress && blocks.iter().any(|b| lz4(b.flags)) {
        return false;
    }
    blocks.iter().any(|b| {
        let compression = b.flags & COMPRESSION_MASK;
        if b.flags & STREAMED != 0 && compression != 0 {
            return true;
        }
        if compress {
            !lz4(b.flags)
        } else {
            compression != 0
        }
    })
}

/// The size of an archive with this layout uncompressed (see the module's documentation).
fn uncompressed_size(layout: &Layout) -> u64 {
    let blocks_info = INFO_HASH + 4 + 10 * layout.blocks.len();
    let directory: usize = 4 + layout
        .nodes
        .iter()
        .map(|n| 8 + 8 + 4 + n.path.len() + 1)
        .sum::<usize>();
    let content: u64 = layout.blocks.iter().map(|b| b.size as u64).sum();
    (layout.header_size + blocks_info + directory) as u64 + content
}

/// The form the cache keeps a downloaded archive in, LZ4 when `compress`, else uncompressed.
pub fn cache_form(data: &[u8], compress: bool) -> Result<CacheForm> {
    let layout = Layout::read(data)?;
    if !needs_rewrite(&layout.blocks, compress) {
        return Ok(CacheForm {
            data: data.to_vec(),
            rewritten: false,
            required_space: data.len() as u64,
        });
    }
    let content = layout.content(data, 0)?;
    let mut blocks = Vec::new();
    let mut stored = Vec::with_capacity(if compress {
        content.len() / 2
    } else {
        content.len()
    });
    for chunk in content.chunks(BLOCK_SIZE) {
        let start = stored.len();
        if compress {
            lz4_block(chunk, &mut stored)?;
        } else {
            stored.extend_from_slice(chunk);
        }
        blocks.push(Block {
            size: chunk.len() as u32,
            stored: (stored.len() - start) as u32,
            flags: if compress { LZ4 } else { STREAMED },
        });
    }
    let mut info = vec![0u8; INFO_HASH];
    info.extend((blocks.len() as u32).to_be_bytes());
    for block in &blocks {
        info.extend(block.size.to_be_bytes());
        info.extend(block.stored.to_be_bytes());
        info.extend(block.flags.to_be_bytes());
    }
    info.extend((layout.nodes.len() as u32).to_be_bytes());
    for node in &layout.nodes {
        info.extend(node.offset.to_be_bytes());
        info.extend(node.size.to_be_bytes());
        info.extend(node.flags.to_be_bytes());
        info.extend(node.path.as_bytes());
        info.push(0);
    }
    let mut packed_info = Vec::new();
    lz4hc_block(&info, &mut packed_info)?;

    let mut out = b"UnityFS\0".to_vec();
    out.extend(layout.version.to_be_bytes());
    out.extend(layout.player.as_bytes());
    out.push(0);
    out.extend(layout.unity_version.as_bytes());
    out.push(0);
    let size_at = out.len();
    out.extend(0i64.to_be_bytes());
    out.extend((packed_info.len() as u32).to_be_bytes());
    out.extend((info.len() as u32).to_be_bytes());
    out.extend(REWRITTEN_FLAGS.to_be_bytes());
    if layout.version >= 7 {
        out.resize(out.len().div_ceil(16) * 16, 0);
    }
    out.extend(&stored);
    out.extend(&packed_info);
    let total = out.len() as i64;
    out[size_at..size_at + 8].copy_from_slice(&total.to_be_bytes());
    Ok(CacheForm {
        data: out,
        rewritten: true,
        required_space: uncompressed_size(&layout),
    })
}

fn bound(len: usize) -> Result<(i32, usize)> {
    let len = i32::try_from(len).context("an archive block is too large for LZ4")?;
    let bound = unsafe { lz4_sys::LZ4_compressBound(len) };
    ensure!(bound > 0, "an archive block is too large for LZ4");
    Ok((len, bound as usize))
}

/// Appends `src` compressed as one LZ4 block (the reference library's default compression).
fn lz4_block(src: &[u8], out: &mut Vec<u8>) -> Result<()> {
    let (len, bound) = bound(src.len())?;
    let start = out.len();
    out.resize(start + bound, 0);
    // SAFETY: the source is `len` readable bytes and the destination `bound` writable ones.
    let written = unsafe {
        lz4_sys::LZ4_compress_default(
            src.as_ptr().cast(),
            out[start..].as_mut_ptr().cast(),
            len,
            bound as i32,
        )
    };
    ensure!(written > 0 || src.is_empty(), "LZ4 compression failed");
    out.truncate(start + written.max(0) as usize);
    Ok(())
}

/// Appends `src` compressed as one LZ4HC block at [`INFO_LEVEL`].
fn lz4hc_block(src: &[u8], out: &mut Vec<u8>) -> Result<()> {
    let (len, bound) = bound(src.len())?;
    let start = out.len();
    out.resize(start + bound, 0);
    // SAFETY: as in `lz4_block`.
    let written = unsafe {
        lz4_sys::LZ4_compress_HC(
            src.as_ptr().cast(),
            out[start..].as_mut_ptr().cast(),
            len,
            bound as i32,
            INFO_LEVEL,
        )
    };
    ensure!(written > 0 || src.is_empty(), "LZ4HC compression failed");
    out.truncate(start + written.max(0) as usize);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::bundle::{Bundle, CrcMismatch, NODE_SERIALIZED};

    /// A UnityFS format 8 archive with its information after the header (flags 0x240, as the
    /// Editor builds them), blocks of `flags` holding `content` in `chunk`-byte pieces (LZ4 for
    /// compression 2 or 3), and the files `nodes`.
    fn archive(
        content: &[u8],
        chunk: usize,
        flags: u16,
        nodes: &[(usize, usize, &str)],
    ) -> Vec<u8> {
        let mut blocks = Vec::new();
        let mut stored = Vec::new();
        for piece in content.chunks(chunk) {
            let packed = match flags & COMPRESSION_MASK {
                0 => piece.to_vec(),
                2 | 3 => lz4_flex::block::compress(piece),
                other => panic!("compression {other}"),
            };
            blocks.push((piece.len() as u32, packed.len() as u32, flags));
            stored.extend(packed);
        }
        let mut info = vec![0u8; INFO_HASH];
        info.extend((blocks.len() as u32).to_be_bytes());
        for (size, packed, flags) in &blocks {
            info.extend(size.to_be_bytes());
            info.extend(packed.to_be_bytes());
            info.extend(flags.to_be_bytes());
        }
        info.extend((nodes.len() as u32).to_be_bytes());
        for (offset, size, path) in nodes {
            info.extend((*offset as i64).to_be_bytes());
            info.extend((*size as i64).to_be_bytes());
            info.extend(NODE_SERIALIZED.to_be_bytes());
            info.extend(path.as_bytes());
            info.push(0);
        }
        let mut out = b"UnityFS\0".to_vec();
        out.extend(8u32.to_be_bytes());
        out.extend(b"5.x.x\06000.0.71f1\0");
        let size_at = out.len();
        out.extend(0i64.to_be_bytes());
        out.extend((info.len() as u32).to_be_bytes());
        out.extend((info.len() as u32).to_be_bytes());
        out.extend(0x240u32.to_be_bytes());
        out.resize(out.len().div_ceil(16) * 16, 0);
        out.extend(&info);
        out.resize(out.len().div_ceil(16) * 16, 0);
        out.extend(stored);
        let total = out.len() as i64;
        out[size_at..size_at + 8].copy_from_slice(&total.to_be_bytes());
        out
    }

    fn content(len: usize) -> Vec<u8> {
        (0..len)
            .map(|i| (i % 251) as u8 ^ (i / 997) as u8)
            .collect()
    }

    fn files(data: &[u8]) -> Vec<(String, Vec<u8>)> {
        Bundle::parse(data)
            .unwrap()
            .files
            .into_iter()
            .map(|f| (f.path, f.data))
            .collect()
    }

    #[test]
    fn lz4_archives_are_kept_with_compression_and_rewritten_without() {
        let body = content(3000);
        let source = archive(&body, 1 << 17, 3, &[(0, 2990, "CAB-a")]);
        let kept = cache_form(&source, true).unwrap();
        assert!(!kept.rewritten);
        assert_eq!(kept.data, source);
        assert_eq!(kept.required_space, source.len() as u64);

        let raw = cache_form(&source, false).unwrap();
        assert!(raw.rewritten);
        assert_eq!(files(&raw.data), files(&source));
        let layout = Layout::read(&raw.data).unwrap();
        assert_eq!(
            layout.blocks,
            [Block {
                size: 3000,
                stored: 3000,
                flags: STREAMED
            }]
        );
        // Header (50 bytes, aligned to 64), block information 20 + 10, directory 4 + 21 + 5.
        assert_eq!(raw.required_space, 64 + 30 + 30 + 3000);
    }

    #[test]
    fn uncompressed_archives_are_kept_without_compression_and_rewritten_with() {
        let body = content(1000);
        let source = archive(&body, 1 << 17, STREAMED, &[(0, 996, "CAB-b")]);
        let kept = cache_form(&source, false).unwrap();
        assert!(!kept.rewritten);
        assert_eq!(kept.data, source);

        let lz4 = cache_form(&source, true).unwrap();
        assert!(lz4.rewritten);
        assert_eq!(files(&lz4.data), files(&source));
        let header_flags = u32::from_be_bytes(lz4.data[46..50].try_into().unwrap());
        assert_eq!(header_flags, REWRITTEN_FLAGS);
        let size = i64::from_be_bytes(lz4.data[30..38].try_into().unwrap());
        assert_eq!(lz4.data.len() as i64, size);
        assert_eq!(
            &lz4.data[50..64],
            &[0; 14],
            "the header is padded to 16 bytes"
        );
    }

    #[test]
    fn rewritten_content_is_split_into_128_kib_blocks_and_keeps_its_crc() {
        let body = content(BLOCK_SIZE * 2 + 5);
        let nodes = [(0, 1000, "CAB-c"), (1000, body.len() - 1000, "CAB-c.resS")];
        let source = archive(&body, 1 << 16, STREAMED, &nodes);
        for compress in [true, false] {
            let form = cache_form(&source, compress).unwrap();
            assert_eq!(form.rewritten, compress);
            let layout = Layout::read(&form.data).unwrap();
            if compress {
                let sizes: Vec<u32> = layout.blocks.iter().map(|b| b.size).collect();
                assert_eq!(sizes, [BLOCK_SIZE as u32, BLOCK_SIZE as u32, 5]);
            }
            let crc = crc32fast::hash(&body);
            assert_eq!(layout.content(&form.data, crc).unwrap(), body);
            assert_eq!(files(&form.data), files(&source));
        }
        let lz4 = archive(&body, 1 << 16, 2, &nodes);
        let raw = cache_form(&lz4, false).unwrap();
        let sizes: Vec<u32> = Layout::read(&raw.data)
            .unwrap()
            .blocks
            .iter()
            .map(|b| b.size)
            .collect();
        assert_eq!(sizes, [BLOCK_SIZE as u32, BLOCK_SIZE as u32, 5]);
        assert_eq!(files(&raw.data), files(&lz4));
    }

    #[test]
    fn crc_mismatches_report_both_values() {
        let body = content(64);
        let source = archive(&body, 64, 0, &[(0, 64, "CAB-e")]);
        let error = Bundle::parse_with_crc(&source, 1).unwrap_err();
        let mismatch = error.downcast_ref::<CrcMismatch>().unwrap();
        assert_eq!(
            (mismatch.expected, mismatch.actual),
            (1, crc32fast::hash(&body))
        );
    }

    #[test]
    fn data_that_is_no_archive_fails() {
        assert!(cache_form(b"RIFF....WAVEfmt ", true).is_err());
        assert!(cache_form(&[], false).is_err());
    }
}
