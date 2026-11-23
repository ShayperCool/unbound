//! Writing Unity's binary serialized files (format version 22, Unity 6000.0) as the Editor writes
//! them: the header, the types with their type trees, the object table, the external files, and
//! each object's data laid out by its type tree. A type's tree is the Editor's own, read from a
//! file it wrote ([`raw_types`]); the bytes the writer gives for the same objects are the file's
//! (tests/projects/bakeenvironment `written/LightingData.asset`).
use anyhow::{Context, Result, bail};
use unbound_yaml::Value;

/// The format version written.
pub const VERSION: u32 = 22;
/// Where the metadata starts: after the header.
const HEADER_SIZE: usize = 48;
/// A field aligns the stream to 4 bytes after it.
const ALIGN: u32 = 0x4000;
/// A node's type flag: the node is an array.
const IS_ARRAY: u8 = 1;

/// A node of a type tree as the file stores it.
#[derive(Debug, Clone, PartialEq)]
pub struct RawNode {
    pub version: u16,
    pub level: u8,
    pub type_flags: u8,
    pub type_offset: u32,
    pub name_offset: u32,
    pub byte_size: i32,
    pub index: i32,
    pub meta_flag: u32,
    pub ref_hash: u64,
}

/// A type of the file's type table, with its tree.
#[derive(Debug, Clone, PartialEq)]
pub struct RawType {
    pub class_id: i32,
    pub stripped: u8,
    pub script_index: i16,
    /// The script's hash (a MonoBehaviour's type).
    pub script_hash: Option<[u8; 16]>,
    pub hash: [u8; 16],
    pub nodes: Vec<RawNode>,
    /// The tree's own strings (names the shared strings do not have).
    pub strings: Vec<u8>,
    pub dependencies: Vec<i32>,
}

/// A file a serialized file names: by GUID (a project asset) or by path.
#[derive(Debug, Clone, PartialEq)]
pub struct ExternalFile {
    pub guid: [u8; 16],
    pub kind: i32,
    pub path: String,
}

/// An object of the file: its path ID, its type (an index into the file's types) and its data.
#[derive(Debug, Clone)]
pub struct FileObject {
    pub path_id: i64,
    pub type_index: usize,
    pub data: Vec<u8>,
}

/// A whole file: the Unity version and platform that write it, its types, its objects in the
/// table's order (by path ID) and the order their data follows, and the files it names.
#[derive(Debug, Clone)]
pub struct File {
    pub unity_version: String,
    pub platform: i32,
    pub types: Vec<RawType>,
    pub objects: Vec<FileObject>,
    /// The objects' indices in the order their data is laid out.
    pub data_order: Vec<usize>,
    pub externals: Vec<ExternalFile>,
}

fn align(bytes: &mut Vec<u8>, to: usize) {
    while bytes.len() % to != 0 {
        bytes.push(0);
    }
}

/// The file's bytes: a 48-byte header (big-endian sizes), the metadata, then the objects' data
/// from a 16-byte boundary, each object's from one too.
pub fn write(file: &File) -> Vec<u8> {
    let mut meta = Vec::new();
    meta.extend(file.unity_version.as_bytes());
    meta.push(0);
    meta.extend(file.platform.to_le_bytes());
    meta.push(1); // type trees
    meta.extend((file.types.len() as i32).to_le_bytes());
    for t in &file.types {
        meta.extend(t.class_id.to_le_bytes());
        meta.push(t.stripped);
        meta.extend(t.script_index.to_le_bytes());
        if let Some(hash) = t.script_hash {
            meta.extend(hash);
        }
        meta.extend(t.hash);
        meta.extend((t.nodes.len() as i32).to_le_bytes());
        meta.extend((t.strings.len() as i32).to_le_bytes());
        for n in &t.nodes {
            meta.extend(n.version.to_le_bytes());
            meta.push(n.level);
            meta.push(n.type_flags);
            meta.extend(n.type_offset.to_le_bytes());
            meta.extend(n.name_offset.to_le_bytes());
            meta.extend(n.byte_size.to_le_bytes());
            meta.extend(n.index.to_le_bytes());
            meta.extend(n.meta_flag.to_le_bytes());
            meta.extend(n.ref_hash.to_le_bytes());
        }
        meta.extend(&t.strings);
        meta.extend((t.dependencies.len() as i32).to_le_bytes());
        for d in &t.dependencies {
            meta.extend(d.to_le_bytes());
        }
    }
    // Where each object's data starts, relative to the data's start.
    let mut starts = vec![0u64; file.objects.len()];
    let mut at = 0u64;
    for &k in &file.data_order {
        at = at.next_multiple_of(16);
        starts[k] = at;
        at += file.objects[k].data.len() as u64;
    }
    meta.extend((file.objects.len() as i32).to_le_bytes());
    for (k, object) in file.objects.iter().enumerate() {
        // (The metadata starts on a 4-byte boundary of the file.)
        align(&mut meta, 4);
        meta.extend(object.path_id.to_le_bytes());
        meta.extend(starts[k].to_le_bytes());
        meta.extend((object.data.len() as u32).to_le_bytes());
        meta.extend((object.type_index as i32).to_le_bytes());
    }
    meta.extend(0i32.to_le_bytes()); // scripts
    meta.extend((file.externals.len() as i32).to_le_bytes());
    for external in &file.externals {
        meta.push(0); // no temporary path
        meta.extend(external.guid);
        meta.extend(external.kind.to_le_bytes());
        meta.extend(external.path.as_bytes());
        meta.push(0);
    }
    meta.extend(0i32.to_le_bytes()); // types of managed references
    meta.push(0); // no user information
    let meta_end = HEADER_SIZE + meta.len();
    let data_offset = meta_end.next_multiple_of(16);
    let file_size = data_offset as u64 + at;
    let mut out = Vec::with_capacity(file_size as usize);
    out.extend([0u8; 8]);
    out.extend(VERSION.to_be_bytes());
    out.extend([0u8; 4]);
    out.extend([0u8; 4]); // little-endian data, reserved bytes
    out.extend((meta.len() as u32).to_be_bytes());
    out.extend(file_size.to_be_bytes());
    out.extend((data_offset as u64).to_be_bytes());
    out.extend([0u8; 8]);
    out.extend(meta);
    align(&mut out, 16);
    for &k in &file.data_order {
        align(&mut out, 16);
        out.extend(&file.objects[k].data);
    }
    out
}

/// The type table of a serialized file (format 22, with type trees) as it stores it.
pub fn raw_types(data: &[u8]) -> Result<Vec<RawType>> {
    Ok(parse_raw(data)?.0)
}

/// The files a serialized file (format 22) names, in its order.
pub fn raw_externals(data: &[u8]) -> Result<Vec<ExternalFile>> {
    Ok(parse_raw(data)?.1)
}

fn parse_raw(data: &[u8]) -> Result<(Vec<RawType>, Vec<ExternalFile>)> {
    struct Cursor<'a> {
        data: &'a [u8],
        at: usize,
    }
    impl Cursor<'_> {
        fn take<const N: usize>(&mut self) -> Result<[u8; N]> {
            let bytes = self
                .data
                .get(self.at..self.at + N)
                .context("a truncated serialized file")?;
            self.at += N;
            Ok(bytes.try_into()?)
        }
        fn i32(&mut self) -> Result<i32> {
            Ok(i32::from_le_bytes(self.take()?))
        }
        fn cstr(&mut self) -> Result<String> {
            let rest = self
                .data
                .get(self.at..)
                .context("a truncated serialized file")?;
            let end = rest
                .iter()
                .position(|&b| b == 0)
                .context("a truncated serialized file")?;
            self.at += end + 1;
            Ok(String::from_utf8_lossy(&rest[..end]).into_owned())
        }
    }
    let version = u32::from_be_bytes(data.get(8..12).context("truncated")?.try_into()?);
    if version != VERSION {
        bail!("serialized file version {version}, expected {VERSION}");
    }
    let mut c = Cursor {
        data,
        at: HEADER_SIZE,
    };
    c.cstr()?;
    c.i32()?;
    if c.take::<1>()?[0] == 0 {
        bail!("a serialized file without type trees");
    }
    let count = c.i32()?;
    let mut types = Vec::new();
    for _ in 0..count {
        let class_id = c.i32()?;
        let stripped = c.take::<1>()?[0];
        let script_index = i16::from_le_bytes(c.take()?);
        let script_hash = (class_id == 114 || class_id < 0)
            .then(|| c.take::<16>())
            .transpose()?;
        let hash = c.take::<16>()?;
        let nodes_count = c.i32()?;
        let strings_size = c.i32()? as usize;
        let mut nodes = Vec::new();
        for _ in 0..nodes_count {
            nodes.push(RawNode {
                version: u16::from_le_bytes(c.take()?),
                level: c.take::<1>()?[0],
                type_flags: c.take::<1>()?[0],
                type_offset: u32::from_le_bytes(c.take()?),
                name_offset: u32::from_le_bytes(c.take()?),
                byte_size: c.i32()?,
                index: c.i32()?,
                meta_flag: u32::from_le_bytes(c.take()?),
                ref_hash: u64::from_le_bytes(c.take()?),
            });
        }
        let strings = data
            .get(c.at..c.at + strings_size)
            .context("a truncated type tree")?
            .to_vec();
        c.at += strings_size;
        let dependencies = (0..c.i32()?).map(|_| c.i32()).collect::<Result<_>>()?;
        types.push(RawType {
            class_id,
            stripped,
            script_index,
            script_hash,
            hash,
            nodes,
            strings,
            dependencies,
        });
    }
    let objects = c.i32()?;
    for _ in 0..objects {
        c.at = c.at.next_multiple_of(4);
        c.at += 24;
    }
    let scripts = c.i32()?;
    for _ in 0..scripts {
        c.at = c.at.next_multiple_of(4);
        c.at += 12;
    }
    let mut externals = Vec::new();
    for _ in 0..c.i32()? {
        c.cstr()?;
        let guid = c.take::<16>()?;
        let kind = c.i32()?;
        let path = c.cstr()?;
        externals.push(ExternalFile { guid, kind, path });
    }
    Ok((types, externals))
}

impl RawType {
    /// Node `i`'s type name.
    pub fn type_name(&self, i: usize) -> &str {
        self.name(self.nodes[i].type_offset)
    }

    /// Node `i`'s field name.
    pub fn field_name(&self, i: usize) -> &str {
        self.name(self.nodes[i].name_offset)
    }

    /// The nodes one level under node `i`.
    pub fn children_of(&self, i: usize) -> Vec<usize> {
        self.children(i)
    }

    fn name(&self, offset: u32) -> &str {
        if offset & 0x8000_0000 != 0 {
            crate::serialized::common_string(offset & 0x7fff_ffff)
        } else {
            let s = &self.strings[(offset as usize).min(self.strings.len())..];
            let end = s.iter().position(|&b| b == 0).unwrap_or(s.len());
            std::str::from_utf8(&s[..end]).unwrap_or("")
        }
    }

    fn children(&self, i: usize) -> Vec<usize> {
        let level = self.nodes[i].level;
        let mut out = Vec::new();
        for j in i + 1..self.nodes.len() {
            if self.nodes[j].level <= level {
                break;
            }
            if self.nodes[j].level == level + 1 {
                out.push(j);
            }
        }
        out
    }
}

/// An object's data laid out by its type's tree, from the value tree
/// [`crate::serialized::SerializedFile::read`] gives (fields by name, arrays as sequences,
/// byte arrays and raw data as hex text).
pub fn write_value(t: &RawType, value: &Value) -> Result<Vec<u8>> {
    let mut out = Vec::new();
    write_node(t, 0, value, &mut out)?;
    Ok(out)
}

fn scalar(value: &Value) -> Result<&str> {
    value.as_str().context("a field without a value")
}

fn hex_bytes(text: &str) -> Result<Vec<u8>> {
    (0..text.len() / 2)
        .map(|k| {
            u8::from_str_radix(&text[2 * k..2 * k + 2], 16).context("raw bytes that are not hex")
        })
        .collect()
}

fn write_node(t: &RawType, i: usize, value: &Value, out: &mut Vec<u8>) -> Result<()> {
    let node = &t.nodes[i];
    let type_name = t.name(node.type_offset);
    let kids = t.children(i);
    if type_name == "TypelessData" {
        let bytes = hex_bytes(scalar(value)?)?;
        out.extend((bytes.len() as i32).to_le_bytes());
        out.extend(bytes);
    } else if kids.is_empty() {
        write_leaf(type_name, scalar(value)?, out)
            .with_context(|| format!("field `{}`", t.name(node.name_offset)))?;
    } else if t.nodes[kids[0]].type_flags & IS_ARRAY != 0 {
        let array = kids[0];
        let element = *t
            .children(array)
            .get(1)
            .context("an array without an element type")?;
        let element_type = t.name(t.nodes[element].type_offset);
        if t.children(element).is_empty() && matches!(element_type, "char" | "UInt8" | "SInt8") {
            let bytes = if type_name == "string" {
                match value {
                    Value::Map(raw) => hex_bytes(
                        raw.get("_bytes")
                            .and_then(Value::as_str)
                            .context("a string's raw bytes")?,
                    )?,
                    other => scalar(other)?.as_bytes().to_vec(),
                }
            } else {
                hex_bytes(scalar(value)?)?
            };
            out.extend((bytes.len() as i32).to_le_bytes());
            out.extend(bytes);
        } else {
            let items: &[Value] = match value {
                Value::Seq(items) => items,
                Value::Null => &[],
                _ => bail!(
                    "field `{}`: an array that is not a sequence",
                    t.name(node.name_offset)
                ),
            };
            out.extend((items.len() as i32).to_le_bytes());
            for item in items {
                write_node(t, element, item, out)?;
            }
        }
        if t.nodes[array].meta_flag & ALIGN != 0 {
            align(out, 4);
        }
    } else {
        for k in kids {
            let name = t.name(t.nodes[k].name_offset);
            let field = value
                .get(name)
                .with_context(|| format!("no field `{name}`"))?;
            write_node(t, k, field, out)?;
        }
    }
    if node.meta_flag & ALIGN != 0 {
        align(out, 4);
    }
    Ok(())
}

fn write_leaf(type_name: &str, text: &str, out: &mut Vec<u8>) -> Result<()> {
    match type_name {
        "bool" => out.push(u8::from(text == "true")),
        "UInt8" | "char" => out.push(text.parse::<u8>()?),
        "SInt8" => out.push(text.parse::<i8>()? as u8),
        "SInt16" | "short" => out.extend(text.parse::<i16>()?.to_le_bytes()),
        "UInt16" | "unsigned short" => out.extend(text.parse::<u16>()?.to_le_bytes()),
        "SInt32" | "int" | "Type*" => out.extend(text.parse::<i32>()?.to_le_bytes()),
        "UInt32" | "unsigned int" => out.extend(text.parse::<u32>()?.to_le_bytes()),
        "float" => out.extend(text.parse::<f32>()?.to_le_bytes()),
        "SInt64" | "long long" => out.extend(text.parse::<i64>()?.to_le_bytes()),
        "UInt64" | "unsigned long long" | "FileSize" => {
            out.extend(text.parse::<u64>()?.to_le_bytes())
        }
        "double" => out.extend(text.parse::<f64>()?.to_le_bytes()),
        other => bail!("unknown leaf type `{other}`"),
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::serialized::SerializedFile;

    /// The lighting data the Editor wrote for a bake of the environment alone.
    fn written() -> Vec<u8> {
        std::fs::read(
            std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join(
                "../../tests/projects/bakeenvironment/Reference/unity-6000.0.71f1/EnvSky/written/LightingData.asset",
            ),
        )
        .unwrap()
    }

    #[test]
    fn the_editors_lighting_data_is_written_again_byte_for_byte() {
        let data = written();
        let parsed = SerializedFile::parse(&data).unwrap();
        let types = raw_types(&data).unwrap();
        let externals = raw_externals(&data).unwrap();
        let mut objects = Vec::new();
        for info in &parsed.objects {
            let type_index = types
                .iter()
                .position(|t| t.class_id == info.class_id)
                .unwrap();
            let value = parsed.read(info.path_id).unwrap();
            let bytes = write_value(&types[type_index], &value).unwrap();
            // Each object's data as the Editor laid it out.
            assert_eq!(
                bytes,
                parsed.object_data(info.path_id).unwrap(),
                "object {}",
                info.path_id
            );
            objects.push(FileObject {
                path_id: info.path_id,
                type_index,
                data: bytes,
            });
        }
        let mut data_order: Vec<usize> = (0..objects.len()).collect();
        data_order.sort_by_key(|&k| parsed.objects[k].offset);
        let file = File {
            unity_version: parsed.unity_version.clone(),
            platform: -2,
            types,
            objects,
            data_order,
            externals,
        };
        assert_eq!(write(&file), data);
    }
}
