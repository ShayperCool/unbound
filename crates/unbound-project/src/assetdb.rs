//! The asset database: every asset of `Assets/` and the resolved packages, by GUID and by path.
use rayon::prelude::*;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
};
use unbound_yaml::Guid;

#[derive(Debug, Clone)]
pub struct Asset {
    pub guid: Guid,
    /// The path Unity shows: `Assets/...` or `Packages/<name>/...`.
    pub path: String,
    pub file: PathBuf,
    pub is_folder: bool,
    /// The asset labels its `.meta` lists (`labels:`).
    pub labels: Vec<String>,
}

#[derive(Debug, Default, Clone)]
pub struct AssetDb {
    assets: Vec<Asset>,
    by_guid: HashMap<Guid, usize>,
    by_path: HashMap<String, usize>,
    /// Assets whose `.meta` has no readable GUID, or a GUID another asset has.
    pub problems: Vec<String>,
}

/// The GUID the Editor gives the project's `Assets` folder, which has no `.meta` (matches the
/// Unity 6000.0.71f1 Editor, tests/projects/defaultasset `folder.assetsRoot.guid`).
pub const ASSETS_ROOT_GUID: Guid = Guid(0x0000_0000_0000_0000_1000_0000_0000_0000);

/// The path of the project's own assets root.
pub const ASSETS_ROOT_PATH: &str = "Assets";

impl Asset {
    /// The project's `Assets` folder itself: listed like a folder, with no `.meta` of its own.
    pub fn is_assets_root(&self) -> bool {
        self.guid == ASSETS_ROOT_GUID
    }
}

/// A root of the database: a directory and the path Unity gives it.
pub struct Root<'a> {
    pub dir: &'a Path,
    pub virtual_path: String,
}

/// Unity skips hidden names: a leading `.`, a trailing `~`, `cvs`, and `.tmp` files.
pub fn is_hidden(name: &str) -> bool {
    name.starts_with('.')
        || name.ends_with('~')
        || name.eq_ignore_ascii_case("cvs")
        || name.ends_with(".tmp")
}

impl AssetDb {
    pub fn scan(roots: &[Root<'_>]) -> Self {
        let mut metas = Vec::new();
        for root in roots {
            let walker = walkdir::WalkDir::new(root.dir)
                .follow_links(true)
                .into_iter()
                .filter_entry(|e| e.depth() == 0 || !is_hidden(&e.file_name().to_string_lossy()));
            for entry in walker.flatten() {
                let name = entry.file_name().to_string_lossy();
                if entry.file_type().is_file() && name.ends_with(".meta") {
                    let file = entry.path().with_extension("");
                    let relative = file.strip_prefix(root.dir).unwrap_or(&file);
                    let virtual_path =
                        format!("{}/{}", root.virtual_path, relative.to_string_lossy());
                    metas.push((entry.into_path(), file, virtual_path));
                }
            }
        }
        let read: Vec<_> = metas
            .into_par_iter()
            .map(|(meta, file, path)| {
                let (guid, labels) = read_meta_header(&meta);
                (guid, labels, file, path)
            })
            .collect();
        let mut db = AssetDb::default();
        // The `Assets` folder is an asset of its own (GetAllAssetPaths lists it; it loads as a
        // folder), under a fixed GUID instead of one from a `.meta`.
        if let Some(root) = roots.iter().find(|r| r.virtual_path == ASSETS_ROOT_PATH) {
            db.insert(Asset {
                guid: ASSETS_ROOT_GUID,
                path: ASSETS_ROOT_PATH.to_string(),
                file: root.dir.to_path_buf(),
                is_folder: true,
                labels: Vec::new(),
            });
        }
        for (guid, labels, file, path) in read {
            let Some(guid) = guid else {
                db.problems.push(format!("{path}: .meta has no guid"));
                continue;
            };
            if !file.exists() {
                continue; // an orphaned .meta: Unity deletes these
            }
            if let Some(&other) = db.by_guid.get(&guid) {
                db.problems.push(format!(
                    "{path}: guid {guid} already used by {}",
                    db.assets[other].path
                ));
                continue;
            }
            let index = db.assets.len();
            db.by_guid.insert(guid, index);
            db.by_path.insert(path.to_ascii_lowercase(), index);
            db.assets.push(Asset {
                guid,
                is_folder: file.is_dir(),
                path,
                file,
                labels,
            });
        }
        db
    }

    pub fn len(&self) -> usize {
        self.assets.len()
    }

    pub fn is_empty(&self) -> bool {
        self.assets.is_empty()
    }

    pub fn by_guid(&self, guid: Guid) -> Option<&Asset> {
        self.by_guid.get(&guid).map(|&i| &self.assets[i])
    }

    /// Unity paths compare case-insensitively.
    pub fn by_path(&self, path: &str) -> Option<&Asset> {
        self.by_path
            .get(&path.to_ascii_lowercase())
            .map(|&i| &self.assets[i])
    }

    pub fn iter(&self) -> impl Iterator<Item = &Asset> {
        self.assets.iter()
    }

    /// Lists `asset`, in place of what had its GUID or its path.
    pub fn insert(&mut self, asset: Asset) {
        if let Some(&index) = self.by_guid.get(&asset.guid) {
            let path = self.assets[index].path.clone();
            self.remove_path(&path);
        }
        self.remove_path(&asset.path);
        let index = self.assets.len();
        self.by_guid.insert(asset.guid, index);
        self.by_path.insert(asset.path.to_ascii_lowercase(), index);
        self.assets.push(asset);
    }

    /// Drops the asset at `path` (Unity paths compare case-insensitively), if any.
    pub fn remove_path(&mut self, path: &str) {
        let Some(index) = self.by_path.remove(&path.to_ascii_lowercase()) else {
            return;
        };
        let removed = self.assets.swap_remove(index);
        self.by_guid.remove(&removed.guid);
        if let Some(moved) = self.assets.get(index) {
            self.by_guid.insert(moved.guid, index);
            self.by_path.insert(moved.path.to_ascii_lowercase(), index);
        }
    }
}

/// What changed on disk under `roots` since `db` was made: the asset paths whose `.meta` is new or
/// says something else (a GUID, labels) than `db` has, and the assets of `db` under those roots
/// whose file is gone. Hidden names and what is inside a folder plug-in (`is_folder_plugin`) are
/// not assets; a `.meta` without its asset is no asset either.
pub fn changes(
    db: &AssetDb,
    roots: &[Root<'_>],
    is_folder_plugin: impl Fn(&Path) -> bool,
) -> (Vec<String>, Vec<Asset>) {
    // The walk lists the `.meta` files in name order; they are read in parallel, as `scan` does.
    let mut metas = Vec::new();
    for root in roots {
        let mut walker = walkdir::WalkDir::new(root.dir)
            .follow_links(true)
            .sort_by_file_name()
            .into_iter()
            .filter_entry(|e| e.depth() == 0 || !is_hidden(&e.file_name().to_string_lossy()));
        while let Some(entry) = walker.next() {
            let Ok(entry) = entry else { continue };
            if entry.file_type().is_dir() && entry.depth() > 0 && is_folder_plugin(entry.path()) {
                walker.skip_current_dir();
            }
            let name = entry.file_name().to_string_lossy();
            if !entry.file_type().is_file() || !name.ends_with(".meta") {
                continue;
            }
            let file = entry.path().with_extension("");
            if !file.exists() {
                continue;
            }
            let relative = file.strip_prefix(root.dir).unwrap_or(&file);
            let path = format!("{}/{}", root.virtual_path, relative.to_string_lossy());
            metas.push((path, entry.into_path()));
        }
    }
    let changed = metas
        .into_par_iter()
        .filter_map(|(path, meta)| {
            let (guid, labels) = read_meta_header(&meta);
            let same = db.by_path(&path).is_some_and(|known| {
                Some(known.guid) == guid && known.labels == labels && known.path == path
            });
            (!same).then_some(path)
        })
        .collect();
    let gone = db
        .iter()
        .filter(|asset| {
            roots.iter().any(|root| asset.file.starts_with(root.dir)) && !asset.file.exists()
        })
        .cloned()
        .collect();
    (changed, gone)
}

/// The `guid:` and the `labels:` of a `.meta`; the rest is parsed by the importer that needs it.
pub fn read_meta_header(meta: &Path) -> (Option<Guid>, Vec<String>) {
    let Ok(text) = std::fs::read_to_string(meta) else {
        return (None, Vec::new());
    };
    header(text.as_str())
}

/// Reads only the `guid:` line; the rest of a `.meta` is parsed by the importer that needs it.
pub fn read_meta_guid(meta: &Path) -> Option<Guid> {
    let text = std::fs::read_to_string(meta).ok()?;
    text.lines()
        .find_map(|line| line.strip_prefix("guid:"))
        .and_then(|guid| Guid::parse(guid.trim()))
}

/// The database metadata reader, shared with field coverage. The file scanner retains its
/// lightweight envelope reader; diagnostics supply tracked nodes to the same field accesses.
pub fn meta_header(value: &unbound_yaml::Value) -> (Option<Guid>, Vec<String>) {
    header(value)
}

trait Header {
    fn scalar(&self, key: &str) -> Option<&str>;
    fn strings(&self, key: &str) -> Vec<String>;
}

fn header(source: &(impl Header + ?Sized)) -> (Option<Guid>, Vec<String>) {
    (
        source.scalar("guid").and_then(Guid::parse),
        source.strings("labels"),
    )
}

impl Header for str {
    fn scalar(&self, key: &str) -> Option<&str> {
        self.lines()
            .find_map(|line| line.strip_prefix(key)?.strip_prefix(':'))
            .map(str::trim)
    }
    fn strings(&self, key: &str) -> Vec<String> {
        self.lines()
            .skip_while(|line| line.strip_suffix(':') != Some(key))
            .skip(1)
            .map_while(|line| line.strip_prefix("- "))
            .map(|label| label.trim().trim_matches('\'').to_string())
            .collect()
    }
}

impl Header for unbound_yaml::Value {
    fn scalar(&self, key: &str) -> Option<&str> {
        self.get(key)?.as_str()
    }
    fn strings(&self, key: &str) -> Vec<String> {
        self.get(key)
            .and_then(unbound_yaml::Value::as_seq)
            .unwrap_or_default()
            .iter()
            .filter_map(unbound_yaml::Value::as_str)
            .map(String::from)
            .collect()
    }
}

#[cfg(test)]
mod header_tests {
    use super::*;

    #[test]
    fn envelope_scan_and_tracked_metadata_share_the_same_field_reader() {
        let text = "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\nModelImporter:\n  ignored: 0\nlabels:\n- first\n- 'second label'\n";
        let mut value = unbound_yaml::parse_value(text).unwrap();
        let reads = unbound_yaml::reads::FieldReads::default();
        reads.track(&mut value);
        assert_eq!(header(text), meta_header(&value));
        assert_eq!(header(text).1, ["first", "second label"]);
        assert_eq!(reads.paths(), ["guid".into(), "labels".into()].into());
        // An importer can reject its body without hiding the asset's envelope identity.
        let malformed = "guid: 11111111111111111111111111111111\nModelImporter:\n  invalid: [\n";
        assert_eq!(header(malformed).0, header(text).0);
    }
}

#[cfg(test)]
mod db_tests {
    use super::*;

    #[test]
    fn changes_list_new_edited_and_gone_assets_only() {
        let dir = std::env::temp_dir().join(format!("unbound-db-changes-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(dir.join("Plug.bundle/Contents")).unwrap();
        let meta = |guid: u128, labels: &str| {
            format!("fileFormatVersion: 2\nguid: {guid:032x}\n{labels}TextScriptImporter:\n")
        };
        for (name, guid) in [("Kept.txt", 1u128), ("Edited.txt", 2), ("Gone.txt", 3)] {
            std::fs::write(dir.join(name), "x").unwrap();
            std::fs::write(dir.join(format!("{name}.meta")), meta(guid, "")).unwrap();
        }
        std::fs::write(dir.join("Plug.bundle/Contents/Inner.txt"), "x").unwrap();
        std::fs::write(dir.join("Plug.bundle/Contents/Inner.txt.meta"), meta(9, "")).unwrap();
        let roots = [Root {
            dir: &dir,
            virtual_path: "Assets".into(),
        }];
        let db = AssetDb::scan(&roots);
        let plugin = |p: &Path| p.extension().is_some_and(|e| e == "bundle");
        // Nothing changed; what is inside the plug-in is not looked at.
        let (changed, gone) = changes(&db, &roots, plugin);
        assert!(changed.is_empty() && gone.is_empty());
        // Outside the AssetDatabase: labels edited, a file gone, a copy with the original's .meta.
        std::fs::write(dir.join("Edited.txt.meta"), meta(2, "labels:\n- outside\n")).unwrap();
        std::fs::remove_file(dir.join("Gone.txt")).unwrap();
        std::fs::copy(dir.join("Kept.txt"), dir.join("Copy.txt")).unwrap();
        std::fs::copy(dir.join("Kept.txt.meta"), dir.join("Copy.txt.meta")).unwrap();
        let (mut changed, gone) = changes(&db, &roots, plugin);
        changed.sort();
        assert_eq!(changed, ["Assets/Copy.txt", "Assets/Edited.txt"]);
        assert_eq!(
            gone.iter().map(|a| a.path.as_str()).collect::<Vec<_>>(),
            ["Assets/Gone.txt"]
        );
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn the_assets_folder_is_listed_under_its_fixed_guid() {
        let dir = std::env::temp_dir().join(format!("unbound-assetdb-root-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(dir.join("Assets/Sub")).unwrap();
        std::fs::write(
            dir.join("Assets/Sub.meta"),
            "fileFormatVersion: 2\nguid: 0000000000000000000000000000abcd\nfolderAsset: yes\n",
        )
        .unwrap();
        let assets = dir.join("Assets");
        let package = dir.join("Package");
        std::fs::create_dir_all(&package).unwrap();
        let db = AssetDb::scan(&[
            Root {
                dir: &assets,
                virtual_path: "Assets".into(),
            },
            Root {
                dir: &package,
                virtual_path: "Packages/com.probe".into(),
            },
        ]);
        let root = db.by_guid(ASSETS_ROOT_GUID).unwrap();
        assert!(root.is_assets_root() && root.is_folder && root.file == assets);
        assert_eq!(root.path, "Assets");
        // Unity paths compare case-insensitively.
        assert_eq!(db.by_path("assets").unwrap().guid, ASSETS_ROOT_GUID);
        assert_eq!(db.by_path("Assets/Sub").unwrap().guid, Guid(0xabcd));
        // A package's root is no asset of its own.
        assert!(db.by_path("Packages/com.probe").is_none());
        assert_eq!(db.len(), 2);
        // Nothing changed on disk: the root, which has no .meta, is neither new nor gone.
        let roots = [Root {
            dir: &assets,
            virtual_path: "Assets".into(),
        }];
        let (changed, gone) = changes(&db, &roots, |_| false);
        assert!(changed.is_empty() && gone.is_empty());
        std::fs::remove_dir_all(&dir).unwrap();
    }

    fn asset(guid: u128, path: &str) -> Asset {
        Asset {
            guid: Guid(guid),
            path: path.into(),
            file: PathBuf::from(path),
            is_folder: false,
            labels: Vec::new(),
        }
    }

    #[test]
    fn inserting_and_removing_keeps_both_indexes() {
        let mut db = AssetDb::default();
        for (guid, path) in [
            (1, "Assets/A.asset"),
            (2, "Assets/B.asset"),
            (3, "Assets/C.asset"),
        ] {
            db.insert(asset(guid, path));
        }
        // Removing the first moves the last into its place; both lookups follow.
        db.remove_path("assets/a.ASSET");
        assert!(db.by_guid(Guid(1)).is_none() && db.by_path("Assets/A.asset").is_none());
        assert_eq!(db.by_guid(Guid(3)).unwrap().path, "Assets/C.asset");
        assert_eq!(db.by_path("Assets/C.asset").unwrap().guid, Guid(3));
        // A move: the GUID's old path goes, the new one answers.
        db.insert(asset(2, "Assets/Moved.asset"));
        assert!(db.by_path("Assets/B.asset").is_none());
        assert_eq!(db.by_path("Assets/Moved.asset").unwrap().guid, Guid(2));
        // A new asset at a listed path replaces it.
        db.insert(asset(4, "Assets/C.asset"));
        assert!(db.by_guid(Guid(3)).is_none());
        assert_eq!(db.len(), 2);
        db.remove_path("Assets/Nothing.asset");
        assert_eq!(db.len(), 2);
    }
}
