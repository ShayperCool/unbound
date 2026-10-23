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

#[derive(Debug, Default)]
pub struct AssetDb {
    assets: Vec<Asset>,
    by_guid: HashMap<Guid, usize>,
    by_path: HashMap<String, usize>,
    /// Assets whose `.meta` has no readable GUID, or a GUID another asset has.
    pub problems: Vec<String>,
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
}

/// The `guid:` and the `labels:` of a `.meta`; the rest is parsed by the importer that needs it.
pub fn read_meta_header(meta: &Path) -> (Option<Guid>, Vec<String>) {
    let Ok(text) = std::fs::read_to_string(meta) else {
        return (None, Vec::new());
    };
    let guid = text
        .lines()
        .find_map(|line| line.strip_prefix("guid:"))
        .and_then(|guid| Guid::parse(guid.trim()));
    let mut labels = Vec::new();
    let mut lines = text.lines().skip_while(|line| *line != "labels:").skip(1);
    for line in lines.by_ref() {
        match line.strip_prefix("- ") {
            Some(label) => labels.push(label.trim().trim_matches('\'').to_string()),
            None => break,
        }
    }
    (guid, labels)
}

/// Reads only the `guid:` line; the rest of a `.meta` is parsed by the importer that needs it.
pub fn read_meta_guid(meta: &Path) -> Option<Guid> {
    let text = std::fs::read_to_string(meta).ok()?;
    text.lines()
        .find_map(|line| line.strip_prefix("guid:"))
        .and_then(|guid| Guid::parse(guid.trim()))
}
