//! The import settings the Editor gives the textures SpeedTree 9 trees use (Unity 6000.0's
//! SpeedTree 9 asset postprocessor, which runs whenever a `.st9` file is imported): for each of the
//! tree's materials, its colour map imports with alpha as transparency and mip maps preserving
//! alpha-test coverage at a reference value of 0.1, its normal map as a normal map, and its extra
//! map without sRGB. The Editor saves those settings into the textures' `.meta` files; Unbound
//! writes no authored file, so a texture import applies them to its own reading of the `.meta`
//! (the other settings stay as the `.meta` says), with the trees that use it as dependencies of
//! its cache entry.
use std::collections::HashMap;
use std::path::{Path, PathBuf};

use unbound_yaml::Value;

/// What the trees use a texture as (one texture can be several maps).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct TreeMapRoles {
    pub color: bool,
    pub normal: bool,
    pub extra: bool,
}

/// The textures the project's SpeedTree 9 trees use, by file, with the trees using each.
#[derive(Debug, Default)]
pub struct TreeTextures {
    textures: HashMap<PathBuf, (TreeMapRoles, Vec<PathBuf>)>,
}

/// The signature a `.st9` file starts with.
const SIGNATURE: &[u8] = b"SpeedTree9______";
/// The root table's entry of the materials.
const ROOT_MATERIALS: usize = 10;
/// A material's entry of its maps.
const MATERIAL_MAPS: usize = 4;
/// The alpha-test reference value the colour maps' mip maps preserve the coverage of.
const COLOR_ALPHA_TEST_REFERENCE: &str = "0.1";
/// `TextureImporterType.NormalMap`.
const NORMAL_MAP_TYPE: &str = "1";

impl TreeTextures {
    /// Reads the trees (`.st9` files, as `(project path, file)`): a texture is a map of a tree's
    /// material when the map is used and names a file relative to the tree's folder. A tree that
    /// is not a readable SpeedTree 9 file uses no texture (the Editor logs an error for it).
    pub fn read<'a>(trees: impl IntoIterator<Item = (&'a str, &'a Path)>) -> Self {
        let mut textures: HashMap<PathBuf, (TreeMapRoles, Vec<PathBuf>)> = HashMap::new();
        for (path, file) in trees {
            // The Editor's postprocessor takes `.st9` files by their exact extension.
            if !path.ends_with(".st9") {
                continue;
            }
            let Ok(bytes) = std::fs::read(file) else {
                continue;
            };
            let Some(materials) = material_maps(&bytes) else {
                continue;
            };
            let folder = file.parent().unwrap_or(Path::new(""));
            for maps in materials {
                for (index, map) in maps.iter().enumerate() {
                    let Some(name) = map else { continue };
                    let entry = textures.entry(folder.join(name)).or_default();
                    match index {
                        0 => entry.0.color = true,
                        1 => entry.0.normal = true,
                        _ => entry.0.extra = true,
                    }
                    if !entry.1.iter().any(|t| t == file) {
                        entry.1.push(file.to_path_buf());
                    }
                }
            }
        }
        Self { textures }
    }

    /// The trees using the texture `file` (none for most textures).
    pub fn trees(&self, file: &Path) -> &[PathBuf] {
        self.textures
            .get(file)
            .map(|(_, trees)| trees.as_slice())
            .unwrap_or(&[])
    }

    /// What the trees use the texture `file` as.
    pub fn roles(&self, file: &Path) -> TreeMapRoles {
        self.textures
            .get(file)
            .map(|(roles, _)| *roles)
            .unwrap_or_default()
    }

    /// The texture's `.meta` as the trees' postprocessing leaves it: `None` when no tree uses it,
    /// or when another importer (a scripted one) imports the file, whose settings the
    /// postprocessing leaves alone.
    pub fn apply(&self, file: &Path, meta: &Value) -> Option<Value> {
        let roles = self.roles(file);
        if roles == TreeMapRoles::default() || other_importer(meta) {
            return None;
        }
        let mut meta = meta.clone();
        let importer = child(&mut meta, "TextureImporter");
        if roles.color {
            set(importer, "alphaIsTransparency", "1");
            let mipmaps = child(importer, "mipmaps");
            set(mipmaps, "mipMapsPreserveCoverage", "1");
            set(
                mipmaps,
                "alphaTestReferenceValue",
                COLOR_ALPHA_TEST_REFERENCE,
            );
        }
        if roles.normal {
            set(importer, "textureType", NORMAL_MAP_TYPE);
        }
        if roles.extra {
            set(child(importer, "mipmaps"), "sRGBTexture", "0");
        }
        Some(meta)
    }
}

/// Whether the `.meta` names an importer other than the texture importer.
fn other_importer(meta: &Value) -> bool {
    let Value::Map(map) = meta else { return false };
    map.0
        .iter()
        .any(|(key, _)| key.ends_with("Importer") && key != "TextureImporter")
}

/// The mapping at `key` of `value` (made a mapping if it is not one), created when missing.
fn child<'v>(value: &'v mut Value, key: &str) -> &'v mut Value {
    if !matches!(value, Value::Map(_)) {
        *value = Value::Map(Default::default());
    }
    let Value::Map(map) = value else {
        unreachable!()
    };
    if !matches!(map.get_mut(key), Some(Value::Map(_))) {
        map.insert(key, Value::Map(Default::default()));
    }
    map.get_mut(key).expect("just inserted")
}

fn set(value: &mut Value, key: &str, scalar: &str) {
    if let Value::Map(map) = value {
        map.insert(key, Value::Scalar(scalar.to_string()));
    }
}

/// The first three maps (colour, normal, extra) of each material of a SpeedTree 9 file: a map's
/// file name when it is used and names one (the format as `Unbound.SpeedTree.SpeedTree9File`
/// in the managed engine reads it for the tree's import). `None` when the file is not a readable SpeedTree 9
/// file. Tables are a uint32 count and that many uint32 offsets, relative to the table, to their
/// entries; a string is a uint32 length counting a terminating zero, then its bytes;
/// little-endian.
pub fn material_maps(bytes: &[u8]) -> Option<Vec<[Option<String>; 3]>> {
    if !bytes.starts_with(SIGNATURE) {
        return None;
    }
    let root = SIGNATURE.len();
    let materials = entry(bytes, root, ROOT_MATERIALS)?;
    let mut result = Vec::new();
    for m in 0..count(bytes, materials, 4)? {
        let material = entry(bytes, materials, m)?;
        let maps = entry(bytes, material, MATERIAL_MAPS)?;
        let mut names: [Option<String>; 3] = Default::default();
        for (index, name) in names.iter_mut().enumerate().take(count(bytes, maps, 4)?) {
            let map = entry(bytes, maps, index)?;
            let used = uint(bytes, entry(bytes, map, 0)?)? != 0;
            let path = string(bytes, entry(bytes, map, 1)?)?;
            if used && !path.is_empty() {
                *name = Some(path);
            }
        }
        result.push(names);
    }
    Some(result)
}

fn uint(bytes: &[u8], at: usize) -> Option<u32> {
    let b = bytes.get(at..at.checked_add(4)?)?;
    Some(u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
}

/// The absolute offset of entry `index` of the table at `table`.
fn entry(bytes: &[u8], table: usize, index: usize) -> Option<usize> {
    let offset = uint(
        bytes,
        table.checked_add(index.checked_add(1)?.checked_mul(4)?)?,
    )?;
    table.checked_add(offset as usize)
}

/// The count at `at` of items of `size` bytes that follow it, which must fit in the file.
fn count(bytes: &[u8], at: usize, size: usize) -> Option<usize> {
    let n = uint(bytes, at)? as usize;
    let end = at.checked_add(4)?.checked_add(n.checked_mul(size)?)?;
    (end <= bytes.len()).then_some(n)
}

fn string(bytes: &[u8], at: usize) -> Option<String> {
    let length = count(bytes, at, 1)?;
    if length <= 1 {
        return Some(String::new());
    }
    Some(String::from_utf8_lossy(&bytes[at + 4..at + 4 + length - 1]).into_owned())
}

/// A SpeedTree 9 file with only materials, each with its maps `(used, path)` (tests).
#[cfg(test)]
pub(crate) fn test_tree(materials: &[Vec<(bool, &str)>]) -> Vec<u8> {
    let materials: Vec<Vec<Vec<u8>>> = materials
        .iter()
        .map(|maps| {
            maps.iter()
                .map(|(used, path)| tests::map(*used, path))
                .collect()
        })
        .collect();
    tests::tree(&materials)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A table of `entries` (each already laid out), placed at `base` in the file.
    fn table(entries: &[Vec<u8>]) -> Vec<u8> {
        let header = 4 + 4 * entries.len();
        let mut out = (entries.len() as u32).to_le_bytes().to_vec();
        let mut offset = header;
        for e in entries {
            out.extend_from_slice(&(offset as u32).to_le_bytes());
            offset += e.len();
        }
        for e in entries {
            out.extend_from_slice(e);
        }
        out
    }

    fn string_entry(s: &str) -> Vec<u8> {
        let mut out = ((s.len() + 1) as u32).to_le_bytes().to_vec();
        out.extend_from_slice(s.as_bytes());
        out.push(0);
        out
    }

    pub(super) fn map(used: bool, path: &str) -> Vec<u8> {
        table(&[
            (used as u32).to_le_bytes().to_vec(),
            string_entry(path),
            vec![0; 16],
        ])
    }

    pub(super) fn tree(materials: &[Vec<Vec<u8>>]) -> Vec<u8> {
        let materials: Vec<Vec<u8>> = materials
            .iter()
            .map(|maps| {
                table(&[
                    string_entry("Mat"),
                    vec![0; 4],
                    vec![0; 4],
                    vec![0; 4],
                    table(maps),
                ])
            })
            .collect();
        let mut root: Vec<Vec<u8>> = (0..ROOT_MATERIALS).map(|_| vec![0; 4]).collect();
        root.push(table(&materials));
        let mut bytes = SIGNATURE.to_vec();
        bytes.extend(table(&root));
        bytes
    }

    #[test]
    fn reads_the_used_maps_of_each_material() {
        let bytes = tree(&[
            vec![
                map(true, "Leaf_Color.png"),
                map(true, "Leaf_Normal.png"),
                map(true, "Leaf_Extra.png"),
                map(true, "Other.png"),
            ],
            vec![
                map(true, "Bark_Color.png"),
                map(false, "Unused.png"),
                map(true, ""),
            ],
        ]);
        let maps = material_maps(&bytes).unwrap();
        assert_eq!(maps.len(), 2);
        assert_eq!(
            maps[0],
            [
                Some("Leaf_Color.png".into()),
                Some("Leaf_Normal.png".into()),
                Some("Leaf_Extra.png".into())
            ]
        );
        assert_eq!(maps[1], [Some("Bark_Color.png".into()), None, None]);
        // Not a SpeedTree 9 file, or cut short: no maps.
        assert!(material_maps(b"SpeedTree8______").is_none());
        assert!(material_maps(&bytes[..bytes.len() / 2]).is_none());
    }

    #[test]
    fn a_used_texture_imports_with_the_trees_settings_and_keeps_the_rest() {
        let dir = std::env::temp_dir().join(format!("st9-textures-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("Tree.st9");
        std::fs::write(
            &file,
            tree(&[vec![
                map(true, "Color.png"),
                map(true, "Normal.png"),
                map(true, "Extra.png"),
            ]]),
        )
        .unwrap();
        let textures = TreeTextures::read([
            ("Assets/Tree.st9", file.as_path()),
            ("Assets/Tree.ST9", file.as_path()),
        ]);
        std::fs::remove_dir_all(&dir).ok();
        assert_eq!(textures.trees(&dir.join("Color.png")), [file.clone()]);
        assert!(textures.trees(&dir.join("Other.png")).is_empty());
        let meta = unbound_yaml::parse_value(
            "TextureImporter:\n  mipmaps:\n    sRGBTexture: 1\n    mipMapsPreserveCoverage: 0\n  textureType: 0\n  maxTextureSize: 512\n",
        )
        .unwrap();
        let extra = textures.apply(&dir.join("Extra.png"), &meta).unwrap();
        let imp = extra.get("TextureImporter").unwrap();
        assert_eq!(
            imp.get("mipmaps").unwrap().get("sRGBTexture"),
            Some(&Value::Scalar("0".into()))
        );
        assert_eq!(
            imp.get("maxTextureSize"),
            Some(&Value::Scalar("512".into()))
        );
        assert_eq!(imp.get("textureType"), Some(&Value::Scalar("0".into())));
        let color = textures.apply(&dir.join("Color.png"), &meta).unwrap();
        let imp = color.get("TextureImporter").unwrap();
        assert_eq!(
            imp.get("alphaIsTransparency"),
            Some(&Value::Scalar("1".into()))
        );
        assert_eq!(
            imp.get("mipmaps").unwrap().get("alphaTestReferenceValue"),
            Some(&Value::Scalar("0.1".into()))
        );
        assert_eq!(
            imp.get("mipmaps").unwrap().get("sRGBTexture"),
            Some(&Value::Scalar("1".into()))
        );
        // Without a .meta: the settings alone.
        let normal = textures
            .apply(&dir.join("Normal.png"), &Value::Null)
            .unwrap();
        assert_eq!(
            normal.get("TextureImporter").unwrap().get("textureType"),
            Some(&Value::Scalar("1".into()))
        );
        assert!(textures.apply(&dir.join("Other.png"), &meta).is_none());
        // A file a scripted importer imports keeps its settings.
        let scripted =
            unbound_yaml::parse_value("ScriptedImporter:\n  script: {fileID: 1}\n").unwrap();
        assert!(textures.apply(&dir.join("Extra.png"), &scripted).is_none());
    }
}
