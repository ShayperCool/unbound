//! Unity's AssetBundle archives (UnityFS, formats 6 to 8): the header, the blocks (uncompressed,
//! LZMA, LZ4) and the files they hold (serialized files, `.resS` and `.resource` streams), and the
//! documents of a serialized file in it as Unbound reads assets: references by pseudo GUID
//! (`bundle:CAB-…` for another bundle file, the built-in files' GUIDs), textures with their pixels
//! inline and block-compressed formats decoded to RGBA32.

use anyhow::{Context, Result, bail};
use unbound_yaml::{Map, Value};

use crate::serialized::{External, SerializedFile};

/// A file of an archive: its path (`CAB-…`, `CAB-….resS`, `BuildPlayer-Scene`, …) and bytes.
#[derive(Debug, Clone)]
pub struct BundleFile {
    pub path: String,
    pub flags: u32,
    pub data: Vec<u8>,
}

/// An AssetBundle archive, decompressed.
#[derive(Debug, Clone)]
pub struct Bundle {
    pub unity_version: String,
    pub files: Vec<BundleFile>,
}

/// A node flag: the file is a serialized file (not a stream).
pub const NODE_SERIALIZED: u32 = 4;

struct Reader<'a> {
    data: &'a [u8],
    at: usize,
}

impl<'a> Reader<'a> {
    fn bytes(&mut self, n: usize) -> Result<&'a [u8]> {
        let s = self
            .data
            .get(self.at..self.at + n)
            .context("truncated bundle")?;
        self.at += n;
        Ok(s)
    }
    fn u16(&mut self) -> Result<u16> {
        Ok(u16::from_be_bytes(self.bytes(2)?.try_into()?))
    }
    fn u32(&mut self) -> Result<u32> {
        Ok(u32::from_be_bytes(self.bytes(4)?.try_into()?))
    }
    fn i64(&mut self) -> Result<i64> {
        Ok(i64::from_be_bytes(self.bytes(8)?.try_into()?))
    }
    fn cstring(&mut self) -> Result<String> {
        let rest = self.data.get(self.at..).context("truncated bundle")?;
        let end = rest
            .iter()
            .position(|&b| b == 0)
            .context("unterminated string")?;
        let s = String::from_utf8_lossy(&rest[..end]).to_string();
        self.at += end + 1;
        Ok(s)
    }
    fn align(&mut self, to: usize) {
        self.at = self.at.div_ceil(to) * to;
    }
}

/// Decompresses a block by its compression (the low six flag bits: 0 none, 1 LZMA, 2 LZ4, 3 LZ4HC).
fn decompress(data: &[u8], compression: u32, size: usize) -> Result<Vec<u8>> {
    match compression {
        0 => Ok(data.to_vec()),
        1 => {
            // Properties and dictionary size (5 bytes), then the stream; the size is known.
            let mut out = Vec::with_capacity(size);
            let options = lzma_rs::decompress::Options {
                unpacked_size: lzma_rs::decompress::UnpackedSize::UseProvided(Some(size as u64)),
                ..Default::default()
            };
            lzma_rs::lzma_decompress_with_options(
                &mut std::io::Cursor::new(data),
                &mut out,
                &options,
            )
            .map_err(|e| anyhow::anyhow!("an LZMA block does not decompress: {e}"))?;
            Ok(out)
        }
        2 | 3 => lz4_flex::block::decompress(data, size)
            .map_err(|e| anyhow::anyhow!("an LZ4 block does not decompress: {e}")),
        other => bail!("blocks compressed with method {other} are not supported"),
    }
}

/// A block of an archive: its uncompressed and stored sizes and its flags (the low six bits are
/// the compression, 0x40 marks a streamed block).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) struct Block {
    pub size: u32,
    pub stored: u32,
    pub flags: u16,
}

/// A file of an archive's directory: where it lies in the content, its flags and path.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) struct Node {
    pub offset: i64,
    pub size: i64,
    pub flags: u32,
    pub path: String,
}

/// A UnityFS archive as stored: its header strings, its block and directory information and
/// where its data blocks start.
#[derive(Debug, Clone)]
pub(crate) struct Layout {
    pub version: u32,
    pub player: String,
    pub unity_version: String,
    /// The header's bytes, aligned as the format aligns it.
    pub header_size: usize,
    pub blocks: Vec<Block>,
    pub nodes: Vec<Node>,
    pub data_start: usize,
}

/// A nonzero CRC that does not match an archive's uncompressed content.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CrcMismatch {
    pub expected: u32,
    pub actual: u32,
}

impl std::fmt::Display for CrcMismatch {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(
            f,
            "CRC mismatch: the AssetBundle's uncompressed content has CRC {}, not the expected {}",
            self.actual, self.expected
        )
    }
}

impl std::error::Error for CrcMismatch {}

impl Layout {
    pub(crate) fn read(data: &[u8]) -> Result<Layout> {
        let mut r = Reader { data, at: 0 };
        let signature = r.cstring()?;
        if signature != "UnityFS" {
            bail!(
                "not a UnityFS AssetBundle (signature {signature:?}; UnityWeb and UnityRaw bundles are not supported)"
            );
        }
        let version = r.u32()?;
        if !(6..=8).contains(&version) {
            bail!("UnityFS format {version} is not supported");
        }
        let player = r.cstring()?;
        let unity_version = r.cstring()?;
        let _size = r.i64()?;
        let compressed_info = r.u32()? as usize;
        let uncompressed_info = r.u32()? as usize;
        let flags = r.u32()?;
        if version >= 7 {
            r.align(16);
        }
        let header_size = r.at;
        let info_at_end = flags & 0x80 != 0;
        let info_bytes = if info_at_end {
            data.len()
                .checked_sub(compressed_info)
                .and_then(|start| data.get(start..))
                .context("truncated bundle")?
        } else {
            r.bytes(compressed_info)?
        };
        let info = decompress(info_bytes, flags & 0x3f, uncompressed_info)?;
        if flags & 0x200 != 0 {
            r.align(16);
        }
        let mut ir = Reader {
            data: &info,
            at: 16,
        };
        let block_count = ir.u32()? as usize;
        let mut blocks = Vec::with_capacity(block_count.min(info.len()));
        for _ in 0..block_count {
            blocks.push(Block {
                size: ir.u32()?,
                stored: ir.u32()?,
                flags: ir.u16()?,
            });
        }
        let node_count = ir.u32()? as usize;
        let mut nodes = Vec::with_capacity(node_count.min(info.len()));
        for _ in 0..node_count {
            nodes.push(Node {
                offset: ir.i64()?,
                size: ir.i64()?,
                flags: ir.u32()?,
                path: ir.cstring()?,
            });
        }
        Ok(Layout {
            version,
            player,
            unity_version,
            header_size,
            blocks,
            nodes,
            data_start: r.at,
        })
    }

    /// The data blocks decompressed, in order. A nonzero `crc` is checked against them (CRC-32,
    /// IEEE, zlib's), as they decompress: no second pass, no extra copy.
    pub(crate) fn content(&self, data: &[u8], crc: u32) -> Result<Vec<u8>> {
        let total: usize = self.blocks.iter().map(|b| b.size as usize).sum();
        let mut stream = Vec::with_capacity(total);
        let mut hasher = (crc != 0).then(crc32fast::Hasher::new);
        let mut r = Reader {
            data,
            at: self.data_start,
        };
        for block in &self.blocks {
            let bytes = r.bytes(block.stored as usize)?;
            let start = stream.len();
            stream.extend(decompress(
                bytes,
                (block.flags & 0x3f) as u32,
                block.size as usize,
            )?);
            if let Some(hasher) = &mut hasher {
                hasher.update(&stream[start..]);
            }
        }
        if let Some(hasher) = hasher {
            let actual = hasher.finalize();
            if actual != crc {
                return Err(CrcMismatch {
                    expected: crc,
                    actual,
                }
                .into());
            }
        }
        Ok(stream)
    }
}

impl Bundle {
    pub fn parse(data: &[u8]) -> Result<Bundle> {
        Bundle::parse_with_crc(data, 0)
    }

    /// [`Bundle::parse`], checking the archive's uncompressed content against `crc` first when it
    /// is not 0 (the `crc` of AssetBundle.LoadFromFile, LoadFromMemory and LoadFromStream). The
    /// content is the data blocks decompressed, in order, without the header and the block and
    /// directory information: its CRC-32 (IEEE, zlib's) is the `CRC:` the Editor writes to the
    /// bundle's `.manifest`, the same for every compression. A mismatch fails with a
    /// [`CrcMismatch`] before any file of the archive is made.
    pub fn parse_with_crc(data: &[u8], crc: u32) -> Result<Bundle> {
        let layout = Layout::read(data)?;
        let stream = layout.content(data, crc)?;
        let files = layout
            .nodes
            .into_iter()
            .map(|node| {
                let (offset, size) = (node.offset as usize, node.size as usize);
                let data = offset
                    .checked_add(size)
                    .and_then(|end| stream.get(offset..end))
                    .with_context(|| format!("bundle file {} lies past the data", node.path))?
                    .to_vec();
                Ok(BundleFile {
                    path: node.path,
                    flags: node.flags,
                    data,
                })
            })
            .collect::<Result<Vec<_>>>()?;
        Ok(Bundle {
            unity_version: layout.unity_version,
            files,
        })
    }

    pub fn file(&self, path: &str) -> Option<&BundleFile> {
        self.files.iter().find(|f| f.path == path)
    }

    /// The serialized files (CAB files and built scenes).
    pub fn serialized_files(&self) -> impl Iterator<Item = &BundleFile> {
        self.files
            .iter()
            .filter(|f| f.flags & NODE_SERIALIZED != 0 || SerializedFile::parse(&f.data).is_ok())
    }

    /// The bytes a stream reference (`archive:/CAB-…/CAB-….resS`) names in this bundle.
    pub fn stream(&self, path: &str, offset: usize, size: usize) -> Option<&[u8]> {
        let name = path.rsplit('/').next().unwrap_or(path);
        self.file(name)?.data.get(offset..offset + size)
    }

    /// A serialized file's objects as documents (see the module's documentation).
    pub fn documents(&self, path: &str) -> Result<Vec<unbound_yaml::Document>> {
        let mut docs = self.raw_documents(path)?;
        self.prepare_documents(&mut docs)?;
        Ok(docs)
    }

    /// Decoded serialized input, before stream data is inlined. Diagnostics use these paths
    /// while capturing reads from the same conversion used by `documents`.
    pub fn raw_documents(&self, path: &str) -> Result<Vec<unbound_yaml::Document>> {
        let file = self
            .file(path)
            .with_context(|| format!("the bundle has no file {path}"))?;
        crate::serialized::documents_with(&file.data, external_reference)
    }

    /// Apply the bundle's texture, mesh and shader conversions to decoded documents.
    pub fn prepare_documents(&self, docs: &mut [unbound_yaml::Document]) -> Result<()> {
        for doc in docs {
            if doc.class_id == 28 {
                inline_texture(self, &mut doc.body)
                    .with_context(|| format!("texture {}", doc.file_id))?;
            }
            if doc.class_id == 43 {
                inline_mesh(self, &mut doc.body);
            }
            if doc.class_id == 48 {
                slim_shader(&mut doc.body);
            }
        }
        Ok(())
    }
}

/// The pseudo GUID a reference to another file uses: a bundle file's `bundle:<name>`, a built-in
/// file's GUID, else the external's own GUID.
fn external_reference(external: &External) -> String {
    let lower = external.path.to_ascii_lowercase();
    if lower.ends_with("unity default resources") {
        return "0000000000000000e000000000000000".into();
    }
    if lower.ends_with("unity_builtin_extra") {
        return "0000000000000000f000000000000000".into();
    }
    if lower.starts_with("archive:/") {
        let name = external.path.rsplit('/').next().unwrap_or(&external.path);
        return format!("bundle:{name}");
    }
    external.guid_hex()
}

fn scalar_i64(map: &Map, key: &str) -> Option<i64> {
    match map.get(key)? {
        Value::Scalar(s) => s.parse().ok(),
        _ => None,
    }
}

fn set(map: &mut Map, key: &str, value: Value) {
    match map.0.iter_mut().find(|(k, _)| k == key) {
        Some(slot) => slot.1 = value,
        None => map.0.push((key.to_string(), value)),
    }
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

/// A texture's pixels from its stream (a `.resS` file) into `image data`; every block-compressed mip
/// decoded to RGBA32 (`m_TextureFormat` 4; `m_SourceTextureFormat` keeps the bundle's).
fn inline_texture(bundle: &Bundle, body: &mut Value) -> Result<()> {
    let Value::Map(map) = body else { return Ok(()) };
    let streamed = match map.get("m_StreamData") {
        Some(Value::Map(stream)) => {
            let path = match stream.get("path") {
                Some(Value::Scalar(p)) => p.clone(),
                _ => String::new(),
            };
            let offset = scalar_i64(stream, "offset").unwrap_or(0) as usize;
            let size = scalar_i64(stream, "size").unwrap_or(0) as usize;
            (!path.is_empty() && size > 0).then(|| (path, offset, size))
        }
        _ => None,
    };
    let mut pixels: Vec<u8> = match &streamed {
        Some((path, offset, size)) => bundle
            .stream(path, *offset, *size)
            .with_context(|| format!("its stream {path} is not in the bundle"))?
            .to_vec(),
        None => match map.get("image data") {
            Some(Value::Scalar(h)) => (0..h.len() / 2)
                .filter_map(|i| u8::from_str_radix(&h[i * 2..i * 2 + 2], 16).ok())
                .collect(),
            _ => Vec::new(),
        },
    };
    if let Some(Value::Map(stream)) = map
        .0
        .iter_mut()
        .find(|(k, _)| k == "m_StreamData")
        .map(|(_, v)| v)
    {
        set(stream, "path", Value::Scalar(String::new()));
        set(stream, "size", Value::Scalar("0".into()));
    }
    let format = scalar_i64(map, "m_TextureFormat").unwrap_or(4) as i32;
    let width = scalar_i64(map, "m_Width").unwrap_or(0).max(0) as usize;
    let height = scalar_i64(map, "m_Height").unwrap_or(0).max(0) as usize;
    let mips = scalar_i64(map, "m_MipCount").unwrap_or(1).max(1) as usize;
    if let Some(decoded) = decode_texture_mips(format, &pixels, width, height, mips)? {
        // CPU raw-data queries expose the encoded bytes, even though the renderer uses RGBA.
        set(map, "m_SourceTextureData", Value::Scalar(hex(&pixels)));
        pixels = decoded;
        set(map, "m_SourceMipCount", Value::Scalar(mips.to_string()));
        set(
            map,
            "m_SourceTextureFormat",
            Value::Scalar(format.to_string()),
        );
        set(map, "m_TextureFormat", Value::Scalar("4".into()));
        set(map, "m_MipCount", Value::Scalar(mips.to_string()));
    }
    set(map, "image data", Value::Scalar(hex(&pixels)));
    Ok(())
}

/// The compressed size of a mip, including the padding of sub-block levels.
fn compressed_mip_size(format: i32, width: usize, height: usize) -> Option<usize> {
    let (block, bytes) = match format {
        10 | 26 | 34 | 41 | 45 | 46 => (4, 8),
        12 | 24 | 25 | 27 | 43 | 47 => (4, 16),
        48 | 66 => (4, 16),
        49 | 67 => (5, 16),
        50 | 68 => (6, 16),
        51 | 69 => (8, 16),
        52 | 70 => (10, 16),
        53 | 71 => (12, 16),
        _ => return None,
    };
    Some(width.div_ceil(block).max(1) * height.div_ceil(block).max(1) * bytes)
}

fn decode_texture_mips(
    format: i32,
    data: &[u8],
    width: usize,
    height: usize,
    mips: usize,
) -> Result<Option<Vec<u8>>> {
    if compressed_mip_size(format, width, height).is_none() {
        return Ok(None);
    }
    let full_chain = usize::BITS - width.max(height).leading_zeros();
    anyhow::ensure!(
        width > 0 && height > 0 && mips > 0 && mips <= full_chain as usize,
        "compressed texture format {format}: invalid {width}x{height} / {mips} mip levels"
    );
    let mut out = Vec::new();
    let mut offset = 0;
    for mip in 0..mips {
        let w = (width >> mip).max(1);
        let h = (height >> mip).max(1);
        let size = compressed_mip_size(format, w, h).unwrap();
        let bytes = data.get(offset..offset + size).with_context(|| {
            format!("compressed texture format {format}: missing data for mip {mip} ({w}x{h})")
        })?;
        let decoded = decode_texture(format, bytes, w, h).with_context(|| {
            format!("compressed texture format {format}: decoding mip {mip} failed")
        })?;
        out.extend(decoded);
        offset += size;
    }
    Ok(Some(out))
}

/// One compressed level as RGBA32 bytes (None for unsupported formats or malformed data).
fn decode_texture(format: i32, data: &[u8], width: usize, height: usize) -> Option<Vec<u8>> {
    use texture2ddecoder as t;
    if width == 0 || height == 0 {
        return None;
    }
    let mut image = vec![0u32; width * height];
    let result = match format {
        10 => t::decode_bc1(data, width, height, &mut image),
        12 => t::decode_bc3(data, width, height, &mut image),
        26 => t::decode_bc4(data, width, height, &mut image),
        27 => t::decode_bc5(data, width, height, &mut image),
        24 => t::decode_bc6_unsigned(data, width, height, &mut image),
        25 => t::decode_bc7(data, width, height, &mut image),
        34 => t::decode_etc1(data, width, height, &mut image),
        45 => t::decode_etc2_rgb(data, width, height, &mut image),
        46 => t::decode_etc2_rgba1(data, width, height, &mut image),
        47 => t::decode_etc2_rgba8(data, width, height, &mut image),
        41 => t::decode_eacr(data, width, height, &mut image),
        43 => t::decode_eacrg(data, width, height, &mut image),
        48 | 66 => t::decode_astc_4_4(data, width, height, &mut image),
        49 | 67 => t::decode_astc_5_5(data, width, height, &mut image),
        50 | 68 => t::decode_astc_6_6(data, width, height, &mut image),
        51 | 69 => t::decode_astc_8_8(data, width, height, &mut image),
        52 | 70 => t::decode_astc_10_10(data, width, height, &mut image),
        53 | 71 => t::decode_astc_12_12(data, width, height, &mut image),
        _ => return None,
    };
    result.ok()?;
    // The decoder's pixels are B, G, R, A in memory.
    let mut out = Vec::with_capacity(width * height * 4);
    for p in image {
        let [b, g, r, a] = p.to_le_bytes();
        out.extend_from_slice(&[r, g, b, a]);
    }
    Some(out)
}

/// A mesh's vertex bytes where the YAML form keeps them (`m_VertexData._typelessdata`, its size
/// in `m_DataSize`), from its stream (a `.resS` file) when it has one.
fn inline_mesh(bundle: &Bundle, body: &mut Value) {
    let Value::Map(map) = body else { return };
    let streamed = match map.get("m_StreamData") {
        Some(Value::Map(stream)) => match stream.get("path") {
            Some(Value::Scalar(p)) if !p.is_empty() => {
                let offset = scalar_i64(stream, "offset").unwrap_or(0) as usize;
                let size = scalar_i64(stream, "size").unwrap_or(0) as usize;
                bundle.stream(p, offset, size).map(hex)
            }
            _ => None,
        },
        _ => None,
    };
    if let Some(Value::Map(vertex)) = map
        .0
        .iter_mut()
        .find(|(k, _)| k == "m_VertexData")
        .map(|(_, v)| v)
    {
        let bytes = match streamed {
            Some(b) => b,
            None => match vertex.get("m_DataSize") {
                Some(Value::Scalar(h)) => h.clone(),
                _ => String::new(),
            },
        };
        set(
            vertex,
            "m_DataSize",
            Value::Scalar((bytes.len() / 2).to_string()),
        );
        set(vertex, "_typelessdata", Value::Scalar(bytes));
    }
}

/// A built shader's name only (`m_ParsedForm.m_Name`): Unbound compiles shaders from their sources
/// by name and leaves the built programs alone.
fn slim_shader(body: &mut Value) {
    let name = match body {
        Value::Map(map) => match map.get("m_ParsedForm") {
            Some(Value::Map(parsed)) => match parsed.get("m_Name") {
                Some(Value::Scalar(n)) => n.clone(),
                _ => String::new(),
            },
            _ => String::new(),
        },
        _ => String::new(),
    };
    let mut slim = Map::default();
    slim.0.push(("m_Name".into(), Value::Scalar(name)));
    *body = Value::Map(slim);
}

#[cfg(test)]
mod texture_mip_tests {
    use super::*;

    fn chain() -> Vec<u8> {
        // BC1 endpoints in RGB565, every selector zero. The 8x4 top level has two blocks;
        // all three smaller levels have one complete block, even below 4x4.
        [0xf800u16, 0xf800, 0x07e0, 0x001f, 0xffff]
            .into_iter()
            .flat_map(|color| [color as u8, (color >> 8) as u8, 0, 0, 0, 0, 0, 0])
            .collect()
    }

    #[test]
    fn every_compressed_mip_is_decoded_in_order() {
        let decoded = decode_texture_mips(10, &chain(), 8, 4, 4).unwrap().unwrap();
        let expected: Vec<u8> = [
            ([255, 0, 0, 255], 32),
            ([0, 255, 0, 255], 8),
            ([0, 0, 255, 255], 2),
            ([255, 255, 255, 255], 1),
        ]
        .into_iter()
        .flat_map(|(color, count)| std::iter::repeat_n(color, count).flatten())
        .collect();
        assert_eq!(decoded, expected);
        let incomplete = chain();
        let error = decode_texture_mips(10, &incomplete[..39], 8, 4, 4).unwrap_err();
        assert!(error.to_string().contains("mip 3"));
        assert!(decode_texture_mips(10, &incomplete, 8, 4, 5).is_err());
        assert!(decode_texture_mips(10, &incomplete, 0, 4, 3).is_err());
    }

    #[test]
    fn inlining_keeps_the_mip_count_and_original_format() {
        let mut body = Value::Map(Map::from(vec![
            ("m_Width".into(), Value::Scalar("8".into())),
            ("m_Height".into(), Value::Scalar("4".into())),
            ("m_TextureFormat".into(), Value::Scalar("10".into())),
            ("m_MipCount".into(), Value::Scalar("4".into())),
            ("image data".into(), Value::Scalar(hex(&chain()))),
        ]));
        inline_texture(
            &Bundle {
                unity_version: "6000.0.71f1".into(),
                files: Vec::new(),
            },
            &mut body,
        )
        .unwrap();
        let Value::Map(map) = body else {
            panic!("texture map")
        };
        assert_eq!(scalar_i64(&map, "m_MipCount"), Some(4));
        assert_eq!(scalar_i64(&map, "m_SourceMipCount"), Some(4));
        assert_eq!(scalar_i64(&map, "m_SourceTextureFormat"), Some(10));
        assert_eq!(scalar_i64(&map, "m_TextureFormat"), Some(4));
        assert_eq!(
            map.get("m_SourceTextureData"),
            Some(&Value::Scalar(hex(&chain())))
        );
        let Some(Value::Scalar(data)) = map.get("image data") else {
            panic!("texture data")
        };
        assert_eq!(data.len(), 172 * 2);
        assert!(data.ends_with("ffffffff"));
    }
}

#[cfg(test)]
mod crc_tests {
    use super::*;

    /// A UnityFS format 8 archive: uncompressed block and directory information after the header
    /// (flags 0x240, as the Editor's bundles here), then `blocks` (uncompressed bytes, compression
    /// 0 none or 2 LZ4) whose content holds `nodes` (offset, size, path). Also each block's start.
    fn archive(blocks: &[(&[u8], u32)], nodes: &[(usize, usize, &str)]) -> (Vec<u8>, Vec<usize>) {
        let packed: Vec<Vec<u8>> = blocks
            .iter()
            .map(|(bytes, compression)| match compression {
                0 => bytes.to_vec(),
                2 => lz4_flex::block::compress(bytes),
                other => panic!("compression {other}"),
            })
            .collect();
        let mut info = vec![0u8; 16];
        info.extend((blocks.len() as u32).to_be_bytes());
        for ((bytes, compression), stored) in blocks.iter().zip(&packed) {
            info.extend((bytes.len() as u32).to_be_bytes());
            info.extend((stored.len() as u32).to_be_bytes());
            info.extend((*compression as u16).to_be_bytes());
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
        let mut starts = Vec::new();
        for stored in &packed {
            starts.push(out.len());
            out.extend(stored);
        }
        let total = out.len() as i64;
        out[size_at..size_at + 8].copy_from_slice(&total.to_be_bytes());
        (out, starts)
    }

    fn pattern(seed: u8, len: usize) -> Vec<u8> {
        (0..len).map(|i| seed.wrapping_add((i % 7) as u8)).collect()
    }

    /// Three blocks (LZ4, none, LZ4) holding two files with ten unowned bytes between them.
    fn three_blocks() -> (Vec<u8>, Vec<usize>, Vec<u8>) {
        let (a, b, c) = (pattern(1, 40), pattern(60, 24), pattern(120, 50));
        let content = [a.as_slice(), b.as_slice(), c.as_slice()].concat();
        let (data, starts) = archive(
            &[(a.as_slice(), 2), (b.as_slice(), 0), (c.as_slice(), 2)],
            &[(0, 30, "CAB-a"), (40, 74, "CAB-a.resS")],
        );
        (data, starts, content)
    }

    fn same_files(a: &Bundle, b: &Bundle) {
        let files = |x: &Bundle| -> Vec<(String, u32, Vec<u8>)> {
            x.files
                .iter()
                .map(|f| (f.path.clone(), f.flags, f.data.clone()))
                .collect()
        };
        assert_eq!(files(a), files(b));
    }

    fn mismatch(result: Result<Bundle>) -> String {
        let error = format!("{:#}", result.err().expect("a CRC mismatch"));
        assert!(error.contains("CRC mismatch"), "{error}");
        error
    }

    #[test]
    fn the_checksum_is_ieee_crc32_and_streams_across_blocks() {
        // CRC-32/ISO-HDLC's published check values, independent of any bundle.
        assert_eq!(crc32fast::hash(b"123456789"), 0xcbf4_3926);
        assert_eq!(
            crc32fast::hash(b"The quick brown fox jumps over the lazy dog"),
            0x414f_a339
        );
        assert_eq!(crc32fast::hash(b""), 0);
        let mut split = crc32fast::Hasher::new();
        split.update(b"1234");
        split.update(b"");
        split.update(b"56789");
        assert_eq!(split.finalize(), 0xcbf4_3926);
    }

    /// The Editor's bundles of `tests/projects/assetbundles` and the `CRC:` of their `.manifest`
    /// files, uncompressed, LZ4 (the scene's in six blocks) and LZMA.
    #[test]
    fn editor_bundles_match_their_manifest_crc_in_every_compression() {
        let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/assetbundles/Reference/bundles");
        let mut things = Vec::new();
        for compression in ["none", "lz4", "lzma"] {
            for name in ["things", "scene"] {
                let path = root.join(compression).join(name);
                let data = std::fs::read(&path).unwrap();
                let manifest = std::fs::read_to_string(path.with_extension("manifest")).unwrap();
                let crc: u32 = manifest
                    .lines()
                    .find_map(|l| l.strip_prefix("CRC: "))
                    .expect("a CRC line")
                    .trim()
                    .parse()
                    .unwrap();
                assert_ne!(crc, 0);
                let checked = Bundle::parse_with_crc(&data, crc)
                    .unwrap_or_else(|e| panic!("{compression}/{name}: {e:#}"));
                same_files(&checked, &Bundle::parse(&data).unwrap());
                let error = mismatch(Bundle::parse_with_crc(&data, crc ^ 1));
                assert!(error.contains(&crc.to_string()), "{error}");
                if name == "things" {
                    things.push(crc);
                }
            }
        }
        // The same content, whatever its compression.
        assert_eq!(things, [things[0]; 3]);
    }

    #[test]
    fn the_checksum_covers_every_block_and_the_bytes_between_files() {
        let (data, starts, content) = three_blocks();
        let crc = crc32fast::hash(&content);
        let checked = Bundle::parse_with_crc(&data, crc).unwrap();
        same_files(&checked, &Bundle::parse(&data).unwrap());
        assert_eq!(checked.files[0].data, content[..30]);
        assert_eq!(checked.files[1].data, content[40..]);
        // Not the files alone, one block, the stored (compressed) bytes or the whole archive.
        let files = [&content[..30], &content[40..]].concat();
        for wrong in [
            crc32fast::hash(&files),
            crc32fast::hash(&content[64..]),
            crc32fast::hash(&data[starts[0]..]),
            crc32fast::hash(&data),
        ] {
            assert_ne!(wrong, crc);
            mismatch(Bundle::parse_with_crc(&data, wrong));
        }
    }

    #[test]
    fn a_zero_crc_skips_the_check_and_damaged_archives_fail() {
        let (data, starts, content) = three_blocks();
        let crc = crc32fast::hash(&content);
        // A changed byte of the uncompressed block: only a check notices.
        let mut changed = data.clone();
        changed[starts[1] + 5] ^= 0x55;
        let unchecked = Bundle::parse_with_crc(&changed, 0).unwrap();
        assert_ne!(unchecked.files[1].data, content[40..]);
        mismatch(Bundle::parse_with_crc(&changed, crc));
        // A damaged LZ4 block either does not decompress or does not match.
        let mut damaged = data.clone();
        damaged[starts[2] + 1] ^= 0xff;
        assert!(Bundle::parse_with_crc(&damaged, crc).is_err());
        // Truncated archives fail with and without a check.
        for end in [data.len() - 1, starts[1], 20] {
            assert!(Bundle::parse_with_crc(&data[..end], crc).is_err(), "{end}");
            assert!(Bundle::parse_with_crc(&data[..end], 0).is_err(), "{end}");
        }
        // Other archive kinds stay unsupported.
        let error = Bundle::parse_with_crc(b"UnityWeb\0\0\0\0\x03", crc).unwrap_err();
        assert!(format!("{error:#}").contains("not a UnityFS AssetBundle"));
    }
}
