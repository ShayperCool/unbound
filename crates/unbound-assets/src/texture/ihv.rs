//! DDS, KTX 1 and `.astc` source files, which the Editor imports with its IHVImageFormatImporter:
//! the texture data a file holds as the Unity 6000.0.71f1 Editor imports it (tests/projects/ihvsources,
//! whose `raw/*` rows give the bytes' hashes).
//!
//! A DDS file's rows run top-down; the Editor turns the BC1, BC3, BC4 and BC5 blocks and the
//! uncompressed texels upside down to its bottom-up rows (see [`flip_blocks`]), not the BC7 blocks
//! (nor BC6H, whose imported data is not measured). KTX and `.astc` data is taken as it is.

use super::{
    BlockFormat, Blocks, ImportedTexture, MipLevel, TextureFormat, TextureSettings, TextureShape,
    bcn,
};
use anyhow::{Context, Result, bail};
use std::path::Path;
use unbound_yaml::{Map, Value};

/// The extensions the Editor imports with its IHVImageFormatImporter that this module reads.
pub const EXTENSIONS: &[&str] = &["dds", "ktx", "astc"];

/// Whether a path is a source file of this module's.
pub fn is_source(path: &Path) -> bool {
    path.extension()
        .and_then(|e| e.to_str())
        .is_some_and(|e| EXTENSIONS.contains(&e.to_ascii_lowercase().as_str()))
}

/// Imports a source file as the Editor's IHVImageFormatImporter does (tests/projects/ihvsources
/// asset.*): its own format, size and mip chain, a DDS cubemap a Cubemap, the importer's sampler,
/// readability and colour-space settings (its `textureSettings`, `isReadable`, `sRGBTexture`);
/// nothing resized or compressed again. The mips hold what the blocks decode to for CPU reads
/// (BC6H: floats), the blocks the data as the Editor holds it.
pub fn import(source: &Path, bytes: &[u8], meta: &Value) -> Result<ImportedTexture> {
    let extension = source.extension().and_then(|e| e.to_str()).unwrap_or("");
    let file = read(extension, bytes).with_context(|| format!("{}", source.display()))?;
    let settings = settings(meta, &file);
    let blocks = file.blocks();
    let hdr = blocks.is_some_and(|f| f == BlockFormat::Bc6h || f.is_astc_hdr());
    let mut raw = Vec::new();
    let mut mips = Vec::new();
    let mut at = 0;
    for _ in 0..file.faces {
        for (w, h) in levels(file.width, file.height, file.mips) {
            let size = level_size(blocks, w, h);
            let level = &file.data[at..at + size];
            at += size;
            let data = match blocks {
                Some(BlockFormat::Bc6h) => bcn::decode_bc6h_f32(level, w, h)
                    .into_iter()
                    .flat_map(f32::to_le_bytes)
                    .collect(),
                Some(f) if f.is_astc_hdr() => super::astc::decode_f32(level, w, h, f)
                    .into_iter()
                    .flat_map(f32::to_le_bytes)
                    .collect(),
                Some(f) => bcn::decode_level(f, level, w, h),
                // ARGB32 texels as RGBA for the importer's pixels.
                None => level
                    .chunks_exact(4)
                    .flat_map(|t| [t[1], t[2], t[3], t[0]])
                    .collect(),
            };
            raw.push(serde_bytes::ByteBuf::from(level.to_vec()));
            mips.push(MipLevel {
                width: w,
                height: h,
                data,
            });
        }
    }
    let has_alpha = !hdr
        && mips
            .first()
            .is_some_and(|m| m.data.chunks_exact(4).any(|t| t[3] < 255));
    Ok(ImportedTexture {
        name: crate::asset_name(source),
        width: file.width,
        height: file.height,
        original_width: file.width,
        original_height: file.height,
        format: if hdr {
            TextureFormat::Rgba32F
        } else {
            TextureFormat::Rgba8
        },
        srgb: settings.srgb && !hdr,
        mips,
        layers: file.faces,
        wrap_u: settings.wrap_u,
        wrap_v: settings.wrap_v,
        wrap_w: settings.wrap_w,
        filter: settings.filter,
        aniso: settings.aniso,
        mip_bias: settings.mip_bias,
        is_readable: settings.is_readable,
        has_alpha,
        sprites: Vec::new(),
        import_settings_hash: settings.hash(),
        settings,
        blocks: blocks.map(|format| Blocks { format, mips: raw }),
        crunched: None,
        uncompressed: blocks.is_none(),
        usage_mode: 0,
        approximation: None,
        bc7: None,
        source_format: blocks.is_none().then_some(file.format),
    })
}

/// The importer's settings: its section's `textureSettings`, `isReadable` and `sRGBTexture` read
/// as a TextureImporter's are, with the file's shape and mips (an IHVImageFormatImporter has no
/// type, size or compression settings of its own).
fn settings(meta: &Value, file: &IhvTexture) -> TextureSettings {
    let section = meta.get("IHVImageFormatImporter");
    let mut importer = Map::default();
    importer.insert("serializedVersion", Value::Scalar("13".into()));
    if let Some(sampler) = section.and_then(|s| s.get("textureSettings")) {
        importer.insert("textureSettings", sampler.clone());
    }
    let srgb = section
        .and_then(|s| s.get("sRGBTexture"))
        .cloned()
        .unwrap_or(Value::Scalar("1".into()));
    let mut mipmaps = Map::default();
    mipmaps.insert("sRGBTexture", srgb);
    mipmaps.insert(
        "enableMipMap",
        Value::Scalar(if file.mips > 1 { "1" } else { "0" }.into()),
    );
    importer.insert("mipmaps", Value::Map(mipmaps));
    if let Some(readable) = section.and_then(|s| s.get("isReadable")) {
        importer.insert("isReadable", readable.clone());
    }
    importer.insert(
        "textureShape",
        Value::Scalar(if file.faces == 6 { "2" } else { "1" }.into()),
    );
    importer.insert("nPOTScale", Value::Scalar("0".into()));
    let mut root = Map::default();
    root.insert("TextureImporter", Value::Map(importer));
    let mut settings = TextureSettings::read(&Value::Map(root), "Standalone");
    settings.shape = if file.faces == 6 {
        TextureShape::Cube
    } else {
        TextureShape::Texture2D
    };
    settings
}

/// Unity's `TextureFormat.ARGB32`: the uncompressed DDS texels' format.
pub const ARGB32: i32 = 5;

/// A source file's texture data as the Editor holds it.
#[derive(Debug, Clone, PartialEq)]
pub struct IhvTexture {
    /// Unity's `TextureFormat` value.
    pub format: i32,
    pub width: u32,
    pub height: u32,
    pub mips: u32,
    /// 6 for a cubemap (each face's mip chain in turn, +X, -X, +Y, -Y, +Z, -Z), else 1.
    pub faces: u32,
    /// Each face's mip chain in turn, mip 0 first, each level whole blocks (or texels).
    pub data: Vec<u8>,
}

impl IhvTexture {
    /// The block format of a block-compressed file.
    pub fn blocks(&self) -> Option<BlockFormat> {
        BlockFormat::from_unity_format(self.format)
    }
}

/// Reads a source file by its extension (`dds`, `ktx`, `astc`, any case).
pub fn read(extension: &str, bytes: &[u8]) -> Result<IhvTexture> {
    match extension.to_ascii_lowercase().as_str() {
        "dds" => read_dds(bytes),
        "ktx" => read_ktx(bytes),
        "astc" => read_astc(bytes),
        other => bail!("not a DDS, KTX or ASTC file: .{other}"),
    }
}

fn u32_at(bytes: &[u8], at: usize) -> Result<u32> {
    let word = bytes
        .get(at..at + 4)
        .context("the file ends inside its header")?;
    Ok(u32::from_le_bytes(word.try_into().unwrap()))
}

fn level_size(format: Option<BlockFormat>, width: u32, height: u32) -> usize {
    match format {
        Some(f) => {
            let side = f.footprint();
            (width.div_ceil(side) * height.div_ceil(side)) as usize * f.block_bytes()
        }
        None => (width * height * 4) as usize,
    }
}

fn levels(width: u32, height: u32, mips: u32) -> impl Iterator<Item = (u32, u32)> {
    (0..mips).map(move |level| ((width >> level).max(1), (height >> level).max(1)))
}

// DDS --------------------------------------------------------------------------------------------

const DDPF_FOURCC: u32 = 0x4;
const DDPF_RGB: u32 = 0x40;
const DDSCAPS2_CUBEMAP: u32 = 0x200;
const DDSCAPS2_CUBEMAP_ALL_FACES: u32 = 0xFC00;

fn read_dds(bytes: &[u8]) -> Result<IhvTexture> {
    if bytes.get(..4) != Some(b"DDS ") {
        bail!("not a DDS file (no `DDS ` magic)");
    }
    let height = u32_at(bytes, 12)?;
    let width = u32_at(bytes, 16)?;
    let mips = u32_at(bytes, 28)?.max(1);
    let pixel_flags = u32_at(bytes, 80)?;
    let fourcc = bytes
        .get(84..88)
        .context("the file ends inside its header")?;
    let caps2 = u32_at(bytes, 112)?;
    let mut offset = 128;
    let (format, uncompressed) = if pixel_flags & DDPF_FOURCC != 0 {
        if fourcc == b"DX10" {
            let dxgi = u32_at(bytes, 128)?;
            offset += 20;
            (dxgi_format(dxgi)?, false)
        } else {
            (fourcc_format(fourcc)?, false)
        }
    } else if pixel_flags & DDPF_RGB != 0 {
        let bits = u32_at(bytes, 88)?;
        let masks = [
            u32_at(bytes, 92)?,
            u32_at(bytes, 96)?,
            u32_at(bytes, 100)?,
            u32_at(bytes, 104)?,
        ];
        // BGRA bytes (A8R8G8B8 masks): the one uncompressed layout measured.
        if bits != 32 || masks != [0x00FF_0000, 0x0000_FF00, 0x0000_00FF, 0xFF00_0000] {
            bail!(
                "an uncompressed DDS of {bits} bits with masks {masks:08x?} is not supported (only 32-bit BGRA is measured)"
            );
        }
        (ARGB32, true)
    } else {
        bail!("a DDS pixel format of flags {pixel_flags:#x} is not supported");
    };
    let faces = if caps2 & DDSCAPS2_CUBEMAP != 0 {
        if caps2 & DDSCAPS2_CUBEMAP_ALL_FACES != DDSCAPS2_CUBEMAP_ALL_FACES {
            bail!("a DDS cubemap without all six faces is not supported");
        }
        6
    } else {
        1
    };
    let blocks = BlockFormat::from_unity_format(format);
    let mut data = Vec::new();
    let mut at = offset;
    for _ in 0..faces {
        for (w, h) in levels(width, height, mips) {
            let size = level_size(blocks, w, h);
            let level = bytes
                .get(at..at + size)
                .context("the file ends inside its texture data")?;
            at += size;
            match blocks {
                Some(
                    f @ (BlockFormat::Bc1 | BlockFormat::Bc3 | BlockFormat::Bc4 | BlockFormat::Bc5),
                ) => data.extend_from_slice(&flip_blocks(f, level, w, h)),
                Some(_) => data.extend_from_slice(level),
                None if uncompressed => data.extend_from_slice(&flip_bgra_to_argb(level, w, h)),
                None => unreachable!(),
            }
        }
    }
    Ok(IhvTexture {
        format,
        width,
        height,
        mips,
        faces,
        data,
    })
}

fn fourcc_format(fourcc: &[u8]) -> Result<i32> {
    Ok(match fourcc {
        b"DXT1" => BlockFormat::Bc1.unity_format(),
        b"DXT5" => BlockFormat::Bc3.unity_format(),
        b"ATI1" | b"BC4U" => BlockFormat::Bc4.unity_format(),
        b"ATI2" | b"BC5U" => BlockFormat::Bc5.unity_format(),
        other => bail!(
            "a DDS of FourCC {:?} is not supported",
            String::from_utf8_lossy(other)
        ),
    })
}

/// `DXGI_FORMAT` values of the DX10 header.
fn dxgi_format(dxgi: u32) -> Result<i32> {
    Ok(match dxgi {
        71 | 72 => BlockFormat::Bc1.unity_format(),
        77 | 78 => BlockFormat::Bc3.unity_format(),
        80 => BlockFormat::Bc4.unity_format(),
        83 => BlockFormat::Bc5.unity_format(),
        95 => BlockFormat::Bc6h.unity_format(),
        98 | 99 => BlockFormat::Bc7.unity_format(),
        other => bail!("a DDS of DXGI format {other} is not supported"),
    })
}

/// A level of BC1, BC3, BC4 or BC5 blocks turned upside down as the Editor turns a DDS file's
/// (tests/projects/ihvsources raw.BC1.dds, BC3.dds, BC5.dds, BC1Npot.dds, BC1NoMips.dds): a level
/// taller than a block has its block rows reversed and the four texel rows in each block; a level of
/// exactly one block's height is left as it is; a shorter one has its rows reversed within the block.
pub fn flip_blocks(format: BlockFormat, level: &[u8], width: u32, height: u32) -> Vec<u8> {
    let rows = match height {
        4 => return level.to_vec(),
        h if h > 4 => 4,
        h => h as usize,
    };
    let bytes = format.block_bytes();
    let across = width.div_ceil(4) as usize;
    let down = height.div_ceil(4) as usize;
    let mut out = Vec::with_capacity(level.len());
    for by in (0..down).rev() {
        for bx in 0..across {
            let at = (by * across + bx) * bytes;
            let mut block = level[at..at + bytes].to_vec();
            match format {
                BlockFormat::Bc1 => flip_colour(&mut block, rows),
                BlockFormat::Bc4 => flip_alpha(&mut block, rows),
                BlockFormat::Bc3 => {
                    flip_alpha(&mut block[..8], rows);
                    flip_colour(&mut block[8..], rows);
                }
                _ => {
                    flip_alpha(&mut block[..8], rows);
                    flip_alpha(&mut block[8..], rows);
                }
            }
            out.extend_from_slice(&block);
        }
    }
    out
}

/// A BC1 colour block's index rows (one byte each) reversed, the first `rows` of them.
fn flip_colour(block: &mut [u8], rows: usize) {
    block[4..4 + rows].reverse();
}

/// A BC3/BC4/BC5 alpha block's index rows (twelve bits each) reversed, the first `rows` of them.
fn flip_alpha(block: &mut [u8], rows: usize) {
    let mut bits = [0u8; 8];
    bits[..6].copy_from_slice(&block[2..8]);
    let value = u64::from_le_bytes(bits);
    let mut row: Vec<u64> = (0..4).map(|r| value >> (12 * r) & 0xFFF).collect();
    row[..rows].reverse();
    let flipped = row
        .iter()
        .enumerate()
        .fold(0u64, |acc, (r, v)| acc | v << (12 * r));
    block[2..8].copy_from_slice(&flipped.to_le_bytes()[..6]);
}

/// A level of 32-bit BGRA texels turned upside down, as ARGB32 bytes.
fn flip_bgra_to_argb(level: &[u8], width: u32, height: u32) -> Vec<u8> {
    let stride = width as usize * 4;
    let mut out = Vec::with_capacity(level.len());
    for y in (0..height as usize).rev() {
        for texel in level[y * stride..(y + 1) * stride].chunks_exact(4) {
            out.extend_from_slice(&[texel[3], texel[2], texel[1], texel[0]]);
        }
    }
    out
}

// KTX 1 -------------------------------------------------------------------------------------------

const KTX_IDENTIFIER: [u8; 12] = [
    0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A,
];

fn read_ktx(bytes: &[u8]) -> Result<IhvTexture> {
    if bytes.get(..12) != Some(&KTX_IDENTIFIER[..]) {
        bail!("not a KTX 1 file (no KTX 11 identifier)");
    }
    if u32_at(bytes, 12)? != 0x0403_0201 {
        bail!("a big-endian KTX file is not supported");
    }
    let internal = u32_at(bytes, 28)?;
    let width = u32_at(bytes, 36)?;
    let height = u32_at(bytes, 40)?.max(1);
    let depth = u32_at(bytes, 44)?;
    let array = u32_at(bytes, 48)?;
    let faces = u32_at(bytes, 52)?.max(1);
    let mips = u32_at(bytes, 56)?.max(1);
    let key_values = u32_at(bytes, 60)? as usize;
    if depth > 1 || array > 0 || faces != 1 {
        bail!("a KTX array, cube or 3D texture is not supported (only 2D ones are measured)");
    }
    let format = match internal {
        0x8D64 => BlockFormat::Etc1,
        0x9274 => BlockFormat::Etc2Rgb,
        0x9278 => BlockFormat::Etc2Rgba8,
        0x93B0 => BlockFormat::Astc4x4,
        0x93B2 => BlockFormat::Astc5x5,
        0x93B4 => BlockFormat::Astc6x6,
        0x93B7 => BlockFormat::Astc8x8,
        0x93BB => BlockFormat::Astc10x10,
        0x93BD => BlockFormat::Astc12x12,
        other => bail!("a KTX of internal format {other:#06x} is not supported"),
    };
    let mut at = 64 + key_values;
    let mut data = Vec::new();
    for (w, h) in levels(width, height, mips) {
        let size = u32_at(bytes, at)? as usize;
        let expected = level_size(Some(format), w, h);
        if size != expected {
            bail!("a KTX level of {size} bytes where {w}x{h} {format:?} blocks take {expected}");
        }
        data.extend_from_slice(
            bytes
                .get(at + 4..at + 4 + size)
                .context("the file ends inside its texture data")?,
        );
        // Each level's data is padded to four bytes.
        at += 4 + size.div_ceil(4) * 4;
    }
    Ok(IhvTexture {
        format: format.unity_format(),
        width,
        height,
        mips,
        faces: 1,
        data,
    })
}

// ASTC --------------------------------------------------------------------------------------------

fn read_astc(bytes: &[u8]) -> Result<IhvTexture> {
    if u32_at(bytes, 0)? != 0x5CA1_AB13 {
        bail!("not an ASTC file (no 0x5CA1AB13 magic)");
    }
    let header = bytes.get(..16).context("the file ends inside its header")?;
    let (bw, bh, bd) = (header[4], header[5], header[6]);
    let size = |at: usize| {
        u32::from(header[at]) | u32::from(header[at + 1]) << 8 | u32::from(header[at + 2]) << 16
    };
    let (width, height, depth) = (size(7), size(10), size(13));
    if bw != bh || bd != 1 || depth != 1 {
        bail!(
            "an ASTC file of {bw}x{bh}x{bd} blocks or depth {depth} is not supported (square 2D blocks only)"
        );
    }
    let format = match bw {
        4 => BlockFormat::Astc4x4,
        5 => BlockFormat::Astc5x5,
        6 => BlockFormat::Astc6x6,
        8 => BlockFormat::Astc8x8,
        10 => BlockFormat::Astc10x10,
        12 => BlockFormat::Astc12x12,
        other => bail!("an ASTC file of {other}x{other} blocks is not supported"),
    };
    let length = level_size(Some(format), width, height);
    let data = bytes
        .get(16..16 + length)
        .context("the file ends inside its texture data")?
        .to_vec();
    Ok(IhvTexture {
        format: format.unity_format(),
        width,
        height,
        mips: 1,
        faces: 1,
        data,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn probe() -> PathBuf {
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/ihvsources")
    }

    /// The Editor's raw data of each source (`raw/<name>`: "length N sha256 H").
    fn editor_raw(name: &str) -> Option<String> {
        let reference =
            std::fs::read_to_string(probe().join("Reference/unity-6000.0.71f1/values.jsonl"))
                .ok()?;
        reference.lines().find_map(|line| {
            let row: serde_json::Value = serde_json::from_str(line).ok()?;
            (row["k"].as_str()? == format!("raw/{name}"))
                .then(|| row["v"].as_str().unwrap().to_string())
        })
    }

    fn sha256(bytes: &[u8]) -> String {
        use sha2::Digest;
        sha2::Sha256::digest(bytes)
            .iter()
            .map(|b| format!("{b:02x}"))
            .collect()
    }

    #[test]
    fn each_sources_data_is_the_editors() {
        // BC6H's imported data is not measured (the Editor's is a fill), the cube's not readable.
        for name in [
            "BC1.dds",
            "BC1NoMips.dds",
            "BC1Npot.dds",
            "BC3.dds",
            "BC5.dds",
            "BC7.dds",
            "BC7Srgb.dds",
            "RGBA8.dds",
            "ETC2RGB.ktx",
            "ETC2RGBA.ktx",
            "ASTC4x4.ktx",
            "Block4x4.astc",
            "Block6x6.astc",
            "Block8x8Npot.astc",
        ] {
            let bytes = std::fs::read(probe().join("Assets/Sources").join(name)).unwrap();
            let extension = name.rsplit('.').next().unwrap();
            let texture = read(extension, &bytes).unwrap();
            assert_eq!(
                editor_raw(name).unwrap(),
                format!(
                    "length {} sha256 {}",
                    texture.data.len(),
                    sha256(&texture.data)
                ),
                "{name}"
            );
        }
    }

    #[test]
    fn an_import_keeps_the_files_data_with_the_importers_settings() {
        let path = probe().join("Assets/Sources/BC1.dds");
        let bytes = std::fs::read(&path).unwrap();
        // The .meta the Editor writes (reference meta/BC1.dds), point filtering and clamping set.
        let meta = unbound_yaml::parse_value(
            "fileFormatVersion: 2\nIHVImageFormatImporter:\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 0\n    aniso: 3\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 0\n  isReadable: 1\n  sRGBTexture: 0\n",
        )
        .unwrap();
        let texture = import(&path, &bytes, &meta).unwrap();
        assert_eq!(
            (
                texture.name.as_str(),
                texture.width,
                texture.height,
                texture.layers
            ),
            ("BC1", 16, 16, 1)
        );
        assert_eq!(texture.reported_format(), Some(10));
        let blocks = texture.blocks.as_ref().unwrap();
        assert_eq!(
            blocks.mips.iter().map(|m| m.len()).collect::<Vec<_>>(),
            [128, 32, 8, 8, 8]
        );
        assert_eq!(
            texture
                .mips
                .iter()
                .map(|m| (m.width, m.data.len()))
                .collect::<Vec<_>>(),
            [(16, 1024), (8, 256), (4, 64), (2, 16), (1, 4)]
        );
        assert_eq!(
            (
                texture.filter,
                texture.wrap_u,
                texture.wrap_w,
                texture.aniso
            ),
            (
                super::super::FilterMode::Point,
                super::super::WrapMode::Clamp,
                super::super::WrapMode::Repeat,
                3
            )
        );
        assert!(texture.is_readable);
        assert!(!texture.srgb);
        // A DDS cube is a Cubemap; uncompressed texels report ARGB32.
        let cube = import(
            &probe().join("Assets/Sources/CubeBC1.dds"),
            &std::fs::read(probe().join("Assets/Sources/CubeBC1.dds")).unwrap(),
            &Value::Null,
        )
        .unwrap();
        assert_eq!((cube.layers, cube.settings.shape), (6, TextureShape::Cube));
        let rgba = import(
            &probe().join("Assets/Sources/RGBA8.dds"),
            &std::fs::read(probe().join("Assets/Sources/RGBA8.dds")).unwrap(),
            &Value::Null,
        )
        .unwrap();
        assert_eq!(
            (rgba.reported_format(), rgba.blocks.is_none(), rgba.srgb),
            (Some(ARGB32), true, true)
        );
    }

    #[test]
    fn formats_sizes_and_faces_are_the_files() {
        let read_named = |name: &str| {
            let bytes = std::fs::read(probe().join("Assets/Sources").join(name)).unwrap();
            read(name.rsplit('.').next().unwrap(), &bytes).unwrap()
        };
        let bc1 = read_named("BC1.dds");
        assert_eq!(
            (bc1.format, bc1.width, bc1.height, bc1.mips, bc1.faces),
            (10, 16, 16, 5, 1)
        );
        let cube = read_named("CubeBC1.dds");
        assert_eq!((cube.format, cube.faces, cube.data.len()), (10, 6, 6 * 32));
        assert_eq!(read_named("BC6H.dds").format, 24);
        assert_eq!(read_named("RGBA8.dds").format, ARGB32);
        assert_eq!(
            read_named("Block8x8Npot.astc").blocks(),
            Some(BlockFormat::Astc8x8)
        );
        // Unsupported content says what it is.
        let error = read("dds", b"DDS \0\0\0\0").unwrap_err().to_string();
        assert!(error.contains("ends inside its header"), "{error}");
        assert!(read("png", &[]).is_err());
    }
}
