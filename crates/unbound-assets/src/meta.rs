//! Reading importer settings from `.meta` files, and the sub-asset fileID tables.
//!
//! A `.meta` holds one importer section (`TextureImporter:`, `ModelImporter:`, ...). Its layout
//! changed over Unity releases; readers here accept every layout this project's assets use and
//! fall back to Unity's defaults for missing fields.
//!
//! An imported asset's objects (the Texture2D and its Sprites, a model's GameObjects, Meshes, ...)
//! are referenced from scenes as `{fileID, guid}`. Their fileIDs come from:
//!
//! * `fileIDToRecycleName` (before 2019.3): `fileID: name`, the class given by the fileID's range
//!   (`fileID / 100000` is the class ID: 100000 GameObject, 4300000 Mesh, 21300000 Sprite, ...);
//! * `internalIDToNameTable` (2019.3+): `- first: {classID: fileID}, second: name`;
//! * per-object fields such as a sprite's `internalID`.
//!
//! Unity converts the old table into the new one on upgrade, keeping the IDs, so both name the
//! same objects.
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};
use unbound_yaml::Value;

/// Unity class IDs of the objects importers create.
pub mod class {
    pub const GAME_OBJECT: i32 = 1;
    pub const TRANSFORM: i32 = 4;
    pub const MATERIAL: i32 = 21;
    pub const MESH_RENDERER: i32 = 23;
    pub const TEXTURE_2D: i32 = 28;
    pub const MESH_FILTER: i32 = 33;
    pub const MESH: i32 = 43;
    pub const ANIMATION_CLIP: i32 = 74;
    pub const AVATAR: i32 = 90;
    pub const ANIMATOR: i32 = 95;
    pub const SKINNED_MESH_RENDERER: i32 = 137;
    pub const SPRITE: i32 = 213;
}

/// The legacy fileID of the first object of a class: `classID * 100000`. Further objects of the
/// class took `+2`, `+4`, ...
pub fn legacy_file_id(class_id: i32, index: usize) -> i64 {
    class_id as i64 * 100_000 + 2 * index as i64
}

/// The importer section of a parsed `.meta` (`TextureImporter`, ...), or [`Value::Null`].
pub fn importer<'a>(meta: &'a Value, name: &str) -> &'a Value {
    static NULL: Value = Value::Null;
    meta.get(name).unwrap_or(&NULL)
}

pub(crate) fn int(v: &Value, path: &str) -> Option<i64> {
    v.path(path)?.as_i64()
}

pub(crate) fn float(v: &Value, path: &str) -> Option<f32> {
    v.path(path)?.as_f64().map(|f| f as f32)
}

pub(crate) fn boolean(v: &Value, path: &str) -> Option<bool> {
    v.path(path)?.as_bool()
}

#[allow(dead_code)]
pub(crate) fn string(v: &Value, path: &str) -> Option<String> {
    v.path(path)?.as_str().map(str::to_string)
}

/// `{x: 1, y: 2}`, also written as a block mapping in old metas.
pub(crate) fn vec2(v: Option<&Value>) -> Option<[f32; 2]> {
    let v = v?;
    Some([float(v, "x")?, float(v, "y")?])
}

/// `{x, y, z, w}`.
pub(crate) fn vec4(v: Option<&Value>) -> Option<[f32; 4]> {
    let v = v?;
    Some([
        float(v, "x")?,
        float(v, "y")?,
        float(v, "z")?,
        float(v, "w")?,
    ])
}

/// Outlines: a list of polygons, each a list of `{x, y}`.
pub(crate) fn polygons(v: Option<&Value>) -> Vec<Vec<[f32; 2]>> {
    let Some(seq) = v.and_then(Value::as_seq) else {
        return Vec::new();
    };
    seq.iter()
        .map(|poly| {
            poly.as_seq()
                .unwrap_or_default()
                .iter()
                .filter_map(|p| vec2(Some(p)))
                .collect()
        })
        .collect()
}

/// Where a sub-asset's fileID came from.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum FileIdSource {
    /// A fixed ID (Texture2D 2800000, a single sprite 21300000, ...).
    Fixed,
    /// Listed in the `.meta` (`fileIDToRecycleName`, `internalIDToNameTable`, `internalID`).
    Meta,
    /// Not listed: derived the way Unity would for a meta of this layout, as far as known. Scenes
    /// referencing such objects are rare (Unity writes the table when it imports); a mismatch
    /// shows up as a missing reference.
    Derived,
}

/// One named object of the sub-asset tables.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct FileIdEntry {
    pub class_id: i32,
    pub file_id: i64,
    pub name: String,
}

/// The `fileIDToRecycleName` / `internalIDToNameTable` entries of an importer section.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct FileIdTable {
    pub entries: Vec<FileIdEntry>,
    /// The meta uses the pre-2019.3 layout (`fileIDToRecycleName`).
    pub legacy: bool,
}

impl FileIdTable {
    pub fn read(importer: &Value) -> Self {
        let mut table = FileIdTable::default();
        if let Some(map) = importer.get("fileIDToRecycleName").and_then(Value::as_map) {
            table.legacy = true;
            for (key, name) in map.iter() {
                let (Ok(file_id), Some(name)) = (key.trim().parse::<i64>(), name.as_str()) else {
                    continue;
                };
                table.entries.push(FileIdEntry {
                    class_id: (file_id / 100_000) as i32,
                    file_id,
                    name: name.to_string(),
                });
            }
        }
        if let Some(seq) = importer
            .get("internalIDToNameTable")
            .and_then(Value::as_seq)
        {
            for item in seq {
                let Some(first) = item.get("first").and_then(Value::as_map) else {
                    continue;
                };
                let name = item.get("second").and_then(Value::as_str).unwrap_or("");
                for (class, id) in first.iter() {
                    let (Ok(class_id), Some(file_id)) = (class.trim().parse(), id.as_i64()) else {
                        continue;
                    };
                    table.entries.push(FileIdEntry {
                        class_id,
                        file_id,
                        name: name.to_string(),
                    });
                }
            }
        }
        table
    }

    /// The fileIDs listed for `(class, name)`, in table order.
    pub fn find<'s>(&'s self, class_id: i32, name: &'s str) -> impl Iterator<Item = i64> + 's {
        self.entries
            .iter()
            .filter(move |e| e.class_id == class_id && e.name == name)
            .map(|e| e.file_id)
    }
}

/// Hands out fileIDs for the objects an importer creates: from the table when listed (the n-th
/// object of a class with a given name gets the n-th listed ID), else a derived ID that no other
/// object uses.
pub struct FileIdAllocator<'a> {
    table: &'a FileIdTable,
    used: HashSet<i64>,
    taken: HashMap<(i32, String), usize>,
}

impl<'a> FileIdAllocator<'a> {
    pub fn new(table: &'a FileIdTable) -> Self {
        Self {
            used: table.entries.iter().map(|e| e.file_id).collect(),
            table,
            taken: HashMap::new(),
        }
    }

    /// Marks an ID as used by an object that carries its own ID.
    pub fn reserve(&mut self, id: i64) {
        self.used.insert(id);
    }

    /// The listed ID of the next object of `(class, name)`, if any.
    pub fn listed(&mut self, class_id: i32, name: &str) -> Option<i64> {
        let n = self.taken.entry((class_id, name.to_string())).or_default();
        let id = self.table.find(class_id, name).nth(*n)?;
        *n += 1;
        Some(id)
    }

    /// The listed ID, else `derived` when it is free, else the first free legacy-style ID.
    pub fn id(&mut self, class_id: i32, name: &str, derived: Option<i64>) -> (i64, FileIdSource) {
        if let Some(id) = self.listed(class_id, name) {
            return (id, FileIdSource::Meta);
        }
        if let Some(id) = derived.filter(|id| !self.used.contains(id)) {
            self.used.insert(id);
            return (id, FileIdSource::Derived);
        }
        let id = (0..)
            .map(|i| legacy_file_id(class_id, i))
            .find(|id| !self.used.contains(id))
            .expect("free id");
        self.used.insert(id);
        (id, FileIdSource::Derived)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn importer_of(text: &str) -> Value {
        let meta = unbound_yaml::parse_value(text).unwrap();
        importer(&meta, "ModelImporter").clone()
    }

    #[test]
    fn reads_the_legacy_table() {
        let imp = importer_of(
            "fileFormatVersion: 2\nModelImporter:\n  fileIDToRecycleName:\n    100000: //RootNode\n    100002: eye_l\n    4300000: aisberg\n    2100000: lambert1\n  serializedVersion: 19\n",
        );
        let table = FileIdTable::read(&imp);
        assert!(table.legacy);
        assert_eq!(table.entries.len(), 4);
        assert_eq!(table.find(class::GAME_OBJECT, "eye_l").next(), Some(100002));
        assert_eq!(table.find(class::MESH, "aisberg").next(), Some(4300000));
        assert_eq!(
            table.find(class::MATERIAL, "lambert1").next(),
            Some(2100000)
        );
    }

    #[test]
    fn reads_the_internal_id_table() {
        let imp = importer_of(
            "ModelImporter:\n  internalIDToNameTable:\n  - first:\n      1: 919132149155446097\n    second: //RootNode\n  - first:\n      43: -2413806693520163455\n    second: Cube\n  externalObjects: {}\n",
        );
        let table = FileIdTable::read(&imp);
        assert!(!table.legacy);
        assert_eq!(
            table.find(class::GAME_OBJECT, "//RootNode").next(),
            Some(919132149155446097)
        );
        assert_eq!(
            table.find(class::MESH, "Cube").next(),
            Some(-2413806693520163455)
        );
    }

    #[test]
    fn allocates_listed_then_free_ids() {
        let imp = importer_of(
            "ModelImporter:\n  fileIDToRecycleName:\n    4300000: a\n    4300002: a\n    4300004: b\n",
        );
        let table = FileIdTable::read(&imp);
        let mut ids = FileIdAllocator::new(&table);
        assert_eq!(
            ids.id(class::MESH, "a", None),
            (4300000, FileIdSource::Meta)
        );
        assert_eq!(
            ids.id(class::MESH, "a", None),
            (4300002, FileIdSource::Meta)
        );
        // A third "a" and an unlisted "c" get the first free legacy IDs.
        assert_eq!(
            ids.id(class::MESH, "a", None),
            (4300006, FileIdSource::Derived)
        );
        assert_eq!(
            ids.id(class::MESH, "c", None),
            (4300008, FileIdSource::Derived)
        );
        assert_eq!(
            ids.id(class::MESH, "b", None),
            (4300004, FileIdSource::Meta)
        );
        assert_eq!(
            ids.id(class::MESH, "d", Some(7)),
            (7, FileIdSource::Derived)
        );
    }
}
