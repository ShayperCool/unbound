//! Unity's binary serialized files (the install's built-in resources, `unity_builtin_extra`):
//! the objects a file holds and, where the file carries type trees, each object's fields read
//! into the same value tree a YAML asset parses to.
use anyhow::{Context, Result, bail};
use unbound_yaml::{Map, Value};

/// A node of a type tree: a field (or array element) with its type, nesting level and flags.
#[derive(Debug, Clone)]
pub struct TypeNode {
    pub level: u8,
    pub type_name: String,
    pub name: String,
    pub byte_size: i32,
    pub meta_flag: u32,
}

/// An object of the file: where its data lies and its type.
#[derive(Debug, Clone, Copy)]
pub struct ObjectInfo {
    pub path_id: i64,
    pub offset: usize,
    pub size: usize,
    pub class_id: i32,
    type_index: usize,
}

pub struct SerializedFile<'a> {
    data: &'a [u8],
    pub version: u32,
    pub unity_version: String,
    /// Per type: its class and type tree (empty without type trees).
    types: Vec<(i32, Vec<TypeNode>)>,
    pub objects: Vec<ObjectInfo>,
}

/// A field aligns the stream to 4 bytes after it.
const ALIGN: u32 = 0x4000;

impl<'a> SerializedFile<'a> {
    /// Reads the header, the types and the object table (format 9 and later, little-endian data).
    pub fn parse(data: &'a [u8]) -> Result<Self> {
        let be32 = |o: usize| -> Result<u32> {
            Ok(u32::from_be_bytes(data.get(o..o + 4).context("truncated")?.try_into()?))
        };
        let be64 = |o: usize| -> Result<u64> {
            Ok(u64::from_be_bytes(data.get(o..o + 8).context("truncated")?.try_into()?))
        };
        let le16 = |o: usize| -> Result<u16> {
            Ok(u16::from_le_bytes(data.get(o..o + 2).context("truncated")?.try_into()?))
        };
        let le32 = |o: usize| -> Result<u32> {
            Ok(u32::from_le_bytes(data.get(o..o + 4).context("truncated")?.try_into()?))
        };
        let le64 = |o: usize| -> Result<u64> {
            Ok(u64::from_le_bytes(data.get(o..o + 8).context("truncated")?.try_into()?))
        };
        let version = be32(8)?;
        if !(9..=30).contains(&version) {
            bail!("unsupported serialized file version {version}");
        }
        let (data_offset, big_endian, mut off) = if version >= 22 {
            (be64(32)? as usize, data.get(16).copied().unwrap_or(0) != 0, 48)
        } else {
            (be32(12)? as usize, data.get(16).copied().unwrap_or(0) != 0, 20)
        };
        if big_endian {
            bail!("big-endian serialized files are not supported");
        }
        let end = data[off..].iter().position(|&b| b == 0).context("truncated")?;
        let unity_version = String::from_utf8_lossy(&data[off..off + end]).to_string();
        off += end + 1;
        off += 4; // target platform
        let type_trees = *data.get(off).context("truncated")? != 0;
        off += 1;
        let type_count = le32(off)? as usize;
        off += 4;
        let mut types = Vec::with_capacity(type_count);
        for _ in 0..type_count {
            let class_id = le32(off)? as i32;
            off += 4 + 1 + 2;
            if class_id == 114 || class_id < 0 {
                off += 16;
            }
            off += 16;
            let mut nodes = Vec::new();
            if type_trees {
                if version < 12 {
                    bail!("serialized files with old-style type trees are not supported");
                }
                let count = le32(off)? as usize;
                let strings_size = le32(off + 4)? as usize;
                let node_size = if version >= 19 { 32 } else { 24 };
                let nodes_at = off + 8;
                let strings_at = nodes_at + count * node_size;
                let strings = data
                    .get(strings_at..strings_at + strings_size)
                    .context("truncated type tree")?;
                let name = |offset: u32| -> String {
                    if offset & 0x8000_0000 != 0 {
                        common_string(offset & 0x7fff_ffff).to_string()
                    } else {
                        let s = &strings[(offset as usize).min(strings.len())..];
                        let end = s.iter().position(|&b| b == 0).unwrap_or(s.len());
                        String::from_utf8_lossy(&s[..end]).to_string()
                    }
                };
                for k in 0..count {
                    let n = nodes_at + k * node_size;
                    let _ = le16(n)?;
                    nodes.push(TypeNode {
                        level: data[n + 2],
                        type_name: name(le32(n + 4)?),
                        name: name(le32(n + 8)?),
                        byte_size: le32(n + 12)? as i32,
                        meta_flag: le32(n + 20)?,
                    });
                }
                off = strings_at + strings_size;
                if version >= 21 {
                    let dependencies = le32(off)? as usize;
                    off += 4 + 4 * dependencies;
                }
            }
            types.push((class_id, nodes));
        }
        let object_count = le32(off)? as usize;
        off += 4;
        let mut objects = Vec::with_capacity(object_count);
        for _ in 0..object_count {
            off = (off + 3) & !3;
            let path_id = le64(off)? as i64;
            off += 8;
            let start = if version >= 22 {
                off += 8;
                le64(off - 8)? as usize
            } else {
                off += 4;
                le32(off - 4)? as usize
            };
            let size = le32(off)? as usize;
            let type_index = le32(off + 4)? as usize;
            off += 8;
            let class_id = types.get(type_index).map_or(0, |t| t.0);
            objects.push(ObjectInfo {
                path_id,
                offset: data_offset + start,
                size,
                class_id,
                type_index,
            });
        }
        Ok(SerializedFile {
            data,
            version,
            unity_version,
            types,
            objects,
        })
    }

    pub fn info(&self, path_id: i64) -> Result<ObjectInfo> {
        self.objects
            .iter()
            .find(|o| o.path_id == path_id)
            .copied()
            .with_context(|| format!("no object {path_id}"))
    }

    /// The raw data of an object.
    pub fn object_data(&self, path_id: i64) -> Result<&'a [u8]> {
        let o = self.info(path_id)?;
        self.data
            .get(o.offset..o.offset + o.size)
            .context("the object lies outside the file")
    }

    /// An object's fields by its type tree: structs as maps, arrays as sequences, strings as
    /// scalars, byte arrays as hexadecimal scalars, references as `{m_FileID, m_PathID}`.
    pub fn read(&self, path_id: i64) -> Result<Value> {
        let o = self.info(path_id)?;
        let nodes = &self.types[o.type_index].1;
        if nodes.is_empty() {
            bail!("the file has no type tree for object {path_id}");
        }
        let mut r = Reader::new(self.object_data(path_id)?);
        let (value, _) = read_node(nodes, 0, &mut r)?;
        Ok(value)
    }
}

/// The node after `i`'s subtree.
fn subtree_end(nodes: &[TypeNode], i: usize) -> usize {
    let level = nodes[i].level;
    let mut j = i + 1;
    while j < nodes.len() && nodes[j].level > level {
        j += 1;
    }
    j
}

fn children(nodes: &[TypeNode], i: usize) -> Vec<usize> {
    let level = nodes[i].level;
    let end = subtree_end(nodes, i);
    (i + 1..end).filter(|&j| nodes[j].level == level + 1).collect()
}

fn read_node(nodes: &[TypeNode], i: usize, r: &mut Reader) -> Result<(Value, usize)> {
    let node = &nodes[i];
    let kids = children(nodes, i);
    let value = if node.type_name == "TypelessData" {
        // Raw bytes (texture image data): a count, then the bytes.
        let count = r.i32()?.max(0) as usize;
        Value::Scalar(r.bytes(count)?.iter().map(|b| format!("{b:02x}")).collect())
    } else if kids.is_empty() {
        read_leaf(&node.type_name, r)?
    } else if nodes[kids[0]].type_name == "Array" {
        let array = kids[0];
        let array_kids = children(nodes, array);
        let element = *array_kids.get(1).context("an array without an element type")?;
        let count = r.i32()?.max(0) as usize;
        let element_type = nodes[element].type_name.as_str();
        let value = if children(nodes, element).is_empty()
            && matches!(element_type, "char" | "UInt8" | "SInt8")
        {
            let bytes = r.bytes(count)?;
            if node.type_name == "string" {
                Value::Scalar(String::from_utf8_lossy(bytes).to_string())
            } else {
                Value::Scalar(bytes.iter().map(|b| format!("{b:02x}")).collect())
            }
        } else {
            let mut items = Vec::with_capacity(count.min(1 << 16));
            for _ in 0..count {
                items.push(read_node(nodes, element, r)?.0);
            }
            Value::Seq(items)
        };
        if nodes[array].meta_flag & ALIGN != 0 {
            r.align();
        }
        value
    } else {
        let mut map = Map::default();
        for k in kids {
            let (v, _) = read_node(nodes, k, r)
                .with_context(|| format!("field `{}` ({})", nodes[k].name, nodes[k].type_name))?;
            map.0.push((nodes[k].name.clone(), v));
        }
        Value::Map(map)
    };
    if node.meta_flag & ALIGN != 0 {
        r.align();
    }
    Ok((value, subtree_end(nodes, i)))
}

fn read_leaf(type_name: &str, r: &mut Reader) -> Result<Value> {
    let s = match type_name {
        "bool" => (r.bytes(1)?[0] != 0).to_string(),
        "UInt8" | "char" => r.bytes(1)?[0].to_string(),
        "SInt8" => (r.bytes(1)?[0] as i8).to_string(),
        "SInt16" | "short" => i16::from_le_bytes(r.bytes(2)?.try_into()?).to_string(),
        "UInt16" | "unsigned short" => u16::from_le_bytes(r.bytes(2)?.try_into()?).to_string(),
        "SInt32" | "int" | "Type*" => r.i32()?.to_string(),
        "UInt32" | "unsigned int" => (r.i32()? as u32).to_string(),
        "float" => format!("{:?}", r.f32()?),
        "SInt64" | "long long" => r.i64()?.to_string(),
        "UInt64" | "unsigned long long" | "FileSize" => (r.i64()? as u64).to_string(),
        "double" => format!("{:?}", f64::from_le_bytes(r.bytes(8)?.try_into()?)),
        other => bail!("unknown leaf type `{other}`"),
    };
    Ok(Value::Scalar(s))
}

/// Unity's shared type tree strings, by offset (a name offset with the high bit set).
fn common_string(offset: u32) -> &'static str {
    const COMMON: &str = "AABB\0AnimationClip\0AnimationCurve\0AnimationState\0Array\0Base\0BitField\0bitset\0bool\0char\0ColorRGBA\0Component\0data\0deque\0double\0dynamic_array\0FastPropertyName\0first\0float\0Font\0GameObject\0Generic Mono\0GradientNEW\0GUID\0GUIStyle\0int\0list\0long long\0map\0Matrix4x4f\0MdFour\0MonoBehaviour\0MonoScript\0m_ByteSize\0m_Curve\0m_EditorClassIdentifier\0m_EditorHideFlags\0m_Enabled\0m_ExtensionPtr\0m_GameObject\0m_Index\0m_IsArray\0m_IsStatic\0m_MetaFlag\0m_Name\0m_ObjectHideFlags\0m_PrefabInternal\0m_PrefabParentObject\0m_Script\0m_StaticEditorFlags\0m_Type\0m_Version\0Object\0pair\0PPtr<Component>\0PPtr<GameObject>\0PPtr<Material>\0PPtr<MonoBehaviour>\0PPtr<MonoScript>\0PPtr<Object>\0PPtr<Prefab>\0PPtr<Sprite>\0PPtr<TextAsset>\0PPtr<Texture>\0PPtr<Texture2D>\0PPtr<Transform>\0Prefab\0Quaternionf\0Rectf\0RectInt\0RectOffset\0second\0set\0short\0size\0SInt16\0SInt32\0SInt64\0SInt8\0staticvector\0string\0TextAsset\0TextMesh\0Texture\0Texture2D\0Transform\0TypelessData\0UInt16\0UInt32\0UInt64\0UInt8\0unsigned int\0unsigned long long\0unsigned short\0vector\0Vector2f\0Vector3f\0Vector4f\0m_ScriptingClassIdentifier\0Gradient\0Type*\0int2_storage\0int3_storage\0BoundsInt\0m_CorrespondingSourceObject\0m_PrefabInstance\0m_PrefabAsset\0FileSize\0Hash128\0RenderingLayerMask\0";
    let s = &COMMON[(offset as usize).min(COMMON.len())..];
    &s[..s.find('\0').unwrap_or(s.len())]
}

/// Whether a file is a binary serialized file rather than Unity's text serialization.
pub fn is_binary(data: &[u8]) -> bool {
    !data.starts_with(b"%YAML") && SerializedFile::parse(data).is_ok()
}

/// A binary asset file (one the Editor wrote with type trees, as `NavMesh.asset`) as the
/// documents its YAML form would hold: each object under its path id, its class's name and its
/// fields read by the type tree.
pub fn documents(data: &[u8]) -> Result<Vec<unbound_yaml::Document>> {
    let file = SerializedFile::parse(data)?;
    let mut docs = Vec::with_capacity(file.objects.len());
    for o in &file.objects {
        let type_name = file.types[o.type_index]
            .1
            .first()
            .map(|n| n.type_name.clone())
            .with_context(|| format!("object {} has no type tree", o.path_id))?;
        docs.push(unbound_yaml::Document {
            class_id: o.class_id,
            file_id: o.path_id,
            stripped: false,
            type_name,
            body: file.read(o.path_id)?,
        });
    }
    Ok(docs)
}

/// The objects of a serialized file: path id → (offset, size) of its data.
pub fn objects(file: &[u8]) -> Result<Vec<(i64, usize, usize)>> {
    Ok(SerializedFile::parse(file)?
        .objects
        .iter()
        .map(|o| (o.path_id, o.offset, o.size))
        .collect())
}

/// The data of object `path_id`.
pub fn object(file: &[u8], path_id: i64) -> Result<&[u8]> {
    SerializedFile::parse(file)?.object_data(path_id)
}

/// Little-endian reads of an object's fields, 4-byte aligned as Unity writes them.
pub struct Reader<'a> {
    data: &'a [u8],
    pub at: usize,
}

impl<'a> Reader<'a> {
    pub fn new(data: &'a [u8]) -> Self {
        Reader { data, at: 0 }
    }

    pub fn bytes(&mut self, n: usize) -> Result<&'a [u8]> {
        let b = self
            .data
            .get(self.at..self.at + n)
            .context("the object ends early")?;
        self.at += n;
        Ok(b)
    }

    pub fn i32(&mut self) -> Result<i32> {
        Ok(i32::from_le_bytes(self.bytes(4)?.try_into()?))
    }

    pub fn i64(&mut self) -> Result<i64> {
        Ok(i64::from_le_bytes(self.bytes(8)?.try_into()?))
    }

    pub fn f32(&mut self) -> Result<f32> {
        Ok(f32::from_le_bytes(self.bytes(4)?.try_into()?))
    }

    pub fn align(&mut self) {
        self.at = (self.at + 3) & !3;
    }
}
