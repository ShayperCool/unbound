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

/// The file extensions of the native libraries the Editor of this OS loads: `.so` on Linux,
/// `.bundle` and `.dylib` on macOS, `.dll` on Windows (Unity tells a plugin's OS by them).
pub const NATIVE_EXTENSIONS: &[&str] = if cfg!(target_os = "macos") {
    &["bundle", "dylib"]
} else if cfg!(windows) {
    &["dll"]
} else {
    &["so"]
};

/// The Editor's name of this OS in a plugin's `OS` setting.
const EDITOR_OS: &str = if cfg!(target_os = "macos") {
    "OSX"
} else if cfg!(windows) {
    "Windows"
} else {
    "Linux"
};

/// A native library the Editor loads on this machine: enabled for the Editor, for this OS and CPU
/// (`PluginImporter` Editor settings `OS: Linux|OSX|AnyOS`, `CPU: x86_64|ARM64|AnyCPU`).
pub fn native_editor_plugin(file: &Path) -> bool {
    let Ok(meta) = std::fs::read_to_string(format!("{}.meta", file.display())) else {
        return false;
    };
    let Ok(meta) = unbound_yaml::parse_value(&meta) else {
        return false;
    };
    let importer = meta.get("PluginImporter").cloned().unwrap_or_default();
    let plugin = Plugin {
        file: file.to_path_buf(),
        path: String::new(),
        explicitly_referenced: false,
        define_constraints: Vec::new(),
        analyzer: false,
        importer,
    };
    if !plugin.compatible_with("Editor") {
        return false;
    }
    let os = plugin.editor_setting("OS").unwrap_or_default();
    let cpu = plugin.editor_setting("CPU").unwrap_or_default();
    let os_ok = os.is_empty() || os == "AnyOS" || os == EDITOR_OS;
    let cpu_ok = match std::env::consts::ARCH {
        "x86_64" => matches!(cpu.as_str(), "" | "AnyCPU" | "x86_64"),
        "aarch64" => matches!(cpu.as_str(), "" | "AnyCPU" | "ARM64"),
        _ => cpu.is_empty() || cpu == "AnyCPU",
    };
    os_ok && cpu_ok
}

/// The loadable file of a native plugin: the file itself, or a macOS bundle directory's
/// executable (`Contents/MacOS/<CFBundleExecutable>`, else the bundle's name).
pub fn native_library_file(plugin: &Path) -> Option<PathBuf> {
    if plugin.is_file() {
        return Some(plugin.to_path_buf());
    }
    let contents = plugin.join("Contents");
    let executable = std::fs::read_to_string(contents.join("Info.plist"))
        .ok()
        .and_then(|plist| {
            let (_, rest) = plist.split_once("<key>CFBundleExecutable</key>")?;
            let (_, rest) = rest.split_once("<string>")?;
            let (name, _) = rest.split_once("</string>")?;
            Some(name.trim().to_string())
        })
        .or_else(|| Some(plugin.file_stem()?.to_str()?.to_string()))?;
    let file = contents.join("MacOS").join(executable);
    file.is_file().then_some(file)
}

impl Plugin {
    /// A setting of the Editor platform (`OS`, `CPU`).
    fn editor_setting(&self, key: &str) -> Option<String> {
        let data = self.importer.get("platformData")?;
        let settings = match data {
            Value::Map(map) => map.get("Editor")?.get("settings")?,
            Value::Seq(items) => items.iter().find_map(|item| {
                let first = item.get("first")?.as_map()?;
                first
                    .iter()
                    .any(|(group, _)| group == "Editor")
                    .then(|| item.get("second")?.get("settings"))
                    .flatten()
            })?,
            _ => return None,
        };
        settings.get(key)?.as_str().map(String::from)
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
    let importer = managed_importer(&meta);
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

/// A managed DLL's importer settings. Unity imports every `.dll` with the PluginImporter whatever
/// importer its `.meta` names; one without `PluginImporter` settings (Google's Version Handler
/// writes `DefaultImporter`) gets the defaults: compatible with any platform.
fn managed_importer(meta: &Value) -> Value {
    meta.get("PluginImporter").cloned().unwrap_or_else(|| {
        unbound_yaml::parse_value("platformData:\n  Any:\n    enabled: 1\n").unwrap_or_default()
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

#[cfg(test)]
mod tests {
    use super::*;

    fn plugin(meta: &str) -> Plugin {
        let meta = unbound_yaml::parse_value(meta).unwrap();
        Plugin {
            file: PathBuf::from("A.dll"),
            path: String::new(),
            explicitly_referenced: false,
            define_constraints: Vec::new(),
            analyzer: false,
            importer: managed_importer(&meta),
        }
    }

    #[test]
    fn a_dll_without_plugin_importer_settings_is_for_any_platform() {
        let p = plugin("fileFormatVersion: 2\nguid: f84de9af142947eeb3522e42d9487838\nDefaultImporter:\n  userData:\n");
        assert!(p.compatible_with("Editor"));
        assert!(p.compatible_with("Android"));
    }

    #[test]
    fn plugin_importer_settings_are_kept() {
        let p = plugin(
            "PluginImporter:\n  platformData:\n    Any:\n      enabled: 0\n    Editor:\n      enabled: 1\n",
        );
        assert!(p.compatible_with("Editor"));
        assert!(!p.compatible_with("Android"));
    }

    #[test]
    fn a_bundle_directory_loads_by_its_executable() {
        let dir = std::env::temp_dir().join(format!("unbound-bundle-{}", std::process::id()));
        let bundle = dir.join("Wrapper.bundle");
        std::fs::create_dir_all(bundle.join("Contents/MacOS")).unwrap();
        std::fs::write(
            bundle.join("Contents/Info.plist"),
            "<dict><key>CFBundleExecutable</key>\n<string>WrapperLib</string></dict>",
        )
        .unwrap();
        std::fs::write(bundle.join("Contents/MacOS/WrapperLib"), b"").unwrap();
        assert_eq!(native_library_file(&bundle), Some(bundle.join("Contents/MacOS/WrapperLib")));
        let file = dir.join("Plain.bundle");
        std::fs::write(&file, b"").unwrap();
        assert_eq!(native_library_file(&file), Some(file.clone()));
        std::fs::remove_dir_all(&dir).unwrap();
    }
}
