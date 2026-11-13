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
    pub const LOD_GROUP: i32 = 205;
    pub const SPRITE: i32 = 213;
}

/// The legacy fileID of the first object of a class: `classID * 100000`. Further objects of the
/// class took `+2`, `+4`, ...
pub fn legacy_file_id(class_id: i32, index: usize) -> i64 {
    class_id as i64 * 100_000 + 2 * index as i64
}

/// The GUID `.meta` files give the Editor's own (built-in) scripts.
pub const BUILTIN_SCRIPT_GUID: &str = "0000000000000000e000000000000000";

/// The Editor's own scripted importers Unbound does not implement, by their script's file ID
/// (others, such as the style sheet importers of `.uss` and `.tss` files, are reproduced by
/// Unbound's own importers).
const UNSUPPORTED_BUILTIN_IMPORTERS: &[(i64, &str)] = &[(12405, "SpeedTree9Importer")];

/// When a `.meta` names one of the Editor's own scripted importers that Unbound does not
/// implement (SpeedTree 9 trees are imported by one): its name.
pub fn unsupported_builtin_importer(meta: &Value) -> Option<&'static str> {
    let script = importer(meta, "ScriptedImporter").get("script")?;
    if script.get("guid")?.as_str()? != BUILTIN_SCRIPT_GUID {
        return None;
    }
    let id = script.get("fileID")?.as_i64()?;
    UNSUPPORTED_BUILTIN_IMPORTERS
        .iter()
        .find(|(script, _)| *script == id)
        .map(|(_, name)| *name)
}

/// The unsupported built-in importer an asset file's `.meta` names, if any
/// ([`unsupported_builtin_importer`]); `None` without a readable `.meta`.
pub fn unsupported_importer_of(file: &std::path::Path) -> Option<&'static str> {
    let mut meta = file.as_os_str().to_owned();
    meta.push(".meta");
    let text = std::fs::read_to_string(meta).ok()?;
    if !text.contains(BUILTIN_SCRIPT_GUID) {
        return None;
    }
    unsupported_builtin_importer(&unbound_yaml::parse_value(&text).ok()?)
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

/// Execute the importers' metadata readers without decoding or converting source files.
/// New fields read by these routines are automatically included in diagnostic coverage.
pub fn read_inputs(meta: &Value, target: &str) {
    if let Some(imp) = meta.get("TextureImporter") {
        let settings = crate::TextureSettings::read(meta, target);
        let _ = crate::sprite::read_sprites(imp, &settings.sprite, "", (1, 1), (1, 1));
    }
    if meta.get("ModelImporter").is_some() {
        let _ = crate::ModelSettings::read(meta);
        crate::model::read_metadata_inputs(importer(meta, "ModelImporter"));
    }
    if meta.get("AudioImporter").is_some() {
        let _ = crate::AudioSettings::read(meta, target);
    }
    if meta.get("TrueTypeFontImporter").is_some() {
        let _ = crate::font::FontSettings::read(meta);
    }
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

/// The ID Unity 2019.3+ gives an imported object its meta does not list: the xxHash64 of
/// `Type:<class name>-><name><index>`, the index counting earlier objects of that class and name
/// (measured on the 6000.0 Editor: every object of `fileIdsGeneration: 1` models, the
/// meshes, materials and clips of `fileIdsGeneration: 2` ones).
pub fn hashed_file_id(class_name: &str, name: &str, index: u32) -> i64 {
    twox_hash::XxHash64::oneshot(0, format!("Type:{class_name}->{name}{index}").as_bytes()) as i64
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

    /// An object's ID: listed, or Unity's hash of its class and name ([`hashed_file_id`]).
    pub fn hashed(&mut self, class_id: i32, class_name: &str, name: &str) -> (i64, FileIdSource) {
        if let Some(id) = self.listed(class_id, name) {
            return (id, FileIdSource::Meta);
        }
        let free = (0..64)
            .map(|index| hashed_file_id(class_name, name, index))
            .find(|id| !self.used.contains(id));
        self.id(class_id, name, free)
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

    #[test]
    fn reads_the_importer_from_an_assets_meta_file() {
        let dir = std::env::temp_dir().join(format!("unbound-meta-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let tree = dir.join("Tree.st9");
        std::fs::write(&tree, [0xff, 0xfe, 0x00]).unwrap();
        std::fs::write(
            dir.join("Tree.st9.meta"),
            format!("fileFormatVersion: 2\nScriptedImporter:\n  script: {{fileID: 12405, guid: {BUILTIN_SCRIPT_GUID}, type: 0}}\n"),
        )
        .unwrap();
        assert_eq!(unsupported_importer_of(&tree), Some("SpeedTree9Importer"));
        // Without a meta, or with another importer's.
        assert_eq!(unsupported_importer_of(&dir.join("Missing.st9")), None);
        let sheet = dir.join("Style.uss");
        std::fs::write(&sheet, "a {}").unwrap();
        std::fs::write(
            dir.join("Style.uss.meta"),
            format!("fileFormatVersion: 2\nScriptedImporter:\n  script: {{fileID: 12385, guid: {BUILTIN_SCRIPT_GUID}, type: 0}}\n"),
        )
        .unwrap();
        assert_eq!(unsupported_importer_of(&sheet), None);
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn names_the_editors_own_importers_unbound_does_not_implement() {
        let meta = |guid: &str, id: i64| {
            unbound_yaml::parse_value(&format!(
                "fileFormatVersion: 2\nScriptedImporter:\n  script: {{fileID: {id}, guid: {guid}, type: 0}}\n"
            ))
            .unwrap()
        };
        assert_eq!(
            unsupported_builtin_importer(&meta(BUILTIN_SCRIPT_GUID, 12405)),
            Some("SpeedTree9Importer")
        );
        // The Editor's style sheet importer (`.uss`): Unbound imports those itself.
        assert_eq!(
            unsupported_builtin_importer(&meta(BUILTIN_SCRIPT_GUID, 12385)),
            None
        );
        // A project's or package's importer is run by Unbound.
        assert_eq!(
            unsupported_builtin_importer(&meta("4dd1a3e1c2b5a4b4aa7e8b1c3b1e5d00", 11500000)),
            None
        );
        assert_eq!(
            unsupported_builtin_importer(
                &unbound_yaml::parse_value("TextureImporter:\n  a: 1\n").unwrap()
            ),
            None
        );
    }

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
