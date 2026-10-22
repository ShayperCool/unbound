//! Precompiled plugin DLLs and their `PluginImporter` settings.
use std::path::{Path, PathBuf};
use unbound_yaml::Value;

#[derive(Debug, Clone)]
pub struct Plugin {
    pub file: PathBuf,
    /// `Assets/...` or `Packages/<name>/...`
    pub path: String,
    /// Referenced only by assemblies that list it in `precompiledReferences`.
    pub explicitly_referenced: bool,
    pub define_constraints: Vec<String>,
    /// Labelled `RoslynAnalyzer`: a Roslyn analyzer or source generator, not a reference.
    pub analyzer: bool,
    importer: Value,
}

impl Plugin {
    pub fn file_name(&self) -> &str {
        self.file
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or_default()
    }

    /// Whether the plugin is enabled for a platform (`Editor`, `Android`, `Linux64`, ...):
    /// with "Any Platform" on, unless that platform is excluded; otherwise when it is enabled.
    pub fn compatible_with(&self, platform: &str) -> bool {
        let any = self.platform_enabled("Any").unwrap_or(false);
        if any {
            !self
                .platform_enabled(&format!("Exclude {platform}"))
                .unwrap_or(false)
        } else {
            self.platform_enabled(platform).unwrap_or(false)
        }
    }

    fn platform_enabled(&self, key: &str) -> Option<bool> {
        let data = self.importer.get("platformData")?;
        match data {
            // `platformData: {Any: {enabled: 1}, Editor: {...}}`
            Value::Map(map) => map.get(key)?.get("enabled")?.as_bool(),
            // `- first: {Any: }  second: {enabled: 1}` and `- first: {Editor: Editor} ...`
            Value::Seq(items) => items.iter().find_map(|item| {
                let first = item.get("first")?.as_map()?;
                let matched = first.iter().any(|(group, name)| {
                    group == key
                        || name.as_str() == Some(key)
                        || format!("{group} {}", name.as_str().unwrap_or("")) == key
                });
                if matched {
                    item.get("second")?.get("enabled")?.as_bool()
                } else {
                    None
                }
            }),
            _ => None,
        }
    }
}

/// Reads a DLL's `.meta`; `None` when it is not a managed assembly.
pub fn read_plugin(file: &Path, path: String) -> Option<Plugin> {
    if !is_managed_assembly(file) {
        return None;
    }
    let meta = std::fs::read_to_string(format!("{}.meta", file.display())).ok()?;
    let meta = unbound_yaml::parse_value(&meta).ok()?;
    let labels: Vec<&str> = meta
        .get("labels")
        .and_then(Value::as_seq)
        .map(|l| l.iter().filter_map(Value::as_str).collect())
        .unwrap_or_default();
    let importer = meta.get("PluginImporter").cloned().unwrap_or_default();
    let explicitly_referenced = importer
        .get("isExplicitlyReferenced")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let define_constraints = importer
        .get("defineConstraints")
        .and_then(Value::as_seq)
        .map(|l| {
            l.iter()
                .filter_map(Value::as_str)
                .map(String::from)
                .collect()
        })
        .unwrap_or_default();
    Some(Plugin {
        file: file.to_path_buf(),
        path,
        explicitly_referenced,
        define_constraints,
        analyzer: labels.contains(&"RoslynAnalyzer"),
        importer,
    })
}

/// A PE file with a CLI header (data directory 14).
pub fn is_managed_assembly(file: &Path) -> bool {
    use std::io::{Read, Seek, SeekFrom};
    let Ok(mut f) = std::fs::File::open(file) else {
        return false;
    };
    let mut dos = [0u8; 64];
    if f.read_exact(&mut dos).is_err() || &dos[..2] != b"MZ" {
        return false;
    }
    let pe = u32::from_le_bytes(dos[60..64].try_into().unwrap()) as u64;
    let mut header = [0u8; 24 + 2];
    if f.seek(SeekFrom::Start(pe)).is_err()
        || f.read_exact(&mut header).is_err()
        || &header[..4] != b"PE\0\0"
    {
        return false;
    }
    let magic = u16::from_le_bytes([header[24], header[25]]);
    // The data directories start at 96 (PE32) or 112 (PE32+) bytes into the optional header.
    let directories = match magic {
        0x10b => 96,
        0x20b => 112,
        _ => return false,
    };
    let clr = pe + 24 + directories + 14 * 8;
    let mut entry = [0u8; 8];
    if f.seek(SeekFrom::Start(clr)).is_err() || f.read_exact(&mut entry).is_err() {
        return false;
    }
    u32::from_le_bytes(entry[..4].try_into().unwrap()) != 0
}
